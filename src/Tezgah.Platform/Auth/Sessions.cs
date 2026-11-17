using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tezgah.Core.Results;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Auth;

/// <summary>What the client receives after any successful sign-in or refresh.</summary>
/// <param name="MustChangePassword">The token is good for changing the password and nothing else.</param>
public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, bool MustChangePassword);

/// <summary>
/// Sign-in sessions. The access token goes to the client in the response body and lives only in
/// memory there; the refresh token goes in an HttpOnly, SameSite=Strict cookie that page scripts
/// cannot read and other sites cannot make the browser send.
/// </summary>
internal sealed class Sessions(
    PlatformDbContext db, AccessTokens tokens, SessionCheck check, IOptions<AuthOptions> options, TimeProvider time,
    ILogger<Sessions> logger)
{
    private AuthOptions Options => options.Value;

    /// <summary>Starts a new session for this device and sets its cookie.</summary>
    public async Task<SessionResponse> StartAsync(User user, HttpContext http, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = Secrets.NewToken();
        db.RefreshTokens.Add(RefreshToken.StartFamily(
            user.Id, user.SessionVersion, Secrets.Hash(token), http.Request.Headers.UserAgent.ToString(), now,
            Options.RefreshTokenLifetime));
        user.RecordSignIn(now);
        await db.SaveChangesAsync(ct);
        check.Forget(user.Id);

        SetCookie(http, token, now + Options.RefreshTokenLifetime);
        return Respond(user);
    }

    /// <summary>
    /// Exchanges the cookie's refresh token for a new one and a fresh access token. A token that was
    /// already exchanged has been copied: the whole session is ended, for the thief and the owner alike.
    /// </summary>
    public async Task<Result<SessionResponse>> RefreshAsync(HttpContext http, CancellationToken ct)
    {
        var presented = http.Request.Cookies[Options.RefreshCookieName];
        if (string.IsNullOrEmpty(presented))
        {
            return SignedOut(http);
        }

        var now = time.GetUtcNow();
        var hash = Secrets.Hash(presented);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now
            || now - token.FamilyStartedAt > Options.SessionMaxAge)
        {
            return SignedOut(http);
        }

        // Claim the token: of two requests presenting it, only one can flip "replaced_at".
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.ReplacedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ReplacedAt, now), ct);
        if (claimed == 0)
        {
            logger.LogWarning("Refresh token reuse for user {UserId}; ending session {FamilyId}", token.UserId, token.FamilyId);
            await RevokeFamilyAsync(token.FamilyId, ct);
            return SignedOut(http, "session_revoked");
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.IsActive || user.SessionVersion != token.SessionVersion)
        {
            await RevokeFamilyAsync(token.FamilyId, ct);
            return SignedOut(http);
        }

        var next = Secrets.NewToken();
        db.RefreshTokens.Add(token.Successor(Secrets.Hash(next), now, Options.RefreshTokenLifetime));
        await db.SaveChangesAsync(ct);

        SetCookie(http, next, now + Options.RefreshTokenLifetime);
        return Respond(user);
    }

    /// <summary>Ends the session the cookie belongs to. Safe to call without one.</summary>
    public async Task EndAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies[Options.RefreshCookieName] is { Length: > 0 } presented)
        {
            var hash = Secrets.Hash(presented);
            var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(ct);
            if (familyId is not null)
            {
                await RevokeFamilyAsync(familyId.Value, ct);
            }
        }

        ClearCookie(http);
    }

    /// <summary>The family of the session the request's cookie belongs to, to mark "this device" in the list.</summary>
    public async Task<Guid?> CurrentFamilyAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies[Options.RefreshCookieName] is not { Length: > 0 } presented)
        {
            return null;
        }

        var hash = Secrets.Hash(presented);
        return await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(ct);
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>Signs the user out everywhere: after a password change, a role change, a deactivation.</summary>
    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
        check.Forget(userId);
    }

    private SessionResponse Respond(User user)
    {
        var (accessToken, expiresAt) = tokens.Issue(user);
        return new SessionResponse(accessToken, expiresAt, user.MustChangePassword);
    }

    private Error SignedOut(HttpContext http, string code = "signed_out")
    {
        ClearCookie(http);
        return Error.Unauthorized(code, "The session has ended. Sign in again.");
    }

    private void SetCookie(HttpContext http, string token, DateTimeOffset expires) =>
        http.Response.Cookies.Append(Options.RefreshCookieName, token, CookieOptions(expires));

    private void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(Options.RefreshCookieName, CookieOptions(expires: null));

    /// <summary>Only the auth endpoints ever receive the cookie; no other request carries it.</summary>
    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Options.SecureCookies,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true,
    };
}
