using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AssistStudio.Mcp;
using AssistStudio.Settings;
using System.Text.Json;

namespace AssistStudio.Controls;

/// <summary>
/// Card that renders one persistent memory entry and raises a delete request
/// so the page owns the Essentials MCP side effect.
/// </summary>
public sealed partial class MemoryEntryCard : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Item"/> dependency property.</summary>
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(MemoryEntryItemViewModel),
            typeof(MemoryEntryCard),
            new PropertyMetadata(null, OnItemChanged));

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="MemoryEntryCard"/>.</summary>
    public MemoryEntryCard()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>Memory entry currently rendered by this card.</summary>
    public MemoryEntryItemViewModel? Item
    {
        get => (MemoryEntryItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    #endregion

    #region Private Methods

    /// <summary>Refreshes x:Bind expressions when the bound item changes.</summary>
    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MemoryEntryCard card)
            card.Bindings.Update();
    }

    /// <summary>
    /// Shows either the delete affordance or the inline progress ring based on
    /// whether the card is currently deleting.
    /// </summary>
    private void SetDeleteBusy(bool busy)
    {
        DeleteButton.IsEnabled = !busy;
        DeleteButton.Visibility = busy ? Visibility.Collapsed : DeleteButton.Visibility;
        DeleteProgressRing.IsActive = busy;
        DeleteProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Event Handlers

    /// <summary>Reveals the delete button when the pointer enters the card.</summary>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (DeleteProgressRing.Visibility == Visibility.Visible) return;
        DeleteButton.Visibility = Visibility.Visible;
    }

    /// <summary>Hides the delete button when the pointer leaves the card.</summary>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (DeleteProgressRing.Visibility == Visibility.Visible) return;
        DeleteButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Deletes the current memory entry via the Essentials MCP server, then
    /// marks the bound item as deleted so the page collection can update.
    /// </summary>
    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Item?.Key))
            return;

        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null || !conn.IsConnected)
            return;

        SetDeleteBusy(true);
        try
        {
            var args = JsonDocument.Parse(JsonSerializer.Serialize(new { key = Item.Key })).RootElement;
            await conn.CallToolWithProgressAsync("forget", args, null, CancellationToken.None);
            Item.MarkDeleted();
        }
        catch
        {
            SetDeleteBusy(false);
        }
    }

    #endregion
}
