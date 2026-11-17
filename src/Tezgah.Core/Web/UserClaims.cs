using System.Security.Claims;

namespace Tezgah.Core.Web;

/// <summary>The claims a Tezgâh access token carries, and how to read them.</summary>
public static class UserClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";

    /// <summary>The company the user belongs to. Platform administrators have no tenant.</summary>
    public const string TenantId = "tenant";

    public static Guid GetUserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(UserId)!);

    public static Guid? GetTenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TenantId), out var tenantId) ? tenantId : null;

    public static string GetDisplayName(this ClaimsPrincipal user) => user.FindFirstValue(Name) ?? "unknown";
}

public static class Roles
{
    /// <summary>Operates the platform: creates companies and decides which modules each one gets.</summary>
    public const string PlatformAdmin = "platform_admin";

    public const string Owner = "owner";
    public const string Staff = "staff";
}

public static class Policies
{
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Any signed-in user who belongs to a company.</summary>
    public const string TenantUser = "tenant-user";
}
