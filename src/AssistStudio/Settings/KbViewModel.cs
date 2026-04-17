using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AssistStudio.Settings;

/// <summary>
/// View model for a knowledge base item in the list.
/// </summary>
public sealed partial class KbViewModel : INotifyPropertyChanged
{
    #region Fields

    private string _id = "";
    private string _name = "";
    private string _sourcePathsText = "";
    private string _modelInfoText = "";
    private string _statusText = "";
    private Brush _statusBrush = new SolidColorBrush(Colors.Gray);
    private string _statsText = "";
    private Visibility _isIndexing = Visibility.Collapsed;
    private double _progress;
    private bool _isPromptStale;
    private int _changesAdded;
    private int _changesModified;
    private int _changesDeleted;
    private int _changesFailed;
    private bool? _changesChecked;
    private bool _isDeferredIndexing;

    #endregion

    #region Properties

    /// <summary>Stable identifier of the underlying <see cref="FieldCure.AssistStudio.Models.KnowledgeBase"/>.</summary>
    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    /// <summary>Display name of the KB shown as the card title.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>Pre-formatted, comma-joined list of source folder paths shown under the title.</summary>
    public string SourcePathsText
    {
        get => _sourcePathsText;
        set => SetField(ref _sourcePathsText, value);
    }

    /// <summary>
    /// Pre-formatted "embedding · contextualizer" line summarizing the
    /// models the KB was indexed with.
    /// </summary>
    public string ModelInfoText
    {
        get => _modelInfoText;
        set => SetField(ref _modelInfoText, value);
    }

    /// <summary>Localized status label (e.g. "Ready", "Indexing", "Scheduled").</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>Foreground brush used to color <see cref="StatusText"/> per state.</summary>
    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetField(ref _statusBrush, value);
    }

    /// <summary>
    /// Pre-formatted stats line (file count, total size, etc.) shown when
    /// stats are available for the KB.
    /// </summary>
    public string StatsText
    {
        get => _statsText;
        set => SetField(ref _statsText, value);
    }

    /// <summary>
    /// Visibility of the progress row. Visible while an indexing run is
    /// active for this KB; collapsed otherwise.
    /// </summary>
    public Visibility IsIndexing
    {
        get => _isIndexing;
        set => SetField(ref _isIndexing, value);
    }

    /// <summary>Indexing progress in the 0..1 range, bound to the progress bar.</summary>
    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    /// <summary>
    /// <c>true</c> when the KB's stored prompt-derived fields are known to be
    /// stale versus the current configuration and a re-index is suggested.
    /// </summary>
    public bool IsPromptStale
    {
        get => _isPromptStale;
        set => SetField(ref _isPromptStale, value);
    }

    /// <summary>
    /// Change-detection result: number of files added since the last
    /// successful index (populated after the "Check changes" action).
    /// </summary>
    public int ChangesAdded
    {
        get => _changesAdded;
        set => SetField(ref _changesAdded, value);
    }

    /// <summary>Change-detection result: number of files modified since the last successful index.</summary>
    public int ChangesModified
    {
        get => _changesModified;
        set => SetField(ref _changesModified, value);
    }

    /// <summary>Change-detection result: number of files deleted since the last successful index.</summary>
    public int ChangesDeleted
    {
        get => _changesDeleted;
        set => SetField(ref _changesDeleted, value);
    }

    /// <summary>Change-detection result: number of files that failed to probe during the check.</summary>
    public int ChangesFailed
    {
        get => _changesFailed;
        set => SetField(ref _changesFailed, value);
    }

    /// <summary>
    /// Tri-state indicator for the change-detection row:
    /// <c>null</c> = not checked yet, <c>true</c> = checked and changes found,
    /// <c>false</c> = checked and no changes found.
    /// </summary>
    public bool? ChangesChecked
    {
        get => _changesChecked;
        set => SetField(ref _changesChecked, value);
    }

    /// <summary>Whether this KB is in the deferred indexing queue.</summary>
    public bool IsDeferredIndexing
    {
        get => _isDeferredIndexing;
        set => SetField(ref _isDeferredIndexing, value);
    }

    #endregion

    #region INotifyPropertyChanged

    /// <summary>Fires whenever any bound property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and
    /// raises <see cref="PropertyChanged"/> when the value actually changes.
    /// No-op when the new value equals the current one.
    /// </summary>
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
