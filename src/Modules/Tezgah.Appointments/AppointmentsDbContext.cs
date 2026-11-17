using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Messaging;
using Tezgah.Core.Tenancy;

namespace Tezgah.Appointments;

internal sealed class AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    /// <summary>
    /// The rule "one person cannot be in two appointments at once" lives in the database as an
    /// exclusion constraint over (staff, time range). EF Core cannot model EXCLUDE constraints, so
    /// it is created with SQL in the initial migration. An overlapping INSERT fails there, no matter
    /// how many requests race for the slot.
    /// </summary>
    public const string NoOverlapConstraint = "ex_appointments_staff_no_overlap";

    public DbSet<Service> Services => Set<Service>();

    public DbSet<StaffMember> Staff => Set<StaffMember>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AppointmentsModule.Key).AddOutbox();
        // Lets a GiST index combine the equality on staff_id with the range overlap.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<Service>(service =>
        {
            service.Property(s => s.Id).ValueGeneratedNever();
            service.Property(s => s.Name).HasMaxLength(100);
            service.Property(s => s.Price).HasPrecision(10, 2);
        });

        modelBuilder.Entity<StaffMember>(staff =>
        {
            staff.ToTable("staff");
            staff.Property(s => s.Id).ValueGeneratedNever();
            staff.Property(s => s.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.Property(a => a.Id).ValueGeneratedNever();
            appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            appointment.Property(a => a.Notes).HasMaxLength(500);
            appointment.HasOne<Service>().WithMany().HasForeignKey(a => a.ServiceId);
            appointment.HasOne<StaffMember>().WithMany().HasForeignKey(a => a.StaffId);
            appointment.HasIndex(a => new { a.TenantId, a.StartsAt });
        });

        IsolateTenants(modelBuilder);
    }
}
