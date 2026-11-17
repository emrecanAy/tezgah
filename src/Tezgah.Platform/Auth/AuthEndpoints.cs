using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Modules;
using Tezgah.Core.Results;
using Tezgah.Core.Web;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Auth;

internal static class AuthEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

    public sealed record MeResponse(UserResponse User, CompanyResponse? Company, IReadOnlyList<ModuleResponse> Modules);

    public sealed record UserResponse(Guid Id, string FullName, string Email, string Role);

    public sealed record CompanyResponse(Guid Id, string Name, string Sector);

    public sealed record ModuleResponse(string Key, string Name, string Description);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/auth/login", LoginAsync).WithTags("Auth");
        api.MapGet("/me", MeAsync).RequireAuthorization().WithTags("Auth");
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, PlatformDbContext db, IPasswordHasher<User> hasher, AccessTokens tokens, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email ?? "");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // One answer for "no such user" and "wrong password", so the endpoint cannot be used to find accounts.
        if (user is null
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return Error.Unauthorized("invalid_credentials", "The e-mail address or password is wrong.").ToProblem();
        }

        var (token, expiresAt) = tokens.Issue(user);
        return Results.Ok(new LoginResponse(token, expiresAt));
    }

    /// <summary>Everything the client needs to build its navigation: who is signed in and which modules they have.</summary>
    private static async Task<IResult> MeAsync(
        ClaimsPrincipal principal, PlatformDbContext db, ModuleCatalog catalog, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Error.Unauthorized("unknown_user", "This account no longer exists.").ToProblem();
        }

        var tenant = user.TenantId is null
            ? null
            : await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == user.TenantId, ct);

        var enabled = tenant?.Modules.Select(module => module.ModuleKey).ToHashSet() ?? [];
        var modules = catalog.Modules
            .Where(module => enabled.Contains(module.Info.Key))
            .Select(module => new ModuleResponse(module.Info.Key, module.Info.Name, module.Info.Description))
            .ToList();

        return Results.Ok(new MeResponse(
            new UserResponse(user.Id, user.FullName, user.Email, user.Role),
            tenant is null ? null : new CompanyResponse(tenant.Id, tenant.Name, tenant.Sector),
            modules));
    }
}
