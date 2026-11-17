using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Tezgah.Core.Tenancy;

/// <summary>
/// Base class for every module's DbContext. Isolation between companies is enforced here, in one
/// place, rather than by remembering a <c>Where(x => x.TenantId == ...)</c> in each query.
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, TenantContext tenant) : DbContext(options)
{
    private static readonly MethodInfo IsolateMethod =
        typeof(TenantDbContext).GetMethod(nameof(Isolate), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Referenced by the query filters below. EF evaluates it per context instance, per query.
    private Guid CurrentTenantId => tenant.TenantId;

    /// <summary>Call from the derived context after its own configuration.</summary>
    protected void IsolateTenants(ModelBuilder modelBuilder)
    {
        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            .ToList();

        foreach (var entityType in tenantOwned)
        {
            IsolateMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Isolate<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == CurrentTenantId);
        modelBuilder.Entity<TEntity>().HasIndex(entity => entity.TenantId);
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = tenant.TenantId;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity.TenantId != tenant.TenantId)
            {
                // Unreachable through filtered queries; guards against entities attached by hand.
                throw new InvalidOperationException("An entity of another tenant cannot be changed.");
            }
        }
    }
}
