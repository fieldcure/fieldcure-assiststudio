using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using IOPath = System.IO.Path;
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
    /// Folder selection comes first; name auto-generates from the first folder.
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
            IsPrimaryButtonEnabled = false,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 500 };
        var nameManuallyEdited = false;

        // --- Source Folders (first) ---
        var folderPanel = new StackPanel { Spacing = 4 };
        var folderHeader = new TextBlock { Text = _loader.GetString("KB_DialogSourceFolders"), Opacity = 0.8 };
        folderPanel.Children.Add(folderHeader);

        var folderList = new StackPanel { Spacing = 4 };
        folderPanel.Children.Add(folderList);

        // --- Name (auto-generated) ---
        var nameBox = new TextBox { Header = _loader.GetString("KB_DialogName"), PlaceholderText = "e.g., Project Docs" };
        var nameHint = new TextBlock
        {
            Text = _loader.GetString("KB_DialogNameHint"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5,
        };
        nameBox.TextChanged += (_, _) => { if (nameBox.FocusState != FocusState.Unfocused) nameManuallyEdited = true; };

        // Warning text for folder already used in another KB
        var folderWarning = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var addFolderButton = new Button { Content = _loader.GetString("KB_DialogAddFolder") };
        addFolderButton.Click += async (s, args) =>
        {
            var folder = await PickFolderAsync();
            if (folder is null) return;

            // Skip if already in the list
            var currentPaths = CollectFolderPaths(folderList);
            if (currentPaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            folderList.Children.Add(BuildFolderRow(folder.Path, folderList));
            dialog.IsPrimaryButtonEnabled = true;

            // Auto-fill name from first folder if not manually edited
            if (!nameManuallyEdited && folderList.Children.Count == 1)
                nameBox.Text = IOPath.GetFileName(folder.Path.TrimEnd(IOPath.DirectorySeparatorChar));

            // Warn if folder is used in another KB
            var existingKbs = KnowledgeBaseStore.ListAll();
            var otherKb = existingKbs.FirstOrDefault(k =>
                k.SourcePaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)));
            if (otherKb is not null)
            {
                folderWarning.Text = string.Format(_loader.GetString("KB_FolderUsedWarning"), otherKb.Name);
                folderWarning.Visibility = Visibility.Visible;
            }
        };
        folderPanel.Children.Add(addFolderButton);
        folderPanel.Children.Add(folderWarning);
        panel.Children.Add(folderPanel);

        panel.Children.Add(nameBox);
        panel.Children.Add(nameHint);

        // --- Model Selection ---
        var modelSelector = new Controls.EmbeddingModelSelector();
        modelSelector.Initialize();
        panel.Children.Add(modelSelector);

        dialog.Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 600,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var sourcePaths = CollectFolderPaths(folderList);
        if (sourcePaths.Count == 0)
            return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            name = GenerateKbName(sourcePaths);

        var kb = KnowledgeBaseStore.Create(name, sourcePaths,
            modelSelector.GetEmbeddingConfig(), modelSelector.GetContextualizerConfig());
        LoggingService.LogInfo($"[KB] Created: {kb.Name} ({kb.Id})");

        RagProcessManager.StartExec(kb.Id);

        var archiveService = new KnowledgeArchiveService(App.McpRegistry);
        await archiveService.EnsureConnectedAsync();

        RefreshList();
        _pollTimer.Start();
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
    /// Immediately starts incremental re-indexing (new/changed files only).
    /// </summary>
    private void OnQuickReindexClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        RagProcessManager.StartExec(kbId);
        LoggingService.LogInfo($"[KB] Quick re-index started: {kbId}");

        // Replace entire button panel with a spinner
        if (btn.Parent is StackPanel buttonsPanel)
        {
            buttonsPanel.Children.Clear();
            buttonsPanel.Children.Add(new ProgressRing { Width = 16, Height = 16, IsActive = true });
        }

        _pollTimer.Start();
    }

    /// <summary>
    /// Opens the KB settings dialog for editing folders, name, and models.
    /// Primary = Save &amp; Re-index, Secondary = Save only.
    /// </summary>
    private async void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        var allKbs = KnowledgeBaseStore.ListAll();
        var kb = allKbs.FirstOrDefault(k => k.Id == kbId);
        if (kb is null) return;

        var panel = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 500 };

        // --- Source Folders (editable) ---
        var folderPanel = new StackPanel { Spacing = 4 };
        var folderHeader = new TextBlock { Text = _loader.GetString("KB_DialogSourceFolders"), Opacity = 0.8 };
        folderPanel.Children.Add(folderHeader);

        var folderList = new StackPanel { Spacing = 4 };
        foreach (var path in kb.SourcePaths)
            folderList.Children.Add(BuildFolderRow(path, folderList));
        folderPanel.Children.Add(folderList);

        var folderWarning = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var addFolderButton = new Button { Content = _loader.GetString("KB_DialogAddFolder") };
        addFolderButton.Click += async (s, args) =>
        {
            var folder = await PickFolderAsync();
            if (folder is null) return;

            var currentPaths = CollectFolderPaths(folderList);
            if (currentPaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            folderList.Children.Add(BuildFolderRow(folder.Path, folderList));

            var otherKb = allKbs.FirstOrDefault(k => k.Id != kbId &&
                k.SourcePaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)));
            if (otherKb is not null)
            {
                folderWarning.Text = string.Format(_loader.GetString("KB_FolderUsedWarning"), otherKb.Name);
                folderWarning.Visibility = Visibility.Visible;
            }
        };
        folderPanel.Children.Add(addFolderButton);
        folderPanel.Children.Add(folderWarning);
        panel.Children.Add(folderPanel);

        // --- Name ---
        var nameBox = new TextBox { Header = _loader.GetString("KB_DialogName"), Text = kb.Name };
        panel.Children.Add(nameBox);

        // --- Model Selection ---
        var modelSelector = new Controls.EmbeddingModelSelector
        {
            CurrentEmbeddingModel = kb.Embedding.Model,
            CurrentContextualizer = kb.Contextualizer.Model,
        };
        modelSelector.Initialize();
        panel.Children.Add(modelSelector);

        // Warning
        var warning = new TextBlock
        {
            Text = "\u26a0\ufe0f " + _loader.GetString("KB_ReindexWarning"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        panel.Children.Add(warning);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = string.Format(_loader.GetString("KB_SettingsDialogTitle"), kb.Name),
            PrimaryButtonText = _loader.GetString("KB_SaveAndReindex"),
            SecondaryButtonText = _loader.GetString("KB_Save"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 600,
            },
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        // Collect updated values
        var newPaths = CollectFolderPaths(folderList);
        if (newPaths.Count == 0) return;

        var newName = nameBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName)) kb.Name = newName;
        kb.SourcePaths = newPaths;
        kb.Embedding = modelSelector.GetEmbeddingConfig();
        kb.Contextualizer = modelSelector.GetContextualizerConfig();
        KnowledgeBaseStore.Update(kb);
        LoggingService.LogInfo($"[KB] Settings saved: {kb.Name} ({kbId})");

        if (result == ContentDialogResult.Primary)
        {
            // Save & Re-index
            var modelChanged = modelSelector.EmbeddingModelChanged || modelSelector.ContextualizerChanged;
            if (modelChanged)
            {
                var dbPath = IOPath.Combine(KnowledgeBaseStore.GetKbPath(kbId), "rag.db");
                try
                {
                    if (File.Exists(dbPath))
                        File.Delete(dbPath);
                }
                catch (IOException ex)
                {
                    LoggingService.LogWarning($"[KB] Could not delete rag.db (in use?): {ex.Message}");
                }

                LoggingService.LogInfo($"[KB] Model changed, full re-index: {kbId}");
                RagProcessManager.StartExec(kbId);
            }
            else
            {
                LoggingService.LogInfo($"[KB] Re-index started: {kbId}");
                RagProcessManager.StartExec(kbId, force: true);
            }

            _pollTimer.Start();
        }

        RefreshList();
    }

    /// <summary>
    /// Cancels an in-progress indexing operation and disables the button.
    /// </summary>
    private void OnCancelIndexClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        RagProcessManager.CancelExec(kbId);
        LoggingService.LogInfo($"[KB] Indexing cancelled: {kbId}");

        btn.IsEnabled = false;
        btn.Content = new ProgressRing { Width = 14, Height = 14, IsActive = true };
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

            // Row 5: Progress bar + stop (indexing) or action buttons
            if (item.IsIndexing == Visibility.Visible)
            {
                var progressRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var progressBar = new ProgressBar
                {
                    Value = item.Progress,
                    Maximum = 100,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(progressBar, 0);
                progressRow.Children.Add(progressBar);

                var stopBtn = new Button
                {
                    Tag = item.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Padding = new Thickness(6),
                    Content = new FontIcon { Glyph = "\uE71A", FontSize = 14 },
                    Margin = new Thickness(8, 0, 0, 0),
                };
                ToolTipService.SetToolTip(stopBtn, new ToolTip
                {
                    Content = _loader.GetString("KB_CancelIndexing") ?? "Cancel indexing",
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
                });
                stopBtn.Click += OnCancelIndexClicked;
                Grid.SetColumn(stopBtn, 1);
                progressRow.Children.Add(stopBtn);

                row.Children.Add(progressRow);
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

                var settingsBtn = new Button
                {
                    Tag = item.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Padding = new Thickness(6),
                    Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
                };
                ToolTipService.SetToolTip(settingsBtn, new ToolTip
                {
                    Content = _loader.GetString("KB_Settings") ?? "Settings",
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
                });
                settingsBtn.Click += OnSettingsClicked;
                buttons.Children.Add(settingsBtn);

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
                reindexBtn.Click += OnQuickReindexClicked;
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
    /// Opens a folder picker dialog and returns the selected folder, or null.
    /// </summary>
    private async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var window = (App.Current as App)?.MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    /// <summary>
    /// Builds a folder row with path label and delete button.
    /// </summary>
    private static Grid BuildFolderRow(string path, StackPanel folderList)
    {
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = path,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var removeBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) => folderList.Children.Remove(row);
        Grid.SetColumn(removeBtn, 1);
        row.Children.Add(removeBtn);

        return row;
    }

    /// <summary>
    /// Collects folder paths from the folder list panel.
    /// </summary>
    private static List<string> CollectFolderPaths(StackPanel folderList) =>
        folderList.Children
            .OfType<Grid>()
            .SelectMany(g => g.Children.OfType<TextBlock>())
            .Select(t => t.Text)
            .ToList();

    /// <summary>
    /// Generates a KB name from the first source folder path,
    /// with deduplication suffix if needed.
    /// </summary>
    private static string GenerateKbName(IReadOnlyList<string> folderPaths)
    {
        if (folderPaths.Count == 0)
            return "Untitled";

        var baseName = IOPath.GetFileName(folderPaths[0].TrimEnd(IOPath.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Untitled";

        var existingNames = KnowledgeBaseStore.ListAll().Select(kb => kb.Name).ToList();
        var name = baseName;
        var counter = 2;
        while (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            name = $"{baseName} ({counter++})";

        return name;
    }

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
