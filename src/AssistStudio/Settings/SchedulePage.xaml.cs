using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing scheduled tasks created by the Runner.
/// The page owns list orchestration, filtering, and empty/loading states while
/// each <see cref="Controls.ScheduleCard"/> owns per-item behavior.
/// </summary>
public sealed partial class SchedulePage : Page
{
    private static readonly ResourceLoader Res = new();

    private readonly ObservableCollection<ScheduleItemViewModel> _items = [];
    private List<ScheduleItemViewModel> _allItems = [];

    private string _loadingText = "Loading schedules...";

    public SchedulePage()
    {
        InitializeComponent();
        ScheduleList.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLocalizedStrings();
        _ = LoadSchedulesAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ClearItems();
    }

    private void LoadLocalizedStrings()
    {
        try
        {
            _loadingText = Res.GetString("Schedule_Loading") is { Length: > 0 } s ? s : _loadingText;
        }
        catch { }

        LoadingText.Text = _loadingText;
    }

    private async Task LoadSchedulesAsync()
    {
        ShowLoading();

        _allItems = (await ScheduleHelper.ListAsync())
            .Select(item => new ScheduleItemViewModel(item))
            .ToList();

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (_allItems.Count == 0)
        {
            ShowEmpty();
            return;
        }

        RenderList(_allItems);
    }

    private void RenderList(List<ScheduleItemViewModel> items)
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
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = $"{items.Count}";

        ClearItems();
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
            _items.Add(item);
        }
    }

    private void ClearItems()
    {
        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _items.Clear();
    }

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

    private void ShowLoading()
    {
        ClearItems();
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    private void ShowEmpty(bool isSearching = false)
    {
        ClearItems();
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = isSearching ? Visibility.Collapsed : Visibility.Visible;
        NoResultsText.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        FilterItems(args.QueryText);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(sender.Text))
        {
            RenderList(_allItems);
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        await LoadSchedulesAsync();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScheduleItemViewModel.IsDeleted))
            return;

        if (sender is not ScheduleItemViewModel item || !item.IsDeleted)
            return;

        item.PropertyChanged -= OnItemPropertyChanged;
        _allItems.RemoveAll(x => x.Id == item.Id);
        _items.Remove(item);
        CounterText.Text = $"{_items.Count}";

        if (_items.Count == 0)
            ShowEmpty(isSearching: !string.IsNullOrWhiteSpace(SearchBox.Text));
    }
}
