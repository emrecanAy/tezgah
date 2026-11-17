namespace Tezgah.Core.Contracts;

/// <summary>
/// What other modules may ask the Customers module. They keep only customer ids and come here
/// for names, so customer data lives in exactly one place.
/// </summary>
public interface ICustomerDirectory
{
    Task<bool> ExistsAsync(Guid customerId, CancellationToken ct);

    /// <summary>Looks up the given customers. Unknown ids are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, CustomerRef>> GetAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken ct);

    Task<IReadOnlyList<CustomerRef>> ListAsync(int take, CancellationToken ct);
}

public sealed record CustomerRef(Guid Id, string FullName, string Phone);
