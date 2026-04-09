using FieldCure.AssistStudio.Controls.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// An inline panel that replaces the ComposeBar when an MCP server requests
/// user input via elicitation. Renders enum/boolean fields as clickable option
/// buttons (click = immediate submit) and string fields with a TextBox.
/// </summary>
public sealed partial class ToolElicitationPanel : Control
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="ToolName"/> dependency property.</summary>
    public static readonly DependencyProperty ToolNameProperty =
        DependencyProperty.Register(nameof(ToolName), typeof(string), typeof(ToolElicitationPanel),
            new PropertyMetadata(string.Empty, OnToolNameChanged));

    /// <summary>Identifies the <see cref="ServerName"/> dependency property.</summary>
    public static readonly DependencyProperty ServerNameProperty =
        DependencyProperty.Register(nameof(ServerName), typeof(string), typeof(ToolElicitationPanel),
            new PropertyMetadata(string.Empty, OnServerNameChanged));

    /// <summary>Identifies the <see cref="Message"/> dependency property.</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ToolElicitationPanel),
            new PropertyMetadata(string.Empty, OnMessageChanged));

    /// <summary>Identifies the <see cref="Fields"/> dependency property.</summary>
    public static readonly DependencyProperty FieldsProperty =
        DependencyProperty.Register(nameof(Fields), typeof(IReadOnlyList<ElicitationFieldInfo>),
            typeof(ToolElicitationPanel),
            new PropertyMetadata(null, OnFieldsChanged));

    /// <summary>Gets or sets the tool name displayed in the header.</summary>
    public string ToolName
    {
        get => (string)GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    /// <summary>Gets or sets the MCP server name for the badge. Empty hides the badge.</summary>
    public string ServerName
    {
        get => (string)GetValue(ServerNameProperty);
        set => SetValue(ServerNameProperty, value);
    }

    /// <summary>Gets or sets the descriptive message shown below the header.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Gets or sets the list of fields to render.</summary>
    public IReadOnlyList<ElicitationFieldInfo>? Fields
    {
        get => (IReadOnlyList<ElicitationFieldInfo>?)GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user submits input (option click or Submit button).
    /// The dictionary maps field names to their selected values.
    /// </summary>
    public event EventHandler<IDictionary<string, object?>>? Submitted;

    /// <summary>Raised when the user clicks Skip (decline).</summary>
    public event EventHandler? Declined;

    /// <summary>Raised when the user presses ESC (cancel).</summary>
    public event EventHandler? Cancelled;

    #endregion

    #region Fields

    private TextBlock? _promptText;
    private TextBlock? _messageText;
    private Border? _serverBadge;
    private TextBlock? _serverBadgeText;
    private StackPanel? _fieldsPanel;
    private Button? _declineButton;
    private Button? _submitButton;
    private string _declineLabel = "Skip";
    private string _submitLabel = "Submit";
    private string _promptTemplate = "{0} requires input";

    /// <summary>Tracks the current text input value for string fields.</summary>
    private readonly Dictionary<string, TextBox> _textInputs = [];

    /// <summary>Tracks the selected option value for enum/boolean fields (multi-field mode).</summary>
    private readonly Dictionary<string, string> _selectedOptions = [];

    /// <summary>Whether the panel has multiple fields (disables immediate submit on option click).</summary>
    private bool _isMultiField;

    #endregion

    #region Constructor

    /// <summary>Initializes a new instance of the <see cref="ToolElicitationPanel"/> control.</summary>
    public ToolElicitationPanel()
    {
        DefaultStyleKey = typeof(ToolElicitationPanel);
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old handlers
        if (_declineButton is not null) _declineButton.Click -= OnDeclineClick;
        if (_submitButton is not null) _submitButton.Click -= OnSubmitClick;

        // Get template parts
        _promptText = GetTemplateChild("PART_PromptText") as TextBlock;
        _messageText = GetTemplateChild("PART_MessageText") as TextBlock;
        _serverBadge = GetTemplateChild("PART_ServerBadge") as Border;
        _serverBadgeText = GetTemplateChild("PART_ServerBadgeText") as TextBlock;
        _fieldsPanel = GetTemplateChild("PART_FieldsPanel") as StackPanel;
        _declineButton = GetTemplateChild("PART_DeclineButton") as Button;
        _submitButton = GetTemplateChild("PART_SubmitButton") as Button;

        // Attach handlers
        if (_declineButton is not null) _declineButton.Click += OnDeclineClick;
        if (_submitButton is not null) _submitButton.Click += OnSubmitClick;

        // Load localized strings
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            _declineLabel = loader.GetString("ToolElicitation_Skip");
            _submitLabel = loader.GetString("ToolElicitation_Submit");
            _promptTemplate = loader.GetString("ToolElicitation_Prompt");
        }
        catch { /* Use defaults */ }

        if (_declineButton is not null) _declineButton.Content = _declineLabel;
        if (_submitButton is not null) _submitButton.Content = _submitLabel;

        // Sync current state
        UpdatePromptText();
        UpdateMessageText();
        UpdateServerBadge();
        RenderFields();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }

    #endregion

    #region Private Methods

    private void OnDeclineClick(object sender, RoutedEventArgs e) =>
        Declined?.Invoke(this, EventArgs.Empty);

    private void OnSubmitClick(object sender, RoutedEventArgs e) =>
        SubmitAllFields();

    /// <summary>Collects all field values (selected options + text inputs) and submits.</summary>
    private void SubmitAllFields()
    {
        var content = new Dictionary<string, object?>();
        foreach (var (name, value) in _selectedOptions)
            content[name] = value;
        foreach (var (name, textBox) in _textInputs)
            content[name] = textBox.Text;
        Submitted?.Invoke(this, content);
    }

    /// <summary>
    /// Handles an option button click. In single-field mode, submits immediately.
    /// In multi-field mode, records the selection and highlights the button.
    /// </summary>
    private void OnOptionSelected(string fieldName, string value, Button clickedButton)
    {
        if (!_isMultiField)
        {
            // Single field — immediate submit
            var content = new Dictionary<string, object?> { [fieldName] = value };
            Submitted?.Invoke(this, content);
            return;
        }

        // Multi-field — record selection, update visual state
        _selectedOptions[fieldName] = value;

        // Find the parent container and update all sibling button visuals
        if (clickedButton.Parent is StackPanel container)
        {
            foreach (var child in container.Children)
            {
                if (child is not Button sibling) continue;
                var isSelected = sibling == clickedButton;
                UpdateOptionButtonVisual(sibling, isSelected);
            }
        }
    }

    /// <summary>Updates an option button's visual to selected or unselected state.</summary>
    private static void UpdateOptionButtonVisual(Button button, bool isSelected)
    {
        if (button.Content is not StackPanel panel || panel.Children.Count < 2) return;
        if (panel.Children[0] is not Border badge) return;
        if (badge.Child is not TextBlock badgeText) return;
        if (panel.Children[1] is not TextBlock label) return;

        badge.Background = isSelected
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        badgeText.Foreground = isSelected
            ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        label.FontWeight = isSelected
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;
    }

    /// <summary>Updates the prompt text.</summary>
    private void UpdatePromptText()
    {
        if (_promptText is not null)
            _promptText.Text = string.Format(_promptTemplate,
                string.IsNullOrEmpty(ToolName) ? "Tool" : ToolName);
    }

    /// <summary>Updates the message text.</summary>
    private void UpdateMessageText()
    {
        if (_messageText is not null)
        {
            _messageText.Text = Message;
            _messageText.Visibility = string.IsNullOrEmpty(Message)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Shows or hides the server badge.</summary>
    private void UpdateServerBadge()
    {
        var hasServer = !string.IsNullOrEmpty(ServerName);
        if (_serverBadge is not null)
            _serverBadge.Visibility = hasServer ? Visibility.Visible : Visibility.Collapsed;
        if (_serverBadgeText is not null)
            _serverBadgeText.Text = ServerName;
    }

    /// <summary>Renders all fields into the fields panel.</summary>
    private void RenderFields()
    {
        if (_fieldsPanel is null) return;
        _fieldsPanel.Children.Clear();
        _textInputs.Clear();
        _selectedOptions.Clear();

        var fields = Fields;
        if (fields is null || fields.Count == 0)
        {
            _isMultiField = false;
            UpdateSubmitVisibility(false);
            return;
        }

        _isMultiField = fields.Count > 1;

        for (var fi = 0; fi < fields.Count; fi++)
        {
            var field = fields[fi];

            // Add spacing between fields in multi-field mode
            if (_isMultiField && fi > 0)
            {
                _fieldsPanel.Children.Add(new Border { Height = 12 });
            }

            // Add field label in multi-field mode
            if (_isMultiField)
            {
                var labelText = field.Description ?? field.Title ?? field.Name;
                _fieldsPanel.Children.Add(new TextBlock
                {
                    Text = labelText,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            switch (field.Type)
            {
                case ElicitationFieldType.Enum:
                case ElicitationFieldType.Boolean:
                    RenderOptionField(field);
                    break;
                case ElicitationFieldType.String:
                    RenderStringField(field);
                    break;
            }
        }

        // Show Submit button if multi-field OR has string fields
        UpdateSubmitVisibility(_isMultiField || _textInputs.Count > 0);
    }

    /// <summary>Renders an enum or boolean field as clickable option buttons.</summary>
    private void RenderOptionField(ElicitationFieldInfo field)
    {
        if (_fieldsPanel is null || field.Options is null) return;

        var container = new StackPanel { Spacing = 4 };

        for (var i = 0; i < field.Options.Count; i++)
        {
            var option = field.Options[i];
            var index = i + 1;

            var optionButton = CreateOptionButton(index, option.DisplayTitle,
                field.Name, option.Value, option.Value == field.DefaultValue);
            container.Children.Add(optionButton);
        }

        _fieldsPanel.Children.Add(container);
    }

    /// <summary>Creates a single clickable option button with a number badge.</summary>
    private Button CreateOptionButton(int index, string title, string fieldName, string value, bool isDefault)
    {
        // Number badge
        var badgeBorder = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = isDefault
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Child = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isDefault
                    ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            }
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                badgeBorder,
                new TextBlock
                {
                    Text = title,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = isDefault
                        ? Microsoft.UI.Text.FontWeights.SemiBold
                        : Microsoft.UI.Text.FontWeights.Normal,
                }
            }
        };

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 10, 8),
            Tag = (fieldName, value),
        };

        // Use Subtle style for non-default, mimic a list item feel
        button.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];

        button.Click += (_, _) => OnOptionSelected(fieldName, value, button);

        return button;
    }

    /// <summary>Renders a string field as a TextBox.</summary>
    private void RenderStringField(ElicitationFieldInfo field)
    {
        if (_fieldsPanel is null) return;

        var textBox = new TextBox
        {
            PlaceholderText = field.Description ?? field.Title ?? field.Name,
            Text = field.DefaultValue ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MaxLength = 500,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _textInputs[field.Name] = textBox;
        _fieldsPanel.Children.Add(textBox);
    }

    /// <summary>Shows or hides the Submit button based on whether string fields exist.</summary>
    private void UpdateSubmitVisibility(bool hasStringField)
    {
        if (_submitButton is not null)
            _submitButton.Visibility = hasStringField ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Property Change Callbacks

    private static void OnToolNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdatePromptText();
    }

    private static void OnServerNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdateServerBadge();
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdateMessageText();
    }

    private static void OnFieldsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.RenderFields();
    }

    #endregion
}
