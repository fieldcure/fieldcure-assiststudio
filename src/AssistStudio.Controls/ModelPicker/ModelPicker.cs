using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.System;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Compact picker presenting a searchable, optionally-grouped list of
/// <see cref="ModelPickerEntry"/> items in a flyout. The button label shows
/// the current selection; the flyout opens above the button (ComposeBar
/// lives at the bottom of ChatPanel).
/// </summary>
[TemplatePart(Name = PartRootButton, Type = typeof(Button))]
[TemplatePart(Name = PartLabelText, Type = typeof(TextBlock))]
[TemplatePart(Name = PartSearchBox, Type = typeof(AutoSuggestBox))]
[TemplatePart(Name = PartList, Type = typeof(ListView))]
public sealed class ModelPicker : Control
{
    private const string PartRootButton = "PART_RootButton";
    private const string PartLabelText = "PART_LabelText";
    private const string PartSearchBox = "PART_SearchBox";
    private const string PartList = "PART_List";

    /// <summary>
    /// Resource loader for localized strings used by this control library.
    /// </summary>
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

    #region Fields

    /// <summary>Backing collection bound to the ListView via a CollectionViewSource.</summary>
    private readonly ObservableGroupedCollection<string, ModelPickerEntry> _groupedView = [];

    /// <summary>CollectionViewSource exposing <see cref="_groupedView"/> as a grouped view.</summary>
    private CollectionViewSource? _cvs;

    private Button? _rootButton;
    private TextBlock? _labelText;
    private AutoSuggestBox? _searchBox;
    private ListView? _list;

    #endregion

    #region Construction

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelPicker"/> class
    /// and wires the default style key for theme lookup.
    /// </summary>
    public ModelPicker()
    {
        DefaultStyleKey = typeof(ModelPicker);
    }

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the <see cref="ItemsSource"/> dependency property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IList),
        typeof(ModelPicker),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// All entries the picker can display. Type is non-generic
    /// <see cref="IList"/> because <see cref="DependencyProperty"/> does
    /// not support open generics; the picker projects items via OfType.
    /// </summary>
    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Identifies the <see cref="SelectedItem"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(ModelPickerEntry),
        typeof(ModelPicker),
        new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>The currently selected entry, or null. Two-way bindable.</summary>
    public ModelPickerEntry? SelectedItem
    {
        get => (ModelPickerEntry?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Identifies the <see cref="PlaceholderText"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(ModelPicker),
        new PropertyMetadata(null, OnPlaceholderTextChanged));

    /// <summary>Placeholder shown when nothing is selected.</summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user picks an entry from the list. Not raised when
    /// the host programmatically changes <see cref="SelectedItem"/>.
    /// </summary>
    public event EventHandler<ModelPickerEntry?>? SelectionChanged;

    #endregion

    #region Template handling

    /// <summary>
    /// Resolves all <c>PART_</c> elements from the applied template and
    /// wires the search box, list, and flyout-opened handlers. Initializes
    /// the grouped view and the label text.
    /// </summary>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old handlers if a template was reapplied.
        DetachHandlers();

        _rootButton = GetTemplateChild(PartRootButton) as Button;
        _labelText = GetTemplateChild(PartLabelText) as TextBlock;
        _searchBox = GetTemplateChild(PartSearchBox) as AutoSuggestBox;
        _list = GetTemplateChild(PartList) as ListView;

        _cvs = new CollectionViewSource { IsSourceGrouped = true, Source = _groupedView };

        if (_list is not null)
        {
            _list.ItemsSource = _cvs.View;
            _list.ItemClick += OnListItemClick;
            _list.KeyDown += OnListKeyDown;
        }

        if (_searchBox is not null)
        {
            _searchBox.TextChanged += OnSearchTextChanged;
            _searchBox.KeyDown += OnSearchKeyDown;
            _searchBox.PlaceholderText = Res.GetString("ModelPicker_SearchPlaceholder") ?? string.Empty;
        }

        if (_rootButton?.Flyout is not null)
        {
            _rootButton.Flyout.Opened += OnFlyoutOpened;
        }

        UpdateLabel();
        RebuildGroupedView(searchText: null);
    }

    /// <summary>
    /// Detaches event handlers from previously-resolved template parts.
    /// Called before <see cref="OnApplyTemplate"/> rewires a fresh template.
    /// </summary>
    private void DetachHandlers()
    {
        if (_list is not null)
        {
            _list.ItemClick -= OnListItemClick;
            _list.KeyDown -= OnListKeyDown;
        }
        if (_searchBox is not null)
        {
            _searchBox.TextChanged -= OnSearchTextChanged;
            _searchBox.KeyDown -= OnSearchKeyDown;
        }
        if (_rootButton?.Flyout is not null)
        {
            _rootButton.Flyout.Opened -= OnFlyoutOpened;
        }
    }

    #endregion

    #region Grouped view

    /// <summary>
    /// Rebuilds the grouped view from <see cref="ItemsSource"/>, applying
    /// the current search filter. Empty groups are omitted, so group
    /// headers disappear automatically when no items match within them.
    /// </summary>
    /// <param name="searchText">Trimmed search query, or null/empty for no filter.</param>
    private void RebuildGroupedView(string? searchText)
    {
        _groupedView.Clear();
        if (ItemsSource is null) return;

        IEnumerable<ModelPickerEntry> entries = ItemsSource.OfType<ModelPickerEntry>();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var query = searchText.Trim();
            entries = entries.Where(e =>
                ((e.DisplayName ?? e.ModelId) ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.ModelId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                ((e.GroupDisplayName ?? e.GroupKey) ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var group in entries
            .GroupBy(e => e.GroupKey ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(e => e.DisplayName ?? e.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _groupedView.Add(new ObservableGroup<string, ModelPickerEntry>(group.Key, ordered));
        }
    }

    #endregion

    #region Event handlers

    /// <summary>
    /// Re-filters the view when the user types into the search box.
    /// Programmatic text changes (e.g., resetting on flyout open) are
    /// ignored.
    /// </summary>
    /// <param name="sender">The search box.</param>
    /// <param name="args">Text-changed arguments.</param>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        RebuildGroupedView(sender.Text);
    }

    /// <summary>
    /// Handles ↓ to move focus from the search box into the list, and Esc
    /// to close the flyout without changing the selection.
    /// </summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">Key event arguments.</param>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            FocusFirstListItem();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            _rootButton?.Flyout?.Hide();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Closes the flyout when Esc is pressed while the list has focus.
    /// </summary>
    /// <param name="sender">The list view.</param>
    /// <param name="e">Key event arguments.</param>
    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _rootButton?.Flyout?.Hide();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Moves keyboard focus to the first item in the list. ListView
    /// container realization is async, so the focus call is queued onto
    /// the dispatcher to run after layout completes.
    /// </summary>
    private void FocusFirstListItem()
    {
        if (_list is null) return;
        if (_list.Items.Count == 0) return;

        if (_list.SelectedIndex < 0)
            _list.SelectedIndex = 0;

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_list is null) return;
            if (_list.ContainerFromIndex(_list.SelectedIndex < 0 ? 0 : _list.SelectedIndex)
                is ListViewItem container)
            {
                container.Focus(FocusState.Programmatic);
            }
            else
            {
                _list.Focus(FocusState.Programmatic);
            }
        });
    }

    /// <summary>
    /// Commits the user's pick: updates <see cref="SelectedItem"/>, the
    /// label, fires <see cref="SelectionChanged"/>, and closes the flyout.
    /// </summary>
    /// <param name="sender">The list view.</param>
    /// <param name="e">Item-click arguments.</param>
    private void OnListItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ModelPickerEntry entry) return;
        SelectedItem = entry;
        UpdateLabel();
        SelectionChanged?.Invoke(this, entry);
        _rootButton?.Flyout?.Hide();
    }

    /// <summary>
    /// Resets the search box and rebuilds the unfiltered grouped view
    /// each time the flyout opens, then focuses the search box.
    /// </summary>
    /// <param name="sender">The flyout.</param>
    /// <param name="e">Unused event payload.</param>
    private void OnFlyoutOpened(object? sender, object e)
    {
        if (_searchBox is not null)
        {
            _searchBox.Text = string.Empty;
            _searchBox.Focus(FocusState.Programmatic);
        }
        RebuildGroupedView(searchText: null);
    }

    #endregion

    #region DP change callbacks

    /// <summary>
    /// Refreshes the grouped view when <see cref="ItemsSource"/> is
    /// reassigned. <see cref="SelectedItem"/> survives because
    /// <see cref="ModelPickerEntry"/> uses (GroupKey, ModelId) equality.
    /// </summary>
    /// <param name="d">The picker instance.</param>
    /// <param name="e">Unused property-change payload.</param>
    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelPicker picker)
        {
            picker.RebuildGroupedView(searchText: null);
        }
    }

    /// <summary>
    /// Updates the button label when <see cref="SelectedItem"/> changes.
    /// Programmatic assignments do not raise
    /// <see cref="SelectionChanged"/>; only user list-clicks do.
    /// </summary>
    /// <param name="d">The picker instance.</param>
    /// <param name="e">Unused property-change payload.</param>
    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelPicker picker)
        {
            picker.UpdateLabel();
        }
    }

    /// <summary>
    /// Updates the button label when <see cref="PlaceholderText"/> changes
    /// and there is no current selection.
    /// </summary>
    /// <param name="d">The picker instance.</param>
    /// <param name="e">Unused property-change payload.</param>
    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelPicker picker)
        {
            picker.UpdateLabel();
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Refreshes the label text from the current <see cref="SelectedItem"/>
    /// and <see cref="PlaceholderText"/>.
    /// </summary>
    private void UpdateLabel()
    {
        if (_labelText is null) return;
        _labelText.Text = SelectedItem?.DisplayName
            ?? SelectedItem?.ModelId
            ?? PlaceholderText
            ?? string.Empty;
    }

    #endregion
}
