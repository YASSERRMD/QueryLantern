namespace QueryLantern.Services;

using System;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Enforces the local-first / air-gapped posture: when enabled, the app refuses to store provider
/// profiles that point at non-loopback hosts, so all model traffic stays on the machine or private
/// network. External endpoints require an explicit opt-out.
/// </summary>
public sealed class LocalFirstService
{
    public bool Enabled { get; }

    public LocalFirstService(IConfiguration config)
    {
        Enabled = config["LocalFirst:Enabled"] != "false";
    }

    /// <summary>
    /// Returns null if the URL is allowed, otherwise a reason it is rejected.
    /// </summary>
    public string? RejectReasonIfBlocked(string baseUrl)
    {
        if (!Enabled) return null;
        if (string.IsNullOrWhiteSpace(baseUrl)) return "A provider base URL is required.";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return "The provider base URL is not a valid absolute URI.";
        var host = uri.Host;
        var isLocal = host is "localhost" or "127.0.0.1" or "::1" || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host == "[::1]" ||
                      IsPrivateIp(host);
        return isLocal ? null : $"Local-first mode blocks external host '{host}'. Disable LocalFirst to allow remote providers.";
    }

    private static bool IsPrivateIp(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
               (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168) || bytes[0] == 169 && bytes[1] == 254);
    }
}
