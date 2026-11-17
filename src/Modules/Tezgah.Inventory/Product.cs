using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Inventory;

internal sealed class Product : ITenantOwned
{
    private Product()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>How the product is counted; one of <see cref="StockUnits.All"/>.</summary>
    public string Unit { get; private set; } = null!;

    /// <summary>
    /// How much is on the shelf, in <see cref="Unit"/>. Never assigned through the entity after
    /// creation: stock only moves through the atomic statement in <see cref="Stockroom"/>, and every
    /// move, the opening one included, leaves a <see cref="StockMovement"/> behind.
    /// </summary>
    public decimal Stock { get; private set; }

    /// <summary>At or below this quantity the product shows up as running low.</summary>
    public decimal ReorderLevel { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Defines a product. What is already on the shelf is not a property of the product but its first
    /// stock movement, so the opening stock shows up in the history like any delivery.
    /// </summary>
    public static Result<(Product Product, StockMovement? Opening)> Create(
        string? name, string? unit, decimal reorderLevel, decimal openingStock, string createdBy, DateTimeOffset now)
    {
        var product = new Product { Id = Guid.CreateVersion7() };
        var updated = product.Update(name, unit, reorderLevel, isActive: true);
        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        if (openingStock == 0)
        {
            return (product, null);
        }

        var valid = StockUnits.ValidateQuantity(product.Unit, openingStock);
        if (!valid.IsSuccess)
        {
            return valid.Error;
        }

        product.Stock = openingStock;
        return (product, StockMovement.Record(product.Id, MovementKind.In, openingStock, "Açılış stoğu", createdBy, now));
    }

    public Result Update(string? name, string? unit, decimal reorderLevel, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("invalid_product", "A product needs a name.");
        }

        unit = unit?.Trim().ToLowerInvariant();
        if (unit is null || !StockUnits.All.Contains(unit))
        {
            return Error.Validation("invalid_unit", $"The unit is one of: {string.Join(", ", StockUnits.All)}.");
        }

        // 12 kg must not quietly become 12 adet.
        if (Unit is not null && unit != Unit && Stock != 0)
        {
            return Error.Conflict("unit_locked", "The unit can only change while the product is out of stock.");
        }

        if (reorderLevel != 0)
        {
            var valid = StockUnits.ValidateQuantity(unit, reorderLevel);
            if (!valid.IsSuccess)
            {
                return Error.Validation("invalid_reorder_level", valid.Error.Message);
            }
        }

        Name = name.Trim();
        Unit = unit;
        ReorderLevel = reorderLevel;
        IsActive = isActive;
        return Result.Success;
    }
}

/// <summary>The units a product can be counted in. Counted units take whole numbers; measured ones take decimals.</summary>
internal static class StockUnits
{
    public static readonly string[] Counted = ["adet", "paket", "koli", "şişe"];

    public static readonly string[] Measured = ["kg", "g", "lt", "ml"];

    public static readonly string[] All = [.. Counted, .. Measured];

    /// <summary>A positive quantity that fits the unit: no 2.5 adet, at most three decimals of a kilo.</summary>
    public static Result ValidateQuantity(string unit, decimal quantity)
    {
        if (quantity <= 0)
        {
            return Error.Validation("invalid_quantity", "The quantity must be greater than zero.");
        }

        if (Counted.Contains(unit) && decimal.Truncate(quantity) != quantity)
        {
            return Error.Validation("invalid_quantity", $"Products counted in '{unit}' take whole numbers.");
        }

        return decimal.Round(quantity, 3) == quantity
            ? Result.Success
            : Error.Validation("invalid_quantity", "A quantity has at most three decimals.");
    }
}

internal enum MovementKind
{
    In,
    Out,
}

internal sealed class StockMovement : ITenantOwned
{
    private StockMovement()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProductId { get; private set; }

    public MovementKind Kind { get; private set; }

    /// <summary>Always positive; <see cref="Kind"/> gives the direction.</summary>
    public decimal Quantity { get; private set; }

    public string? Note { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static StockMovement Record(
        Guid productId, MovementKind kind, decimal quantity, string? note, string createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ProductId = productId,
        Kind = kind,
        Quantity = quantity,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        CreatedBy = createdBy,
        CreatedAt = now,
    };
}
