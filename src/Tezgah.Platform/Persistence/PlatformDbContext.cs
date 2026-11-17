using Microsoft.EntityFrameworkCore;
using Tezgah.Platform.Domain;

namespace Tezgah.Platform.Persistence;

/// <summary>
/// Companies, their users and their module selections. Deliberately not a <c>TenantDbContext</c>:
/// this is the data that sits above tenants.
/// </summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Id).ValueGeneratedNever();
            tenant.Property(t => t.Name).HasMaxLength(120);
            tenant.Property(t => t.Sector).HasMaxLength(60);
            tenant.HasMany(t => t.Modules).WithOne().HasForeignKey(m => m.TenantId);
            tenant.Navigation(t => t.Modules).AutoInclude();
        });

        modelBuilder.Entity<TenantModule>(module =>
        {
            module.ToTable("tenant_modules");
            module.HasKey(m => new { m.TenantId, m.ModuleKey });
            module.Property(m => m.ModuleKey).HasMaxLength(40);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.Email).HasMaxLength(200);
            user.Property(u => u.FullName).HasMaxLength(120);
            user.Property(u => u.Role).HasMaxLength(30);
            user.HasIndex(u => u.Email).IsUnique();
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
