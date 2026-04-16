using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AssistStudio.Settings;

/// <summary>
/// View model for a knowledge base item in the list.
/// </summary>
public sealed class KbViewModel : INotifyPropertyChanged
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
    private string? _modelWarningText;
    private int _changesAdded;
    private int _changesModified;
    private int _changesDeleted;
    private int _changesFailed;
    private bool? _changesChecked;

    #endregion

    #region Properties

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string SourcePathsText
    {
        get => _sourcePathsText;
        set => SetField(ref _sourcePathsText, value);
    }

    public string ModelInfoText
    {
        get => _modelInfoText;
        set => SetField(ref _modelInfoText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetField(ref _statusBrush, value);
    }

    public string StatsText
    {
        get => _statsText;
        set => SetField(ref _statsText, value);
    }

    public Visibility IsIndexing
    {
        get => _isIndexing;
        set => SetField(ref _isIndexing, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public bool IsPromptStale
    {
        get => _isPromptStale;
        set => SetField(ref _isPromptStale, value);
    }

    /// <summary>
    /// Short, pre-formatted warning line shown under the model info row
    /// when one or more of the KB's configured models is not reachable.
    /// </summary>
    public string? ModelWarningText
    {
        get => _modelWarningText;
        set => SetField(ref _modelWarningText, value);
    }

    /// <summary>Change detection results (populated after "Check changes" button click).</summary>
    public int ChangesAdded
    {
        get => _changesAdded;
        set => SetField(ref _changesAdded, value);
    }

    public int ChangesModified
    {
        get => _changesModified;
        set => SetField(ref _changesModified, value);
    }

    public int ChangesDeleted
    {
        get => _changesDeleted;
        set => SetField(ref _changesDeleted, value);
    }

    public int ChangesFailed
    {
        get => _changesFailed;
        set => SetField(ref _changesFailed, value);
    }

    public bool? ChangesChecked
    {
        get => _changesChecked;
        set => SetField(ref _changesChecked, value);
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
