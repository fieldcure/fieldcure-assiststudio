using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// An inline panel that replaces the ComposeBar when a tool requires user confirmation.
/// Displays the tool name, arguments preview, and Allow/Reject buttons.
/// </summary>
public sealed partial class ToolApprovalPanel : Control
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="ToolName"/> dependency property.</summary>
    public static readonly DependencyProperty ToolNameProperty =
        DependencyProperty.Register(nameof(ToolName), typeof(string), typeof(ToolApprovalPanel),
            new PropertyMetadata(string.Empty, OnToolNameChanged));

    /// <summary>Identifies the <see cref="ToolDisplayName"/> dependency property.</summary>
    public static readonly DependencyProperty ToolDisplayNameProperty =
        DependencyProperty.Register(nameof(ToolDisplayName), typeof(string), typeof(ToolApprovalPanel),
            new PropertyMetadata(string.Empty, OnToolNameChanged));

    /// <summary>Identifies the <see cref="Arguments"/> dependency property.</summary>
    public static readonly DependencyProperty ArgumentsProperty =
        DependencyProperty.Register(nameof(Arguments), typeof(string), typeof(ToolApprovalPanel),
            new PropertyMetadata(string.Empty, OnArgumentsChanged));

    /// <summary>Identifies the <see cref="ServerName"/> dependency property.</summary>
    public static readonly DependencyProperty ServerNameProperty =
        DependencyProperty.Register(nameof(ServerName), typeof(string), typeof(ToolApprovalPanel),
            new PropertyMetadata(string.Empty, OnServerNameChanged));

    /// <summary>Identifies the <see cref="IsExpanded"/> dependency property.</summary>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(ToolApprovalPanel),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>Gets or sets the internal tool name (e.g. "write_file").</summary>
    public string ToolName
    {
        get => (string)GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    /// <summary>Gets or sets the localized display name shown in the header.</summary>
    public string ToolDisplayName
    {
        get => (string)GetValue(ToolDisplayNameProperty);
        set => SetValue(ToolDisplayNameProperty, value);
    }

    /// <summary>Gets or sets the raw JSON arguments string shown in the preview area.</summary>
    public string Arguments
    {
        get => (string)GetValue(ArgumentsProperty);
        set => SetValue(ArgumentsProperty, value);
    }

    /// <summary>Gets or sets the MCP server name for the badge display. Empty hides the badge.</summary>
    public string ServerName
    {
        get => (string)GetValue(ServerNameProperty);
        set => SetValue(ServerNameProperty, value);
    }

    /// <summary>Gets or sets whether the arguments preview is expanded.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    #endregion

    #region Events

    /// <summary>Raised when the user clicks Allow. The string argument contains the optional user note.</summary>
    public event EventHandler<string?>? Approved;

    /// <summary>Raised when the user clicks Reject. The string argument contains the optional user note.</summary>
    public event EventHandler<string?>? Rejected;

    #endregion

    #region Fields

    private Button? _approveButton;
    private Button? _rejectButton;
    private Button? _expandButton;
    private StackPanel? _parametersPanel;
    private TextBlock? _promptText;
    private ScrollViewer? _argumentsContainer;
    private FontIcon? _expandIcon;
    private TextBox? _userNoteBox;
    private Border? _serverBadge;
    private TextBlock? _serverBadgeText;
    private string _approveLabel = "Allow";
    private string _rejectLabel = "Reject";
    private string _promptTemplate = "Allow {0} to execute?";

    private const int CollapseThreshold = 120;

    #endregion

    #region Constructor

    /// <summary>Initializes a new instance of the <see cref="ToolApprovalPanel"/> control.</summary>
    public ToolApprovalPanel()
    {
        DefaultStyleKey = typeof(ToolApprovalPanel);
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old handlers
        if (_approveButton is not null) _approveButton.Click -= OnApproveClick;
        if (_rejectButton is not null) _rejectButton.Click -= OnRejectClick;
        if (_expandButton is not null) _expandButton.Click -= OnExpandClick;

        // Get template parts
        _approveButton = GetTemplateChild("PART_ApproveButton") as Button;
        _rejectButton = GetTemplateChild("PART_RejectButton") as Button;
        _expandButton = GetTemplateChild("PART_ExpandButton") as Button;
        _parametersPanel = GetTemplateChild("PART_ParametersPanel") as StackPanel;
        _promptText = GetTemplateChild("PART_PromptText") as TextBlock;
        _argumentsContainer = GetTemplateChild("PART_ArgumentsContainer") as ScrollViewer;
        _expandIcon = GetTemplateChild("PART_ExpandIcon") as FontIcon;
        _userNoteBox = GetTemplateChild("PART_UserNoteBox") as TextBox;
        _serverBadge = GetTemplateChild("PART_ServerBadge") as Border;
        _serverBadgeText = GetTemplateChild("PART_ServerBadgeText") as TextBlock;

        // Attach handlers
        if (_approveButton is not null) _approveButton.Click += OnApproveClick;
        if (_rejectButton is not null) _rejectButton.Click += OnRejectClick;
        if (_expandButton is not null) _expandButton.Click += OnExpandClick;

        // Load localized strings
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            _approveLabel = loader.GetString("ToolApproval_Approve");
            _rejectLabel = loader.GetString("ToolApproval_Reject");
            _promptTemplate = loader.GetString("ToolApproval_Prompt");
            if (_userNoteBox is not null)
                _userNoteBox.PlaceholderText = loader.GetString("ToolApproval_NotePlaceholder");
        }
        catch { /* Use defaults */ }

        if (_approveButton is not null) _approveButton.Content = _approveLabel;
        if (_rejectButton is not null) _rejectButton.Content = _rejectLabel;

        // Sync current state
        UpdatePromptText();
        PopulateParameters();
        UpdateArgumentsVisibility();
        UpdateServerBadge();
    }

    #endregion

    #region Private Methods

    /// <summary>Sets keyboard focus to the user note TextBox.</summary>
    public void FocusUserNote() => _userNoteBox?.Focus(FocusState.Programmatic);

    /// <summary>Gets the user note text, or <c>null</c> if empty.</summary>
    private string? UserNote
    {
        get
        {
            var text = _userNoteBox?.Text;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }

    /// <summary>Clears the user note TextBox.</summary>
    private void ClearUserNote()
    {
        if (_userNoteBox is not null)
            _userNoteBox.Text = string.Empty;
    }

    /// <summary>Handles the Approve button click.</summary>
    private void OnApproveClick(object sender, RoutedEventArgs e)
    {
        var note = UserNote;
        ClearUserNote();
        Approved?.Invoke(this, note);
    }

    /// <summary>Handles the Reject button click.</summary>
    private void OnRejectClick(object sender, RoutedEventArgs e)
    {
        var note = UserNote;
        ClearUserNote();
        Rejected?.Invoke(this, note);
    }

    /// <summary>Toggles the arguments preview expansion.</summary>
    private void OnExpandClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    /// <summary>Updates the prompt text with the current tool display name.</summary>
    private void UpdatePromptText()
    {
        if (_promptText is not null)
            _promptText.Text = string.Format(_promptTemplate,
                string.IsNullOrEmpty(ToolDisplayName) ? ToolName : ToolDisplayName);
    }

    /// <summary>Shows or hides the arguments container based on <see cref="IsExpanded"/>.</summary>
    private void UpdateArgumentsVisibility()
    {
        if (_argumentsContainer is not null)
            _argumentsContainer.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_expandIcon is not null)
            _expandIcon.Glyph = IsExpanded ? "\uE70D" : "\uE70E"; // ChevronUp : ChevronRight
    }

    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// Parses the Arguments JSON and populates the parameters panel with
    /// individual rows for each property. Long values are collapsible.
    /// </summary>
    private void PopulateParameters()
    {
        if (_parametersPanel is null) return;
        _parametersPanel.Children.Clear();

        var json = Arguments;
        if (string.IsNullOrWhiteSpace(json))
        {
            if (_expandButton is not null) _expandButton.IsEnabled = false;
            return;
        }

        if (_expandButton is not null) _expandButton.IsEnabled = true;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch
        {
            _parametersPanel.Children.Add(CreateRawFallback(json));
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                _parametersPanel.Children.Add(CreateRawFallback(json));
                return;
            }

            var hasAny = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                _parametersPanel.Children.Add(CreateParameterRow(prop.Name, prop.Value));
                hasAny = true;
            }

            if (!hasAny && _expandButton is not null)
                _expandButton.IsEnabled = false;
        }
    }

    /// <summary>Creates a single parameter row. Short values inline; long values collapsible with chevron.</summary>
    private static FrameworkElement CreateParameterRow(string name, JsonElement value)
    {
        var display = FormatJsonValue(value);
        var isLong = display.Length > CollapseThreshold || display.Contains('\n');

        if (!isLong)
        {
            // Short value: "name  value" on one line
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = display,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        // Long value: chevron + name + preview, click to expand
        var container = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };

        var chevron = new FontIcon
        {
            Glyph = "\uE76C", // ChevronRight
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };

        var preview = display[..Math.Min(CollapseThreshold, display.Length)].Replace('\n', ' ').Replace('\r', ' ');
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerRow.Children.Add(chevron);
        headerRow.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = preview + "...",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        container.Children.Add(headerRow);

        var fullText = new TextBlock
        {
            Text = display,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(16, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        container.Children.Add(fullText);

        // Click header to toggle
        headerRow.Tapped += (_, _) =>
        {
            if (fullText.Visibility == Visibility.Collapsed)
            {
                fullText.Visibility = Visibility.Visible;
                chevron.Glyph = "\uE70D"; // ChevronDown
            }
            else
            {
                fullText.Visibility = Visibility.Collapsed;
                chevron.Glyph = "\uE76C"; // ChevronRight
            }
        };

        return container;
    }

    /// <summary>Fallback: show raw text when JSON parsing fails.</summary>
    private static FrameworkElement CreateRawFallback(string raw)
    {
        return new TextBlock
        {
            Text = raw,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
    }

    /// <summary>Formats a JsonElement as a display string. Strings unquoted; objects/arrays pretty-printed.</summary>
    private static string FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Object or JsonValueKind.Array =>
                JsonSerializer.Serialize(value, s_indentedOptions),
            _ => value.GetRawText(),
        };
    }

    /// <summary>Callback when <see cref="ToolName"/> or <see cref="ToolDisplayName"/> changes.</summary>
    private static void OnToolNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolApprovalPanel panel)
            panel.UpdatePromptText();
    }

    /// <summary>Callback when <see cref="Arguments"/> property changes.</summary>
    private static void OnArgumentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolApprovalPanel panel)
            panel.PopulateParameters();
    }

    /// <summary>Callback when <see cref="IsExpanded"/> property changes.</summary>
    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolApprovalPanel panel)
            panel.UpdateArgumentsVisibility();
    }

    /// <summary>Callback when <see cref="ServerName"/> property changes.</summary>
    private static void OnServerNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolApprovalPanel panel)
            panel.UpdateServerBadge();
    }

    /// <summary>Shows or hides the server badge and updates its text.</summary>
    private void UpdateServerBadge()
    {
        var hasServer = !string.IsNullOrEmpty(ServerName);
        if (_serverBadge is not null)
            _serverBadge.Visibility = hasServer ? Visibility.Visible : Visibility.Collapsed;
        if (_serverBadgeText is not null)
            _serverBadgeText.Text = ServerName;
    }

    #endregion
}
