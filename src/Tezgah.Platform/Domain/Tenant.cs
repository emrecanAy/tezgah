namespace Tezgah.Platform.Domain;

/// <summary>A company using the platform.</summary>
internal sealed class Tenant
{
    private readonly List<TenantModule> _modules = [];

    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Free text such as "Kuaför" or "Lojistik"; shown next to the name.</summary>
    public string Sector { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<TenantModule> Modules => _modules;

    public static Tenant Create(string name, string sector, IEnumerable<string> moduleKeys, DateTimeOffset now)
    {
        var tenant = new Tenant { Id = Guid.CreateVersion7(), Name = name.Trim(), Sector = sector.Trim(), CreatedAt = now };
        tenant.SetModules(moduleKeys, now);
        return tenant;
    }

    /// <summary>Replaces the selection. Modules that stay keep the date they were first enabled.</summary>
    public void SetModules(IEnumerable<string> moduleKeys, DateTimeOffset now)
    {
        var wanted = moduleKeys.ToHashSet();
        _modules.RemoveAll(module => !wanted.Contains(module.ModuleKey));
        foreach (var key in wanted.Where(key => _modules.All(module => module.ModuleKey != key)))
        {
            _modules.Add(new TenantModule(Id, key, now));
        }
    }
}

internal sealed class TenantModule(Guid tenantId, string moduleKey, DateTimeOffset enabledAt)
{
    public Guid TenantId { get; private set; } = tenantId;

    public string ModuleKey { get; private set; } = moduleKey;

    public DateTimeOffset EnabledAt { get; private set; } = enabledAt;
}
