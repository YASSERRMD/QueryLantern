namespace QueryLantern.Security;

using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;

/// <summary>
/// Persists connection and provider profiles while storing only encrypted secret references in
/// the catalog. Plaintext passwords and API keys never reach the catalog tables.
/// </summary>
public sealed class ProfileSecrets
{
    private readonly SecretVault _vault;
    private readonly ConnectionRepository _connections;
    private readonly ProviderRepository _providers;

    public ProfileSecrets(SecretVault vault, ConnectionRepository connections, ProviderRepository providers)
    {
        _vault = vault;
        _connections = connections;
        _providers = providers;
    }

    /// <summary>
    /// Saves a connection, encrypting the supplied password and storing only its reference.
    /// </summary>
    public async Task<int> SaveConnectionAsync(ConnectionProfile profile, string? password)
    {
        var secretRef = password is null ? string.Empty : _vault.Encrypt(password);
        return await _connections.InsertAsync(profile with { SecretRef = secretRef });
    }

    /// <summary>
    /// Saves a provider, encrypting the supplied API key and storing only its reference.
    /// </summary>
    public async Task<int> SaveProviderAsync(ProviderProfile profile, string? apiKey)
    {
        var keyRef = apiKey is null ? string.Empty : _vault.Encrypt(apiKey);
        return await _providers.InsertAsync(profile with { KeyRef = keyRef });
    }
}
