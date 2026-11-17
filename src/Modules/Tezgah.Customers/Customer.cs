using Tezgah.Core.Results;
using Tezgah.Core.Tenancy;

namespace Tezgah.Customers;

internal sealed class Customer : ITenantOwned
{
    private Customer()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FullName { get; private set; } = null!;

    /// <summary>Digits only, so "0532 111 22 33" and "05321112233" are the same customer.</summary>
    public string Phone { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Customer> Create(string? fullName, string? phone, string? email, string? notes, DateTimeOffset now)
    {
        var customer = new Customer { Id = Guid.CreateVersion7(), CreatedAt = now };
        var updated = customer.Update(fullName, phone, email, notes);
        return updated.IsSuccess ? customer : updated.Error;
    }

    public Result Update(string? fullName, string? phone, string? email, string? notes)
    {
        var digits = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (string.IsNullOrWhiteSpace(fullName) || digits.Length < 10)
        {
            return Error.Validation("invalid_customer", "A name and a phone number of at least 10 digits are required.");
        }

        FullName = fullName.Trim();
        Phone = digits;
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return Result.Success;
    }
}
