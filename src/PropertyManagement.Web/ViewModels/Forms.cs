using System.ComponentModel.DataAnnotations;

namespace PropertyManagement.Web.ViewModels;

public abstract class EditInput
{
    public int Id { get; set; }
    [StringLength(20)]
    public string? Version { get; set; }
}

public sealed class PropertyInput : EditInput, IValidatableObject
{
    [Required, StringLength(120), Display(Name = "Property name")]
    public string Name { get; set; } = "";
    [Required, StringLength(240), Display(Name = "Street address")]
    public string Address { get; set; } = "";
    [Required, StringLength(100)]
    public string City { get; set; } = "";
    [Range(0, 50)]
    public int Bedrooms { get; set; }
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true), Display(Name = "Asking rent / month")]
    public decimal MonthlyRent { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => FormRules.Money(MonthlyRent, nameof(MonthlyRent));
}

public sealed class TenantInput : EditInput
{
    [Required, StringLength(120), Display(Name = "Full name")]
    public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";
    [Required, StringLength(40)]
    public string Phone { get; set; } = "";
}

public sealed class LeaseInput : EditInput, IValidatableObject
{
    [Range(1, int.MaxValue), Display(Name = "Property")]
    public int PropertyId { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Tenant")]
    public int TenantId { get; set; }
    [Required, DataType(DataType.Date), Display(Name = "Start date")]
    public DateOnly? StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, DataType(DataType.Date), Display(Name = "End date")]
    public DateOnly? EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddYears(1).AddDays(-1);
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true), Display(Name = "Agreed rent / month")]
    public decimal MonthlyRent { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
            yield return new ValidationResult("The end date must be on or after the start date.", [nameof(EndDate)]);
        if (StartDate < new DateOnly(1900, 1, 1) || EndDate > new DateOnly(2100, 12, 31))
            yield return new ValidationResult("Choose lease dates between 1900 and 2100.", [nameof(StartDate), nameof(EndDate)]);
        foreach (var result in FormRules.Money(MonthlyRent, nameof(MonthlyRent))) yield return result;
    }
}

public sealed class PaymentInput : IValidatableObject
{
    [Range(1, int.MaxValue), Display(Name = "Lease")]
    public int LeaseId { get; set; }
    [Required, Display(Name = "Rental month")]
    public DateOnly? RentalMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [Required, DataType(DataType.Date), Display(Name = "Payment date")]
    public DateOnly? PaidOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }
    [Required, StringLength(100), Display(Name = "Payment reference")]
    public string Reference { get; set; } = "";
    public Guid SubmissionKey { get; set; } = Guid.NewGuid();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RentalMonth is { Day: not 1 })
            yield return new ValidationResult("Choose the first day of the rental month.", [nameof(RentalMonth)]);
        if (PaidOn > DateOnly.FromDateTime(DateTime.Today))
            yield return new ValidationResult("The payment date cannot be in the future.", [nameof(PaidOn)]);
        if (SubmissionKey == Guid.Empty)
            yield return new ValidationResult("Reload the payment form before submitting.");
        foreach (var result in FormRules.Money(Amount, nameof(Amount))) yield return result;
    }
}

public static class FormRules
{
    public static IEnumerable<ValidationResult> Money(decimal amount, string member)
    {
        if (decimal.Round(amount, 2) != amount)
            yield return new ValidationResult("Use no more than two decimal places.", [member]);
    }
}