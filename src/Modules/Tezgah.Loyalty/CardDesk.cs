using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

/// <summary>
/// Stamps and redeems cards. Both operations take a row lock on the card first, so two tills
/// serving the same customer at the same moment each count: neither stamp is lost and a reward
/// cannot be handed out twice.
/// </summary>
internal sealed class CardDesk(LoyaltyDbContext db, ICustomerDirectory customers, TenantContext tenant, TimeProvider time)
{
    /// <param name="source">
    /// Identifies an automatic stamp. Stamping again with the same source changes nothing.
    /// </param>
    public async Task<Result<StampResponse>> StampAsync(
        Guid programId, Guid customerId, string? note, string? source, string stampedBy, CancellationToken ct)
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

        try
        {
            return await StampLockedAsync(program, customer, note, source, stampedBy, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another request opened the customer's card (or applied the same automatic stamp) a moment
            // earlier. The card exists now, so the second pass takes the ordinary locked path.
            db.ChangeTracker.Clear();
            return await StampLockedAsync(program, customer, note, source, stampedBy, ct);
        }
    }

    public async Task<Result<CardResponse>> RedeemAsync(Guid cardId, string redeemedBy, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var card = await LockAsync(
            db.Cards.FromSql($"SELECT * FROM loyalty.cards WHERE tenant_id = {tenant.TenantId} AND id = {cardId} FOR UPDATE"), ct);
        if (card is null)
        {
            return Error.NotFound("card_not_found", "This card does not exist.");
        }

        var now = time.GetUtcNow();
        var redeemed = card.Redeem(now);
        if (!redeemed.IsSuccess)
        {
            return redeemed.Error;
        }

        db.Entries.Add(CardEntry.Record(card.Id, CardEntryKind.Redemption, null, null, redeemedBy, now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var program = await db.Programs.SingleAsync(p => p.Id == card.ProgramId, ct);
        var customer = (await customers.GetAsync([card.CustomerId], ct)).GetValueOrDefault(card.CustomerId);
        return CardResponse.From(card, program, customer);
    }

    private async Task<Result<StampResponse>> StampLockedAsync(
        LoyaltyProgram program, CustomerRef customer, string? note, string? source, string stampedBy, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var card = await LockAsync(
            db.Cards.FromSql($"""
                SELECT * FROM loyalty.cards
                WHERE tenant_id = {tenant.TenantId} AND program_id = {program.Id} AND customer_id = {customer.Id}
                FOR UPDATE
                """),
            ct);
        if (card is null)
        {
            card = LoyaltyCard.Open(program.Id, customer.Id, now);
            db.Cards.Add(card);
            await db.SaveChangesAsync(ct);
        }

        if (source is not null && await db.Entries.AnyAsync(entry => entry.CardId == card.Id && entry.Source == source, ct))
        {
            return new StampResponse(CardResponse.From(card, program, customer), RewardEarned: false);
        }

        var rewardEarned = card.AddStamp(program.StampsRequired, now);
        db.Entries.Add(CardEntry.Record(card.Id, CardEntryKind.Stamp, note, source, stampedBy, now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new StampResponse(CardResponse.From(card, program, customer), rewardEarned);
    }

    // The tenant filter is written into the SQL above because FOR UPDATE must stay the last clause;
    // letting EF append its own filter would wrap the statement in a sub-select.
    private static async Task<LoyaltyCard?> LockAsync(IQueryable<LoyaltyCard> lockingQuery, CancellationToken ct) =>
        (await lockingQuery.IgnoreQueryFilters().ToListAsync(ct)).SingleOrDefault();
}
