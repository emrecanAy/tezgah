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

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<SignInThrottle> SignInThrottles => Set<SignInThrottle>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

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
            user.Property(u => u.TotpSecret).HasMaxLength(200);
            user.Property(u => u.PendingTotpSecret).HasMaxLength(200);
            user.Ignore(u => u.TwoStepEnabled);
            user.HasIndex(u => u.Email).IsUnique();
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.Property(t => t.Id).ValueGeneratedNever();
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.Property(t => t.Device).HasMaxLength(RefreshToken.MaxDeviceLength);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.FamilyId);
            token.HasIndex(t => t.UserId);
            token.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SignInThrottle>(throttle =>
        {
            throttle.HasKey(t => t.Email);
            throttle.Property(t => t.Email).HasMaxLength(200);
        });

        modelBuilder.Entity<RecoveryCode>(code =>
        {
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.CodeHash).HasMaxLength(64);
            code.HasIndex(c => new { c.UserId, c.CodeHash });
            code.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
