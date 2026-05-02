using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A panel that renders MCP elicitation UI. In conversations it replaces the
/// ComposeBar inline; dialog hosts can reuse it with explicit submit mode.
/// </summary>
public sealed partial class ToolElicitationPanel : Control
{
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

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

    /// <summary>Identifies the <see cref="SubmitMode"/> dependency property.</summary>
    public static readonly DependencyProperty SubmitModeProperty =
        DependencyProperty.Register(nameof(SubmitMode), typeof(ElicitationSubmitMode),
            typeof(ToolElicitationPanel),
            new PropertyMetadata(ElicitationSubmitMode.Inline, OnSubmitModeChanged));

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

    /// <summary>Gets or sets whether the panel or its host owns submit/cancel actions.</summary>
    public ElicitationSubmitMode SubmitMode
    {
        get => (ElicitationSubmitMode)GetValue(SubmitModeProperty);
        set => SetValue(SubmitModeProperty, value);
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
    private ItemsControl? _fieldsPanel;
    private FrameworkElement? _actionPanel;
    private Button? _declineButton;
    private Button? _submitButton;
    private string _declineLabel = "Skip";
    private string _submitLabel = "Submit";
    private string _promptTemplate = "{0} requires input";
    private bool _isMultiField;
    private bool _hasManualSubmitField;

    /// <summary>Projection of elicitation fields into XAML-friendly field view models.</summary>
    private readonly ObservableCollection<ToolElicitationFieldViewModel> _fieldItems = [];

    #endregion

    #region Constructor

    /// <summary>Initializes a new instance of the <see cref="ToolElicitationPanel"/> control.</summary>
    public ToolElicitationPanel()
    {
        DefaultStyleKey = typeof(ToolElicitationPanel);
        AutomationHelper.SetAutomation(this, "ToolElicitationPanel",
            nameKey: "ToolElicitationPanel_ControlName", helpTextKey: "ToolElicitationPanel_ControlHelpText");
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_declineButton is not null) _declineButton.Click -= OnDeclineClick;
        if (_submitButton is not null) _submitButton.Click -= OnSubmitClick;

        _promptText = GetTemplateChild("PART_PromptText") as TextBlock;
        _messageText = GetTemplateChild("PART_MessageText") as TextBlock;
        _serverBadge = GetTemplateChild("PART_ServerBadge") as Border;
        _serverBadgeText = GetTemplateChild("PART_ServerBadgeText") as TextBlock;
        _fieldsPanel = GetTemplateChild("PART_FieldsPanel") as ItemsControl;
        _actionPanel = GetTemplateChild("PART_ActionPanel") as FrameworkElement;
        _declineButton = GetTemplateChild("PART_DeclineButton") as Button;
        _submitButton = GetTemplateChild("PART_SubmitButton") as Button;

        if (_declineButton is not null) _declineButton.Click += OnDeclineClick;
        if (_submitButton is not null) _submitButton.Click += OnSubmitClick;

        try
        {
            _declineLabel = Res.GetString("ToolElicitation_Skip");
            _submitLabel = Res.GetString("ToolElicitation_Submit");
            _promptTemplate = Res.GetString("ToolElicitation_Prompt");
        }
        catch
        {
            // Fall back to the built-in defaults when localization is unavailable.
        }

        if (_declineButton is not null) _declineButton.Content = _declineLabel;
        if (_submitButton is not null) _submitButton.Content = _submitLabel;
        if (_fieldsPanel is not null) _fieldsPanel.ItemsSource = _fieldItems;

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

    #region Public Methods

    /// <summary>
    /// Validates the current field values. Marks each required field that is empty
    /// or whitespace as invalid (with a localized error message) and returns
    /// <see langword="false"/> when at least one required field is missing input.
    /// Fields that are no longer empty have their invalid state cleared.
    /// </summary>
    public bool TryValidate()
    {
        string errorMessage;
        try { errorMessage = Res.GetString("ToolElicitation_RequiredField"); }
        catch { errorMessage = string.Empty; }
        if (string.IsNullOrEmpty(errorMessage)) errorMessage = "Required field";

        var allValid = true;
        foreach (var field in _fieldItems)
        {
            if (!field.IsRequired)
                continue;

            if (string.IsNullOrWhiteSpace(field.CurrentValue))
            {
                field.ErrorMessage = errorMessage;
                field.IsInvalid = true;
                allValid = false;
            }
            else
            {
                field.IsInvalid = false;
            }
        }

        return allValid;
    }

    /// <summary>Collects current field values without raising panel events.</summary>
    public IDictionary<string, object?> GetContent()
    {
        var content = new Dictionary<string, object?>();
        foreach (var field in _fieldItems)
            content[field.Name] = field.CurrentValue;

        return content;
    }

    #endregion

    #region Private Methods

    /// <summary>Handles the Skip button click by raising <see cref="Declined"/>.</summary>
    private void OnDeclineClick(object sender, RoutedEventArgs e) =>
        Declined?.Invoke(this, EventArgs.Empty);

    /// <summary>Handles the Submit button click by collecting field values and raising <see cref="Submitted"/>.</summary>
    private void OnSubmitClick(object sender, RoutedEventArgs e) =>
        SubmitAllFields();

    /// <summary>Collects the current values from all projected field view models and submits them.</summary>
    private void SubmitAllFields()
    {
        if (TryValidate())
            Submitted?.Invoke(this, GetContent());
    }

    /// <summary>Updates the prompt text shown in the header.</summary>
    private void UpdatePromptText()
    {
        if (_promptText is not null)
        {
            _promptText.Text = string.Format(
                _promptTemplate,
                string.IsNullOrEmpty(ToolName) ? "Tool" : ToolName);
        }
    }

    /// <summary>Updates the optional descriptive message below the header.</summary>
    private void UpdateMessageText()
    {
        if (_messageText is not null)
        {
            _messageText.Text = Message;
            _messageText.Visibility = string.IsNullOrEmpty(Message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>Shows or hides the MCP server badge based on <see cref="ServerName"/>.</summary>
    private void UpdateServerBadge()
    {
        var hasServer = !string.IsNullOrEmpty(ServerName);
        if (_serverBadge is not null)
            _serverBadge.Visibility = hasServer ? Visibility.Visible : Visibility.Collapsed;
        if (_serverBadgeText is not null)
            _serverBadgeText.Text = ServerName;
    }

    /// <summary>Projects the field schema into XAML-friendly field view models.</summary>
    private void RenderFields()
    {
        _fieldItems.Clear();

        var fields = Fields;
        if (fields is null || fields.Count == 0)
        {
            _isMultiField = false;
            UpdateSubmitVisibility(false);
            return;
        }

        _isMultiField = fields.Count > 1;
        foreach (var field in fields)
            _fieldItems.Add(CreateFieldViewModel(field));

        UpdateSubmitVisibility(_isMultiField || _fieldItems.Any(field => field.Kind is ToolElicitationFieldKind.Text or ToolElicitationFieldKind.Password));
    }

    /// <summary>Creates a view model projection for one elicitation field.</summary>
    private ToolElicitationFieldViewModel CreateFieldViewModel(ElicitationFieldInfo field)
    {
        var labelText = field.Description ?? field.Title ?? field.Name;
        var placeholder = field.Description ?? field.Title ?? field.Name;
        var accessibilityName = field.Title ?? field.Description ?? field.Name;
        var showLabel = _isMultiField;

        return field.Type switch
        {
            ElicitationFieldType.Enum or ElicitationFieldType.Boolean => CreateOptionsFieldViewModel(field, labelText, showLabel),
            ElicitationFieldType.String when LooksLikeSecret(field) => new ToolElicitationFieldViewModel(
                field.Name,
                ToolElicitationFieldKind.Password,
                labelText,
                showLabel,
                placeholder,
                accessibilityName,
                field.DefaultValue ?? string.Empty,
                field.Required),
            _ => new ToolElicitationFieldViewModel(
                field.Name,
                ToolElicitationFieldKind.Text,
                labelText,
                showLabel,
                placeholder,
                accessibilityName,
                field.DefaultValue ?? string.Empty,
                field.Required),
        };
    }

    /// <summary>Creates an option-based field view model and wires the option selection commands.</summary>
    private ToolElicitationFieldViewModel CreateOptionsFieldViewModel(ElicitationFieldInfo field, string labelText, bool showLabel)
    {
        var selectedValue = field.DefaultValue;
        var fieldViewModel = new ToolElicitationFieldViewModel(
            field.Name,
            ToolElicitationFieldKind.Options,
            labelText,
            showLabel,
            placeholderText: null,
            accessibilityName: labelText,
            currentValue: selectedValue ?? string.Empty,
            isRequired: field.Required);

        if (field.Options is null)
            return fieldViewModel;

        for (var i = 0; i < field.Options.Count; i++)
        {
            var option = field.Options[i];
            var optionViewModel = new ToolElicitationOptionViewModel(
                (i + 1).ToString(),
                option.DisplayTitle,
                option.Value,
                option.Value == selectedValue,
                () => OnOptionSelected(fieldViewModel, option.Value));
            fieldViewModel.Options.Add(optionViewModel);
        }

        return fieldViewModel;
    }

    /// <summary>
    /// Handles an option selection. Single-field option prompts submit immediately,
    /// while multi-field prompts just update the current selection state.
    /// </summary>
    private void OnOptionSelected(ToolElicitationFieldViewModel field, string value)
    {
        field.CurrentValue = value;
        foreach (var option in field.Options)
            option.IsSelected = option.Value == value;

        if (_isMultiField || SubmitMode is ElicitationSubmitMode.Explicit)
            return;

        var content = new Dictionary<string, object?> { [field.Name] = value };
        Submitted?.Invoke(this, content);
    }

    /// <summary>Determines whether the Submit button should be visible.</summary>
    private void UpdateSubmitVisibility(bool hasManualSubmitField)
    {
        _hasManualSubmitField = hasManualSubmitField;
        UpdateActionPanelVisibility();
    }

    /// <summary>Applies the inline-action visibility setting to the action row.</summary>
    private void UpdateActionPanelVisibility()
    {
        if (_actionPanel is not null)
        {
            _actionPanel.Visibility = SubmitMode is ElicitationSubmitMode.Inline
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_submitButton is not null)
        {
            _submitButton.Visibility = SubmitMode is ElicitationSubmitMode.Inline && _hasManualSubmitField
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Heuristic for rendering a string field as a masked input. The MCP 1.2 SDK does
    /// not support <c>format: "password"</c> on StringSchema.
    /// </summary>
    private static bool LooksLikeSecret(ElicitationFieldInfo field)
    {
        if (MatchesSecretName(field.Name)) return true;
        if (ContainsSecretKeyword(field.Title)) return true;
        if (ContainsSecretKeyword(field.Description)) return true;
        return false;
    }

    /// <summary>Checks whether a field name matches a known secret pattern.</summary>
    private static bool MatchesSecretName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var normalized = name.Trim().ToLowerInvariant();
        return normalized is "api_key" or "apikey" or "token" or "secret" or "password"
               || normalized.EndsWith("_key", StringComparison.Ordinal)
               || normalized.EndsWith("_token", StringComparison.Ordinal)
               || normalized.EndsWith("_secret", StringComparison.Ordinal);
    }

    /// <summary>Checks whether free-text metadata contains a secret keyword.</summary>
    private static bool ContainsSecretKeyword(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("key", StringComparison.OrdinalIgnoreCase)
               || text.Contains("token", StringComparison.OrdinalIgnoreCase)
               || text.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || text.Contains("password", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Property Change Callbacks

    /// <summary>Callback when <see cref="ToolName"/> changes to refresh the prompt text.</summary>
    private static void OnToolNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdatePromptText();
    }

    /// <summary>Callback when <see cref="ServerName"/> changes to refresh the server badge.</summary>
    private static void OnServerNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdateServerBadge();
    }

    /// <summary>Callback when <see cref="Message"/> changes to refresh the message text.</summary>
    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdateMessageText();
    }

    /// <summary>Callback when <see cref="Fields"/> changes to re-render the field list.</summary>
    private static void OnFieldsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.RenderFields();
    }

    /// <summary>Callback when <see cref="SubmitMode"/> changes.</summary>
    private static void OnSubmitModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolElicitationPanel panel) panel.UpdateActionPanelVisibility();
    }

    #endregion
}

/// <summary>Selects the appropriate field template for one elicitation field view model.</summary>
public sealed partial class ToolElicitationFieldTemplateSelector : DataTemplateSelector
{
    /// <summary>Template used for option-based fields.</summary>
    public DataTemplate? OptionsTemplate { get; set; }

    /// <summary>Template used for plain text fields.</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <summary>Template used for password-like fields.</summary>
    public DataTemplate? PasswordTemplate { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is not ToolElicitationFieldViewModel field)
            return base.SelectTemplateCore(item, container);

        return field.Kind switch
        {
            ToolElicitationFieldKind.Options when OptionsTemplate is not null => OptionsTemplate,
            ToolElicitationFieldKind.Password when PasswordTemplate is not null => PasswordTemplate,
            ToolElicitationFieldKind.Text when TextTemplate is not null => TextTemplate,
            _ => base.SelectTemplateCore(item, container),
        };
    }
}

/// <summary>Binds <see cref="PasswordBox.Password"/> to a view model-friendly attached property.</summary>
public static class PasswordBoxBindingHelper
{
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBindingHelper),
            new PropertyMetadata(false));

    /// <summary>Identifies the attached bound-password property.</summary>
    /// <remarks>
    /// Default is <see langword="null"/> (not <see cref="string.Empty"/>) so the binding's
    /// initial set to "" still triggers <see cref="OnBoundPasswordChanged"/> — that callback
    /// is where <c>PasswordChanged</c> gets subscribed, and DP change callbacks don't fire
    /// when the new value equals the existing default.
    /// </remarks>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBindingHelper),
            new PropertyMetadata(null, OnBoundPasswordChanged));

    /// <summary>Gets the bound password value.</summary>
    public static string GetBoundPassword(DependencyObject obj) =>
        (string)obj.GetValue(BoundPasswordProperty);

    /// <summary>Sets the bound password value.</summary>
    public static void SetBoundPassword(DependencyObject obj, string value) =>
        obj.SetValue(BoundPasswordProperty, value);

    /// <summary>Updates the real PasswordBox value when the bound property changes.</summary>
    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox)
            return;

        passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
        if (!(bool)passwordBox.GetValue(IsUpdatingProperty))
            passwordBox.Password = e.NewValue as string ?? string.Empty;
        passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
    }

    /// <summary>Pushes password edits back into the attached bound-password property.</summary>
    private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
            return;

        passwordBox.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        // WinUI 3 does not propagate attached-DP SetValue to TwoWay binding sources,
        // so push to the field view model directly.
        if (passwordBox.DataContext is ToolElicitationFieldViewModel vm)
            vm.CurrentValue = passwordBox.Password;
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}

/// <summary>Represents one XAML-facing elicitation field projection.</summary>
public sealed partial class ToolElicitationFieldViewModel : INotifyPropertyChanged
{
    private string _currentValue;
    private bool _isInvalid;
    private string _errorMessage = string.Empty;

    /// <summary>Initializes a new field view model.</summary>
    public ToolElicitationFieldViewModel(
        string name,
        ToolElicitationFieldKind kind,
        string labelText,
        bool showLabel,
        string? placeholderText,
        string accessibilityName,
        string currentValue,
        bool isRequired = false)
    {
        Name = name;
        Kind = kind;
        LabelText = labelText;
        LabelVisibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderText = placeholderText;
        AccessibilityName = accessibilityName;
        _currentValue = currentValue;
        IsRequired = isRequired;
    }

    /// <summary>Raised when a bindable property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the result key for this field.</summary>
    public string Name { get; }

    /// <summary>Gets the rendering kind for this field.</summary>
    public ToolElicitationFieldKind Kind { get; }

    /// <summary>Gets the optional label shown above the field.</summary>
    public string LabelText { get; }

    /// <summary>Gets whether the label should be visible.</summary>
    public Visibility LabelVisibility { get; }

    /// <summary>Gets the placeholder text for text-based inputs.</summary>
    public string? PlaceholderText { get; }

    /// <summary>Gets the accessibility label for the input control.</summary>
    public string AccessibilityName { get; }

    /// <summary>Gets whether the field must contain a non-empty value before submission.</summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Gets the current text or selected option value for the field. Setting a non-empty
    /// value automatically clears the invalid flag so the error indicator hides as the
    /// user fixes the field.
    /// </summary>
    public string CurrentValue
    {
        get => _currentValue;
        set
        {
            if (!SetProperty(ref _currentValue, value))
                return;

            if (_isInvalid && !string.IsNullOrWhiteSpace(value))
                IsInvalid = false;
        }
    }

    /// <summary>Gets or sets whether the field currently fails validation.</summary>
    public bool IsInvalid
    {
        get => _isInvalid;
        set
        {
            if (!SetProperty(ref _isInvalid, value))
                return;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorVisibility)));
        }
    }

    /// <summary>Gets or sets the validation error message shown when the field is invalid.</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Gets the visibility of the inline error message based on <see cref="IsInvalid"/>.</summary>
    public Visibility ErrorVisibility => _isInvalid ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gets the option list for enum/boolean fields.</summary>
    public ObservableCollection<ToolElicitationOptionViewModel> Options { get; } = [];

    /// <summary>Updates a property backing field and raises change notification.</summary>
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>Represents one selectable option within an option-based elicitation field.</summary>
public sealed partial class ToolElicitationOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    /// <summary>Initializes a new option view model.</summary>
    public ToolElicitationOptionViewModel(
        string indexText,
        string title,
        string value,
        bool isSelected,
        Action selectAction)
    {
        IndexText = indexText;
        Title = title;
        Value = value;
        _isSelected = isSelected;
        SelectCommand = new DelegateCommand(selectAction);
    }

    /// <summary>Raised when a bindable property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the display index shown in the option badge.</summary>
    public string IndexText { get; }

    /// <summary>Gets the option title shown to the user.</summary>
    public string Title { get; }

    /// <summary>Gets the result value sent back when this option is chosen.</summary>
    public string Value { get; }

    /// <summary>Gets the command that selects this option.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Gets or sets whether this option is the selected one.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            RaiseAllVisualPropertiesChanged();
        }
    }

    /// <summary>Gets the visibility of the selected-state visuals (badge background + text).</summary>
    public Visibility SelectedVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gets the visibility of the unselected-state visuals.</summary>
    public Visibility UnselectedVisibility => _isSelected ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Gets the title font weight for the current selection state.</summary>
    public Windows.UI.Text.FontWeight TitleFontWeight =>
        _isSelected ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>Raises change notifications for the selection-dependent visual properties.</summary>
    private void RaiseAllVisualPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnselectedVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleFontWeight)));
    }
}

/// <summary>Distinguishes the input template used for one elicitation field.</summary>
public enum ToolElicitationFieldKind
{
    /// <summary>Option buttons for enum/boolean fields.</summary>
    Options,

    /// <summary>Plain text input.</summary>
    Text,

    /// <summary>Masked password-style input.</summary>
    Password,
}

/// <summary>Determines whether elicitation submission is handled inline or by a host dialog.</summary>
public enum ElicitationSubmitMode
{
    /// <summary>The panel shows and owns its built-in action buttons.</summary>
    Inline,

    /// <summary>The host owns submit/cancel actions and the panel only renders fields.</summary>
    Explicit,
}

/// <summary>Minimal command wrapper for invoking a provided delegate from XAML.</summary>
internal sealed partial class DelegateCommand(Action execute) : ICommand
{
    /// <summary>Unused because this command is always executable.</summary>
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <summary>Always returns <c>true</c>.</summary>
    public bool CanExecute(object? parameter) => true;

    /// <summary>Invokes the wrapped delegate.</summary>
    public void Execute(object? parameter) => execute();
}
