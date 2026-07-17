namespace QueryLantern.Security;

using System.Security.Cryptography;
using NSec.Cryptography;

/// <summary>
/// Holds the local Ed25519 identity used to sign the activity journal. The private key is persisted
/// to a local file (outside the catalog) and never leaves the machine. The public key identifies
/// this QueryLantern install when exporting or verifying journal entries.
/// </summary>
public sealed class IdentityService : IDisposable
{
    private readonly string _keyPath;
    private readonly Key _key;

    public IdentityService(string keyPath)
    {
        _keyPath = keyPath;
        _key = LoadOrCreate(keyPath);
    }

    public byte[] PublicKey => _key.Export(KeyBlobFormat.RawPublicKey);

    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);

    public byte[] Sign(byte[] data)
    {
        return SignatureAlgorithm.Ed25519.Sign(_key, data);
    }

    public bool Verify(byte[] data, byte[] signature)
    {
        var pub = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return SignatureAlgorithm.Ed25519.Verify(pub, data, signature);
    }

    private static Key LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var bytes = Convert.FromBase64String(File.ReadAllText(path));
            return Key.Import(SignatureAlgorithm.Ed25519, bytes, KeyBlobFormat.RawPrivateKey);
        }

        var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var exported = key.Export(KeyBlobFormat.RawPrivateKey);
        File.WriteAllText(path, Convert.ToBase64String(exported));
        return key;
    }

    public void Dispose() => _key.Dispose();
}
