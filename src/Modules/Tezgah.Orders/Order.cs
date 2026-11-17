using Tezgah.Core.Contracts;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Orders;

internal enum OrderStatus
{
    Placed,
    Cancelled,
}

/// <summary>
/// One sale at the till. Every amount on it is worked out here from the menu, never taken from the
/// client: the till sends what was ordered, not what it costs.
/// </summary>
internal sealed class Order : ITenantOwned
{
    public const int MaxLines = 50;
    public const int MaxQuantity = 99;

    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    /// <summary>Chosen by the till, so that sending the same order twice records it once.</summary>
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Empty for a walk-in customer, who earns no stamps.</summary>
    public Guid? CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public decimal Subtotal { get; private set; }

    public decimal Discount { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>The loyalty reward spent on this order, in the program's words.</summary>
    public string? RewardNote { get; private set; }

    public string PlacedBy { get; private set; } = null!;

    /// <summary>The user who rang the order up; empty for imported or demo orders.</summary>
    public Guid? PlacedById { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c>: two tills cancelling the same order cannot both succeed.</summary>
    public uint Version { get; private set; }

    /// <param name="reward">A reward already claimed for this order; it makes one qualifying item free.</param>
    public static Result<Order> Place(
        Guid id, Guid? customerId, IReadOnlyList<(MenuItem Item, int Quantity)> picks, RewardClaim? reward,
        Guid? placedById, string placedBy, DateTimeOffset now)
    {
        if (picks.Count is 0 or > MaxLines || picks.Any(pick => pick.Quantity is < 1 or > MaxQuantity))
        {
            return Error.Validation("invalid_order", $"An order has 1 to {MaxLines} items, each 1 to {MaxQuantity} times.");
        }

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            Status = OrderStatus.Placed,
            PlacedById = placedById,
            PlacedBy = placedBy,
            PlacedAt = now,
        };

        // The same item picked twice becomes one line, so a reward cannot be split across copies.
        foreach (var group in picks.GroupBy(pick => pick.Item.Id))
        {
            var item = group.First().Item;
            order._lines.Add(OrderLine.For(order._lines.Count + 1, item, group.Sum(pick => pick.Quantity)));
        }

        if (reward is not null)
        {
            // The cheapest qualifying item goes free: "1 coffee on us" is the coffee, not the cake beside it.
            var freeLine = order._lines
                .Where(line => reward.Category is null
                    || string.Equals(line.Category, reward.Category, StringComparison.OrdinalIgnoreCase))
                .MinBy(line => line.UnitPrice);
            if (freeLine is null)
            {
                return Error.Conflict(
                    "reward_not_applicable", $"The reward is for an item of category '{reward.Category}', and the order has none.");
            }

            freeLine.GiveOneFree();
            order.RewardNote = $"{reward.ProgramName}: {reward.Reward}";
        }

        order.Subtotal = order._lines.Sum(line => line.UnitPrice * line.Quantity);
        order.Total = order._lines.Sum(line => line.Amount);
        order.Discount = order.Subtotal - order.Total;
        return order;
    }

    /// <summary>How long after placing it the person who rang an order up may still cancel it themselves.</summary>
    public static readonly TimeSpan OwnCancellationWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether this user may cancel this order. Managers may cancel anything; anyone else only an order
    /// they placed themselves, within <see cref="OwnCancellationWindow"/>: a mistake at the till gets
    /// fixed on the spot, but nobody can quietly erase yesterday's sales.
    /// </summary>
    public bool MayBeCancelledBy(Guid userId, bool canCancelAny, DateTimeOffset now) =>
        canCancelAny || (PlacedById == userId && now - PlacedAt <= OwnCancellationWindow);

    public Result Cancel(DateTimeOffset now)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return Error.Conflict("order_cancelled", "This order is already cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        return Result.Success;
    }

    public IReadOnlyList<OrderedItem> ToOrderedItems() =>
        _lines.ConvertAll(line => new OrderedItem(line.Name, line.Category, line.Quantity, line.FreeQuantity));
}

/// <summary>A line of an order, with the name, category and price the item had when it was sold.</summary>
internal sealed class OrderLine
{
    private OrderLine()
    {
    }

    /// <summary>1, 2, 3... in the order the items were rung up.</summary>
    public int Position { get; private set; }

    public Guid MenuItemId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Category { get; private set; } = null!;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>How many of <see cref="Quantity"/> were given away as a loyalty reward.</summary>
    public int FreeQuantity { get; private set; }

    public decimal Amount => UnitPrice * (Quantity - FreeQuantity);

    public static OrderLine For(int position, MenuItem item, int quantity) => new()
    {
        Position = position,
        MenuItemId = item.Id,
        Name = item.Name,
        Category = item.Category,
        UnitPrice = item.Price,
        Quantity = quantity,
    };

    public void GiveOneFree() => FreeQuantity++;
}
