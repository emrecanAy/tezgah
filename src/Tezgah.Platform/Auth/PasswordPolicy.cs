using Tezgah.Core.Results;

namespace Tezgah.Platform.Auth;

/// <summary>
/// Length over composition, as NIST SP 800-63B advises: at least 12 characters, any characters,
/// spaces welcome, no forced mix of digits and symbols. Refused: the well-known passwords and the
/// e-mail address itself.
/// </summary>
internal static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    // The commonest passwords that are 12 characters or longer, from public breach corpora.
    // Shorter ones are already refused by the length rule.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "12345678910", "123456789123", "qwertyuiopas", "qwerty123456",
        "password1234", "password12345", "password123!", "passwordpassword", "iloveyou1234", "abc123456789",
        "111111111111", "000000000000", "123123123123", "aaaaaaaaaaaa", "qwertyqwerty", "1q2w3e4r5t6y",
        "1qaz2wsx3edc", "zaq12wsxcde3", "trustno1trustno1", "letmein12345", "welcome12345", "administrator",
        "superman1234", "football1234", "baseball1234", "monkey123456", "dragon123456", "sunshine1234",
        "princess1234", "starwars1234", "changeme1234", "default12345", "galatasaray1", "fenerbahce19",
        "besiktas1903", "trabzonspor1", "sifre1234567", "parola123456", "123456789qwe", "qwe123456789",
    };

    public static Result Check(string? password, string email)
    {
        if (password is null || password.Length < MinLength)
        {
            return Error.Validation("weak_password", $"A password has at least {MinLength} characters.");
        }

        if (password.Length > MaxLength)
        {
            return Error.Validation("weak_password", $"A password has at most {MaxLength} characters.");
        }

        var localPart = email.Split('@')[0];
        if (Common.Contains(password) || password.Distinct().Count() < 4
            || (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Validation("weak_password", "This password is too easy to guess.");
        }

        return Result.Success;
    }
}
