using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;
using Tezgah.Core.Tenancy;

namespace Tezgah.Orders;

public sealed class OrdersModule : IModule
{
    public const string Key = "orders";

    public ModuleInfo Info { get; } = new(
        Key, "Sipariş", "Menü ve kasa: tutar menüden hesaplanır, sadakat damgası siparişle gelir.", DependsOn: "customers");

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<OrdersDbContext>(Key);
        services.AddOutbox<OrdersDbContext>();
        services.AddScoped<Till>();
        // Replaced by the Loyalty module when the installation ships it, whichever registers first.
        services.TryAddScoped<ILoyaltyRewards, NoLoyaltyRewards>();
    }

    public void MapEndpoints(RouteGroupBuilder api) => OrderEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<OrdersDbContext>().Database.MigrateAsync(ct);

    /// <summary>
    /// A café menu and two weeks of sales. The orders of known customers publish the same event the
    /// till does, so their loyalty cards fill up through the regular path.
    /// </summary>
    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        (string Name, string Category, decimal Price)[] menu =
        [
            ("Türk Kahvesi", "Kahve", 90m), ("Filtre Kahve", "Kahve", 100m), ("Americano", "Kahve", 110m),
            ("Latte", "Kahve", 130m), ("Çay", "Sıcak İçecek", 30m), ("Sıcak Çikolata", "Sıcak İçecek", 120m),
            ("Limonata", "Soğuk İçecek", 95m), ("San Sebastian", "Tatlı", 180m), ("Brownie", "Tatlı", 140m),
            ("Kaşarlı Tost", "Atıştırmalık", 150m),
        ];

        var db = services.GetRequiredService<OrdersDbContext>();
        var items = menu.Select(entry => MenuItem.Create(entry.Name, entry.Category, entry.Price).Value).ToArray();
        db.Menu.AddRange(items);

        // Who bought what, how many days ago. The first customer has bought five coffees: one full card and a stamp.
        var customers = await services.GetRequiredService<ICustomerDirectory>().ListAsync(5, ct);
        (int? Customer, int DaysAgo, (int Item, int Quantity)[] Picks)[] sales =
        [
            (0, 12, [(3, 1)]), (0, 9, [(3, 1), (7, 1)]), (0, 6, [(0, 2)]), (0, 2, [(1, 1)]),
            (1, 8, [(2, 1), (8, 1)]), (1, 3, [(3, 2)]), (2, 5, [(0, 1), (4, 2)]), (3, 1, [(9, 1), (6, 1)]),
            (null, 4, [(4, 3)]), (null, 1, [(3, 1), (7, 1)]), (null, 0, [(2, 2), (8, 1)]), (4, 0, [(3, 1)]),
        ];

        var tenantId = services.GetRequiredService<TenantContext>().TenantId;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        foreach (var (customer, daysAgo, picks) in sales)
        {
            var customerId = customer is { } index && index < customers.Count ? customers[index].Id : (Guid?)null;
            var placedAt = now.AddDays(-daysAgo).AddHours(-1 - picks.Length);
            var order = Order.Place(
                Guid.CreateVersion7(), customerId, [.. picks.Select(pick => (items[pick.Item], pick.Quantity))], reward: null,
                "Demo", placedAt).Value;
            db.Orders.Add(order);
            if (customerId is { } id)
            {
                db.Publish(new OrderPlaced(tenantId, order.Id, id, order.ToOrderedItems(), order.Total, placedAt));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
