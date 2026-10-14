using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;
using PropertyManagement.Web.Models;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Controllers;

public sealed class TenantsController(PropertyDbContext database) : AppController
{
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        ViewData["Search"] = search;
        var query = database.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(tenant => tenant.FullName.Contains(search) || tenant.Email.Contains(search));
        return View(await query.Include(tenant => tenant.Leases).ThenInclude(lease => lease.Property).OrderBy(tenant => tenant.FullName).ToListAsync(cancellationToken));
    }

    public IActionResult Create() => View("Edit", new TenantInput());

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var tenant = await database.Tenants.FindAsync([id], cancellationToken);
        return tenant is null ? NotFound() : View(new TenantInput
        {
            Id = tenant.Id, FullName = tenant.FullName, Email = tenant.Email, Phone = tenant.Phone, Version = Convert.ToBase64String(tenant.Version)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(TenantInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        var tenant = input.Id == 0 ? new Tenant() : await database.Tenants.FindAsync([input.Id], cancellationToken);
        if (tenant is null) return NotFound();
        try
        {
            if (input.Id != 0) SetOriginalVersion(database, tenant, input.Version);
            tenant.FullName = input.FullName.Trim();
            tenant.Email = input.Email.Trim();
            tenant.Phone = input.Phone.Trim();
            if (input.Id == 0) database.Tenants.Add(tenant);
            await database.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Tenant saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception) when (DataError(exception) is not null)
        {
            ModelState.AddModelError("", DataError(exception)!);
            return View(input);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string version, CancellationToken cancellationToken)
    {
        var tenant = await database.Tenants.FindAsync([id], cancellationToken);
        if (tenant is null) return NotFound();
        try
        {
            SetOriginalVersion(database, tenant, version);
            database.Tenants.Remove(tenant);
            await database.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Tenant removed.";
        }
        catch (Exception exception) when (DataError(exception) is not null) { TempData["Error"] = DataError(exception); }
        return RedirectToAction(nameof(Index));
    }
}