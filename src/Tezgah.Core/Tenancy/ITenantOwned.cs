namespace Tezgah.Core.Tenancy;

/// <summary>
/// Marks data that belongs to one company. <see cref="TenantDbContext"/> fills in the tenant on
/// insert and filters every query by it, so module code never handles tenant ids itself.
/// Declare the property as <c>public Guid TenantId { get; private set; }</c>.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
