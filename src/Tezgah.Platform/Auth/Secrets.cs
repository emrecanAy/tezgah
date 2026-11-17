using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tezgah.Platform.Auth;

/// <summary>Random tokens, their hashes, and encryption of the secrets that must be read back.</summary>
internal sealed class Secrets(IOptions<AuthOptions> options)
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.EncryptionKey));

    /// <summary>256 random bits, URL-safe.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// SHA-256 is enough for tokens and recovery codes: they carry full entropy, so there is nothing to
    /// guess, and a lookup by hash stays an index seek.
    /// </summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A password for someone else to type once: 16 characters with no look-alikes (0/O, 1/l).</summary>
    public static string NewTemporaryPassword() =>
        RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 16);

    /// <summary>AES-GCM: confidential and tamper-evident. Output is nonce, tag and ciphertext, base64.</summary>
    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }

        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText)
    {
        var bytes = Convert.FromBase64String(protectedText);
        var (nonceSize, tagSize) = (AesGcm.NonceByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
        var plain = new byte[bytes.Length - nonceSize - tagSize];
        using (var aes = new AesGcm(_key, tagSize))
        {
            aes.Decrypt(bytes.AsSpan(0, nonceSize), bytes.AsSpan(nonceSize + tagSize), bytes.AsSpan(nonceSize, tagSize), plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
