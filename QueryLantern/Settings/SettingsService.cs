namespace QueryLantern.Settings;

using System;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;

/// <summary>
/// Resolves a saved profile into a fully usable runtime configuration, decrypting secret material
/// on demand and never persisting or logging the plaintext.
/// </summary>
public sealed class SettingsService
{
    private readonly ConnectionRepository _connections;
    private readonly ProviderRepository _providers;
    private readonly SecretVault _vault;

    public SettingsService(ConnectionRepository connections, ProviderRepository providers, SecretVault vault)
    {
        _connections = connections;
        _providers = providers;
        _vault = vault;
    }

    /// <summary>
    /// Resolves a connection profile into its decrypted password plus a provider style connection
    /// string for the active adapter.
    /// </summary>
    public async Task<ResolvedConnection> ResolveConnectionAsync(int id)
    {
        var profile = await _connections.GetAsync(id)
            ?? throw new InvalidOperationException($"Connection profile {id} not found.");
        var password = profile.SecretRef.Length == 0 ? null : _vault.Decrypt(profile.SecretRef);
        return new ResolvedConnection(profile, password);
    }

    /// <summary>
    /// Resolves a provider profile into an Ancora <see cref="ProviderConfig"/>. The API key is
    /// exposed to the process environment only for the lifetime of the provider config lookup.
    /// </summary>
    public async Task<ProviderConfig> ResolveProviderAsync(int id)
    {
        var profile = await _providers.GetAsync(id)
            ?? throw new InvalidOperationException($"Provider profile {id} not found.");
        var apiKey = profile.KeyRef.Length == 0 ? null : _vault.Decrypt(profile.KeyRef);
        var authEnv = $"QL_PROVIDER_KEY_{profile.Id}";
        if (apiKey is not null)
        {
            Environment.SetEnvironmentVariable(authEnv, apiKey);
        }

        return new ProviderConfig(profile.BaseUrl, authEnv, "/v1/chat/completions");
    }
}

/// <summary>
/// A connection profile with its decrypted password materialised for the current operation only.
/// </summary>
public sealed record ResolvedConnection(ConnectionProfile Profile, string? Password);
