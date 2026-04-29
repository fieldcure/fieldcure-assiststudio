using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Represents a single (Provider × Model) entry. Multiple instances per
/// ProviderType are allowed — one per enabled model.
///
/// <para>
/// Per-Provider broadcast fields (shared across all instances of the same
/// ProviderType): <see cref="MaxTokens"/>, <see cref="Temperature"/>,
/// <see cref="StreamingEnabled"/>, <see cref="PdfCapability"/>,
/// <see cref="ThinkingEnabled"/>, <see cref="ThinkingOverride"/>,
/// <see cref="ThinkingBudget"/>. The storage layer broadcasts changes to
/// these fields when the user edits any one instance in the Models page.
/// </para>
/// <para>
/// Per-model fields (unique to each instance, even within the same
/// ProviderType): <see cref="KeepAlive"/>, <see cref="NumCtx"/>. These are
/// physical constraints that vary by Ollama model size and host VRAM, so
/// each ProviderModel carries its own value (nullable; null falls back to
/// Ollama protocol defaults).
/// </para>
/// </summary>
public partial class ProviderModel : INotifyPropertyChanged
{
    #region Fields

    /// <summary>Backing field for <see cref="Name"/>.</summary>
    private string _name = "";

    /// <summary>Backing field for <see cref="ProviderType"/>.</summary>
    private string _providerType = "Mock";

    /// <summary>Backing field for <see cref="ModelId"/>.</summary>
    private string _modelId = "";

    /// <summary>Backing field for <see cref="BaseUrl"/>.</summary>
    private string? _baseUrl;

    #endregion

    #region Properties

    /// <summary>
    /// The display name of this ProviderModel.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// Provider type key: "Mock", "Claude", "OpenAI", "Ollama", "Gemini", "Groq"
    /// </summary>
    public string ProviderType
    {
        get => _providerType;
        set => SetField(ref _providerType, value);
    }

    /// <summary>
    /// API key is not serialized — stored in PasswordVault separately.
    /// </summary>
    [JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// The model identifier to use with this provider (e.g., "gpt-4o", "claude-sonnet-4-6").
    /// </summary>
    public string ModelId
    {
        get => _modelId;
        set => SetField(ref _modelId, value);
    }

    /// <summary>
    /// Custom base URL for compatible endpoints (e.g., Groq).
    /// </summary>
    public string? BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value);
    }

    /// <summary>
    /// Sampling temperature (0.0–2.0). Default is 0.7.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens for the response. Default is 4096.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Whether streaming is enabled for this ProviderModel. Default is true.
    /// </summary>
    public bool StreamingEnabled { get; set; } = true;

    /// <summary>
    /// How this ProviderModel handles PDF document attachments. Default is Auto (provider determines).
    /// </summary>
    public PdfCapability PdfCapability { get; set; } = PdfCapability.Auto;

    /// <summary>
    /// Whether extended thinking/reasoning is enabled for this ProviderModel.
    /// </summary>
    public bool ThinkingEnabled { get; set; }

    /// <summary>
    /// Thinking budget in tokens. Null uses provider default.
    /// For providers that use effort levels (e.g., OpenAI o-series), mapped automatically.
    /// </summary>
    public int? ThinkingBudget { get; set; }

    /// <summary>
    /// User override for thinking support detection.
    /// Auto: use provider's heuristic. ForceOn: always treat as thinking-capable.
    /// ForceOff: always treat as non-thinking.
    /// </summary>
    public ThinkingOverride ThinkingOverride { get; set; } = ThinkingOverride.Auto;

    /// <summary>
    /// Ollama-specific: duration to keep the model loaded in VRAM after the last request.
    /// Go duration format ("30m", "1h", "-1" for permanent, "0" for immediate unload).
    /// Null = Ollama built-in default (5m). Ignored for non-Ollama providers.
    /// </summary>
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Ollama-specific: context window size in tokens.
    /// Null = default 8192. Ignored for non-Ollama providers.
    /// </summary>
    public int? NumCtx { get; set; }

    /// <summary>
    /// Whether this provider type requires an API key.
    /// </summary>
    [JsonIgnore]
    public bool RequiresApiKey => ProviderType is "Claude" or "OpenAI" or "Gemini" or "Groq"
        || ProviderType.StartsWith("Custom_");

    #endregion

    #region INotifyPropertyChanged

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    /// <summary>Sets a backing field and raises <see cref="PropertyChanged"/> if the value changed.</summary>
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
