using Tezgah.Core.Tenancy;

namespace Tezgah.Fleet;

internal sealed class MaintenanceRecord : ITenantOwned
{
    private MaintenanceRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public DateOnly PerformedOn { get; private set; }

    public string Description { get; private set; } = null!;

    public decimal Cost { get; private set; }

    public int OdometerKm { get; private set; }

    /// <summary>When the next service is due, if the workshop gave a date.</summary>
    public DateOnly? NextDueOn { get; private set; }

    public static MaintenanceRecord Log(
        Guid vehicleId, DateOnly performedOn, string description, decimal cost, int odometerKm, DateOnly? nextDueOn) => new()
    {
        Id = Guid.CreateVersion7(),
        VehicleId = vehicleId,
        PerformedOn = performedOn,
        Description = description.Trim(),
        Cost = cost,
        OdometerKm = odometerKm,
        NextDueOn = nextDueOn,
    };
}

internal sealed class FuelEntry : ITenantOwned
{
    private FuelEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public DateOnly FilledOn { get; private set; }

    public decimal Liters { get; private set; }

    public decimal TotalCost { get; private set; }

    public int OdometerKm { get; private set; }

    public static FuelEntry Log(Guid vehicleId, DateOnly filledOn, decimal liters, decimal totalCost, int odometerKm) => new()
    {
        Id = Guid.CreateVersion7(),
        VehicleId = vehicleId,
        FilledOn = filledOn,
        Liters = liters,
        TotalCost = totalCost,
        OdometerKm = odometerKm,
    };
}
