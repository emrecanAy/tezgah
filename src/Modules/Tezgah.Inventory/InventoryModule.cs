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
        db.Products.AddRange(
            Product.Create("Espresso çekirdeği", "kg", reorderLevel: 5, initialStock: 18.5m).Value,
            Product.Create("Süt", "lt", reorderLevel: 20, initialStock: 14).Value,
            Product.Create("Karton bardak 8 oz", "adet", reorderLevel: 300, initialStock: 1250).Value,
            Product.Create("Bardak kapağı", "adet", reorderLevel: 300, initialStock: 240).Value,
            Product.Create("Vanilya şurubu", "şişe", reorderLevel: 2, initialStock: 6).Value,
            Product.Create("Filtre kağıdı", "paket", reorderLevel: 3, initialStock: 9).Value);
        await db.SaveChangesAsync(ct);
    }
}
