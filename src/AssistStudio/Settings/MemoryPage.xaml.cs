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
    public MemoryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = RefreshListAsync();
    }

    private string _deleteTooltip = "Delete";

    private async Task RefreshListAsync()
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            _deleteTooltip = loader.GetString("Memory_DeleteTooltip") is { Length: > 0 } s ? s : "Delete";
        }
        catch { /* fallback */ }

        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null || !conn.IsConnected)
        {
            ShowEmpty();
            return;
        }

        try
        {
            var args = JsonDocument.Parse("{\"limit\": 100}").RootElement;
            var resultJson = await conn.CallToolWithProgressAsync("list_memories", args, null, CancellationToken.None);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("memories", out var memories) || memories.GetArrayLength() == 0)
            {
                ShowEmpty();
                return;
            }

            var total = root.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : memories.GetArrayLength();

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

                var textPanel = new StackPanel { Spacing = 2 };
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
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
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

    private void ShowEmpty()
    {
        MemoryList.ItemsSource = null;
        EmptyPanel.Visibility = Visibility.Visible;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        ClearAllButton.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
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
            await RefreshListAsync();
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

        // Delete all by fetching all keys and deleting each
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

            await RefreshListAsync();
        }
        catch { /* best effort */ }
    }
}
