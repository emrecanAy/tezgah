using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tezgah.Core.Modules;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform;

/// <summary>
/// Every module request asks "does this company have the module?", so the answer is cached for a
/// short while and dropped the moment an administrator changes the selection.
/// </summary>
internal sealed class TenantModules(PlatformDbContext db, IMemoryCache cache) : ITenantModules
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    public async Task<bool> IsEnabledAsync(Guid tenantId, string moduleKey, CancellationToken ct)
    {
        var enabled = await cache.GetOrCreateAsync(CacheKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var keys = await db.Set<Domain.TenantModule>()
                .Where(module => module.TenantId == tenantId)
                .Select(module => module.ModuleKey)
                .ToListAsync(ct);
            return keys.ToHashSet();
        });

        return enabled!.Contains(moduleKey);
    }

    public void Forget(Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private static string CacheKey(Guid tenantId) => $"tenant-modules:{tenantId}";
}
