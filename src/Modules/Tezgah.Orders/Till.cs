using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Orders;

/// <param name="Id">Chosen by the till. Sending the same order again (a double tap, a retry after a timeout) records it once.</param>
/// <param name="RewardCardId">A loyalty card whose reward the customer wants to spend on this order.</param>
internal sealed record PlaceOrder(Guid Id, Guid? CustomerId, IReadOnlyList<OrderPick> Items, Guid? RewardCardId);

internal sealed record OrderPick(Guid MenuItemId, int Quantity);

/// <summary>Rings up and cancels orders. Loyalty learns about both only through events.</summary>
internal sealed class Till(
    OrdersDbContext db, ICustomerDirectory customers, ILoyaltyRewards rewards, TenantContext tenant, TimeProvider time)
{
    public async Task<Result<Order>> PlaceAsync(PlaceOrder request, string placedBy, CancellationToken ct)
    {
        if (request.Id == Guid.Empty)
        {
            return Error.Validation("invalid_order", "An order needs an id chosen by the till.");
        }

        if (await FindAsync(request.Id, ct) is { } alreadyPlaced)
        {
            return alreadyPlaced;
        }

        if (request.CustomerId is { } customerId && !await customers.ExistsAsync(customerId, ct))
        {
            return Error.NotFound("customer_not_found", "This customer does not exist.");
        }

        var picks = await PicksAsync(request.Items, ct);
        if (!picks.IsSuccess)
        {
            return picks.Error;
        }

        RewardClaim? reward = null;
        if (request.RewardCardId is { } cardId)
        {
            if (request.CustomerId is null)
            {
                return Error.Validation("reward_needs_customer", "A reward can only be spent on an order with a customer.");
            }

            var claim = await rewards.ClaimAsync(cardId, request.CustomerId.Value, request.Id, ct);
            if (!claim.IsSuccess)
            {
                return claim.Error;
            }

            reward = claim.Value;
        }

        var order = Order.Place(request.Id, request.CustomerId, picks.Value, reward, placedBy, time.GetUtcNow());
        if (!order.IsSuccess)
        {
            await ReleaseAsync(reward, request.Id);
            return order.Error;
        }

        return await SaveAsync(order.Value, reward, ct);
    }

    public async Task<Result> CancelAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return OrderNotFound();
        }

        var now = time.GetUtcNow();
        var cancelled = order.Cancel(now);
        if (!cancelled.IsSuccess)
        {
            return cancelled.Error;
        }

        if (order.CustomerId is { } customerId)
        {
            db.Publish(new OrderCancelled(order.TenantId, order.Id, customerId, now));
        }

        // A second till cancelling at the same moment fails on the xmin check, so the event goes out once.
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct);

    private async Task<Result<Order>> SaveAsync(Order order, RewardClaim? reward, CancellationToken ct)
    {
        db.Orders.Add(order);
        if (order.CustomerId is { } customerId)
        {
            // Stored in the same transaction as the order: there is no sale without its stamps, and no stamps without a sale.
            db.Publish(new OrderPlaced(tenant.TenantId, order.Id, customerId, order.ToOrderedItems(), order.Total, order.PlacedAt));
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return order;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // The same order arrived twice at once and the other request saved it first. Its reward claim
            // was the same claim (claims are per order), so nothing is given back.
            db.ChangeTracker.Clear();
            return await FindAsync(order.Id, ct) is { } saved
                ? saved
                : Error.Conflict("order_id_in_use", "This order id is already taken.");
        }
        catch
        {
            await ReleaseAsync(reward, order.Id);
            throw;
        }
    }

    /// <summary>
    /// Gives back a reward claimed for an order that was not saved. Uses its own token: the request may
    /// already be cancelled, and a reward must not stay spent on an order that does not exist.
    /// </summary>
    private Task ReleaseAsync(RewardClaim? reward, Guid orderId) =>
        reward is null ? Task.CompletedTask : rewards.ReleaseAsync(orderId, CancellationToken.None);

    private async Task<Result<List<(MenuItem Item, int Quantity)>>> PicksAsync(
        IReadOnlyList<OrderPick>? items, CancellationToken ct)
    {
        if (items is null || items.Count is 0 or > Order.MaxLines)
        {
            return Error.Validation("invalid_order", $"An order has 1 to {Order.MaxLines} items.");
        }

        var ids = items.Select(item => item.MenuItemId).Distinct().ToList();
        var menu = await db.Menu.AsNoTracking()
            .Where(item => ids.Contains(item.Id) && item.IsAvailable)
            .ToDictionaryAsync(item => item.Id, ct);
        if (menu.Count != ids.Count)
        {
            return Error.Conflict("menu_item_unavailable", "An item in the order is not on the menu.");
        }

        return items.Select(pick => (menu[pick.MenuItemId], pick.Quantity)).ToList();
    }

    private static Error OrderNotFound() => Error.NotFound("order_not_found", "This order does not exist.");
}
