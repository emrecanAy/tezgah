using Tezgah.Core.Contracts;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>What earns a stamp. There is deliberately no "by hand": a stamp always stands for a recorded sale or visit.</summary>
internal enum EarnRule
{
    /// <summary>One stamp for every completed appointment.</summary>
    Appointment,

    /// <summary>One stamp for every order, however many items it holds.</summary>
    Order,

    /// <summary>One stamp for every item sold: two lattes, two stamps.</summary>
    Item,
}

/// <summary>
/// A stamp-card rule a company defines for itself: "every 4 haircuts, one free" for a hairdresser,
/// "every 4 coffees, one on the house" for a café. The module knows nothing about either trade.
/// </summary>
internal sealed class LoyaltyProgram : ITenantOwned
{
    public const int MinStamps = 2;
    public const int MaxStamps = 20;
    public const int MaxCategoryLength = 50;

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

    public EarnRule EarnRule { get; private set; }

    /// <summary>
    /// For order-based programs: only items of this menu category count, and a reward makes one of
    /// them free. <c>null</c> means every item.
    /// </summary>
    public string? Category { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The program's rewards can be spent at the till.</summary>
    public bool IsEarnedWithOrders => EarnRule is EarnRule.Order or EarnRule.Item;

    public static Result<LoyaltyProgram> Create(
        string? name, int stampsRequired, string? reward, EarnRule earnRule, string? category, DateTimeOffset now)
    {
        var program = new LoyaltyProgram { Id = Guid.CreateVersion7(), CreatedAt = now };
        var updated = program.Update(name, stampsRequired, reward, earnRule, category, isActive: true);
        return updated.IsSuccess ? program : updated.Error;
    }

    public Result Update(string? name, int stampsRequired, string? reward, EarnRule earnRule, string? category, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(reward))
        {
            return Error.Validation("invalid_program", "A program needs a name and a reward.");
        }

        if (stampsRequired is < MinStamps or > MaxStamps)
        {
            return Error.Validation("invalid_stamp_count", $"A card holds between {MinStamps} and {MaxStamps} stamps.");
        }

        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category?.Length > MaxCategoryLength)
        {
            return Error.Validation("invalid_program", $"A category is at most {MaxCategoryLength} characters.");
        }

        Name = name.Trim();
        StampsRequired = stampsRequired;
        Reward = reward.Trim();
        EarnRule = earnRule;
        // Appointments have no menu category, so a category on an appointment program would never match.
        Category = earnRule == EarnRule.Appointment ? null : category;
        IsActive = isActive;
        return Result.Success;
    }

    /// <summary>How many stamps an order earns. Items given away as a reward earn nothing.</summary>
    public int StampsFor(IReadOnlyList<OrderedItem> items)
    {
        var paidItems = items.Where(Counts).Sum(item => item.Quantity - item.FreeQuantity);
        return EarnRule switch
        {
            EarnRule.Item => paidItems,
            EarnRule.Order => paidItems > 0 ? 1 : 0,
            _ => 0,
        };
    }

    private bool Counts(OrderedItem item) =>
        Category is null || string.Equals(item.Category, Category, StringComparison.OrdinalIgnoreCase);
}
