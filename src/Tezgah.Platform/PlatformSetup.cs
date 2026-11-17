using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;
using Tezgah.Core.Tenancy;
using Tezgah.Core.Web;
using Tezgah.Platform.Admin;
using Tezgah.Platform.Auth;
using Tezgah.Platform.Demo;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform;

public static class PlatformSetup
{
    /// <summary>Companies, users, sign-in and the per-company module switchboard. Always present.</summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services)
    {
        services.AddOptions<PlatformOptions>().BindConfiguration("Platform");
        services.AddOptions<AuthOptions>().BindConfiguration("Auth").ValidateDataAnnotations().ValidateOnStart();

        services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.Schema);
        services.AddMemoryCache();
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantModules>();
        services.AddScoped<ITenantModules>(provider => provider.GetRequiredService<TenantModules>());
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<AccessTokens>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = AuthOptions.Issuer,
                    ValidateAudience = false,
                    IssuerSigningKey = AccessTokens.SigningKey(auth.Value),
                    NameClaimType = UserClaims.Name,
                    RoleClaimType = UserClaims.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.PlatformAdmin, policy => policy.RequireRole(Roles.PlatformAdmin))
            .AddPolicy(Policies.TenantUser, policy => policy.RequireClaim(UserClaims.TenantId));

        return services;
    }

    /// <summary>Puts the signed-in user's company into the request's <see cref="TenantContext"/>.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.User.GetTenantId() is { } tenantId)
            {
                http.RequestServices.GetRequiredService<TenantContext>().Set(tenantId);
            }

            await next(http);
        });

    public static void MapPlatformEndpoints(this IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        AdminEndpoints.Map(api);
    }

    /// <summary>Migrates the platform schema and makes sure the configured administrator exists.</summary>
    public static async Task MigratePlatformAsync(this IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await db.Database.MigrateAsync(ct);

        var options = scope.ServiceProvider.GetRequiredService<IOptions<PlatformOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            return;
        }

        var email = User.NormalizeEmail(options.AdminEmail);
        if (!await db.Users.AnyAsync(user => user.Email == email, ct))
        {
            var admin = User.Create(tenantId: null, email, "Platform Yöneticisi", Roles.PlatformAdmin);
            admin.SetPasswordHash(
                scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(admin, options.AdminPassword));
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task SeedDemoCompaniesAsync(this IServiceProvider services, CancellationToken ct)
    {
        if (services.GetRequiredService<IOptions<PlatformOptions>>().Value.SeedDemoData)
        {
            await DemoCompanies.SeedAsync(services, ct);
        }
    }
}
