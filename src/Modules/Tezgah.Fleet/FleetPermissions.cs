using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Fleet;

internal static class FleetPermissions
{
    public static readonly Permission View = new("fleet.view", "Araçları ve uyarıları görür", Roles.Staff);

    public static readonly Permission Log = new("fleet.log", "Yakıt ve bakım kaydı girer", Roles.Staff);

    public static readonly Permission ManageVehicles = new("fleet.vehicles", "Araç ekler, muayene ve sigorta tarihlerini düzenler", Roles.Manager);

    public static readonly IReadOnlyList<Permission> All = [View, Log, ManageVehicles];
}
