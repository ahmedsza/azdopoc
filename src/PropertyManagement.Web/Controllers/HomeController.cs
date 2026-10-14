using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Web.Data;

namespace PropertyManagement.Web.Controllers;

public sealed class HomeController(ReportingQueries reports) : AppController
{
    public async Task<IActionResult> Index(string? month, CancellationToken cancellationToken)
    {
        try { return View(await reports.GetDashboardAsync(GetMonth(month), cancellationToken: cancellationToken)); }
        catch (ValidationException exception) { return BadRequest(exception.Message); }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View();
    }
}