using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;

namespace Tezgah.Customers;

public sealed class CustomersModule : IModule
{
    public const string Key = "customers";

    public ModuleInfo Info { get; } = new(Key, "Müşteriler", "Müşteri kayıtları ve iletişim bilgileri.");

    public void AddServices(IServiceCollection services)
    {
        services.AddModuleDbContext<CustomersDbContext>(Key);
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();
    }

    public void MapEndpoints(RouteGroupBuilder api) => CustomerEndpoints.Map(api);

    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<CustomersDbContext>().Database.MigrateAsync(ct);

    public async Task SeedDemoAsync(IServiceProvider services, DemoBusiness business, CancellationToken ct)
    {
        (string Name, string Phone)[] people =
        [
            ("Ayşe Yılmaz", "0532 410 22 31"), ("Mehmet Kaya", "0533 287 64 10"), ("Zeynep Arslan", "0541 903 17 58"),
            ("Can Öztürk", "0555 612 40 77"), ("Selin Doğan", "0544 730 95 12"), ("Burak Şahin", "0536 158 83 46"),
            ("Derya Çelik", "0542 369 21 05"), ("Emre Aydın", "0531 844 59 60"), ("Gizem Koç", "0553 276 08 93"),
            ("Oğuz Polat", "0545 501 37 24"),
        ];

        var db = services.GetRequiredService<CustomersDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        for (var i = 0; i < people.Length; i++)
        {
            // Spread over the past weeks so "new this month" shows a believable number.
            db.Customers.Add(Customer.Create(people[i].Name, people[i].Phone, null, null, now.AddDays(-6 * i)).Value);
        }

        await db.SaveChangesAsync(ct);
    }
}
