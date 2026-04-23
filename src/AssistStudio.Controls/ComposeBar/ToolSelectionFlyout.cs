using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A flyout that displays the list of tool/server toggles for the <see cref="ComposeBar"/>.
/// Internally uses an <see cref="ItemsControl"/> bound to an <see cref="ObservableCollection{T}"/>
/// so the item list re-renders via data binding rather than a full rebuild on each change.
/// </summary>
internal sealed partial class ToolSelectionFlyout : Flyout
{
    #region Fields

    /// <summary>Resource loader for localized strings in this library.</summary>
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

    /// <summary>Backing collection for the tool rows; bound to the internal <see cref="ItemsControl"/>.</summary>
    private readonly ObservableCollection<ToolToggleItem> _items = [];

    /// <summary>The XAML view hosting the flyout UI.</summary>
    private readonly ToolSelectionFlyoutContent _contentView;

    /// <summary>The items control hosting tool toggle rows.</summary>
    private readonly ItemsControl _itemsControl;

    /// <summary>The "Select all / Deselect all" toggle hyperlink shown at the bottom of the flyout.</summary>
    private readonly HyperlinkButton _toggleAllLink;

    /// <summary>Localized "Select all" label.</summary>
    private readonly string _selectAllLabel;

    /// <summary>Localized "Deselect all" label.</summary>
    private readonly string _deselectAllLabel;

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user toggles any tool's checkbox or uses the select/deselect-all link.
    /// The argument contains the current set of enabled tool names, or <c>null</c> when all
    /// tools are currently selected (the caller should interpret <c>null</c> as "all enabled").
    /// </summary>
    public event EventHandler<IReadOnlyList<string>?>? SelectionChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSelectionFlyout"/> class.
    /// </summary>
    public ToolSelectionFlyout()
    {
        Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;

        _selectAllLabel = Res.GetString("ComposeBar_SelectAll") is { Length: > 0 } s
            ? s : "Select all";
        _deselectAllLabel = Res.GetString("ComposeBar_DeselectAll") is { Length: > 0 } d
            ? d : "Deselect all";

        _contentView = new ToolSelectionFlyoutContent();
        _itemsControl = _contentView.ItemsControl;
        _itemsControl.ItemsSource = _items;

        _toggleAllLink = _contentView.ToggleAllLink;
        _toggleAllLink.Content = _selectAllLabel;
        _toggleAllLink.Click += OnToggleAllClick;
        Content = _contentView;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Rebuilds the items list from the given tool collection and enabled-name set.
    /// If the tool list is empty, the flyout switches to an empty-state message.
    /// </summary>
    /// <param name="tools">The tools available for toggling (already filtered to server placeholders).</param>
    /// <param name="enabledNames">The currently enabled tool names, or <c>null</c> to treat all as enabled.</param>
    public void SetItems(IReadOnlyList<IAssistTool> tools, IReadOnlyList<string>? enabledNames)
    {
        // Detach change notifications during bulk update to avoid spamming SelectionChanged.
        foreach (var item in _items)
            item.PropertyChanged -= OnItemPropertyChanged;
        _items.Clear();

        if (tools.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        // Restore the normal layout if the empty state was shown previously.
        RestoreListLayout();

        var enabledSet = enabledNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            var item = new ToolToggleItem(tool.Name, tool.DisplayName,
                enabledSet is null || enabledSet.Contains(tool.Name));
            item.PropertyChanged += OnItemPropertyChanged;
            _items.Add(item);
        }

        UpdateToggleAllLabel();
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Replaces the content with an empty-state message explaining how to enable tools.
    /// </summary>
    private void ShowEmptyState()
    {
        var emptyText = Res.GetString("ComposeBar_NoToolsEnabled") is { Length: > 0 } t
            ? t
            : "No tools or servers enabled.\nAdd them in Profile settings.";
        _contentView.ShowEmptyState(emptyText);
    }

    /// <summary>
    /// Ensures the flyout <see cref="Flyout.Content"/> is the list-layout root panel
    /// (reverting from the empty-state content).
    /// </summary>
    private void RestoreListLayout()
    {
        _contentView.ShowListLayout();
    }

    /// <summary>
    /// Updates the toggle-all hyperlink label based on whether every item is currently enabled.
    /// </summary>
    private void UpdateToggleAllLabel()
    {
        var allChecked = _items.Count > 0 && _items.All(i => i.IsEnabled);
        _toggleAllLink.Content = allChecked ? _deselectAllLabel : _selectAllLabel;
    }

    /// <summary>
    /// Handles the "Select all / Deselect all" click by flipping every item's enabled state.
    /// </summary>
    private void OnToggleAllClick(object sender, RoutedEventArgs e)
    {
        var allChecked = _items.All(i => i.IsEnabled);
        foreach (var item in _items)
            item.IsEnabled = !allChecked;
    }

    /// <summary>
    /// Reacts to per-item enable/disable toggles by updating the footer label and raising
    /// <see cref="SelectionChanged"/> with the computed enabled-names list.
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolToggleItem.IsEnabled)) return;

        UpdateToggleAllLabel();

        var total = _items.Count;
        var enabledNames = _items.Where(i => i.IsEnabled).Select(i => i.Name).ToArray();
        IReadOnlyList<string>? result = enabledNames.Length == total ? null : enabledNames;
        SelectionChanged?.Invoke(this, result);
    }

    #endregion
}

/// <summary>
/// View model for a single tool toggle row in the <see cref="ToolSelectionFlyout"/>.
/// Implements <see cref="INotifyPropertyChanged"/> so the bound checkbox reflects programmatic changes.
/// </summary>
internal sealed partial class ToolToggleItem : INotifyPropertyChanged
{
    /// <summary>
    /// Backing field for the current enabled state.
    /// </summary>
    private bool _isEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolToggleItem"/> class.
    /// </summary>
    /// <param name="name">The tool's technical identifier.</param>
    /// <param name="displayName">The tool's localized display name.</param>
    /// <param name="isEnabled">The initial enabled state.</param>
    public ToolToggleItem(string name, string displayName, bool isEnabled)
    {
        Name = name;
        DisplayName = displayName;
        _isEnabled = isEnabled;
    }

    /// <summary>Gets the tool's technical identifier (non-localized).</summary>
    public string Name { get; }

    /// <summary>Gets the tool's human-readable display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets or sets whether this tool is currently enabled.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}
