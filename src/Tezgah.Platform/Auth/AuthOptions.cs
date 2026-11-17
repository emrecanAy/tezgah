using System.ComponentModel.DataAnnotations;

namespace Tezgah.Platform.Auth;

public sealed class AuthOptions
{
    public const string Issuer = "tezgah";

    /// <summary>HMAC key that signs access tokens. Supplied per environment; there is no default.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(12);
}
