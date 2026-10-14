using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Models;

namespace PropertyManagement.Web.Data;

public sealed class PropertyDbContext(DbContextOptions<PropertyDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<RentPayment> RentPayments => Set<RentPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Property>(entity =>
        {
            entity.ToTable("Properties", "dbo");
            entity.Property(property => property.Name).HasMaxLength(120);
            entity.Property(property => property.Address).HasMaxLength(240);
            entity.Property(property => property.City).HasMaxLength(100);
            entity.Property(property => property.MonthlyRent).HasPrecision(18, 2);
            entity.Property(property => property.Version).IsRowVersion();
        });
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants", "dbo");
            entity.Property(tenant => tenant.FullName).HasMaxLength(120);
            entity.Property(tenant => tenant.Email).HasMaxLength(200);
            entity.Property(tenant => tenant.Phone).HasMaxLength(40);
            entity.Property(tenant => tenant.Version).IsRowVersion();
        });
        modelBuilder.Entity<Lease>(entity =>
        {
            entity.ToTable("Leases", "dbo");
            entity.Property(lease => lease.MonthlyRent).HasPrecision(18, 2);
            entity.Property(lease => lease.Version).IsRowVersion();
            entity.HasOne(lease => lease.Property).WithMany(property => property.Leases)
                .HasForeignKey(lease => lease.PropertyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(lease => lease.Tenant).WithMany(tenant => tenant.Leases)
                .HasForeignKey(lease => lease.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RentPayment>(entity =>
        {
            entity.ToTable("RentPayments", "dbo");
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.Reference).HasMaxLength(100);
            entity.HasOne(payment => payment.Lease).WithMany(lease => lease.Payments)
                .HasForeignKey(payment => payment.LeaseId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}