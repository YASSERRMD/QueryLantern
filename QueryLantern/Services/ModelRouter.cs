namespace QueryLantern.Services;

using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Settings;

/// <summary>
/// Resolves a saved provider profile into the Ancora runtime config and model id used to start a
/// run. Supports a per conversation model override so different chats can use different providers
/// or models without editing the saved profile.
/// </summary>
public sealed class ModelRouter
{
    private readonly SettingsService _settings;

    public ModelRouter(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Resolves the provider profile into a provider config. The API key is placed in the environment
    /// variable named by the provider config so Ancora can read it, then returns the config plus the
    /// model id to use (the override wins over the saved model id).
    /// </summary>
    public async Task<(ProviderConfig Config, string Model)> ResolveAsync(
        int providerProfileId,
        string? modelOverride = null,
        CancellationToken ct = default)
    {
        var config = await _settings.ResolveProviderAsync(providerProfileId);
        var profile = await _settings.GetProviderProfileAsync(providerProfileId)
            ?? throw new System.InvalidOperationException($"Provider profile {providerProfileId} not found.");
        var model = modelOverride ?? profile.ModelId;
        return (config, model);
    }
}
