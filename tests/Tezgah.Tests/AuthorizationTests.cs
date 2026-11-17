using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Modules;
using Tezgah.Core.Security;
using Tezgah.Orders;
using Tezgah.Platform.Auth;
using Tezgah.Platform.Team;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class AuthorizationTests(TezgahApp app)
{
    /// <summary>A new endpoint that forgets to say who may call it fails this test instead of being open to every role.</summary>
    [Fact]
    public void Every_module_endpoint_asks_for_a_permission_declared_by_its_module()
    {
        var catalog = app.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        foreach (var module in catalog.Modules)
        {
            var own = endpoints.Where(e => e.RoutePattern.RawText!.StartsWith($"/api/{module.Info.Key}", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(own);
            foreach (var endpoint in own)
            {
                var permission = endpoint.Metadata.GetMetadata<Permission>();
                Assert.True(permission is not null, $"{endpoint.DisplayName} asks for no permission.");
                Assert.Contains(permission, module.Permissions);
            }
        }
    }

    [Fact]
    public void Only_the_sign_in_endpoints_and_the_health_check_are_open_without_signing_in()
    {
        var open = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!)
            .Where(route => route.StartsWith("/api", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["/api/auth/refresh", "/api/auth/sign-in", "/api/auth/sign-in/second-step", "/api/auth/sign-out"], open);
    }

    [Fact]
    public async Task Staff_ring_up_orders_but_cannot_change_the_menu_or_its_prices()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        var (_, staff) = await app.AddMemberAsync(cafe.Company, "staff");

        var order = await staff.PostAsJsonAsync("/api/orders", new
        {
            id = Guid.NewGuid(),
            items = new[] { new { menuItemId = cafe.Latte, quantity = 1 } },
        });
        var cheaperLatte = await staff.PutAsJsonAsync($"/api/orders/menu/{cafe.Latte}", new { name = "Latte", category = "Kahve", price = 1m });

        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cheaperLatte.StatusCode);
    }

    /// <summary>A mistake at the till is fixed on the spot by the one who made it; nobody else's sales can be erased.</summary>
    [Fact]
    public async Task Staff_cancel_their_own_order_but_not_a_colleagues_and_a_manager_cancels_any()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        var (_, alice) = await app.AddMemberAsync(cafe.Company, "staff");
        var (_, bob) = await app.AddMemberAsync(cafe.Company, "staff");
        var (_, manager) = await app.AddMemberAsync(cafe.Company, "manager");
        var alicesOrder = await PlaceAsync(alice, cafe);
        var bobsOrder = await PlaceAsync(bob, cafe);

        var aliceCancelsBobs = await alice.PostAsync($"/api/orders/{bobsOrder}/cancel", null);
        var aliceCancelsOwn = await alice.PostAsync($"/api/orders/{alicesOrder}/cancel", null);
        var managerCancelsBobs = await manager.PostAsync($"/api/orders/{bobsOrder}/cancel", null);

        Assert.Equal("cancel_not_allowed", await aliceCancelsBobs.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, aliceCancelsOwn.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, managerCancelsBobs.StatusCode);
    }

    [Fact]
    public void An_order_placed_more_than_15_minutes_ago_is_beyond_its_cashier_but_not_beyond_a_manager()
    {
        var cashier = Guid.NewGuid();
        var placedAt = DateTimeOffset.UtcNow;
        var item = MenuItem.Create("Latte", "Kahve", 130m).Value;
        var order = Order.Place(Guid.NewGuid(), null, [(item, 1)], null, cashier, "Kasa", placedAt).Value;

        Assert.True(order.MayBeCancelledBy(cashier, canCancelAny: false, placedAt.AddMinutes(15)));
        Assert.False(order.MayBeCancelledBy(cashier, canCancelAny: false, placedAt.AddMinutes(16)));
        Assert.False(order.MayBeCancelledBy(Guid.NewGuid(), canCancelAny: false, placedAt));
        Assert.True(order.MayBeCancelledBy(Guid.NewGuid(), canCancelAny: true, placedAt.AddDays(3)));
    }

    [Fact]
    public async Task What_each_role_may_do_is_what_me_reports()
    {
        var company = await app.OpenCompanyAsync("customers", "orders", "loyalty");
        var (_, staff) = await app.AddMemberAsync(company, "staff");
        var (_, manager) = await app.AddMemberAsync(company, "manager");

        var forStaff = (await staff.GetAsync<AuthEndpoints.MeResponse>("/api/me")).Permissions;
        var forManager = (await manager.GetAsync<AuthEndpoints.MeResponse>("/api/me")).Permissions;
        var forOwner = (await company.Api.GetAsync<AuthEndpoints.MeResponse>("/api/me")).Permissions;

        Assert.Contains("orders.sell", forStaff);
        Assert.DoesNotContain("orders.menu", forStaff);
        Assert.DoesNotContain("loyalty.programs", forStaff);
        Assert.Contains("orders.menu", forManager);
        Assert.DoesNotContain("team.manage", forManager);
        Assert.Contains("team.manage", forOwner);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("manager")]
    public async Task Only_owners_manage_the_team(string role)
    {
        var company = await app.OpenCompanyAsync("customers");
        var (_, member) = await app.AddMemberAsync(company, role);

        var response = await member.PostAsJsonAsync("/api/team", new { fullName = "X", email = "x@test.local", role = "owner" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A promotion or demotion takes effect on the next request, not when the old token expires.</summary>
    [Fact]
    public async Task A_new_role_takes_effect_at_once()
    {
        var cafe = await Cafe.OpenAsync(app, "customers", "orders");
        var (memberId, member) = await app.AddMemberAsync(cafe.Company, "manager");
        var email = (await member.GetAsync<AuthEndpoints.MeResponse>("/api/me")).User.Email;
        Assert.Equal(HttpStatusCode.OK, (await EditMenuAsync(member, cafe)).StatusCode);

        (await cafe.Api.PutAsJsonAsync($"/api/team/{memberId}", new { role = "staff" })).EnsureSuccessStatusCode();
        var withOldToken = await member.GetAsync("/api/orders/menu");
        var signedInAgain = await app.SignInAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EditMenuAsync(signedInAgain, cafe)).StatusCode);
    }

    [Fact]
    public async Task A_company_always_keeps_an_active_owner_and_no_one_changes_their_own_access()
    {
        var company = await app.OpenCompanyAsync("customers");
        var me = (await company.Api.GetAsync<AuthEndpoints.MeResponse>("/api/me")).User;
        var (secondOwnerId, _) = await app.AddMemberAsync(company, "owner");

        var demoteSelf = await company.Api.PutAsJsonAsync($"/api/team/{me.Id}", new { role = "staff" });
        (await company.Api.PutAsJsonAsync($"/api/team/{secondOwnerId}", new { role = "manager" })).EnsureSuccessStatusCode();
        var secondOwnerBack = await company.Api.PutAsJsonAsync($"/api/team/{secondOwnerId}", new { role = "owner" });

        Assert.Equal("cannot_change_self", await demoteSelf.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, secondOwnerBack.StatusCode);
    }

    /// <summary>Two owners demoting each other at the same moment would leave nobody in charge; the company row lock lets one through.</summary>
    [Fact]
    public async Task Two_owners_demoting_each_other_at_once_leave_the_company_with_one_owner()
    {
        var company = await app.OpenCompanyAsync("customers");
        var firstId = (await company.Api.GetAsync<AuthEndpoints.MeResponse>("/api/me")).User.Id;
        var (secondId, second) = await app.AddMemberAsync(company, "owner");

        var results = await Task.WhenAll(
            company.Api.PutAsJsonAsync($"/api/team/{secondId}", new { role = "staff" }),
            second.PutAsJsonAsync($"/api/team/{firstId}", new { role = "staff" }));

        Assert.Single(results, response => response.IsSuccessStatusCode);
        var team = await (results[0].IsSuccessStatusCode ? company.Api : second)
            .GetAsync<List<TeamEndpoints.MemberResponse>>("/api/team");
        Assert.Single(team, member => member.Role == "owner");
    }

    [Fact]
    public async Task An_owner_cannot_reach_another_companys_people()
    {
        var first = await app.OpenCompanyAsync("customers");
        var second = await app.OpenCompanyAsync("customers");
        var (strangerId, _) = await app.AddMemberAsync(second, "staff");

        var response = await first.Api.PutAsJsonAsync($"/api/team/{strangerId}", new { isActive = false });

        Assert.Equal("member_not_found", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task The_platform_administrator_sees_no_company_data()
    {
        var admin = await app.SignInAsAdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/team")).StatusCode);
    }

    private static Task<HttpResponseMessage> EditMenuAsync(HttpClient client, Cafe cafe) =>
        client.PutAsJsonAsync($"/api/orders/menu/{cafe.Latte}", new { name = "Latte", category = "Kahve", price = Cafe.LattePrice });

    private static async Task<Guid> PlaceAsync(HttpClient client, Cafe cafe)
    {
        var order = await client.PostAsync<OrderEndpoints.OrderResponse>("/api/orders", new
        {
            id = Guid.NewGuid(),
            items = new[] { new { menuItemId = cafe.Latte, quantity = 1 } },
        });
        return order.Id;
    }
}
