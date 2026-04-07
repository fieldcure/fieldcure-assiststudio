using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing scheduled tasks created by the Runner.
/// Reads from the Runner's SQLite database and synchronizes changes with Windows Task Scheduler.
/// </summary>
public sealed partial class SchedulePage : Page
{
    #region Fields

    private List<ScheduleItem> _allItems = [];
    private bool _isUpdating;

    private string _deleteTooltip = "Delete";
    private string _enableTooltip = "Enable";
    private string _disableTooltip = "Disable";
    private string _searchPlaceholder = "Search schedules...";
    private string _loadingText = "Loading schedules...";
    private string _deleteConfirmTitle = "Delete Schedule";
    private string _deleteConfirmContent = "Are you sure you want to delete \"{0}\"?";
    private string _cancelText = "Cancel";
    private string _errorTitle = "Error";
    private string _okText = "OK";

    #endregion

    #region Constructors

    public SchedulePage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLocalizedStrings();
        _ = LoadSchedulesAsync();
    }

    #endregion

    #region Private Methods

    private void LoadLocalizedStrings()
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            _deleteTooltip = loader.GetString("Schedule_DeleteTooltip") is { Length: > 0 } s1 ? s1 : _deleteTooltip;
            _enableTooltip = loader.GetString("Schedule_EnableTooltip") is { Length: > 0 } s2 ? s2 : _enableTooltip;
            _disableTooltip = loader.GetString("Schedule_DisableTooltip") is { Length: > 0 } s3 ? s3 : _disableTooltip;
            _searchPlaceholder = loader.GetString("Schedule_SearchPlaceholder") is { Length: > 0 } s4 ? s4 : _searchPlaceholder;
            _loadingText = loader.GetString("Schedule_Loading") is { Length: > 0 } s5 ? s5 : _loadingText;
            _deleteConfirmTitle = loader.GetString("Schedule_DeleteConfirmTitle") is { Length: > 0 } s6 ? s6 : _deleteConfirmTitle;
            _deleteConfirmContent = loader.GetString("Schedule_DeleteConfirmContent") is { Length: > 0 } s7 ? s7 : _deleteConfirmContent;
            _cancelText = loader.GetString("Common_Cancel") is { Length: > 0 } s8 ? s8 : _cancelText;
            _errorTitle = loader.GetString("Common_Error") is { Length: > 0 } s9 ? s9 : _errorTitle;
            _okText = loader.GetString("Common_OK") is { Length: > 0 } s10 ? s10 : _okText;
        }
        catch { /* fallback defaults */ }

        LoadingText.Text = _loadingText;
        SearchBox.PlaceholderText = _searchPlaceholder;
    }

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

    private void RenderList(List<ScheduleItem> items)
    {
        if (items.Count == 0)
        {
            ShowEmpty();
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
        HintDivider.Visibility = Visibility.Visible;
        CounterText.Text = $"{items.Count}";

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

            var grid = new Grid
            {
                Padding = new Thickness(0, 10, 0, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            // Column 0: task info
            var infoPanel = new StackPanel { Spacing = 4 };

            infoPanel.Children.Add(new TextBlock
            {
                Text = item.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.7,
                });
            }

            // Schedule (human-readable) + output channel info line
            var detailParts = new List<string>();
            if (item.ScheduleOnce.HasValue)
            {
                var local = item.ScheduleOnce.Value.ToLocalTime();
                var suffix = local <= DateTimeOffset.Now ? " (\uc644\ub8cc)" : " (1\ud68c)";
                detailParts.Add($"{local:yyyy-MM-dd HH:mm}{suffix}");
            }
            else if (!string.IsNullOrWhiteSpace(item.Schedule))
                detailParts.Add(ScheduleHelper.DescribeCron(item.Schedule));
            if (!string.IsNullOrWhiteSpace(item.OutputChannel))
                detailParts.Add($"\u2192 {item.OutputChannel}");

            if (detailParts.Count > 0)
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = string.Join(" \u00B7 ", detailParts),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.5,
                });
            }

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // Column 1: toggle switch
            var toggle = new ToggleSwitch
            {
                IsOn = item.IsEnabled,
                OnContent = "",
                OffContent = "",
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = item.Id,
                Margin = new Thickness(8, 0, 0, 0),
            };
            ToolTipService.SetToolTip(toggle, item.IsEnabled ? _disableTooltip : _enableTooltip);
            ToolTipService.SetPlacement(toggle, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
            toggle.Toggled += OnToggled;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            // Column 2: delete button (visible on hover)
            var deleteButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                Padding = new Thickness(6),
                MinWidth = 0,
                MinHeight = 0,
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Opacity = 0,
                Margin = new Thickness(4, 0, 0, 0),
            };
            ToolTipService.SetToolTip(deleteButton, _deleteTooltip);
            ToolTipService.SetPlacement(deleteButton, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
            deleteButton.Click += OnDeleteClicked;
            Grid.SetColumn(deleteButton, 2);
            grid.Children.Add(deleteButton);

            grid.PointerEntered += (_, _) => deleteButton.Opacity = 1;
            grid.PointerExited += (_, _) => deleteButton.Opacity = 0;

            elements.Add(grid);
        }

        ScheduleList.ItemsSource = elements;
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
        ScheduleList.ItemsSource = null;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    private void ShowEmpty()
    {
        ScheduleList.ItemsSource = null;
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    #endregion

    #region Event Handlers

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

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ScheduleItem item)
            return;

        var dialog = new ContentDialog
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

        var (success, error) = await ScheduleHelper.DeleteAsync(item.Id);

        if (!success && !string.IsNullOrWhiteSpace(error))
        {
            var errorDialog = new ContentDialog
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

    private async void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        if (sender is not ToggleSwitch toggle || toggle.Tag is not string taskId)
            return;

        _isUpdating = true;
        toggle.IsEnabled = false;

        try
        {
            var (success, error) = await ScheduleHelper.SetEnabledAsync(taskId, toggle.IsOn);

            if (!success)
            {
                // Revert toggle on failure
                toggle.IsOn = !toggle.IsOn;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = error,
                        CloseButtonText = "OK",
                        XamlRoot = XamlRoot,
                    };
                    await errorDialog.ShowAsync();
                }
            }

            // Update tooltip
            ToolTipService.SetToolTip(toggle, toggle.IsOn ? _disableTooltip : _enableTooltip);

            // Reload to reflect actual state
            await LoadSchedulesAsync();
        }
        finally
        {
            toggle.IsEnabled = true;
            _isUpdating = false;
        }
    }

    #endregion
}
