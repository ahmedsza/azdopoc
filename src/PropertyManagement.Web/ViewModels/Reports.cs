namespace PropertyManagement.Web.ViewModels;

public sealed record PropertyOverview(int Id, string Name, string City, int Bedrooms, decimal MonthlyRent, string? TenantName);
public sealed record PaymentOverview(int Id, string PropertyName, string TenantName, DateOnly PaidOn, decimal Amount, string Reference);
public sealed record LedgerRow(int LeaseId, string PropertyName, string TenantName, decimal MonthlyRent, decimal Paid, decimal Outstanding)
{
    public string Status => Paid > MonthlyRent ? "Credit" : Outstanding == 0 ? "Paid" : Paid > 0 ? "Partial" : "Unpaid";
}
public sealed record DashboardViewModel(DateOnly Month, int PropertyCount, int OccupiedCount, int TenantCount,
    decimal ExpectedRent, decimal CollectedRent, decimal OutstandingRent,
    IReadOnlyList<PropertyOverview> Properties, IReadOnlyList<PaymentOverview> RecentPayments);
public sealed record RentLedgerViewModel(DateOnly Month, IReadOnlyList<LedgerRow> Rows);