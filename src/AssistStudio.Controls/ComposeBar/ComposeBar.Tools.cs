using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ComposeBar
{
    #region Fields

    /// <summary>
    /// The reusable flyout showing the tool/server toggles. Instantiated once and updated in place.
    /// </summary>
    private ToolSelectionFlyout? _toolFlyout;

    #endregion

    #region Dependency Property Callbacks

    /// <summary>
    /// Called when <see cref="AvailableTools"/> changes to refresh the tool toggle flyout.
    /// </summary>
    private static void OnAvailableToolsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            // Reset — all tools enabled by default
            self.EnabledToolNames = null;
            self.UpdateToolButtonVisibility();
            self.RefreshToolsFlyout();
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedProfile"/> changes; the tool list may depend on the profile.
    /// </summary>
    private static void OnSelectedProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
            self.UpdateToolButtonVisibility();
    }

    #endregion

    #region Tool Button Visibility

    /// <summary>
    /// Updates the tool button visibility. Always visible so users can see the empty state
    /// message when no tools or servers are enabled in the profile.
    /// </summary>
    private void UpdateToolButtonVisibility()
    {
        if (_toolButton is null) return;
        _toolButton.Visibility = Visibility.Visible;
    }

    #endregion

    #region Tool Flyout

    /// <summary>
    /// Raised whenever the user toggles a server in the tool-selection flyout.
    /// The argument is the new <see cref="EnabledToolNames"/> set (<see langword="null"/>
    /// means "all enabled"). Hosts subscribe to react to per-conversation tool
    /// enable/disable — for example, the per-tab Filesystem MCP server is
    /// connected only when its placeholder appears enabled in this list and
    /// workspace folders are configured (see
    /// <c>ChatTabViewModel.OnEnabledToolsChanged</c> for the full principle).
    /// </summary>
    public event EventHandler<IReadOnlyList<string>?>? EnabledToolsChanged;

    /// <summary>
    /// Ensures the tools flyout instance exists and is attached to the tool button, creating
    /// it lazily on first use. Subsequent calls are no-ops.
    /// </summary>
    private void EnsureToolsFlyout()
    {
        if (_toolButton is null || _toolFlyout is not null) return;

        _toolFlyout = new ToolSelectionFlyout();
        _toolFlyout.SelectionChanged += (_, enabled) =>
        {
            EnabledToolNames = enabled;
            EnabledToolsChanged?.Invoke(this, enabled);
        };
        _toolButton.Flyout = _toolFlyout;
    }

    /// <summary>
    /// Refreshes the tools flyout content from the current <see cref="AvailableTools"/> list.
    /// Filters to server placeholders (individual tools and meta-tools are hidden).
    /// </summary>
    private void RefreshToolsFlyout()
    {
        EnsureToolsFlyout();
        if (_toolFlyout is null) return;

        // Flyout shows only server placeholders (Essentials, Workspace, etc.).
        // Individual tools and meta-tools (search_tools) are hidden.
        var visibleTools = AvailableTools.Where(IsVisibleTool).ToList();
        _toolFlyout.SetItems(visibleTools, EnabledToolNames);
    }

    /// <summary>
    /// Determines whether a tool should be visible in the selection flyout.
    /// </summary>
    private static bool IsVisibleTool(IAssistTool t) => t.IsServerPlaceholder;

    #endregion
}
