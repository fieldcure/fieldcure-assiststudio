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

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();

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
