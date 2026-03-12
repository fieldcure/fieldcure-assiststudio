using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FluentView.AI.Models;

public partial class ProviderPreset : INotifyPropertyChanged
{
    private string _name = "";
    private string _providerType = "Mock";
    private string _modelId = "";
    private string? _baseUrl;

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
    /// Whether this provider type requires an API key.
    /// </summary>
    [JsonIgnore]
    public bool RequiresApiKey => ProviderType is "Claude" or "OpenAI" or "Gemini" or "Groq";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
