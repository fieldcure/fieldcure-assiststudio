using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Controls;

/// <summary>
/// View-model for a single row in the cloud / Ollama provider model checklist.
/// Combines the persistent <see cref="ModelId"/> with mutable enabled/state flags.
/// </summary>
public sealed class ModelChecklistItem : INotifyPropertyChanged
{
    /// <summary>The provider's model identifier (e.g. "claude-opus-4-7"). Immutable.</summary>
    public string ModelId { get; }

    private bool _isEnabled;
    /// <summary>Two-way bound checkbox state — true when the model is registered as a <c>ProviderModel</c>.</summary>
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

    private bool _isManuallyAdded;
    /// <summary>True when the user typed this ID via "+ Add model ID" rather than the upstream /models response.</summary>
    public bool IsManuallyAdded
    {
        get => _isManuallyAdded;
        set
        {
            if (_isManuallyAdded == value) return;
            _isManuallyAdded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(StateTooltip));
        }
    }

    private bool _isMissingUpstream;
    /// <summary>True when the model was previously registered/cached but is absent from the latest upstream fetch.</summary>
    public bool IsMissingUpstream
    {
        get => _isMissingUpstream;
        set
        {
            if (_isMissingUpstream == value) return;
            _isMissingUpstream = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(StateTooltip));
        }
    }

    private Brush? _rowBackground;
    /// <summary>Optional row background used for momentary highlight on duplicate-add.</summary>
    public Brush? RowBackground
    {
        get => _rowBackground;
        set
        {
            _rowBackground = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Derived opacity: 0.55 when manually added or missing upstream; 1.0 otherwise.</summary>
    public double RowOpacity => (_isManuallyAdded || _isMissingUpstream) ? 0.55 : 1.0;

    /// <summary>Derived tooltip describing why the row is greyed out, or null when the row is normal.</summary>
    public string? StateTooltip
    {
        get
        {
            if (_isMissingUpstream) return "Removed from upstream model list";
            if (_isManuallyAdded) return "Manually added";
            return null;
        }
    }

    /// <summary>Initializes a new <see cref="ModelChecklistItem"/> with the given <paramref name="modelId"/>.</summary>
    public ModelChecklistItem(string modelId) => ModelId = modelId;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property name.</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
