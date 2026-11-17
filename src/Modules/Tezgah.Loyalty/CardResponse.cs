using Tezgah.Core.Contracts;

namespace Tezgah.Loyalty;

internal sealed record CardResponse(
    Guid Id,
    Guid ProgramId,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    int Stamps,
    int StampsRequired,
    int RewardsAvailable,
    int RewardsRedeemed,
    DateTimeOffset UpdatedAt)
{
    public static CardResponse From(LoyaltyCard card, LoyaltyProgram program, CustomerRef? customer) => new(
        card.Id, card.ProgramId, card.CustomerId, customer?.FullName ?? "Silinmiş müşteri", customer?.Phone ?? "",
        // A card that owes stamps after a cancellation shows as empty rather than with a minus.
        Math.Max(card.Stamps, 0), program.StampsRequired, card.RewardsAvailable, card.RewardsRedeemed, card.UpdatedAt);
}

/// <param name="RewardsEarned">How many cards these stamps filled.</param>
internal sealed record StampResponse(CardResponse Card, int RewardsEarned);
