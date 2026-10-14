using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;
using PropertyManagement.Web.Models;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Controllers;

public sealed class PropertiesController(PropertyDbContext database) : AppController
{
    public async Task<IActionResult> Index(string? search, string? status, CancellationToken cancellationToken)
    {
        ViewData["Search"] = search;
        ViewData["Status"] = status;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var query = database.Properties.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(property => property.Name.Contains(search) || property.Address.Contains(search) || property.City.Contains(search));
        if (status == "occupied") query = query.Where(property => property.Leases.Any(lease => lease.StartDate <= today && lease.EndDate >= today));
        if (status == "vacant") query = query.Where(property => !property.Leases.Any(lease => lease.StartDate <= today && lease.EndDate >= today));
        return View(await query.Include(property => property.Leases).ThenInclude(lease => lease.Tenant).OrderBy(property => property.Name).ToListAsync(cancellationToken));
    }

    public IActionResult Create() => View("Edit", new PropertyInput());

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var property = await database.Properties.FindAsync([id], cancellationToken);
        return property is null ? NotFound() : View(new PropertyInput
        {
            Id = property.Id, Name = property.Name, Address = property.Address, City = property.City,
            Bedrooms = property.Bedrooms, MonthlyRent = property.MonthlyRent, Version = Convert.ToBase64String(property.Version)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(PropertyInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        var property = input.Id == 0 ? new Property() : await database.Properties.FindAsync([input.Id], cancellationToken);
        if (property is null) return NotFound();
        try
        {
            if (input.Id != 0) SetOriginalVersion(database, property, input.Version);
            property.Name = input.Name.Trim();
            property.Address = input.Address.Trim();
            property.City = input.City.Trim();
            property.Bedrooms = input.Bedrooms;
            property.MonthlyRent = input.MonthlyRent;
            if (input.Id == 0) database.Properties.Add(property);
            await database.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Property saved.";
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
        var property = await database.Properties.FindAsync([id], cancellationToken);
        if (property is null) return NotFound();
        try
        {
            SetOriginalVersion(database, property, version);
            database.Properties.Remove(property);
            await database.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Property removed.";
        }
        catch (Exception exception) when (DataError(exception) is not null) { TempData["Error"] = DataError(exception); }
        return RedirectToAction(nameof(Index));
    }
}