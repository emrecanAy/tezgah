namespace Tezgah.Platform.Domain;

internal sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary><c>null</c> for platform administrators, who belong to no company.</summary>
    public Guid? TenantId { get; private set; }

    public string Email { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public static User Create(Guid? tenantId, string email, string fullName, string role) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        Role = role,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
