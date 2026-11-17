using System.Net;
using System.Net.Http.Json;
using Tezgah.Inventory;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class InventoryTests(TezgahApp app)
{
    [Fact]
    public async Task Stock_cannot_be_taken_below_zero()
    {
        var (company, productId) = await ProductAsync(openingStock: 5);

        var tooMuch = await MoveAsync(company, productId, "out", 6);

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal("insufficient_stock", await tooMuch.ErrorCodeAsync());
        Assert.Equal(5m, (await ProductsAsync(company)).Single().Stock);
    }

    [Fact]
    public async Task Thirty_people_taking_one_item_each_from_a_stock_of_ten_empty_it_exactly()
    {
        var (company, productId) = await ProductAsync(openingStock: 10);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => MoveAsync(company, productId, "out", 1)));

        Assert.Equal(10, attempts.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(20, attempts.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0m, (await ProductsAsync(company)).Single().Stock);
    }

    [Fact]
    public async Task A_product_at_or_below_its_reorder_level_is_flagged_and_restocking_clears_it()
    {
        var (company, productId) = await ProductAsync(openingStock: 12, reorderLevel: 10);
        Assert.False((await ProductsAsync(company)).Single().RunningLow);

        (await MoveAsync(company, productId, "out", 2)).EnsureSuccessStatusCode();
        Assert.True((await ProductsAsync(company)).Single().RunningLow);

        (await MoveAsync(company, productId, "in", 50)).EnsureSuccessStatusCode();
        Assert.False((await ProductsAsync(company)).Single().RunningLow);
    }

    [Fact]
    public async Task Editing_a_product_does_not_overwrite_stock_that_moved_in_the_meantime()
    {
        var (company, productId) = await ProductAsync(openingStock: 10);
        (await MoveAsync(company, productId, "out", 4)).EnsureSuccessStatusCode();

        (await company.Api.PutAsJsonAsync($"/api/inventory/products/{productId}",
            new { name = "Yeni İsim", unit = "adet", reorderLevel = 1 })).EnsureSuccessStatusCode();

        Assert.Equal(("Yeni İsim", 6m), (await ProductsAsync(company)).Select(p => (p.Name, p.Stock)).Single());
    }

    [Fact]
    public async Task The_opening_stock_is_the_products_first_movement()
    {
        var (company, productId) = await ProductAsync(openingStock: 18.5m, unit: "kg");

        var movement = Assert.Single(await company.Api.GetAsync<List<InventoryEndpoints.MovementResponse>>(
            $"/api/inventory/products/{productId}/movements"));

        Assert.Equal(("in", 18.5m, "Açılış stoğu"), (movement.Kind, movement.Quantity, movement.Note));
        Assert.Equal(18.5m, (await ProductsAsync(company)).Single().Stock);
    }

    [Fact]
    public async Task A_product_added_without_opening_stock_starts_empty_with_no_history()
    {
        var (company, productId) = await ProductAsync(openingStock: 0);

        Assert.Empty(await company.Api.GetAsync<List<InventoryEndpoints.MovementResponse>>(
            $"/api/inventory/products/{productId}/movements"));
        Assert.Equal(0m, (await ProductsAsync(company)).Single().Stock);
    }

    [Fact]
    public async Task Counted_units_take_whole_numbers_and_measured_units_take_decimals()
    {
        var (cups, cupsId) = await ProductAsync(openingStock: 10, unit: "adet");
        var (beans, beansId) = await ProductAsync(openingStock: 10, unit: "kg");

        var halfCup = await MoveAsync(cups, cupsId, "out", 2.5m);
        var halfKilo = await MoveAsync(beans, beansId, "out", 2.5m);

        Assert.Equal("invalid_quantity", await halfCup.ErrorCodeAsync());
        halfKilo.EnsureSuccessStatusCode();
        Assert.Equal(7.5m, (await ProductsAsync(beans)).Single().Stock);
    }

    [Theory]
    [InlineData("kilo")]
    [InlineData("5")]
    [InlineData("")]
    public async Task A_unit_outside_the_list_is_refused(string unit)
    {
        var company = await app.OpenCompanyAsync("inventory");

        var response = await company.Api.PostAsJsonAsync(
            "/api/inventory/products", new { name = "Süt", unit, reorderLevel = 0, openingStock = 0 });

        Assert.Equal("invalid_unit", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task The_unit_of_a_product_in_stock_cannot_change()
    {
        var (company, productId) = await ProductAsync(openingStock: 12, unit: "kg");

        var response = await company.Api.PutAsJsonAsync(
            $"/api/inventory/products/{productId}", new { name = "Karton bardak", unit = "adet", reorderLevel = 0 });

        Assert.Equal("unit_locked", await response.ErrorCodeAsync());
    }

    private async Task<(Company Company, Guid ProductId)> ProductAsync(
        decimal openingStock, decimal reorderLevel = 0, string unit = "adet")
    {
        var company = await app.OpenCompanyAsync("inventory");
        var product = await company.Api.PostAsync<InventoryEndpoints.ProductResponse>(
            "/api/inventory/products", new { name = "Karton bardak", unit, reorderLevel, openingStock });
        return (company, product.Id);
    }

    private static Task<HttpResponseMessage> MoveAsync(Company company, Guid productId, string kind, decimal quantity) =>
        company.Api.PostAsJsonAsync($"/api/inventory/products/{productId}/movements", new { kind, quantity });

    private static Task<List<InventoryEndpoints.ProductResponse>> ProductsAsync(Company company) =>
        company.Api.GetAsync<List<InventoryEndpoints.ProductResponse>>("/api/inventory/products");
}
