namespace QueryLantern.Providers;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Models;

/// <summary>
/// A thin OpenAI compatible client used to validate a provider profile (test connection) and to
/// expose the request shape. Actual agent runs go through Ancora, which consumes the same endpoint.
/// </summary>
public sealed class ProviderClient : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    public ProviderClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Builds the request headers for a provider, embedding the API key into the kind specific
    /// header templates. Secrets never leave this method as plaintext in logs.
    /// </summary>
    public static Dictionary<string, string> BuildHeaders(ProviderProfile profile, string? apiKey)
    {
        var template = ProviderDefaults.For(profile.Kind).HeaderTemplates;
        var headers = new Dictionary<string, string>();
        foreach (var (name, value) in template)
        {
            headers[name] = value.Replace("{key}", apiKey ?? string.Empty, StringComparison.Ordinal);
        }

        return headers;
    }

    /// <summary>
    /// Performs a minimal chat completion round trip to confirm the endpoint, model, and key work.
    /// Returns the model reply text (or an error message prefixed with "error:").
    /// </summary>
    public async Task<string> TestProviderAsync(
        ProviderProfile profile,
        string? apiKey,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var url = profile.BaseUrl.TrimEnd('/') + "/chat/completions";
        var request = new
        {
            model = profile.ModelId,
            messages = new[] { new { role = "user", content = "ping" } },
            max_tokens = 5,
            stream = false
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request)
        };
        foreach (var (name, value) in BuildHeaders(profile, apiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await _http.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return $"error: {response.StatusCode} {Truncate(body)}";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return text ?? string.Empty;
        }
        catch (Exception)
        {
            return $"error: unexpected response shape: {Truncate(body)}";
        }
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;

    public void Dispose()
    {
        _disposed = true;
        _http.Dispose();
    }
}
