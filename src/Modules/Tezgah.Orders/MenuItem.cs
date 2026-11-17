using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Orders;

/// <summary>Something the company sells over the counter, at the price the till charges for it.</summary>
internal sealed class MenuItem : ITenantOwned
{
    public const int MaxNameLength = 100;
    public const int MaxCategoryLength = 50;

    private MenuItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>"Kahve", "Tatlı"... Groups the menu at the till, and loyalty programs can count on it.</summary>
    public string Category { get; private set; } = null!;

    public decimal Price { get; private set; }

    /// <summary>Off the menu for now (sold out, seasonal). Past orders keep their own copy of name and price.</summary>
    public bool IsAvailable { get; private set; }

    public static Result<MenuItem> Create(string? name, string? category, decimal price)
    {
        var item = new MenuItem { Id = Guid.CreateVersion7() };
        var updated = item.Update(name, category, price, isAvailable: true);
        return updated.IsSuccess ? item : updated.Error;
    }

    public Result Update(string? name, string? category, decimal price, bool isAvailable)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category)
            || name.Trim().Length > MaxNameLength || category.Trim().Length > MaxCategoryLength)
        {
            return Error.Validation("invalid_menu_item", "A menu item needs a name and a category.");
        }

        if (price is < 0 or > 1_000_000 || decimal.Round(price, 2) != price)
        {
            return Error.Validation("invalid_price", "A price is a non-negative amount with at most two decimals.");
        }

        Name = name.Trim();
        Category = category.Trim();
        Price = price;
        IsAvailable = isAvailable;
        return Result.Success;
    }
}
