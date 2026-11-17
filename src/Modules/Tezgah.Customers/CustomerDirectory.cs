using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;

namespace Tezgah.Customers;

internal sealed class CustomerDirectory(CustomersDbContext db) : ICustomerDirectory
{
    public Task<bool> ExistsAsync(Guid customerId, CancellationToken ct) =>
        db.Customers.AnyAsync(customer => customer.Id == customerId, ct);

    public async Task<IReadOnlyDictionary<Guid, CustomerRef>> GetAsync(
        IReadOnlyCollection<Guid> customerIds, CancellationToken ct) =>
        await db.Customers.AsNoTracking()
            .Where(customer => customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => new CustomerRef(customer.Id, customer.FullName, customer.Phone), ct);

    public async Task<IReadOnlyList<CustomerRef>> ListAsync(int take, CancellationToken ct) =>
        await db.Customers.AsNoTracking()
            .OrderBy(customer => customer.FullName)
            .Take(take)
            .Select(customer => new CustomerRef(customer.Id, customer.FullName, customer.Phone))
            .ToListAsync(ct);
}
