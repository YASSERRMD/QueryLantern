namespace QueryLantern.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using QueryLantern.Security;
using Xunit;

public class SecretVaultTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"ql_key_{Guid.NewGuid():N}.key");
    private readonly SecretVault _vault;

    public SecretVaultTests()
    {
        _vault = new SecretVault(_keyPath);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var secret = "s3cr3t-p@ssword!";
        var reference = _vault.Encrypt(secret);
        Assert.StartsWith("vault://", reference);
        Assert.DoesNotContain(secret, reference);
        Assert.Equal(secret, _vault.Decrypt(reference));
    }

    [Fact]
    public void Decrypt_NonVaultValue_PassesThrough()
    {
        Assert.Equal("plain", _vault.Decrypt("plain"));
    }

    [Fact]
    public void Encrypt_ProducesUniqueTokens()
    {
        var a = _vault.Encrypt("x");
        var b = _vault.Encrypt("x");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SameKeyFile_DecryptsAcrossInstances()
    {
        var reference = _vault.Encrypt("shared-secret");
        using var other = new SecretVault(_keyPath);
        Assert.Equal("shared-secret", other.Decrypt(reference));
    }

    [Fact]
    public void Redactor_Masks_Secrets()
    {
        Assert.Equal("***", SecretRedactor.Mask("real-password"));
        Assert.Equal(string.Empty, SecretRedactor.Mask(null));
    }

    public void Dispose()
    {
        _vault.Dispose();
        if (File.Exists(_keyPath))
        {
            File.Delete(_keyPath);
        }
    }
}
