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
/// "same installation, different modules per firm" is visible right after the first start. Each has
/// an owner and a member of staff, so the difference between the roles can be tried.
/// </summary>
internal static class DemoCompanies
{
    /// <summary>Shared by every demo account, which is fine for a demo and only for a demo.</summary>
    public const string Password = "tezgah-demo-2026";

    private static readonly DemoCompany[] Companies =
    [
        new("Makas Kuaför", "Kuaför", DemoBusiness.Hairdresser, ["customers", "loyalty", "appointments"],
            [("Elif Demir", "makas@tezgah.local", Roles.Owner), ("Selin Kaya", "resepsiyon@tezgah.local", Roles.Staff)]),
        new("Köşe Kahve", "Kafe", DemoBusiness.Cafe, ["customers", "orders", "loyalty", "inventory"],
            [("Murat Aksoy", "kahve@tezgah.local", Roles.Owner), ("Deniz Arı", "kasa@tezgah.local", Roles.Staff)]),
        new("Yıldız Lojistik", "Lojistik", DemoBusiness.Logistics, ["fleet"],
            [("Hakan Yıldız", "lojistik@tezgah.local", Roles.Owner), ("Okan Çelik", "filo@tezgah.local", Roles.Manager)]),
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
                db.Add(tenant);
                foreach (var (name, email, role) in company.People)
                {
                    var user = User.Create(tenant.Id, email, name, role, now);
                    user.SetPassword(hasher.HashPassword(user, Password));
                    db.Add(user);
                }

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
        string Name, string Sector, DemoBusiness Business, string[] Modules, (string Name, string Email, string Role)[] People);
}
