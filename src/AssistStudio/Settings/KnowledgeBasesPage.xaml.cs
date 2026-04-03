using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing knowledge bases (create, delete, re-index, monitor).
/// </summary>
public sealed partial class KnowledgeBasesPage : Page
{
    #region Fields

    private readonly DispatcherTimer _pollTimer;
    private readonly List<KbViewModel> _items = [];

    #endregion

    #region Constructor

    public KnowledgeBasesPage()
    {
        InitializeComponent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += OnPollTick;
    }

    #endregion

    #region Navigation

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshList();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pollTimer.Stop();
    }

    #endregion

    #region Event Handlers

    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create Knowledge Base",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 400 };

        var nameBox = new TextBox { Header = "Name", PlaceholderText = "e.g., Project Docs" };
        panel.Children.Add(nameBox);

        var folderPanel = new StackPanel { Spacing = 4 };
        var folderHeader = new TextBlock { Text = "Source Folders", Opacity = 0.8 };
        folderPanel.Children.Add(folderHeader);

        var folderList = new StackPanel { Spacing = 4 };
        folderPanel.Children.Add(folderList);

        var addFolderButton = new Button { Content = "Add Folder" };
        addFolderButton.Click += async (s, args) =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var window = (App.Current as App)?.MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                var item = new TextBlock
                {
                    Text = folder.Path,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                folderList.Children.Add(item);
            }
        };
        folderPanel.Children.Add(addFolderButton);
        panel.Children.Add(folderPanel);

        // Embedding model selection
        var embeddingHeader = new TextBlock { Text = "Embedding Model", Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(embeddingHeader);

        var embeddingRadio = new RadioButtons();
        embeddingRadio.Items.Add("Ollama — nomic-embed-text");
        embeddingRadio.Items.Add("OpenAI — text-embedding-3-small");
        embeddingRadio.SelectedIndex = 0;
        panel.Children.Add(embeddingRadio);

        // Contextualizer selection
        var ctxHeader = new TextBlock { Text = "Contextualizer", Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(ctxHeader);

        var ctxRadio = new RadioButtons();
        ctxRadio.Items.Add("None");
        ctxRadio.Items.Add("Anthropic — claude-haiku-4-5-20251001");
        ctxRadio.Items.Add("OpenAI — gpt-4o-mini");
        ctxRadio.Items.Add("Ollama — gemma3:4b");
        ctxRadio.SelectedIndex = 0;
        panel.Children.Add(ctxRadio);

        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            return;

        var sourcePaths = folderList.Children
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ToList();

        if (sourcePaths.Count == 0)
            return;

        var embedding = ResolveEmbeddingConfig(embeddingRadio.SelectedIndex);
        var contextualizer = ResolveContextualizerConfig(ctxRadio.SelectedIndex);

        var kb = KnowledgeBaseStore.Create(name, sourcePaths, embedding, contextualizer);
        LoggingService.LogInfo($"[KB] Created: {kb.Name} ({kb.Id})");

        // Start indexing
        RagProcessManager.StartExec(kb.Id);

        // Ensure shared serve is running (first KB triggers dynamic start)
        var archiveService = new KnowledgeArchiveService(App.McpRegistry);
        await archiveService.EnsureConnectedAsync();

        RefreshList();
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete Knowledge Base",
            Content = "This will permanently delete the knowledge base and its index. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // Cancel exec if running
        if (RagProcessManager.IsExecRunning(kbId))
        {
            RagProcessManager.CancelExec(kbId);
            await Task.Delay(2000); // Give exec time to exit
        }

        KnowledgeBaseStore.Delete(kbId);
        LoggingService.LogInfo($"[KB] Deleted: {kbId}");

        RefreshList();
    }

    private void OnReindexClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        RagProcessManager.StartExec(kbId, force: true);
        LoggingService.LogInfo($"[KB] Re-index started: {kbId}");

        RefreshList();
    }

    private void OnPollTick(object? sender, object e)
    {
        var anyIndexing = false;

        foreach (var item in _items)
        {
            var status = KnowledgeBaseStore.GetIndexingStatus(item.Id);
            if (status is not null)
            {
                anyIndexing = true;
                item.IsIndexing = Visibility.Visible;
                item.Progress = status.Total > 0
                    ? (double)status.Current / status.Total * 100
                    : 0;
                item.StatusText = $"Indexing ({status.Current}/{status.Total})";
                item.StatusBrush = new SolidColorBrush(Colors.DodgerBlue);
            }
            else
            {
                item.IsIndexing = Visibility.Collapsed;
                item.Progress = 0;

                var stats = KnowledgeBaseStore.GetStats(item.Id);
                if (stats is not null)
                {
                    item.StatusText = "Ready";
                    item.StatusBrush = new SolidColorBrush(Colors.ForestGreen);
                    item.StatsText = $"{stats.TotalFiles} files, {stats.TotalChunks} chunks";
                }
                else
                {
                    item.StatusText = "No index";
                    item.StatusBrush = new SolidColorBrush(Colors.Gray);
                    item.StatsText = "";
                }
            }
        }

        KbList.ItemsSource = null;
        KbList.ItemsSource = _items;

        if (!anyIndexing)
            _pollTimer.Stop();
    }

    #endregion

    #region Private Methods

    private void RefreshList()
    {
        var kbs = KnowledgeBaseStore.ListAll();
        _items.Clear();

        foreach (var kb in kbs)
        {
            var status = KnowledgeBaseStore.GetIndexingStatus(kb.Id);
            var stats = KnowledgeBaseStore.GetStats(kb.Id);

            _items.Add(new KbViewModel
            {
                Id = kb.Id,
                Name = kb.Name,
                SourcePathsText = string.Join(", ", kb.SourcePaths),
                IsIndexing = status is not null ? Visibility.Visible : Visibility.Collapsed,
                Progress = status is not null && status.Total > 0
                    ? (double)status.Current / status.Total * 100 : 0,
                StatusText = status is not null
                    ? $"Indexing ({status.Current}/{status.Total})"
                    : stats is not null ? "Ready" : "No index",
                StatusBrush = status is not null
                    ? new SolidColorBrush(Colors.DodgerBlue)
                    : stats is not null
                        ? new SolidColorBrush(Colors.ForestGreen)
                        : new SolidColorBrush(Colors.Gray),
                StatsText = stats is not null
                    ? $"{stats.TotalFiles} files, {stats.TotalChunks} chunks"
                    : "",
            });
        }

        KbList.ItemsSource = _items;
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Start polling if any KB is indexing
        if (_items.Any(i => i.IsIndexing == Visibility.Visible))
            _pollTimer.Start();
        else
            _pollTimer.Stop();
    }

    private static KbProviderConfig ResolveEmbeddingConfig(int index) => index switch
    {
        1 => new KbProviderConfig
        {
            Provider = "openai",
            Model = "text-embedding-3-small",
            ApiKeyPreset = "OpenAI",
        },
        _ => new KbProviderConfig
        {
            Provider = "ollama",
            Model = "nomic-embed-text",
        },
    };

    private static KbProviderConfig ResolveContextualizerConfig(int index) => index switch
    {
        1 => new KbProviderConfig
        {
            Provider = "anthropic",
            Model = "claude-haiku-4-5-20251001",
            ApiKeyPreset = "Claude",
        },
        2 => new KbProviderConfig
        {
            Provider = "openai",
            Model = "gpt-4o-mini",
            ApiKeyPreset = "OpenAI",
        },
        3 => new KbProviderConfig
        {
            Provider = "ollama",
            Model = "gemma3:4b",
        },
        _ => new KbProviderConfig(), // None — empty model = NullChunkContextualizer
    };

    #endregion
}

/// <summary>
/// View model for a knowledge base card in the list.
/// </summary>
internal class KbViewModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePathsText { get; set; } = "";
    public string StatusText { get; set; } = "";
    public Brush StatusBrush { get; set; } = new SolidColorBrush(Colors.Gray);
    public string StatsText { get; set; } = "";
    public Visibility IsIndexing { get; set; } = Visibility.Collapsed;
    public double Progress { get; set; }
}
