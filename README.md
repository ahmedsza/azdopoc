# Yard: Property Management

A local ASP.NET Core 10 MVC website backed directly by SQL Server. Manage properties, tenants, leases, and rent payments, with a monthly rent ledger and occupancy dashboard.

The SQL Database Project is the **only schema owner**. Azure Pipelines builds and validates the database, and optionally deploys the same DACPAC to a configured SQL Server. There are no GitHub Actions workflows, EF migrations, startup database creation, or automatic sample-data deployment.

## Run Locally

Prerequisites:

- .NET 10 SDK and PowerShell 7.2 or later.
- SQL Server 2019 LocalDB with the `MSSQLLocalDB` instance, or an explicitly configured SQL Server 2019+ target.
- `sqlcmd` for the optional demo-data script.
- Recommended VS Code extensions are listed in [.vscode/extensions.json](.vscode/extensions.json), including SQL Database Projects and MSSQL.

Run these commands from the repository root in PowerShell:

```powershell
dotnet tool restore
dotnet restore PropertyManagement.slnx
dotnet build PropertyManagement.slnx -c Release --no-restore -warnaserror

$env:SQL_DEPLOY_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Database=PropertyManagementDemo;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$dacpac = 'database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac'

./scripts/Deploy-Database.ps1 -Action Preview -Dacpac $dacpac
```

Review the generated `artifacts/database-preview/deployment-report.xml` and `deployment.sql`. Then explicitly publish the approved plan:

```powershell
./scripts/Deploy-Database.ps1 -Action Publish -Dacpac $dacpac -ApprovedPlan artifacts/database-preview/plan.json
sqlcmd -S '(localdb)\MSSQLLocalDB' -d PropertyManagementDemo -E -i database/PropertyManagement.Database/Seed/DemoData.sql -b
dotnet run --project src/PropertyManagement.Web/PropertyManagement.Web.csproj -c Release --no-build --launch-profile local
```

Open **http://localhost:5180**. If that port is occupied, append `-- --urls http://localhost:5181` to the run command.

The fictional seed contains six properties, five tenants, five leases, and three payments. Dates are relative to the month in which it first runs. It only runs against `PropertyManagementDemo`, inserts transactionally into an empty portfolio, and skips a database that already contains properties or tenants. It never resets existing data.

The app deliberately has **no sign-in**. Keep it on localhost with fictional data. Authentication, authorization, HTTPS deployment, backups, and production operational controls are prerequisites for any shared use.

### Package Feed Troubleshooting

If direct NuGet access is unavailable in your environment, use an approved accessible feed for the current terminal. For the Microsoft package proxy used during this implementation:

```powershell
$env:NUGET_PACKAGE_SOURCES = 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json'
$env:RestoreSources = $env:NUGET_PACKAGE_SOURCES
dotnet tool restore --add-source $env:NUGET_PACKAGE_SOURCES --ignore-failed-sources
dotnet restore PropertyManagement.slnx --source $env:NUGET_PACKAGE_SOURCES
```

`RestoreSources` also covers the small temporary SQL projects built by the deployment tests. The repository and hosted CI retain normal NuGet defaults; no global feed settings or disabled TLS checks are required.

## Data Access Map

All paths use `ConnectionStrings:PropertyManagement`. ASP.NET Core opens the database connections itself; there is no intermediate HTTP API or browser-to-database connection.

| Feature | Access Path | Implementation |
| --- | --- | --- |
| Property and tenant CRUD | EF Core | [Controllers](src/PropertyManagement.Web/Controllers), [PropertyDbContext.cs](src/PropertyManagement.Web/Data/PropertyDbContext.cs) |
| Lease operations | EF Core with a serializable transaction | [LeaseService.cs](src/PropertyManagement.Web/Data/LeaseService.cs) |
| Dashboard and property summaries | Parameterized T-SQL using `SqlCommand` | [ReportingQueries.cs](src/PropertyManagement.Web/Data/ReportingQueries.cs) |
| Record rent payment | `SqlCommand` with `CommandType.StoredProcedure` | [RentPaymentService.cs](src/PropertyManagement.Web/Data/RentPaymentService.cs), [usp_RecordRentPayment.sql](database/PropertyManagement.Database/dbo/StoredProcedures/usp_RecordRentPayment.sql) |
| Monthly ledger | Direct stored-procedure call | [usp_GetRentLedger.sql](database/PropertyManagement.Database/dbo/StoredProcedures/usp_GetRentLedger.sql) |

The four tables are `Properties`, `Tenants`, `Leases`, and `RentPayments`. Foreign keys preserve lease/payment history, row versions protect edits from stale writes, and a unique submission key makes repeated identical payment submissions idempotent. A token reused with different payment details is rejected.

Lease overlaps are prevented, including concurrent attempts. Dates and financial terms of a lease with payments cannot be changed. Deleting a referenced property, tenant, or paid lease is rejected with a friendly message.

## SQL Project Workflow

The SDK-style project uses `Microsoft.Build.Sql` and targets SQL Server 2019 (`Sql150`). That matches the local engine; the version in a documentation URL does not select the database platform.

1. Open [PropertyManagement.Database.sqlproj](database/PropertyManagement.Database/PropertyManagement.Database.sqlproj) in the VS Code SQL Database Projects view.
2. Edit the declarative `CREATE TABLE` or `CREATE PROCEDURE` definitions under [dbo](database/PropertyManagement.Database/dbo). Update EF mappings and application contracts when columns change.
3. Build the SQL project. References, SQL analysis, and warnings are validated:

   ```powershell
   dotnet build database/PropertyManagement.Database/PropertyManagement.Database.sqlproj -c Release -warnaserror
   ```

4. Use Schema Compare in the MSSQL extension to inspect differences, or run the `Preview` command above. Preview produces a deployment report, SQL script, and hash-based approval manifest without applying schema changes.
5. Review changes, run the tests, and publish the same built DACPAC. A changed artifact, profile, target identity, report, or generated script invalidates an approved plan.

The [safe publish profile](database/PropertyManagement.Database/PublishProfiles/Safe.publish.xml) contains no target or credentials. The [local profile](database/PropertyManagement.Database/PublishProfiles/Local.publish.xml) supplies the non-secret LocalDB target for extension-based publishing.

The helper enforces `BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False`, `CreateNewDatabase=False`, compatible-platform validation, and explicit no-drop settings for unmodeled constraints, indexes, and DML triggers. `CreateNewDatabase=False` prevents replacing an existing database; an absent target can still be created.

These settings are safeguards, not a blanket guarantee against every destructive operation. Always review the plan. A script generated for an absent database is not safe to replay blindly against a database created later; prefer the helper's fresh comparison and SqlPackage publish. Renames, data backfills, and destructive changes need a reviewed refactor or expand/contract strategy and a recovery plan. Redeploying an old DACPAC is **not** an automatic rollback.

The seed script is excluded from model compilation and is never a post-deployment script. Application startup and tests do not call `EnsureCreated`, `Database.Migrate`, or `dotnet ef database update`.

Reference: [Microsoft SQL Database Projects getting started](https://learn.microsoft.com/en-us/sql/tools/visual-studio-code-extensions/sql-database-projects/getting-started-sql-database-projects-extension?view=sql-server-ver17).

## Azure Pipelines

[azure-pipelines.yml](azure-pipelines.yml) is intended for **Azure DevOps Services**.

For a minimal web-only build, create a separate Azure Pipeline that points to [azure-pipelines-web.yml](azure-pipelines-web.yml). It builds the .NET website and publishes the deployable output as a pipeline artifact named `web`.

| Stage | Behavior |
| --- | --- |
| `Build` | Restore pinned tools/packages, build the solution with SQL analysis and warnings as errors, publish `database` and `web` artifacts. |
| `ValidateSql` | Download this run's DACPAC, verify its checksum, start a digest-pinned SQL Server 2019 Developer container, preview/publish it, run SQL and HTTP integration tests, publish reports, and remove the container. |
| `PlanDeployment` | Opt-in, trusted-main-only stage that generates target-specific review artifacts without publishing. |
| `DeployDatabase` | Deployment job using a protected Azure DevOps environment; compares the approved plan again and publishes the exact same DACPAC without rebuilding it. |

CI uses a generated and masked per-run password, a loopback-only SQL port, and isolated test databases. It needs no persistent SQL credentials. The SQL image is a reproducible test baseline; review and update its tag/digest as SQL Server servicing releases change.

### Enable CI

1. Place this source in your chosen repository and create an Azure Pipeline pointing to the root YAML file. No repository, branch, commit, or remote has been created automatically.
2. Authorize Microsoft-hosted Ubuntu agents and permit downloads from NuGet, Microsoft Container Registry, and the .NET SDK distribution endpoints.
3. For **Azure Repos Git**, configure a required **Build validation** policy on `main` to run this pipeline for pull requests. Azure Repos PR validation is a branch policy, not a YAML `pr:` trigger. Main pushes trigger CI directly; no path filter excludes application-only changes.
4. Leave `deployDatabase=false` for normal CI. Inspect the `database`, `web`, and `sql-ci-reports` artifacts and the published test results.

The `web` artifact is packaged application output, not an automatically hosted website. No Azure infrastructure or public application deployment is included.

### Enable Shared SQL Deployment

Only enable this after selecting a real target, deployment identity, agent, and backup/recovery policy:

1. Create a private-network self-hosted agent pool, default `SqlDeploymentAgents`, with PowerShell 7.2+, target connectivity, and .NET SDK/tool download access. A hosted agent **cannot reach this PC's LocalDB**. Do not expose LocalDB to the internet.
2. Create a variable group, default `PropertyManagement-Sql`, with a **secret** `SqlDeploymentConnectionString` specifying the server and database. Use Windows integrated authentication under the agent identity where appropriate, or a separately managed SQL deployment login. Use trusted TLS certificates for shared targets; local `TrustServerCertificate=True` is only a demo/CI convenience.
3. Restrict the variable group and agent pool to this pipeline and trusted branches using Azure DevOps resource permissions/checks. Do not grant untrusted PR builds access to persistent credentials. YAML branch conditions are useful guards, not a substitute for protected resource permissions.
4. Create the environment `property-management-sql`. Configure **approval**, **branch control allowing protected `main`**, and an **exclusive lock** in Azure DevOps before enabling deployment. These checks are configured outside YAML and cannot be guaranteed by a file in the repository.
5. Run from trusted `main` with `deployDatabase=true`. The pool, variable group, and environment names are parameters. Review `sql-target-plan/deployment-report.xml` and `deployment.sql` from the preceding stage before approving the environment deployment.

Deployments use `download: current`, artifact checksums, and a sequential environment lock. The publish job rejects changed plans instead of silently applying new changes after approval. Prevent out-of-band schema changes during deployment: an environment lock serializes pipeline jobs but is not an atomic lock against every external database administrator.

The deployment identity needs DacFx metadata access and permission to perform the reviewed DDL; configure those permissions for the selected target. Use a separate, limited application identity in shared environments. No production target or Azure DevOps configuration is assumed or provisioned here.

## Tests

```powershell
dotnet build database/PropertyManagement.Database/PropertyManagement.Database.sqlproj -c Release -warnaserror
dotnet tool restore
dotnet test tests/PropertyManagement.Tests/PropertyManagement.Tests.csproj -c Release -warnaserror
```

Tests deploy the DACPAC to a generated `PropertyManagement_Tests_<guid>` database, create their own fixtures, and drop only that database afterward. They never reset the demo database. The default is local Windows authentication; set `TEST_SQL_CONNECTION_STRING` for a different disposable test server. Its catalog is always replaced with a unique test database name. The test identity needs database creation/deletion permissions.

`DACPAC_PATH` selects an already-built artifact, as used in CI. The additive/destructive deployment test builds small isolated probe DACPACs, not a replacement application deployment artifact. An actual previous-release upgrade baseline should be added when a released DACPAC exists.

Coverage includes EF-to-SQL consistency, parameterized search, payment validation/idempotency, unpaid and overpaid ledger calculations, foreign keys, currency/month form binding, antiforgery, simultaneous lease submissions, repeated publishing, additive upgrades, blocked data loss, and stale deployment-plan rejection.

Azure Pipelines must still be activated and run in your organization. Local tests and YAML validation do not prove remote agent access, permissions, approval configuration, or deployment to an unconfigured target.

## Demo Boundaries

- One property is one rentable unit. There is no building/unit hierarchy.
- Each lease overlapping a month owes its full monthly rent. There is no proration, invoicing, late-fee calculation, bank integration, or payment gateway.
- Outstanding rent is clamped per lease; a credit on one lease does not cancel another lease's unpaid rent. Payments are grouped by allocated rental month, not cash-receipt month.
- Occupancy is evaluated as of today. Future leases are shown separately from current occupancy.
- Display defaults to `en-ZA` and `ZAR`, configured in [appsettings.json](src/PropertyManagement.Web/appsettings.json). The culture controls formatting and the currency symbol; keep the displayed currency code consistent. Changing these settings does not convert amounts.

Icons are the locally vendored [Lucide](https://lucide.dev/) library, with its [license](src/PropertyManagement.Web/wwwroot/js/lucide.LICENSE.txt). The illustrative residence photograph is from [Unsplash](https://images.unsplash.com/photo-1600585154340-be6161a56a0c), distributed under the [Unsplash license](https://unsplash.com/license); it does not represent a specific demo property.