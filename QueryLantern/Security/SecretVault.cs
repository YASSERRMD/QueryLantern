namespace QueryLantern.Security;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Encrypts secret material (connection passwords, API keys) at rest with AES 256 GCM and stores
/// only opaque references in the catalog. The symmetric key lives in a local key file outside the
/// catalog so the catalog alone never exposes plaintext secrets.
/// </summary>
public sealed class SecretVault : IDisposable
{
    private readonly string _keyPath;
    private readonly byte[] _key;
    private bool _disposed;

    public SecretVault(string keyPath)
    {
        _keyPath = keyPath;
        _key = LoadOrCreateKey(keyPath);
    }

    private static byte[] LoadOrCreateKey(string keyPath)
    {
        if (File.Exists(keyPath))
        {
            var bytes = File.ReadAllBytes(keyPath);
            if (bytes.Length == 32)
            {
                return bytes;
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(keyPath, key);
        return key;
    }

    /// <summary>
    /// Encrypts plaintext and returns a self describing reference token. The token embeds the IV,
    /// auth tag, and ciphertext, base64 encoded, so it is portable across processes on this machine.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var packed = new byte[4 + nonce.Length + tag.Length + ciphertext.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(0, 4), ciphertext.Length);
        nonce.CopyTo(packed, 4);
        tag.CopyTo(packed, 4 + nonce.Length);
        ciphertext.CopyTo(packed, 4 + nonce.Length + tag.Length);
        return "vault://" + Convert.ToBase64String(packed);
    }

    /// <summary>
    /// Resolves a reference token produced by <see cref="Encrypt"/> back to its plaintext. Tokens
    /// that are not vault references are returned unchanged so callers can pass through plain values.
    /// </summary>
    public string Decrypt(string reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!reference.StartsWith("vault://", StringComparison.Ordinal))
        {
            return reference;
        }

        var packed = Convert.FromBase64String(reference["vault://".Length..]);
        var ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(0, 4));
        var nonce = packed.AsSpan(4, 12).ToArray();
        var tag = packed.AsSpan(4 + 12, 16).ToArray();
        var ciphertext = packed.AsSpan(4 + 12 + 16, ciphertextLength).ToArray();
        var plaintext = new byte[ciphertextLength];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public void Dispose()
    {
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }
}
