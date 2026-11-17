using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;

namespace Tezgah.Customers;

internal static class CustomerEndpoints
{
    public sealed record CustomerRequest(string? FullName, string? Phone, string? Email, string? Notes);

    public sealed record CustomerResponse(Guid Id, string FullName, string Phone, string? Email, string? Notes, DateTimeOffset CreatedAt)
    {
        public static CustomerResponse From(Customer customer) => new(
            customer.Id, customer.FullName, customer.Phone, customer.Email, customer.Notes, customer.CreatedAt);
    }

    public sealed record Summary(int Total, int NewThisMonth);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("", ListAsync).RequirePermission(CustomerPermissions.View);
        api.MapPost("", CreateAsync).RequirePermission(CustomerPermissions.Edit);
        api.MapPut("/{customerId:guid}", UpdateAsync).RequirePermission(CustomerPermissions.Edit);
        api.MapGet("/summary", SummaryAsync).RequirePermission(CustomerPermissions.View);
    }

    private static async Task<IReadOnlyList<CustomerResponse>> ListAsync(
        string? search, CustomersDbContext db, CancellationToken ct)
    {
        var customers = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            customers = customers.Where(c => EF.Functions.ILike(c.FullName, term) || EF.Functions.ILike(c.Phone, term));
        }

        return await customers
            .OrderBy(c => c.FullName)
            .Take(200)
            .Select(c => new CustomerResponse(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.CreatedAt))
            .ToListAsync(ct);
    }

    private static async Task<IResult> CreateAsync(
        CustomerRequest request, CustomersDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = Customer.Create(request.FullName, request.Phone, request.Email, request.Notes, time.GetUtcNow());
        if (!customer.IsSuccess)
        {
            return customer.Error.ToProblem();
        }

        db.Customers.Add(customer.Value);
        return await SaveAsync(db, ct) is { } conflict
            ? conflict.ToProblem()
            : Results.Created($"/api/customers/{customer.Value.Id}", CustomerResponse.From(customer.Value));
    }

    private static async Task<IResult> UpdateAsync(
        Guid customerId, CustomerRequest request, CustomersDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return Error.NotFound("customer_not_found", "This customer does not exist.").ToProblem();
        }

        var updated = customer.Update(request.FullName, request.Phone, request.Email, request.Notes);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        return await SaveAsync(db, ct) is { } conflict
            ? conflict.ToProblem()
            : Results.Ok(CustomerResponse.From(customer));
    }

    private static async Task<Summary> SummaryAsync(CustomersDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new Summary(
            await db.Customers.CountAsync(ct),
            await db.Customers.CountAsync(customer => customer.CreatedAt >= monthStart, ct));
    }

    private static async Task<Error?> SaveAsync(CustomersDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("phone_in_use", "Another customer already has this phone number.");
        }
    }
}
