using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>
/// The only code that changes cards. Every change to a customer's cards runs under a lock on that
/// customer, so orders rung up at two tills, a cancellation and a redemption arriving at the same
/// moment are applied one after the other: no stamp is lost, no reward is spent twice, and a
/// cancellation cannot slip past a stamp that is still being written.
/// </summary>
internal sealed class CardDesk(LoyaltyDbContext db, ICustomerDirectory customers, TenantContext tenant, TimeProvider time)
{
    /// <param name="source">What earned the stamps, e.g. <c>order:{id}</c>. Stamping again for the same source changes nothing.</param>
    public async Task<Result<StampResponse>> StampAsync(
        Guid programId, Guid customerId, int count, string source, string? note, string stampedBy, CancellationToken ct)
    {
        var program = await db.Programs.SingleOrDefaultAsync(p => p.Id == programId, ct);
        if (program is null)
        {
            return Error.NotFound("program_not_found", "This loyalty program does not exist.");
        }

        if (!program.IsActive)
        {
            return Error.Conflict("program_inactive", "This loyalty program is paused.");
        }

        var customer = (await customers.GetAsync([customerId], ct)).GetValueOrDefault(customerId);
        if (customer is null)
        {
            return Error.NotFound("customer_not_found", "This customer does not exist.");
        }

        var stamped = await ForCustomerAsync<StampResponse?>(customerId, async () =>
        {
            if (await db.VoidedSources.AnyAsync(v => v.Source == source, ct))
            {
                return null;
            }

            var card = await db.Cards.SingleOrDefaultAsync(c => c.ProgramId == programId && c.CustomerId == customerId, ct);
            if (card is not null && await HasEntryAsync(card.Id, CardEntryKind.Stamp, source, ct))
            {
                return new StampResponse(CardResponse.From(card, program, customer), RewardsEarned: 0);
            }

            var now = time.GetUtcNow();
            if (card is null)
            {
                card = LoyaltyCard.Open(programId, customerId, now);
                db.Cards.Add(card);
            }

            var rewards = card.AddStamps(count, program.StampsRequired, now);
            db.Entries.Add(CardEntry.Record(card.Id, CardEntryKind.Stamp, count, note, source, stampedBy, now));
            await db.SaveChangesAsync(ct);
            return new StampResponse(CardResponse.From(card, program, customer), rewards);
        }, ct);

        return stamped is null
            ? Error.Conflict("source_voided", "What earned these stamps has been cancelled.")
            : stamped;
    }

    /// <summary>
    /// A reward handed over at the counter, for appointment programs. Rewards of order programs are
    /// spent on an order instead, so that the free item shows up in the order's total.
    /// </summary>
    public async Task<Result<CardResponse>> RedeemAsync(Guid cardId, string redeemedBy, CancellationToken ct)
    {
        var customerId = await db.Cards.Where(c => c.Id == cardId).Select(c => (Guid?)c.CustomerId).SingleOrDefaultAsync(ct);
        if (customerId is null)
        {
            return CardNotFound();
        }

        var redeemed = await ForCustomerAsync<Result<(LoyaltyCard Card, LoyaltyProgram Program)>>(customerId.Value, async () =>
        {
            var programId = await db.Cards.Where(c => c.Id == cardId).Select(c => c.ProgramId).SingleAsync(ct);
            if (await db.Programs.AnyAsync(p => p.Id == programId && p.EarnRule != EarnRule.Appointment, ct))
            {
                return Error.Conflict("redeem_at_till", "This program's rewards are spent on an order at the till.");
            }

            return await RedeemLockedAsync(cardId, source: null, redeemedBy, ct);
        }, ct);
        if (!redeemed.IsSuccess)
        {
            return redeemed.Error;
        }

        var (card, program) = redeemed.Value;
        var customer = (await customers.GetAsync([card.CustomerId], ct)).GetValueOrDefault(card.CustomerId);
        return CardResponse.From(card, program, customer);
    }

    /// <summary>Spends a reward on an order. Claiming again for the same order returns the first claim.</summary>
    public async Task<Result<RewardClaim>> ClaimForOrderAsync(Guid cardId, Guid customerId, Guid orderId, CancellationToken ct)
    {
        var source = OrderSource(orderId);
        var claimed = await ForCustomerAsync<Result<(LoyaltyCard Card, LoyaltyProgram Program)>>(customerId, async () =>
        {
            var card = await db.Cards.SingleOrDefaultAsync(c => c.Id == cardId && c.CustomerId == customerId, ct);
            if (card is null)
            {
                return CardNotFound();
            }

            var program = await db.Programs.SingleAsync(p => p.Id == card.ProgramId, ct);
            if (!program.IsActive || !program.IsEarnedWithOrders)
            {
                return Error.Conflict("reward_not_for_orders", "This program's rewards cannot be spent on an order.");
            }

            if (await HasEntryAsync(card.Id, CardEntryKind.Redemption, source, ct))
            {
                return (card, program);
            }

            return await RedeemLockedAsync(cardId, source, "Sipariş", ct);
        }, ct);

        return claimed.IsSuccess
            ? new RewardClaim(claimed.Value.Program.Name, claimed.Value.Program.Reward, claimed.Value.Program.Category)
            : claimed.Error;
    }

    /// <summary>
    /// Undoes everything <paramref name="source"/> did to the customer's cards: stamps are taken back and
    /// spent rewards returned. Running it again changes nothing.
    /// </summary>
    /// <param name="voidSource">
    /// Also refuse any stamp for this source that arrives later. Set when an order is cancelled; not when
    /// a claim is released because its order could not be saved, since the till may retry that order.
    /// </param>
    public Task ReverseAsync(Guid customerId, string source, string reversedBy, bool voidSource, CancellationToken ct) =>
        ForCustomerAsync(customerId, async () =>
        {
            var now = time.GetUtcNow();
            if (voidSource && !await db.VoidedSources.AnyAsync(v => v.Source == source, ct))
            {
                db.VoidedSources.Add(VoidedSource.Record(source, now));
            }

            var entries = await db.Entries
                .Where(e => e.Source == source && (e.Kind == CardEntryKind.Stamp || e.Kind == CardEntryKind.Redemption))
                .ToListAsync(ct);
            foreach (var entry in entries)
            {
                var reversal = entry.Kind == CardEntryKind.Stamp ? CardEntryKind.StampReversal : CardEntryKind.RedemptionReversal;
                if (await HasEntryAsync(entry.CardId, reversal, source, ct))
                {
                    continue;
                }

                var card = await db.Cards.SingleAsync(c => c.Id == entry.CardId, ct);
                if (entry.Kind == CardEntryKind.Stamp)
                {
                    var program = await db.Programs.SingleAsync(p => p.Id == card.ProgramId, ct);
                    card.RemoveStamps(entry.Quantity, program.StampsRequired, now);
                }
                else
                {
                    card.ReturnReward(now);
                }

                db.Entries.Add(CardEntry.Record(card.Id, reversal, entry.Quantity, null, source, reversedBy, now));
            }

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    public static string OrderSource(Guid orderId) => $"order:{orderId}";

    private async Task<Result<(LoyaltyCard Card, LoyaltyProgram Program)>> RedeemLockedAsync(
        Guid cardId, string? source, string redeemedBy, CancellationToken ct)
    {
        var card = await db.Cards.SingleAsync(c => c.Id == cardId, ct);
        var program = await db.Programs.SingleAsync(p => p.Id == card.ProgramId, ct);
        var now = time.GetUtcNow();

        var redeemed = card.Redeem(now);
        if (!redeemed.IsSuccess)
        {
            return redeemed.Error;
        }

        db.Entries.Add(CardEntry.Record(card.Id, CardEntryKind.Redemption, 1, null, source, redeemedBy, now));
        await db.SaveChangesAsync(ct);
        return (card, program);
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction that first takes a lock on the customer. The lock
    /// is a PostgreSQL advisory lock rather than a row lock because the card may not exist yet: two
    /// first orders racing to open it have no row to lock. It is released when the transaction ends.
    /// </summary>
    private async Task<T> ForCustomerAsync<T>(Guid customerId, Func<Task<T>> work, CancellationToken ct)
    {
        var key = $"loyalty:{tenant.TenantId}:{customerId}";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);

        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }

    private Task<bool> HasEntryAsync(Guid cardId, CardEntryKind kind, string source, CancellationToken ct) =>
        db.Entries.AnyAsync(e => e.CardId == cardId && e.Kind == kind && e.Source == source, ct);

    private static Error CardNotFound() => Error.NotFound("card_not_found", "This card does not exist.");
}
