using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Tenancy;

namespace Tezgah.Customers;

internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(CustomersModule.Key);

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.Id).ValueGeneratedNever();
            customer.Property(c => c.FullName).HasMaxLength(120);
            customer.Property(c => c.Phone).HasMaxLength(20);
            customer.Property(c => c.Email).HasMaxLength(200);
            customer.Property(c => c.Notes).HasMaxLength(1000);
            // The phone number identifies a customer within one company; two companies can share a customer.
            customer.HasIndex(c => new { c.TenantId, c.Phone }).IsUnique();
        });

        IsolateTenants(modelBuilder);
    }
}
