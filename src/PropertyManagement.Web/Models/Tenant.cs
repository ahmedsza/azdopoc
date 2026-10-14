namespace PropertyManagement.Web.Models;

public sealed class Tenant
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public byte[] Version { get; set; } = [];
    public ICollection<Lease> Leases { get; set; } = [];
}