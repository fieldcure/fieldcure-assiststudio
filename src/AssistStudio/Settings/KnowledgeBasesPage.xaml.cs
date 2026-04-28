using AssistStudio.Controls;
using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing knowledge bases (create, delete, re-index, monitor).
/// Flat list style unified with Memory and Schedule pages. Per-card lifecycle —
/// each <see cref="KbCard"/> owns its own status loading and polling.
/// </summary>
public sealed partial class KnowledgeBasesPage : Page
{
    #region Fields

    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly ObservableCollection<KbItemViewModel> _items = [];
    private readonly Dictionary<string, int> _matchCountsByKbId = [];
    private readonly ResourceLoader _loader = new();
    private string _searchQuery = "";
    private Task? _ragReadyTask;
    private bool _isDialogOpen;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeBasesPage"/> class.
    /// </summary>
    public KnowledgeBasesPage()
    {
        InitializeComponent();
        KbList.ItemsSource = _items;

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
    }

    #endregion

    #region Navigation

    /// <inheritdoc/>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Must be assigned BEFORE RefreshListAsync so cards created during
        // the refresh pick up a non-null RagReadyTask.
        // Per ADR-001 Principle 5, the RAG server self-recovers from key rotation
        // via 401 → Invalidate → Elicitation, so page entry only needs to make sure
        // the server is connected at all — not to force a reconnect.
        _ragReadyTask = new KnowledgeBaseService(App.McpRegistry).EnsureConnectedAsync();

        await RefreshListAsync();
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _searchDebounceTimer.Stop();
        ReleaseLiveCards();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the Refresh button click — re-runs the same load path that fires on
    /// page navigation, so card status reflects the latest server-side state.
    /// </summary>
    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshListAsync();
    }

    /// <summary>
    /// Opens the KB creation dialog.
    /// Folder selection comes first; name auto-generates from the first folder.
    /// </summary>
    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;

        var dialog = new KbEditDialog { XamlRoot = XamlRoot };
        await dialog.InitializeAsync();

        var result = await dialog.ShowAsync();
        _isDialogOpen = false;
        if (result != ContentDialogResult.Primary)
            return;

        var sourcePaths = dialog.SourcePaths;
        if (sourcePaths.Count == 0)
            return;

        var name = EnsureUniqueName(dialog.KbName);

        var kb = KnowledgeBaseStore.Create(name, sourcePaths,
            dialog.EmbeddingConfig, dialog.ContextualizerConfig);
        LoggingService.LogInfo($"[KB] Created: {kb.Name} ({kb.Id})");

        SnapshotIndexedWith(kb);

        var isDeferred = dialog.IsDeferred;
        var status = await StartReindexAsync(kb.Id, deferred: isDeferred);

        if (isDeferred)
        {
            LoggingService.LogInfo($"[KB] Deferred indexing scheduled: {kb.Name} ({kb.Id})");

            if (App.Current is App app)
                app.MainWindow?.DeferredThisSession.Add((kb.Id, kb.Name));

            AppendKbCard(kb);
            FindItem(kb.Id)?.SetDeferredVisual(true);
            return;
        }

        if (!await HandleStartReindexResultAsync(status))
        {
            AppendKbCard(kb);
            return;
        }

        var kbService = new KnowledgeBaseService(App.McpRegistry);
        await kbService.EnsureConnectedAsync();

        AppendKbCard(kb);
    }

    /// <summary>
    /// Handles delete request from a <see cref="KbCard"/>.
    /// </summary>
    private async void OnDeleteRequested(object? sender, string kbId)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;
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
        _isDialogOpen = false;
        if (result != ContentDialogResult.Primary) return;

        // Logical deletion: remove config.json. Physical folder cleanup
        // happens via prune-orphans at next app startup. Serve cache is
        // cleaned up lazily on next GetKb() call.
        KnowledgeBaseStore.Delete(kbId);
        await CancelReindexAsync(kbId);
        LoggingService.LogInfo($"[KB] Deleted (config.json removed): {kbId}");

        if (App.Current is App app)
            app.MainWindow?.DeferredThisSession.RemoveAll(e => e.KbId == kbId);

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
        _isDialogOpen = true;
        await errorDialog.ShowAsync();
        _isDialogOpen = false;
    }

    /// <summary>
    /// Handles "Re-index now" from a <see cref="KbCard"/>. Snapshots the
    /// current config into <c>IndexedWith</c>, fires <c>start_reindex</c>,
    /// and asks the card to refresh — the card picks up the new
    /// indexing state on its own from <c>get_index_info</c>.
    /// </summary>
    private async void OnReindexRequested(object? sender, string kbId)
    {
        var kbForSnapshot = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kbForSnapshot is not null)
            SnapshotIndexedWith(kbForSnapshot);

        var status = await StartReindexAsync(kbId);
        if (!await HandleStartReindexResultAsync(status))
            return;

        LoggingService.LogInfo($"[KB] Quick re-index queued: {kbId}");

        FindItem(kbId)?.RequestRefresh();
    }

    /// <summary>
    /// Handles "Re-index when app closes" from a <see cref="KbCard"/>.
    /// Schedules a deferred re-index in the RAG queue and flips the card
    /// into its "Scheduled" state so the status badge updates immediately.
    /// </summary>
    private async void OnReindexScheduledRequested(object? sender, string kbId)
    {
        var kb = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kb is null) return;

        SnapshotIndexedWith(kb);

        var status = await StartReindexAsync(kbId, deferred: true);
        if (!await HandleStartReindexResultAsync(status))
            return;

        LoggingService.LogInfo($"[KB] Deferred re-index scheduled: {kb.Name} ({kbId})");

        if (App.Current is App app)
            app.MainWindow?.DeferredThisSession.Add((kbId, kb.Name));

        FindItem(kbId)?.SetDeferredVisual(true);
    }

    /// <summary>
    /// Handles settings request from a <see cref="KbCard"/>.
    /// </summary>
    private async void OnSettingsRequested(object? sender, string kbId)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;

        var kb = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kb is null) return;

        var dialog = new KbEditDialog(kb) { XamlRoot = XamlRoot };
        await dialog.InitializeAsync();

        var result = await dialog.ShowAsync();
        _isDialogOpen = false;
        if (result == ContentDialogResult.None)
            return;

        var newPaths = dialog.SourcePaths;
        if (newPaths.Count == 0) return;

        kb.Name = dialog.KbName;
        kb.SourcePaths = newPaths;
        kb.Embedding = dialog.EmbeddingConfig;
        kb.Contextualizer = dialog.ContextualizerConfig;
        KnowledgeBaseStore.Update(kb);
        LoggingService.LogInfo($"[KB] Settings saved: {kb.Name} ({kbId})");

        if (result == ContentDialogResult.Primary)
        {
            SnapshotIndexedWith(kb);

            string? partial = null;
            if (dialog.ContextualizerChanged)
                partial = "contextualization";
            else if (dialog.EmbeddingModelChanged)
                partial = "embedding";

            var isSettingsDeferred = dialog.IsDeferred;
            var reindexStatus = await StartReindexAsync(
                kbId,
                partialMode: partial,
                force: partial is null,
                deferred: isSettingsDeferred);

            if (isSettingsDeferred)
            {
                LoggingService.LogInfo($"[KB] Deferred re-index scheduled: {kb.Name} ({kbId})" +
                    (partial is not null ? $" partial={partial}" : ""));

                if (App.Current is App app2)
                    app2.MainWindow?.DeferredThisSession.Add((kbId, kb.Name));

                FindItem(kbId)?.SetDeferredVisual(true);
            }
            else
            {
                if (!await HandleStartReindexResultAsync(reindexStatus))
                {
                    await RefreshListAsync();
                    return;
                }

                if (partial is not null)
                    LoggingService.LogInfo($"[KB] Partial re-index ({partial}) queued: {kbId}");
                else
                    LoggingService.LogInfo($"[KB] Re-index queued: {kbId}");
            }
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
    /// Calls the <c>start_reindex</c> MCP tool to queue an indexing request.
    /// Returns the <c>status</c> string from the response, or <c>null</c> on failure.
    /// </summary>
    private static async Task<string?> StartReindexAsync(
        string kbId, string? partialMode = null, bool force = false, bool deferred = false)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true)
        {
            LoggingService.LogWarning($"[KB] start_reindex skipped — RAG server not connected");
            return null;
        }

        try
        {
            var argsObj = new Dictionary<string, object?> { ["kb_id"] = kbId };
            if (partialMode is not null) argsObj["partial_mode"] = partialMode;
            if (force) argsObj["force"] = true;
            if (deferred) argsObj["deferred"] = true;

            var argsJson = JsonSerializer.Serialize(argsObj);
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("start_reindex", args, null);

            using var doc = JsonDocument.Parse(result);
            var status = doc.RootElement.GetProperty("status").GetString();

            LoggingService.LogInfo($"[KB] start_reindex({kbId}): status={status}" +
                (partialMode is not null ? $", partial={partialMode}" : "") +
                (deferred ? ", deferred" : "") +
                (force ? ", force" : ""));

            return status;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[KB] start_reindex failed: {kbId} — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Calls the <c>cancel_reindex</c> MCP tool to remove a pending queue entry.
    /// Returns <c>true</c> if the entry was actually cancelled (or was already gone).
    /// Returns <c>false</c> only when the MCP call itself failed (serve not connected, etc.).
    /// </summary>
    private static async Task<bool> CancelReindexAsync(string kbId)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true) return false;

        try
        {
            var argsJson = JsonSerializer.Serialize(new { kb_id = kbId });
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("cancel_reindex", args, null);
            LoggingService.LogInfo($"[KB] cancel_reindex: {kbId} — {result}");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[KB] cancel_reindex failed: {kbId} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Shows a failure dialog for start_reindex and returns <c>true</c> if the
    /// caller should continue (success) or <c>false</c> if indexing did not queue.
    /// </summary>
    private async Task<bool> HandleStartReindexResultAsync(string? status)
    {
        if (status is not null and not "not_found") return true;

        var title = _loader.GetString("KB_PreflightFailureTitle") ?? "Cannot start re-indexing";
        var body = status == "not_found"
            ? "Knowledge base not found."
            : "RAG server is not connected.";

        LoggingService.LogWarning($"[KB] start_reindex surfaced failure to user: {title} — {body}");

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
        _isDialogOpen = true;
        await dialog.ShowAsync();
        _isDialogOpen = false;
        return false;
    }

    /// <summary>
    /// Handles cancel-index request from a <see cref="KbCard"/>.
    /// </summary>
    private void OnCancelIndexRequested(object? sender, string kbId)
    {
        RagProcessManager.CancelExec(kbId);
        LoggingService.LogInfo($"[KB] Indexing cancelled: {kbId}");
    }

    /// <summary>
    /// Treats Enter as "apply now" — bypasses the debounce timer.
    /// </summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _searchDebounceTimer.Stop();
        _searchQuery = sender.Text?.Trim() ?? "";
        PropagateSearchQuery();
    }

    /// <summary>
    /// Starts the debounce timer on user input.
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        _searchQuery = sender.Text?.Trim() ?? "";
        sender.ItemsSource = null;

        _searchDebounceTimer.Stop();

        if (string.IsNullOrEmpty(_searchQuery))
        {
            PropagateSearchQuery();
        }
        else
        {
            _searchDebounceTimer.Start();
        }
    }

    /// <summary>
    /// Fires after the 250ms debounce interval.
    /// </summary>
    private void OnSearchDebounceTick(object? sender, object e)
    {
        _searchDebounceTimer.Stop();
        PropagateSearchQuery();
    }

    /// <summary>
    /// Pushes the current search query to every live <see cref="KbCard"/>.
    /// </summary>
    private void PropagateSearchQuery()
    {
        foreach (var item in _items)
            item.SearchQuery = _searchQuery;

        if (string.IsNullOrEmpty(_searchQuery))
        {
            _matchCountsByKbId.Clear();
            UpdateMatchSummary();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Refreshes the full KB list from disk, sorted alphabetically by name.
    /// Synchronous — each card loads its own status independently once
    /// attached.
    /// </summary>
    private Task RefreshListAsync()
    {
        var snapshot = KnowledgeBaseStore.ListAll()
            .OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(k => new KbItemViewModel(k, _searchQuery, _ragReadyTask))
            .ToList();

        RebuildItems(snapshot);
        ApplyFilter();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a single newly-created KB to the in-memory list, re-sorts, and
    /// refreshes the card panel so the new card lands at its alphabetical
    /// position. The new card loads its own status on attach.
    /// </summary>
    private void AppendKbCard(KnowledgeBase kb)
    {
        var snapshot = _items
            .Select(i => i.Kb)
            .Append(kb)
            .OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(k => new KbItemViewModel(k, _searchQuery, _ragReadyTask))
            .ToList();

        RebuildItems(snapshot);
        ApplyFilter();
    }

    /// <summary>
    /// Refreshes page-level chrome for the current bound KB item collection.
    /// </summary>
    private void ApplyFilter()
    {
        CounterText.Text = _items.Count.ToString();

        var hasItems = _items.Count > 0;
        EmptyPanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        HintDivider.Visibility = Visibility.Collapsed;
        HintText.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Replaces the bound KB items with a fresh sorted snapshot.
    /// </summary>
    private void RebuildItems(List<KbItemViewModel> items)
    {
        _items.Clear();
        foreach (var item in items)
            _items.Add(item);
    }

    /// <summary>
    /// Clears page-owned per-card aggregates and releases the bound items.
    /// </summary>
    private void ReleaseLiveCards()
    {
        _items.Clear();
        _matchCountsByKbId.Clear();
        UpdateMatchSummary();
    }

    /// <summary>
    /// Aggregates per-card match counts and refreshes the summary bar.
    /// </summary>
    private void OnCardMatchCountChanged(object? sender, int count)
    {
        if (sender is not KbCard card) return;
        var id = card.KbId;
        if (id.Length == 0) return;

        if (count < 0) _matchCountsByKbId.Remove(id);
        else _matchCountsByKbId[id] = count;

        UpdateMatchSummary();
    }

    /// <summary>
    /// Updates the match summary text below the search box.
    /// </summary>
    private void UpdateMatchSummary()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            MatchSummaryText.Visibility = Visibility.Collapsed;
            return;
        }

        MatchSummaryText.Visibility = Visibility.Visible;

        var hitsByName = _items
            .Select(item => item.Kb)
            .Where(kb => _matchCountsByKbId.TryGetValue(kb.Id, out var c) && c > 0)
            .Select(kb => $"{kb.Name} ({_matchCountsByKbId[kb.Id]})")
            .ToList();

        if (hitsByName.Count == 0)
        {
            MatchSummaryText.Text = _loader.GetString("KB_MatchSummary_None") ?? "No matches";
            MatchSummaryText.Opacity = 0.5;
        }
        else
        {
            var prefix = _loader.GetString("KB_MatchSummary_Prefix") ?? "Matches";
            MatchSummaryText.Text = $"{prefix}: {string.Join(" \u00b7 ", hitsByName)}";
            MatchSummaryText.Opacity = 0.8;
        }
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
    /// Returns <paramref name="candidate"/> as-is if unique among existing KB names,
    /// otherwise appends " (2)", " (3)", etc. until a free slot is found.
    /// </summary>
    private static string EnsureUniqueName(string candidate)
    {
        var existingNames = new HashSet<string>(
            KnowledgeBaseStore.ListAll().Select(kb => kb.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(candidate))
            return candidate;

        for (var i = 2; i < 1000; i++)
        {
            var attempt = $"{candidate} ({i})";
            if (!existingNames.Contains(attempt))
                return attempt;
        }

        return candidate;
    }

    /// <summary>
    /// Handles "Index now" on a deferred KB — upgrades the deferred
    /// entry to immediate via <c>start_reindex</c> (deferred=false) and
    /// nudges the card to re-pull its new state.
    /// </summary>
    private async void OnIndexNowRequested(object? sender, string kbId)
    {
        var item = FindItem(kbId);
        item?.SetDeferredVisual(false);

        var status = await StartReindexAsync(kbId, deferred: false);
        if (!await HandleStartReindexResultAsync(status))
        {
            item?.RequestRefresh();
            return;
        }

        LoggingService.LogInfo($"[KB] Deferred → immediate indexing: {kbId}");

        if (App.Current is App app)
            app.MainWindow?.DeferredThisSession.RemoveAll(e => e.KbId == kbId);

        item?.RequestRefresh();
    }

    /// <summary>
    /// Handles "Cancel scheduled" on a deferred KB — removes it from the
    /// queue via MCP, then refreshes the card so the status drops back
    /// to whatever the server now reports (usually "No index").
    /// </summary>
    private async void OnCancelDeferredRequested(object? sender, string kbId)
    {
        if (!await CancelReindexAsync(kbId))
            return;

        if (App.Current is App app)
            app.MainWindow?.DeferredThisSession.RemoveAll(e => e.KbId == kbId);

        var item = FindItem(kbId);
        item?.SetDeferredVisual(false);
        item?.RequestRefresh();

        LoggingService.LogInfo($"[KB] Deferred indexing cancelled: {kbId}");
    }

    /// <summary>
    /// Handles check-changes request from a <see cref="KbCard"/>.
    /// </summary>
    private async void OnCheckChangesRequested(object? sender, string kbId)
    {
        var card = sender as KbCard;
        if (card is null) return;

        var result = await KbMcpClient.CheckChangesAsync(kbId);
        if (result is not null)
            card.ApplyChangeCheckResult(result);
    }

    private KbItemViewModel? FindItem(string kbId) =>
        _items.FirstOrDefault(i => i.Kb.Id == kbId);

    #endregion
}
