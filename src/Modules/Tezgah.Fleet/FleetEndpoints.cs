using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Fleet;

internal static class FleetEndpoints
{
    /// <param name="Kind"><c>inspection</c>, <c>insurance</c> or <c>maintenance</c>.</param>
    /// <param name="DaysLeft">Negative when the date has passed.</param>
    public sealed record Alert(Guid VehicleId, string Plate, string Kind, DateOnly DueOn, int DaysLeft);

    public sealed record VehicleResponse(
        Guid Id, string Plate, string Brand, string Model, int Year, int OdometerKm, string? Driver,
        DateOnly? InspectionDueOn, DateOnly? InsuranceDueOn, bool IsActive, IReadOnlyList<Alert> Alerts);

    public sealed record MaintenanceRequest(DateOnly PerformedOn, string? Description, decimal Cost, int OdometerKm, DateOnly? NextDueOn);

    public sealed record MaintenanceResponse(Guid Id, DateOnly PerformedOn, string Description, decimal Cost, int OdometerKm, DateOnly? NextDueOn);

    public sealed record FuelRequest(DateOnly FilledOn, decimal Liters, decimal TotalCost, int OdometerKm);

    public sealed record FuelResponse(Guid Id, DateOnly FilledOn, decimal Liters, decimal TotalCost, int OdometerKm);

    public sealed record VehicleLogbook(
        VehicleResponse Vehicle, IReadOnlyList<MaintenanceResponse> Maintenance, IReadOnlyList<FuelResponse> Fuel,
        decimal? AverageConsumption, decimal FuelCost, decimal MaintenanceCost);

    public sealed record Summary(int Vehicles, int Alerts, decimal FuelCostThisMonth);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/vehicles", ListAsync).RequirePermission(FleetPermissions.View);
        api.MapPost("/vehicles", RegisterAsync).RequirePermission(FleetPermissions.ManageVehicles);
        api.MapGet("/vehicles/{vehicleId:guid}", LogbookAsync).RequirePermission(FleetPermissions.View);
        api.MapPut("/vehicles/{vehicleId:guid}", UpdateAsync).RequirePermission(FleetPermissions.ManageVehicles);
        api.MapPost("/vehicles/{vehicleId:guid}/maintenance", LogMaintenanceAsync).RequirePermission(FleetPermissions.Log);
        api.MapPost("/vehicles/{vehicleId:guid}/fuel", LogFuelAsync).RequirePermission(FleetPermissions.Log);
        api.MapGet("/alerts", async (FleetDbContext db, TimeProvider time, CancellationToken ct) =>
            (await LoadAsync(db, time, ct)).SelectMany(vehicle => vehicle.Alerts).OrderBy(alert => alert.DaysLeft).ToList())
            .RequirePermission(FleetPermissions.View);
        api.MapGet("/summary", SummaryAsync).RequirePermission(FleetPermissions.View);
    }

    private static async Task<IReadOnlyList<VehicleResponse>> ListAsync(FleetDbContext db, TimeProvider time, CancellationToken ct) =>
        await LoadAsync(db, time, ct);

    private static async Task<IResult> RegisterAsync(VehicleDetails details, FleetDbContext db, TimeProvider time, CancellationToken ct)
    {
        var vehicle = Vehicle.Register(details);
        if (!vehicle.IsSuccess)
        {
            return vehicle.Error.ToProblem();
        }

        db.Vehicles.Add(vehicle.Value);
        return await SaveAsync(db, ct) is { } conflict
            ? conflict.ToProblem()
            : Results.Created($"/api/fleet/vehicles/{vehicle.Value.Id}", ToResponse(vehicle.Value, null, Today(time)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid vehicleId, VehicleDetails details, FleetDbContext db, TimeProvider time, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
        {
            return NotFound().ToProblem();
        }

        var updated = vehicle.Update(details);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        if (await SaveAsync(db, ct) is { } conflict)
        {
            return conflict.ToProblem();
        }

        return Results.Ok(ToResponse(vehicle, await NextServiceAsync(db, vehicleId, ct), Today(time)));
    }

    private static async Task<IResult> LogbookAsync(Guid vehicleId, FleetDbContext db, TimeProvider time, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
        {
            return NotFound().ToProblem();
        }

        var maintenance = await db.Maintenance.AsNoTracking()
            .Where(record => record.VehicleId == vehicleId)
            .OrderByDescending(record => record.PerformedOn)
            .ToListAsync(ct);
        var fuel = await db.Fuel.AsNoTracking()
            .Where(entry => entry.VehicleId == vehicleId)
            .OrderByDescending(entry => entry.OdometerKm)
            .ToListAsync(ct);

        return Results.Ok(new VehicleLogbook(
            ToResponse(vehicle, maintenance.Max(record => record.NextDueOn), Today(time)),
            maintenance.ConvertAll(r => new MaintenanceResponse(r.Id, r.PerformedOn, r.Description, r.Cost, r.OdometerKm, r.NextDueOn)),
            fuel.ConvertAll(e => new FuelResponse(e.Id, e.FilledOn, e.Liters, e.TotalCost, e.OdometerKm)),
            FleetMath.AverageConsumption([.. fuel.Select(entry => (entry.OdometerKm, entry.Liters))]),
            fuel.Sum(entry => entry.TotalCost),
            maintenance.Sum(record => record.Cost)));
    }

    private static async Task<IResult> LogMaintenanceAsync(
        Guid vehicleId, MaintenanceRequest request, FleetDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Cost < 0)
        {
            return Error.Validation("invalid_maintenance", "Describe the work done; the cost cannot be negative.").ToProblem();
        }

        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
        {
            return NotFound().ToProblem();
        }

        // Workshop paperwork is often entered late, so an older reading is accepted here and
        // simply does not move the odometer.
        if (request.OdometerKm > vehicle.OdometerKm)
        {
            vehicle.RecordOdometer(request.OdometerKm);
        }

        var record = MaintenanceRecord.Log(
            vehicleId, request.PerformedOn, request.Description, request.Cost, request.OdometerKm, request.NextDueOn);
        db.Maintenance.Add(record);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/fleet/vehicles/{vehicleId}", new MaintenanceResponse(
            record.Id, record.PerformedOn, record.Description, record.Cost, record.OdometerKm, record.NextDueOn));
    }

    private static async Task<IResult> LogFuelAsync(Guid vehicleId, FuelRequest request, FleetDbContext db, CancellationToken ct)
    {
        if (request.Liters <= 0 || request.TotalCost < 0)
        {
            return Error.Validation("invalid_fuel_entry", "Litres must be positive and the cost cannot be negative.").ToProblem();
        }

        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
        {
            return NotFound().ToProblem();
        }

        // Consumption is computed from consecutive readings, so a fill-up must not go backwards.
        var recorded = vehicle.RecordOdometer(request.OdometerKm);
        if (!recorded.IsSuccess)
        {
            return recorded.Error.ToProblem();
        }

        var entry = FuelEntry.Log(vehicleId, request.FilledOn, request.Liters, request.TotalCost, request.OdometerKm);
        db.Fuel.Add(entry);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/fleet/vehicles/{vehicleId}",
            new FuelResponse(entry.Id, entry.FilledOn, entry.Liters, entry.TotalCost, entry.OdometerKm));
    }

    private static async Task<Summary> SummaryAsync(FleetDbContext db, TimeProvider time, CancellationToken ct)
    {
        var today = Today(time);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var vehicles = await LoadAsync(db, time, ct);

        return new Summary(
            vehicles.Count(vehicle => vehicle.IsActive),
            vehicles.Sum(vehicle => vehicle.Alerts.Count),
            await db.Fuel.Where(entry => entry.FilledOn >= monthStart).SumAsync(entry => entry.TotalCost, ct));
    }

    private static async Task<List<VehicleResponse>> LoadAsync(FleetDbContext db, TimeProvider time, CancellationToken ct)
    {
        var vehicles = await db.Vehicles.AsNoTracking().OrderBy(vehicle => vehicle.Plate).ToListAsync(ct);
        var nextService = await db.Maintenance
            .Where(record => record.NextDueOn != null)
            .GroupBy(record => record.VehicleId)
            .Select(group => new { VehicleId = group.Key, DueOn = group.Max(record => record.NextDueOn) })
            .ToDictionaryAsync(row => row.VehicleId, row => row.DueOn, ct);

        var today = Today(time);
        return vehicles.ConvertAll(vehicle => ToResponse(vehicle, nextService.GetValueOrDefault(vehicle.Id), today));
    }

    private static Task<DateOnly?> NextServiceAsync(FleetDbContext db, Guid vehicleId, CancellationToken ct) =>
        db.Maintenance.Where(record => record.VehicleId == vehicleId).MaxAsync(record => record.NextDueOn, ct);

    private static VehicleResponse ToResponse(Vehicle vehicle, DateOnly? nextServiceOn, DateOnly today)
    {
        var alerts = new List<Alert>();
        if (vehicle.IsActive)
        {
            AddAlert("inspection", vehicle.InspectionDueOn);
            AddAlert("insurance", vehicle.InsuranceDueOn);
            AddAlert("maintenance", nextServiceOn);
        }

        return new VehicleResponse(
            vehicle.Id, vehicle.Plate, vehicle.Brand, vehicle.Model, vehicle.Year, vehicle.OdometerKm, vehicle.Driver,
            vehicle.InspectionDueOn, vehicle.InsuranceDueOn, vehicle.IsActive, alerts);

        void AddAlert(string kind, DateOnly? dueOn)
        {
            if (FleetMath.DaysLeftIfDueSoon(dueOn, today) is { } daysLeft)
            {
                alerts.Add(new Alert(vehicle.Id, vehicle.Plate, kind, dueOn!.Value, daysLeft));
            }
        }
    }

    // Due dates are calendar dates in the company's country; Türkiye is UTC+3 all year.
    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().AddHours(3).DateTime);

    private static async Task<Error?> SaveAsync(FleetDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("plate_in_use", "Another vehicle is already registered with this plate.");
        }
    }

    private static Error NotFound() => Error.NotFound("vehicle_not_found", "This vehicle does not exist.");
}
