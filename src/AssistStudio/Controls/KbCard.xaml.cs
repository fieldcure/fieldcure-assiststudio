using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Settings;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Controls;

/// <summary>
/// Card that renders one knowledge base and owns its own status loading
/// and polling. Takes a <see cref="KnowledgeBase"/> via <see cref="Kb"/>
/// and fully manages its visual state — shimmer while loading the first
/// <c>get_index_info</c> response, progress bar while indexing, idle
/// actions once ready.
/// </summary>
public sealed partial class KbCard : UserControl, INotifyPropertyChanged
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Item"/> dependency property.</summary>
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(KbItemViewModel),
            typeof(KbCard),
            new PropertyMetadata(null, OnItemChanged));

    /// <summary>Identifies the <see cref="Kb"/> dependency property.</summary>
    public static readonly DependencyProperty KbProperty =
        DependencyProperty.Register(
            nameof(Kb),
            typeof(KnowledgeBase),
            typeof(KbCard),
            new PropertyMetadata(null, OnKbChanged));

    /// <summary>Identifies the <see cref="SearchQuery"/> dependency property.</summary>
    public static readonly DependencyProperty SearchQueryProperty =
        DependencyProperty.Register(
            nameof(SearchQuery),
            typeof(string),
            typeof(KbCard),
            new PropertyMetadata(string.Empty, OnSearchQueryChanged));

    #endregion

    #region Constants

    private const int MaxChunksPerCard = 3;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private readonly KnowledgeBaseSearchService _searchService = new();
    private readonly KbViewModel _vm = new();
    private DispatcherTimer? _pollTimer;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private bool _isLoading;
    private bool _isSearching;
    private bool _isReindexFlyoutOpen;
    private string _statusBrushKey = "TextFillColorSecondaryBrush";
    private string _changeSummaryBrushKey = "DefaultTextForegroundThemeBrush";

    #endregion

    #region Events

    /// <summary>Raised when the user clicks the delete button.</summary>
    public event EventHandler<string>? DeleteRequested;

    /// <summary>Raised when the user clicks the settings button.</summary>
    public event EventHandler<string>? SettingsRequested;

    /// <summary>
    /// Raised when the user picks "Re-index now" from the flyout —
    /// immediate indexing (queued by the RAG orchestrator, runs right away).
    /// </summary>
    public event EventHandler<string>? ReindexRequested;

    /// <summary>
    /// Raised when the user picks "Re-index when app closes" — deferred
    /// indexing, scheduled to run on app shutdown.
    /// </summary>
    public event EventHandler<string>? ReindexScheduledRequested;

    /// <summary>Raised when the user clicks the cancel/stop button.</summary>
    public event EventHandler<string>? CancelIndexRequested;

    /// <summary>Raised when the user clicks the check-changes button.</summary>
    public event EventHandler<string>? CheckChangesRequested;

    /// <summary>Raised when the user clicks "Index now" on a deferred KB.</summary>
    public event EventHandler<string>? IndexNowRequested;

    /// <summary>Raised when the user clicks "Cancel scheduled" on a deferred KB.</summary>
    public event EventHandler<string>? CancelDeferredRequested;

    /// <summary>Raised when a search completes or clears. Value is match count (-1 = cleared).</summary>
    public event EventHandler<int>? MatchCountChanged;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="KbCard"/>.</summary>
    public KbCard()
    {
        InitializeComponent();
        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += OnCardLoaded;
        Unloaded += OnCardUnloaded;
        ThemeHelper.SubscribeThemeChanges(this, RefreshThemeBrushes);

        // Re-index button's flyout: keep the action icons visible while
        // the flyout is open even though it renders in a popup outside
        // the card bounds (which would otherwise fire PointerExited).
        if (ReindexButton.Flyout is { } flyout)
        {
            flyout.Opened += OnReindexFlyoutOpened;
            flyout.Closed += OnReindexFlyoutClosed;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// The item view model that drives this card when hosted from an
    /// <see cref="ItemsRepeater"/> template.
    /// </summary>
    public KbItemViewModel? Item
    {
        get => (KbItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>
    /// The knowledge base this card represents. Setting it rebuilds the
    /// static fields (name, source paths, model info) and, once the card
    /// is loaded, kicks off a status refresh.
    /// </summary>
    public KnowledgeBase? Kb
    {
        get => (KnowledgeBase?)GetValue(KbProperty);
        set => SetValue(KbProperty, value);
    }

    /// <summary>Current search query. Non-empty triggers a chunk search.</summary>
    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    /// <summary>
    /// Task from <see cref="KnowledgeBaseService.EnsureConnectedAsync"/>.
    /// Injected by the page so searches can wait for RAG serve startup.
    /// </summary>
    public Task? RagReadyTask { get; set; }

    /// <summary>Most recent match count. -1 = not searched or cleared.</summary>
    public int LastMatchCount { get; private set; } = -1;

    /// <summary>Id of the KB this card is bound to, or empty if unbound.</summary>
    public string KbId => Kb?.Id ?? "";

    /// <summary>
    /// <c>true</c> while the initial <c>get_index_info</c> call is in
    /// flight. Drives the load-shimmer placeholders via x:Bind.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>
    /// <c>true</c> while a chunk search is in flight for this card.
    /// Drives the match-panel shimmer via x:Bind.
    /// </summary>
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetField(ref _isSearching, value);
    }

    /// <summary>Fires whenever a bindable card property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> and raises <see cref="PropertyChanged"/>
    /// when the value changes. Backing helper for the card's own x:Bind
    /// sources (view model changes flow through <see cref="KbViewModel"/>).
    /// </summary>
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Flips the card into the "deferred scheduled" state: Index-now /
    /// Cancel-scheduled buttons, "Scheduled" status label, blue color.
    /// Called by the page right after a deferred re-index is scheduled
    /// so the UI updates immediately, without waiting for a server poll.
    /// </summary>
    public void MarkDeferredScheduled()
    {
        _vm.IsDeferredIndexing = true;
        _vm.StatusText = _loader.GetString("KB_StatusScheduled") ?? "Scheduled";
        SetStatusBrush("StatusAccentForegroundBrush");
    }

    /// <summary>
    /// Clears the deferred flag. Callers typically follow up with
    /// <see cref="RefreshAsync"/> so the status label picks up the real
    /// current state from the server.
    /// </summary>
    public void ClearDeferred()
    {
        _vm.IsDeferredIndexing = false;
    }

    #endregion

    #region Kb Lifecycle

    /// <summary>
    /// Reacts to a new <see cref="Kb"/> value: repopulates the static
    /// fields on the view model and, if the card is already attached to
    /// the tree, kicks off an async status refresh.
    /// </summary>
    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KbCard card) return;

        if (e.OldValue is KbItemViewModel oldItem)
            oldItem.PropertyChanged -= card.OnItemPropertyChanged;

        if (e.NewValue is KbItemViewModel newItem)
        {
            newItem.PropertyChanged += card.OnItemPropertyChanged;
            card.Kb = newItem.Kb;
            card.SearchQuery = newItem.SearchQuery;
            card.RagReadyTask = newItem.RagReadyTask;
            if (newItem.IsDeferredVisual)
                card.MarkDeferredScheduled();
            else
                card.ClearDeferred();
        }
    }

    private static void OnKbChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KbCard card) return;
        card.ApplyKbSnapshot();
        if (card.IsLoaded && card.Kb is not null)
            _ = card.StartLoadAsync();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not KbItemViewModel item) return;

        if (e.PropertyName == nameof(KbItemViewModel.Kb))
        {
            Kb = item.Kb;
            return;
        }

        if (e.PropertyName == nameof(KbItemViewModel.SearchQuery))
        {
            SearchQuery = item.SearchQuery;
            return;
        }

        if (e.PropertyName == nameof(KbItemViewModel.RagReadyTask))
        {
            RagReadyTask = item.RagReadyTask;
            return;
        }

        if (e.PropertyName == nameof(KbItemViewModel.IsDeferredVisual))
        {
            if (item.IsDeferredVisual) MarkDeferredScheduled();
            else ClearDeferred();
            return;
        }

        if (e.PropertyName == nameof(KbItemViewModel.RefreshToken))
            _ = RefreshAsync();
    }

    /// <summary>
    /// Copies the immediately-known fields from <see cref="Kb"/> onto the
    /// view model. Status/stats/indexing progress stay untouched — those
    /// come from MCP and are filled in by <see cref="StartLoadAsync"/>.
    /// </summary>
    private void ApplyKbSnapshot()
    {
        var kb = Kb;
        if (kb is null)
        {
            _vm.Id = "";
            _vm.Name = "";
            _vm.SourcePathsText = "";
            _vm.ModelInfoText = "";
            return;
        }

        _vm.Id = kb.Id;
        _vm.Name = kb.Name;
        _vm.SourcePathsText = string.Join(", ", kb.SourcePaths);

        // Model info line shows what the DB was actually indexed with
        // (IndexedWith snapshot), not the current top-level fields which
        // may have been edited via "Save only" without a re-index.
        var embModel = kb.IndexedWith?.Embedding.Model ?? kb.Embedding.Model;
        var ctxModel = kb.IndexedWith?.Contextualizer.Model ?? kb.Contextualizer.Model;
        var modelInfo = embModel;
        if (!string.IsNullOrEmpty(ctxModel))
            modelInfo += $" \u00b7 {ctxModel}";
        _vm.ModelInfoText = modelInfo;
    }

    private async void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (Kb is null) return;
        await StartLoadAsync();
    }

    private void OnCardUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();
        StopPollTimer();
    }

    /// <summary>Reveals the idle action icons (Settings/Reindex/Delete) on hover.</summary>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ActionIconsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides the idle action icons when the pointer leaves — unless the
    /// re-index flyout is open. The flyout renders in a popup outside the
    /// card's visual bounds, which would otherwise trigger a false exit
    /// and hide the icon the user is about to click.
    /// </summary>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isReindexFlyoutOpen) return;
        ActionIconsPanel.Visibility = Visibility.Collapsed;
    }

    private void OnReindexFlyoutOpened(object? sender, object e)
    {
        _isReindexFlyoutOpen = true;
        ActionIconsPanel.Visibility = Visibility.Visible;
    }

    private void OnReindexFlyoutClosed(object? sender, object e)
    {
        _isReindexFlyoutOpen = false;
        // After close, the next PointerExited will hide the icons if the
        // pointer has actually left the card bounds. No direct check here.
    }

    /// <summary>
    /// Shows the shimmer overlay, fetches the KB's live status once, then
    /// flips to the real content. Starts the poll timer if the KB turns
    /// out to be actively indexing.
    /// </summary>
    public async Task StartLoadAsync()
    {
        // Cancel any in-flight predecessor and install ourselves as the
        // "active" load. We intentionally do NOT gate the finally on
        // ct.IsCancellationRequested — a transient Unloaded/Loaded cycle
        // during WinUI's initial visual tree wiring can cancel our token
        // without any follow-up load kicking off, which would leave the
        // card stuck in shimmer state. Instead we check whether we're
        // still the latest load (ReferenceEquals on _loadCts) and reset
        // IsLoading in that case, regardless of cancellation.
        _loadCts?.Cancel();
        var myCts = new CancellationTokenSource();
        _loadCts = myCts;

        IsLoading = true;
        try
        {
            await RefreshStatusAsync();
        }
        finally
        {
            if (ReferenceEquals(_loadCts, myCts))
            {
                IsLoading = false;
                SyncPollTimer();
                // Drop the field reference before disposing so later
                // callers (OnCardUnloaded, a follow-up StartLoadAsync)
                // don't call .Cancel() on a disposed CTS.
                _loadCts = null;
            }
            myCts.Dispose();
        }
    }

    /// <summary>
    /// Public entry point for the page to nudge this card after an action
    /// (re-index, cancel, etc.) that changes the server-side state.
    /// Refreshes status and restarts/stops the poll timer accordingly.
    /// </summary>
    public async Task RefreshAsync()
    {
        await RefreshStatusAsync();
        SyncPollTimer();
    }

    #endregion

    #region Status Refresh

    /// <summary>
    /// Fetches <c>get_index_info</c> for this KB and applies the result
    /// to the view model. Falls back to direct SQLite reads when the RAG
    /// server is not connected.
    /// </summary>
    private async Task RefreshStatusAsync()
    {
        if (_vm.Id.Length == 0) return;

        var info = await KbMcpClient.GetIndexInfoAsync(_vm.Id);

        if (info is not null)
        {
            ApplyIndexInfo(info);
            return;
        }

        // MCP disconnected — direct SQLite fallback.
        var status = KnowledgeBaseStore.GetIndexingStatus(_vm.Id);
        if (status is not null)
        {
            _vm.IsIndexing = Visibility.Visible;
            _vm.Progress = status.Total > 0 ? (double)status.Current / status.Total * 100 : 0;
            var indexingLabel = _loader.GetString("KB_StatusIndexing") ?? "Indexing";
            _vm.StatusText = $"{indexingLabel} ({status.Current}/{status.Total})";
            SetStatusBrush("StatusAccentForegroundBrush");
            return;
        }

        _vm.IsIndexing = Visibility.Collapsed;
        _vm.Progress = 0;

        var stats = KnowledgeBaseStore.GetStats(_vm.Id);
        if (stats is not null)
        {
            _vm.StatusText = _loader.GetString("KB_StatusReady") ?? "Ready";
            SetStatusBrush("TextFillColorSecondaryBrush");
            _vm.StatsText = $"{stats.TotalFiles} files, {stats.TotalChunks} chunks";
        }
        else
        {
            _vm.StatusText = _loader.GetString("KB_StatusNoIndex") ?? "No index";
            SetStatusBrush("TextFillColorSecondaryBrush");
            _vm.StatsText = "";
        }
    }

    /// <summary>
    /// Maps a parsed <see cref="IndexInfoResult"/> onto the view model
    /// fields, picking the right status label + color for each state
    /// (indexing in progress, stale prompt, ready, empty).
    /// </summary>
    private void ApplyIndexInfo(IndexInfoResult info)
    {
        var statsBase = $"{info.TotalFiles} files, {info.TotalChunks} chunks";
        if (info.FailedCount > 0)
            statsBase += $" \u00b7 {info.FailedCount} failed";
        if (!info.IsIndexing
            && info.LastIndexedAt is not null
            && DateTime.TryParse(info.LastIndexedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            statsBase += $" \u00b7 {dt.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
        _vm.StatsText = statsBase;
        _vm.IsPromptStale = info.IsPromptStale;

        if (info.IsIndexing)
        {
            // Clear change-check cache while indexing — results will be stale.
            _vm.ChangesChecked = null;
            _vm.ChangesAdded = 0;
            _vm.ChangesModified = 0;
            _vm.ChangesDeleted = 0;
            _vm.ChangesFailed = 0;

            _vm.IsIndexing = Visibility.Visible;
            _vm.Progress = info is { Current: not null, Total: > 0 }
                ? (double)info.Current.Value / info.Total.Value * 100 : 0;
            var indexingLabel = _loader.GetString("KB_StatusIndexing") ?? "Indexing";
            _vm.StatusText = info.Current is not null
                ? $"{indexingLabel} ({info.Current}/{info.Total})"
                : indexingLabel;
            SetStatusBrush("StatusAccentForegroundBrush");
            return;
        }

        if (info.IsPromptStale)
        {
            _vm.IsIndexing = Visibility.Collapsed;
            _vm.Progress = 0;
            _vm.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
            SetStatusBrush("SystemFillColorCautionBrush");
            return;
        }

        if (info.TotalFiles > 0)
        {
            _vm.IsIndexing = Visibility.Collapsed;
            _vm.Progress = 0;

            // Cached change-check dirty? Show stale status.
            if (_vm.ChangesChecked == true
                && (_vm.ChangesAdded > 0 || _vm.ChangesModified > 0 || _vm.ChangesDeleted > 0))
            {
                _vm.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                SetStatusBrush("SystemFillColorCautionBrush");
            }
            else
            {
                _vm.StatusText = _loader.GetString("KB_StatusReady") ?? "Ready";
                SetStatusBrush("TextFillColorSecondaryBrush");
            }
            return;
        }

        _vm.IsIndexing = Visibility.Collapsed;
        _vm.Progress = 0;
        _vm.StatusText = _loader.GetString("KB_StatusNoIndex") ?? "No index";
        SetStatusBrush("TextFillColorSecondaryBrush");
        _vm.StatsText = "";
    }

    #endregion

    #region Poll Timer

    /// <summary>
    /// Starts the polling timer when the KB is actively indexing; stops
    /// it otherwise. Safe to call repeatedly.
    /// </summary>
    private void SyncPollTimer()
    {
        if (_vm.IsIndexing == Visibility.Visible)
            StartPollTimer();
        else
            StopPollTimer();
    }

    private void StartPollTimer()
    {
        if (_pollTimer is not null) return;
        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPollTimer()
    {
        if (_pollTimer is null) return;
        _pollTimer.Tick -= OnPollTick;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    /// <summary>
    /// Re-pulls status on each poll tick. When the KB transitions from
    /// indexing to idle we auto-run <c>check_changes</c> so the card
    /// picks up any fresh dirt without requiring another click.
    /// </summary>
    private async void OnPollTick(object? sender, object e)
    {
        var wasIndexing = _vm.IsIndexing == Visibility.Visible;
        await RefreshStatusAsync();
        var justFinished = wasIndexing && _vm.IsIndexing == Visibility.Collapsed;

        if (justFinished)
        {
            var result = await KbMcpClient.CheckChangesAsync(_vm.Id);
            if (result is not null)
            {
                _vm.ChangesChecked = true;
                _vm.ChangesAdded = result.Added;
                _vm.ChangesModified = result.Modified;
                _vm.ChangesDeleted = result.Deleted;
                _vm.ChangesFailed = result.Failed;

                if (!result.IsClean)
                {
                    _vm.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
                    SetStatusBrush("SystemFillColorCautionBrush");
                }
            }
        }

        SyncPollTimer();
    }

    #endregion

    #region UI Binding

    /// <summary>
    /// Reflects view-model property changes back onto the visual tree.
    /// Marshalled to the dispatcher because property changes may arrive
    /// from poll-timer ticks running on the ThreadPool.
    /// </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUI);
    }

    /// <summary>
    /// Rebuilds the UI from the current view model. Called on every VM
    /// property change — cheap because it only toggles visibility and
    /// assigns strings.
    /// </summary>
    private void UpdateUI()
    {
        // Simple text assignments — visibility for NameText/SourcePathsText
        // /ModelInfoText is driven by the IsLoading x:Bind in XAML, so we
        // only set Text here and let the binding swap the shimmer in/out.
        NameText.Text = _vm.Name;
        StatusText.Text = _vm.StatusText;
        StatusText.Foreground = _vm.StatusBrush;
        SourcePathsText.Text = _vm.SourcePathsText;
        ModelInfoText.Text = _vm.ModelInfoText;

        StatsText.Text = _vm.StatsText;

        var isIndexing = _vm.IsIndexing == Visibility.Visible;
        var isDeferred = _vm.IsDeferredIndexing && !isIndexing;

        IndexingPanel.Visibility = isIndexing ? Visibility.Visible : Visibility.Collapsed;
        DeferredPanel.Visibility = isDeferred ? Visibility.Visible : Visibility.Collapsed;
        ActionPanel.Visibility = (isIndexing || isDeferred) ? Visibility.Collapsed : Visibility.Visible;

        if (isIndexing)
            IndexProgressBar.Value = _vm.Progress;
        else if (!isDeferred)
            UpdateChangeSummary();
    }

    /// <summary>
    /// Formats the change-check summary line. Hides the row entirely
    /// when the user has not yet triggered a check.
    /// </summary>
    private void UpdateChangeSummary()
    {
        if (_vm.ChangesChecked != true)
        {
            ChangeSummaryText.Visibility = Visibility.Collapsed;
            return;
        }

        ChangeSummaryText.Visibility = Visibility.Visible;

        if (_vm.ChangesAdded == 0 && _vm.ChangesModified == 0
            && _vm.ChangesDeleted == 0 && _vm.ChangesFailed == 0)
        {
            ChangeSummaryText.Text = _loader.GetString("KB_NoChanges") ?? "No changes";
            ChangeSummaryText.Opacity = 0.5;
            SetChangeSummaryBrush("DefaultTextForegroundThemeBrush");
            return;
        }

        var parts = new List<string>();
        if (_vm.ChangesAdded > 0)
            parts.Add($"+ {(_loader.GetString("KB_ChangesAdded") ?? "Added")} {_vm.ChangesAdded}");
        if (_vm.ChangesModified > 0)
            parts.Add($"~ {(_loader.GetString("KB_ChangesModified") ?? "Modified")} {_vm.ChangesModified}");
        if (_vm.ChangesDeleted > 0)
            parts.Add($"- {(_loader.GetString("KB_ChangesDeleted") ?? "Deleted")} {_vm.ChangesDeleted}");
        if (_vm.ChangesFailed > 0)
            parts.Add($"! {(_loader.GetString("KB_ChangesFailed") ?? "Failed")} {_vm.ChangesFailed}");

        ChangeSummaryText.Text = string.Join("    ", parts);
        ChangeSummaryText.Opacity = 1.0;
        SetChangeSummaryBrush("SystemFillColorCautionBrush");
    }

    #endregion

    #region Search

    private static void OnSearchQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KbCard card)
            _ = card.RunSearchAsync((string)e.NewValue);
    }

    /// <summary>
    /// Runs a chunk search against this KB and renders match rows. An
    /// empty query clears the match area. Previous in-flight search is
    /// cancelled on every call.
    /// </summary>
    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();

        if (string.IsNullOrWhiteSpace(query) || _vm.Id.Length == 0)
        {
            ClearMatches();
            return;
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        ShowMatchShimmer();

        try
        {
            var hits = await _searchService
                .SearchAsync(_vm.Id, query, MaxChunksPerCard, RagReadyTask, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested) return;
            RenderMatches(hits);
        }
        catch (OperationCanceledException) { }
        catch
        {
            RenderMatches([]);
        }
    }

    /// <summary>
    /// Shows the declarative match-shimmer panel while a search is
    /// pending. The actual Shimmer elements live in XAML — this just
    /// toggles visibility and clears any previous results.
    /// </summary>
    private void ShowMatchShimmer()
    {
        MatchPanel.Visibility = Visibility.Visible;
        IsSearching = true;
        NoMatchText.Visibility = Visibility.Collapsed;
        MatchResultsPanel.Children.Clear();
    }

    /// <summary>
    /// Hides the shimmer and renders match rows. An empty hit list
    /// switches to the "no matches" caption instead.
    /// </summary>
    private void RenderMatches(IReadOnlyList<ChunkMatchViewModel> hits)
    {
        IsSearching = false;
        MatchResultsPanel.Children.Clear();

        if (hits.Count == 0)
        {
            MatchPanel.Visibility = Visibility.Visible;
            NoMatchText.Visibility = Visibility.Visible;
        }
        else
        {
            MatchPanel.Visibility = Visibility.Visible;
            NoMatchText.Visibility = Visibility.Collapsed;

            foreach (var hit in hits)
                MatchResultsPanel.Children.Add(BuildMatchRow(hit));
        }

        LastMatchCount = hits.Count;
        MatchCountChanged?.Invoke(this, hits.Count);
    }

    /// <summary>
    /// Hides the entire match area. Called when the page's search query
    /// clears out, so stale match rows don't linger on idle cards.
    /// </summary>
    private void ClearMatches()
    {
        MatchPanel.Visibility = Visibility.Collapsed;
        IsSearching = false;
        NoMatchText.Visibility = Visibility.Collapsed;
        MatchResultsPanel.Children.Clear();

        if (LastMatchCount != -1)
        {
            LastMatchCount = -1;
            MatchCountChanged?.Invoke(this, -1);
        }
    }

    /// <summary>
    /// Builds one match row: icon + source name + snippet + score. Done
    /// in code because the row count is dynamic per hit.
    /// </summary>
    private static Grid BuildMatchRow(ChunkMatchViewModel hit)
    {
        var row = new Grid { Padding = new Thickness(0, 3, 0, 3), ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = "\uE8A5",
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        row.Children.Add(icon);

        var textPanel = new StackPanel { Spacing = 1 };
        textPanel.Children.Add(new TextBlock
        {
            Text = hit.SourceName,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = hit.Snippet,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        var scoreText = new TextBlock
        {
            Text = hit.ScoreText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetColumn(scoreText, 2);
        row.Children.Add(scoreText);

        return row;
    }

    #endregion

    #region Button Handlers

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) DeleteRequested?.Invoke(this, _vm.Id);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) SettingsRequested?.Invoke(this, _vm.Id);
    }

    private void OnReindexNowClicked(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) ReindexRequested?.Invoke(this, _vm.Id);
    }

    private void OnReindexDeferredClicked(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) ReindexScheduledRequested?.Invoke(this, _vm.Id);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) CancelIndexRequested?.Invoke(this, _vm.Id);
    }

    private void CheckChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) CheckChangesRequested?.Invoke(this, _vm.Id);
    }

    private void IndexNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) IndexNowRequested?.Invoke(this, _vm.Id);
    }

    private void CancelDeferredButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Id.Length > 0) CancelDeferredRequested?.Invoke(this, _vm.Id);
    }

    #endregion

    #region Page-facing Mutators

    /// <summary>
    /// Applies the result of a page-initiated <c>check_changes</c> call
    /// to this card's view model. The page keeps ownership of the MCP
    /// call for that click so one shared error dialog stays in charge;
    /// the card only needs the parsed values.
    /// </summary>
    public void ApplyChangeCheckResult(ChangeCheckResult result)
    {
        _vm.ChangesChecked = true;
        _vm.ChangesAdded = result.Added;
        _vm.ChangesModified = result.Modified;
        _vm.ChangesDeleted = result.Deleted;
        _vm.ChangesFailed = result.Failed;

        if (!result.IsClean)
        {
            _vm.StatusText = _loader.GetString("KB_StatusStale") ?? "Prompt stale";
            SetStatusBrush("SystemFillColorCautionBrush");
        }
    }

    #endregion

    #region Theme

    /// <summary>
    /// Stores the status brush resource key and resolves the current theme's brush.
    /// </summary>
    private void SetStatusBrush(string resourceKey)
    {
        _statusBrushKey = resourceKey;
        _vm.StatusBrush = ThemeHelper.GetBrush(resourceKey);
    }

    /// <summary>
    /// Stores the change-summary brush resource key and resolves the current theme's brush.
    /// </summary>
    private void SetChangeSummaryBrush(string resourceKey)
    {
        _changeSummaryBrushKey = resourceKey;
        ChangeSummaryText.Foreground = ThemeHelper.GetBrush(resourceKey);
    }

    /// <summary>
    /// Reapplies code-assigned brushes after a runtime theme switch.
    /// </summary>
    private void RefreshThemeBrushes()
    {
        _vm.StatusBrush = ThemeHelper.GetBrush(_statusBrushKey);
        ChangeSummaryText.Foreground = ThemeHelper.GetBrush(_changeSummaryBrushKey);
    }

    #endregion
}
