using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Results;
using Tezgah.Core.Web;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Auth;

/// <summary>What every signed-in user manages for themselves: password, devices, two-step sign-in.</summary>
internal static class AccountEndpoints
{
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record SessionInfo(Guid Id, string? Device, DateTimeOffset StartedAt, DateTimeOffset LastUsedAt, bool IsCurrent);

    public sealed record TwoStepSetup(string Secret, string SetupUri);

    public sealed record CodeRequest(string? Code);

    public sealed record DisableTwoStepRequest(string? Password, string? Code);

    public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

    public static void Map(IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("/account").WithTags("Account");

        // The one thing a user with a temporary password may do.
        account.MapPost("/password", ChangePasswordAsync).RequireAuthorization();

        var signedIn = account.MapGroup("").RequireAuthorization(Policies.PasswordSet);
        signedIn.MapGet("/sessions", ListSessionsAsync);
        signedIn.MapDelete("/sessions/{sessionId:guid}", EndSessionAsync);
        signedIn.MapPost("/two-step/setup", BeginTwoStepAsync);
        signedIn.MapPost("/two-step/enable", EnableTwoStepAsync);
        signedIn.MapPost("/two-step/disable", DisableTwoStepAsync);
    }

    /// <summary>
    /// Every other device is signed out, and this one gets a fresh session: whoever knew the old
    /// password is locked out the moment the new one is saved.
    /// </summary>
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, ClaimsPrincipal principal, HttpContext http, PlatformDbContext db,
        IPasswordHasher<User> hasher, Sessions sessions, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == principal.GetUserId(), ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
        {
            return Error.Validation("wrong_password", "The current password is wrong.").ToProblem();
        }

        var policy = PasswordPolicy.Check(request.NewPassword, user.Email);
        if (!policy.IsSuccess)
        {
            return policy.Error.ToProblem();
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Error.Validation("same_password", "Choose a password different from the current one.").ToProblem();
        }

        user.SetPassword(hasher.HashPassword(user, request.NewPassword!));
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, ct);
        return Results.Ok(await sessions.StartAsync(user, http, ct));
    }

    private static async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(
        ClaimsPrincipal principal, HttpContext http, PlatformDbContext db, Sessions sessions, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var now = time.GetUtcNow();
        var current = await sessions.CurrentFamilyAsync(http, ct);
        var live = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ReplacedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return live.ConvertAll(t => new SessionInfo(t.FamilyId, t.Device, t.FamilyStartedAt, t.CreatedAt, t.FamilyId == current));
    }

    private static async Task<IResult> EndSessionAsync(
        Guid sessionId, ClaimsPrincipal principal, PlatformDbContext db, Sessions sessions, CancellationToken ct)
    {
        // Only one's own sessions: an id from someone else's list is simply not found.
        if (!await db.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.UserId == principal.GetUserId(), ct))
        {
            return Error.NotFound("session_not_found", "This session does not exist.").ToProblem();
        }

        await sessions.RevokeFamilyAsync(sessionId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BeginTwoStepAsync(
        ClaimsPrincipal principal, PlatformDbContext db, Secrets secrets, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == principal.GetUserId(), ct);
        if (user.TwoStepEnabled)
        {
            return Error.Conflict("two_step_already_enabled", "Two-step sign-in is already on.").ToProblem();
        }

        var secret = Totp.NewSecret();
        user.BeginTwoStepSetup(secrets.Protect(secret));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TwoStepSetup(secret, Totp.SetupUri(secret, user.Email)));
    }

    /// <summary>Switches two-step sign-in on once the app proves it has the secret, and hands out recovery codes once.</summary>
    private static async Task<IResult> EnableTwoStepAsync(
        CodeRequest request, ClaimsPrincipal principal, PlatformDbContext db, Secrets secrets, SessionCheck check,
        TimeProvider time, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == principal.GetUserId(), ct);
        if (user.PendingTotpSecret is null)
        {
            return Error.Conflict("two_step_not_started", "Start the setup first.").ToProblem();
        }

        if (Totp.Match(secrets.Unprotect(user.PendingTotpSecret), request.Code?.Trim(), time.GetUtcNow()) is not { } step)
        {
            return Error.Validation("invalid_code", "The code is wrong. Check the time on the phone and try the next code.").ToProblem();
        }

        user.EnableTwoStep(step);
        var codes = Enumerable.Range(0, 10).Select(_ => NewRecoveryCode()).ToList();
        await db.RecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        db.RecoveryCodes.AddRange(codes.Select(code => RecoveryCode.For(user.Id, Secrets.Hash(code))));
        await db.SaveChangesAsync(ct);
        check.Forget(user.Id);

        return Results.Ok(new RecoveryCodes(codes));
    }

    private static async Task<IResult> DisableTwoStepAsync(
        DisableTwoStepRequest request, ClaimsPrincipal principal, PlatformDbContext db, IPasswordHasher<User> hasher,
        SignIn signIn, SessionCheck check, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == principal.GetUserId(), ct);
        if (!user.TwoStepEnabled)
        {
            return Error.Conflict("two_step_not_enabled", "Two-step sign-in is already off.").ToProblem();
        }

        // Both factors: a stolen laptop with an open session is not enough to switch it off.
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return Error.Validation("wrong_password", "The current password is wrong.").ToProblem();
        }

        var code = await signIn.CheckSecondStepAsync(user, request.Code, ct);
        if (!code.IsSuccess)
        {
            return code.Error.ToProblem();
        }

        user.DisableTwoStep();
        await db.RecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        check.Forget(user.Id);
        return Results.NoContent();
    }

    /// <summary>Eight characters from a 32-letter alphabet, as "ABCD-EFGH": 40 bits, single use.</summary>
    private static string NewRecoveryCode()
    {
        var raw = System.Security.Cryptography.RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 8);
        return $"{raw[..4]}-{raw[4..]}";
    }
}
