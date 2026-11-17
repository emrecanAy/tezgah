using System.Net.Http.Json;
using Tezgah.Loyalty;
using Tezgah.Orders;

namespace Tezgah.Tests.Support;

/// <summary>A café with a small menu, a customer and (optionally) a coffee card, for driving loyalty through real orders.</summary>
internal sealed record Cafe(Company Company, Guid CustomerId, Guid Latte, Guid TurkishCoffee, Guid Brownie)
{
    public const decimal LattePrice = 130m;
    public const decimal TurkishCoffeePrice = 90m;
    public const decimal BrowniePrice = 140m;

    public HttpClient Api => Company.Api;

    public static async Task<Cafe> OpenAsync(TezgahApp app, params string[] modules)
    {
        var company = await app.OpenCompanyAsync(modules.Length > 0 ? modules : ["customers", "orders", "loyalty"]);
        return new Cafe(
            company,
            await company.AddCustomerAsync(),
            await AddMenuItemAsync(company, "Latte", "Kahve", LattePrice),
            await AddMenuItemAsync(company, "Türk Kahvesi", "Kahve", TurkishCoffeePrice),
            await AddMenuItemAsync(company, "Brownie", "Tatlı", BrowniePrice));
    }

    public async Task<Guid> AddProgramAsync(int stampsRequired, string earnRule = "item", string? category = "Kahve")
    {
        var program = await Api.PostAsync<LoyaltyEndpoints.ProgramResponse>(
            "/api/loyalty/programs", new { name = "Kahve Kartı", stampsRequired, reward = "1 kahve bizden", earnRule, category });
        return program.Id;
    }

    public Task<OrderEndpoints.OrderResponse> OrderAsync(params (Guid Item, int Quantity)[] items) =>
        Api.PostAsync<OrderEndpoints.OrderResponse>("/api/orders", Body(Guid.NewGuid(), CustomerId, items, rewardCardId: null));

    public Task<HttpResponseMessage> SendOrderAsync(
        Guid orderId, Guid? customerId, Guid? rewardCardId, params (Guid Item, int Quantity)[] items) =>
        Api.PostAsJsonAsync("/api/orders", Body(orderId, customerId, items, rewardCardId));

    public async Task<List<CardResponse>> CardsAsync(Guid programId) =>
        await Api.GetAsync<List<CardResponse>>($"/api/loyalty/programs/{programId}/cards");

    /// <summary>Stamps arrive through the outbox; waits until the customer's card reads as expected.</summary>
    public async Task<CardResponse> CardWhenAsync(Guid programId, Func<CardResponse, bool> done) =>
        (await Eventually.ValueAsync(() => CardsAsync(programId), cards => cards.Count == 1 && done(cards[0])))[0];

    private static object Body(Guid id, Guid? customerId, (Guid Item, int Quantity)[] items, Guid? rewardCardId) => new
    {
        id,
        customerId,
        items = items.Select(item => new { menuItemId = item.Item, quantity = item.Quantity }),
        rewardCardId,
    };

    private static async Task<Guid> AddMenuItemAsync(Company company, string name, string category, decimal price) =>
        (await company.Api.PostAsync<OrderEndpoints.MenuItemResponse>("/api/orders/menu", new { name, category, price })).Id;
}
