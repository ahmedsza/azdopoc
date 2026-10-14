namespace PropertyManagement.Web.Models;

public sealed class RentPayment
{
    public int Id { get; set; }
    public int LeaseId { get; set; }
    public DateOnly RentalMonth { get; set; }
    public DateOnly PaidOn { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public Guid SubmissionKey { get; set; }
    public Lease Lease { get; set; } = null!;
}