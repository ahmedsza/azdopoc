using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Controllers;

public sealed class LeasesController(PropertyDbContext database, LeaseService leases) : AppController
{
    public async Task<IActionResult> Index(string? status, CancellationToken cancellationToken)
    {
        ViewData["Status"] = status;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var query = database.Leases.AsNoTracking();
        if (status == "active") query = query.Where(lease => lease.StartDate <= today && lease.EndDate >= today);
        if (status == "upcoming") query = query.Where(lease => lease.StartDate > today);
        if (status == "ended") query = query.Where(lease => lease.EndDate < today);
        return View(await query.Include(lease => lease.Property).Include(lease => lease.Tenant).OrderByDescending(lease => lease.StartDate).ToListAsync(cancellationToken));
    }

    public async Task<IActionResult> Create(int? propertyId, CancellationToken cancellationToken)
    {
        await LoadChoicesAsync(cancellationToken);
        var property = propertyId.HasValue ? await database.Properties.FindAsync([propertyId.Value], cancellationToken) : null;
        return View("Edit", new LeaseInput { PropertyId = property?.Id ?? 0, MonthlyRent = property?.MonthlyRent ?? 0 });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var lease = await database.Leases.FindAsync([id], cancellationToken);
        if (lease is null) return NotFound();
        await LoadChoicesAsync(cancellationToken);
        return View(new LeaseInput { Id = lease.Id, PropertyId = lease.PropertyId, TenantId = lease.TenantId,
            StartDate = lease.StartDate, EndDate = lease.EndDate, MonthlyRent = lease.MonthlyRent, Version = Convert.ToBase64String(lease.Version) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(LeaseInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await leases.SaveAsync(input, cancellationToken);
                TempData["Success"] = "Lease saved.";
                return RedirectToAction(nameof(Index));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (Exception exception) when (DataError(exception) is not null) { ModelState.AddModelError("", DataError(exception)!); }
        }
        await LoadChoicesAsync(cancellationToken);
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string version, CancellationToken cancellationToken)
    {
        var lease = await database.Leases.FindAsync([id], cancellationToken);
        if (lease is null) return NotFound();
        try
        {
            SetOriginalVersion(database, lease, version);
            database.Leases.Remove(lease);
            await database.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Lease removed.";
        }
        catch (Exception exception) when (DataError(exception) is not null) { TempData["Error"] = DataError(exception); }
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadChoicesAsync(CancellationToken cancellationToken)
    {
        ViewBag.Properties = new SelectList(await database.Properties.AsNoTracking().OrderBy(property => property.Name).ToListAsync(cancellationToken), "Id", "Name");
        ViewBag.Tenants = new SelectList(await database.Tenants.AsNoTracking().OrderBy(tenant => tenant.FullName).ToListAsync(cancellationToken), "Id", "FullName");
    }
}