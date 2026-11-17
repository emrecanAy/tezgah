using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Appointments;

internal enum AppointmentStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoShow,
}

internal sealed class Appointment : ITenantOwned
{
    private Appointment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid ServiceId { get; private set; }

    public Guid StaffId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Fixed when booked. Changing a service's duration later does not move existing bookings.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public static Appointment Book(Guid customerId, Service service, Guid staffId, DateTimeOffset startsAt, string? notes) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        ServiceId = service.Id,
        StaffId = staffId,
        StartsAt = startsAt,
        EndsAt = startsAt.AddMinutes(service.DurationMinutes),
        Status = AppointmentStatus.Scheduled,
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
    };

    public Result Complete() => Close(AppointmentStatus.Completed);

    public Result Cancel() => Close(AppointmentStatus.Cancelled);

    public Result MarkNoShow() => Close(AppointmentStatus.NoShow);

    private Result Close(AppointmentStatus outcome)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return Error.Conflict("appointment_closed", $"This appointment is already {Status}.");
        }

        Status = outcome;
        return Result.Success;
    }
}
