using Scalar.AspNetCore;
using Tezgah.Api;
using Tezgah.Appointments;
using Tezgah.Core.Modules;
using Tezgah.Customers;
using Tezgah.Fleet;
using Tezgah.Inventory;
using Tezgah.Loyalty;
using Tezgah.Platform;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// The one place that knows every module. "Platform:Modules" narrows an installation to a subset,
// e.g. ["fleet"] for a customer who bought only fleet tracking; left out, everything is shipped.
var catalog = ModuleCatalog.Create(
    [new AppointmentsModule(), new CustomersModule(), new FleetModule(), new InventoryModule(), new LoyaltyModule()],
    builder.Configuration.GetSection("Platform:Modules").Get<string[]>());

services.AddProblemDetails();
services.AddExceptionHandler<ConcurrencyConflictHandler>();
services.AddOpenApi();
services.AddHealthChecks();
services.AddSingleton(TimeProvider.System);
services.AddSingleton(catalog);

services.AddPlatform();
foreach (var module in catalog.Modules)
{
    module.AddServices(services);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseTenantContext();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/docs");
app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapPlatformEndpoints();
api.MapModules(catalog);

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigratePlatformAsync(CancellationToken.None);
    foreach (var module in catalog.Modules)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await module.MigrateAsync(scope.ServiceProvider, CancellationToken.None);
    }

    await app.Services.SeedDemoCompaniesAsync(CancellationToken.None);
}

app.Run();
