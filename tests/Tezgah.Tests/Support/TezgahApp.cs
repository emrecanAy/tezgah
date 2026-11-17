using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Tenancy;
using Tezgah.Platform.Admin;
using Tezgah.Platform.Auth;
using Testcontainers.PostgreSql;

namespace Tezgah.Tests.Support;

/// <summary>
/// The whole application in-process against a real PostgreSQL. Tenant filters, the exclusion
/// constraint and row locks are what is being tested, so the database is not replaced by a fake.
/// </summary>
public sealed class TezgahApp : IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "correct-horse";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            host.UseSetting("Auth:SigningKey", "integration-tests-signing-key-0123456789abcdef");
            host.UseSetting("Database:MigrateOnStartup", "true");
            host.UseSetting("Platform:AdminEmail", AdminEmail);
            host.UseSetting("Platform:AdminPassword", Password);
            host.UseSetting("Platform:SeedDemoData", "false");
            host.UseSetting("Outbox:PollInterval", "00:00:00.100");
        });

        // Forces the host to start, which applies the migrations and creates the administrator.
        _factory.CreateClient().Dispose();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient Anonymous() => _factory.CreateClient();

    public async Task<HttpClient> SignInAsync(string email, string password = Password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    public Task<HttpClient> SignInAsAdminAsync() => SignInAsync(AdminEmail);

    /// <summary>Opens a new company with the given modules and signs in as its owner.</summary>
    public async Task<Company> OpenCompanyAsync(params string[] modules)
    {
        var admin = await SignInAsAdminAsync();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.local";
        var response = await admin.PostAsJsonAsync("/api/admin/companies", new
        {
            name = $"Company {Guid.NewGuid():N}",
            sector = "Test",
            ownerFullName = "Test Owner",
            ownerEmail,
            ownerPassword = Password,
            modules,
        });
        response.EnsureSuccessStatusCode();
        var company = await response.Content.ReadFromJsonAsync<AdminEndpoints.CompanyResponse>();

        return new Company(company!.Id, await SignInAsync(ownerEmail));
    }

    /// <summary>A service scope acting as the given company, for looking straight into a module's database.</summary>
    public AsyncServiceScope ScopeFor(Guid companyId)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId);
        return scope;
    }
}

/// <param name="Api">An HTTP client signed in as the company's owner.</param>
public sealed record Company(Guid Id, HttpClient Api);

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<TezgahApp>
{
    public const string Name = "app";
}
