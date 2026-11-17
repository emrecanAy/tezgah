using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tezgah.Core.Web;
using Tezgah.Platform.Domain;

namespace Tezgah.Platform.Auth;

internal sealed class AccessTokens(IOptions<AuthOptions> options, TimeProvider time)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var claims = new List<Claim>
        {
            new(UserClaims.UserId, user.Id.ToString()),
            new(UserClaims.Name, user.FullName),
            new(UserClaims.Role, user.Role),
        };
        if (user.TenantId is { } tenantId)
        {
            claims.Add(new Claim(UserClaims.TenantId, tenantId.ToString()));
        }

        var expiresAt = time.GetUtcNow() + options.Value.TokenLifetime;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(options.Value), SecurityAlgorithms.HmacSha256),
        });

        return (token, expiresAt);
    }

    public static SymmetricSecurityKey SigningKey(AuthOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));
}
