using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;

namespace Tezgah.Fleet;

public sealed class FleetModule : IModule
{
    public const string Key = "fleet";

    public ModuleInfo Info { get; } = new(Key, "Filo", "Araçlar, yakıt ve bakım kayıtları, muayene ve sigorta hatırlatmaları.");

    public void AddServices(IServiceCollection services) => services.AddModuleDbContext<FleetDbContext>(Key);

    public void MapEndpoints(RouteGroupBuilder api) => FleetEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<FleetDbContext>().Database.MigrateAsync(ct);

    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        var db = services.GetRequiredService<FleetDbContext>();
        var today = DateOnly.FromDateTime(services.GetRequiredService<TimeProvider>().GetUtcNow().AddHours(3).DateTime);

        // One vehicle overdue, one due soon, the rest in order: every alert state is on screen.
        (VehicleDetails Details, int Fills)[] fleet =
        [
            (new("16 YLD 101", "Ford", "Transit", 2021, 148_200, "Serkan Uçar", today.AddDays(12), today.AddDays(140), true), 4),
            (new("16 YLD 102", "Mercedes-Benz", "Sprinter", 2020, 203_750, "Halil Ersoy", today.AddDays(-6), today.AddDays(75), true), 5),
            (new("16 YLD 103", "Fiat", "Doblo Cargo", 2022, 86_400, "Nurcan Tek", today.AddDays(210), today.AddDays(21), true), 3),
            (new("34 YLD 204", "Renault", "Master", 2019, 261_900, null, today.AddDays(95), today.AddDays(180), true), 0),
        ];

        foreach (var (details, fills) in fleet)
        {
            var vehicle = Vehicle.Register(details with { OdometerKm = details.OdometerKm - fills * 620 }).Value;
            db.Vehicles.Add(vehicle);

            for (var fill = 1; fill <= fills; fill++)
            {
                var odometer = vehicle.OdometerKm + 620;
                vehicle.RecordOdometer(odometer);
                db.Fuel.Add(FuelEntry.Log(vehicle.Id, today.AddDays(-7 * (fills - fill)), 58.5m + fill, 2_580m + 44 * fill, odometer));
            }

            db.Maintenance.Add(MaintenanceRecord.Log(
                vehicle.Id, today.AddDays(-150), "Periyodik bakım: yağ, filtre, fren kontrolü", 7_400m,
                vehicle.OdometerKm - 9_000, today.AddDays(fills == 5 ? 9 : 200)));
        }

        await db.SaveChangesAsync(ct);
    }
}
