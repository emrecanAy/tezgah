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

    /// <summary>How the product is counted: "adet", "kg", "lt"...</summary>
    public string Unit { get; private set; } = null!;

    /// <summary>
    /// Never assigned through the entity after creation. Stock only moves through the atomic
    /// statement in <see cref="Stockroom"/>, so a stale product object cannot overwrite a movement
    /// that happened in between.
    /// </summary>
    public decimal Stock { get; private set; }

    /// <summary>At or below this quantity the product shows up as running low.</summary>
    public decimal ReorderLevel { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Product> Create(string? name, string? unit, decimal reorderLevel, decimal initialStock)
    {
        if (initialStock < 0)
        {
            return Error.Validation("invalid_product", "The opening stock cannot be negative.");
        }

        var product = new Product { Id = Guid.CreateVersion7(), Stock = initialStock };
        var updated = product.Update(name, unit, reorderLevel, isActive: true);
        return updated.IsSuccess ? product : updated.Error;
    }

    public Result Update(string? name, string? unit, decimal reorderLevel, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit) || reorderLevel < 0)
        {
            return Error.Validation("invalid_product", "A product needs a name and a unit; the reorder level cannot be negative.");
        }

        Name = name.Trim();
        Unit = unit.Trim();
        ReorderLevel = reorderLevel;
        IsActive = isActive;
        return Result.Success;
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
