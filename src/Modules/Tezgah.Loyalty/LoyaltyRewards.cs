using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Modules;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>Rewards the till can spend. Answers as if empty for a company that has not been given loyalty.</summary>
internal sealed class LoyaltyRewards(LoyaltyDbContext db, CardDesk desk, ITenantModules modules, TenantContext tenant)
    : ILoyaltyRewards
{
    public async Task<IReadOnlyList<RewardOffer>> AvailableAsync(Guid customerId, CancellationToken ct)
    {
        if (!await EnabledAsync(ct))
        {
            return [];
        }

        return await db.Cards
            .Where(card => card.CustomerId == customerId && card.RewardsAvailable > 0)
            .Join(db.Programs, card => card.ProgramId, program => program.Id, (card, program) => new { card, program })
            .Where(row => row.program.IsActive && row.program.EarnRule != EarnRule.Appointment)
            .OrderBy(row => row.program.CreatedAt)
            .Select(row => new RewardOffer(
                row.card.Id, row.program.Name, row.program.Reward, row.program.Category, row.card.RewardsAvailable))
            .ToListAsync(ct);
    }

    public async Task<Result<RewardClaim>> ClaimAsync(Guid cardId, Guid customerId, Guid orderId, CancellationToken ct) =>
        await EnabledAsync(ct)
            ? await desk.ClaimForOrderAsync(cardId, customerId, orderId, ct)
            : Error.NotFound("card_not_found", "This card does not exist.");

    public async Task ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        var source = CardDesk.OrderSource(orderId);
        var customerId = await db.Entries
            .Where(entry => entry.Source == source && entry.Kind == CardEntryKind.Redemption)
            .Join(db.Cards, entry => entry.CardId, card => card.Id, (_, card) => (Guid?)card.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (customerId is not null)
        {
            await desk.ReverseAsync(customerId.Value, source, "Kaydedilemeyen sipariş", voidSource: false, ct);
        }
    }

    private Task<bool> EnabledAsync(CancellationToken ct) => modules.IsEnabledAsync(tenant.TenantId, LoyaltyModule.Key, ct);
}
