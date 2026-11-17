using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Security;

namespace Tezgah.Core.Modules;

/// <summary>
/// A self-contained business capability: its own project, schema, migrations and endpoints.
/// A deployment decides which modules it ships; inside a deployment each company is given its own
/// selection.
/// </summary>
public interface IModule
{
    ModuleInfo Info { get; }

    /// <summary>
    /// Everything a user can be allowed to do in this module. Every endpoint the module maps asks for
    /// one of them (a test checks that none is left open to every role).
    /// </summary>
    IReadOnlyList<Permission> Permissions { get; }

    void AddServices(IServiceCollection services);

    /// <param name="api">
    /// Already scoped to <c>/api/{key}</c> and closed to companies that do not have the module.
    /// </param>
    void MapEndpoints(RouteGroupBuilder api);

    Task MigrateAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>Fills a demo company with believable data. The tenant context is already set.</summary>
    Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct) => Task.CompletedTask;
}

/// <param name="Key">Stable identifier, also the URL segment and database schema.</param>
/// <param name="Name">Shown to users.</param>
/// <param name="DependsOn">Modules that must be enabled together with this one.</param>
public sealed record ModuleInfo(string Key, string Name, string Description, params string[] DependsOn);

/// <summary>The kinds of demo company the seed data knows how to dress up.</summary>
public enum DemoBusiness
{
    Hairdresser,
    Cafe,
    Logistics,
}
