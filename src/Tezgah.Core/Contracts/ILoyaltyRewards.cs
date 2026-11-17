using Tezgah.Core.Results;

namespace Tezgah.Core.Contracts;

/// <summary>
/// What the till may ask the Loyalty module: which rewards a customer can use now, and to spend
/// one on an order. Spending has to happen while the order is rung up, because it changes the
/// price, so this is a direct call rather than an event.
/// </summary>
public interface ILoyaltyRewards
{
    /// <summary>Rewards the customer can spend on an order. Empty when the company has no loyalty module.</summary>
    Task<IReadOnlyList<RewardOffer>> AvailableAsync(Guid customerId, CancellationToken ct);

    /// <summary>
    /// Spends one reward from the card. Claiming again with the same <paramref name="orderId"/> spends
    /// nothing more, so a till that retries a timed-out order cannot use up two rewards.
    /// </summary>
    Task<Result<RewardClaim>> ClaimAsync(Guid cardId, Guid customerId, Guid orderId, CancellationToken ct);

    /// <summary>Gives back a reward claimed for an order that was never saved.</summary>
    Task ReleaseAsync(Guid orderId, CancellationToken ct);
}

/// <param name="Category">The menu category the reward makes free, or <c>null</c> for any item.</param>
public sealed record RewardOffer(Guid CardId, string ProgramName, string Reward, string? Category, int Available);

/// <param name="Category">The menu category the reward makes free, or <c>null</c> for any item.</param>
public sealed record RewardClaim(string ProgramName, string Reward, string? Category);

/// <summary>Used when the installation does not ship the Loyalty module: nobody has rewards to spend.</summary>
public sealed class NoLoyaltyRewards : ILoyaltyRewards
{
    public Task<IReadOnlyList<RewardOffer>> AvailableAsync(Guid customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RewardOffer>>([]);

    public Task<Result<RewardClaim>> ClaimAsync(Guid cardId, Guid customerId, Guid orderId, CancellationToken ct) =>
        Task.FromResult<Result<RewardClaim>>(Error.NotFound("card_not_found", "This card does not exist."));

    public Task ReleaseAsync(Guid orderId, CancellationToken ct) => Task.CompletedTask;
}
