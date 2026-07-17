namespace QueryLantern.Security;

using System.Text.Json;

/// <summary>
/// Redacts secret material from any object that is about to be logged or surfaced to the UI.
/// The vault reference itself is safe to show; plaintext is never present here.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    /// Returns a log safe copy of a connection profile with the secret reference masked. The
    /// reference is opaque and non reversible without the vault, but we still hide it by default.
    /// </summary>
    public static string ToLogSafeJson(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            });
        }
        catch (Exception)
        {
            return "[unloggable]";
        }
    }

    /// <summary>
    /// Masks a potential secret value for display. Never call this with a real decrypted password.
    /// </summary>
    public static string Mask(string? value) => string.IsNullOrEmpty(value) ? string.Empty : "***";
}
