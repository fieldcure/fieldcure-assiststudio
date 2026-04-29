using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Controls.Primitives;
using FieldCure.AssistStudio.Core.Models;
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

    /// <summary>Identifies the <see cref="AvailableModels"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableModelsProperty =
        DependencyProperty.Register(nameof(AvailableModels), typeof(IList), typeof(ComposeBar),
            new PropertyMetadata(null, OnAvailableModelsChanged));

    /// <summary>Identifies the <see cref="SelectedModel"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedModelProperty =
        DependencyProperty.Register(nameof(SelectedModel), typeof(ProviderModel), typeof(ComposeBar),
            new PropertyMetadata(null, OnSelectedModelChanged));

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

    /// <summary>Identifies the <see cref="ShowModelSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowModelSelectorProperty =
        DependencyProperty.Register(nameof(ShowModelSelector), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(true, OnShowModelSelectorChanged));

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

    /// <summary>
    /// Identifies the <see cref="IsEditing"/> dependency property. Toggles the edit banner.
    /// </summary>
    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(ComposeBar),
            new PropertyMetadata(false, OnIsEditingChanged));

    /// <summary>
    /// Identifies the <see cref="AudioCapability"/> dependency property. Drives send-time reject behavior.
    /// </summary>
    public static readonly DependencyProperty AudioCapabilityProperty =
        DependencyProperty.Register(nameof(AudioCapability), typeof(AudioCapability), typeof(ComposeBar),
            new PropertyMetadata(AudioCapability.NotSupported, OnAudioContextChanged));

    /// <summary>
    /// Identifies the <see cref="AudioProviderName"/> dependency property. Used to look up provider-supported MIMEs.
    /// </summary>
    public static readonly DependencyProperty AudioProviderNameProperty =
        DependencyProperty.Register(nameof(AudioProviderName), typeof(string), typeof(ComposeBar),
            new PropertyMetadata(null, OnAudioContextChanged));

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
    public IList? AvailableModels
    {
        get => (IList?)GetValue(AvailableModelsProperty);
        set => SetValue(AvailableModelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected provider preset.
    /// </summary>
    public ProviderModel? SelectedModel
    {
        get => (ProviderModel?)GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
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
    public bool ShowModelSelector
    {
        get => (bool)GetValue(ShowModelSelectorProperty);
        set => SetValue(ShowModelSelectorProperty, value);
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

    /// <summary>
    /// Gets or sets whether the compose bar is currently in edit mode (editing an existing message).
    /// When true, the edit banner is visible above the input area.
    /// </summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets the audio handling capability of the currently active provider/model.
    /// </summary>
    public AudioCapability AudioCapability
    {
        get => (AudioCapability)GetValue(AudioCapabilityProperty);
        set => SetValue(AudioCapabilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the active provider name (e.g. "Gemini", "OpenAI") used to look up
    /// provider-specific audio MIME support and limit messages.
    /// </summary>
    public string? AudioProviderName
    {
        get => (string?)GetValue(AudioProviderNameProperty);
        set => SetValue(AudioProviderNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the current text in the message text box.
    /// Passthrough used by external edit-mode controllers.
    /// </summary>
    public string Text
    {
        get => _messageTextBox?.Text ?? string.Empty;
        set
        {
            if (_messageTextBox is not null)
                _messageTextBox.Text = value ?? string.Empty;
        }
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
    public event EventHandler<ProviderModel>? ModelChanged;

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

    /// <summary>
    /// Occurs when the user clicks the cancel button on the edit banner or presses Escape
    /// while in edit mode. The host is responsible for setting <see cref="IsEditing"/> = false
    /// and restoring conversation state.
    /// </summary>
    public event EventHandler? EditCanceled;

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
        if (_modelPicker is not null)
            _modelPicker.SelectionChanged -= OnModelPickerSelectionChanged;
        if (_profileComboBox is not null)
            _profileComboBox.SelectionChanged -= ProfileComboBox_SelectionChanged;

        // Get template parts
        _previewBar = GetTemplateChild("PART_PreviewBar") as AttachmentPreviewBar;
        _messageTextBox = GetTemplateChild("PART_MessageTextBox") as TextBox;
        _attachButton = GetTemplateChild("PART_AttachButton") as Button;
        _sendButton = GetTemplateChild("PART_SendButton") as Button;
        _stopButton = GetTemplateChild("PART_StopButton") as Button;
        _modelPicker = GetTemplateChild("PART_ModelPicker") as ModelPicker;
        _profileComboBox = GetTemplateChild("PART_ProfileComboBox") as ComboBox;
        _containerBorder = GetTemplateChild("PART_ContainerBorder") as Border;
        _toolButton = GetTemplateChild("PART_ToolButton") as Button;
        if (_toolButton is not null)
            _toolButton.Click += (_, _) => RefreshToolsFlyout();

        if (_editBannerCancelButton is not null)
            _editBannerCancelButton.Click -= EditBannerCancelButton_Click;
        _editBanner = GetTemplateChild("PART_EditBanner") as Grid;
        _editBannerLabel = GetTemplateChild("PART_EditBannerLabel") as TextBlock;
        _editBannerCancelButton = GetTemplateChild("PART_EditBannerCancelButton") as Button;
        if (_editBannerCancelButton is not null)
            _editBannerCancelButton.Click += EditBannerCancelButton_Click;

        if (_audioRejectCancelButton is not null)
            _audioRejectCancelButton.Click -= AudioRejectCancelButton_Click;
        if (_audioRejectSendButton is not null)
            _audioRejectSendButton.Click -= AudioRejectSendButton_Click;
        _audioRejectBar = GetTemplateChild("PART_AudioRejectBar") as Grid;
        _audioRejectLabel = GetTemplateChild("PART_AudioRejectLabel") as TextBlock;
        _audioRejectCancelButton = GetTemplateChild("PART_AudioRejectCancelButton") as Button;
        _audioRejectSendButton = GetTemplateChild("PART_AudioRejectSendButton") as Button;
        if (_audioRejectCancelButton is not null)
            _audioRejectCancelButton.Click += AudioRejectCancelButton_Click;
        if (_audioRejectSendButton is not null)
            _audioRejectSendButton.Click += AudioRejectSendButton_Click;

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
        if (_modelPicker is not null)
            _modelPicker.SelectionChanged += OnModelPickerSelectionChanged;
        if (_profileComboBox is not null)
            _profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

        // Apply localized tooltips + AutomationProperties.Name (x:Uid doesn't work in TemplatedControls from library assemblies)
        try
        {
            SetTooltip(_attachButton, Res.GetString("ComposeBar_AttachTooltip"));
            SetTooltip(_stopButton, Res.GetString("ComposeBar_StopTooltip"));
            SetTooltip(_sendButton, Res.GetString("ComposeBar_SendTooltip"));
            SetTooltip(_toolButton, Res.GetString("ComposeBar_ToolsTooltip"));
            SetTooltip(_editBannerCancelButton, Res.GetString("ComposeBar_EditBanner_CancelTooltip"));
            if (_editBannerLabel is not null)
                _editBannerLabel.Text = Res.GetString("ComposeBar_EditBanner_Label") ?? string.Empty;
            if (_audioRejectCancelButton is not null)
                _audioRejectCancelButton.Content = Res.GetString("ComposeBar_AudioReject_CancelLabel") ?? "Cancel";
            if (_audioRejectSendButton is not null)
                _audioRejectSendButton.Content = Res.GetString("ComposeBar_AudioReject_SendLabel") ?? "Send";
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
        if (_editBannerCancelButton is not null)
            AutomationHelper.SetAutomation(_editBannerCancelButton, "ComposeBarEditCancelButton", nameKey: "ComposeBar_EditBanner_CancelName");

        // Apply current IsEditing state (XAML default Visibility="Collapsed" already correct for false)
        if (_editBanner is not null)
            _editBanner.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;

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

        // Apply initial visibility for selectors
        if (_modelPicker is not null)
            _modelPicker.Visibility = ShowModelSelector ? Visibility.Visible : Visibility.Collapsed;
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
    /// Called when <see cref="AvailableModels"/> changes to populate or defer the picker.
    /// </summary>
    private static void OnAvailableModelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && e.NewValue is IList models)
        {
            if (!self.IsLoaded)
            {
                self._pendingModelPopulate = true;
                return;
            }
            self.PopulateModelPicker(models);
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedModel"/> changes to sync the picker selection.
    /// </summary>
    private static void OnSelectedModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComposeBar self || !self.IsLoaded) return;

        if (e.NewValue is ProviderModel model)
        {
            self.SelectModelInPicker(model);
        }
        else
        {
            // Model cleared — deselect picker
            if (self._modelPicker is not null)
            {
                self._suppressModelChanged = true;
                self._modelPicker.SelectedItem = null;
                self._suppressModelChanged = false;
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
    /// Called when <see cref="ShowModelSelector"/> changes to show or hide the model picker.
    /// </summary>
    private static void OnShowModelSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && self._modelPicker is not null)
        {
            self._modelPicker.Visibility = (bool)e.NewValue
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

    /// <summary>
    /// Called when <see cref="IsEditing"/> changes to show or hide the edit banner.
    /// </summary>
    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBar self && self._editBanner is not null)
        {
            self._editBanner.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
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

        if (_pendingModelPopulate && AvailableModels is { } models)
        {
            _pendingModelPopulate = false;
            PopulateModelPicker(models);
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
        if (e.Key == VirtualKey.Escape && IsEditing)
        {
            e.Handled = true;
            EditCanceled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.Key == VirtualKey.Enter && !IsShiftPressed())
        {
            e.Handled = true;
            TrySend();
        }
    }

    /// <summary>
    /// Handles the cancel-edit button click on the edit banner.
    /// </summary>
    private void EditBannerCancelButton_Click(object sender, RoutedEventArgs e)
    {
        EditCanceled?.Invoke(this, EventArgs.Empty);
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
    /// Handles the <see cref="ModelPicker.SelectionChanged"/> event to propagate the new model.
    /// </summary>
    /// <param name="sender">The model picker.</param>
    /// <param name="entry">The selected entry (or null when cleared).</param>
    private void OnModelPickerSelectionChanged(object? sender, ModelPickerEntry? entry)
    {
        if (_suppressModelChanged) return;
        if (entry?.Tag is ProviderModel model)
        {
            SelectedModel = model;
            ModelChanged?.Invoke(this, model);
        }
    }

    /// <summary>
    /// Handles the prompt preset ComboBox selection change to propagate the new prompt preset.
    /// </summary>
    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModelChanged) return;
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
    /// Adds an attachment to the preview bar. Passthrough to <see cref="AttachmentPreviewBar.AddAttachment"/>.
    /// </summary>
    public void AddAttachment(ChatAttachment attachment)
    {
        _previewBar?.AddAttachment(attachment);
    }

    /// <summary>
    /// Clears all attachments from the preview bar.
    /// </summary>
    public void ClearAttachments()
    {
        _previewBar?.Clear();
    }

    /// <summary>
    /// Returns a snapshot of the currently attached items (excluding unsupported entries).
    /// </summary>
    public IReadOnlyList<ChatAttachment> GetAttachments()
    {
        if (_previewBar is null) return [];
        return [.. _previewBar.Attachments.Where(a => !a.IsUnsupported)];
    }

    /// <summary>
    /// Selects the matching prompt preset in the ComboBox.
    /// </summary>
    public void SelectProfileInCombo(Profile preset)
    {
        if (_profileComboBox is null) return;

        _suppressModelChanged = true;
        foreach (var obj in _profileComboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is Profile p && p.Name == preset.Name)
            {
                _profileComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressModelChanged = false;
    }

    #endregion

    #region ComboBox Management

    /// <summary>
    /// Populates the <see cref="ModelPicker"/> with entries from the given list.
    /// The list may contain <see cref="ProviderModel"/> objects and literal <c>"-"</c>
    /// string separators (the format produced by host-side ordered builders).
    /// Separators are dropped because the picker performs its own grouping by
    /// <see cref="ModelPickerEntry.GroupKey"/>.
    /// </summary>
    /// <param name="models">The source list of models (and optional separator strings).</param>
    private void PopulateModelPicker(IList models)
    {
        if (_modelPicker is null) return;

        _suppressModelChanged = true;
        var entries = BuildEntries(models);
        _modelPicker.ItemsSource = (IList)entries;

        if (SelectedModel is not null)
        {
            SelectModelInPicker(SelectedModel);
        }
        _suppressModelChanged = false;
    }

    /// <summary>
    /// Selects the entry in the picker matching the given <see cref="ProviderModel"/>
    /// (by <see cref="ProviderModel.ProviderType"/> + <see cref="ProviderModel.ModelId"/>),
    /// without raising change events.
    /// </summary>
    /// <param name="model">The model to select.</param>
    private void SelectModelInPicker(ProviderModel model)
    {
        if (_modelPicker?.ItemsSource is not IList items) return;

        _suppressModelChanged = true;
        foreach (var obj in items)
        {
            if (obj is ModelPickerEntry entry &&
                entry.GroupKey == model.ProviderType &&
                entry.ModelId == model.ModelId)
            {
                _modelPicker.SelectedItem = entry;
                break;
            }
        }
        _suppressModelChanged = false;
    }

    /// <summary>
    /// Projects an interleaved list of <see cref="ProviderModel"/> + <c>"-"</c>
    /// separators into <see cref="ModelPickerEntry"/> entries. The separator
    /// strings are dropped. <c>Custom_*</c> provider-type entries fall back to
    /// the literal <c>ProviderType</c> as the group label because this control
    /// library is host-agnostic and cannot resolve user-defined custom display
    /// names; hosts that need richer labels can build entries themselves and
    /// assign <see cref="ModelPicker.ItemsSource"/> directly.
    /// </summary>
    /// <param name="models">The source list (contains <see cref="ProviderModel"/> and
    /// optional <c>"-"</c> separator strings).</param>
    /// <returns>The projected entries.</returns>
    private static IList<ModelPickerEntry> BuildEntries(IList models)
    {
        var result = new List<ModelPickerEntry>(models.Count);
        foreach (var obj in models)
        {
            if (obj is not ProviderModel model) continue;
            result.Add(new ModelPickerEntry
            {
                ModelId = model.ModelId,
                DisplayName = model.ModelId,
                GroupKey = model.ProviderType,
                GroupDisplayName = model.ProviderType == "Mock" ? "demo" : model.ProviderType,
                Tag = model,
            });
        }
        return result;
    }

    /// <summary>
    /// Populates the prompt preset ComboBox with items from the current <see cref="AvailableProfiles"/>.
    /// </summary>
    private void PopulateProfileCombo()
    {
        if (_profileComboBox is null) return;

        _suppressModelChanged = true;
        _profileComboBox.ItemsSource = null;
        _profileComboBox.Items.Clear();
        var presets = AvailableProfiles;
        if (presets is null || presets.Count == 0)
        {
            _suppressModelChanged = false;
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
        _suppressModelChanged = false;
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

        // Audio capability gate — show inline reject bar instead of sending.
        // User must explicitly accept dropping audio (or cancel and revise).
        var rejectMessage = ClassifyAudioReject(attachments, out var offendingAudio);
        if (rejectMessage is not null)
        {
            _pendingAudioReject = offendingAudio;
            ShowAudioRejectBar(rejectMessage);
            return;
        }

        DispatchSend(text, attachments);
    }

    /// <summary>
    /// Performs the actual send: clears input, raises <see cref="MessageSent"/>, refocuses textbox.
    /// </summary>
    private void DispatchSend(string text, List<ChatAttachment> attachments)
    {
        if (_messageTextBox is not null)
            _messageTextBox.Text = string.Empty;
        _previewBar?.Clear();
        HideAudioRejectBar();

        MessageSent?.Invoke(this, new MessageSentEventArgs(text, attachments));
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Classifies whether the pending send should be blocked by audio capability rules.
    /// Returns the localized rejection message and the offending audio attachments, or
    /// <c>null</c> when send may proceed unchanged.
    /// </summary>
    private string? ClassifyAudioReject(
        IReadOnlyList<ChatAttachment> attachments,
        out List<ChatAttachment> offending)
    {
        offending = [];
        var audioAttachments = attachments.Where(a => a.Type == AttachmentType.Audio).ToList();
        if (audioAttachments.Count == 0) return null;

        // Case 1: provider does not accept any audio.
        if (AudioCapability != AudioCapability.NativeAudio)
        {
            offending = audioAttachments;
            return Res.GetString("ComposeBar_AudioReject_NotSupported");
        }

        var supportedMimes = FieldCure.Ai.Providers.Helpers.AudioMimeHelper
            .GetSupportedMimes(AudioProviderName ?? string.Empty, AudioCapability);

        // Case 2: partial format mismatch — at least one audio is in an unsupported MIME.
        if (supportedMimes is not null)
        {
            var unsupported = audioAttachments
                .Where(a => a.MimeType is null || !supportedMimes.Contains(a.MimeType))
                .ToList();
            if (unsupported.Count > 0)
            {
                offending = unsupported;
                var extList = string.Join(", ",
                    unsupported.Select(a => Path.GetExtension(a.FileName).TrimStart('.')).Distinct());
                var format = Res.GetString("ComposeBar_AudioReject_PartialFormat") ?? "{0}";
                return string.Format(format, extList);
            }
        }

        // Case 3: provider-specific size limit (Gemini 20 MB inline).
        if (string.Equals(AudioProviderName, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var oversize = audioAttachments
                .Where(a => a.Data.LongLength > FieldCure.Ai.Providers.Helpers.AudioMimeHelper.GeminiInlineSizeLimit)
                .ToList();
            if (oversize.Count > 0)
            {
                offending = oversize;
                var first = oversize[0];
                var sizeMb = first.Data.LongLength / (1024.0 * 1024.0);
                var limitMb = FieldCure.Ai.Providers.Helpers.AudioMimeHelper.GeminiInlineSizeLimit / (1024 * 1024);
                var format = Res.GetString("ComposeBar_AudioReject_SizeLimit") ?? "{0} {1} {2} {3}";
                return string.Format(format, "Gemini", limitMb, first.FileName, sizeMb.ToString("0.#"));
            }
        }

        return null;
    }

    /// <summary>
    /// Shows the audio reject bar with the given message. Bar stays open until user clicks
    /// Cancel (close + keep state) or Send (drop offending audio + dispatch).
    /// </summary>
    private void ShowAudioRejectBar(string message)
    {
        if (_audioRejectBar is null) return;
        if (_audioRejectLabel is not null) _audioRejectLabel.Text = message;
        _audioRejectBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides the audio reject bar and clears the pending state.
    /// </summary>
    private void HideAudioRejectBar()
    {
        if (_audioRejectBar is not null) _audioRejectBar.Visibility = Visibility.Collapsed;
        _pendingAudioReject = null;
    }

    /// <summary>
    /// Reactive change handler for <see cref="AudioCapability"/> / <see cref="AudioProviderName"/>.
    /// Default state (no reject pending) ignores capability changes per spec section 1.2.
    /// When the reject bar is open the user is mid-decision, so re-classify with the new capability:
    /// dismiss the bar if the rejection no longer applies, otherwise refresh the message in place.
    /// </summary>
    private static void OnAudioContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComposeBar self) return;
        if (self._pendingAudioReject is null) return; // bar not visible — nothing to recompute

        var attachments = self._previewBar?.Attachments
            .Where(a => !a.IsUnsupported).ToList() ?? [];
        var newMessage = self.ClassifyAudioReject(attachments, out var newOffending);
        if (newMessage is null)
        {
            self.HideAudioRejectBar();
        }
        else
        {
            self._pendingAudioReject = newOffending;
            if (self._audioRejectLabel is not null) self._audioRejectLabel.Text = newMessage;
        }
    }

    /// <summary>
    /// Handles the audio reject bar Cancel button: closes the bar, leaves attachments intact,
    /// allows the user to revise their selection or switch models.
    /// </summary>
    private void AudioRejectCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideAudioRejectBar();
    }

    /// <summary>
    /// Handles the audio reject bar Send button: drops the offending audio attachments
    /// from the preview bar and dispatches the send with the remaining content.
    /// </summary>
    private void AudioRejectSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewBar is null) return;
        if (_pendingAudioReject is { } drop)
        {
            foreach (var att in drop)
            {
                if (_previewBar.Attachments.Contains(att))
                    _previewBar.Attachments.Remove(att);
            }
        }

        var text = _messageTextBox?.Text?.Trim() ?? "";
        var attachments = _previewBar.Attachments.Where(a => !a.IsUnsupported).ToList();
        if (string.IsNullOrEmpty(text) && attachments.Count == 0)
        {
            HideAudioRejectBar();
            return;
        }
        DispatchSend(text, attachments);
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
