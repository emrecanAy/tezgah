using Tezgah.Core.Messaging;

namespace Tezgah.Core.Contracts;

/// <summary>
/// Published by Orders when a sale is rung up for a known customer. Loyalty turns it into stamps;
/// the till never stamps a card itself, so a stamp always stands for a sale that happened.
/// </summary>
public sealed record OrderPlaced(
    Guid TenantId, Guid OrderId, Guid CustomerId, IReadOnlyList<OrderedItem> Items, decimal Total, DateTimeOffset PlacedAt)
    : ITenantEvent;

/// <param name="Category">The menu category ("Kahve", "Tatlı"), which loyalty programs may count on.</param>
/// <param name="FreeQuantity">How many of <paramref name="Quantity"/> were given away as a loyalty reward.</param>
public sealed record OrderedItem(string Name, string Category, int Quantity, int FreeQuantity);

/// <summary>Published when a placed order is called off. Whatever the order earned or spent is undone.</summary>
public sealed record OrderCancelled(Guid TenantId, Guid OrderId, Guid CustomerId, DateTimeOffset CancelledAt) : ITenantEvent;
