using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Modules;
using Tezgah.Core.Tenancy;
using Tezgah.Core.Web;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Demo;

/// <summary>
/// Three example companies, each with a different slice of the platform, so the idea
/// "same installation, different modules per firm" is visible right after the first start.
/// </summary>
internal static class DemoCompanies
{
    public const string Password = "demo1234";

    private static readonly DemoCompany[] Companies =
    [
        new("Makas Kuaför", "Kuaför", "Elif Demir", "makas@tezgah.local", DemoBusiness.Hairdresser,
            ["customers", "loyalty", "appointments"]),
        new("Köşe Kahve", "Kafe", "Murat Aksoy", "kahve@tezgah.local", DemoBusiness.Cafe,
            ["customers", "loyalty", "inventory"]),
        new("Yıldız Lojistik", "Lojistik", "Hakan Yıldız", "lojistik@tezgah.local", DemoBusiness.Logistics,
            ["fleet"]),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var catalog = services.GetRequiredService<ModuleCatalog>();
        var created = new List<(Tenant Tenant, DemoCompany Company)>();

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            if (await db.Tenants.AnyAsync(ct))
            {
                return;
            }

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
            foreach (var company in Companies)
            {
                // An installation that ships only some modules gets the part of each demo company it can run.
                var modules = company.Modules.Where(catalog.Contains).ToList();
                if (modules.Count == 0 || catalog.Validate(modules) is not null)
                {
                    continue;
                }

                var tenant = Tenant.Create(company.Name, company.Sector, modules, now);
                var owner = User.Create(tenant.Id, company.OwnerEmail, company.OwnerName, Roles.Owner);
                owner.SetPasswordHash(hasher.HashPassword(owner, Password));
                db.AddRange(tenant, owner);
                created.Add((tenant, company));
            }

            await db.SaveChangesAsync(ct);
        }

        foreach (var (tenant, company) in created)
        {
            // One scope per company: the tenant of a scope is fixed once set.
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.Id);

            var enabled = tenant.Modules.Select(module => module.ModuleKey).ToHashSet();
            foreach (var module in catalog.Modules.Where(module => enabled.Contains(module.Info.Key)))
            {
                await module.SeedDemoAsync(scope.ServiceProvider, company.Business, ct);
            }
        }
    }

    private sealed record DemoCompany(
        string Name, string Sector, string OwnerName, string OwnerEmail, DemoBusiness Business, string[] Modules);
}
