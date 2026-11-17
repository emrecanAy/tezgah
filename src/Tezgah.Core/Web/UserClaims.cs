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

    /// <summary>
    /// The user's session version when the token was issued. Changing the password or the role, or
    /// deactivating the user, raises the version and every older token stops working at once.
    /// </summary>
    public const string SessionVersion = "sv";

    /// <summary>Present while the user still signs in with a password someone else chose.</summary>
    public const string PasswordChangeRequired = "pwd_change";

    public static Guid GetUserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(UserId)!);

    public static Guid? GetTenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TenantId), out var tenantId) ? tenantId : null;

    public static string GetDisplayName(this ClaimsPrincipal user) => user.FindFirstValue(Name) ?? "unknown";

    public static string? GetRole(this ClaimsPrincipal user) => user.FindFirstValue(Role);
}

public static class Roles
{
    /// <summary>Operates the platform: opens companies and decides which modules each one gets. Sees no company data.</summary>
    public const string PlatformAdmin = "platform_admin";

    /// <summary>Runs the company: everything a manager can do, plus who works in it.</summary>
    public const string Owner = "owner";

    /// <summary>Sets the business up: menu, prices, services, loyalty rules, vehicles; can cancel any order.</summary>
    public const string Manager = "manager";

    /// <summary>Does the daily work: rings up orders, books appointments, moves stock, logs fuel.</summary>
    public const string Staff = "staff";

    /// <summary>The roles inside a company, lowest first.</summary>
    public static readonly IReadOnlyList<string> CompanyRoles = [Staff, Manager, Owner];

    public static bool IsCompanyRole(string? role) => role is not null && CompanyRoles.Contains(role);

    /// <summary>A role's place in the company ladder; 0 for anything that is not a company role.</summary>
    public static int Rank(string? role) => role is null ? 0 : ((IList<string>)CompanyRoles).IndexOf(role) + 1;
}

public static class Policies
{
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Signed in with a password the user chose (not a temporary one handed to them).</summary>
    public const string PasswordSet = "password-set";

    /// <summary>Any signed-in user who belongs to a company and has set their own password.</summary>
    public const string TenantUser = "tenant-user";
}
