using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Tezgah.Api;
using Tezgah.Appointments;
using Tezgah.Core.Modules;
using Tezgah.Customers;
using Tezgah.Fleet;
using Tezgah.Inventory;
using Tezgah.Loyalty;
using Tezgah.Orders;
using Tezgah.Platform;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// The one place that knows every module. "Platform:Modules" narrows an installation to a subset,
// e.g. ["fleet"] for a customer who bought only fleet tracking; left out, everything is shipped.
var catalog = ModuleCatalog.Create(
    [
        new AppointmentsModule(), new CustomersModule(), new FleetModule(), new InventoryModule(), new LoyaltyModule(),
        new OrdersModule(),
    ],
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

// Behind the web container's nginx the client address arrives in X-Forwarded-For. It is believed only
// from the networks listed here, so a client talking to the API directly cannot pick its own address
// and slip past the sign-in rate limit.
services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (http, next) =>
{
    // An API answers with data, never with something to render or frame; nothing it says is cached.
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.CacheControl = "no-store";
    headers["Referrer-Policy"] = "no-referrer";
    await next(http);
});
app.UseAuthentication();
app.UseTenantContext();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference("/docs").AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

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
