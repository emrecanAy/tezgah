using System.Net;
using System.Net.Http.Json;
using Tezgah.Fleet;
using Tezgah.Tests.Support;

namespace Tezgah.Tests;

[Collection(AppCollection.Name)]
public sealed class FleetTests(TezgahApp app)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [Fact]
    public async Task Inspection_and_insurance_dates_show_up_as_alerts_when_close_or_overdue()
    {
        var company = await app.OpenCompanyAsync("fleet");
        await RegisterAsync(company, "16 TST 001", inspectionDueOn: Today.AddDays(10), insuranceDueOn: Today.AddDays(-3));
        await RegisterAsync(company, "16 TST 002", inspectionDueOn: Today.AddDays(200), insuranceDueOn: Today.AddDays(31));

        var alerts = await company.Api.GetAsync<List<FleetEndpoints.Alert>>("/api/fleet/alerts");

        Assert.Equal(
            new[] { ("16 TST 001", "insurance", -3), ("16 TST 001", "inspection", 10) },
            alerts.Select(alert => (alert.Plate, alert.Kind, alert.DaysLeft)));
    }

    [Fact]
    public async Task A_fill_up_moves_the_odometer_forward_and_never_backwards()
    {
        var company = await app.OpenCompanyAsync("fleet");
        var vehicle = await RegisterAsync(company, "16 TST 010", odometerKm: 50_000);

        var forward = await LogFuelAsync(company, vehicle.Id, odometerKm: 50_600, liters: 55);
        var backward = await LogFuelAsync(company, vehicle.Id, odometerKm: 50_100, liters: 40);

        Assert.Equal(HttpStatusCode.Created, forward.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backward.StatusCode);
        Assert.Equal("odometer_backwards", await backward.ErrorCodeAsync());
        var logbook = await company.Api.GetAsync<FleetEndpoints.VehicleLogbook>($"/api/fleet/vehicles/{vehicle.Id}");
        Assert.Equal(50_600, logbook.Vehicle.OdometerKm);
        Assert.Single(logbook.Fuel);
    }

    [Fact]
    public async Task The_logbook_reports_average_consumption_and_costs()
    {
        var company = await app.OpenCompanyAsync("fleet");
        var vehicle = await RegisterAsync(company, "16 TST 020", odometerKm: 10_000);
        await LogFuelAsync(company, vehicle.Id, odometerKm: 10_000, liters: 60, totalCost: 2_700);
        await LogFuelAsync(company, vehicle.Id, odometerKm: 10_500, liters: 40, totalCost: 1_800);
        await LogFuelAsync(company, vehicle.Id, odometerKm: 11_000, liters: 45, totalCost: 2_025);

        var logbook = await company.Api.GetAsync<FleetEndpoints.VehicleLogbook>($"/api/fleet/vehicles/{vehicle.Id}");

        // 85 litres burned over the 1,000 km between the first and the last fill-up.
        Assert.Equal(8.5m, logbook.AverageConsumption);
        Assert.Equal(6_525m, logbook.FuelCost);
    }

    [Fact]
    public async Task A_plate_can_be_registered_once_per_company_however_it_is_typed()
    {
        var company = await app.OpenCompanyAsync("fleet");
        await RegisterAsync(company, "16 TST 030");

        var duplicate = await company.Api.PostAsJsonAsync("/api/fleet/vehicles", VehicleBody("16  tst 030"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("plate_in_use", await duplicate.ErrorCodeAsync());
    }

    [Theory]
    [InlineData(new[] { 1000, 1500, 2000 }, new[] { 60.0, 40.0, 45.0 }, 8.5)]
    [InlineData(new[] { 1000, 1400 }, new[] { 50.0, 30.0 }, 7.5)]
    public void Consumption_ignores_the_first_fill_up(int[] odometers, double[] liters, double expected)
    {
        var fillUps = odometers.Zip(liters, (odometer, amount) => (odometer, (decimal)amount)).ToList();

        Assert.Equal((decimal)expected, FleetMath.AverageConsumption(fillUps));
    }

    [Fact]
    public void Consumption_needs_two_fill_ups_and_some_distance()
    {
        Assert.Null(FleetMath.AverageConsumption([(1000, 50m)]));
        Assert.Null(FleetMath.AverageConsumption([(1000, 50m), (1000, 20m)]));
    }

    private static object VehicleBody(
        string plate, int odometerKm = 1_000, DateOnly? inspectionDueOn = null, DateOnly? insuranceDueOn = null) => new
        {
            plate, brand = "Ford", model = "Transit", year = 2021, odometerKm, inspectionDueOn, insuranceDueOn,
        };

    private static Task<FleetEndpoints.VehicleResponse> RegisterAsync(
        Company company, string plate, int odometerKm = 1_000, DateOnly? inspectionDueOn = null, DateOnly? insuranceDueOn = null) =>
        company.Api.PostAsync<FleetEndpoints.VehicleResponse>(
            "/api/fleet/vehicles", VehicleBody(plate, odometerKm, inspectionDueOn, insuranceDueOn));

    private static Task<HttpResponseMessage> LogFuelAsync(
        Company company, Guid vehicleId, int odometerKm, decimal liters, decimal totalCost = 1_000) =>
        company.Api.PostAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/fuel",
            new { filledOn = Today, liters, totalCost, odometerKm });
}
