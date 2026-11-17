using System.Net;
using System.Net.Http.Json;
using Tezgah.Customers;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class TenantIsolationTests(TezgahApp app)
{
    [Fact]
    public async Task One_company_cannot_see_or_change_another_companys_customers()
    {
        var first = await app.OpenCompanyAsync("customers");
        var second = await app.OpenCompanyAsync("customers");
        var customerId = await first.AddCustomerAsync("Yalnızca Birincinin Müşterisi");

        var listedBySecond = await second.Api.GetAsync<List<CustomerEndpoints.CustomerResponse>>("/api/customers");
        var updateBySecond = await second.Api.PutAsJsonAsync(
            $"/api/customers/{customerId}", new { fullName = "Ele Geçirildi", phone = Http.RandomPhone() });

        Assert.Empty(listedBySecond);
        Assert.Equal(HttpStatusCode.NotFound, updateBySecond.StatusCode);
        var stillFirsts = await first.Api.GetAsync<List<CustomerEndpoints.CustomerResponse>>("/api/customers");
        Assert.Equal("Yalnızca Birincinin Müşterisi", Assert.Single(stillFirsts).FullName);
    }

    [Fact]
    public async Task Another_companys_customer_id_is_useless_in_other_modules_too()
    {
        var first = await app.OpenCompanyAsync("customers");
        var second = await Cafe.OpenAsync(app, "customers", "orders");
        var foreignCustomer = await first.AddCustomerAsync();

        var response = await second.SendOrderAsync(Guid.NewGuid(), foreignCustomer, null, (second.Latte, 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("customer_not_found", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task The_same_phone_number_can_be_a_customer_of_two_companies_but_only_once_in_each()
    {
        var first = await app.OpenCompanyAsync("customers");
        var second = await app.OpenCompanyAsync("customers");
        var phone = Http.RandomPhone();
        await first.AddCustomerAsync("Ortak Müşteri", phone);

        await second.AddCustomerAsync("Ortak Müşteri", phone);
        var duplicate = await first.Api.PostAsJsonAsync("/api/customers", new { fullName = "Aynı Numara", phone });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("phone_in_use", await duplicate.ErrorCodeAsync());
    }
}
