using System.Collections.Generic;
using System.Text.Json;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// An inline panel that replaces the ComposeBar when a tool requires user confirmation.
/// Displays the tool name, arguments preview, and Allow/Reject buttons.
/// </summary>
public sealed partial class ToolApprovalPanel : Control
{
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

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
            new PropertyMetadata(true, OnIsExpandedChanged));

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
    private ItemsControl? _parametersControl;
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
        AutomationHelper.SetAutomation(this, "ToolApprovalPanel",
            nameKey: "ToolApprovalPanel_ControlName", helpTextKey: "ToolApprovalPanel_ControlHelpText");
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
        _parametersControl = GetTemplateChild("PART_ParametersControl") as ItemsControl;
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
            _approveLabel = Res.GetString("ToolApproval_Approve");
            _rejectLabel = Res.GetString("ToolApproval_Reject");
            _promptTemplate = Res.GetString("ToolApproval_Prompt");
            if (_userNoteBox is not null)
                _userNoteBox.PlaceholderText = Res.GetString("ToolApproval_NotePlaceholder");
        }
        catch { /* Use defaults */ }

        if (_approveButton is not null) _approveButton.Content = _approveLabel;
        if (_rejectButton is not null) _rejectButton.Content = _rejectLabel;

        if (_expandButton is not null)
            AutomationHelper.SetAutomation(_expandButton, "ToolApprovalExpandButton",
                nameKey: "ToolApproval_ExpandName");
        if (_userNoteBox is not null)
            AutomationHelper.SetAutomation(_userNoteBox, "ToolApprovalUserNoteBox",
                nameKey: "ToolApproval_UserNoteName");

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

    /// <summary>JSON serializer options used when pretty-printing nested object/array arguments for display.</summary>
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    /// <summary>Parses the raw <see cref="Arguments"/> JSON into <see cref="ToolParameterItem"/> rows bound to the parameters list.</summary>
    private void PopulateParameters()
    {
        if (_parametersControl is null) return;

        var json = Arguments;
        if (string.IsNullOrWhiteSpace(json))
        {
            _parametersControl.ItemsSource = null;
            if (_expandButton is not null) _expandButton.IsEnabled = false;
            return;
        }

        if (_expandButton is not null) _expandButton.IsEnabled = true;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch
        {
            // Raw fallback: single item with full text
            _parametersControl.ItemsSource = new[]
            {
                new ToolParameterItem { Name = "", Display = json, IsLong = true,
                    Preview = json[..Math.Min(CollapseThreshold, json.Length)] }
            };
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                var raw = json;
                _parametersControl.ItemsSource = new[]
                {
                    new ToolParameterItem { Name = "", Display = raw, IsLong = true,
                        Preview = raw[..Math.Min(CollapseThreshold, raw.Length)] }
                };
                return;
            }

            var items = new List<ToolParameterItem>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var display = FormatJsonValue(prop.Value);
                var isLong = display.Length > CollapseThreshold || display.Contains('\n');
                items.Add(new ToolParameterItem
                {
                    Name = prop.Name,
                    Display = display,
                    IsLong = isLong,
                    Preview = isLong
                        ? (display.Contains('\n')
                            ? display[..display.IndexOf('\n')].TrimEnd()
                            : display[..Math.Min(CollapseThreshold, display.Length)])
                        : "",
                });
            }

            if (items.Count == 0 && _expandButton is not null)
                _expandButton.IsEnabled = false;

            _parametersControl.ItemsSource = items;
        }
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
