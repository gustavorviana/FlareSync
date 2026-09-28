using System.Security.Cryptography;
using System.Text;

namespace FlareSync.Core.Secrets;

/// <summary>Encrypts secrets stored in configuration files.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);

    bool IsProtected(string value);
}

/// <summary>
/// AES-256-GCM protector. Format: <c>enc:v1:</c> + base64(nonce[12] | ciphertext | tag[16]).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string Prefix = "enc:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Func<byte[]> _encryptionKey;
    private readonly Func<byte[]> _decryptionKey;

    public AesGcmSecretProtector(MasterKeyProvider keys)
        : this(keys.GetOrCreateKey, keys.GetKey)
    {
    }

    public AesGcmSecretProtector(byte[] key)
        : this(() => key, () => key)
    {
    }

    private AesGcmSecretProtector(Func<byte[]> encryptionKey, Func<byte[]> decryptionKey)
    {
        _encryptionKey = encryptionKey;
        _decryptionKey = decryptionKey;
    }

    public bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + plain.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, plain.Length);
        var tag = payload.AsSpan(NonceSize + plain.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_encryptionKey(), TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        if (!IsProtected(protectedValue))
        {
            throw new FlareSyncException("Secret is not in the expected encrypted format.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new FlareSyncException("Encrypted secret is corrupted (invalid base64).", ex);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new FlareSyncException("Encrypted secret is corrupted (too short).");
        }

        var cipherLength = payload.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(_decryptionKey(), TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize, cipherLength),
                payload.AsSpan(NonceSize + cipherLength, TagSize),
                plain);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new FlareSyncException(
                "Could not decrypt secret: the master key does not match or the value was modified. Log in again.", ex);
        }

        return Encoding.UTF8.GetString(plain);
    }
}
