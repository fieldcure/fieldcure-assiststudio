using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Card that renders one scheduled task from the Runner database and owns
/// its own toggle/delete side effects.
/// </summary>
public sealed partial class ScheduleCard : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Item"/> dependency property.</summary>
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(ScheduleItemViewModel),
            typeof(ScheduleCard),
            new PropertyMetadata(null, OnItemChanged));

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private bool _suppressToggledEvent;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="ScheduleCard"/>.</summary>
    public ScheduleCard()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>The scheduled task this card represents.</summary>
    public ScheduleItemViewModel? Item
    {
        get => (ScheduleItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>
    /// <c>true</c> when the underlying schedule was a one-time run whose
    /// moment has already passed.
    /// </summary>
    private static bool IsCompletedOneTime(ScheduleItemViewModel item) =>
        item.ScheduleOnce.HasValue && item.ScheduleOnce.Value.ToLocalTime() <= DateTimeOffset.Now;

    #endregion

    #region Item Rendering

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScheduleCard card)
            card.ApplyItem();
    }

    private void ApplyItem()
    {
        var item = Item;
        if (item is null)
        {
            DescriptionText.Visibility = Visibility.Collapsed;
            DetailText.Visibility = Visibility.Collapsed;
            return;
        }

        DescriptionText.Visibility = string.IsNullOrWhiteSpace(item.Description)
            ? Visibility.Collapsed
            : Visibility.Visible;

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

    private static string BuildDetailText(ScheduleItemViewModel item)
    {
        var parts = new List<string>();

        if (item.ScheduleOnce.HasValue)
        {
            var local = item.ScheduleOnce.Value.ToLocalTime();
            var suffix = local <= DateTimeOffset.Now ? " (완료)" : " (1회)";
            parts.Add($"{local:yyyy-MM-dd HH:mm}{suffix}");
        }
        else if (!string.IsNullOrWhiteSpace(item.Schedule))
        {
            parts.Add(ScheduleHelper.DescribeCron(item.Schedule));
        }

        if (!string.IsNullOrWhiteSpace(item.OutputChannel))
            parts.Add($"→ {item.OutputChannel}");

        return string.Join(" · ", parts);
    }

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

    #region Event Handlers

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Visible;
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Collapsed;
    }

    private async void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggledEvent || Item is null)
            return;

        var requestedState = EnabledToggle.IsOn;
        var completedOnce = IsCompletedOneTime(Item);
        EnabledToggle.IsEnabled = false;

        try
        {
            var (success, _) = await ScheduleHelper.SetEnabledAsync(Item.Id, requestedState);
            if (success)
            {
                Item.SetEnabled(requestedState);
                UpdateToggleTooltip(completedOnce, requestedState);
                return;
            }

            _suppressToggledEvent = true;
            EnabledToggle.IsOn = Item.IsEnabled;
            _suppressToggledEvent = false;
            UpdateToggleTooltip(completedOnce, Item.IsEnabled);
        }
        finally
        {
            EnabledToggle.IsEnabled = !completedOnce;
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (Item is null)
            return;

        var dialog = new ThemedContentDialog
        {
            Title = _loader.GetString("Schedule_DeleteConfirmTitle") ?? "Delete Schedule",
            Content = string.Format(
                _loader.GetString("Schedule_DeleteConfirmContent") ?? "Are you sure you want to delete \"{0}\"?",
                Item.Name),
            PrimaryButtonText = _loader.GetString("Schedule_DeleteTooltip") ?? "Delete",
            CloseButtonText = _loader.GetString("Common_Cancel") ?? "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        DeleteButton.IsEnabled = false;
        try
        {
            var (success, _) = await ScheduleHelper.DeleteAsync(Item.Id);
            if (success)
                Item.MarkDeleted();
        }
        finally
        {
            DeleteButton.IsEnabled = true;
        }
    }

    #endregion
}
