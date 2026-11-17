using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Appointments;

/// <summary>Something the company can be booked for: a haircut, a consultation, a tyre change.</summary>
internal sealed class Service : ITenantOwned
{
    private Service()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public int DurationMinutes { get; private set; }

    public decimal Price { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Service> Create(string? name, int durationMinutes, decimal price)
    {
        var service = new Service { Id = Guid.CreateVersion7() };
        var updated = service.Update(name, durationMinutes, price, isActive: true);
        return updated.IsSuccess ? service : updated.Error;
    }

    public Result Update(string? name, int durationMinutes, decimal price, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || durationMinutes is < 5 or > 480 || price < 0)
        {
            return Error.Validation("invalid_service",
                "A service needs a name, a duration between 5 minutes and 8 hours, and a price that is not negative.");
        }

        Name = name.Trim();
        DurationMinutes = durationMinutes;
        Price = price;
        IsActive = isActive;
        return Result.Success;
    }
}

/// <summary>A person (or chair, or room) whose calendar appointments are booked into.</summary>
internal sealed class StaffMember : ITenantOwned
{
    private StaffMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static Result<StaffMember> Create(string? name)
    {
        var staff = new StaffMember { Id = Guid.CreateVersion7() };
        var updated = staff.Update(name, isActive: true);
        return updated.IsSuccess ? staff : updated.Error;
    }

    public Result Update(string? name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("invalid_staff", "A staff member needs a name.");
        }

        Name = name.Trim();
        IsActive = isActive;
        return Result.Success;
    }
}
