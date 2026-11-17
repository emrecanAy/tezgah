namespace Tezgah.Core.Modules;

/// <summary>Answers which modules a company has been given. Implemented by the platform.</summary>
public interface ITenantModules
{
    Task<bool> IsEnabledAsync(Guid tenantId, string moduleKey, CancellationToken ct);
}
