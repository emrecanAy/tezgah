using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Inventory;

/// <summary>
/// Moves stock with one conditional UPDATE. "Is there enough?" and "take it" are a single
/// statement under the row lock, so two people at two tills cannot both take the last item, and
/// there is no read-then-write gap to retry around.
/// </summary>
internal sealed class Stockroom(InventoryDbContext db, TenantContext tenant, TimeProvider time)
{
    public async Task<Result<decimal>> MoveAsync(
        Guid productId, MovementKind kind, decimal quantity, string? note, string movedBy, CancellationToken ct)
    {
        var unit = await db.Products
            .Where(product => product.Id == productId)
            .Select(product => product.Unit)
            .SingleOrDefaultAsync(ct);
        if (unit is null)
        {
            return Error.NotFound("product_not_found", "This product does not exist.");
        }

        var valid = StockUnits.ValidateQuantity(unit, quantity);
        if (!valid.IsSuccess)
        {
            return valid.Error;
        }

        var delta = kind == MovementKind.In ? quantity : -quantity;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var moved = await db.Database.ExecuteSqlAsync($"""
            UPDATE inventory.products
               SET stock = stock + {delta}
             WHERE id = {productId} AND tenant_id = {tenant.TenantId} AND stock + {delta} >= 0
            """, ct);

        if (moved == 0)
        {
            return await db.Products.AnyAsync(product => product.Id == productId, ct)
                ? Error.Conflict("insufficient_stock", "There is not enough stock for this movement.")
                : Error.NotFound("product_not_found", "This product does not exist.");
        }

        db.Movements.Add(StockMovement.Record(productId, kind, quantity, note, movedBy, time.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await db.Products.AsNoTracking().Where(product => product.Id == productId).Select(product => product.Stock).SingleAsync(ct);
    }
}
