using System.ComponentModel;
using AssistStudio.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Card control that displays a knowledge base item with status, stats, and action buttons.
/// </summary>
public sealed partial class KbCard : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="ViewModel"/> dependency property.</summary>
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(KbViewModel),
            typeof(KbCard),
            new PropertyMetadata(null, OnViewModelChanged));

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

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private readonly KbSearchService _searchService = new();
    private CancellationTokenSource? _searchCts;

    #endregion

    #region Events

    /// <summary>Raised when the user clicks the delete button.</summary>
    public event EventHandler<string>? DeleteRequested;

    /// <summary>Raised when the user clicks the settings button.</summary>
    public event EventHandler<string>? SettingsRequested;

    /// <summary>Raised when the user clicks the re-index button.</summary>
    public event EventHandler<string>? ReindexRequested;

    /// <summary>Raised when the user clicks the cancel/stop button.</summary>
    public event EventHandler<string>? CancelIndexRequested;

    /// <summary>Raised when the user clicks the check changes button.</summary>
    public event EventHandler<string>? CheckChangesRequested;

    /// <summary>Raised when a search completes or clears. Value is match count (-1 = cleared).</summary>
    public event EventHandler<int>? MatchCountChanged;

    #endregion

    #region Constructor

    public KbCard()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the knowledge base view model to display.
    /// </summary>
    public KbViewModel? ViewModel
    {
        get => (KbViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Gets or sets the current search query. Non-empty triggers a chunk search.
    /// </summary>
    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    /// <summary>
    /// Cached Task from <c>KnowledgeBaseService.EnsureConnectedAsync</c>.
    /// Injected by the page so the search can wait for RAG serve startup.
    /// </summary>
    public Task? RagReadyTask { get; set; }

    /// <summary>Most recent match count. -1 = not searched or cleared.</summary>
    public int LastMatchCount { get; private set; } = -1;

    #endregion

    #region Private Methods

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KbCard card) return;

        if (e.OldValue is KbViewModel oldVm)
            oldVm.PropertyChanged -= card.OnViewModelPropertyChanged;

        if (e.NewValue is KbViewModel newVm)
            newVm.PropertyChanged += card.OnViewModelPropertyChanged;

        card.UpdateUI();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUI);
    }

    private void UpdateUI()
    {
        var vm = ViewModel;
        if (vm is null) return;

        // Name + Status
        NameText.Text = vm.Name;
        StatusText.Text = vm.StatusText;
        StatusText.Foreground = vm.StatusBrush;

        // Stats
        if (!string.IsNullOrEmpty(vm.StatsText))
        {
            StatsText.Text = vm.StatsText;
            StatsText.Visibility = Visibility.Visible;
        }
        else
        {
            StatsText.Visibility = Visibility.Collapsed;
        }

        // Source paths
        SourcePathsText.Text = vm.SourcePathsText;

        // Model info
        if (!string.IsNullOrEmpty(vm.ModelInfoText))
        {
            ModelInfoText.Text = vm.ModelInfoText;
            ModelInfoText.Visibility = Visibility.Visible;
        }
        else
        {
            ModelInfoText.Visibility = Visibility.Collapsed;
        }

        // Model warning
        if (!string.IsNullOrEmpty(vm.ModelWarningText))
        {
            ModelWarningText.Text = vm.ModelWarningText;
            ModelWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            ModelWarningText.Visibility = Visibility.Collapsed;
        }

        // Indexing vs idle state
        var isIndexing = vm.IsIndexing == Visibility.Visible;
        IndexingPanel.Visibility = isIndexing ? Visibility.Visible : Visibility.Collapsed;
        ActionPanel.Visibility = isIndexing ? Visibility.Collapsed : Visibility.Visible;

        if (isIndexing)
        {
            IndexProgressBar.Value = vm.Progress;
            SetMouseToolTip(StopButton, _loader.GetString("KB_CancelIndexing") ?? "Cancel indexing");
        }
        else
        {
            UpdateChangeSummary(vm);
            UpdateActionButtons(vm);
        }
    }

    private void UpdateChangeSummary(KbViewModel vm)
    {
        if (vm.ChangesChecked != true)
        {
            ChangeSummaryText.Visibility = Visibility.Collapsed;
            return;
        }

        ChangeSummaryText.Visibility = Visibility.Visible;

        if (vm.ChangesAdded == 0 && vm.ChangesModified == 0
            && vm.ChangesDeleted == 0 && vm.ChangesFailed == 0)
        {
            ChangeSummaryText.Text = _loader.GetString("KB_NoChanges") ?? "No changes";
            ChangeSummaryText.Opacity = 0.5;
            ChangeSummaryText.Foreground = (Brush)Application.Current.Resources["DefaultTextForegroundThemeBrush"];
        }
        else
        {
            var parts = new List<string>();
            if (vm.ChangesAdded > 0)
                parts.Add($"+ {(_loader.GetString("KB_ChangesAdded") ?? "Added")} {vm.ChangesAdded}");
            if (vm.ChangesModified > 0)
                parts.Add($"~ {(_loader.GetString("KB_ChangesModified") ?? "Modified")} {vm.ChangesModified}");
            if (vm.ChangesDeleted > 0)
                parts.Add($"- {(_loader.GetString("KB_ChangesDeleted") ?? "Deleted")} {vm.ChangesDeleted}");
            if (vm.ChangesFailed > 0)
                parts.Add($"! {(_loader.GetString("KB_ChangesFailed") ?? "Failed")} {vm.ChangesFailed}");

            ChangeSummaryText.Text = string.Join("    ", parts);
            ChangeSummaryText.Opacity = 1.0;
            ChangeSummaryText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        }
    }

    private void UpdateActionButtons(KbViewModel vm)
    {
        CheckChangesButton.Content = _loader.GetString("KB_CheckChanges") ?? "Check changes";

        SetMouseToolTip(SettingsButton, _loader.GetString("KB_Settings") ?? "Settings");
        SetMouseToolTip(ReindexButton, _loader.GetString("KB_Reindex/Content") ?? "Re-index");
        SetMouseToolTip(DeleteButton, _loader.GetString("KB_Delete/Content") ?? "Delete");

        ReindexButton.IsEnabled = string.IsNullOrEmpty(vm.ModelWarningText);
    }

    private static void SetMouseToolTip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, new ToolTip
        {
            Content = text,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
        });
    }

    #endregion

    #region Search

    private static void OnSearchQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KbCard card)
            _ = card.RunSearchAsync((string)e.NewValue);
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();

        if (string.IsNullOrWhiteSpace(query) || ViewModel is null)
        {
            ClearMatches();
            return;
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        ShowShimmer();

        try
        {
            var hits = await _searchService
                .SearchAsync(ViewModel.Id, query, MaxChunksPerCard, RagReadyTask, token)
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

    private void ShowShimmer()
    {
        MatchPanel.Visibility = Visibility.Visible;
        MatchShimmerPanel.Visibility = Visibility.Visible;
        MatchShimmerPanel.Children.Clear();
        MatchShimmerPanel.Children.Add(new Shimmer { Height = 14, Width = 280, HorizontalAlignment = HorizontalAlignment.Left });
        MatchShimmerPanel.Children.Add(new Shimmer { Height = 14, Width = 240, HorizontalAlignment = HorizontalAlignment.Left });
        MatchShimmerPanel.Children.Add(new Shimmer { Height = 14, Width = 180, HorizontalAlignment = HorizontalAlignment.Left });
        NoMatchText.Visibility = Visibility.Collapsed;
        MatchResultsPanel.Children.Clear();
    }

    private void RenderMatches(IReadOnlyList<ChunkMatchViewModel> hits)
    {
        MatchShimmerPanel.Visibility = Visibility.Collapsed;
        MatchResultsPanel.Children.Clear();

        if (hits.Count == 0)
        {
            MatchPanel.Visibility = Visibility.Visible;
            NoMatchText.Text = _loader.GetString("KB_NoMatches") ?? "No matches in this knowledge base";
            NoMatchText.Visibility = Visibility.Visible;
        }
        else
        {
            MatchPanel.Visibility = Visibility.Visible;
            NoMatchText.Visibility = Visibility.Collapsed;

            foreach (var hit in hits)
            {
                var row = new Grid { Padding = new Thickness(0, 3, 0, 3), ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon { Glyph = "\uE8A5", FontSize = 12, Opacity = 0.6 };
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 2, 0, 0);
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

                MatchResultsPanel.Children.Add(row);
            }
        }

        LastMatchCount = hits.Count;
        MatchCountChanged?.Invoke(this, hits.Count);
    }

    private void ClearMatches()
    {
        MatchPanel.Visibility = Visibility.Collapsed;
        MatchShimmerPanel.Visibility = Visibility.Collapsed;
        NoMatchText.Visibility = Visibility.Collapsed;
        MatchResultsPanel.Children.Clear();

        if (LastMatchCount != -1)
        {
            LastMatchCount = -1;
            MatchCountChanged?.Invoke(this, -1);
        }
    }

    #endregion

    #region Event Handlers

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            DeleteRequested?.Invoke(this, ViewModel.Id);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            SettingsRequested?.Invoke(this, ViewModel.Id);
    }

    private void ReindexButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ReindexRequested?.Invoke(this, ViewModel.Id);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            CancelIndexRequested?.Invoke(this, ViewModel.Id);
    }

    private void CheckChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            CheckChangesRequested?.Invoke(this, ViewModel.Id);
    }

    #endregion
}
