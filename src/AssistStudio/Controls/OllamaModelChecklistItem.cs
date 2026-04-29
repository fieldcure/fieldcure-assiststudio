using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Controls;

/// <summary>
/// View-model for an Ollama model row. Carries the locally-pulled model ID, hardware
/// fit label, enabled/disabled flag, and per-model <see cref="NumCtx"/> / <see cref="KeepAlive"/>
/// overrides. Per-model fields are persisted on the matching <c>ProviderModel</c>.
/// </summary>
public sealed class OllamaModelChecklistItem : INotifyPropertyChanged
{
    /// <summary>The Ollama-side model identifier.</summary>
    public string ModelId { get; }

    /// <summary>Hardware-fit label ("GPU", "CPU", "Maybe", or empty) for the row.</summary>
    public string FitLabel { get; }

    private bool _isEnabled;
    /// <summary>True when this model is registered as a <c>ProviderModel</c>.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    private double _numCtxValue = double.NaN;
    /// <summary>Per-model context-window size; <see cref="double.NaN"/> means "use default".</summary>
    public double NumCtxValue
    {
        get => _numCtxValue;
        set
        {
            if (_numCtxValue.Equals(value)) return;
            _numCtxValue = value;
            OnPropertyChanged();
        }
    }

    private string _keepAlive = "";
    /// <summary>Per-model keep-alive duration; empty means "use Ollama default".</summary>
    public string KeepAlive
    {
        get => _keepAlive;
        set
        {
            if (_keepAlive == value) return;
            _keepAlive = value ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>Initializes a new <see cref="OllamaModelChecklistItem"/>.</summary>
    public OllamaModelChecklistItem(string modelId, string fitLabel)
    {
        ModelId = modelId;
        FitLabel = fitLabel;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property name.</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
