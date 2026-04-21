using AssistStudio.Controls;
using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing scheduled tasks created by the Runner.
/// Reads from the Runner's SQLite database and synchronizes changes with
/// Windows Task Scheduler. Per-item rendering is delegated to
/// <see cref="ScheduleCard"/>; the page only handles list orchestration
/// and backend effects (SQLite + schtasks).
/// </summary>
public sealed partial class SchedulePage : Page
{
    #region Fields

    private static readonly ResourceLoader Res = new();

    private readonly List<ScheduleCard> _liveCards = [];
    private List<ScheduleItem> _allItems = [];
    private bool _isUpdating;

    private string _deleteTooltip = "Delete";
    private string _loadingText = "Loading schedules...";
    private string _deleteConfirmTitle = "Delete Schedule";
    private string _deleteConfirmContent = "Are you sure you want to delete \"{0}\"?";
    private string _cancelText = "Cancel";
    private string _errorTitle = "Error";
    private string _okText = "OK";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulePage"/> class.
    /// </summary>
    public SchedulePage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLocalizedStrings();
        _ = LoadSchedulesAsync();
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ReleaseLiveCards();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads localized UI strings used for dialogs and the loading label.
    /// Card-internal tooltips are loaded by the card itself.
    /// </summary>
    private void LoadLocalizedStrings()
    {
        try
        {
            _deleteTooltip = Res.GetString("Schedule_DeleteTooltip") is { Length: > 0 } s1 ? s1 : _deleteTooltip;
            _loadingText = Res.GetString("Schedule_Loading") is { Length: > 0 } s2 ? s2 : _loadingText;
            _deleteConfirmTitle = Res.GetString("Schedule_DeleteConfirmTitle") is { Length: > 0 } s3 ? s3 : _deleteConfirmTitle;
            _deleteConfirmContent = Res.GetString("Schedule_DeleteConfirmContent") is { Length: > 0 } s4 ? s4 : _deleteConfirmContent;
            _cancelText = Res.GetString("Common_Cancel") is { Length: > 0 } s5 ? s5 : _cancelText;
            _errorTitle = Res.GetString("Common_Error") is { Length: > 0 } s6 ? s6 : _errorTitle;
            _okText = Res.GetString("Common_OK") is { Length: > 0 } s7 ? s7 : _okText;
        }
        catch { /* fallback defaults */ }

        LoadingText.Text = _loadingText;
    }

    /// <summary>
    /// Loads all scheduled tasks from the Runner database and rebuilds the card list.
    /// </summary>
    private async Task LoadSchedulesAsync()
    {
        ShowLoading();

        _allItems = await ScheduleHelper.ListAsync();

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (_allItems.Count == 0)
        {
            ShowEmpty();
            return;
        }

        RenderList(_allItems);
    }

    /// <summary>
    /// Builds and displays the schedule card list. Each item becomes a
    /// <see cref="ScheduleCard"/>; dividers are interleaved between them.
    /// </summary>
    private void RenderList(List<ScheduleItem> items)
    {
        if (items.Count == 0)
        {
            ShowEmpty(isSearching: _allItems.Count > 0);
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
        HintDivider.Visibility = Visibility.Visible;
        CounterText.Text = $"{items.Count}";

        ReleaseLiveCards();

        var elements = new List<FrameworkElement>();
        var isFirst = true;

        foreach (var item in items)
        {
            if (!isFirst)
            {
                elements.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                });
            }
            isFirst = false;

            var card = new ScheduleCard { Item = item };
            card.DeleteRequested += OnCardDeleteRequested;
            card.ToggleRequested += OnCardToggleRequested;
            _liveCards.Add(card);
            elements.Add(card);
        }

        ScheduleList.ItemsSource = elements;
    }

    /// <summary>
    /// Unsubscribes events from every live card and clears the tracking
    /// list so instances can be garbage-collected.
    /// </summary>
    private void ReleaseLiveCards()
    {
        foreach (var c in _liveCards)
        {
            c.DeleteRequested -= OnCardDeleteRequested;
            c.ToggleRequested -= OnCardToggleRequested;
        }
        _liveCards.Clear();
    }

    /// <summary>
    /// Filters the schedule list by name or description. Empty query
    /// restores the full list.
    /// </summary>
    private void FilterItems(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RenderList(_allItems);
            return;
        }

        var filtered = _allItems
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (i.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        RenderList(filtered);
    }

    /// <summary>
    /// Switches the UI to the "loading" state.
    /// </summary>
    private void ShowLoading()
    {
        ScheduleList.ItemsSource = null;
        ReleaseLiveCards();
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    /// <summary>
    /// Switches the UI to the "empty" state.
    /// </summary>
    private void ShowEmpty(bool isSearching = false)
    {
        ScheduleList.ItemsSource = null;
        ReleaseLiveCards();
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = isSearching ? Visibility.Collapsed : Visibility.Visible;
        NoResultsText.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles search query submission to filter schedules.
    /// </summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        FilterItems(args.QueryText);
    }

    /// <summary>
    /// Resets the list when the search box is cleared.
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(sender.Text))
        {
            RenderList(_allItems);
        }
    }

    /// <summary>
    /// Reloads the schedule list from the database.
    /// </summary>
    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        await LoadSchedulesAsync();
    }

    /// <summary>
    /// Handles a delete request from a <see cref="ScheduleCard"/>. Shows
    /// confirmation, calls <see cref="ScheduleHelper.DeleteAsync"/>, and
    /// reloads the list so the UI reflects the new state.
    /// </summary>
    private async void OnCardDeleteRequested(object? sender, string taskId)
    {
        var item = _allItems.FirstOrDefault(i => i.Id == taskId);
        if (item is null) return;

        var dialog = new ThemedContentDialog
        {
            Title = _deleteConfirmTitle,
            Content = string.Format(_deleteConfirmContent, item.Name),
            PrimaryButtonText = _deleteTooltip,
            CloseButtonText = _cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var (success, error) = await ScheduleHelper.DeleteAsync(taskId);

        if (!success && !string.IsNullOrWhiteSpace(error))
        {
            var errorDialog = new ThemedContentDialog
            {
                Title = _errorTitle,
                Content = error,
                CloseButtonText = _okText,
                XamlRoot = XamlRoot,
            };
            await errorDialog.ShowAsync();
        }

        await LoadSchedulesAsync();
    }

    /// <summary>
    /// Handles a toggle request from a <see cref="ScheduleCard"/>. Calls
    /// <see cref="ScheduleHelper.SetEnabledAsync"/>; on failure reverts
    /// the card's visual state and surfaces the error.
    /// </summary>
    private async void OnCardToggleRequested(object? sender, (string TaskId, bool IsOn) args)
    {
        if (_isUpdating) return;
        if (sender is not ScheduleCard card) return;

        _isUpdating = true;
        card.SetToggleBusy(true);

        try
        {
            var (success, error) = await ScheduleHelper.SetEnabledAsync(args.TaskId, args.IsOn);

            if (!success)
            {
                // Revert to the previous visual state so the card matches
                // the database truth instead of the user's failed intent.
                card.RevertToggle(!args.IsOn);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    var errorDialog = new ThemedContentDialog
                    {
                        Title = _errorTitle,
                        Content = error,
                        CloseButtonText = _okText,
                        XamlRoot = XamlRoot,
                    };
                    await errorDialog.ShowAsync();
                }
            }

            // Reload to reflect the authoritative database state. This
            // rebuilds cards — the old card instance is discarded.
            await LoadSchedulesAsync();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    #endregion
}
