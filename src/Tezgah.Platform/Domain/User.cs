using Tezgah.Core.Results;
using Tezgah.Core.Web;

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

    /// <summary>A deactivated user cannot sign in, and the tokens they still hold stop working.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Raised whenever the user's access is taken away or changes hands: a new password, a new role,
    /// deactivation. Access tokens carry the version they were issued with and are refused once it moves on.
    /// </summary>
    public int SessionVersion { get; private set; }

    /// <summary>Set while the user signs in with a password that someone else chose for them.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>The authenticator secret, encrypted. Present only once two-step sign-in is switched on.</summary>
    public string? TotpSecret { get; private set; }

    /// <summary>A secret handed to the authenticator app but not confirmed with a code yet.</summary>
    public string? PendingTotpSecret { get; private set; }

    /// <summary>The last 30-second step a code was accepted for; a code is never accepted twice.</summary>
    public long? TotpLastStep { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastSignInAt { get; private set; }

    public bool TwoStepEnabled => TotpSecret is not null;

    public static User Create(Guid? tenantId, string email, string fullName, string role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        Role = role,
        IsActive = true,
        SessionVersion = 1,
        CreatedAt = now,
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>A password the user chose themselves.</summary>
    public void SetPassword(string hash)
    {
        PasswordHash = hash;
        MustChangePassword = false;
        SessionVersion++;
    }

    /// <summary>A password someone else chose (a new account, a reset). The user has to replace it at the next sign-in.</summary>
    public void SetTemporaryPassword(string hash)
    {
        PasswordHash = hash;
        MustChangePassword = true;
        SessionVersion++;
    }

    /// <summary>Upgrades the stored hash after the hasher's settings were strengthened; nothing else changes.</summary>
    public void RehashPassword(string hash) => PasswordHash = hash;

    public void RecordSignIn(DateTimeOffset now) => LastSignInAt = now;

    public void Rename(string fullName) => FullName = fullName.Trim();

    public Result ChangeRole(string role)
    {
        if (TenantId is null || !Roles.IsCompanyRole(role))
        {
            return Error.Validation("invalid_role", "The role is owner, manager or staff.");
        }

        if (role != Role)
        {
            Role = role;
            SessionVersion++;
        }

        return Result.Success;
    }

    public void SetActive(bool isActive)
    {
        if (isActive != IsActive)
        {
            IsActive = isActive;
            SessionVersion++;
        }
    }

    public void BeginTwoStepSetup(string protectedSecret) => PendingTotpSecret = protectedSecret;

    public void EnableTwoStep(long acceptedStep)
    {
        TotpSecret = PendingTotpSecret;
        PendingTotpSecret = null;
        TotpLastStep = acceptedStep;
    }

    public void DisableTwoStep()
    {
        TotpSecret = null;
        PendingTotpSecret = null;
        TotpLastStep = null;
    }

    /// <summary>Records that a code was used, so the same code cannot be replayed within its window.</summary>
    public bool AcceptTotpStep(long step)
    {
        if (step <= TotpLastStep)
        {
            return false;
        }

        TotpLastStep = step;
        return true;
    }
}
