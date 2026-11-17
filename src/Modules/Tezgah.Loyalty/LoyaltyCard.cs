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

    /// <summary>Stamps on the card that is currently being filled.</summary>
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

    /// <returns><c>true</c> when this stamp completed a card and earned a reward.</returns>
    public bool AddStamp(int stampsRequired, DateTimeOffset now)
    {
        UpdatedAt = now;
        Stamps++;

        // ">=" rather than "==": if the company lowers the requirement, a card that is already past
        // the new threshold pays out on its next stamp instead of being stuck.
        if (Stamps < stampsRequired)
        {
            return false;
        }

        Stamps = 0;
        RewardsAvailable++;
        return true;
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
}

internal enum CardEntryKind
{
    Stamp,
    Redemption,
}

/// <summary>The card's history: who stamped or redeemed, when, and why.</summary>
internal sealed class CardEntry : ITenantOwned
{
    private CardEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CardId { get; private set; }

    public CardEntryKind Kind { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// Set for stamps given automatically (for example <c>appointment:{id}</c>). Unique per card, which
    /// is what makes a redelivered event harmless.
    /// </summary>
    public string? Source { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CardEntry Record(
        Guid cardId, CardEntryKind kind, string? note, string? source, string createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CardId = cardId,
        Kind = kind,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        Source = source,
        CreatedBy = createdBy,
        CreatedAt = now,
    };
}
