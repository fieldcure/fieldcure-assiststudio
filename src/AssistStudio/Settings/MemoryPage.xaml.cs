using AssistStudio.Controls;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing persistent memory entries.
/// Reads from Essentials MCP server via list_memories/forget tools.
/// </summary>
public sealed partial class MemoryPage : Page
{
    private static readonly ResourceLoader Res = new();
    private readonly ObservableCollection<MemoryEntryItemViewModel> _items = [];

    private string _connectingText = "";

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPage"/> class.
    /// </summary>
    public MemoryPage()
    {
        InitializeComponent();
        MemoryList.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLocalizedStrings();
        _ = LoadMemoriesAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ClearItems();
    }

    /// <summary>
    /// Loads localized UI strings from the resource file.
    /// </summary>
    private void LoadLocalizedStrings()
    {
        _connectingText = Res.GetString("Memory_Connecting");
        ConnectingText.Text = _connectingText;
    }

    /// <summary>
    /// Loads memories from the Essentials MCP server, waiting for connection if needed.
    /// </summary>
    private async Task LoadMemoriesAsync(string? query = null)
    {
        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);

        // Not connected - show connecting state and wait
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

    /// <summary>
    /// Polls for the Essentials MCP server connection up to 10 seconds.
    /// </summary>
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

    /// <summary>
    /// Fetches memories from the given connection and rebuilds the item collection.
    /// </summary>
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
                ShowEmpty(isSearching: !string.IsNullOrWhiteSpace(query));
                return;
            }

            var total = root.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : memories.GetArrayLength();
            RenderItems(memories, total);
        }
        catch
        {
            ShowEmpty();
        }
    }

    /// <summary>
    /// Rebuilds the repeater item collection from the latest server response.
    /// </summary>
    private void RenderItems(JsonElement memories, int total)
    {
        if (memories.GetArrayLength() == 0)
        {
            ShowEmpty(isSearching: !string.IsNullOrWhiteSpace(SearchBox.Text));
            return;
        }

        ConnectingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = $"{total}";

        ClearItems();

        foreach (var entry in memories.EnumerateArray())
        {
            var item = new MemoryEntryItemViewModel(
                entry.GetProperty("key").GetString() ?? "",
                entry.GetProperty("value").GetString() ?? "");
            item.PropertyChanged += OnItemPropertyChanged;
            _items.Add(item);
        }
    }

    /// <summary>
    /// Unsubscribes from all item view models and clears the collection.
    /// </summary>
    private void ClearItems()
    {
        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _items.Clear();
    }

    /// <summary>
    /// Switches the UI to the "connecting" state.
    /// </summary>
    private void ShowConnecting()
    {
        ClearItems();
        ConnectingPanel.Visibility = Visibility.Visible;
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
        ClearItems();
        ConnectingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = isSearching ? Visibility.Collapsed : Visibility.Visible;
        NoResultsText.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        CounterText.Text = "";
    }

    /// <summary>
    /// Handles search query submission to filter memories.
    /// </summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _ = LoadMemoriesAsync(args.QueryText);
    }

    /// <summary>
    /// Resets the list when the search box is cleared.
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(sender.Text))
        {
            _ = LoadMemoriesAsync();
        }
    }

    /// <summary>
    /// Removes deleted items from the collection and updates page-level state.
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MemoryEntryItemViewModel.IsDeleted))
            return;

        if (sender is not MemoryEntryItemViewModel item || !item.IsDeleted)
            return;

        item.PropertyChanged -= OnItemPropertyChanged;
        _items.Remove(item);

        var remainingItems = _items.Count;
        CounterText.Text = $"{remainingItems}";

        if (remainingItems == 0)
            ShowEmpty(isSearching: !string.IsNullOrWhiteSpace(SearchBox.Text));
    }

    /// <summary>
    /// Re-runs <c>list_memories</c> against the Essentials server. The page does
    /// not auto-refresh while the user is on another page, so this button lets
    /// the user pick up memories the AI added or removed during a conversation.
    /// </summary>
    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await LoadMemoriesAsync(string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}


