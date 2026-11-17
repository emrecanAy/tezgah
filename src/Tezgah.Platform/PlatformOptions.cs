namespace Tezgah.Platform;

public sealed class PlatformOptions
{
    /// <summary>The first platform administrator, created at start-up if no account has this e-mail yet.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Must meet the password policy; the API refuses to start otherwise.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Creates three example companies with data when the platform has none.</summary>
    public bool SeedDemoData { get; set; }
}
