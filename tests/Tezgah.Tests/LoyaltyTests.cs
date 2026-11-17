using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Loyalty;
using Tezgah.Orders;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

/// <summary>
/// Stamps come only from orders and appointments. Every test here rings up real orders and watches
/// the card fill through the outbox, the same way it does in use.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class LoyaltyTests(TezgahApp app)
{
    [Fact]
    public async Task Every_coffee_sold_earns_a_stamp_and_the_fourth_fills_the_card()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4);

        await cafe.OrderAsync((cafe.Latte, 1));
        await cafe.OrderAsync((cafe.Latte, 2), (cafe.TurkishCoffee, 1), (cafe.Brownie, 1));

        // 1 + 3 coffees; the brownie is not a coffee.
        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);
        Assert.Equal((0, 1), (card.Stamps, card.RewardsAvailable));
    }

    [Fact]
    public async Task With_the_order_rule_an_order_earns_one_stamp_however_many_items_it_has()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4, earnRule: "order", category: null);

        await cafe.OrderAsync((cafe.Latte, 3), (cafe.Brownie, 2));
        await cafe.OrderAsync((cafe.Brownie, 1));

        var card = await cafe.CardWhenAsync(programId, card => card.Stamps == 2);
        Assert.Equal(0, card.RewardsAvailable);
    }

    [Fact]
    public async Task Orders_without_a_qualifying_item_or_without_a_customer_earn_nothing()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4);

        await cafe.OrderAsync((cafe.Brownie, 3));
        (await cafe.SendOrderAsync(Guid.NewGuid(), customerId: null, rewardCardId: null, (cafe.Latte, 5))).EnsureSuccessStatusCode();
        await cafe.OrderAsync((cafe.Latte, 1));

        // The outbox delivers in order: once the last order's stamp is there, the first two were already handled.
        var card = await cafe.CardWhenAsync(programId, card => card.Stamps > 0);
        Assert.Equal(1, card.Stamps);
    }

    [Fact]
    public async Task There_is_no_way_to_stamp_a_card_by_hand()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4);

        var response = await cafe.Api.PostAsJsonAsync($"/api/loyalty/programs/{programId}/stamps", new { cafe.CustomerId });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(await cafe.CardsAsync(programId));
    }

    /// <summary>A double tap or a retry after a timeout sends the same order again; it is recorded and stamped once.</summary>
    [Fact]
    public async Task The_same_order_sent_several_times_at_once_is_recorded_and_stamped_once()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 10);
        var orderId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            cafe.SendOrderAsync(orderId, cafe.CustomerId, rewardCardId: null, (cafe.Latte, 2))));
        await cafe.OrderAsync((cafe.Latte, 1));

        Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode));
        var card = await cafe.CardWhenAsync(programId, card => card.Stamps >= 3);
        Assert.Equal(3, card.Stamps);
    }

    /// <summary>Orders for one customer at several tills at once: every stamp counts, including the ones racing to open the card.</summary>
    [Fact]
    public async Task Stamps_from_orders_rung_up_at_the_same_moment_are_all_counted()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 5);

        await Task.WhenAll(Enumerable.Range(0, 23).Select(_ => cafe.OrderAsync((cafe.Latte, 1))));

        // Waits for all 23 stamps (4 full cards of 5, plus 3); a lost stamp keeps it waiting, an extra one fails below.
        var card = await cafe.CardWhenAsync(programId, card => (card.RewardsAvailable * 5) + card.Stamps >= 23);
        Assert.Equal((3, 4), (card.Stamps, card.RewardsAvailable));
    }

    [Fact]
    public async Task Cancelling_an_order_takes_its_stamps_back()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 5);
        var order = await cafe.OrderAsync((cafe.Latte, 3));
        await cafe.CardWhenAsync(programId, card => card.Stamps == 3);

        (await cafe.Api.PostAsync($"/api/orders/{order.Id}/cancel", null)).EnsureSuccessStatusCode();
        var again = await cafe.Api.PostAsync($"/api/orders/{order.Id}/cancel", null);

        Assert.Equal("order_cancelled", await again.ErrorCodeAsync());
        await cafe.CardWhenAsync(programId, card => card.Stamps == 0);
    }

    [Fact]
    public async Task Cancelling_the_order_that_filled_the_card_also_takes_back_the_unspent_reward()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4);
        await cafe.OrderAsync((cafe.Latte, 1));
        var filling = await cafe.OrderAsync((cafe.Latte, 3));
        await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);

        (await cafe.Api.PostAsync($"/api/orders/{filling.Id}/cancel", null)).EnsureSuccessStatusCode();

        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 0);
        Assert.Equal(1, card.Stamps);
    }

    /// <summary>
    /// A stamp event that failed and is retried can arrive after its order's cancellation was handled.
    /// The cancellation leaves a mark, so the late stamp is refused instead of outliving the order.
    /// </summary>
    [Fact]
    public async Task A_stamp_arriving_after_its_order_was_cancelled_is_refused()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 4);
        var (orderId, now) = (Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (var scope = app.ScopeFor(cafe.Company.Id))
        {
            await scope.ServiceProvider.GetRequiredService<OrderCancelledHandler>()
                .HandleAsync(new OrderCancelled(cafe.Company.Id, orderId, cafe.CustomerId, now), CancellationToken.None);
            var lattes = new OrderedItem("Latte", "Kahve", Quantity: 2, FreeQuantity: 0);
            await scope.ServiceProvider.GetRequiredService<OrderPlacedHandler>()
                .HandleAsync(new OrderPlaced(cafe.Company.Id, orderId, cafe.CustomerId, [lattes], 260m, now), CancellationToken.None);
        }

        Assert.Empty(await cafe.CardsAsync(programId));
    }

    [Fact]
    public async Task A_reward_spent_at_the_till_makes_the_cheapest_coffee_free_and_the_free_coffee_earns_no_stamp()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 2);
        await cafe.OrderAsync((cafe.Latte, 2));
        var full = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);

        var offer = Assert.Single(await cafe.Api.GetAsync<List<RewardOffer>>($"/api/orders/rewards?customerId={cafe.CustomerId}"));
        var response = await cafe.SendOrderAsync(
            Guid.NewGuid(), cafe.CustomerId, offer.CardId, (cafe.Latte, 1), (cafe.TurkishCoffee, 1), (cafe.Brownie, 1));
        var order = (await response.Content.ReadFromJsonAsync<OrderEndpoints.OrderResponse>())!;

        Assert.Equal(full.Id, offer.CardId);
        Assert.Equal(Cafe.LattePrice + Cafe.TurkishCoffeePrice + Cafe.BrowniePrice, order.Subtotal);
        Assert.Equal(Cafe.TurkishCoffeePrice, order.Discount);
        Assert.Equal(Cafe.LattePrice + Cafe.BrowniePrice, order.Total);
        Assert.Equal(1, Assert.Single(order.Lines, line => line.Name == "Türk Kahvesi").FreeQuantity);

        // The reward is gone at once; the paid latte's stamp follows through the outbox.
        var card = await cafe.CardWhenAsync(programId, card => card.Stamps == 1);
        Assert.Equal((0, 1), (card.RewardsAvailable, card.RewardsRedeemed));
    }

    [Fact]
    public async Task One_reward_spent_from_several_tills_at_once_goes_to_one_order()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 2);
        await cafe.OrderAsync((cafe.Latte, 2));
        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            cafe.SendOrderAsync(Guid.NewGuid(), cafe.CustomerId, card.Id, (cafe.Latte, 1))));

        Assert.Single(attempts, response => response.IsSuccessStatusCode);
        foreach (var refused in attempts.Where(response => !response.IsSuccessStatusCode))
        {
            Assert.Equal("no_reward_available", await refused.ErrorCodeAsync());
        }
    }

    [Fact]
    public async Task Cancelling_an_order_gives_back_the_reward_spent_on_it()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 2, earnRule: "order", category: null);
        await cafe.OrderAsync((cafe.Latte, 1));
        await cafe.OrderAsync((cafe.Latte, 1));
        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);
        var withReward = await cafe.SendOrderAsync(Guid.NewGuid(), cafe.CustomerId, card.Id, (cafe.Latte, 1));
        var order = (await withReward.Content.ReadFromJsonAsync<OrderEndpoints.OrderResponse>())!;
        Assert.Equal(0m, order.Total);

        (await cafe.Api.PostAsync($"/api/orders/{order.Id}/cancel", null)).EnsureSuccessStatusCode();

        var after = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);
        Assert.Equal(0, after.RewardsRedeemed);
    }

    [Fact]
    public async Task A_reward_for_coffee_is_refused_on_an_order_without_coffee_and_stays_on_the_card()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 2);
        await cafe.OrderAsync((cafe.Latte, 2));
        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);

        var response = await cafe.SendOrderAsync(Guid.NewGuid(), cafe.CustomerId, card.Id, (cafe.Brownie, 1));

        Assert.Equal("reward_not_applicable", await response.ErrorCodeAsync());
        Assert.Equal(1, Assert.Single(await cafe.CardsAsync(programId)).RewardsAvailable);
    }

    [Fact]
    public async Task The_reward_of_an_order_program_is_spent_at_the_till_not_handed_out_at_the_counter()
    {
        var cafe = await Cafe.OpenAsync(app);
        var programId = await cafe.AddProgramAsync(stampsRequired: 2);
        await cafe.OrderAsync((cafe.Latte, 2));
        var card = await cafe.CardWhenAsync(programId, card => card.RewardsAvailable == 1);

        var response = await cafe.Api.PostAsync($"/api/loyalty/cards/{card.Id}/redemptions", null);

        Assert.Equal("redeem_at_till", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task A_paused_program_gives_no_stamps()
    {
        var cafe = await Cafe.OpenAsync(app);
        var paused = await cafe.AddProgramAsync(stampsRequired: 4);
        var active = await cafe.AddProgramAsync(stampsRequired: 4, earnRule: "order", category: null);
        (await cafe.Api.PutAsJsonAsync($"/api/loyalty/programs/{paused}",
            new { name = "Kahve Kartı", stampsRequired = 4, reward = "1 kahve", earnRule = "item", category = "Kahve", isActive = false }))
            .EnsureSuccessStatusCode();

        await cafe.OrderAsync((cafe.Latte, 1));

        await cafe.CardWhenAsync(active, card => card.Stamps == 1);
        Assert.Empty(await cafe.CardsAsync(paused));
    }

    [Fact]
    public async Task A_program_needs_a_known_way_of_earning_stamps()
    {
        var cafe = await Cafe.OpenAsync(app);

        var response = await cafe.Api.PostAsJsonAsync(
            "/api/loyalty/programs", new { name = "Kart", stampsRequired = 4, reward = "Hediye", earnRule = "manual" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_earn_rule", await response.ErrorCodeAsync());
    }
}
