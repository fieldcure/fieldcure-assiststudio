using System.Text.Json.Serialization;

namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Knowledge base configuration stored in <c>{kb-path}/config.json</c>.
/// Schema matches <c>FieldCure.Mcp.Rag</c>'s <c>RagConfig</c> exactly.
/// </summary>
public sealed class KnowledgeBase
{
    /// <summary>UUID assigned when the KB is created.</summary>
    public string Id { get; set; } = "";

    /// <summary>User-provided display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>ISO 8601 creation timestamp.</summary>
    public string Created { get; set; } = "";

    /// <summary>Folders to index.</summary>
    public List<string> SourcePaths { get; set; } = [];

    /// <summary>Chunk contextualization provider settings.</summary>
    public KbProviderConfig Contextualizer { get; set; } = new();

    /// <summary>Embedding provider settings.</summary>
    public KbProviderConfig Embedding { get; set; } = new();

    /// <summary>Custom system prompt for chunk contextualization. Null = built-in default.</summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Provider configuration for contextualizer or embedding within a <see cref="KnowledgeBase"/>.
/// </summary>
public sealed class KbProviderConfig
{
    /// <summary>Provider type: "openai", "anthropic", "ollama", etc.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Model ID (e.g., "text-embedding-3-small").</summary>
    public string Model { get; set; } = "";

    /// <summary>API base URL override. Null = provider default.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// PasswordVault preset name for API key lookup (e.g., "Claude", "OpenAI").
    /// Null for providers that don't need a key (e.g., Ollama).
    /// </summary>
    public string? ApiKeyPreset { get; set; }

    /// <summary>Vector dimension override (embedding only, 0 = auto-detect).</summary>
    public int Dimension { get; set; }
}

/// <summary>
/// Source-generated JSON serialization context for <see cref="KnowledgeBase"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(KnowledgeBase))]
public partial class KnowledgeBaseJsonContext : JsonSerializerContext;
