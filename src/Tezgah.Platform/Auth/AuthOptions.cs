using System.ComponentModel.DataAnnotations;

namespace Tezgah.Platform.Auth;

public sealed class AuthOptions
{
    public const string Issuer = "tezgah";

    /// <summary>The only audience an access token is accepted for.</summary>
    public const string ApiAudience = "tezgah-api";

    /// <summary>The audience of the short ticket between the password and the authenticator code; never accepted by the API.</summary>
    public const string TwoStepAudience = "tezgah-two-step";

    /// <summary>HMAC key that signs access tokens. Supplied per environment; there is no default.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    /// <summary>Key that encrypts authenticator secrets at rest. Separate from the signing key; no default.</summary>
    [Required, MinLength(32)]
    public string EncryptionKey { get; set; } = null!;

    /// <summary>Short on purpose: a leaked access token is useful for minutes, not days.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a device stays signed in without being used.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The longest a sign-in lasts however often it is used; after that the password is asked again.</summary>
    public TimeSpan SessionMaxAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Send the refresh cookie only over HTTPS. On by default; switched off only for a local run over
    /// plain HTTP (some browsers do not treat http://localhost as secure).
    /// </summary>
    public bool SecureCookies { get; set; } = true;

    /// <summary>Failed sign-ins for one e-mail address before it is locked for <see cref="LockoutDuration"/>.</summary>
    public int LockoutThreshold { get; set; } = 5;

    public TimeSpan LockoutWindow { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sign-in requests one IP address may make per minute, whatever the e-mail addresses.</summary>
    public int SignInRequestsPerMinute { get; set; } = 10;

    public string RefreshCookieName => SecureCookies ? "__Secure-tezgah-refresh" : "tezgah-refresh";
}
