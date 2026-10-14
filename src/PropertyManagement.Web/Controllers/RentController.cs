using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Controllers;

public sealed class RentController(PropertyDbContext database, RentPaymentService payments) : AppController
{
    public async Task<IActionResult> Index(string? month, CancellationToken cancellationToken)
    {
        try
        {
            var selected = GetMonth(month);
            return View(new RentLedgerViewModel(selected, await payments.GetLedgerAsync(selected, cancellationToken)));
        }
        catch (ValidationException exception) { return BadRequest(exception.Message); }
    }

    public async Task<IActionResult> Record(int? leaseId, string? month, CancellationToken cancellationToken)
    {
        try
        {
            var selected = GetMonth(month);
            var row = (await payments.GetLedgerAsync(selected, cancellationToken)).FirstOrDefault(row => row.LeaseId == leaseId);
            await LoadChoicesAsync(cancellationToken);
            return View(new PaymentInput { LeaseId = leaseId ?? 0, RentalMonth = selected, Amount = row?.Outstanding ?? 0 });
        }
        catch (ValidationException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost]
    public async Task<IActionResult> Record(PaymentInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await payments.RecordAsync(new(input.LeaseId, input.RentalMonth!.Value, input.PaidOn!.Value, input.Amount, input.Reference.Trim(), input.SubmissionKey), cancellationToken);
                TempData["Success"] = "Rent payment recorded.";
                return RedirectToAction(nameof(Index), new { month = input.RentalMonth.Value.ToString("yyyy-MM") });
            }
            catch (Exception exception) when (DataError(exception) is not null) { ModelState.AddModelError("", DataError(exception)!); }
        }
        await LoadChoicesAsync(cancellationToken);
        return View(input);
    }

    private async Task LoadChoicesAsync(CancellationToken cancellationToken)
    {
        var choices = await database.Leases.AsNoTracking().OrderBy(lease => lease.Property.Name)
            .Select(lease => new { lease.Id, Label = lease.Property.Name + " / " + lease.Tenant.FullName }).ToListAsync(cancellationToken);
        ViewBag.Leases = new SelectList(choices, "Id", "Label");
    }
}