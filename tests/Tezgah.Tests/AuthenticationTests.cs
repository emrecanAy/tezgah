using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tezgah.Platform.Auth;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class AuthenticationTests(TezgahApp app)
{
    [Fact]
    public async Task A_wrong_password_and_an_unknown_account_get_the_same_answer()
    {
        var company = await app.OpenCompanyAsync("customers");
        var email = await OwnerEmailAsync(company);
        var client = app.Anonymous();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "not-the-password" });
        var unknownAccount = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { email = $"nobody-{Guid.NewGuid():N}@test.local", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal("invalid_credentials", await wrongPassword.ErrorCodeAsync());
        Assert.Equal("invalid_credentials", await unknownAccount.ErrorCodeAsync());
    }

    /// <summary>The lock is per address and kicks in for addresses that have no account, too: being locked proves nothing.</summary>
    [Fact]
    public async Task Five_wrong_passwords_lock_the_address_for_the_right_one_and_for_unknown_addresses_alike()
    {
        var company = await app.OpenCompanyAsync("customers");
        var email = await OwnerEmailAsync(company);
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";
        var client = app.Anonymous();

        foreach (var address in new[] { email, unknown })
        {
            for (var i = 0; i < 5; i++)
            {
                await client.PostAsJsonAsync("/api/auth/sign-in", new { email = address, password = "not-the-password" });
            }
        }

        var rightPassword = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = TezgahApp.Password });
        var unknownAgain = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = unknown, password = "anything-at-all" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rightPassword.StatusCode);
        Assert.Equal("too_many_attempts", await rightPassword.ErrorCodeAsync());
        Assert.Equal("too_many_attempts", await unknownAgain.ErrorCodeAsync());
    }

    [Fact]
    public async Task Auth_requests_without_the_client_header_are_refused()
    {
        var bare = app.Anonymous();
        bare.DefaultRequestHeaders.Remove(AuthEndpoints.ClientHeader);

        var response = await bare.PostAsJsonAsync("/api/auth/sign-in", new { email = TezgahApp.AdminEmail, password = TezgahApp.Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("client_header_missing", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task The_refresh_token_lives_in_a_cookie_scripts_cannot_read_and_other_sites_cannot_send()
    {
        var response = await app.Anonymous().PostAsJsonAsync(
            "/api/auth/sign-in", new { email = TezgahApp.AdminEmail, password = TezgahApp.Password });

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(RefreshToken(cookie), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refreshing_hands_out_a_new_refresh_token_every_time()
    {
        var (client, first) = await SignInWithCookieAsync();

        var refreshed = await client.PostAsync("/api/auth/refresh", null);
        var second = RefreshToken(Assert.Single(refreshed.Headers.GetValues("Set-Cookie")));
        var session = (await refreshed.Content.ReadFromJsonAsync<SessionResponse>())!;
        TezgahApp.UseSession(client, session);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    /// <summary>
    /// A refresh token that is used twice has been copied. Both the copy and the original's session are
    /// ended, because the server cannot tell which of the two holders is the thief.
    /// </summary>
    [Fact]
    public async Task A_refresh_token_used_twice_ends_the_whole_session()
    {
        var (owner, stolen) = await SignInWithCookieAsync();
        (await owner.PostAsync("/api/auth/refresh", null)).EnsureSuccessStatusCode();

        var thief = app.Anonymous();
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", $"tezgah-refresh={stolen}");
        var thiefResponse = await thief.SendAsync(replay);
        var ownerNext = await owner.PostAsync("/api/auth/refresh", null);

        Assert.Equal("session_revoked", await thiefResponse.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ownerNext.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server()
    {
        var (client, _) = await SignInWithCookieAsync();

        (await client.PostAsync("/api/auth/sign-out", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_every_other_device_out_at_once()
    {
        var company = await app.OpenCompanyAsync("customers");
        var email = await OwnerEmailAsync(company);
        var laptop = await app.SignInAsync(email);

        var changed = await company.Api.PostAsJsonAsync(
            "/api/account/password", new { currentPassword = TezgahApp.Password, newPassword = "a much longer passphrase" });
        TezgahApp.UseSession(company.Api, (await changed.Content.ReadFromJsonAsync<SessionResponse>())!);

        // The laptop's access token has not expired, yet it no longer opens anything; nor does its refresh token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await company.Api.GetAsync("/api/customers")).StatusCode);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("password1234")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public async Task A_weak_password_is_refused(string weak)
    {
        var company = await app.OpenCompanyAsync("customers");

        var response = await company.Api.PostAsJsonAsync(
            "/api/account/password", new { currentPassword = TezgahApp.Password, newPassword = weak });

        Assert.Equal("weak_password", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task With_a_temporary_password_the_only_thing_one_can_do_is_choose_a_new_one()
    {
        var company = await app.OpenCompanyAsync("customers");
        var added = await company.Api.PostAsJsonAsync(
            "/api/team", new { fullName = "Yeni Personel", email = $"new-{Guid.NewGuid():N}@test.local", role = "staff" });
        var temporary = (await added.Content.ReadFromJsonAsync<Tezgah.Platform.Team.TeamEndpoints.TemporaryPassword>())!;

        var newcomer = await app.SignInAsync(temporary.Member.Email, temporary.Password);
        var me = await newcomer.GetAsync<AuthEndpoints.MeResponse>("/api/me");

        Assert.True(me.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await newcomer.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await newcomer.GetAsync("/api/account/sessions")).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_member_is_shut_out_at_once_and_cannot_sign_in_again()
    {
        var company = await app.OpenCompanyAsync("customers");
        var (memberId, member) = await app.AddMemberAsync(company, "staff");
        var email = (await member.GetAsync<AuthEndpoints.MeResponse>("/api/me")).User.Email;

        (await company.Api.PutAsJsonAsync($"/api/team/{memberId}", new { isActive = false })).EnsureSuccessStatusCode();
        var signIn = await app.Anonymous().PostAsJsonAsync("/api/auth/sign-in", new { email, password = TezgahApp.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/customers")).StatusCode);
        Assert.Equal("account_disabled", await signIn.ErrorCodeAsync());
    }

    [Fact]
    public async Task A_token_with_a_tampered_signature_or_payload_is_refused()
    {
        var admin = await app.SignInAsAdminAsync();
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        var parts = token.Split('.');
        var badSignature = $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}";
        var badPayload = $"{parts[0]}.{parts[1][..^2]}{(parts[1][^2] == 'A' ? 'B' : 'A')}{parts[1][^1]}.{parts[2]}";

        foreach (var forged in new[] { badSignature, badPayload, $"{parts[0]}.{parts[1]}." })
        {
            var client = app.Anonymous();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task Two_step_sign_in_needs_a_fresh_code_and_a_recovery_code_works_only_once()
    {
        var company = await app.OpenCompanyAsync("customers");
        var email = await OwnerEmailAsync(company);
        var setup = await company.Api.PostAsync<AccountEndpoints.TwoStepSetup>("/api/account/two-step/setup", new { });
        var key = Totp.FromBase32(setup.Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var enabled = await company.Api.PostAsync<AccountEndpoints.RecoveryCodes>(
            "/api/account/two-step/enable", new { code = Totp.Code(key, step) });

        // The password alone gives a ticket for the second step, not a session.
        var first = await PasswordStepAsync(email);
        Assert.Null(first.Session);
        var wrong = await SecondStepAsync(first.TwoStepTicket, "000000");
        var reused = await SecondStepAsync((await PasswordStepAsync(email)).TwoStepTicket, Totp.Code(key, step));
        var fresh = await SecondStepAsync((await PasswordStepAsync(email)).TwoStepTicket, Totp.Code(key, step + 1));
        var replayed = await SecondStepAsync((await PasswordStepAsync(email)).TwoStepTicket, Totp.Code(key, step + 1));

        Assert.Equal("invalid_code", await wrong.ErrorCodeAsync());
        Assert.Equal("invalid_code", await reused.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal("invalid_code", await replayed.ErrorCodeAsync());

        var recovery = enabled.Codes[0];
        Assert.Equal(HttpStatusCode.OK, (await SecondStepAsync((await PasswordStepAsync(email)).TwoStepTicket, recovery)).StatusCode);
        Assert.Equal("invalid_code", await (await SecondStepAsync((await PasswordStepAsync(email)).TwoStepTicket, recovery)).ErrorCodeAsync());
    }

    [Fact]
    public async Task The_second_step_ticket_is_not_an_access_token()
    {
        var company = await app.OpenCompanyAsync("customers");
        var email = await OwnerEmailAsync(company);
        var setup = await company.Api.PostAsync<AccountEndpoints.TwoStepSetup>("/api/account/two-step/setup", new { });
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        await company.Api.PostAsync<AccountEndpoints.RecoveryCodes>(
            "/api/account/two-step/enable", new { code = Totp.Code(Totp.FromBase32(setup.Secret), step) });

        var ticket = (await PasswordStepAsync(email)).TwoStepTicket!;
        var client = app.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    private async Task<(HttpClient Client, string RefreshToken)> SignInWithCookieAsync()
    {
        var client = app.Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = TezgahApp.AdminEmail, password = TezgahApp.Password });
        response.EnsureSuccessStatusCode();
        TezgahApp.UseSession(client, (await response.Content.ReadFromJsonAsync<AuthEndpoints.SignInResponse>())!.Session!);
        return (client, RefreshToken(Assert.Single(response.Headers.GetValues("Set-Cookie"))));
    }

    private async Task<AuthEndpoints.SignInResponse> PasswordStepAsync(string email) =>
        await app.Anonymous().PostAsync<AuthEndpoints.SignInResponse>("/api/auth/sign-in", new { email, password = TezgahApp.Password });

    private Task<HttpResponseMessage> SecondStepAsync(string? ticket, string code) =>
        app.Anonymous().PostAsJsonAsync("/api/auth/sign-in/second-step", new { ticket, code });

    private static async Task<string> OwnerEmailAsync(Company company) =>
        (await company.Api.GetAsync<AuthEndpoints.MeResponse>("/api/me")).User.Email;

    private static string RefreshToken(string setCookie) => setCookie.Split(';')[0].Split('=', 2)[1];
}
