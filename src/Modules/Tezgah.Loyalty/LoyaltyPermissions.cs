using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Loyalty;

internal static class LoyaltyPermissions
{
    public static readonly Permission View = new("loyalty.view", "Programları ve kartları görür", Roles.Staff);

    public static readonly Permission Redeem = new("loyalty.redeem", "Randevu programlarında ödülü teslim eder", Roles.Staff);

    public static readonly Permission ManagePrograms = new("loyalty.programs", "Sadakat programı açar, kuralını değiştirir, duraklatır", Roles.Manager);

    public static readonly IReadOnlyList<Permission> All = [View, Redeem, ManagePrograms];
}
