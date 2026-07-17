namespace QueryLantern.Models;

/// <summary>
/// Supported OpenAI compatible provider kinds. Each maps to default endpoint and header shapes.
/// </summary>
public enum ProviderKind
{
    Novita,
    OpenRouter,
    OpenAI,
    Azure,
    Vllm,
    Ollama,
    Nim,
    Custom
}

/// <summary>
/// A saved LLM provider profile. The API key is stored only as a reference resolved by the
/// secret vault, never as plaintext in the catalog.
/// </summary>
public sealed record ProviderProfile
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ProviderKind Kind { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string KeyRef { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
