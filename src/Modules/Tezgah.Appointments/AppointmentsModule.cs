using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;
using Tezgah.Core.Tenancy;

namespace Tezgah.Appointments;

public sealed class AppointmentsModule : IModule
{
    public const string Key = "appointments";

    public ModuleInfo Info { get; } = new(
        Key, "Randevu", "Hizmetler, personel takvimi ve çakışmayan randevular.", DependsOn: "customers");

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<AppointmentsDbContext>(Key);
        services.AddOutbox<AppointmentsDbContext>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        SetupEndpoints.Map(api);
        AppointmentEndpoints.Map(api);
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<AppointmentsDbContext>().Database.MigrateAsync(ct);

    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppointmentsDbContext>();

        Service[] offered =
        [
            Service.Create("Saç Kesimi", 45, 450m).Value,
            Service.Create("Fön", 30, 250m).Value,
            Service.Create("Saç Boyama", 120, 1800m).Value,
            Service.Create("Sakal Tıraşı", 20, 200m).Value,
        ];
        StaffMember[] team = [StaffMember.Create("Elif").Value, StaffMember.Create("Kerem").Value, StaffMember.Create("Seda").Value];
        db.AddRange(offered);
        db.AddRange(team);

        // A plausible afternoon and a morning tomorrow, in the company's local time (UTC+3).
        var customers = await services.GetRequiredService<ICustomerDirectory>().ListAsync(8, ct);
        var localNow = services.GetRequiredService<TimeProvider>().GetUtcNow().ToOffset(TimeSpan.FromHours(3));
        var nextHour = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0, localNow.Offset).AddHours(1);
        var tomorrow = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 10, 0, 0, localNow.Offset).AddDays(1);

        (DateTimeOffset Start, int Staff, int Service)[] slots =
        [
            (nextHour, 0, 0), (nextHour, 1, 3), (nextHour.AddMinutes(30), 2, 1), (nextHour.AddHours(1), 0, 2),
            (tomorrow, 0, 0), (tomorrow, 1, 0), (tomorrow.AddHours(1), 2, 2), (tomorrow.AddHours(2), 1, 1),
        ];
        for (var i = 0; i < slots.Length && i < customers.Count; i++)
        {
            var (start, staff, service) = slots[i];
            db.Appointments.Add(Appointment.Book(customers[i].Id, offered[service], team[staff].Id, start.ToUniversalTime(), null));
        }

        // Past visits, completed the way the screen completes them, so loyalty cards fill from real history.
        // The first customer has had four haircuts: a full card.
        var tenantId = services.GetRequiredService<TenantContext>().TenantId;
        (int Customer, int DaysAgo, int Service)[] visits =
        [
            (0, 70, 0), (0, 45, 0), (0, 24, 2), (0, 6, 0), (1, 30, 0), (1, 9, 3), (2, 14, 1),
        ];
        foreach (var (customer, daysAgo, service) in visits.Where(visit => visit.Customer < customers.Count))
        {
            var start = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 14, 0, 0, localNow.Offset).AddDays(-daysAgo);
            var visit = Appointment.Book(
                customers[customer].Id, offered[service], team[daysAgo % team.Length].Id, start.ToUniversalTime(), null);
            visit.Complete();
            db.Appointments.Add(visit);
            db.Publish(new AppointmentCompleted(tenantId, visit.Id, visit.CustomerId, offered[service].Name, visit.EndsAt));
        }

        await db.SaveChangesAsync(ct);
    }
}
