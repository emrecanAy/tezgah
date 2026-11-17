using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Appointments;

/// <summary>What can be booked (services) and with whom (staff).</summary>
internal static class SetupEndpoints
{
    public sealed record ServiceRequest(string? Name, int DurationMinutes, decimal Price, bool? IsActive);

    public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, decimal Price, bool IsActive)
    {
        public static ServiceResponse From(Service service) =>
            new(service.Id, service.Name, service.DurationMinutes, service.Price, service.IsActive);
    }

    public sealed record StaffRequest(string? Name, bool? IsActive);

    public sealed record StaffResponse(Guid Id, string Name, bool IsActive)
    {
        public static StaffResponse From(StaffMember staff) => new(staff.Id, staff.Name, staff.IsActive);
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/services", async (AppointmentsDbContext db, CancellationToken ct) =>
            (await db.Services.AsNoTracking().OrderBy(service => service.Name).ToListAsync(ct)).ConvertAll(ServiceResponse.From))
            .RequirePermission(AppointmentPermissions.View);

        api.MapPost("/services", async (ServiceRequest request, AppointmentsDbContext db, CancellationToken ct) =>
        {
            var service = Service.Create(request.Name, request.DurationMinutes, request.Price);
            if (!service.IsSuccess)
            {
                return service.Error.ToProblem();
            }

            db.Services.Add(service.Value);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/appointments/services/{service.Value.Id}", ServiceResponse.From(service.Value));
        }).RequirePermission(AppointmentPermissions.Setup);

        api.MapPut("/services/{serviceId:guid}", async (
            Guid serviceId, ServiceRequest request, AppointmentsDbContext db, CancellationToken ct) =>
        {
            var service = await db.Services.SingleOrDefaultAsync(s => s.Id == serviceId, ct);
            if (service is null)
            {
                return Error.NotFound("service_not_found", "This service does not exist.").ToProblem();
            }

            var updated = service.Update(request.Name, request.DurationMinutes, request.Price, request.IsActive ?? service.IsActive);
            if (!updated.IsSuccess)
            {
                return updated.Error.ToProblem();
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ServiceResponse.From(service));
        }).RequirePermission(AppointmentPermissions.Setup);

        api.MapGet("/staff", async (AppointmentsDbContext db, CancellationToken ct) =>
            (await db.Staff.AsNoTracking().OrderBy(staff => staff.Name).ToListAsync(ct)).ConvertAll(StaffResponse.From))
            .RequirePermission(AppointmentPermissions.View);

        api.MapPost("/staff", async (StaffRequest request, AppointmentsDbContext db, CancellationToken ct) =>
        {
            var staff = StaffMember.Create(request.Name);
            if (!staff.IsSuccess)
            {
                return staff.Error.ToProblem();
            }

            db.Staff.Add(staff.Value);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/appointments/staff/{staff.Value.Id}", StaffResponse.From(staff.Value));
        }).RequirePermission(AppointmentPermissions.Setup);

        api.MapPut("/staff/{staffId:guid}", async (
            Guid staffId, StaffRequest request, AppointmentsDbContext db, CancellationToken ct) =>
        {
            var staff = await db.Staff.SingleOrDefaultAsync(s => s.Id == staffId, ct);
            if (staff is null)
            {
                return Error.NotFound("staff_not_found", "This staff member does not exist.").ToProblem();
            }

            var updated = staff.Update(request.Name, request.IsActive ?? staff.IsActive);
            if (!updated.IsSuccess)
            {
                return updated.Error.ToProblem();
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(StaffResponse.From(staff));
        }).RequirePermission(AppointmentPermissions.Setup);
    }
}
