using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;

namespace PropertyManagement.Web.Controllers;

public abstract class AppController : Controller
{
    protected static DateOnly GetMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return new(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        throw new ValidationException("Choose a rental month in YYYY-MM format.");
    }

    protected static void SetOriginalVersion<TEntity>(PropertyDbContext database, TEntity entity, string? encoded) where TEntity : class
    {
        byte[] version = new byte[8];
        if (string.IsNullOrEmpty(encoded) || !Convert.TryFromBase64String(encoded, version, out var length) || length != 8)
            throw new ValidationException("Reload this record before saving changes.");
        database.Entry(entity).Property("Version").OriginalValue = version;
    }

    protected static string? DataError(Exception exception)
    {
        if (exception is ValidationException) return exception.Message;
        if (exception is DbUpdateConcurrencyException) return "This record changed since you opened it. Reload it before trying again.";
        var sql = exception.GetBaseException() as SqlException;
        return sql?.Number switch
        {
            >= 50001 and <= 50005 => sql.Message,
            547 => "This record is linked to a lease or payment, or contains invalid values. Its history must be preserved.",
            2601 or 2627 => "This record has already been saved.",
            1205 => "Another request changed these records. Please reload and try again.",
            _ => null
        };
    }
}