using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Fleet;

internal sealed class Vehicle : ITenantOwned
{
    private Vehicle()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Upper case, single spaces: "16 abc  123" is stored as "16 ABC 123".</summary>
    public string Plate { get; private set; } = null!;

    public string Brand { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    public int Year { get; private set; }

    public int OdometerKm { get; private set; }

    public string? Driver { get; private set; }

    /// <summary>Periodic roadworthiness inspection ("muayene").</summary>
    public DateOnly? InspectionDueOn { get; private set; }

    public DateOnly? InsuranceDueOn { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c>: two people logging against the same vehicle cannot overwrite each other's odometer.</summary>
    public uint Version { get; private set; }

    public static Result<Vehicle> Register(VehicleDetails details)
    {
        var vehicle = new Vehicle { Id = Guid.CreateVersion7(), OdometerKm = Math.Max(details.OdometerKm, 0) };
        var updated = vehicle.Update(details);
        return updated.IsSuccess ? vehicle : updated.Error;
    }

    public Result Update(VehicleDetails details)
    {
        var plate = string.Join(' ', (details.Plate ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (plate.Length < 5 || string.IsNullOrWhiteSpace(details.Brand) || string.IsNullOrWhiteSpace(details.Model))
        {
            return Error.Validation("invalid_vehicle", "A vehicle needs a plate, a brand and a model.");
        }

        if (details.Year is < 1980 or > 2100)
        {
            return Error.Validation("invalid_year", "The model year is out of range.");
        }

        if (details.OdometerKm < OdometerKm)
        {
            return OdometerWentBackwards(details.OdometerKm);
        }

        Plate = plate;
        Brand = details.Brand.Trim();
        Model = details.Model.Trim();
        Year = details.Year;
        OdometerKm = details.OdometerKm;
        Driver = string.IsNullOrWhiteSpace(details.Driver) ? null : details.Driver.Trim();
        InspectionDueOn = details.InspectionDueOn;
        InsuranceDueOn = details.InsuranceDueOn;
        IsActive = details.IsActive ?? true;
        return Result.Success;
    }

    /// <summary>Moves the odometer forward to a reading taken at the pump or in the workshop.</summary>
    public Result RecordOdometer(int readingKm)
    {
        if (readingKm < OdometerKm)
        {
            return OdometerWentBackwards(readingKm);
        }

        OdometerKm = readingKm;
        return Result.Success;
    }

    private Error OdometerWentBackwards(int readingKm) => Error.Validation(
        "odometer_backwards", $"The odometer reads {OdometerKm} km; {readingKm} km would be going backwards.");
}

internal sealed record VehicleDetails(
    string? Plate, string? Brand, string? Model, int Year, int OdometerKm, string? Driver,
    DateOnly? InspectionDueOn, DateOnly? InsuranceDueOn, bool? IsActive);
