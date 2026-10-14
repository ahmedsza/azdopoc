namespace PropertyManagement.Web.Models;

public sealed class Lease
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public int TenantId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public byte[] Version { get; set; } = [];
    public Property Property { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public ICollection<RentPayment> Payments { get; set; } = [];
}