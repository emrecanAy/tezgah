using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;

namespace Tezgah.Loyalty;

/// <summary>Turns a sale into stamps, as many as each program's rule says the order is worth.</summary>
internal sealed class OrderPlacedHandler(LoyaltyDbContext db, CardDesk desk) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced order, CancellationToken ct)
    {
        var programs = await db.Programs
            .Where(program => program.IsActive && program.EarnRule != EarnRule.Appointment)
            .ToListAsync(ct);

        foreach (var program in programs)
        {
            var stamps = program.StampsFor(order.Items);
            if (stamps == 0)
            {
                continue;
            }

            // Fails with "source_voided" only if the order was cancelled before this event got here;
            // then there is nothing to stamp, which is the right outcome.
            await desk.StampAsync(
                program.Id, order.CustomerId, stamps, CardDesk.OrderSource(order.OrderId), Describe(order.Items),
                stampedBy: "Sipariş", ct);
        }
    }

    private static string Describe(IReadOnlyList<OrderedItem> items) =>
        string.Join(", ", items.Select(item => $"{item.Quantity} × {item.Name}"));
}

/// <summary>A cancelled order takes back its stamps and returns the reward it spent.</summary>
internal sealed class OrderCancelledHandler(CardDesk desk) : IIntegrationEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderCancelled order, CancellationToken ct) =>
        desk.ReverseAsync(order.CustomerId, CardDesk.OrderSource(order.OrderId), "Sipariş iptali", voidSource: true, ct);
}
