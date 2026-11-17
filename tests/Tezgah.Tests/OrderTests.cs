using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Orders;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class OrderTests(TezgahApp app)
{
    [Fact]
    public async Task The_amounts_are_worked_out_from_the_menu_and_a_price_sent_by_the_till_is_ignored()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");

        var response = await cafe.Api.PostAsJsonAsync("/api/orders", new
        {
            id = Guid.NewGuid(),
            items = new object[]
            {
                new { menuItemId = cafe.Latte, quantity = 2, price = 1 },
                new { menuItemId = cafe.Brownie, quantity = 1 },
                new { menuItemId = cafe.Latte, quantity = 1 },
            },
            total = 1,
        });
        var order = (await response.Content.ReadFromJsonAsync<OrderEndpoints.OrderResponse>())!;

        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(3, Assert.Single(order.Lines, line => line.Name == "Latte").Quantity);
        Assert.Equal((3 * Cafe.LattePrice) + Cafe.BrowniePrice, order.Total);
        Assert.Equal(0m, order.Discount);
    }

    [Fact]
    public async Task A_price_change_on_the_menu_leaves_past_orders_as_they_were()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        var before = await cafe.OrderAsync((cafe.Latte, 1));

        (await cafe.Api.PutAsJsonAsync($"/api/orders/menu/{cafe.Latte}", new { name = "Latte", category = "Kahve", price = 150m }))
            .EnsureSuccessStatusCode();
        var after = await cafe.OrderAsync((cafe.Latte, 1));

        var today = await TodayAsync(cafe);
        Assert.Equal(Cafe.LattePrice, today.Single(order => order.Id == before.Id).Total);
        Assert.Equal(150m, after.Total);
    }

    [Fact]
    public async Task An_item_taken_off_the_menu_cannot_be_ordered()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        (await cafe.Api.PutAsJsonAsync($"/api/orders/menu/{cafe.Brownie}",
            new { name = "Brownie", category = "Tatlı", price = Cafe.BrowniePrice, isAvailable = false })).EnsureSuccessStatusCode();

        var response = await cafe.SendOrderAsync(Guid.NewGuid(), cafe.CustomerId, null, (cafe.Latte, 1), (cafe.Brownie, 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("menu_item_unavailable", await response.ErrorCodeAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task A_quantity_outside_1_to_99_is_refused(int quantity)
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");

        var response = await cafe.SendOrderAsync(Guid.NewGuid(), null, null, (cafe.Latte, quantity));

        Assert.Equal("invalid_order", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task An_order_cancelled_from_two_tills_at_once_is_cancelled_once()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        var order = await cafe.OrderAsync((cafe.Latte, 1));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            cafe.Api.PostAsync($"/api/orders/{order.Id}/cancel", null)));

        Assert.Single(attempts, response => response.IsSuccessStatusCode);
        await using var scope = app.ScopeFor(cafe.Company.Id);
        var outbox = scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Set<OutboxMessage>().AsNoTracking();
        var messages = await outbox.Where(m => m.Type == IntegrationEventRoutes.NameOf<OrderCancelled>()).ToListAsync();
        Assert.Single(messages, m => m.Payload.Contains(order.Id.ToString()));
    }

    [Fact]
    public async Task Cancelled_orders_do_not_count_towards_the_days_takings()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        await cafe.OrderAsync((cafe.Latte, 1));
        var cancelled = await cafe.OrderAsync((cafe.Brownie, 1));
        (await cafe.Api.PostAsync($"/api/orders/{cancelled.Id}/cancel", null)).EnsureSuccessStatusCode();

        var summary = await cafe.Api.GetAsync<OrderEndpoints.Summary>(
            $"/api/orders/summary?dayStart={Uri.EscapeDataString(DateTimeOffset.UtcNow.Date.ToString("O"))}");

        Assert.Equal((1, Cafe.LattePrice), (summary.OrdersToday, summary.RevenueToday));
    }

    [Fact]
    public async Task Without_the_loyalty_module_the_till_works_offers_no_rewards_and_its_events_go_nowhere()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");

        var order = await cafe.OrderAsync((cafe.Latte, 2));
        var rewards = await cafe.Api.GetAsync<List<RewardOffer>>($"/api/orders/rewards?customerId={cafe.CustomerId}");

        Assert.Empty(rewards);
        await using var scope = app.ScopeFor(cafe.Company.Id);
        var outbox = scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Set<OutboxMessage>().AsNoTracking();
        var message = await Eventually.ValueAsync(
            async () => (await outbox.ToListAsync()).Single(m => m.Payload.Contains(order.Id.ToString())),
            m => m.ProcessedAt is not null);
        Assert.Equal(0, message.Attempts);
    }

    private static async Task<List<OrderEndpoints.OrderResponse>> TodayAsync(Cafe cafe)
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        return await cafe.Api.GetAsync<List<OrderEndpoints.OrderResponse>>($"/api/orders?from={from}&to={to}");
    }
}
