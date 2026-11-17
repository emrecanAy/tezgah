using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Orders;

internal static class OrderPermissions
{
    public static readonly Permission View = new("orders.view", "Menüyü ve günün siparişlerini görür", Roles.Staff);

    public static readonly Permission Sell = new("orders.sell", "Sipariş girer; kendi siparişini 15 dakika içinde iptal edebilir", Roles.Staff);

    public static readonly Permission CancelAny = new("orders.cancel", "Her siparişi, her zaman iptal eder", Roles.Manager);

    public static readonly Permission ManageMenu = new("orders.menu", "Menüyü ve fiyatları düzenler", Roles.Manager);

    public static readonly IReadOnlyList<Permission> All = [View, Sell, CancelAny, ManageMenu];
}
