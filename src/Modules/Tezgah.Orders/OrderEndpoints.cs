using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Orders;

internal static class OrderEndpoints
{
    public sealed record MenuItemRequest(string? Name, string? Category, decimal Price, bool? IsAvailable);

    public sealed record MenuItemResponse(Guid Id, string Name, string Category, decimal Price, bool IsAvailable)
    {
        public static MenuItemResponse From(MenuItem item) => new(item.Id, item.Name, item.Category, item.Price, item.IsAvailable);
    }

    public sealed record OrderRequest(Guid Id, Guid? CustomerId, IReadOnlyList<OrderPick>? Items, Guid? RewardCardId);

    public sealed record LineResponse(string Name, string Category, decimal UnitPrice, int Quantity, int FreeQuantity, decimal Amount);

    public sealed record OrderResponse(
        Guid Id,
        Guid? CustomerId,
        string? CustomerName,
        string Status,
        IReadOnlyList<LineResponse> Lines,
        decimal Subtotal,
        decimal Discount,
        decimal Total,
        string? RewardNote,
        string PlacedBy,
        Guid? PlacedById,
        DateTimeOffset PlacedAt);

    public sealed record Summary(int OrdersToday, decimal RevenueToday);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/menu", ListMenuAsync).RequirePermission(OrderPermissions.View);
        api.MapPost("/menu", CreateMenuItemAsync).RequirePermission(OrderPermissions.ManageMenu);
        api.MapPut("/menu/{itemId:guid}", UpdateMenuItemAsync).RequirePermission(OrderPermissions.ManageMenu);

        api.MapGet("", ListAsync).RequirePermission(OrderPermissions.View);
        api.MapPost("", PlaceAsync).RequirePermission(OrderPermissions.Sell);
        // Staff may cancel only their own order, shortly after placing it; the till decides per order.
        api.MapPost("/{orderId:guid}/cancel", async (Guid orderId, ClaimsPrincipal user, Till till, CancellationToken ct) =>
            (await till.CancelAsync(orderId, user, ct)).ToHttp())
            .RequirePermission(OrderPermissions.Sell);
        api.MapGet("/rewards", (Guid customerId, ILoyaltyRewards rewards, CancellationToken ct) =>
            rewards.AvailableAsync(customerId, ct))
            .RequirePermission(OrderPermissions.Sell);
        api.MapGet("/summary", SummaryAsync).RequirePermission(OrderPermissions.View);
    }

    private static async Task<IReadOnlyList<MenuItemResponse>> ListMenuAsync(OrdersDbContext db, CancellationToken ct) =>
        (await db.Menu.AsNoTracking().OrderBy(item => item.Category).ThenBy(item => item.Name).ToListAsync(ct))
            .ConvertAll(MenuItemResponse.From);

    private static async Task<IResult> CreateMenuItemAsync(MenuItemRequest request, OrdersDbContext db, CancellationToken ct)
    {
        var item = MenuItem.Create(request.Name, request.Category, request.Price);
        if (!item.IsSuccess)
        {
            return item.Error.ToProblem();
        }

        db.Menu.Add(item.Value);
        return await SaveMenuAsync(db, ct) is { } conflict
            ? conflict.ToProblem()
            : Results.Created($"/api/orders/menu/{item.Value.Id}", MenuItemResponse.From(item.Value));
    }

    private static async Task<IResult> UpdateMenuItemAsync(
        Guid itemId, MenuItemRequest request, OrdersDbContext db, CancellationToken ct)
    {
        var item = await db.Menu.SingleOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
        {
            return Error.NotFound("menu_item_not_found", "This menu item does not exist.").ToProblem();
        }

        var updated = item.Update(request.Name, request.Category, request.Price, request.IsAvailable ?? item.IsAvailable);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        return await SaveMenuAsync(db, ct) is { } conflict ? conflict.ToProblem() : Results.Ok(MenuItemResponse.From(item));
    }

    /// <summary>Orders placed inside [from, to). The client passes the bounds of its local day.</summary>
    private static async Task<IResult> ListAsync(
        DateTimeOffset from, DateTimeOffset to, OrdersDbContext db, ICustomerDirectory customers, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31))
        {
            return Error.Validation("invalid_range", "Ask for a range of at most 31 days.").ToProblem();
        }

        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        var orders = await db.Orders.AsNoTracking()
            .Where(order => order.PlacedAt >= fromUtc && order.PlacedAt < toUtc)
            .OrderByDescending(order => order.PlacedAt)
            .Take(300)
            .ToListAsync(ct);

        return Results.Ok(await ToResponsesAsync(orders, customers, ct));
    }

    private static async Task<IResult> PlaceAsync(
        OrderRequest request, ClaimsPrincipal user, Till till, ICustomerDirectory customers, CancellationToken ct)
    {
        var placed = await till.PlaceAsync(
            new PlaceOrder(request.Id, request.CustomerId, request.Items ?? [], request.RewardCardId),
            new Cashier(user.GetUserId(), user.GetDisplayName()),
            ct);

        return placed.IsSuccess
            ? Results.Ok((await ToResponsesAsync([placed.Value], customers, ct))[0])
            : placed.Error.ToProblem();
    }

    private static async Task<Summary> SummaryAsync(DateTimeOffset dayStart, OrdersDbContext db, CancellationToken ct)
    {
        var (from, to) = (dayStart.ToUniversalTime(), dayStart.ToUniversalTime().AddDays(1));
        var today = db.Orders.Where(order => order.Status == OrderStatus.Placed && order.PlacedAt >= from && order.PlacedAt < to);
        return new Summary(await today.CountAsync(ct), await today.SumAsync(order => order.Total, ct));
    }

    private static async Task<List<OrderResponse>> ToResponsesAsync(
        IReadOnlyList<Order> orders, ICustomerDirectory customers, CancellationToken ct)
    {
        var names = await customers.GetAsync(
            [.. orders.Where(order => order.CustomerId is not null).Select(order => order.CustomerId!.Value).Distinct()], ct);

        return [.. orders.Select(order => new OrderResponse(
            order.Id,
            order.CustomerId,
            order.CustomerId is { } id ? names.GetValueOrDefault(id)?.FullName ?? "Silinmiş müşteri" : null,
            order.Status.ToString().ToLowerInvariant(),
            [.. order.Lines.Select(line => new LineResponse(
                line.Name, line.Category, line.UnitPrice, line.Quantity, line.FreeQuantity, line.Amount))],
            order.Subtotal,
            order.Discount,
            order.Total,
            order.RewardNote,
            order.PlacedBy,
            order.PlacedById,
            order.PlacedAt))];
    }

    private static async Task<Error?> SaveMenuAsync(OrdersDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("menu_item_name_in_use", "A menu item with this name already exists.");
        }
    }
}
