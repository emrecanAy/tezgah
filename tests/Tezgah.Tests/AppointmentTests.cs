using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Appointments;
using Tezgah.Core.Messaging;
using Tezgah.Loyalty;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class AppointmentTests(TezgahApp app)
{
    private static readonly DateTimeOffset Tomorrow10 = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1).AddHours(10);

    [Fact]
    public async Task A_staff_member_cannot_be_booked_twice_for_overlapping_times()
    {
        var salon = await SalonAsync();
        await BookAsync(salon, salon.Elif, Tomorrow10);

        var overlapping = await TryBookAsync(salon, salon.Elif, Tomorrow10.AddMinutes(30));

        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal("slot_taken", await overlapping.ErrorCodeAsync());
    }

    [Fact]
    public async Task Back_to_back_appointments_and_other_staff_at_the_same_time_are_fine()
    {
        var salon = await SalonAsync();
        await BookAsync(salon, salon.Elif, Tomorrow10);

        await BookAsync(salon, salon.Elif, Tomorrow10.AddMinutes(60));
        await BookAsync(salon, salon.Kerem, Tomorrow10);
    }

    /// <summary>The constraint, not application code, decides: ten simultaneous requests cannot all pass a "is it free?" check.</summary>
    [Fact]
    public async Task Ten_people_booking_the_same_slot_at_once_produce_exactly_one_appointment()
    {
        var salon = await SalonAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => TryBookAsync(salon, salon.Elif, Tomorrow10)));

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, attempts.Count(response => response.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_cancelled_appointment_frees_its_slot()
    {
        var salon = await SalonAsync();
        var appointment = await BookAsync(salon, salon.Elif, Tomorrow10);

        (await salon.Company.Api.PostAsync($"/api/appointments/{appointment.Id}/cancel", null)).EnsureSuccessStatusCode();

        await BookAsync(salon, salon.Elif, Tomorrow10);
    }

    [Fact]
    public async Task Completing_an_appointment_stamps_the_customers_loyalty_card_exactly_once()
    {
        var salon = await SalonAsync("customers", "appointments", "loyalty");
        var program = await salon.Company.Api.PostAsync<LoyaltyEndpoints.ProgramResponse>("/api/loyalty/programs",
            new { name = "Saç Bakım Kartı", stampsRequired = 4, reward = "1 kesim hediye", earnRule = "appointment" });
        var appointment = await BookAsync(salon, salon.Elif, Tomorrow10);

        (await salon.Company.Api.PostAsync($"/api/appointments/{appointment.Id}/complete", null)).EnsureSuccessStatusCode();

        // Appointments knows nothing about Loyalty; the stamp arrives through the outbox.
        var cards = await Eventually.ValueAsync(
            () => salon.Company.Api.GetAsync<List<CardResponse>>($"/api/loyalty/programs/{program.Id}/cards"),
            found => found.Count == 1);
        Assert.Equal((salon.CustomerId, 1), (cards[0].CustomerId, cards[0].Stamps));

        var again = await salon.Company.Api.PostAsync($"/api/appointments/{appointment.Id}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task For_a_company_without_loyalty_a_completed_appointment_is_delivered_to_nobody_and_causes_no_error()
    {
        var salon = await SalonAsync("customers", "appointments");
        var appointment = await BookAsync(salon, salon.Elif, Tomorrow10);

        (await salon.Company.Api.PostAsync($"/api/appointments/{appointment.Id}/complete", null)).EnsureSuccessStatusCode();

        await using var scope = app.ScopeFor(salon.Company.Id);
        var outbox = scope.ServiceProvider.GetRequiredService<AppointmentsDbContext>().Set<OutboxMessage>().AsNoTracking();
        var message = await Eventually.ValueAsync(
            async () => (await outbox.ToListAsync()).Single(m => m.Payload.Contains(appointment.Id.ToString())),
            m => m.ProcessedAt is not null);
        Assert.Equal(0, message.Attempts);
    }

    private async Task<Salon> SalonAsync(params string[] modules)
    {
        var company = await app.OpenCompanyAsync(modules.Length > 0 ? modules : ["customers", "appointments"]);
        var service = await company.Api.PostAsync<SetupEndpoints.ServiceResponse>(
            "/api/appointments/services", new { name = "Saç Kesimi", durationMinutes = 60, price = 450 });
        var elif = await company.Api.PostAsync<SetupEndpoints.StaffResponse>("/api/appointments/staff", new { name = "Elif" });
        var kerem = await company.Api.PostAsync<SetupEndpoints.StaffResponse>("/api/appointments/staff", new { name = "Kerem" });
        return new Salon(company, service.Id, elif.Id, kerem.Id, await company.AddCustomerAsync());
    }

    private static Task<HttpResponseMessage> TryBookAsync(Salon salon, Guid staffId, DateTimeOffset startsAt) =>
        salon.Company.Api.PostAsJsonAsync("/api/appointments",
            new { customerId = salon.CustomerId, serviceId = salon.ServiceId, staffId, startsAt });

    private static async Task<AppointmentEndpoints.AppointmentResponse> BookAsync(Salon salon, Guid staffId, DateTimeOffset startsAt)
    {
        var response = await TryBookAsync(salon, staffId, startsAt);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>())!;
    }

    private sealed record Salon(Company Company, Guid ServiceId, Guid Elif, Guid Kerem, Guid CustomerId);
}
