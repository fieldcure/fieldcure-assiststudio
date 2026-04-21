using System.Collections.ObjectModel;
using System.ComponentModel;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A flyout that displays the list of tool/server toggles for the <see cref="ComposeBar"/>.
/// Internally uses an <see cref="ItemsControl"/> bound to an <see cref="ObservableCollection{T}"/>
/// so the item list re-renders via data binding rather than a full rebuild on each change.
/// </summary>
internal sealed class ToolSelectionFlyout : Flyout
{
    #region Fields

    /// <summary>Resource loader for localized strings in this library.</summary>
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

    /// <summary>Backing collection for the tool rows; bound to the internal <see cref="ItemsControl"/>.</summary>
    private readonly ObservableCollection<ToolToggleItem> _items = [];

    /// <summary>Root content panel hosting either the list or the empty-state text.</summary>
    private readonly StackPanel _rootPanel;

    /// <summary>The items control hosting tool toggle rows.</summary>
    private readonly ItemsControl _itemsControl;

    /// <summary>The "Select all / Deselect all" toggle hyperlink shown at the bottom of the flyout.</summary>
    private readonly HyperlinkButton _toggleAllLink;

    /// <summary>The rectangle separator between the list and the toggle-all footer.</summary>
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _separator;

    /// <summary>The footer stack panel containing separator and toggle-all link.</summary>
    private readonly StackPanel _footerPanel;

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
    /// Initializes a new instance of the <see cref="ToolSelectionFlyout"/> class and builds
    /// its content tree. The item template is defined inline via XAML to enable two-way
    /// binding on the checkbox <c>IsChecked</c> state.
    /// </summary>
    public ToolSelectionFlyout()
    {
        Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;

        _selectAllLabel = Res.GetString("ComposeBar_SelectAll") is { Length: > 0 } s
            ? s : "Select all";
        _deselectAllLabel = Res.GetString("ComposeBar_DeselectAll") is { Length: > 0 } d
            ? d : "Deselect all";

        // Inline DataTemplate defined via XamlReader so that IsEnabled/IsChecked can bind
        // to ToolToggleItem — binding-driven rendering avoids rebuilding the entire flyout
        // whenever AvailableTools changes.
        var template = (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <CheckBox Content="{Binding DisplayName}"
                          IsChecked="{Binding IsEnabled, Mode=TwoWay}"
                          MinWidth="0" />
            </DataTemplate>
            """);

        _itemsControl = new ItemsControl
        {
            ItemTemplate = template,
            ItemsSource = _items,
        };

        var listPanel = new StackPanel { Spacing = 4 };
        listPanel.Children.Add(_itemsControl);

        _separator = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(0, 2, 0, 2),
        };

        _toggleAllLink = new HyperlinkButton
        {
            Content = _selectAllLabel,
            Padding = new Thickness(0),
            FontSize = 12,
        };
        _toggleAllLink.Click += OnToggleAllClick;

        _footerPanel = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(4, 0, 4, 4),
        };
        _footerPanel.Children.Add(_separator);
        _footerPanel.Children.Add(_toggleAllLink);

        var scroll = new ScrollViewer
        {
            Content = listPanel,
            MaxHeight = 400,
            Padding = new Thickness(0, 0, 8, 0),
        };

        _rootPanel = new StackPanel { Padding = new Thickness(4) };
        _rootPanel.Children.Add(scroll);
        _rootPanel.Children.Add(_footerPanel);

        Content = _rootPanel;
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

        var emptyPanel = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(8),
            MaxWidth = 240,
        };
        emptyPanel.Children.Add(new TextBlock
        {
            Text = emptyText,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            FontSize = 12,
        });

        Content = emptyPanel;
    }

    /// <summary>
    /// Ensures the flyout <see cref="Flyout.Content"/> is the list-layout root panel
    /// (reverting from the empty-state content).
    /// </summary>
    private void RestoreListLayout()
    {
        if (!ReferenceEquals(Content, _rootPanel))
            Content = _rootPanel;
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
internal sealed class ToolToggleItem : INotifyPropertyChanged
{
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
