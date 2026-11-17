using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Results;
using Tezgah.Core.Web;

namespace Tezgah.Loyalty;

internal static class LoyaltyEndpoints
{
    /// <param name="EarnRule"><c>appointment</c>, <c>order</c> or <c>item</c>.</param>
    /// <param name="Category">Only items of this menu category count, for order-based rules.</param>
    public sealed record ProgramRequest(
        string? Name, int StampsRequired, string? Reward, string? EarnRule, string? Category, bool? IsActive);

    public sealed record ProgramResponse(
        Guid Id, string Name, int StampsRequired, string Reward, string EarnRule, string? Category, bool IsActive, int CardCount)
    {
        public static ProgramResponse From(LoyaltyProgram program, int cardCount) => new(
            program.Id, program.Name, program.StampsRequired, program.Reward, program.EarnRule.ToString().ToLowerInvariant(),
            program.Category, program.IsActive, cardCount);
    }

    public sealed record EntryResponse(string Kind, int Quantity, string? Note, string CreatedBy, DateTimeOffset CreatedAt);

    public sealed record Summary(int ActivePrograms, int StampsThisWeek, int RewardsWaiting);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/programs", ListProgramsAsync);
        api.MapPost("/programs", CreateProgramAsync);
        api.MapPut("/programs/{programId:guid}", UpdateProgramAsync);
        api.MapGet("/programs/{programId:guid}/cards", ListCardsAsync);
        api.MapPost("/cards/{cardId:guid}/redemptions", RedeemAsync);
        api.MapGet("/cards/{cardId:guid}/entries", ListEntriesAsync);
        api.MapGet("/summary", SummaryAsync);
    }

    private static async Task<IReadOnlyList<ProgramResponse>> ListProgramsAsync(LoyaltyDbContext db, CancellationToken ct)
    {
        var programs = await db.Programs.AsNoTracking().OrderBy(program => program.CreatedAt).ToListAsync(ct);
        var cardCounts = await db.Cards
            .GroupBy(card => card.ProgramId)
            .Select(group => new { ProgramId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ProgramId, row => row.Count, ct);

        return programs.ConvertAll(program => ProgramResponse.From(program, cardCounts.GetValueOrDefault(program.Id)));
    }

    private static async Task<IResult> CreateProgramAsync(
        ProgramRequest request, LoyaltyDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (ParseEarnRule(request.EarnRule) is not { } earnRule)
        {
            return InvalidEarnRule().ToProblem();
        }

        var program = LoyaltyProgram.Create(
            request.Name, request.StampsRequired, request.Reward, earnRule, request.Category, time.GetUtcNow());
        if (!program.IsSuccess)
        {
            return program.Error.ToProblem();
        }

        db.Programs.Add(program.Value);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/loyalty/programs/{program.Value.Id}", ProgramResponse.From(program.Value, 0));
    }

    private static async Task<IResult> UpdateProgramAsync(
        Guid programId, ProgramRequest request, LoyaltyDbContext db, CancellationToken ct)
    {
        var program = await db.Programs.SingleOrDefaultAsync(p => p.Id == programId, ct);
        if (program is null)
        {
            return ProgramNotFound().ToProblem();
        }

        if (ParseEarnRule(request.EarnRule) is not { } earnRule)
        {
            return InvalidEarnRule().ToProblem();
        }

        var updated = program.Update(
            request.Name, request.StampsRequired, request.Reward, earnRule, request.Category, request.IsActive ?? program.IsActive);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ProgramResponse.From(program, await db.Cards.CountAsync(card => card.ProgramId == programId, ct)));
    }

    private static async Task<IResult> ListCardsAsync(
        Guid programId, LoyaltyDbContext db, ICustomerDirectory customers, CancellationToken ct)
    {
        var program = await db.Programs.AsNoTracking().SingleOrDefaultAsync(p => p.Id == programId, ct);
        if (program is null)
        {
            return ProgramNotFound().ToProblem();
        }

        var cards = await db.Cards.AsNoTracking()
            .Where(card => card.ProgramId == programId)
            .OrderByDescending(card => card.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);
        var names = await customers.GetAsync([.. cards.Select(card => card.CustomerId)], ct);

        return Results.Ok(cards.ConvertAll(card => CardResponse.From(card, program, names.GetValueOrDefault(card.CustomerId))));
    }

    private static async Task<IResult> RedeemAsync(Guid cardId, ClaimsPrincipal user, CardDesk desk, CancellationToken ct) =>
        (await desk.RedeemAsync(cardId, user.GetDisplayName(), ct)).ToHttp();

    private static async Task<IReadOnlyList<EntryResponse>> ListEntriesAsync(
        Guid cardId, LoyaltyDbContext db, CancellationToken ct)
    {
        var entries = await db.Entries.AsNoTracking()
            .Where(entry => entry.CardId == cardId)
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return entries.ConvertAll(entry => new EntryResponse(
            JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Kind.ToString()), entry.Quantity, entry.Note, entry.CreatedBy,
            entry.CreatedAt));
    }

    private static async Task<Summary> SummaryAsync(LoyaltyDbContext db, TimeProvider time, CancellationToken ct)
    {
        var weekAgo = time.GetUtcNow().AddDays(-7);
        var recent = db.Entries.Where(entry => entry.CreatedAt >= weekAgo);
        return new Summary(
            await db.Programs.CountAsync(program => program.IsActive, ct),
            await recent.Where(entry => entry.Kind == CardEntryKind.Stamp).SumAsync(entry => entry.Quantity, ct)
                - await recent.Where(entry => entry.Kind == CardEntryKind.StampReversal).SumAsync(entry => entry.Quantity, ct),
            await db.Cards.SumAsync(card => card.RewardsAvailable, ct));
    }

    private static EarnRule? ParseEarnRule(string? value) =>
        Enum.TryParse<EarnRule>(value, ignoreCase: true, out var rule) && Enum.IsDefined(rule) ? rule : null;

    private static Error InvalidEarnRule() =>
        Error.Validation("invalid_earn_rule", "Choose how stamps are earned: appointment, order or item.");

    private static Error ProgramNotFound() => Error.NotFound("program_not_found", "This loyalty program does not exist.");
}
