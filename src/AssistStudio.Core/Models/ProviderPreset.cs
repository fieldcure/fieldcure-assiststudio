using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FieldCure.AssistStudio.Models;

/// <summary>
/// A saved AI provider configuration including provider type, model, API key reference, and generation parameters.
/// </summary>
public partial class ProviderPreset : INotifyPropertyChanged
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
    /// The display name of this preset.
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
    /// The model identifier to use with this provider (e.g., "gpt-4o", "claude-sonnet-4-20250514").
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
    /// Whether streaming is enabled for this preset. Default is true.
    /// </summary>
    public bool StreamingEnabled { get; set; } = true;

    /// <summary>
    /// How this preset handles PDF document attachments. Default is Auto (provider determines).
    /// </summary>
    public PdfCapability PdfCapability { get; set; } = PdfCapability.Auto;

    /// <summary>
    /// Whether extended thinking/reasoning is enabled for this preset.
    /// </summary>
    public bool ThinkingEnabled { get; set; }

    /// <summary>
    /// Thinking budget in tokens. Null uses provider default (4096).
    /// For providers that use effort levels (e.g., OpenAI o-series), mapped automatically.
    /// </summary>
    public int? ThinkingBudget { get; set; }

    /// <summary>
    /// Whether this provider type requires an API key.
    /// </summary>
    [JsonIgnore]
    public bool RequiresApiKey => ProviderType is "Claude" or "OpenAI" or "Gemini" or "Groq";

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
