using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Inventory;

internal static class InventoryPermissions
{
    public static readonly Permission View = new("inventory.view", "Stoğu ve hareketleri görür", Roles.Staff);

    public static readonly Permission Move = new("inventory.move", "Stok girişi ve çıkışı yapar", Roles.Staff);

    public static readonly Permission ManageProducts = new("inventory.products", "Ürün ekler, birim ve uyarı seviyesini düzenler", Roles.Manager);

    public static readonly IReadOnlyList<Permission> All = [View, Move, ManageProducts];
}
