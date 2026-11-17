using System.Net;
using System.Net.Http.Json;
using Tezgah.Platform.Auth;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class PlatformTests(TezgahApp app)
{
    [Fact]
    public async Task A_company_sees_exactly_the_modules_it_was_given()
    {
        var company = await app.OpenCompanyAsync("customers", "loyalty");

        var me = await company.Api.GetAsync<AuthEndpoints.MeResponse>("/api/me");

        Assert.Equal(new[] { "customers", "loyalty" }, me.Modules.Select(module => module.Key));
        Assert.Equal(company.Id, me.Company!.Id);
    }

    [Fact]
    public async Task A_module_the_company_does_not_have_is_closed_to_it()
    {
        var fleetOnly = await app.OpenCompanyAsync("fleet");

        var response = await fleetOnly.Api.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("module_not_enabled", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Giving_a_company_a_module_opens_it_immediately_and_taking_it_away_closes_it()
    {
        var company = await app.OpenCompanyAsync("fleet");
        var admin = await app.SignInAsAdminAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await company.Api.GetAsync("/api/inventory/products")).StatusCode);

        await SetModulesAsync(admin, company, "fleet", "inventory");
        Assert.Equal(HttpStatusCode.OK, (await company.Api.GetAsync("/api/inventory/products")).StatusCode);

        await SetModulesAsync(admin, company, "fleet");
        Assert.Equal(HttpStatusCode.Forbidden, (await company.Api.GetAsync("/api/inventory/products")).StatusCode);
    }

    [Fact]
    public async Task Data_survives_a_module_being_switched_off_and_on_again()
    {
        var company = await app.OpenCompanyAsync("customers");
        var admin = await app.SignInAsAdminAsync();
        await company.AddCustomerAsync("Kalıcı Müşteri");

        await SetModulesAsync(admin, company);
        await SetModulesAsync(admin, company, "customers");

        var customers = await company.Api.GetAsync<List<Customers.CustomerEndpoints.CustomerResponse>>("/api/customers");
        Assert.Single(customers, customer => customer.FullName == "Kalıcı Müşteri");
    }

    [Theory]
    [InlineData("loyalty")]
    [InlineData("appointments")]
    [InlineData("orders")]
    public async Task A_module_cannot_be_given_without_the_modules_it_depends_on(string dependent)
    {
        var company = await app.OpenCompanyAsync("customers", dependent);
        var admin = await app.SignInAsAdminAsync();

        var withoutCustomers = await admin.PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/modules", new { modules = new[] { dependent } });

        Assert.Equal(HttpStatusCode.BadRequest, withoutCustomers.StatusCode);
        Assert.Equal("invalid_module_selection", await withoutCustomers.ErrorCodeAsync());
    }

    [Fact]
    public async Task Only_the_platform_administrator_can_manage_companies()
    {
        var company = await app.OpenCompanyAsync("fleet");

        var asOwner = await company.Api.GetAsync("/api/admin/companies");
        var anonymous = await app.Anonymous().GetAsync("/api/admin/companies");

        Assert.Equal(HttpStatusCode.Forbidden, asOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task The_platform_administrator_belongs_to_no_company_and_cannot_use_modules()
    {
        var admin = await app.SignInAsAdminAsync();

        var response = await admin.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task SetModulesAsync(HttpClient admin, Company company, params string[] modules) =>
        (await admin.PutAsJsonAsync($"/api/admin/companies/{company.Id}/modules", new { modules })).EnsureSuccessStatusCode();
}
