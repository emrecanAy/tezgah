using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;

namespace Tezgah.Loyalty;

public sealed class LoyaltyModule : IModule
{
    public const string Key = "loyalty";

    public ModuleInfo Info { get; } = new(
        Key, "Sadakat", "Damga kartları: belirli sayıda alışverişe bir ödül.", DependsOn: "customers");

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<LoyaltyDbContext>(Key);
        services.AddScoped<CardDesk>();
        services.AddIntegrationEventHandler<AppointmentCompleted, AppointmentCompletedHandler>(Key);
    }

    public void MapEndpoints(RouteGroupBuilder api) => LoyaltyEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<LoyaltyDbContext>().Database.MigrateAsync(ct);

    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        var (name, stamps, reward, onAppointment) = business switch
        {
            DemoBusiness.Hairdresser => ("Saç Bakım Kartı", 4, "1 saç kesimi hediye", true),
            DemoBusiness.Cafe => ("Kahve Kartı", 4, "1 kahve bizden", false),
            _ => ("Sadakat Kartı", 5, "Bir sonraki alışverişte %10 indirim", false),
        };

        var db = services.GetRequiredService<LoyaltyDbContext>();
        var program = LoyaltyProgram.Create(name, stamps, reward, onAppointment, services.GetRequiredService<TimeProvider>().GetUtcNow()).Value;
        db.Programs.Add(program);
        await db.SaveChangesAsync(ct);

        // Give the first customers cards at different stages, including one with a reward waiting.
        var desk = services.GetRequiredService<CardDesk>();
        var customers = await services.GetRequiredService<ICustomerDirectory>().ListAsync(6, ct);
        for (var i = 0; i < customers.Count; i++)
        {
            for (var stamp = 0; stamp < (i == 0 ? stamps + 1 : Math.Min(i, stamps - 1)); stamp++)
            {
                await desk.StampAsync(program.Id, customers[i].Id, note: null, source: null, "Demo", ct);
            }
        }
    }
}
