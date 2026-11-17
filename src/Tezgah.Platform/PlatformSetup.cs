using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Tenancy;
using Tezgah.Core.Web;
using Tezgah.Platform.Admin;
using Tezgah.Platform.Auth;
using Tezgah.Platform.Demo;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;
using Tezgah.Platform.Team;

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
        services.AddSingleton<Secrets>();
        services.AddSingleton<SessionCheck>();
        services.AddScoped<Sessions>();
        services.AddScoped<SignIn>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = AccessTokens.Validation(auth.Value, AuthOptions.ApiAudience);
                jwt.Events = new JwtBearerEvents
                {
                    // A valid signature is not enough: the user must still be active and the token must
                    // come from their current session version.
                    OnTokenValidated = async context =>
                    {
                        var check = context.HttpContext.RequestServices.GetRequiredService<SessionCheck>();
                        var user = context.Principal!;
                        if (!await check.IsCurrentAsync(
                                user.FindFirstValue(UserClaims.UserId), user.FindFirstValue(UserClaims.SessionVersion),
                                context.HttpContext.RequestAborted))
                        {
                            context.Fail("The session behind this token has ended.");
                        }
                    },
                };
            });

        // Anything not marked otherwise needs a signed-in user; anonymous endpoints say so explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.PasswordSet, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == UserClaims.PasswordChangeRequired)))
            .AddPolicy(Policies.PlatformAdmin, policy => policy
                .RequireRole(Roles.PlatformAdmin)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == UserClaims.PasswordChangeRequired)))
            .AddPolicy(Policies.TenantUser, policy => policy
                .RequireClaim(UserClaims.TenantId)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == UserClaims.PasswordChangeRequired)));
        services.AddPermissions();

        // Per address, on top of the per-account lockout: one machine cannot try many accounts quickly either.
        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(AuthEndpoints.SignInRateLimit, http =>
            {
                var perMinute = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.SignInRequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Error.TooManyRequests("too_many_requests", "Too many sign-in attempts from this address. Wait a minute.")
                    .ToProblem().ExecuteAsync(context.HttpContext);
            };
        });

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
        AccountEndpoints.Map(api);
        TeamEndpoints.Map(api);
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
        if (PasswordPolicy.Check(options.AdminPassword, email) is { IsSuccess: false } weak)
        {
            throw new InvalidOperationException($"Platform:AdminPassword is not accepted: {weak.Error.Message}");
        }

        if (!await db.Users.AnyAsync(user => user.Email == email, ct))
        {
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
            var admin = User.Create(tenantId: null, email, "Platform Yöneticisi", Roles.PlatformAdmin, now);
            admin.SetPassword(
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
