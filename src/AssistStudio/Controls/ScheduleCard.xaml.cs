using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Card that renders one scheduled task from the Runner database. Takes
/// a <see cref="ScheduleItem"/> via <see cref="Item"/> and raises
/// <see cref="DeleteRequested"/> / <see cref="ToggleRequested"/> so the
/// page owns the SQLite + schtasks side effects.
/// </summary>
public sealed partial class ScheduleCard : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Item"/> dependency property.</summary>
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(ScheduleItem),
            typeof(ScheduleCard),
            new PropertyMetadata(null, OnItemChanged));

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private bool _suppressToggledEvent;

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user clicks the delete button. The argument is the
    /// task id from <see cref="ScheduleItem.Id"/>.
    /// </summary>
    public event EventHandler<string>? DeleteRequested;

    /// <summary>
    /// Raised when the user flips the enable/disable toggle. Tuple is the
    /// task id and the new <c>IsOn</c> state.
    /// </summary>
    public event EventHandler<(string TaskId, bool IsOn)>? ToggleRequested;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="ScheduleCard"/>.</summary>
    public ScheduleCard()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The scheduled task this card represents. Setting it repaints the
    /// info column and resyncs the toggle state without firing the
    /// <see cref="ToggleSwitch.Toggled"/> event.
    /// </summary>
    public ScheduleItem? Item
    {
        get => (ScheduleItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Task id this card is bound to, or empty if unbound.</summary>
    public string TaskId => Item?.Id ?? "";

    /// <summary>
    /// <c>true</c> when the user-facing toggle should be disabled — the
    /// underlying schedule was a one-time run whose moment has passed.
    /// </summary>
    private static bool IsCompletedOneTime(ScheduleItem item) =>
        item.ScheduleOnce.HasValue && item.ScheduleOnce.Value.ToLocalTime() <= DateTimeOffset.Now;

    #endregion

    #region Item Rendering

    /// <summary>
    /// Re-renders all card visuals when <see cref="Item"/> is reassigned.
    /// </summary>
    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScheduleCard card)
            card.ApplyItem();
    }

    /// <summary>
    /// Applies the current <see cref="Item"/> values onto the visual
    /// tree. Suppresses the toggle's <c>Toggled</c> event while syncing
    /// state so callers can rebind without re-entrancy.
    /// </summary>
    private void ApplyItem()
    {
        var item = Item;
        if (item is null)
        {
            NameText.Text = "";
            DescriptionText.Visibility = Visibility.Collapsed;
            DetailText.Visibility = Visibility.Collapsed;
            return;
        }

        NameText.Text = item.Name;

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            DescriptionText.Text = item.Description;
            DescriptionText.Visibility = Visibility.Visible;
        }
        else
        {
            DescriptionText.Visibility = Visibility.Collapsed;
        }

        var detail = BuildDetailText(item);
        if (!string.IsNullOrEmpty(detail))
        {
            DetailText.Text = detail;
            DetailText.Visibility = Visibility.Visible;
        }
        else
        {
            DetailText.Visibility = Visibility.Collapsed;
        }

        var completedOnce = IsCompletedOneTime(item);

        _suppressToggledEvent = true;
        try
        {
            EnabledToggle.IsOn = item.IsEnabled;
        }
        finally
        {
            _suppressToggledEvent = false;
        }

        EnabledToggle.IsEnabled = !completedOnce;
        UpdateToggleTooltip(completedOnce, item.IsEnabled);
    }

    /// <summary>
    /// Builds the single caption line under the description: human-readable
    /// schedule (cron or one-time timestamp) and, when present, the output
    /// channel the task writes to.
    /// </summary>
    private static string BuildDetailText(ScheduleItem item)
    {
        var parts = new List<string>();

        if (item.ScheduleOnce.HasValue)
        {
            var local = item.ScheduleOnce.Value.ToLocalTime();
            var suffix = local <= DateTimeOffset.Now ? " (\uc644\ub8cc)" : " (1\ud68c)";
            parts.Add($"{local:yyyy-MM-dd HH:mm}{suffix}");
        }
        else if (!string.IsNullOrWhiteSpace(item.Schedule))
        {
            parts.Add(ScheduleHelper.DescribeCron(item.Schedule));
        }

        if (!string.IsNullOrWhiteSpace(item.OutputChannel))
            parts.Add($"\u2192 {item.OutputChannel}");

        return string.Join(" \u00B7 ", parts);
    }

    /// <summary>
    /// Refreshes the toggle's tooltip based on its current state and
    /// whether it's disabled (completed one-time).
    /// </summary>
    private void UpdateToggleTooltip(bool completedOnce, bool isOn)
    {
        string tip;
        if (completedOnce)
            tip = "";
        else if (isOn)
            tip = _loader.GetString("Schedule_DisableTooltip") ?? "Disable";
        else
            tip = _loader.GetString("Schedule_EnableTooltip") ?? "Enable";

        ToolTipService.SetToolTip(EnabledToggle, tip);
        ToolTipService.SetPlacement(EnabledToggle, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
    }

    #endregion

    #region Page-facing Mutators

    /// <summary>
    /// Reverts the toggle visual state without firing <see cref="ToggleRequested"/>.
    /// Called by the page when the backend refuses the state change so the
    /// card reflects reality instead of the user's failed intent.
    /// </summary>
    public void RevertToggle(bool actualIsOn)
    {
        _suppressToggledEvent = true;
        try
        {
            EnabledToggle.IsOn = actualIsOn;
        }
        finally
        {
            _suppressToggledEvent = false;
        }

        if (Item is not null)
            UpdateToggleTooltip(IsCompletedOneTime(Item), actualIsOn);
    }

    /// <summary>
    /// Enables or disables interaction on the toggle while the page is
    /// processing the request. Prevents double-clicks during the
    /// async SQLite + schtasks round-trip.
    /// </summary>
    public void SetToggleBusy(bool busy)
    {
        var completedOnce = Item is not null && IsCompletedOneTime(Item);
        EnabledToggle.IsEnabled = !busy && !completedOnce;
    }

    #endregion

    #region Event Handlers

    /// <summary>Reveals the delete button when the pointer enters the card.</summary>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Visible;
    }

    /// <summary>Hides the delete button when the pointer leaves the card.</summary>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Raises <see cref="ToggleRequested"/> with the new state unless the
    /// event is being suppressed during programmatic re-sync.
    /// </summary>
    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggledEvent) return;
        if (Item is null) return;
        ToggleRequested?.Invoke(this, (Item.Id, EnabledToggle.IsOn));
    }

    /// <summary>Raises <see cref="DeleteRequested"/> with the current task id.</summary>
    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (Item is not null)
            DeleteRequested?.Invoke(this, Item.Id);
    }

    #endregion
}
