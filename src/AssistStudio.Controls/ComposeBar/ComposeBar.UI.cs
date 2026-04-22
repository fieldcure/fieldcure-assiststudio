using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Controls.Primitives;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections;
using Windows.System;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ComposeBar
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Placeholder"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(ComposeBar),
            new PropertyMetadata("Reply..."));

    /// <summary>Identifies the <see cref="IsInputEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(nameof(IsInputEnabled), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(true, OnIsInputEnabledChanged));

    /// <summary>Identifies the <see cref="AvailablePresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(ComposeBar),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(ComposeBar),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    /// <summary>Identifies the <see cref="AvailableProfiles"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableProfilesProperty =
        DependencyProperty.Register(nameof(AvailableProfiles), typeof(IList<Profile>), typeof(ComposeBar),
            new PropertyMetadata(null, OnAvailableProfilesChanged));

    /// <summary>Identifies the <see cref="SelectedProfile"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedProfileProperty =
        DependencyProperty.Register(nameof(SelectedProfile), typeof(Profile), typeof(ComposeBar),
            new PropertyMetadata(null, OnSelectedProfileChanged));

    /// <summary>Identifies the <see cref="MaxLength"/> dependency property.</summary>
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(ComposeBar),
            new PropertyMetadata(0, OnMaxLengthChanged));

    /// <summary>Identifies the <see cref="ShowAttachButton"/> dependency property.</summary>
    public static readonly DependencyProperty ShowAttachButtonProperty =
        DependencyProperty.Register(nameof(ShowAttachButton), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(true, OnShowAttachButtonChanged));

    /// <summary>Identifies the <see cref="ShowPresetSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowPresetSelectorProperty =
        DependencyProperty.Register(nameof(ShowPresetSelector), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(true, OnShowPresetSelectorChanged));

    /// <summary>Identifies the <see cref="ShowProfileSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowProfileSelectorProperty =
        DependencyProperty.Register(nameof(ShowProfileSelector), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(true, OnShowProfileSelectorChanged));

    /// <summary>Identifies the <see cref="InputAreaMinHeight"/> dependency property.</summary>
    public static readonly DependencyProperty InputAreaMinHeightProperty =
        DependencyProperty.Register(nameof(InputAreaMinHeight), typeof(double), typeof(ComposeBar),
            new PropertyMetadata(32.0, OnInputAreaMinHeightChanged));

    /// <summary>Identifies the <see cref="AvailableTools"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableToolsProperty =
        DependencyProperty.Register(nameof(AvailableTools), typeof(IReadOnlyList<IAssistTool>), typeof(ComposeBar),
            new PropertyMetadata(null, OnAvailableToolsChanged));

    /// <summary>Identifies the <see cref="EnabledToolNames"/> dependency property.</summary>
    public static readonly DependencyProperty EnabledToolNamesProperty =
        DependencyProperty.Register(nameof(EnabledToolNames), typeof(IReadOnlyList<string>), typeof(ComposeBar),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="PasteAsAttachmentThreshold"/> dependency property.
    /// Minimum character count for pasted text to be auto-converted into an attachment chip.
    /// Set to 0 or negative to disable. Default: 500.
    /// </summary>
    public static readonly DependencyProperty PasteAsAttachmentThresholdProperty =
        DependencyProperty.Register(nameof(PasteAsAttachmentThreshold), typeof(int), typeof(ComposeBar),
            new PropertyMetadata(500));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the placeholder text displayed in the message text box.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the input area is enabled for user interaction.
    /// </summary>
    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available provider presets for the preset selector.
    /// </summary>
    public IList? AvailablePresets
    {
        get => (IList?)GetValue(AvailablePresetsProperty);
        set => SetValue(AvailablePresetsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected provider preset.
    /// </summary>
    public ProviderPreset? SelectedPreset
    {
        get => (ProviderPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available prompt presets for the prompt preset selector.
    /// </summary>
    public IList<Profile>? AvailableProfiles
    {
        get => (IList<Profile>?)GetValue(AvailableProfilesProperty);
        set => SetValue(AvailableProfilesProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected prompt preset.
    /// </summary>
    public Profile? SelectedProfile
    {
        get => (Profile?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum character length for input. 0 = unlimited (default).
    /// </summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the attach button is visible.
    /// </summary>
    public bool ShowAttachButton
    {
        get => (bool)GetValue(ShowAttachButtonProperty);
        set => SetValue(ShowAttachButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the preset (model) selector ComboBox is visible.
    /// </summary>
    public bool ShowPresetSelector
    {
        get => (bool)GetValue(ShowPresetSelectorProperty);
        set => SetValue(ShowPresetSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the profile selector ComboBox is visible.
    /// </summary>
    public bool ShowProfileSelector
    {
        get => (bool)GetValue(ShowProfileSelectorProperty);
        set => SetValue(ShowProfileSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the input text area.
    /// </summary>
    public double InputAreaMinHeight
    {
        get => (double)GetValue(InputAreaMinHeightProperty);
        set => SetValue(InputAreaMinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available tools for the tool toggle flyout.
    /// Set by the parent <see cref="ChatPanel"/> from its RegisteredTools.
    /// </summary>
    public IReadOnlyList<IAssistTool> AvailableTools
    {
        get => (IReadOnlyList<IAssistTool>?)GetValue(AvailableToolsProperty) ?? [];
        set => SetValue(AvailableToolsProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of currently enabled tool names. Null means all tools are enabled.
    /// </summary>
    public IReadOnlyList<string>? EnabledToolNames
    {
        get => (IReadOnlyList<string>?)GetValue(EnabledToolNamesProperty);
        set => SetValue(EnabledToolNamesProperty, value);
    }

    /// <summary>
    /// Gets or sets the character threshold above which pasted text is converted to an attachment chip.
    /// Set to 0 or negative to disable the feature. Default: 500.
    /// </summary>
    public int PasteAsAttachmentThreshold
    {
        get => (int)GetValue(PasteAsAttachmentThresholdProperty);
        set => SetValue(PasteAsAttachmentThresholdProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the user sends a message by pressing Enter or submitting the input.
    /// </summary>
    public event EventHandler<MessageSentEventArgs>? MessageSent;

    /// <summary>
    /// Occurs when the user selects a different provider preset from the dropdown.
    /// </summary>
    public event EventHandler<ProviderPreset>? PresetChanged;

    /// <summary>
    /// Occurs when the user selects a different prompt preset from the dropdown.
    /// </summary>
    public event EventHandler<Profile>? ProfileChanged;

    /// <summary>
    /// Occurs when the user clicks the stop button to cancel the current streaming response.
    /// </summary>
    public event EventHandler? StopRequested;

    /// <summary>
    /// Occurs when the message text box receives focus.
    /// </summary>
    public event EventHandler? InputFocused;

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old event handlers
        if (_messageTextBox is not null)
        {
            _messageTextBox.PreviewKeyDown -= MessageTextBox_PreviewKeyDown;
            _messageTextBox.Paste -= MessageTextBox_Paste;
            _messageTextBox.TextChanged -= MessageTextBox_TextChanged;
        }
        if (_attachButton is not null)
            _attachButton.Click -= AttachButton_Click;
        if (_sendButton is not null)
            _sendButton.Click -= SendButton_Click;
        if (_stopButton is not null)
            _stopButton.Click -= StopButton_Click;
        if (_presetComboBox is not null)
            _presetComboBox.SelectionChanged -= PresetComboBox_SelectionChanged;
        if (_profileComboBox is not null)
            _profileComboBox.SelectionChanged -= ProfileComboBox_SelectionChanged;

        // Get template parts
        _previewBar = GetTemplateChild("PART_PreviewBar") as AttachmentPreviewBar;
        _messageTextBox = GetTemplateChild("PART_MessageTextBox") as TextBox;
        _attachButton = GetTemplateChild("PART_AttachButton") as Button;
        _sendButton = GetTemplateChild("PART_SendButton") as Button;
        _stopButton = GetTemplateChild("PART_StopButton") as Button;
        _presetComboBox = GetTemplateChild("PART_PresetComboBox") as ComboBox;
        _profileComboBox = GetTemplateChild("PART_ProfileComboBox") as ComboBox;
        _containerBorder = GetTemplateChild("PART_ContainerBorder") as Border;
        _toolButton = GetTemplateChild("PART_ToolButton") as Button;
        if (_toolButton is not null)
            _toolButton.Click += (_, _) => RefreshToolsFlyout();

        // Apply ThemeShadow in code (XAML compiler crashes with ThemeShadow in ControlTemplate.Resources)
        if (_containerBorder is not null)
        {
            _containerBorder.Shadow = new ThemeShadow();
        }

        // Attach event handlers
        if (_messageTextBox is not null)
        {
            _messageTextBox.PreviewKeyDown += MessageTextBox_PreviewKeyDown;
            _messageTextBox.Paste += MessageTextBox_Paste;
            _messageTextBox.TextChanged += MessageTextBox_TextChanged;
            _messageTextBox.GotFocus += (_, _) => InputFocused?.Invoke(this, EventArgs.Empty);
        }
        if (_attachButton is not null)
            _attachButton.Click += AttachButton_Click;
        if (_sendButton is not null)
            _sendButton.Click += SendButton_Click;
        if (_stopButton is not null)
            _stopButton.Click += StopButton_Click;
        if (_presetComboBox is not null)
            _presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
        if (_profileComboBox is not null)
            _profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

        // Apply localized tooltips + AutomationProperties.Name (x:Uid doesn't work in TemplatedControls from library assemblies)
        try
        {
            SetTooltip(_attachButton, Res.GetString("ComposeBar_AttachTooltip"));
            SetTooltip(_stopButton, Res.GetString("ComposeBar_StopTooltip"));
            SetTooltip(_sendButton, Res.GetString("ComposeBar_SendTooltip"));
            SetTooltip(_toolButton, Res.GetString("ComposeBar_ToolsTooltip"));
        }
        catch { /* Resource not found — tooltips will be empty */ }

        if (_attachButton is not null)
            AutomationHelper.SetAutomation(_attachButton, "ComposeBarAttachButton", nameKey: "ComposeBar_AttachName");
        if (_toolButton is not null)
            AutomationHelper.SetAutomation(_toolButton, "ComposeBarToolButton", nameKey: "ComposeBar_ToolsName");
        if (_sendButton is not null)
            AutomationHelper.SetAutomation(_sendButton, "ComposeBarSendButton", nameKey: "ComposeBar_SendName");
        if (_stopButton is not null)
            AutomationHelper.SetAutomation(_stopButton, "ComposeBarStopButton", nameKey: "ComposeBar_StopName");

        // Apply deferred property values
        if (_pendingMaxLength.HasValue && _messageTextBox is not null)
        {
            _messageTextBox.MaxLength = _pendingMaxLength.Value;
            _pendingMaxLength = null;
        }
        if (_pendingMinHeight.HasValue && _messageTextBox is not null)
        {
            _messageTextBox.MinHeight = _pendingMinHeight.Value;
            _pendingMinHeight = null;
        }

        // Apply initial visibility for ShowAttachButton
        if (_attachButton is not null)
            _attachButton.Visibility = ShowAttachButton ? Visibility.Visible : Visibility.Collapsed;

        // Apply initial visibility for selector ComboBoxes
        if (_presetComboBox is not null)
            _presetComboBox.Visibility = ShowPresetSelector ? Visibility.Visible : Visibility.Collapsed;
        if (_profileComboBox is not null)
            _profileComboBox.Visibility = ShowProfileSelector ? Visibility.Visible : Visibility.Collapsed;

        // Update Send button when attachments change
        if (_previewBar is not null)
            _previewBar.Attachments.CollectionChanged += (_, _) => UpdateSendButtonState();

        // Disable Send button until text is entered
        UpdateSendButtonState();

        // Set up drag-drop on the control itself
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    #endregion

    #region Dependency Property Callbacks

    /// <summary>
    /// Called when <see cref="IsInputEnabled"/> changes to enable or disable input controls.
    /// </summary>
    private static void OnIsInputEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            var enabled = (bool)e.NewValue;
            // Keep TextBox enabled during streaming so the user can continue typing
            if (self._attachButton is not null)
                self._attachButton.IsEnabled = enabled;
            // Toggle Send ↔ Stop button
            if (self._sendButton is not null)
                self._sendButton.Visibility = enabled
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
            if (self._stopButton is not null)
                self._stopButton.Visibility = enabled
                    ? Microsoft.UI.Xaml.Visibility.Collapsed
                    : Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    /// <summary>
    /// Called when <see cref="AvailablePresets"/> changes to populate or defer ComboBox items.
    /// </summary>
    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && e.NewValue is IList presets)
        {
            if (!self.IsLoaded)
            {
                self._pendingPresetPopulate = true;
                return;
            }
            self.PopulatePresetCombo(presets);
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedPreset"/> changes to sync the ComboBox selection.
    /// </summary>
    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComposeBar self || !self.IsLoaded) return;

        if (e.NewValue is ProviderPreset preset)
        {
            self.SelectPresetInCombo(preset);
        }
        else
        {
            // Preset cleared — deselect ComboBox
            if (self._presetComboBox is not null)
            {
                self._suppressPresetChanged = true;
                self._presetComboBox.SelectedItem = null;
                self._suppressPresetChanged = false;
            }
        }
    }

    /// <summary>
    /// Called when <see cref="AvailableProfiles"/> changes to populate or defer ComboBox items.
    /// </summary>
    private static void OnAvailableProfilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            if (!self.IsLoaded)
            {
                self._pendingProfilePopulate = true;
                return;
            }
            self.PopulateProfileCombo();
        }
    }

    /// <summary>
    /// Called when <see cref="MaxLength"/> changes to apply the character limit to the text box.
    /// </summary>
    private static void OnMaxLengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            var value = (int)e.NewValue;
            if (self._messageTextBox is not null)
                self._messageTextBox.MaxLength = value;
            else
                self._pendingMaxLength = value;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowAttachButton"/> changes to show or hide the attach button.
    /// </summary>
    private static void OnShowAttachButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && self._attachButton is not null)
        {
            self._attachButton.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowPresetSelector"/> changes to show or hide the preset ComboBox.
    /// </summary>
    private static void OnShowPresetSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && self._presetComboBox is not null)
        {
            self._presetComboBox.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowProfileSelector"/> changes to show or hide the profile ComboBox.
    /// </summary>
    private static void OnShowProfileSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && self._profileComboBox is not null)
        {
            self._profileComboBox.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="InputAreaMinHeight"/> changes to set the text box minimum height.
    /// </summary>
    private static void OnInputAreaMinHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self)
        {
            var value = (double)e.NewValue;
            if (self._messageTextBox is not null)
                self._messageTextBox.MinHeight = value;
            else
                self._pendingMinHeight = value;
        }
    }

    #endregion

    #region Loaded Handler

    /// <summary>
    /// Handles the Loaded event to perform deferred ComboBox population.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_pendingPresetPopulate && AvailablePresets is { } presets)
        {
            _pendingPresetPopulate = false;
            PopulatePresetCombo(presets);
        }

        if (_pendingProfilePopulate)
        {
            _pendingProfilePopulate = false;
            PopulateProfileCombo();
        }
    }

    #endregion

    #region Input Event Handlers

    /// <summary>
    /// Handles the PreviewKeyDown event on the message text box to send on Enter (without Shift).
    /// </summary>
    private void MessageTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !IsShiftPressed())
        {
            e.Handled = true;
            TrySend();
        }
    }

    /// <summary>
    /// Handles text changes in the message text box to update the Send button enabled state.
    /// </summary>
    private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSendButtonState();
    }

    /// <summary>
    /// Handles the send button click to submit the current message.
    /// </summary>
    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        TrySend();
    }

    /// <summary>
    /// Handles the stop button click to cancel the current streaming response.
    /// </summary>
    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles the preset ComboBox selection change to propagate the new preset.
    /// </summary>
    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChanged) return;
        if (_presetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is ProviderPreset preset)
        {
            SelectedPreset = preset;
            PresetChanged?.Invoke(this, preset);
        }
    }

    /// <summary>
    /// Handles the prompt preset ComboBox selection change to propagate the new prompt preset.
    /// </summary>
    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChanged) return;
        if (_profileComboBox?.SelectedItem is ComboBoxItem item && item.Tag is Profile preset)
        {
            SelectedProfile = preset;
            ProfileChanged?.Invoke(this, preset);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets keyboard focus to the message text box.
    /// </summary>
    public void FocusInput()
    {
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Selects the matching prompt preset in the ComboBox.
    /// </summary>
    public void SelectProfileInCombo(Profile preset)
    {
        if (_profileComboBox is null) return;

        _suppressPresetChanged = true;
        foreach (var obj in _profileComboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is Profile p && p.Name == preset.Name)
            {
                _profileComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    #endregion

    #region ComboBox Management

    /// <summary>
    /// Populates the provider preset ComboBox with items from the given list.
    /// The list may contain <see cref="ProviderPreset"/> objects and <c>"-"</c> string
    /// separators to visually group providers by category (e.g., Cloud / Custom / Local / Demo).
    /// </summary>
    private void PopulatePresetCombo(IList presets)
    {
        if (_presetComboBox is null) return;

        _suppressPresetChanged = true;
        _presetComboBox.ItemsSource = null;
        _presetComboBox.Items.Clear();
        foreach (var obj in presets)
        {
            if (obj is ProviderPreset preset)
            {
                var displayName = preset.ProviderType == "Mock" ? "Demo" : preset.Name;
                var item = new ComboBoxItem { Content = displayName, Tag = preset };
                _presetComboBox.Items.Add(item);
            }
            else if (obj is "-")
            {
                _presetComboBox.Items.Add(new ComboBoxSeparatorItem());
            }
        }

        if (SelectedPreset is not null)
        {
            SelectPresetInCombo(SelectedPreset);
        }
        _suppressPresetChanged = false;
    }

    /// <summary>
    /// Selects the matching provider preset in the ComboBox without raising change events.
    /// </summary>
    private void SelectPresetInCombo(ProviderPreset preset)
    {
        if (_presetComboBox is null) return;

        _suppressPresetChanged = true;
        foreach (var obj in _presetComboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is ProviderPreset p && p.Name == preset.Name)
            {
                _presetComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    /// <summary>
    /// Populates the prompt preset ComboBox with items from the current <see cref="AvailableProfiles"/>.
    /// </summary>
    private void PopulateProfileCombo()
    {
        if (_profileComboBox is null) return;

        _suppressPresetChanged = true;
        _profileComboBox.ItemsSource = null;
        _profileComboBox.Items.Clear();
        var presets = AvailableProfiles;
        if (presets is null || presets.Count == 0)
        {
            _suppressPresetChanged = false;
            return;
        }

        foreach (var preset in presets)
        {
            var item = new ComboBoxItem { Content = preset.Name, Tag = preset };
            _profileComboBox.Items.Add(item);
        }

        if (SelectedProfile is not null)
        {
            SelectProfileInCombo(SelectedProfile);
        }
        _suppressPresetChanged = false;
    }

    #endregion

    #region UI Utilities

    /// <summary>
    /// Sends the current message text and attachments, then clears the input area.
    /// </summary>
    private void TrySend()
    {
        if (!IsInputEnabled) return;

        var text = _messageTextBox?.Text?.Trim() ?? "";
        var attachments = _previewBar?.Attachments
            .Where(a => !a.IsUnsupported).ToList() ?? [];

        if (string.IsNullOrEmpty(text) && attachments.Count == 0) return;

        if (_messageTextBox is not null)
            _messageTextBox.Text = string.Empty;
        _previewBar?.Clear();

        MessageSent?.Invoke(this, new MessageSentEventArgs(text, attachments));
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Updates the Send button enabled state based on whether the message text box has content
    /// or attachments are present.
    /// </summary>
    private void UpdateSendButtonState()
    {
        if (_sendButton is null) return;
        var hasText = !string.IsNullOrWhiteSpace(_messageTextBox?.Text);
        var hasAttachments = _previewBar?.Attachments.Count > 0;
        _sendButton.IsEnabled = hasText || hasAttachments == true;
    }

    /// <summary>
    /// Sets a tooltip on a button with placement below the control.
    /// </summary>
    private static void SetTooltip(Button? button, string? text)
    {
        if (button is null || string.IsNullOrEmpty(text)) return;
        var tooltip = new ToolTip { Content = text, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse };
        ToolTipService.SetToolTip(button, tooltip);
    }

    /// <summary>
    /// Checks whether the Shift key is currently pressed.
    /// </summary>
    private static bool IsShiftPressed()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    #endregion
}
