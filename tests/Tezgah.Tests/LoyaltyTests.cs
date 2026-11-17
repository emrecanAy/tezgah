using System.Net;
using System.Net.Http.Json;
using Tezgah.Loyalty;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class LoyaltyTests(TezgahApp app)
{
    [Fact]
    public async Task The_fourth_stamp_fills_the_card_earns_the_reward_and_starts_a_new_card()
    {
        var (cafe, programId, customerId) = await CafeWithProgramAsync(stampsRequired: 4);

        StampResponse last = null!;
        for (var coffee = 1; coffee <= 4; coffee++)
        {
            last = await StampAsync(cafe, programId, customerId);
            Assert.Equal(coffee == 4, last.RewardEarned);
        }

        Assert.Equal(0, last.Card.Stamps);
        Assert.Equal(1, last.Card.RewardsAvailable);
    }

    [Fact]
    public async Task A_reward_can_be_redeemed_once()
    {
        var (cafe, programId, customerId) = await CafeWithProgramAsync(stampsRequired: 2);
        await StampAsync(cafe, programId, customerId);
        var card = (await StampAsync(cafe, programId, customerId)).Card;

        var redeemed = await cafe.Api.PostAsync<CardResponse>($"/api/loyalty/cards/{card.Id}/redemptions");
        var again = await cafe.Api.PostAsync($"/api/loyalty/cards/{card.Id}/redemptions", null);

        Assert.Equal((0, 1), (redeemed.RewardsAvailable, redeemed.RewardsRedeemed));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("no_reward_available", await again.ErrorCodeAsync());
    }

    /// <summary>Two tills, one customer: every stamp must count, including the very first ones that race to open the card.</summary>
    [Fact]
    public async Task Stamps_given_at_the_same_moment_are_all_counted()
    {
        var (cafe, programId, customerId) = await CafeWithProgramAsync(stampsRequired: 5);

        await Task.WhenAll(Enumerable.Range(0, 23).Select(_ => StampAsync(cafe, programId, customerId)));

        var card = Assert.Single(await cafe.Api.GetAsync<List<CardResponse>>($"/api/loyalty/programs/{programId}/cards"));
        Assert.Equal((3, 4), (card.Stamps, card.RewardsAvailable));
    }

    [Fact]
    public async Task One_reward_redeemed_from_several_tills_at_once_is_handed_out_once()
    {
        var (cafe, programId, customerId) = await CafeWithProgramAsync(stampsRequired: 2);
        await StampAsync(cafe, programId, customerId);
        var card = (await StampAsync(cafe, programId, customerId)).Card;

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            cafe.Api.PostAsync($"/api/loyalty/cards/{card.Id}/redemptions", null)));

        Assert.Single(attempts, response => response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_paused_program_gives_no_stamps()
    {
        var (cafe, programId, customerId) = await CafeWithProgramAsync(stampsRequired: 4);
        (await cafe.Api.PutAsJsonAsync($"/api/loyalty/programs/{programId}",
            new { name = "Kahve Kartı", stampsRequired = 4, reward = "1 kahve", isActive = false })).EnsureSuccessStatusCode();

        var response = await cafe.Api.PostAsJsonAsync($"/api/loyalty/programs/{programId}/stamps", new { customerId });

        Assert.Equal("program_inactive", await response.ErrorCodeAsync());
    }

    private async Task<(Company Cafe, Guid ProgramId, Guid CustomerId)> CafeWithProgramAsync(int stampsRequired)
    {
        var cafe = await app.OpenCompanyAsync("customers", "loyalty");
        var program = await cafe.Api.PostAsync<LoyaltyEndpoints.ProgramResponse>(
            "/api/loyalty/programs", new { name = "Kahve Kartı", stampsRequired, reward = "1 kahve" });
        return (cafe, program.Id, await cafe.AddCustomerAsync());
    }

    private static Task<StampResponse> StampAsync(Company company, Guid programId, Guid customerId) =>
        company.Api.PostAsync<StampResponse>($"/api/loyalty/programs/{programId}/stamps", new { customerId });
}
