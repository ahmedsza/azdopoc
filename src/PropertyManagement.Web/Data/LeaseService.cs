using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Models;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Data;

public sealed class LeaseService(PropertyDbContext database)
{
    public async Task<int> SaveAsync(LeaseInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var lease = input.Id == 0 ? new Lease() : await database.Leases.SingleOrDefaultAsync(lease => lease.Id == input.Id, cancellationToken)
            ?? throw new KeyNotFoundException("This lease no longer exists.");
        var start = input.StartDate!.Value;
        var end = input.EndDate!.Value;

        if (!await database.Properties.AnyAsync(property => property.Id == input.PropertyId, cancellationToken)
            || !await database.Tenants.AnyAsync(tenant => tenant.Id == input.TenantId, cancellationToken))
            throw new ValidationException("Choose an existing property and tenant.");

        if (input.Id != 0)
        {
            byte[] version = new byte[8];
            if (string.IsNullOrEmpty(input.Version) || !Convert.TryFromBase64String(input.Version, version, out var bytesWritten) || bytesWritten != 8)
                throw new ValidationException("Reload the lease before saving changes.");
            database.Entry(lease).Property(lease => lease.Version).OriginalValue = version;
            if (await database.RentPayments.AnyAsync(payment => payment.LeaseId == input.Id, cancellationToken)
                && (lease.PropertyId != input.PropertyId || lease.TenantId != input.TenantId
                    || lease.StartDate != start || lease.EndDate != end || lease.MonthlyRent != input.MonthlyRent))
                throw new ValidationException("A lease with recorded payments cannot be changed. Create a new lease for a renewal.");
        }

        if (await database.Leases.AnyAsync(existing => existing.Id != input.Id && existing.PropertyId == input.PropertyId
            && existing.StartDate <= end && existing.EndDate >= start, cancellationToken))
            throw new ValidationException("This property already has a lease covering these dates.");

        lease.PropertyId = input.PropertyId;
        lease.TenantId = input.TenantId;
        lease.StartDate = start;
        lease.EndDate = end;
        lease.MonthlyRent = input.MonthlyRent;
        if (input.Id == 0) database.Leases.Add(lease);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return lease.Id;
    }
}