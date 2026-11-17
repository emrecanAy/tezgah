using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>One customer's card in one program.</summary>
internal sealed class LoyaltyCard : ITenantOwned
{
    private LoyaltyCard()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProgramId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>
    /// Stamps on the card that is currently being filled. Can drop below zero when an order is
    /// cancelled after its reward was already spent; the next stamps then pay that back first.
    /// </summary>
    public int Stamps { get; private set; }

    /// <summary>Full cards the customer has not cashed in yet.</summary>
    public int RewardsAvailable { get; private set; }

    public int RewardsRedeemed { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LoyaltyCard Open(Guid programId, Guid customerId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ProgramId = programId,
        CustomerId = customerId,
        UpdatedAt = now,
    };

    /// <returns>How many rewards these stamps completed.</returns>
    public int AddStamps(int count, int stampsRequired, DateTimeOffset now)
    {
        UpdatedAt = now;
        var rewards = 0;
        for (var i = 0; i < count; i++)
        {
            Stamps++;

            // ">=" rather than "==": if the company lowers the requirement, a card that is already past
            // the new threshold pays out on its next stamp instead of being stuck.
            if (Stamps >= stampsRequired)
            {
                Stamps = 0;
                RewardsAvailable++;
                rewards++;
            }
        }

        return rewards;
    }

    /// <summary>Takes back stamps of a cancelled order, including a reward they completed if it is still unspent.</summary>
    public void RemoveStamps(int count, int stampsRequired, DateTimeOffset now)
    {
        UpdatedAt = now;
        for (var i = 0; i < count; i++)
        {
            if (Stamps == 0 && RewardsAvailable > 0)
            {
                RewardsAvailable--;
                Stamps = stampsRequired;
            }

            Stamps--;
        }
    }

    public Result Redeem(DateTimeOffset now)
    {
        if (RewardsAvailable == 0)
        {
            return Error.Conflict("no_reward_available", "This card has no reward to redeem.");
        }

        RewardsAvailable--;
        RewardsRedeemed++;
        UpdatedAt = now;
        return Result.Success;
    }

    /// <summary>Hands back a reward spent on an order that was cancelled.</summary>
    public void ReturnReward(DateTimeOffset now)
    {
        RewardsAvailable++;
        RewardsRedeemed--;
        UpdatedAt = now;
    }
}

internal enum CardEntryKind
{
    Stamp,
    Redemption,
    StampReversal,
    RedemptionReversal,
}

/// <summary>The card's history: what changed it, when, and for which order or appointment.</summary>
internal sealed class CardEntry : ITenantOwned
{
    private CardEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CardId { get; private set; }

    public CardEntryKind Kind { get; private set; }

    /// <summary>Stamps given or taken back; 1 for a redemption.</summary>
    public int Quantity { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// What caused the entry, such as <c>order:{id}</c> or <c>appointment:{id}</c>. Unique per card and
    /// kind, which is what makes a redelivered event or a retried order harmless.
    /// </summary>
    public string? Source { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CardEntry Record(
        Guid cardId, CardEntryKind kind, int quantity, string? note, string? source, string createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CardId = cardId,
        Kind = kind,
        Quantity = quantity,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        Source = source,
        CreatedBy = createdBy,
        CreatedAt = now,
    };
}

/// <summary>
/// A cancelled order or other source whose effects were undone. Kept so that a stamp for it arriving
/// late (a retried event overtaken by its cancellation) is refused instead of applied.
/// </summary>
internal sealed class VoidedSource : ITenantOwned
{
    private VoidedSource()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Source { get; private set; } = null!;

    public DateTimeOffset VoidedAt { get; private set; }

    public static VoidedSource Record(string source, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), Source = source, VoidedAt = now };
}
