namespace QueryLantern.Providers;

using System.Collections.Generic;

/// <summary>
/// Default endpoint and header shapes for each supported OpenAI compatible provider kind. When the
/// user picks a kind in the UI, these prefill the base URL and any kind specific headers.
/// </summary>
public static class ProviderDefaults
{
    private static readonly Dictionary<Models.ProviderKind, ProviderTemplate> Templates = new()
    {
        [Models.ProviderKind.Novita] = new("https://novita.ai/v1", new() { ["Authorization"] = "Bearer {key}" }),
        [Models.ProviderKind.OpenRouter] = new("https://openrouter.ai/api/v1", new() { ["Authorization"] = "Bearer {key}", ["HTTP-Referer"] = "https://querylantern.dev" }),
        [Models.ProviderKind.OpenAI] = new("https://api.openai.com/v1", new() { ["Authorization"] = "Bearer {key}" }),
        [Models.ProviderKind.Azure] = new("https://YOUR-RESOURCE.openai.azure.com/openai", new() { ["api-key"] = "{key}" }),
        [Models.ProviderKind.Vllm] = new("http://localhost:8000/v1", new() { ["Authorization"] = "Bearer {key}" }),
        [Models.ProviderKind.Ollama] = new("http://localhost:11434/v1", new() { ["Authorization"] = "Bearer {key}" }),
        [Models.ProviderKind.Nim] = new("https://integrate.api.nvidia.com/v1", new() { ["Authorization"] = "Bearer {key}" }),
        [Models.ProviderKind.Custom] = new("https://api.example.com/v1", new() { ["Authorization"] = "Bearer {key}" })
    };

    public static ProviderTemplate For(Models.ProviderKind kind) => Templates[kind];

    public static IEnumerable<Models.ProviderKind> AllKinds => Templates.Keys;
}

/// <summary>
/// A prefilled template for a provider kind: the default base URL and the header names whose values
/// embed the API key.
/// </summary>
public sealed record ProviderTemplate(string BaseUrl, Dictionary<string, string> HeaderTemplates);
