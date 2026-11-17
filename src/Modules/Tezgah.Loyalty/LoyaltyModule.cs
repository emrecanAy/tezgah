using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Modules;
using Tezgah.Core.Security;
using Tezgah.Core.Persistence;

namespace Tezgah.Loyalty;

public sealed class LoyaltyModule : IModule
{
    public const string Key = "loyalty";

    public ModuleInfo Info { get; } = new(
        Key, "Sadakat", "Damga kartları: randevu ve siparişlerden otomatik damga, belirli sayıda alışverişe bir ödül.",
        DependsOn: "customers");

    public IReadOnlyList<Permission> Permissions => LoyaltyPermissions.All;

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<LoyaltyDbContext>(Key);
        services.AddScoped<CardDesk>();
        services.AddScoped<ILoyaltyRewards, LoyaltyRewards>();
        services.AddIntegrationEventHandler<AppointmentCompleted, AppointmentCompletedHandler>(Key);
        services.AddIntegrationEventHandler<OrderPlaced, OrderPlacedHandler>(Key);
        services.AddIntegrationEventHandler<OrderCancelled, OrderCancelledHandler>(Key);
    }

    public void MapEndpoints(RouteGroupBuilder api) => LoyaltyEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<LoyaltyDbContext>().Database.MigrateAsync(ct);

    /// <summary>
    /// Only the programs. The cards fill up from the demo's appointments and orders, the same way
    /// they do in real use.
    /// </summary>
    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        (string Name, int Stamps, string Reward, EarnRule Rule, string? Category) program = business switch
        {
            DemoBusiness.Hairdresser => ("Saç Bakım Kartı", 4, "1 saç kesimi hediye", EarnRule.Appointment, null),
            DemoBusiness.Cafe => ("Kahve Kartı", 4, "1 kahve bizden", EarnRule.Item, "Kahve"),
            _ => ("Sadakat Kartı", 5, "Bir sonraki alışverişte 1 ürün hediye", EarnRule.Order, null),
        };

        var db = services.GetRequiredService<LoyaltyDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var (name, stamps, reward, rule, category) = program;
        db.Programs.Add(LoyaltyProgram.Create(name, stamps, reward, rule, category, now).Value);
        await db.SaveChangesAsync(ct);
    }
}
