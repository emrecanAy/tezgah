using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Tenancy;

namespace Tezgah.Fleet;

internal sealed class FleetDbContext(DbContextOptions<FleetDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<MaintenanceRecord> Maintenance => Set<MaintenanceRecord>();

    public DbSet<FuelEntry> Fuel => Set<FuelEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(FleetModule.Key);

        modelBuilder.Entity<Vehicle>(vehicle =>
        {
            vehicle.Property(v => v.Id).ValueGeneratedNever();
            vehicle.Property(v => v.Plate).HasMaxLength(16);
            vehicle.Property(v => v.Brand).HasMaxLength(60);
            vehicle.Property(v => v.Model).HasMaxLength(60);
            vehicle.Property(v => v.Driver).HasMaxLength(120);
            vehicle.Property(v => v.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            vehicle.HasIndex(v => new { v.TenantId, v.Plate }).IsUnique();
        });

        modelBuilder.Entity<MaintenanceRecord>(record =>
        {
            record.ToTable("maintenance_records");
            record.Property(r => r.Id).ValueGeneratedNever();
            record.Property(r => r.Description).HasMaxLength(300);
            record.Property(r => r.Cost).HasPrecision(12, 2);
            record.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId);
        });

        modelBuilder.Entity<FuelEntry>(entry =>
        {
            entry.ToTable("fuel_entries");
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Liters).HasPrecision(8, 2);
            entry.Property(e => e.TotalCost).HasPrecision(12, 2);
            entry.HasOne<Vehicle>().WithMany().HasForeignKey(e => e.VehicleId);
        });

        IsolateTenants(modelBuilder);
    }
}
