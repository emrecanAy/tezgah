using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Modules;
using Tezgah.Core.Results;
using Tezgah.Core.Web;
using Tezgah.Platform.Persistence;
using Tezgah.Platform.Team;

namespace Tezgah.Platform.Auth;

internal static class AuthEndpoints
{
    /// <summary>
    /// The header every cookie-carrying auth request must send. A form on another site cannot set a
    /// custom header, and a script on another site would need a CORS preflight this API never grants,
    /// so a request with it came from Tezgâh's own pages. SameSite=Strict on the cookie is the first wall.
    /// </summary>
    public const string ClientHeader = "X-Requested-With";
    public const string ClientHeaderValue = "tezgah";

    public const string SignInRateLimit = "sign-in";

    public sealed record SignInRequest(string? Email, string? Password);

    /// <summary>Either a session, or a ticket for the second step when the user has two-step sign-in.</summary>
    public sealed record SignInResponse(SessionResponse? Session, string? TwoStepTicket);

    public sealed record SecondStepRequest(string? Ticket, string? Code);

    public sealed record MeResponse(
        UserResponse User, CompanyResponse? Company, IReadOnlyList<ModuleResponse> Modules, IReadOnlyList<string> Permissions);

    public sealed record UserResponse(Guid Id, string FullName, string Email, string Role, bool TwoStepEnabled, bool MustChangePassword);

    public sealed record CompanyResponse(Guid Id, string Name, string Sector);

    public sealed record ModuleResponse(string Key, string Name, string Description);

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").AddEndpointFilter(RequireClientHeader);

        auth.MapPost("/sign-in", SignInAsync).AllowAnonymous().RequireRateLimiting(SignInRateLimit);
        auth.MapPost("/sign-in/second-step", SecondStepAsync).AllowAnonymous().RequireRateLimiting(SignInRateLimit);
        auth.MapPost("/refresh", async (HttpContext http, Sessions sessions, CancellationToken ct) =>
            (await sessions.RefreshAsync(http, ct)).ToHttp()).AllowAnonymous();
        auth.MapPost("/sign-out", async (HttpContext http, Sessions sessions, CancellationToken ct) =>
        {
            await sessions.EndAsync(http, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        api.MapGet("/me", MeAsync).RequireAuthorization().WithTags("Auth");
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request, HttpContext http, SignIn signIn, Sessions sessions, AccessTokens tokens, PlatformDbContext db,
        CancellationToken ct)
    {
        var user = await signIn.CheckPasswordAsync(request.Email, request.Password, ct);
        if (!user.IsSuccess)
        {
            return user.Error.ToProblem();
        }

        if (user.Value.TwoStepEnabled)
        {
            await db.SaveChangesAsync(ct);
            return Results.Ok(new SignInResponse(null, tokens.IssueTwoStepTicket(user.Value)));
        }

        return Results.Ok(new SignInResponse(await sessions.StartAsync(user.Value, http, ct), null));
    }

    private static async Task<IResult> SecondStepAsync(
        SecondStepRequest request, HttpContext http, SignIn signIn, Sessions sessions, AccessTokens tokens, PlatformDbContext db,
        CancellationToken ct)
    {
        var ticket = await tokens.ReadTwoStepTicketAsync(request.Ticket);
        var user = ticket is null ? null : await db.Users.SingleOrDefaultAsync(u => u.Id == ticket.Value.UserId, ct);
        if (user is null || !user.IsActive || user.SessionVersion != ticket!.Value.SessionVersion || !user.TwoStepEnabled)
        {
            return Error.Unauthorized("ticket_expired", "The sign-in took too long. Start again with the password.").ToProblem();
        }

        var checkedUser = await signIn.CheckSecondStepAsync(user, request.Code, ct);
        return checkedUser.IsSuccess
            ? Results.Ok(new SignInResponse(await sessions.StartAsync(user, http, ct), null))
            : checkedUser.Error.ToProblem();
    }

    /// <summary>Who is signed in, which modules their company has, and what they may do in them.</summary>
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
        var modules = catalog.Modules.Where(module => enabled.Contains(module.Info.Key)).ToList();
        var permissions = modules
            .SelectMany(module => module.Permissions)
            .Append(TeamPermissions.Manage)
            .Where(permission => permission.IsGrantedTo(principal))
            .Select(permission => permission.Key)
            .ToList();

        return Results.Ok(new MeResponse(
            new UserResponse(user.Id, user.FullName, user.Email, user.Role, user.TwoStepEnabled, user.MustChangePassword),
            tenant is null ? null : new CompanyResponse(tenant.Id, tenant.Name, tenant.Sector),
            [.. modules.Select(module => new ModuleResponse(module.Info.Key, module.Info.Name, module.Info.Description))],
            permissions));
    }

    private static async ValueTask<object?> RequireClientHeader(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers[ClientHeader] == ClientHeaderValue
            ? await next(context)
            : Error.Forbidden("client_header_missing", $"Auth requests must carry '{ClientHeader}: {ClientHeaderValue}'.").ToProblem();
}
