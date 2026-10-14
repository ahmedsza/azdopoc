using System.Data;
using Microsoft.Data.SqlClient;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Data;

public sealed class ReportingQueries(SqlConnectionFactory connections)
{
    public async Task<DashboardViewModel> GetDashboardAsync(DateOnly month, string search = "", CancellationToken cancellationToken = default)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT(*) FROM dbo.Properties) AS PropertyCount,
                (SELECT COUNT(DISTINCT PropertyId) FROM dbo.Leases WHERE StartDate <= @Today AND EndDate >= @Today) AS OccupiedCount,
                (SELECT COUNT(*) FROM dbo.Tenants) AS TenantCount,
                COALESCE(SUM(lease.MonthlyRent), 0) AS ExpectedRent,
                COALESCE(SUM(payment.Paid), 0) AS CollectedRent,
                COALESCE(SUM(CASE WHEN lease.MonthlyRent > COALESCE(payment.Paid, 0)
                    THEN lease.MonthlyRent - COALESCE(payment.Paid, 0) ELSE 0 END), 0) AS OutstandingRent
            FROM dbo.Leases AS lease
            OUTER APPLY (
                SELECT SUM(rent.Amount) AS Paid FROM dbo.RentPayments AS rent
                WHERE rent.LeaseId = lease.Id AND rent.RentalMonth = @Month
            ) AS payment
            WHERE lease.StartDate <= EOMONTH(@Month) AND lease.EndDate >= @Month;

            SELECT TOP (6) property.Id, property.Name, property.City, property.Bedrooms, property.MonthlyRent, activeLease.FullName
            FROM dbo.Properties AS property
            OUTER APPLY (
                SELECT TOP (1) tenant.FullName FROM dbo.Leases AS lease
                INNER JOIN dbo.Tenants AS tenant ON tenant.Id = lease.TenantId
                WHERE lease.PropertyId = property.Id AND lease.StartDate <= @Today AND lease.EndDate >= @Today
                ORDER BY lease.StartDate DESC, lease.Id DESC
            ) AS activeLease
            WHERE @Search = N'' OR CHARINDEX(@Search, property.Name) > 0 OR CHARINDEX(@Search, property.City) > 0
            ORDER BY property.Name, property.Id;

            SELECT TOP (5) payment.Id, property.Name, tenant.FullName, payment.PaidOn, payment.Amount, payment.Reference
            FROM dbo.RentPayments AS payment
            INNER JOIN dbo.Leases AS lease ON lease.Id = payment.LeaseId
            INNER JOIN dbo.Properties AS property ON property.Id = lease.PropertyId
            INNER JOIN dbo.Tenants AS tenant ON tenant.Id = lease.TenantId
            WHERE payment.RentalMonth = @Month
            ORDER BY payment.PaidOn DESC, payment.Id DESC;
            """, connection);
        command.Parameters.Add("@Month", SqlDbType.Date).Value = month.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@Today", SqlDbType.Date).Value = DateTime.Today;
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 100).Value = search;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var propertyCount = reader.GetInt32(0);
        var occupiedCount = reader.GetInt32(1);
        var tenantCount = reader.GetInt32(2);
        var expected = reader.GetDecimal(3);
        var collected = reader.GetDecimal(4);
        var outstanding = reader.GetDecimal(5);
        List<PropertyOverview> properties = [];
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            properties.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        List<PaymentOverview> payments = [];
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            payments.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4), reader.GetString(5)));
        return new(month, propertyCount, occupiedCount, tenantCount, expected, collected, outstanding, properties, payments);
    }
}