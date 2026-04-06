using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing knowledge bases (create, delete, re-index, monitor).
/// Flat list style unified with Memory and Schedule pages.
/// </summary>
public sealed partial class KnowledgeBasesPage : Page
{
    #region Fields

    private readonly DispatcherTimer _pollTimer;
    private readonly List<KbViewModel> _allItems = [];
    private readonly ResourceLoader _loader = new();
    private string _searchQuery = "";

    #endregion

    #region Constructor

    public KnowledgeBasesPage()
    {
        InitializeComponent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += OnPollTick;

        SearchBox.PlaceholderText = _loader.GetString("KB_SearchPlaceholder");
    }

    #endregion

    #region Navigation

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshList();
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pollTimer.Stop();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Opens the KB creation dialog.
    /// </summary>
    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _loader.GetString("KB_CreateDialogTitle"),
            PrimaryButtonText = _loader.GetString("KB_Create/Content"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
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

    /// <summary>
    /// Deletes a knowledge base after confirmation.
    /// </summary>
    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _loader.GetString("KB_DeleteDialogTitle"),
            Content = _loader.GetString("KB_DeleteDialogMessage"),
            PrimaryButtonText = _loader.GetString("KB_Delete/Content"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // Cancel exec if running
        if (RagProcessManager.IsExecRunning(kbId))
        {
            RagProcessManager.CancelExec(kbId);
            await Task.Delay(2000);
        }

        KnowledgeBaseStore.Delete(kbId);
        LoggingService.LogInfo($"[KB] Deleted: {kbId}");

        RefreshList();
    }

    /// <summary>
    /// Re-indexes a knowledge base.
    /// </summary>
    private void OnReindexClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        RagProcessManager.StartExec(kbId, force: true);
        LoggingService.LogInfo($"[KB] Re-index started: {kbId}");

        RefreshList();
    }

    /// <summary>
    /// Handles search query submission.
    /// </summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _searchQuery = args.QueryText?.Trim() ?? "";
        ApplyFilter();
    }

    /// <summary>
    /// Handles search text changes for live filtering.
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _searchQuery = sender.Text?.Trim() ?? "";
            ApplyFilter();
        }
    }

    /// <summary>
    /// Polls indexing status and updates the list.
    /// </summary>
    private void OnPollTick(object? sender, object e)
    {
        var anyIndexing = false;

        foreach (var item in _allItems)
        {
            var status = KnowledgeBaseStore.GetIndexingStatus(item.Id);
            if (status is not null)
            {
                anyIndexing = true;
                item.IsIndexing = Visibility.Visible;
                item.Progress = status.Total > 0
                    ? (double)status.Current / status.Total * 100
                    : 0;
                var indexingLabel = _loader.GetString("KB_StatusIndexing") ?? "Indexing";
                item.StatusText = $"{indexingLabel} ({status.Current}/{status.Total})";
                item.StatusBrush = new SolidColorBrush(Colors.DodgerBlue);
            }
            else
            {
                item.IsIndexing = Visibility.Collapsed;
                item.Progress = 0;

                var stats = KnowledgeBaseStore.GetStats(item.Id);
                if (stats is not null)
                {
                    item.StatusText = _loader.GetString("KB_StatusReady") ?? "Ready";
                    item.StatusBrush = new SolidColorBrush(Colors.Gray);
                    item.StatsText = $"{stats.TotalFiles} files, {stats.TotalChunks} chunks";
                }
                else
                {
                    item.StatusText = _loader.GetString("KB_StatusNoIndex") ?? "No index";
                    item.StatusBrush = new SolidColorBrush(Colors.Gray);
                    item.StatsText = "";
                }
            }
        }

        ApplyFilter();

        if (!anyIndexing)
            _pollTimer.Stop();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Refreshes the full KB list from disk.
    /// </summary>
    private void RefreshList()
    {
        var kbs = KnowledgeBaseStore.ListAll();
        _allItems.Clear();

        foreach (var kb in kbs)
        {
            var status = KnowledgeBaseStore.GetIndexingStatus(kb.Id);
            var stats = KnowledgeBaseStore.GetStats(kb.Id);

            // Model info line: "embedding-model · contextualizer-model" or just "embedding-model"
            var modelInfo = kb.Embedding.Model;
            if (!string.IsNullOrEmpty(kb.Contextualizer.Model))
                modelInfo += $" \u00b7 {kb.Contextualizer.Model}";

            _allItems.Add(new KbViewModel
            {
                Id = kb.Id,
                Name = kb.Name,
                SourcePathsText = string.Join(", ", kb.SourcePaths),
                ModelInfoText = modelInfo,
                IsIndexing = status is not null ? Visibility.Visible : Visibility.Collapsed,
                Progress = status is not null && status.Total > 0
                    ? (double)status.Current / status.Total * 100 : 0,
                StatusText = status is not null
                    ? $"{_loader.GetString("KB_StatusIndexing") ?? "Indexing"} ({status.Current}/{status.Total})"
                    : stats is not null
                        ? _loader.GetString("KB_StatusReady") ?? "Ready"
                        : _loader.GetString("KB_StatusNoIndex") ?? "No index",
                StatusBrush = status is not null
                    ? new SolidColorBrush(Colors.DodgerBlue)
                    : new SolidColorBrush(Colors.Gray),
                StatsText = stats is not null
                    ? $"{stats.TotalFiles} files, {stats.TotalChunks} chunks"
                    : "",
            });
        }

        // Start polling if any KB is indexing
        if (_allItems.Any(i => i.IsIndexing == Visibility.Visible))
            _pollTimer.Start();
        else
            _pollTimer.Stop();

        ApplyFilter();
    }

    /// <summary>
    /// Applies the current search filter and updates the UI.
    /// </summary>
    private void ApplyFilter()
    {
        var filtered = string.IsNullOrEmpty(_searchQuery)
            ? _allItems
            : _allItems.Where(i =>
                i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)
                || i.SourcePathsText.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
              .ToList();

        // Update counter
        CounterText.Text = _allItems.Count.ToString();

        // Build flat list UI
        KbList.ItemsSource = null;
        var panel = new StackPanel { Spacing = 0 };

        for (int idx = 0; idx < filtered.Count; idx++)
        {
            var item = filtered[idx];

            // Divider between items
            if (idx > 0)
            {
                panel.Children.Add(new Rectangle
                {
                    Height = 1,
                    Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }

            var row = new StackPanel { Spacing = 2, Padding = new Thickness(0, 4, 0, 4) };

            // Row 1: Name + Status
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerRow.Children.Add(new TextBlock
            {
                Text = item.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });

            var statusText = new TextBlock
            {
                Text = item.StatusText,
                Foreground = item.StatusBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(statusText, 1);
            headerRow.Children.Add(statusText);
            row.Children.Add(headerRow);

            // Row 2: Stats
            if (!string.IsNullOrEmpty(item.StatsText))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.StatsText,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.6,
                });
            }

            // Row 3: Source paths
            row.Children.Add(new TextBlock
            {
                Text = item.SourcePathsText,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            // Row 4: Model info
            if (!string.IsNullOrEmpty(item.ModelInfoText))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.ModelInfoText,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.45,
                    FontSize = 11,
                });
            }

            // Row 5: Progress bar (indexing) or action buttons
            if (item.IsIndexing == Visibility.Visible)
            {
                row.Children.Add(new ProgressBar
                {
                    Value = item.Progress,
                    Maximum = 100,
                    Margin = new Thickness(0, 6, 0, 0),
                });
            }
            else
            {
                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };

                var reindexBtn = new Button
                {
                    Tag = item.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Padding = new Thickness(6),
                    Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 },
                };
                ToolTipService.SetToolTip(reindexBtn, new ToolTip
                {
                    Content = _loader.GetString("KB_Reindex/Content") ?? "Re-index",
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
                });
                reindexBtn.Click += OnReindexClicked;
                buttons.Children.Add(reindexBtn);

                var deleteBtn = new Button
                {
                    Tag = item.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Padding = new Thickness(6),
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                };
                ToolTipService.SetToolTip(deleteBtn, new ToolTip
                {
                    Content = _loader.GetString("KB_Delete/Content") ?? "Delete",
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
                });
                deleteBtn.Click += OnDeleteClicked;
                buttons.Children.Add(deleteBtn);

                row.Children.Add(buttons);
            }

            panel.Children.Add(row);
        }

        KbList.ItemsSource = new[] { panel };

        // Show/hide states
        var hasItems = filtered.Count > 0;
        EmptyPanel.Visibility = _allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HintDivider.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Resolves embedding config from create dialog selection index.
    /// </summary>
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

    /// <summary>
    /// Resolves contextualizer config from create dialog selection index.
    /// </summary>
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
        _ => new KbProviderConfig(),
    };

    #endregion
}

/// <summary>
/// View model for a knowledge base item in the list.
/// </summary>
internal class KbViewModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePathsText { get; set; } = "";
    public string ModelInfoText { get; set; } = "";
    public string StatusText { get; set; } = "";
    public Brush StatusBrush { get; set; } = new SolidColorBrush(Colors.Gray);
    public string StatsText { get; set; } = "";
    public Visibility IsIndexing { get; set; } = Visibility.Collapsed;
    public double Progress { get; set; }
}
