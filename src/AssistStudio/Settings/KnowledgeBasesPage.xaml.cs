using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Mcp.ModelAvailability;
using FieldCure.AssistStudio.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json;
using IOPath = System.IO.Path;
using Microsoft.Windows.ApplicationModel.Resources;

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
    }

    #endregion

    #region Navigation

    /// <inheritdoc/>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshListAsync();
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

        // The "생성" / Create button needs BOTH at least one source folder
        // AND an available model selection. Tracked separately so we can
        // re-evaluate on either event (folder add / model radio click).
        var hasFolder = false;
        Controls.EmbeddingModelSelector? modelSelectorRef = null;
        void RefreshCreateButton()
        {
            dialog.IsPrimaryButtonEnabled =
                hasFolder && (modelSelectorRef?.IsCurrentSelectionAvailable ?? true);
        }

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
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
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
            hasFolder = true;
            RefreshCreateButton();

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
        await modelSelector.InitializeAsync();
        panel.Children.Add(modelSelector);
        modelSelectorRef = modelSelector;
        modelSelector.SelectionChanged += (_, _) => RefreshCreateButton();
        RefreshCreateButton();

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

        SnapshotIndexedWith(kb);
        var execResult = await RagProcessManager.StartExecAsync(kb.Id);
        if (!await HandleStartExecResultAsync(execResult))
        {
            // Creation succeeded but initial indexing didn't start —
            // leave the empty KB in place so the user can fix the
            // configuration and hit re-index.
            await RefreshListAsync();
            return;
        }

        var archiveService = new KnowledgeArchiveService(App.McpRegistry);
        await archiveService.EnsureConnectedAsync();

        await RefreshListAsync();
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

        await RefreshListAsync();
    }

    /// <summary>
    /// Immediately starts incremental re-indexing (new/changed files only).
    /// </summary>
    private async void OnQuickReindexClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        // Snapshot the current model config into IndexedWith before launch
        // so the card continues to show the right "what the index was built
        // with" label if the user opens settings and edits anything mid-run.
        var kbForSnapshot = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kbForSnapshot is not null)
            SnapshotIndexedWith(kbForSnapshot);

        // Pre-flight first so we don't show a spinner for a run that will
        // never start. If the models are reachable we proceed exactly as
        // before (clear change cache, swap button for spinner, start
        // polling). If not, HandleStartExecResultAsync surfaces the
        // reason and we leave the KB row untouched.
        var execResult = await RagProcessManager.StartExecAsync(kbId);
        if (!await HandleStartExecResultAsync(execResult))
            return;

        LoggingService.LogInfo($"[KB] Quick re-index started: {kbId}");

        // Clear cached change-check results — they become stale after re-indexing
        var item = _allItems.FirstOrDefault(i => i.Id == kbId);
        if (item is not null)
        {
            item.ChangesChecked = null;
            item.ChangesAdded = 0;
            item.ChangesModified = 0;
            item.ChangesDeleted = 0;
            item.ChangesFailed = 0;
        }

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
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
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
        // Pre-select the models from IndexedWith (the snapshot captured at
        // the last re-index launch) so the radios match what is actually
        // in the DB. Fall back to the top-level fields for brand new KBs
        // and for legacy configs that predate IndexedWith.
        var modelSelector = new Controls.EmbeddingModelSelector
        {
            CurrentEmbeddingModel = kb.IndexedWith?.Embedding.Model ?? kb.Embedding.Model,
            CurrentContextualizer = kb.IndexedWith?.Contextualizer.Model ?? kb.Contextualizer.Model,
        };
        await modelSelector.InitializeAsync();
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

        // Vertical title: KB name in the normal dialog title font, with
        // a smaller "indexed with" caption directly under it. Shows the
        // snapshot captured at the last re-index launch (IndexedWith) so
        // the line reflects what is actually in the DB, not whatever the
        // user just selected in the form. Caption is omitted entirely
        // for brand new KBs that have never been indexed.
        var indexedEmbeddingModel = kb.IndexedWith?.Embedding.Model ?? kb.Embedding.Model;
        var indexedContextualizerModel = kb.IndexedWith?.Contextualizer.Model ?? kb.Contextualizer.Model;
        var indexedCaption = indexedEmbeddingModel;
        if (!string.IsNullOrEmpty(indexedContextualizerModel))
            indexedCaption += $" \u00b7 {indexedContextualizerModel}";

        var titlePanel = new StackPanel { Spacing = 2 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = kb.Name,
        });
        if (!string.IsNullOrEmpty(indexedCaption) && kb.IndexedWith is not null)
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = indexedCaption,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.6,
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = titlePanel,
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

        // Wire the "Save & Re-index" primary button to the selector's
        // availability state so the user cannot trigger a run that would
        // immediately fail pre-flight. The secondary "Save" button stays
        // enabled — saving a config without re-indexing is always OK.
        dialog.IsPrimaryButtonEnabled = modelSelector.IsCurrentSelectionAvailable;
        modelSelector.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = modelSelector.IsCurrentSelectionAvailable;

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
                SnapshotIndexedWith(kb);
                var execResult = await RagProcessManager.StartExecAsync(kbId);
                if (!await HandleStartExecResultAsync(execResult))
                {
                    await RefreshListAsync();
                    return;
                }
            }
            else
            {
                LoggingService.LogInfo($"[KB] Re-index started: {kbId}");
                SnapshotIndexedWith(kb);
                var execResult = await RagProcessManager.StartExecAsync(kbId, force: true);
                if (!await HandleStartExecResultAsync(execResult))
                {
                    await RefreshListAsync();
                    return;
                }
            }

            _pollTimer.Start();
        }

        await RefreshListAsync();
    }

    /// <summary>
    /// Captures the KB's current embedding + contextualizer configuration
    /// into <see cref="KnowledgeBase.IndexedWith"/> and persists the KB,
    /// so the list page card can keep showing "what the index was built
    /// with" even after the user edits the top-level fields via "Save
    /// only". Called right before every re-index launch — the snapshot
    /// represents the configuration the exec process is about to run
    /// against.
    /// </summary>
    private static void SnapshotIndexedWith(KnowledgeBase kb)
    {
        kb.IndexedWith = new IndexedWithSnapshot
        {
            Embedding = new KbProviderConfig
            {
                Provider = kb.Embedding.Provider,
                Model = kb.Embedding.Model,
                BaseUrl = kb.Embedding.BaseUrl,
                ApiKeyPreset = kb.Embedding.ApiKeyPreset,
                Dimension = kb.Embedding.Dimension,
            },
            Contextualizer = new KbProviderConfig
            {
                Provider = kb.Contextualizer.Provider,
                Model = kb.Contextualizer.Model,
                BaseUrl = kb.Contextualizer.BaseUrl,
                ApiKeyPreset = kb.Contextualizer.ApiKeyPreset,
                Dimension = kb.Contextualizer.Dimension,
            },
        };
        KnowledgeBaseStore.Update(kb);
    }

    /// <summary>
    /// Shows a ContentDialog for a <see cref="StartExecResult"/> failure and
    /// returns <c>true</c> if the caller should continue (success) or
    /// <c>false</c> if exec did not start. Pre-flight failures list every
    /// unavailable model with the specific reason so the user can fix the
    /// exact problem without having to read log files.
    /// </summary>
    private async Task<bool> HandleStartExecResultAsync(StartExecResult result)
    {
        if (result.IsSuccess) return true;

        var title = _loader.GetString("KB_PreflightFailureTitle") ?? "Cannot start re-indexing";
        string body;

        if (result.Outcome == StartExecOutcome.PreflightFailed && result.Problems is { Count: > 0 })
        {
            // State-only messaging: we report that the model cannot be
            // reached, not why. Ollama might be off, the model might not
            // be pulled, the network might be blocked — guessing the
            // cause would push the user toward a specific fix that may
            // not even be the right one.
            var embedTemplate = _loader.GetString("KB_PreflightEmbeddingUnavailable")
                ?? "Embedding model '{0}' is not available.";
            var ctxTemplate = _loader.GetString("KB_PreflightContextualizerUnavailable")
                ?? "Contextualizer model '{0}' is not available.";
            var lines = result.Problems.Select(p =>
                string.Format(
                    p.Role == KbModelRole.Embedding ? embedTemplate : ctxTemplate,
                    p.ModelId));
            body = string.Join("\n", lines);
        }
        else
        {
            body = result.ErrorMessage ?? result.Outcome.ToString();
        }

        LoggingService.LogWarning($"[KB] StartExec surfaced failure to user: {title} — {body}");

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
        return false;
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
    /// Auto-runs change check when indexing completes.
    /// </summary>
    private async void OnPollTick(object? sender, object e)
    {
        var justFinished = new List<KbViewModel>();

        foreach (var item in _allItems)
        {
            var wasIndexing = item.IsIndexing == Visibility.Visible;
            await UpdateItemStatusAsync(item);
            if (wasIndexing && item.IsIndexing == Visibility.Collapsed)
                justFinished.Add(item);
        }

        // Auto-check changes for KBs that just finished indexing
        foreach (var item in justFinished)
        {
            var result = await CheckChangesAsync(item.Id);
            if (result is not null)
            {
                item.ChangesChecked = true;
                item.ChangesAdded = result.Added;
                item.ChangesModified = result.Modified;
                item.ChangesDeleted = result.Deleted;
                item.ChangesFailed = result.Failed;

                if (!result.IsClean)
                {
                    item.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                    item.StatusBrush = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                }
            }
        }

        ApplyFilter();

        if (!_allItems.Any(i => i.IsIndexing == Visibility.Visible))
            _pollTimer.Stop();
    }

    /// <summary>
    /// Updates a single KB item's status via MCP <c>get_index_info</c>, falling back to SQLite.
    /// </summary>
    private async Task UpdateItemStatusAsync(KbViewModel item)
    {
        var info = await GetIndexInfoAsync(item.Id);

        if (info is not null)
        {
            // MCP data available — build stats with optional failed count and date (skip date while indexing)
            var statsBase = $"{info.TotalFiles} files, {info.TotalChunks} chunks";
            if (info.FailedCount > 0)
                statsBase += $" \u00b7 {info.FailedCount} failed";
            if (!info.IsIndexing
                && info.LastIndexedAt is not null
                && DateTime.TryParse(info.LastIndexedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                statsBase += $" \u00b7 {dt.ToLocalTime():yyyy-MM-dd HH:mm}";
            }
            item.StatsText = statsBase;
            item.IsPromptStale = info.IsPromptStale;

            if (info.IsIndexing)
            {
                // Clear change-check cache while indexing — results will be stale
                item.ChangesChecked = null;
                item.ChangesAdded = 0;
                item.ChangesModified = 0;
                item.ChangesDeleted = 0;
                item.ChangesFailed = 0;

                item.IsIndexing = Visibility.Visible;
                item.Progress = info is { Current: not null, Total: > 0 }
                    ? (double)info.Current.Value / info.Total.Value * 100 : 0;
                var indexingLabel = _loader.GetString("KB_StatusIndexing") ?? "Indexing";
                item.StatusText = info.Current is not null
                    ? $"{indexingLabel} ({info.Current}/{info.Total})"
                    : indexingLabel;
                item.StatusBrush = new SolidColorBrush(Colors.DodgerBlue);
            }
            else if (info.IsPromptStale)
            {
                item.IsIndexing = Visibility.Collapsed;
                item.Progress = 0;
                item.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                item.StatusBrush = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            }
            else if (info.TotalFiles > 0)
            {
                item.IsIndexing = Visibility.Collapsed;
                item.Progress = 0;

                // If cached change-check found dirty files, show stale status
                if (item.ChangesChecked == true
                    && (item.ChangesAdded > 0 || item.ChangesModified > 0 || item.ChangesDeleted > 0))
                {
                    item.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                    item.StatusBrush = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                }
                else
                {
                    item.StatusText = _loader.GetString("KB_StatusReady") ?? "Ready";
                    item.StatusBrush = new SolidColorBrush(Colors.Gray);
                }
            }
            else
            {
                item.IsIndexing = Visibility.Collapsed;
                item.Progress = 0;
                item.StatusText = _loader.GetString("KB_StatusNoIndex") ?? "No index";
                item.StatusBrush = new SolidColorBrush(Colors.Gray);
                item.StatsText = "";
            }
        }
        else
        {
            // Fallback: direct SQLite read
            var status = KnowledgeBaseStore.GetIndexingStatus(item.Id);
            if (status is not null)
            {
                item.IsIndexing = Visibility.Visible;
                item.Progress = status.Total > 0
                    ? (double)status.Current / status.Total * 100 : 0;
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
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Refreshes the full KB list from disk, enriched with MCP index info when available.
    /// </summary>
    private async Task RefreshListAsync()
    {
        // Preserve cached change-check results across refreshes
        var cachedChanges = _allItems.Where(i => i.ChangesChecked == true)
            .ToDictionary(i => i.Id, i => (i.ChangesAdded, i.ChangesModified, i.ChangesDeleted, i.ChangesFailed, i.ChangesChecked));

        var kbs = KnowledgeBaseStore.ListAll();
        _allItems.Clear();

        // Shared availability checker for this refresh pass. One instance
        // for the whole loop means the Ollama /api/tags probe runs once
        // regardless of how many KBs are in the list.
        var availabilityChecker = new ModelAvailabilityChecker();
        var statusUnavailableLabel = _loader.GetString("KB_StatusModelUnavailable") ?? "Model unavailable";

        foreach (var kb in kbs)
        {
            // Model info line shows what the DB was actually indexed with,
            // not the top-level fields (which may have been edited since
            // via "Save only" without a re-index). IndexedWith is the
            // snapshot captured at the last re-index launch; fall back to
            // the top-level fields for brand new KBs.
            var cardEmbeddingModel = kb.IndexedWith?.Embedding.Model ?? kb.Embedding.Model;
            var cardContextualizerModel = kb.IndexedWith?.Contextualizer.Model ?? kb.Contextualizer.Model;
            var modelInfo = cardEmbeddingModel;
            if (!string.IsNullOrEmpty(cardContextualizerModel))
                modelInfo += $" \u00b7 {cardContextualizerModel}";

            var item = new KbViewModel
            {
                Id = kb.Id,
                Name = kb.Name,
                SourcePathsText = string.Join(", ", kb.SourcePaths),
                ModelInfoText = modelInfo,
            };

            // State report only: which models are currently unreachable.
            // Checked against the IndexedWith snapshot when present so the
            // warning line matches the model ids shown above it.
            var kbForCheck = kb.IndexedWith is null ? kb : new KnowledgeBase
            {
                Id = kb.Id,
                Name = kb.Name,
                Embedding = kb.IndexedWith.Embedding,
                Contextualizer = kb.IndexedWith.Contextualizer,
            };
            var problems = await availabilityChecker.CheckKbAsync(kbForCheck);
            if (problems.Count > 0)
            {
                var ids = string.Join(", ", problems.Select(p => p.ModelId));
                item.ModelWarningText = $"\u26a0 {statusUnavailableLabel}: {ids}";
            }

            // Restore cached change-check results
            if (cachedChanges.TryGetValue(kb.Id, out var cached))
            {
                item.ChangesAdded = cached.ChangesAdded;
                item.ChangesModified = cached.ChangesModified;
                item.ChangesDeleted = cached.ChangesDeleted;
                item.ChangesFailed = cached.ChangesFailed;
                item.ChangesChecked = cached.ChangesChecked;
            }

            await UpdateItemStatusAsync(item);
            _allItems.Add(item);
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

            // Row 4b: Model availability warning (only when a configured
            // model is unreachable). Drawn in caution color so it stands
            // out against the other caption rows.
            if (!string.IsNullOrEmpty(item.ModelWarningText))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.ModelWarningText,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                    TextWrapping = TextWrapping.Wrap,
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
                // Change summary bar (shown after check_changes)
                if (item.ChangesChecked == true)
                {
                    if (item.ChangesAdded == 0 && item.ChangesModified == 0 && item.ChangesDeleted == 0 && item.ChangesFailed == 0)
                    {
                        row.Children.Add(new TextBlock
                        {
                            Text = _loader.GetString("KB_NoChanges") ?? "No changes",
                            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                            Opacity = 0.5,
                            Margin = new Thickness(0, 4, 0, 0),
                        });
                    }
                    else
                    {
                        var changeParts = new List<string>();
                        if (item.ChangesAdded > 0)
                            changeParts.Add($"+ {(_loader.GetString("KB_ChangesAdded") ?? "Added")} {item.ChangesAdded}");
                        if (item.ChangesModified > 0)
                            changeParts.Add($"~ {(_loader.GetString("KB_ChangesModified") ?? "Modified")} {item.ChangesModified}");
                        if (item.ChangesDeleted > 0)
                            changeParts.Add($"- {(_loader.GetString("KB_ChangesDeleted") ?? "Deleted")} {item.ChangesDeleted}");
                        if (item.ChangesFailed > 0)
                            changeParts.Add($"! {(_loader.GetString("KB_ChangesFailed") ?? "Failed")} {item.ChangesFailed}");

                        row.Children.Add(new TextBlock
                        {
                            Text = string.Join("    ", changeParts),
                            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                            Margin = new Thickness(0, 4, 0, 0),
                        });
                    }
                }

                // Action buttons row: [Check changes] ... [Settings] [Re-index] [Delete]
                var actionRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var checkBtn = new Button
                {
                    Tag = item.Id,
                    Content = _loader.GetString("KB_CheckChanges") ?? "Check changes",
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 12,
                };
                checkBtn.Click += OnCheckChangesClicked;
                Grid.SetColumn(checkBtn, 0);
                actionRow.Children.Add(checkBtn);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
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
                    // Disable the re-index button when the indexed-with
                    // models are unreachable — the pre-flight check would
                    // block the run anyway and the user already sees the
                    // warning line above. "변경 확인" is still enabled
                    // because check_changes is a read-only dry run that
                    // does not depend on the embedding pipeline.
                    IsEnabled = string.IsNullOrEmpty(item.ModelWarningText),
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

                Grid.SetColumn(buttons, 2);
                actionRow.Children.Add(buttons);

                row.Children.Add(actionRow);
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

    /// <summary>
    /// Calls the <c>get_index_info</c> MCP tool on the RAG server for the given KB.
    /// Returns <c>null</c> when the RAG server is not connected (caller should fall back to SQLite).
    /// </summary>
    private static async Task<IndexInfoResult?> GetIndexInfoAsync(string kbId)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true)
            return null;

        try
        {
            var argsJson = JsonSerializer.Serialize(new { kb_id = kbId });
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("get_index_info", args, null);

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            var totalFiles = root.GetProperty("total_files").GetInt32();
            var totalChunks = root.GetProperty("total_chunks").GetInt32();
            var isIndexing = root.GetProperty("is_indexing").GetBoolean();

            int? current = null, total = null;
            if (isIndexing && root.TryGetProperty("indexing_progress", out var prog) && prog.ValueKind == JsonValueKind.Object)
            {
                current = prog.GetProperty("current").GetInt32();
                total = prog.GetProperty("total").GetInt32();
            }

            var isPromptStale = root.TryGetProperty("is_prompt_stale", out var stale) && stale.GetBoolean();
            var lastIndexedAt = root.TryGetProperty("last_indexed_at", out var lai) ? lai.GetString() : null;
            var failedCount = root.TryGetProperty("last_failed_count", out var fc) ? fc.GetInt32() : 0;

            return new IndexInfoResult(totalFiles, totalChunks, isIndexing, current, total, isPromptStale, lastIndexedAt, failedCount);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parsed result from the <c>get_index_info</c> MCP tool.</summary>
    private sealed record IndexInfoResult(
        int TotalFiles, int TotalChunks,
        bool IsIndexing, int? Current, int? Total,
        bool IsPromptStale, string? LastIndexedAt,
        int FailedCount);

    /// <summary>Parsed result from the <c>check_changes</c> MCP tool.</summary>
    private sealed record ChangeCheckResult(
        int Added, int Modified, int Deleted, int Failed, bool IsClean);

    /// <summary>
    /// Calls the <c>check_changes</c> MCP tool to compare filesystem against the index.
    /// Returns <c>null</c> when the RAG server is not connected.
    /// </summary>
    private static async Task<ChangeCheckResult?> CheckChangesAsync(string kbId)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true)
            return null;

        try
        {
            var argsJson = JsonSerializer.Serialize(new { kb_id = kbId });
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("check_changes", args, null);

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            return new ChangeCheckResult(
                root.GetProperty("added").GetInt32(),
                root.GetProperty("modified").GetInt32(),
                root.GetProperty("deleted").GetInt32(),
                root.TryGetProperty("failed", out var fail) ? fail.GetInt32() : 0,
                root.TryGetProperty("is_clean", out var clean) && clean.GetBoolean());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Handles the "Check changes" button click — calls check_changes and updates the KB card.
    /// </summary>
    private async void OnCheckChangesClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kbId) return;

        var item = _allItems.FirstOrDefault(i => i.Id == kbId);
        if (item is null) return;

        // Show loading state
        var originalContent = btn.Content;
        btn.IsEnabled = false;
        btn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new ProgressRing { IsActive = true, Width = 12, Height = 12 },
                new TextBlock { Text = _loader.GetString("KB_Checking") ?? "Checking...", FontSize = 12 },
            },
        };

        var result = await CheckChangesAsync(kbId);
        if (result is not null)
        {
            item.ChangesChecked = true;
            item.ChangesAdded = result.Added;
            item.ChangesModified = result.Modified;
            item.ChangesDeleted = result.Deleted;

            if (!result.IsClean)
            {
                item.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                item.StatusBrush = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            }
        }

        // Restore button
        btn.Content = originalContent;
        btn.IsEnabled = true;
        ApplyFilter();
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
    public bool IsPromptStale { get; set; }

    /// <summary>
    /// Short, pre-formatted warning line shown under the model info row
    /// when one or more of the KB's configured models is not reachable.
    /// Null when everything is fine. Populated during
    /// <see cref="KnowledgeBasesPage.RefreshListAsync"/> via
    /// <see cref="AssistStudio.Mcp.ModelAvailability.ModelAvailabilityService"/>.
    /// </summary>
    public string? ModelWarningText { get; set; }

    /// <summary>Change detection results (populated after "Check changes" button click).</summary>
    public int ChangesAdded { get; set; }
    public int ChangesModified { get; set; }
    public int ChangesDeleted { get; set; }
    public int ChangesFailed { get; set; }
    public bool? ChangesChecked { get; set; }
}
