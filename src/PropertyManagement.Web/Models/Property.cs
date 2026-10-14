namespace PropertyManagement.Web.Models;

public sealed class Property
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public int Bedrooms { get; set; }
    public decimal MonthlyRent { get; set; }
    public byte[] Version { get; set; } = [];
    public ICollection<Lease> Leases { get; set; } = [];
}