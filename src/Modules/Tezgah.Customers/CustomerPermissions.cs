using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Customers;

internal static class CustomerPermissions
{
    public static readonly Permission View = new("customers.view", "Müşterileri görür", Roles.Staff);

    public static readonly Permission Edit = new("customers.edit", "Müşteri ekler ve bilgilerini düzeltir", Roles.Staff);

    public static readonly IReadOnlyList<Permission> All = [View, Edit];
}
