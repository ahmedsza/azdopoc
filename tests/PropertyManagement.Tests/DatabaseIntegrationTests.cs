using System.Data;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropertyManagement.Web.Data;
using PropertyManagement.Web.Models;
using PropertyManagement.Web.ViewModels;

[assembly: DoNotParallelize]

namespace PropertyManagement.Tests;

[TestClass]
public sealed class DatabaseIntegrationTests
{
    private static string connectionString = "";
    private static string root = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropertyManagement.slnx")))
            directory = directory.Parent;
        root = directory?.FullName ?? throw new InvalidOperationException("Run tests from the project workspace.");
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
        {
            InitialCatalog = "PropertyManagement_Tests_" + Guid.NewGuid().ToString("N")
        };
        connectionString = builder.ConnectionString;
        await PublishAsync();
    }

    private static async Task PublishAsync(string? sourceFile = null, bool expectFailure = false)
    {
        var dacpac = sourceFile ?? Environment.GetEnvironmentVariable("DACPAC_PATH")
            ?? Path.Combine(root, "database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac");
        var result = await RunProcessAsync("dotnet",
        [
            "tool", "run", "sqlpackage", "--", "/Action:Publish", "/SourceFile:" + dacpac,
            "/Profile:" + Path.Combine(root, "database/PropertyManagement.Database/PublishProfiles/Safe.publish.xml"),
            "/TargetConnectionString:" + connectionString
        ]);
        if (expectFailure) Assert.AreNotEqual(0, result.ExitCode, "An unsafe schema change unexpectedly succeeded.");
        else Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await output) + (await error));
    }

    private static PropertyDbContext CreateContext() => new(new DbContextOptionsBuilder<PropertyDbContext>()
        .UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(150)).Options);

    [TestCleanup]
    public async Task ResetDataAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "DELETE FROM dbo.RentPayments; DELETE FROM dbo.Leases; DELETE FROM dbo.Tenants; DELETE FROM dbo.Properties;", connection);
        await command.ExecuteNonQueryAsync();
    }

    [ClassCleanup]
    public static async Task CleanupAsync()
    {
        if (string.IsNullOrEmpty(connectionString)) return;
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (!database.StartsWith("PropertyManagement_Tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to delete a non-test database.");
        SqlConnection.ClearAllPools();
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", connection);
        await command.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task MvcFormsSaveWithLocaleAndRequireAntiforgeryAsync()
    {
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:PropertyManagement", connectionString).UseEnvironment("Testing"));
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await ReadFormAsync(client, "/Properties/Create");
        SetFields(fields, new() { ["Name"] = "Web house", ["Address"] = "27 Example Road", ["City"] = "Johannesburg", ["Bedrooms"] = "2", ["MonthlyRent"] = "8700.50" });
        using var propertyResponse = await client.PostAsync("/Properties/Edit", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, propertyResponse.StatusCode, await propertyResponse.Content.ReadAsStringAsync());
        await using var database = CreateContext();
        var property = await database.Properties.SingleAsync();
        Assert.AreEqual(8700.50m, property.MonthlyRent);

        fields = await ReadFormAsync(client, "/Tenants/Create");
        SetFields(fields, new() { ["FullName"] = "Web Tenant", ["Email"] = "web@example.com", ["Phone"] = "0100000000" });
        using var tenantResponse = await client.PostAsync("/Tenants/Edit", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, tenantResponse.StatusCode, await tenantResponse.Content.ReadAsStringAsync());
        var tenant = await database.Tenants.SingleAsync();

        fields = await ReadFormAsync(client, "/Leases/Create");
        SetFields(fields, new() { ["PropertyId"] = property.Id.ToString(CultureInfo.InvariantCulture), ["TenantId"] = tenant.Id.ToString(CultureInfo.InvariantCulture),
            ["StartDate"] = CurrentMonth.ToString("yyyy-MM-dd"), ["EndDate"] = CurrentMonth.AddYears(1).ToString("yyyy-MM-dd"), ["MonthlyRent"] = "8700.50" });
        using var leaseResponse = await client.PostAsync("/Leases/Edit", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, leaseResponse.StatusCode, await leaseResponse.Content.ReadAsStringAsync());
        var lease = await database.Leases.SingleAsync();

        fields = await ReadFormAsync(client, $"/Rent/Record?leaseId={lease.Id}");
        SetFields(fields, new() { ["LeaseId"] = lease.Id.ToString(CultureInfo.InvariantCulture), ["RentalMonth"] = CurrentMonth.ToString("yyyy-MM"),
            ["PaidOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Amount"] = "3200.25", ["Reference"] = "WEB-001" });
        using var paymentResponse = await client.PostAsync("/Rent/Record", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, paymentResponse.StatusCode, await paymentResponse.Content.ReadAsStringAsync());
        using var duplicateResponse = await client.PostAsync("/Rent/Record", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, duplicateResponse.StatusCode);
        var payment = await database.RentPayments.SingleAsync();
        Assert.AreEqual(3200.25m, payment.Amount);
        Assert.AreEqual(CurrentMonth, payment.RentalMonth);
        var report = await new ReportingQueries(new(connectionString)).GetDashboardAsync(CurrentMonth);
        Assert.AreEqual(5500.25m, report.OutstandingRent);

        using var rejected = await client.PostAsync("/Properties/Edit", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private static async Task<List<KeyValuePair<string, string>>> ReadFormAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        return document.QuerySelectorAll("form[method=post] input[name]")
            .Select(input => new KeyValuePair<string, string>(input.GetAttribute("name")!, input.GetAttribute("value") ?? "")).ToList();
    }

    private static void SetFields(List<KeyValuePair<string, string>> fields, Dictionary<string, string> values)
    {
        fields.RemoveAll(field => values.ContainsKey(field.Key));
        fields.AddRange(values);
    }

    [TestMethod]
    public void MoneyValidationSupportsLocalizedCurrency()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-ZA");
            object[] inputs =
            [
                new PropertyInput { Name = "Test", Address = "Test", City = "Test", MonthlyRent = 8700.50m },
                new LeaseInput { PropertyId = 1, TenantId = 1, MonthlyRent = 8700.50m },
                new PaymentInput { LeaseId = 1, Amount = 8700.50m, Reference = "TEST" }
            ];
            foreach (var input in inputs) Validator.ValidateObject(input, new ValidationContext(input), true);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [TestMethod]
    public async Task EfWriteCanBeReadByParameterizedSqlAsync()
    {
        await using var context = CreateContext();
        var property = new Property { Name = "Harbour ' House", Address = "10 Example Road", City = "Johannesburg", Bedrooms = 2, MonthlyRent = 9000m };
        context.Properties.Add(property);
        await context.SaveChangesAsync();
        Assert.HasCount(8, property.Version);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT [Name] FROM dbo.Properties WHERE [Id] = @Id;", connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = property.Id;
        Assert.AreEqual(property.Name, await command.ExecuteScalarAsync());
    }

    private static DateOnly CurrentMonth => new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private static async Task<Lease> AddLeaseAsync(decimal rent = 9000m)
    {
        await using var context = CreateContext();
        var lease = new Lease
        {
            Property = new Property { Name = "Test house", Address = "10 Example Road", City = "Johannesburg", Bedrooms = 2, MonthlyRent = rent },
            Tenant = new Tenant { FullName = "Test Tenant", Email = "test@example.com", Phone = "0100000000" },
            StartDate = CurrentMonth.AddMonths(-1), EndDate = CurrentMonth.AddYears(1), MonthlyRent = rent
        };
        context.Leases.Add(lease);
        await context.SaveChangesAsync();
        return lease;
    }

    [TestMethod]
    public async Task PaymentProcedureIsIdempotentAndUpdatesBothReportsAsync()
    {
        var lease = await AddLeaseAsync();
        var payments = new RentPaymentService(new(connectionString));
        var request = new PaymentRequest(lease.Id, CurrentMonth, DateOnly.FromDateTime(DateTime.Today), 3000m, "TEST-001", Guid.NewGuid());
        var paymentId = await payments.RecordAsync(request);
        Assert.AreEqual(paymentId, await payments.RecordAsync(request));
        var ledger = await payments.GetLedgerAsync(CurrentMonth);
        Assert.HasCount(1, ledger);
        Assert.AreEqual(3000m, ledger[0].Paid);
        Assert.AreEqual(6000m, ledger[0].Outstanding);
        var dashboard = await new ReportingQueries(new(connectionString)).GetDashboardAsync(CurrentMonth);
        Assert.AreEqual(9000m, dashboard.ExpectedRent);
        Assert.AreEqual(3000m, dashboard.CollectedRent);
        Assert.AreEqual(6000m, dashboard.OutstandingRent);
        Assert.AreEqual(1, dashboard.OccupiedCount);
        Assert.HasCount(1, dashboard.RecentPayments);
    }

    [TestMethod]
    public async Task ProcedureRejectsChangedDuplicateAndInvalidPaymentAsync()
    {
        var lease = await AddLeaseAsync();
        var payments = new RentPaymentService(new(connectionString));
        var request = new PaymentRequest(lease.Id, CurrentMonth, DateOnly.FromDateTime(DateTime.Today), 3000m, "TEST-002", Guid.NewGuid());
        await payments.RecordAsync(request);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => payments.RecordAsync(request with { Amount = 4000m }));
        Assert.AreEqual(50005, duplicate.Number);
        var amount = await Assert.ThrowsAsync<SqlException>(() => payments.RecordAsync(request with { Amount = -1m, SubmissionKey = Guid.NewGuid() }));
        Assert.AreEqual(50001, amount.Number);
        var month = await Assert.ThrowsAsync<SqlException>(() => payments.RecordAsync(request with { RentalMonth = CurrentMonth.AddYears(-3), SubmissionKey = Guid.NewGuid() }));
        Assert.AreEqual(50004, month.Number);
    }

    [TestMethod]
    public async Task UnpaidLeaseAndOverpaymentRemainSeparateAsync()
    {
        var paidLease = await AddLeaseAsync(5000m);
        await AddLeaseAsync(7000m);
        var payments = new RentPaymentService(new(connectionString));
        await payments.RecordAsync(new(paidLease.Id, CurrentMonth, DateOnly.FromDateTime(DateTime.Today), 15000m, "CREDIT", Guid.NewGuid()));
        var ledger = await payments.GetLedgerAsync(CurrentMonth);
        Assert.HasCount(2, ledger);
        Assert.AreEqual(7000m, ledger.Sum(row => row.Outstanding));
        var dashboard = await new ReportingQueries(new(connectionString)).GetDashboardAsync(CurrentMonth);
        Assert.AreEqual(7000m, dashboard.OutstandingRent);
    }

    [TestMethod]
    public async Task ParameterizedSearchTreatsSqlAsTextAsync()
    {
        await AddLeaseAsync();
        var dashboard = await new ReportingQueries(new(connectionString)).GetDashboardAsync(CurrentMonth, "'; DROP TABLE dbo.Properties;--");
        Assert.HasCount(0, dashboard.Properties);
        await using var context = CreateContext();
        Assert.AreEqual(1, await context.Properties.CountAsync());
    }

    [TestMethod]
    public async Task LeaseServiceRejectsOverlapAndPaidLeaseChangesAsync()
    {
        var lease = await AddLeaseAsync();
        var input = new LeaseInput { PropertyId = lease.PropertyId, TenantId = lease.TenantId, StartDate = CurrentMonth, EndDate = CurrentMonth.AddMonths(6), MonthlyRent = 9000m };
        await using (var context = CreateContext())
            await Assert.ThrowsAsync<ValidationException>(() => new LeaseService(context).SaveAsync(input));
        await new RentPaymentService(new(connectionString)).RecordAsync(new(lease.Id, CurrentMonth, DateOnly.FromDateTime(DateTime.Today), 100m, "LOCK", Guid.NewGuid()));
        input.Id = lease.Id;
        input.Version = Convert.ToBase64String(lease.Version);
        input.MonthlyRent = 9500m;
        await using var editContext = CreateContext();
        await Assert.ThrowsAsync<ValidationException>(() => new LeaseService(editContext).SaveAsync(input));
    }

    [TestMethod]
    public async Task ForeignKeysAndMoneyConstraintsProtectStoredDataAsync()
    {
        var lease = await AddLeaseAsync();
        await using (var context = CreateContext())
        {
            var property = await context.Properties.SingleAsync(property => property.Id == lease.PropertyId);
            context.Properties.Remove(property);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using var invalidContext = CreateContext();
        invalidContext.Properties.Add(new Property { Name = "Invalid", Address = "Test", City = "Test", MonthlyRent = -1m });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalidContext.SaveChangesAsync());
    }

    [TestMethod]
    public async Task RepublishPreservesExistingDataAsync()
    {
        var lease = await AddLeaseAsync();
        await PublishAsync();
        await using var context = CreateContext();
        Assert.AreEqual(lease.MonthlyRent, (await context.Leases.SingleAsync(existing => existing.Id == lease.Id)).MonthlyRent);
        var output = Path.Combine(root, "artifacts", "republish-" + Guid.NewGuid().ToString("N") + ".xml");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var dacpac = Environment.GetEnvironmentVariable("DACPAC_PATH")
            ?? Path.Combine(root, "database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac");
        try
        {
            var result = await RunProcessAsync("dotnet", ["tool", "run", "sqlpackage", "--", "/Action:DeployReport", "/SourceFile:" + dacpac,
                "/Profile:" + Path.Combine(root, "database/PropertyManagement.Database/PublishProfiles/Safe.publish.xml"),
                "/TargetConnectionString:" + connectionString, "/OutputPath:" + output]);
            Assert.AreEqual(0, result.ExitCode, result.Output);
            var report = XDocument.Load(output);
            Assert.IsFalse(report.Descendants().Any(element => element.Name.LocalName == "Operation"), report.ToString());
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [TestMethod]
    public async Task ConcurrentLeaseSubmissionsCannotOverlapAsync()
    {
        var existing = await AddLeaseAsync();
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:PropertyManagement", connectionString).UseEnvironment("Testing"));
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await ReadFormAsync(client, "/Leases/Create");
        SetFields(fields, new() { ["PropertyId"] = existing.PropertyId.ToString(CultureInfo.InvariantCulture),
            ["TenantId"] = existing.TenantId.ToString(CultureInfo.InvariantCulture), ["StartDate"] = existing.EndDate.AddDays(1).ToString("yyyy-MM-dd"),
            ["EndDate"] = existing.EndDate.AddYears(1).ToString("yyyy-MM-dd"), ["MonthlyRent"] = "9500.00" });
        using var firstContent = new FormUrlEncodedContent(fields);
        using var secondContent = new FormUrlEncodedContent(fields);
        var results = await Task.WhenAll(client.PostAsync("/Leases/Edit", firstContent), client.PostAsync("/Leases/Edit", secondContent));
        Assert.AreEqual(1, results.Count(response => response.StatusCode == HttpStatusCode.Redirect));
        var rejected = results.Single(response => response.StatusCode != HttpStatusCode.Redirect);
        Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode);
        var validation = await rejected.Content.ReadAsStringAsync();
        Assert.IsTrue(validation.Contains("already has a lease", StringComparison.Ordinal) || validation.Contains("Another request changed", StringComparison.Ordinal));
        foreach (var response in results) response.Dispose();
        await using var context = CreateContext();
        Assert.AreEqual(2, await context.Leases.CountAsync());
    }

    [TestMethod]
    public async Task AdditiveDeploymentPreservesDataAndUnsafeChangeIsBlockedAsync()
    {
        var directory = Path.Combine(root, "artifacts", "schema-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "PropertyManagement.Database.sqlproj");
        File.Copy(Path.Combine(root, "database/PropertyManagement.Database/PropertyManagement.Database.sqlproj"), project);
        try
        {
            async Task<string> BuildProbeAsync(int labelLength, bool addNotes)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "DeploymentProbe.sql"), $$"""
                    CREATE TABLE [dbo].[DeploymentProbe]
                    (
                        [Id] INT NOT NULL,
                        [Label] NVARCHAR({{labelLength}}) NOT NULL,
                        {{(addNotes ? "[Notes] NVARCHAR(100) NULL," : "")}}
                        CONSTRAINT [PK_DeploymentProbe] PRIMARY KEY CLUSTERED ([Id])
                    );
                    """);
                var result = await RunProcessAsync("dotnet", ["build", project, "-c", "Release", "-warnaserror"]);
                Assert.AreEqual(0, result.ExitCode, result.Output);
                return Path.Combine(directory, "bin/Release/PropertyManagement.Database.dacpac");
            }
            await PublishAsync(await BuildProbeAsync(120, false));
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var insert = new SqlCommand("INSERT dbo.DeploymentProbe (Id, Label) VALUES (1, N'Existing data must survive');", connection))
                await insert.ExecuteNonQueryAsync();
            await PublishAsync(await BuildProbeAsync(120, true));
            await using (var read = new SqlCommand("SELECT Label FROM dbo.DeploymentProbe WHERE Id = 1 AND Notes IS NULL;", connection))
                Assert.AreEqual("Existing data must survive", await read.ExecuteScalarAsync());
            await PublishAsync(await BuildProbeAsync(4, true), expectFailure: true);
            await using var preserved = new SqlCommand("SELECT Label FROM dbo.DeploymentProbe WHERE Id = 1;", connection);
            Assert.AreEqual("Existing data must survive", await preserved.ExecuteScalarAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ApprovedDeploymentRejectsTargetSchemaDriftAsync()
    {
        var output = Path.Combine(root, "artifacts", "approval-probe-" + Guid.NewGuid().ToString("N"));
        var dacpac = Environment.GetEnvironmentVariable("DACPAC_PATH")
            ?? Path.Combine(root, "database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac");
        string[] arguments = ["-NoProfile", "-File", Path.Combine(root, "scripts/Deploy-Database.ps1"), "-Dacpac", dacpac,
            "-ConnectionString", connectionString, "-OutputDirectory", output];
        try
        {
            var preview = await RunProcessAsync("pwsh", arguments.Concat(["-Action", "Preview"]));
            Assert.AreEqual(0, preview.ExitCode, preview.Output);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var drift = new SqlCommand("ALTER TABLE dbo.Properties ALTER COLUMN City NVARCHAR(80) NOT NULL;", connection))
                await drift.ExecuteNonQueryAsync();
            var publish = await RunProcessAsync("pwsh", arguments.Concat(["-Action", "Publish", "-ApprovedPlan", Path.Combine(output, "plan.json")]));
            Assert.AreNotEqual(0, publish.ExitCode);
            StringAssert.Contains(publish.Output, "Approved deployment changed");
            await using var check = new SqlCommand("SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Properties') AND name = N'City';", connection);
            Assert.AreEqual((short)160, await check.ExecuteScalarAsync());
        }
        finally
        {
            await PublishAsync();
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}