using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Appointments;

internal static class AppointmentPermissions
{
    public static readonly Permission View = new("appointments.view", "Takvimi, hizmetleri ve personeli görür", Roles.Staff);

    public static readonly Permission Book = new("appointments.book", "Randevu verir, tamamlar, iptal eder", Roles.Staff);

    public static readonly Permission Setup = new("appointments.setup", "Hizmetleri, süreleri ve personeli düzenler", Roles.Manager);

    public static readonly IReadOnlyList<Permission> All = [View, Book, Setup];
}
