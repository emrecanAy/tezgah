using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Inventory;

internal static class InventoryEndpoints
{
    /// <param name="OpeningStock">What is on the shelf when the product is added; recorded as its first stock movement. Ignored on update.</param>
    public sealed record ProductRequest(string? Name, string? Unit, decimal ReorderLevel, decimal OpeningStock, bool? IsActive);

    public sealed record ProductResponse(Guid Id, string Name, string Unit, decimal Stock, decimal ReorderLevel, bool IsActive, bool RunningLow)
    {
        public static ProductResponse From(Product product) => new(
            product.Id, product.Name, product.Unit, product.Stock, product.ReorderLevel, product.IsActive,
            product.IsActive && product.Stock <= product.ReorderLevel);
    }

    /// <param name="Kind"><c>in</c> or <c>out</c>.</param>
    public sealed record MovementRequest(string? Kind, decimal Quantity, string? Note);

    public sealed record MovementResponse(string Kind, decimal Quantity, string? Note, string CreatedBy, DateTimeOffset CreatedAt);

    public sealed record Summary(int Products, int RunningLow);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/products", ListAsync).RequirePermission(InventoryPermissions.View);
        api.MapPost("/products", CreateAsync).RequirePermission(InventoryPermissions.ManageProducts);
        api.MapPut("/products/{productId:guid}", UpdateAsync).RequirePermission(InventoryPermissions.ManageProducts);
        api.MapPost("/products/{productId:guid}/movements", MoveAsync).RequirePermission(InventoryPermissions.Move);
        api.MapGet("/products/{productId:guid}/movements", ListMovementsAsync).RequirePermission(InventoryPermissions.View);
        api.MapGet("/summary", SummaryAsync).RequirePermission(InventoryPermissions.View);
    }

    private static async Task<IReadOnlyList<ProductResponse>> ListAsync(InventoryDbContext db, CancellationToken ct) =>
        (await db.Products.AsNoTracking().OrderBy(product => product.Name).ToListAsync(ct)).ConvertAll(ProductResponse.From);

    private static async Task<IResult> CreateAsync(
        ProductRequest request, ClaimsPrincipal user, InventoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        var created = Product.Create(
            request.Name, request.Unit, request.ReorderLevel, request.OpeningStock, user.GetDisplayName(), time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error.ToProblem();
        }

        // Product and opening movement are saved together: there is no stock without its history.
        var (product, opening) = created.Value;
        db.Products.Add(product);
        if (opening is not null)
        {
            db.Movements.Add(opening);
        }

        return await SaveAsync(db, ct) is { } conflict
            ? conflict.ToProblem()
            : Results.Created($"/api/inventory/products/{product.Id}", ProductResponse.From(product));
    }

    private static async Task<IResult> UpdateAsync(Guid productId, ProductRequest request, InventoryDbContext db, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            return Error.NotFound("product_not_found", "This product does not exist.").ToProblem();
        }

        var updated = product.Update(request.Name, request.Unit, request.ReorderLevel, request.IsActive ?? product.IsActive);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        return await SaveAsync(db, ct) is { } conflict ? conflict.ToProblem() : Results.Ok(ProductResponse.From(product));
    }

    private static async Task<IResult> MoveAsync(
        Guid productId, MovementRequest request, ClaimsPrincipal user, Stockroom stockroom, InventoryDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<MovementKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Error.Validation("invalid_movement_kind", "The movement kind is 'in' or 'out'.").ToProblem();
        }

        var moved = await stockroom.MoveAsync(productId, kind, request.Quantity, request.Note, user.GetDisplayName(), ct);
        if (!moved.IsSuccess)
        {
            return moved.Error.ToProblem();
        }

        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId, ct);
        return Results.Ok(ProductResponse.From(product));
    }

    private static async Task<IReadOnlyList<MovementResponse>> ListMovementsAsync(
        Guid productId, InventoryDbContext db, CancellationToken ct)
    {
        var movements = await db.Movements.AsNoTracking()
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return movements.ConvertAll(movement => new MovementResponse(
            movement.Kind.ToString().ToLowerInvariant(), movement.Quantity, movement.Note, movement.CreatedBy, movement.CreatedAt));
    }

    private static async Task<Summary> SummaryAsync(InventoryDbContext db, CancellationToken ct) => new(
        await db.Products.CountAsync(product => product.IsActive, ct),
        await db.Products.CountAsync(product => product.IsActive && product.Stock <= product.ReorderLevel, ct));

    private static async Task<Error?> SaveAsync(InventoryDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("product_name_in_use", "Another product already has this name.");
        }
    }
}
