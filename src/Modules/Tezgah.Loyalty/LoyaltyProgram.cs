using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>
/// A stamp-card rule a company defines for itself: "every 4 haircuts, one free" for a hairdresser,
/// "every 4 coffees, one on the house" for a café. The module knows nothing about either trade.
/// </summary>
internal sealed class LoyaltyProgram : ITenantOwned
{
    public const int MinStamps = 2;
    public const int MaxStamps = 20;

    private LoyaltyProgram()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>How many stamps fill a card.</summary>
    public int StampsRequired { get; private set; }

    /// <summary>What a full card is worth, in the company's own words.</summary>
    public string Reward { get; private set; } = null!;

    /// <summary>Stamp the customer's card whenever one of their appointments is completed.</summary>
    public bool StampOnAppointment { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<LoyaltyProgram> Create(
        string? name, int stampsRequired, string? reward, bool stampOnAppointment, DateTimeOffset now)
    {
        var program = new LoyaltyProgram { Id = Guid.CreateVersion7(), CreatedAt = now };
        var updated = program.Update(name, stampsRequired, reward, stampOnAppointment, isActive: true);
        return updated.IsSuccess ? program : updated.Error;
    }

    public Result Update(string? name, int stampsRequired, string? reward, bool stampOnAppointment, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(reward))
        {
            return Error.Validation("invalid_program", "A program needs a name and a reward.");
        }

        if (stampsRequired is < MinStamps or > MaxStamps)
        {
            return Error.Validation("invalid_stamp_count", $"A card holds between {MinStamps} and {MaxStamps} stamps.");
        }

        Name = name.Trim();
        StampsRequired = stampsRequired;
        Reward = reward.Trim();
        StampOnAppointment = stampOnAppointment;
        IsActive = isActive;
        return Result.Success;
    }
}
