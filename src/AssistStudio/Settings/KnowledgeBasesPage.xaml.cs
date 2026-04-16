using AssistStudio.Controls;
using AssistStudio.Controls.Dialogs;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeBasesPage"/> class.
    /// </summary>
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
        var dialog = new ThemedContentDialog
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
            await AppendKbItemAsync(kb);
            return;
        }

        var kbService = new KnowledgeBaseService(App.McpRegistry);
        await kbService.EnsureConnectedAsync();

        await AppendKbItemAsync(kb);
        _pollTimer.Start();
    }

    /// <summary>
    /// Handles delete request from a <see cref="Controls.KbCard"/>.
    /// </summary>
    private async void OnDeleteRequested(object? sender, string kbId)
    {

        var dialog = new ThemedContentDialog
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

        // Cancel exec if running. The cancel file is polled between
        // chunks so a mid-OCR exec may take several seconds to notice
        // and exit. We wait a short grace period here, and
        // KnowledgeBaseStore.Delete retries up to 3 more seconds on top
        // of that — if the process is genuinely stuck we surface the
        // lock to the user instead of crashing.
        if (RagProcessManager.IsExecRunning(kbId))
        {
            RagProcessManager.CancelExec(kbId);
            await Task.Delay(2000);
        }

        try
        {
            KnowledgeBaseStore.Delete(kbId);
            LoggingService.LogInfo($"[KB] Deleted: {kbId}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingService.LogWarning($"[KB] Delete failed (file locked): {kbId} — {ex.Message}");
            await ShowDeleteFailedDialogAsync(kbId);
        }

        await RefreshListAsync();
    }

    /// <summary>
    /// Surfaces a context-aware delete-failed dialog. If the KB is still
    /// indexing we tell the user that specifically and show the current
    /// file progress, because that is the actionable piece of info —
    /// wait for indexing to finish, then retry. If the lock is not from
    /// an active exec (e.g. serve is still holding a read handle) we
    /// fall back to the generic "files still in use" message.
    /// </summary>
    private async Task ShowDeleteFailedDialogAsync(string kbId)
    {
        var title = _loader.GetString("KB_DeleteFailedTitle") ?? "Cannot delete knowledge base";

        var body = new StackPanel { Spacing = 8 };

        var progress = RagProcessManager.GetProgress(kbId);
        if (progress is not null)
        {
            // KB is currently being indexed — name the actual cause.
            body.Children.Add(new TextBlock
            {
                Text = _loader.GetString("KB_DeleteFailedIndexingMessage")
                    ?? "This knowledge base is currently being indexed. Try again after indexing finishes.",
                TextWrapping = TextWrapping.Wrap,
            });

            // Progress line matches the format the KB card uses so the
            // user can correlate "N / M files processed" with what they
            // see on the list page.
            var progressTemplate = _loader.GetString("KB_DeleteFailedIndexingProgress")
                ?? "Progress: {0} / {1} files";
            body.Children.Add(new TextBlock
            {
                Text = string.Format(progressTemplate, progress.Current, progress.Total),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.7,
            });
        }
        else
        {
            body.Children.Add(new TextBlock
            {
                Text = _loader.GetString("KB_DeleteFailedMessage")
                    ?? "The knowledge base files are still in use. Try again after indexing finishes, or restart the app if the issue persists.",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var errorDialog = new ThemedContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = body,
            CloseButtonText = "OK",
        };
        await errorDialog.ShowAsync();
    }

    /// <summary>
    /// Handles re-index request from a <see cref="Controls.KbCard"/>.
    /// </summary>
    private async void OnReindexRequested(object? sender, string kbId)
    {
        var kbForSnapshot = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kbForSnapshot is not null)
            SnapshotIndexedWith(kbForSnapshot);

        var execResult = await RagProcessManager.StartExecAsync(kbId);
        if (!await HandleStartExecResultAsync(execResult))
            return;

        LoggingService.LogInfo($"[KB] Quick re-index started: {kbId}");

        var item = _allItems.FirstOrDefault(i => i.Id == kbId);
        if (item is not null)
        {
            item.ChangesChecked = null;
            item.ChangesAdded = 0;
            item.ChangesModified = 0;
            item.ChangesDeleted = 0;
            item.ChangesFailed = 0;
        }

        _pollTimer.Start();
        ApplyFilter();
    }

    /// <summary>
    /// Handles settings request from a <see cref="Controls.KbCard"/>.
    /// </summary>
    private async void OnSettingsRequested(object? sender, string kbId)
    {

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

        var dialog = new ThemedContentDialog
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

        var dialog = new ThemedContentDialog
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
    /// Handles cancel-index request from a <see cref="Controls.KbCard"/>.
    /// </summary>
    private void OnCancelIndexRequested(object? sender, string kbId)
    {
        RagProcessManager.CancelExec(kbId);
        LoggingService.LogInfo($"[KB] Indexing cancelled: {kbId}");
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
    /// Renders shimmer placeholder cards matching the expected item count.
    /// </summary>
    private void ShowShimmerCards(int count)
    {
        if (count == 0)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            HintDivider.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        HintDivider.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        CounterText.Text = count.ToString();

        var panel = new StackPanel { Spacing = 0 };
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new Rectangle
                {
                    Height = 1,
                    Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }

            var card = new StackPanel { Spacing = 6, Padding = new Thickness(0, 4, 0, 4) };
            card.Children.Add(new Shimmer { Height = 16, Width = 180, HorizontalAlignment = HorizontalAlignment.Left });
            card.Children.Add(new Shimmer { Height = 12, Width = 260, HorizontalAlignment = HorizontalAlignment.Left });
            card.Children.Add(new Shimmer { Height = 12, Width = 320, HorizontalAlignment = HorizontalAlignment.Left });
            panel.Children.Add(card);
        }

        KbList.ItemsSource = new[] { panel };
    }

    /// <summary>
    /// Refreshes the full KB list from disk, enriched with MCP index info when available.
    /// </summary>
    private async Task RefreshListAsync()
    {
        // Preserve cached change-check results across refreshes
        var cachedChanges = _allItems.Where(i => i.ChangesChecked == true)
            .ToDictionary(i => i.Id, i => (i.ChangesAdded, i.ChangesModified, i.ChangesDeleted, i.ChangesFailed, i.ChangesChecked));

        var kbs = KnowledgeBaseStore.ListAll();

        // Show shimmer placeholders matching the KB count
        ShowShimmerCards(kbs.Count);
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
    /// Adds a single newly-created KB to the in-memory list and refreshes the
    /// UI without re-querying every existing KB's status and model availability.
    /// </summary>
    private async Task AppendKbItemAsync(KnowledgeBase kb)
    {
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

        await UpdateItemStatusAsync(item);
        _allItems.Add(item);
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

        CounterText.Text = _allItems.Count.ToString();

        KbList.ItemsSource = null;
        var panel = new StackPanel { Spacing = 0 };

        for (int idx = 0; idx < filtered.Count; idx++)
        {
            if (idx > 0)
            {
                panel.Children.Add(new Rectangle
                {
                    Height = 1,
                    Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }

            var card = new Controls.KbCard { ViewModel = filtered[idx] };
            card.DeleteRequested += OnDeleteRequested;
            card.SettingsRequested += OnSettingsRequested;
            card.ReindexRequested += OnReindexRequested;
            card.CancelIndexRequested += OnCancelIndexRequested;
            card.CheckChangesRequested += OnCheckChangesRequested;
            panel.Children.Add(card);
        }

        KbList.ItemsSource = new[] { panel };

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
    /// Handles check-changes request from a <see cref="Controls.KbCard"/>.
    /// </summary>
    private async void OnCheckChangesRequested(object? sender, string kbId)
    {
        var item = _allItems.FirstOrDefault(i => i.Id == kbId);
        if (item is null) return;

        var result = await CheckChangesAsync(kbId);
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

    #endregion
}

