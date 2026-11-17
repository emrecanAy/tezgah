namespace Tezgah.Platform.Domain;

/// <summary>
/// One link in a sign-in session's chain of refresh tokens. Every refresh replaces the token with a
/// new one in the same family; presenting a token that was already replaced means it was copied, so
/// the whole family is revoked. Only a hash of the token is stored.
/// </summary>
internal sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The sign-in session: one device, from sign-in to sign-out.</summary>
    public Guid FamilyId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the token. The token itself only ever exists in the user's cookie.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>The user's session version when the family started; a newer version ends the family.</summary>
    public int SessionVersion { get; private set; }

    /// <summary>The browser that signed in, for the user's list of sessions.</summary>
    public string? Device { get; private set; }

    public DateTimeOffset FamilyStartedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the token was exchanged for its successor.</summary>
    public DateTimeOffset? ReplacedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken StartFamily(
        Guid userId, int sessionVersion, string tokenHash, string? device, DateTimeOffset now, TimeSpan lifetime)
    {
        var id = Guid.CreateVersion7();
        return new RefreshToken
        {
            Id = id,
            FamilyId = id,
            UserId = userId,
            TokenHash = tokenHash,
            SessionVersion = sessionVersion,
            Device = device is null ? null : device[..Math.Min(device.Length, MaxDeviceLength)],
            FamilyStartedAt = now,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public RefreshToken Successor(string tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.CreateVersion7(),
        FamilyId = FamilyId,
        UserId = UserId,
        TokenHash = tokenHash,
        SessionVersion = SessionVersion,
        Device = Device,
        FamilyStartedAt = FamilyStartedAt,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };

    public const int MaxDeviceLength = 200;
}

/// <summary>
/// Failed sign-ins per e-mail address, whether or not an account exists for it, so a locked address
/// tells an attacker nothing about which accounts are real.
/// </summary>
internal sealed class SignInThrottle
{
    private SignInThrottle()
    {
    }

    /// <summary>The normalised e-mail address.</summary>
    public string Email { get; private set; } = null!;

    public int Failures { get; private set; }

    public DateTimeOffset WindowStartedAt { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public static SignInThrottle For(string email, DateTimeOffset now) => new() { Email = email, WindowStartedAt = now };

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public void RecordFailure(DateTimeOffset now, int threshold, TimeSpan window, TimeSpan lockout)
    {
        if (now - WindowStartedAt > window)
        {
            WindowStartedAt = now;
            Failures = 0;
        }

        Failures++;
        if (Failures >= threshold)
        {
            LockedUntil = now + lockout;
            Failures = 0;
            WindowStartedAt = now;
        }
    }
}

/// <summary>A single-use code that signs in when the authenticator app is out of reach. Stored hashed.</summary>
internal sealed class RecoveryCode
{
    private RecoveryCode()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset? UsedAt { get; private set; }

    public static RecoveryCode For(Guid userId, string codeHash) =>
        new() { Id = Guid.CreateVersion7(), UserId = userId, CodeHash = codeHash };
}
