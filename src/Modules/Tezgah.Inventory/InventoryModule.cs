using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;

namespace Tezgah.Inventory;

public sealed class InventoryModule : IModule
{
    public const string Key = "inventory";

    public ModuleInfo Info { get; } = new(Key, "Stok", "Ürünler, giriş-çıkış hareketleri ve azalan stok uyarıları.");

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<InventoryDbContext>(Key);
        services.AddScoped<Stockroom>();
    }

    public void MapEndpoints(RouteGroupBuilder api) => InventoryEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<InventoryDbContext>().Database.MigrateAsync(ct);

    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        (string Name, string Unit, decimal ReorderLevel, decimal OpeningStock)[] products =
        [
            ("Espresso çekirdeği", "kg", 5, 18.5m), ("Süt", "lt", 20, 14), ("Karton bardak 8 oz", "adet", 300, 1250),
            ("Bardak kapağı", "adet", 300, 240), ("Vanilya şurubu", "şişe", 2, 6), ("Filtre kağıdı", "paket", 3, 9),
        ];
        foreach (var (name, unit, reorderLevel, openingStock) in products)
        {
            var (product, opening) = Product.Create(name, unit, reorderLevel, openingStock, "Demo", now).Value;
            db.Products.Add(product);
            db.Movements.Add(opening!);
        }

        await db.SaveChangesAsync(ct);
    }
}
