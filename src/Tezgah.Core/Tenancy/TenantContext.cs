namespace Tezgah.Core.Tenancy;

/// <summary>
/// Which company the current unit of work acts for. Set once per scope: from the access token
/// for HTTP requests, from the event for background handlers.
/// </summary>
public sealed class TenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    /// <exception cref="InvalidOperationException">
    /// No tenant was set. Reading or writing tenant data without knowing whose it is is a bug,
    /// so this fails loudly instead of returning everything or nothing.
    /// </exception>
    public Guid TenantId => _tenantId
        ?? throw new InvalidOperationException("Tenant data was accessed outside the scope of a tenant.");

    public void Set(Guid tenantId)
    {
        if (_tenantId.HasValue && _tenantId != tenantId)
        {
            throw new InvalidOperationException("The tenant of a running operation cannot be switched.");
        }

        _tenantId = tenantId;
    }
}
