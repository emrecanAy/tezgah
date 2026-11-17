using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tezgah.Core.Web;
using Tezgah.Platform.Domain;

namespace Tezgah.Platform.Auth;

/// <summary>Short-lived access tokens, and the five-minute ticket that carries a sign-in from the password to the authenticator code.</summary>
internal sealed class AccessTokens(IOptions<AuthOptions> options, TimeProvider time)
{
    private static readonly TimeSpan TwoStepTicketLifetime = TimeSpan.FromMinutes(5);

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var claims = new List<Claim>
        {
            new(UserClaims.UserId, user.Id.ToString()),
            new(UserClaims.Name, user.FullName),
            new(UserClaims.Role, user.Role),
            new(UserClaims.SessionVersion, user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (user.TenantId is { } tenantId)
        {
            claims.Add(new Claim(UserClaims.TenantId, tenantId.ToString()));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(UserClaims.PasswordChangeRequired, "true"));
        }

        var expiresAt = time.GetUtcNow() + options.Value.AccessTokenLifetime;
        return (Create(claims, AuthOptions.ApiAudience, expiresAt), expiresAt);
    }

    /// <summary>Proof that the password was right, good only for the second step and only for a few minutes.</summary>
    public string IssueTwoStepTicket(User user) => Create(
        [new(UserClaims.UserId, user.Id.ToString()), new(UserClaims.SessionVersion, user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        AuthOptions.TwoStepAudience,
        time.GetUtcNow() + TwoStepTicketLifetime);

    /// <returns>The user id and session version the ticket was issued for, or null if it is not a valid, unexpired ticket.</returns>
    public async Task<(Guid UserId, int SessionVersion)?> ReadTwoStepTicketAsync(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(ticket, Validation(options.Value, AuthOptions.TwoStepAudience));
        if (!result.IsValid
            || !result.Claims.TryGetValue(UserClaims.UserId, out var userClaim)
            || !result.Claims.TryGetValue(UserClaims.SessionVersion, out var versionClaim)
            || !Guid.TryParse(userClaim as string, out var userId)
            || !int.TryParse(versionClaim as string, System.Globalization.CultureInfo.InvariantCulture, out var version))
        {
            return null;
        }

        return (userId, version);
    }

    /// <summary>What a token must satisfy: our issuer, the given audience, HS256 only, not expired.</summary>
    public static TokenValidationParameters Validation(AuthOptions options, string audience) => new()
    {
        ValidIssuer = AuthOptions.Issuer,
        ValidAudience = audience,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        IssuerSigningKey = SigningKey(options),
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = UserClaims.Name,
        RoleClaimType = UserClaims.Role,
    };

    private string Create(IEnumerable<Claim> claims, string audience, DateTimeOffset expiresAt) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = time.GetUtcNow().UtcDateTime,
            NotBefore = time.GetUtcNow().UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(options.Value), SecurityAlgorithms.HmacSha256),
        });

    private static SymmetricSecurityKey SigningKey(AuthOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));
}
