using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ComposeBar
{
    #region Dependency Property Callbacks

    /// <summary>
    /// Called when <see cref="AvailableTools"/> changes to rebuild the tool toggle flyout.
    /// </summary>
    private static void OnAvailableToolsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            // Reset — all tools enabled by default
            self.EnabledToolNames = null;
            self.UpdateToolButtonVisibility();
        }
    }

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
    /// Builds the tools flyout showing tool toggles from the profile.
    /// Shows an empty state message when no tools are enabled in the profile.
    /// </summary>
    private void BuildToolsFlyout()
    {
        if (_toolButton is null) return;

        var tools = AvailableTools;

        // Flyout shows only server placeholders (Essentials, Workspace, etc.).
        // Individual tools and meta-tools (search_tools) are hidden.
        bool IsVisibleTool(IAssistTool t) => t.IsServerPlaceholder;

        // Empty state: no tools enabled
        if (tools.Count == 0)
        {
            var emptyPanel = new StackPanel
            {
                Spacing = 4,
                Padding = new Thickness(8),
                MaxWidth = 240,
            };
            emptyPanel.Children.Add(new TextBlock
            {
                Text = Res.GetString("ComposeBar_NoToolsEnabled") is { Length: > 0 } emptyText
                    ? emptyText
                    : "No tools or servers enabled.\nAdd them in Profile settings.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                FontSize = 12,
            });

            _toolButton.Flyout = new Flyout
            {
                Content = emptyPanel,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
            };
            return;
        }

        var panel = new StackPanel { Spacing = 4 };
        var allCheckBoxes = new List<CheckBox>();

        var enabledToolSet = EnabledToolNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools.Where(IsVisibleTool))
        {
            // Try localized name from resources (e.g., Tool_read_file → "파일 읽기")
            var displayName = Res.GetString($"Tool_{tool.Name}") is { Length: > 0 } localized
                ? localized
                : tool.DisplayName;

            var cb = new CheckBox
            {
                Content = displayName,
                IsChecked = enabledToolSet is null || enabledToolSet.Contains(tool.Name),
                Tag = tool.Name,
                MinWidth = 0,
            };
            cb.Checked += ToolCheckBox_Changed;
            cb.Unchecked += ToolCheckBox_Changed;
            panel.Children.Add(cb);
            allCheckBoxes.Add(cb);
        }

        // --- Footer: Separator + Toggle all ---
        var footer = new StackPanel { Spacing = 4, Padding = new Thickness(4, 0, 4, 4) };
        footer.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 1,
            Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(0, 2, 0, 2),
        });

        var selectLabel = Res.GetString("ComposeBar_SelectAll") ?? "Select all";
        var deselectLabel = Res.GetString("ComposeBar_DeselectAll") ?? "Deselect all";
        var allChecked = allCheckBoxes.All(c => c.IsChecked == true);

        var toggleLink = new HyperlinkButton
        {
            Content = allChecked ? deselectLabel : selectLabel,
            Padding = new Thickness(0),
            FontSize = 12,
        };
        toggleLink.Click += (_, _) =>
        {
            var nowAllChecked = allCheckBoxes.All(c => c.IsChecked == true);
            foreach (var cb in allCheckBoxes)
                cb.IsChecked = !nowAllChecked;
        };
        footer.Children.Add(toggleLink);

        var outerPanel = new StackPanel { Padding = new Thickness(4) };
        outerPanel.Children.Add(new ScrollViewer
        {
            Content = panel,
            MaxHeight = 400,
            Padding = new Thickness(0, 0, 8, 0),
        });
        outerPanel.Children.Add(footer);

        _toolButton.Flyout = new Flyout
        {
            Content = outerPanel,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
    }

    /// <summary>
    /// Handles tool checkbox toggle to update <see cref="EnabledToolNames"/>.
    /// </summary>
    private void ToolCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string toolName) return;

        var tools = AvailableTools;
        var current = EnabledToolNames?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [.. tools.Select(t => t.Name)];

        if (cb.IsChecked == true)
            current.Add(toolName);
        else
            current.Remove(toolName);

        EnabledToolNames = current.Count == tools.Count ? null : [.. current];
    }

    #endregion
}
