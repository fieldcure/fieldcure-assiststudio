using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Settings;

/// <summary>
/// Presentation model for a single memory entry rendered by <see cref="Controls.MemoryEntryCard"/>.
/// </summary>
public sealed class MemoryEntryItemViewModel : INotifyPropertyChanged
{
    private bool _isDeleted;

    public MemoryEntryItemViewModel(string key, string value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>Stable memory key returned by the Essentials MCP server.</summary>
    public string Key { get; }

    /// <summary>User-visible memory value.</summary>
    public string Value { get; }

    /// <summary>
    /// Indicates that the backing memory entry was deleted and should be removed
    /// from the page collection.
    /// </summary>
    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (_isDeleted == value)
                return;

            _isDeleted = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Marks this entry as deleted after the card completes the backend action.</summary>
    public void MarkDeleted()
    {
        IsDeleted = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
