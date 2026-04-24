using FieldCure.AssistStudio.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Settings;

/// <summary>
/// Presentation model for one knowledge-base card hosted by the settings page.
/// </summary>
public sealed class KbItemViewModel : INotifyPropertyChanged
{
    private KnowledgeBase _kb;
    private string _searchQuery = "";
    private Task? _ragReadyTask;
    private int _refreshToken;
    private bool _isDeferredVisual;

    public KbItemViewModel(KnowledgeBase kb, string searchQuery, Task? ragReadyTask)
    {
        _kb = kb;
        _searchQuery = searchQuery;
        _ragReadyTask = ragReadyTask;
    }

    public KnowledgeBase Kb
    {
        get => _kb;
        set
        {
            if (ReferenceEquals(_kb, value))
                return;

            _kb = value;
            OnPropertyChanged();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value)
                return;

            _searchQuery = value;
            OnPropertyChanged();
        }
    }

    public Task? RagReadyTask
    {
        get => _ragReadyTask;
        set
        {
            if (ReferenceEquals(_ragReadyTask, value))
                return;

            _ragReadyTask = value;
            OnPropertyChanged();
        }
    }

    public int RefreshToken
    {
        get => _refreshToken;
        private set
        {
            if (_refreshToken == value)
                return;

            _refreshToken = value;
            OnPropertyChanged();
        }
    }

    public bool IsDeferredVisual
    {
        get => _isDeferredVisual;
        private set
        {
            if (_isDeferredVisual == value)
                return;

            _isDeferredVisual = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RequestRefresh()
    {
        RefreshToken++;
    }

    public void SetDeferredVisual(bool isDeferred)
    {
        IsDeferredVisual = isDeferred;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
