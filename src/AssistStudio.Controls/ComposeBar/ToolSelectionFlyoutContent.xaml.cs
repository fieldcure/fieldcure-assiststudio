using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// XAML view for the tool selection flyout content.
/// </summary>
internal sealed partial class ToolSelectionFlyoutContent : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSelectionFlyoutContent"/> class.
    /// </summary>
    public ToolSelectionFlyoutContent()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the items control that renders tool toggle rows.
    /// </summary>
    internal ItemsControl ItemsControl => PART_ItemsControl;

    /// <summary>
    /// Gets the footer hyperlink that toggles all rows on or off.
    /// </summary>
    internal HyperlinkButton ToggleAllLink => PART_ToggleAllLink;

    /// <summary>
    /// Shows the normal list layout and hides the empty-state message.
    /// </summary>
    internal void ShowListLayout()
    {
        PART_ListLayout.Visibility = Visibility.Visible;
        PART_EmptyStatePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the empty-state message and hides the normal list layout.
    /// </summary>
    /// <param name="message">The localized empty-state text to display.</param>
    internal void ShowEmptyState(string message)
    {
        PART_EmptyText.Text = message;
        PART_ListLayout.Visibility = Visibility.Collapsed;
        PART_EmptyStatePanel.Visibility = Visibility.Visible;
    }
}
