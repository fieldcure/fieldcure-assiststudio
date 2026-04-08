using System.Text.Json;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing persistent memory entries.
/// Reads from Essentials MCP server via list_memories/forget tools.
/// </summary>
public sealed partial class MemoryPage : Page
{
    private string _deleteTooltip = "Delete";
    private string _connectingText = "Connecting to Essentials server...";

    public MemoryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLocalizedStrings();
        _ = LoadMemoriesAsync();
    }

    private void LoadLocalizedStrings()
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            _deleteTooltip = loader.GetString("Memory_DeleteTooltip") is { Length: > 0 } s ? s : "Delete";
            _connectingText = loader.GetString("Memory_Connecting") is { Length: > 0 } s2 ? s2 : _connectingText;
        }
        catch { /* fallback defaults */ }

        ConnectingText.Text = _connectingText;
    }

    private async Task LoadMemoriesAsync(string? query = null)
    {
        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);

        // Not connected — show connecting state and wait
        if (conn is null || !conn.IsConnected)
        {
            ShowConnecting();
            conn = await WaitForEssentialsAsync();
            if (conn is null)
            {
                ShowEmpty();
                return;
            }
        }

        await RefreshListAsync(conn, query);
    }

    private async Task<McpServerConnection?> WaitForEssentialsAsync()
    {
        // Poll for connection (max 10 seconds)
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
            if (conn?.IsConnected == true)
            {
                ConnectingPanel.Visibility = Visibility.Collapsed;
                return conn;
            }
        }
        ConnectingPanel.Visibility = Visibility.Collapsed;
        return null;
    }

    private async Task RefreshListAsync(McpServerConnection conn, string? query = null)
    {
        try
        {
            var argsObj = string.IsNullOrWhiteSpace(query)
                ? new { limit = 100 }
                : (object)new { query, limit = 100 };
            var args = JsonDocument.Parse(JsonSerializer.Serialize(argsObj)).RootElement;
            var resultJson = await conn.CallToolWithProgressAsync("list_memories", args, null, CancellationToken.None);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("memories", out var memories) || memories.GetArrayLength() == 0)
            {
                ShowEmpty();
                return;
            }

            var total = root.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : memories.GetArrayLength();

            ConnectingPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Visible;
            HintDivider.Visibility = Visibility.Visible;
            ClearAllButton.Visibility = Visibility.Visible;
            CounterText.Text = $"{total}";

            var items = new List<FrameworkElement>();
            var isFirst = true;
            foreach (var entry in memories.EnumerateArray())
            {
                if (!isFirst)
                {
                    items.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Height = 1,
                        Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    });
                }
                isFirst = false;

                var key = entry.GetProperty("key").GetString() ?? "";
                var value = entry.GetProperty("value").GetString() ?? "";

                var grid = new Grid
                {
                    Padding = new Thickness(0, 10, 0, 10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto },
                    },
                };

                var textPanel = new StackPanel { Spacing = 6 };
                textPanel.Children.Add(new TextBlock
                {
                    Text = key,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.6,
                });
                textPanel.Children.Add(new TextBlock
                {
                    Text = value,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                });
                Grid.SetColumn(textPanel, 0);
                grid.Children.Add(textPanel);

                var deleteButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                    Padding = new Thickness(6),
                    MinWidth = 0,
                    MinHeight = 0,
                    Tag = key,
                    VerticalAlignment = VerticalAlignment.Top,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Opacity = 0,
                };
                ToolTipService.SetToolTip(deleteButton, _deleteTooltip);
                ToolTipService.SetPlacement(deleteButton, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
                deleteButton.Click += OnDeleteClicked;
                Grid.SetColumn(deleteButton, 1);
                grid.Children.Add(deleteButton);

                grid.PointerEntered += (_, _) => deleteButton.Opacity = 1;
                grid.PointerExited += (_, _) => deleteButton.Opacity = 0;

                items.Add(grid);
            }

            MemoryList.ItemsSource = items;
        }
        catch
        {
            ShowEmpty();
        }
    }

    private void ShowConnecting()
    {
        MemoryList.ItemsSource = null;
        ConnectingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        ClearAllButton.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    private void ShowEmpty()
    {
        MemoryList.ItemsSource = null;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        ClearAllButton.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _ = LoadMemoriesAsync(args.QueryText);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(sender.Text))
        {
            _ = LoadMemoriesAsync();
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key)
            return;

        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null || !conn.IsConnected) return;

        try
        {
            var args = JsonDocument.Parse(JsonSerializer.Serialize(new { key })).RootElement;
            await conn.CallToolWithProgressAsync("forget", args, null, CancellationToken.None);
            await RefreshListAsync(conn, string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text);
        }
        catch { /* best effort */ }
    }

    private async void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        string title, content;
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            title = loader.GetString("Memory_ClearAll") is { Length: > 0 } t ? t : "Clear All";
            content = loader.GetString("Memory_ClearConfirm") is { Length: > 0 } c
                ? c : "Are you sure you want to clear all memories?";
        }
        catch
        {
            title = "Clear All";
            content = "Are you sure you want to clear all memories?";
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = title,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null || !conn.IsConnected) return;

        try
        {
            var listArgs = JsonDocument.Parse("{\"limit\": 100}").RootElement;
            var resultJson = await conn.CallToolWithProgressAsync("list_memories", listArgs, null, CancellationToken.None);

            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("memories", out var memories))
            {
                foreach (var entry in memories.EnumerateArray())
                {
                    var key = entry.GetProperty("key").GetString();
                    if (key is null) continue;
                    var args = JsonDocument.Parse(JsonSerializer.Serialize(new { key })).RootElement;
                    await conn.CallToolWithProgressAsync("forget", args, null, CancellationToken.None);
                }
            }

            await RefreshListAsync(conn);
        }
        catch { /* best effort */ }
    }
}
