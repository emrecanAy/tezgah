using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Web;

namespace Tezgah.Appointments;

internal static class AppointmentEndpoints
{
    public sealed record BookRequest(Guid CustomerId, Guid ServiceId, Guid StaffId, DateTimeOffset StartsAt, string? Notes);

    public sealed record AppointmentResponse(
        Guid Id, Guid CustomerId, string CustomerName, Guid ServiceId, string ServiceName, Guid StaffId, string StaffName,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, string? Notes);

    public sealed record Summary(int Today, int NextSevenDays);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("", ListAsync);
        api.MapPost("", BookAsync);
        api.MapPost("/{appointmentId:guid}/complete", CompleteAsync);
        api.MapPost("/{appointmentId:guid}/cancel", (Guid appointmentId, AppointmentsDbContext db, CancellationToken ct) =>
            CloseAsync(appointmentId, db, appointment => appointment.Cancel(), ct));
        api.MapPost("/{appointmentId:guid}/no-show", (Guid appointmentId, AppointmentsDbContext db, CancellationToken ct) =>
            CloseAsync(appointmentId, db, appointment => appointment.MarkNoShow(), ct));
        api.MapGet("/summary", SummaryAsync);
    }

    /// <summary>Appointments that start inside [from, to). The client passes the bounds of its local day.</summary>
    private static async Task<IResult> ListAsync(
        DateTimeOffset from, DateTimeOffset to, AppointmentsDbContext db, ICustomerDirectory customers, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31))
        {
            return Error.Validation("invalid_range", "Ask for a range of at most 31 days.").ToProblem();
        }

        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        var appointments = await db.Appointments.AsNoTracking()
            .Where(appointment => appointment.StartsAt >= fromUtc && appointment.StartsAt < toUtc)
            .OrderBy(appointment => appointment.StartsAt)
            .ToListAsync(ct);

        return Results.Ok(await ToResponsesAsync(appointments, db, customers, ct));
    }

    private static async Task<IResult> BookAsync(
        BookRequest request, AppointmentsDbContext db, ICustomerDirectory customers, TimeProvider time, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive, ct);
        var staff = await db.Staff.SingleOrDefaultAsync(s => s.Id == request.StaffId && s.IsActive, ct);
        if (service is null || staff is null)
        {
            return Error.NotFound("service_or_staff_not_found", "The chosen service or staff member is not available.").ToProblem();
        }

        if (!await customers.ExistsAsync(request.CustomerId, ct))
        {
            return Error.NotFound("customer_not_found", "This customer does not exist.").ToProblem();
        }

        // A few minutes of slack so a walk-in can be booked "now".
        if (request.StartsAt < time.GetUtcNow().AddMinutes(-5))
        {
            return Error.Validation("in_the_past", "An appointment cannot start in the past.").ToProblem();
        }

        var appointment = Appointment.Book(request.CustomerId, service, staff.Id, request.StartsAt.ToUniversalTime(), request.Notes);
        db.Appointments.Add(appointment);

        // Bookings for one staff member queue up behind this lock. The exclusion constraint alone would
        // still refuse an overlap, but overlapping inserts that check it at the same moment wait for each
        // other, and PostgreSQL breaks that tie with a deadlock error instead of a clean violation.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var staffKey = $"appointments:staff:{staff.Id}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({staffKey}, 0))", ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsExclusionViolation())
        {
            return Error.Conflict("slot_taken", $"{staff.Name} already has an appointment at that time.").ToProblem();
        }

        var response = (await ToResponsesAsync([appointment], db, customers, ct))[0];
        return Results.Created($"/api/appointments/{appointment.Id}", response);
    }

    private static async Task<IResult> CompleteAsync(
        Guid appointmentId, AppointmentsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null)
        {
            return NotFound().ToProblem();
        }

        var completed = appointment.Complete();
        if (!completed.IsSuccess)
        {
            return completed.Error.ToProblem();
        }

        // Other modules may care that the visit happened (Loyalty stamps the customer's card).
        // The event is stored with the status change and delivered afterwards by the outbox.
        var serviceName = await db.Services.Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).SingleAsync(ct);
        db.Publish(new AppointmentCompleted(
            appointment.TenantId, appointment.Id, appointment.CustomerId, serviceName, time.GetUtcNow()));

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CloseAsync(
        Guid appointmentId, AppointmentsDbContext db, Func<Appointment, Result> close, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null)
        {
            return NotFound().ToProblem();
        }

        var closed = close(appointment);
        if (!closed.IsSuccess)
        {
            return closed.Error.ToProblem();
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<Summary> SummaryAsync(
        DateTimeOffset dayStart, AppointmentsDbContext db, CancellationToken ct)
    {
        var start = dayStart.ToUniversalTime();
        var scheduled = db.Appointments.Where(appointment => appointment.Status == AppointmentStatus.Scheduled);
        return new Summary(
            await scheduled.CountAsync(a => a.StartsAt >= start && a.StartsAt < start.AddDays(1), ct),
            await scheduled.CountAsync(a => a.StartsAt >= start && a.StartsAt < start.AddDays(7), ct));
    }

    private static async Task<List<AppointmentResponse>> ToResponsesAsync(
        List<Appointment> appointments, AppointmentsDbContext db, ICustomerDirectory customers, CancellationToken ct)
    {
        var services = await db.Services.AsNoTracking().ToDictionaryAsync(service => service.Id, service => service.Name, ct);
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(member => member.Id, member => member.Name, ct);
        var names = await customers.GetAsync([.. appointments.Select(appointment => appointment.CustomerId).Distinct()], ct);

        return appointments.ConvertAll(appointment => new AppointmentResponse(
            appointment.Id,
            appointment.CustomerId, names.GetValueOrDefault(appointment.CustomerId)?.FullName ?? "Silinmiş müşteri",
            appointment.ServiceId, services[appointment.ServiceId],
            appointment.StaffId, staff[appointment.StaffId],
            appointment.StartsAt, appointment.EndsAt, appointment.Status.ToString().ToLowerInvariant(), appointment.Notes));
    }

    private static Error NotFound() => Error.NotFound("appointment_not_found", "This appointment does not exist.");
}
