using System.Net.Http.Json;

namespace Tezgah.Tests.Support;

internal static class Http
{
    /// <summary>POSTs and returns the body, failing the test if the call did not succeed.</summary>
    public static async Task<T> PostAsync<T>(this HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { });
        Assert.True(response.IsSuccessStatusCode, $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url) =>
        (await client.GetFromJsonAsync<T>(url))!;

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        return problem!.TryGetValue("code", out var code) ? code?.ToString() : null;
    }

    public static async Task<Guid> AddCustomerAsync(this Company company, string name = "Ayşe Yılmaz", string? phone = null)
    {
        var customer = await company.Api.PostAsync<Customers.CustomerEndpoints.CustomerResponse>(
            "/api/customers", new { fullName = name, phone = phone ?? RandomPhone() });
        return customer.Id;
    }

    public static string RandomPhone() => $"05{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
}
