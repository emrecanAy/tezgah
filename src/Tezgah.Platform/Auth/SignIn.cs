using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tezgah.Core.Results;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Auth;

/// <summary>
/// Checks passwords and authenticator codes. Every answer is shaped so that it says nothing about
/// whether an account exists: the same error, the same lockout, and the same hashing work for an
/// unknown address as for a wrong password.
/// </summary>
internal sealed class SignIn(
    PlatformDbContext db, IPasswordHasher<User> hasher, Secrets secrets, IOptions<AuthOptions> options, TimeProvider time,
    ILogger<SignIn> logger)
{
    // Verified when the address is unknown, so that case costs as much as a wrong password.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<User>().HashPassword(null!, Secrets.NewToken()));

    public async Task<Result<User>> CheckPasswordAsync(string? email, string? password, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email ?? "");
        var now = time.GetUtcNow();
        if (await IsLockedAsync(normalized, now, ct))
        {
            return Locked();
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);
        var verdict = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash.Value, password ?? "");
        if (user is null || verdict == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Failed sign-in for an address ending {Domain}", normalized.Split('@').LastOrDefault());
            return await FailAsync(normalized, now, ct);
        }

        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.RehashPassword(hasher.HashPassword(user, password!));
        }

        if (!user.IsActive)
        {
            // Only said after the right password, so it reveals nothing to someone guessing.
            return Error.Forbidden("account_disabled", "This account has been deactivated by the company.");
        }

        await ClearAsync(normalized, ct);
        return user;
    }

    /// <summary>The second step: a code from the authenticator app, or one of the recovery codes.</summary>
    public async Task<Result<User>> CheckSecondStepAsync(User user, string? code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (await IsLockedAsync(user.Email, now, ct))
        {
            return Locked();
        }

        if (user.TotpSecret is not null
            && Totp.Match(secrets.Unprotect(user.TotpSecret), code?.Trim(), now) is { } step
            && user.AcceptTotpStep(step))
        {
            await db.SaveChangesAsync(ct);
            await ClearAsync(user.Email, ct);
            return user;
        }

        if (await UseRecoveryCodeAsync(user.Id, code, now, ct))
        {
            logger.LogInformation("User {UserId} signed in with a recovery code", user.Id);
            await ClearAsync(user.Email, ct);
            return user;
        }

        var failed = await FailAsync(user.Email, now, ct);
        return failed.Code == "too_many_attempts"
            ? failed
            : Error.Unauthorized("invalid_code", "The code is wrong or has already been used.");
    }

    private async Task<bool> UseRecoveryCodeAsync(Guid userId, string? code, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = code?.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized is not { Length: 9 })
        {
            return false;
        }

        var hash = Secrets.Hash(normalized);
        // One statement, so a code raced from two tabs is spent once.
        return await db.RecoveryCodes
            .Where(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.UsedAt, now), ct) == 1;
    }

    private async Task<bool> IsLockedAsync(string email, DateTimeOffset now, CancellationToken ct) =>
        await db.SignInThrottles.AnyAsync(t => t.Email == email && t.LockedUntil > now, ct);

    private async Task<Error> FailAsync(string email, DateTimeOffset now, CancellationToken ct)
    {
        var o = options.Value;
        // Row-locked so that parallel guesses cannot each read "4 failures" and all slip under the threshold.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var throttle = await db.SignInThrottles
            .FromSql($"SELECT * FROM platform.sign_in_throttles WHERE email = {email} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (throttle is null)
        {
            throttle = SignInThrottle.For(email, now);
            db.SignInThrottles.Add(throttle);
        }

        throttle.RecordFailure(now, o.LockoutThreshold, o.LockoutWindow, o.LockoutDuration);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first failures for the same address raced to create the row; the other one counted.
        }

        return throttle.IsLocked(now)
            ? Locked()
            : Error.Unauthorized("invalid_credentials", "The e-mail address or password is wrong.");
    }

    private Task ClearAsync(string email, CancellationToken ct) =>
        db.SignInThrottles.Where(t => t.Email == email).ExecuteDeleteAsync(ct);

    private Error Locked() => Error.TooManyRequests(
        "too_many_attempts",
        $"Too many failed attempts. Try again in {(int)options.Value.LockoutDuration.TotalMinutes} minutes.");
}
