using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tezgah.Platform.Auth;

/// <summary>
/// Time-based one-time passwords (RFC 6238): what Google Authenticator, Microsoft Authenticator and
/// 1Password show. 30-second steps, six digits, HMAC-SHA1, the defaults every app expects.
/// </summary>
internal static class Totp
{
    private const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new 160-bit secret, base32-encoded the way authenticator apps take it.</summary>
    public static string NewSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    /// <summary>The address an authenticator app reads from a QR code.</summary>
    public static string SetupUri(string secret, string account) =>
        $"otpauth://totp/Tezg%C3%A2h:{Uri.EscapeDataString(account)}?secret={secret}&issuer=Tezg%C3%A2h&digits=6&period=30";

    /// <summary>
    /// The time step the code belongs to, or null when it matches none. One step either side is allowed
    /// for a phone clock that runs a little off.
    /// </summary>
    public static long? Match(string secret, string? code, DateTimeOffset now)
    {
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var key = FromBase32(secret);
        var current = now.ToUnixTimeSeconds() / StepSeconds;
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Code(key, step).AsSpan().AsBytes(), code.AsSpan().AsBytes()))
            {
                return step;
            }
        }

        return null;
    }

    public static string Code(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7FFFFFFF) % 1_000_000;
        return binary.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static byte[] FromBase32(string text)
    {
        var bits = 0;
        var value = 0;
        var output = new List<byte>(text.Length * 5 / 8);
        foreach (var c in text.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }

    private static string ToBase32(byte[] bytes)
    {
        var output = new System.Text.StringBuilder((bytes.Length * 8 + 4) / 5);
        var (bits, value) = (0, 0);
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Base32Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    private static ReadOnlySpan<byte> AsBytes(this ReadOnlySpan<char> chars) => System.Runtime.InteropServices.MemoryMarshal.AsBytes(chars);
}
