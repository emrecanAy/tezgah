namespace Tezgah.Core.Modules;

/// <summary>The modules this deployment ships, ordered so that dependencies come first.</summary>
public sealed class ModuleCatalog
{
    private readonly Dictionary<string, IModule> _byKey;

    private ModuleCatalog(IReadOnlyList<IModule> modules)
    {
        Modules = modules;
        _byKey = modules.ToDictionary(module => module.Info.Key);
    }

    public IReadOnlyList<IModule> Modules { get; }

    /// <param name="available">Every module compiled into the host.</param>
    /// <param name="shipped">Keys to load, or <c>null</c> for all. Lets one build serve as "fleet only" or "everything".</param>
    public static ModuleCatalog Create(IEnumerable<IModule> available, IReadOnlyCollection<string>? shipped)
    {
        var selected = available
            .Where(module => shipped is null || shipped.Contains(module.Info.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(module => module.Info.Key);

        var unknown = shipped?.Where(key => !selected.ContainsKey(key.ToLowerInvariant())).ToList() ?? [];
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"Unknown module(s) in configuration: {string.Join(", ", unknown)}.");
        }

        var ordered = new List<IModule>();
        foreach (var module in selected.Values.OrderBy(module => module.Info.Key, StringComparer.Ordinal))
        {
            Place(module, selected, ordered, []);
        }

        return new ModuleCatalog(ordered);
    }

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public IModule Get(string key) => _byKey[key];

    /// <summary>
    /// Checks a company's module selection against this catalog.
    /// </summary>
    /// <returns><c>null</c> when valid, otherwise a sentence saying what is wrong.</returns>
    public string? Validate(IReadOnlyCollection<string> selection)
    {
        foreach (var key in selection)
        {
            if (!_byKey.TryGetValue(key, out var module))
            {
                return $"Module '{key}' is not part of this installation.";
            }

            var missing = module.Info.DependsOn.Where(dependency => !selection.Contains(dependency)).ToList();
            if (missing.Count > 0)
            {
                return $"Module '{key}' also needs: {string.Join(", ", missing)}.";
            }
        }

        return null;
    }

    private static void Place(IModule module, Dictionary<string, IModule> selected, List<IModule> ordered, HashSet<string> visiting)
    {
        if (ordered.Contains(module))
        {
            return;
        }

        if (!visiting.Add(module.Info.Key))
        {
            throw new InvalidOperationException($"Module '{module.Info.Key}' is part of a dependency cycle.");
        }

        foreach (var dependency in module.Info.DependsOn)
        {
            if (!selected.TryGetValue(dependency, out var required))
            {
                throw new InvalidOperationException(
                    $"Module '{module.Info.Key}' needs module '{dependency}', which this installation does not ship.");
            }

            Place(required, selected, ordered, visiting);
        }

        ordered.Add(module);
    }
}
