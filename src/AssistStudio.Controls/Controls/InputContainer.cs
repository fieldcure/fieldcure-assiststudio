using DocumentFormat.OpenXml.Packaging;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A templated control that provides a chat input area with text entry, file attachments,
/// preset selection, and drag-and-drop support. Default style is defined in Generic.xaml.
/// </summary>
public sealed partial class InputContainer : Control
{
    #region Constants

    /// <summary>
    /// Set of file extensions recognized as image attachments.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>
    /// Set of file extensions recognized as plain text attachments.
    /// </summary>
    private static readonly HashSet<string> TextExtensions =
        [".txt", ".csv", ".log", ".md", ".json", ".xml"];

    /// <summary>
    /// Set of file extensions recognized as document attachments requiring text extraction.
    /// </summary>
    private static readonly HashSet<string> DocumentExtensions = [".pdf", ".docx"];

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the <see cref="Placeholder"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(InputContainer),
            new PropertyMetadata("Reply..."));

    /// <summary>Identifies the <see cref="IsInputEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(nameof(IsInputEnabled), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnIsInputEnabledChanged));

    /// <summary>Identifies the <see cref="IsSummarizeEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsSummarizeEnabledProperty =
        DependencyProperty.Register(nameof(IsSummarizeEnabled), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(false, OnIsSummarizeEnabledChanged));

    /// <summary>Identifies the <see cref="AvailablePresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(InputContainer),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    /// <summary>Identifies the <see cref="AvailableProfiles"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableProfilesProperty =
        DependencyProperty.Register(nameof(AvailableProfiles), typeof(IList<Profile>), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailableProfilesChanged));

    /// <summary>Identifies the <see cref="SelectedProfile"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedProfileProperty =
        DependencyProperty.Register(nameof(SelectedProfile), typeof(Profile), typeof(InputContainer),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="MaxLength"/> dependency property.</summary>
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(InputContainer),
            new PropertyMetadata(0, OnMaxLengthChanged));

    /// <summary>Identifies the <see cref="ShowSummarizeButton"/> dependency property.</summary>
    public static readonly DependencyProperty ShowSummarizeButtonProperty =
        DependencyProperty.Register(nameof(ShowSummarizeButton), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnShowSummarizeButtonChanged));

    /// <summary>Identifies the <see cref="ShowAttachButton"/> dependency property.</summary>
    public static readonly DependencyProperty ShowAttachButtonProperty =
        DependencyProperty.Register(nameof(ShowAttachButton), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnShowAttachButtonChanged));

    /// <summary>Identifies the <see cref="InputAreaMinHeight"/> dependency property.</summary>
    public static readonly DependencyProperty InputAreaMinHeightProperty =
        DependencyProperty.Register(nameof(InputAreaMinHeight), typeof(double), typeof(InputContainer),
            new PropertyMetadata(32.0, OnInputAreaMinHeightChanged));

    /// <summary>Identifies the <see cref="AvailableTools"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableToolsProperty =
        DependencyProperty.Register(nameof(AvailableTools), typeof(IReadOnlyList<IAssistTool>), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailableToolsChanged));

    /// <summary>Identifies the <see cref="EnabledToolNames"/> dependency property.</summary>
    public static readonly DependencyProperty EnabledToolNamesProperty =
        DependencyProperty.Register(nameof(EnabledToolNames), typeof(IReadOnlyList<string>), typeof(InputContainer),
            new PropertyMetadata(null));

    #endregion

    #region Fields

    /// <summary>
    /// Flag to suppress preset changed events during programmatic ComboBox updates.
    /// </summary>
    private bool _suppressPresetChanged;

    /// <summary>
    /// Flag indicating a pending preset ComboBox population deferred until control is loaded.
    /// </summary>
    private bool _pendingPresetPopulate;

    /// <summary>
    /// Flag indicating a pending prompt preset ComboBox population deferred until control is loaded.
    /// </summary>
    private bool _pendingProfilePopulate;

    /// <summary>
    /// Pending MaxLength value deferred until the text box is available.
    /// </summary>
    private int? _pendingMaxLength;

    /// <summary>
    /// Pending InputAreaMinHeight value deferred until the text box is available.
    /// </summary>
    private double? _pendingMinHeight;

    /// <summary>
    /// Internal set of enabled tool names for the tool toggle flyout.
    /// </summary>
    private HashSet<string>? _enabledToolSet;

    #endregion

    #region Template Parts

    /// <summary>
    /// The attachment preview bar displaying file and image thumbnails.
    /// </summary>
    private AttachmentPreviewBar? _previewBar;

    /// <summary>
    /// The text box for composing chat messages.
    /// </summary>
    private TextBox? _messageTextBox;

    /// <summary>
    /// The button that opens the file picker to attach files.
    /// </summary>
    private Button? _attachButton;

    /// <summary>
    /// The button that triggers conversation summarization.
    /// </summary>
    private Button? _summarizeButton;

    /// <summary>
    /// The button that sends the current message.
    /// </summary>
    private Button? _sendButton;

    /// <summary>
    /// The button that cancels the current streaming operation.
    /// </summary>
    private Button? _stopButton;

    /// <summary>
    /// The combo box for selecting provider presets.
    /// </summary>
    private ComboBox? _presetComboBox;

    /// <summary>
    /// The combo box for selecting prompt presets.
    /// </summary>
    private ComboBox? _profileComboBox;

    /// <summary>
    /// The border container that receives a theme shadow.
    /// </summary>
    private Border? _containerBorder;

    /// <summary>
    /// The button that opens the tool toggle flyout.
    /// </summary>
    private Button? _toolButton;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="InputContainer"/> class.
    /// </summary>
    public InputContainer()
    {
        DefaultStyleKey = typeof(InputContainer);
        Loaded += OnLoaded;
    }

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
    /// Gets or sets whether the summarize button is enabled.
    /// </summary>
    public bool IsSummarizeEnabled
    {
        get => (bool)GetValue(IsSummarizeEnabledProperty);
        set => SetValue(IsSummarizeEnabledProperty, value);
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
    /// Gets or sets whether the summarize button is visible.
    /// </summary>
    public bool ShowSummarizeButton
    {
        get => (bool)GetValue(ShowSummarizeButtonProperty);
        set => SetValue(ShowSummarizeButtonProperty, value);
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
    /// Occurs when the user clicks the summarize button.
    /// </summary>
    public event EventHandler? SummarizeRequested;

    /// <summary>
    /// Occurs when the user clicks the stop button to cancel the current streaming response.
    /// </summary>
    public event EventHandler? StopRequested;

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
        if (_summarizeButton is not null)
            _summarizeButton.Click -= SummarizeButton_Click;
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
        _summarizeButton = GetTemplateChild("PART_SummarizeButton") as Button;
        _sendButton = GetTemplateChild("PART_SendButton") as Button;
        _stopButton = GetTemplateChild("PART_StopButton") as Button;
        _presetComboBox = GetTemplateChild("PART_PresetComboBox") as ComboBox;
        _profileComboBox = GetTemplateChild("PART_ProfileComboBox") as ComboBox;
        _containerBorder = GetTemplateChild("PART_ContainerBorder") as Border;
        _toolButton = GetTemplateChild("PART_ToolButton") as Button;

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
        }
        if (_attachButton is not null)
            _attachButton.Click += AttachButton_Click;
        if (_summarizeButton is not null)
            _summarizeButton.Click += SummarizeButton_Click;
        if (_sendButton is not null)
            _sendButton.Click += SendButton_Click;
        if (_stopButton is not null)
            _stopButton.Click += StopButton_Click;
        if (_presetComboBox is not null)
            _presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
        if (_profileComboBox is not null)
            _profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

        // Apply localized tooltips (x:Uid doesn't work in TemplatedControls from library assemblies)
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            SetTooltip(_attachButton, loader.GetString("InputContainer_AttachTooltip"));
            SetTooltip(_stopButton, loader.GetString("InputContainer_StopTooltip"));
            SetTooltip(_sendButton, loader.GetString("InputContainer_SendTooltip"));
        }
        catch { /* Resource not found — tooltips will be empty */ }

        // Apply initial summarize button state and tooltip
        if (_summarizeButton is not null)
        {
            ApplySummarizeVisualState(IsSummarizeEnabled && IsInputEnabled);
            UpdateSummarizeTooltip(IsSummarizeEnabled);
        }

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

        // Apply initial visibility for ShowSummarizeButton / ShowAttachButton
        if (_summarizeButton is not null)
            _summarizeButton.Visibility = ShowSummarizeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_attachButton is not null)
            _attachButton.Visibility = ShowAttachButton ? Visibility.Visible : Visibility.Collapsed;

        // Build tool flyout if tools are available
        BuildToolFlyout();

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

    #region Public Methods

    /// <summary>
    /// Sets keyboard focus to the message text box.
    /// </summary>
    public void FocusInput()
    {
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Add storage items as attachments. Called from drag-drop and external sources.
    /// </summary>
    public async Task AddFilesAsync(IReadOnlyList<IStorageItem> items)
    {
        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                var attachment = await CreateAttachmentAsync(file);
                if (attachment is not null)
                {
                    _previewBar?.AddAttachment(attachment);
                }
            }
        }
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

    #region Event Handlers

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
    /// Handles the attach button click to open a file picker and add selected files as attachments.
    /// </summary>
    private async void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in ImageExtensions) picker.FileTypeFilter.Add(ext);
        foreach (var ext in TextExtensions) picker.FileTypeFilter.Add(ext);
        foreach (var ext in DocumentExtensions) picker.FileTypeFilter.Add(ext);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(GetWindow());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return;

        foreach (var file in files)
        {
            var attachment = await CreateAttachmentAsync(file);
            if (attachment is not null)
            {
                _previewBar?.AddAttachment(attachment);
            }
        }
    }

    /// <summary>
    /// Handles the summarize button click to raise the <see cref="SummarizeRequested"/> event.
    /// </summary>
    private void SummarizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSummarizeEnabled || !IsInputEnabled) return;
        SummarizeRequested?.Invoke(this, EventArgs.Empty);
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
    /// Handles the Paste event on the message text box to support image and file paste from clipboard.
    /// </summary>
    private async void MessageTextBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        var content = Clipboard.GetContent();

        // Handle image from clipboard
        if (content.Contains(StandardDataFormats.Bitmap))
        {
            e.Handled = true;
            var streamRef = await content.GetBitmapAsync();
            using var stream = await streamRef.OpenReadAsync();
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            var attachment = new ChatAttachment
            {
                FileName = "clipboard-image.png",
                Type = AttachmentType.Image,
                Data = bytes,
                MimeType = "image/png"
            };
            _previewBar?.AddAttachment(attachment);
            return;
        }

        // Handle files from clipboard
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            var items = await content.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    var attachment = await CreateAttachmentAsync(file);
                    if (attachment is not null)
                    {
                        _previewBar?.AddAttachment(attachment);
                    }
                }
            }
            return;
        }

        // Text paste: let default behavior handle it
    }

    /// <summary>
    /// Handles the DragOver event to accept file drop operations.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    /// <summary>
    /// Handles the Drop event to add dropped files as attachments.
    /// </summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.Handled = true;
        var items = await e.DataView.GetStorageItemsAsync();
        await AddFilesAsync(items);
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

    #region Dependency Property Callbacks

    /// <summary>
    /// Called when <see cref="IsInputEnabled"/> changes to enable or disable input controls.
    /// </summary>
    private static void OnIsInputEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            var enabled = (bool)e.NewValue;
            // Keep TextBox enabled during streaming so the user can continue typing
            if (self._attachButton is not null)
                self._attachButton.IsEnabled = enabled;
            if (self._summarizeButton is not null)
                self.ApplySummarizeVisualState(enabled && self.IsSummarizeEnabled);
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
    /// Called when <see cref="IsSummarizeEnabled"/> changes to update button state and tooltip.
    /// </summary>
    private static void OnIsSummarizeEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && self._summarizeButton is not null)
        {
            var canSummarize = (bool)e.NewValue;
            self.ApplySummarizeVisualState(canSummarize && self.IsInputEnabled);
            self.UpdateSummarizeTooltip(canSummarize);
        }
    }

    /// <summary>
    /// Called when <see cref="AvailablePresets"/> changes to populate or defer ComboBox items.
    /// </summary>
    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && e.NewValue is IList presets)
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
        if (d is not InputContainer self || !self.IsLoaded) return;

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
        if (d is InputContainer self)
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
        if (d is InputContainer self)
        {
            var value = (int)e.NewValue;
            if (self._messageTextBox is not null)
                self._messageTextBox.MaxLength = value;
            else
                self._pendingMaxLength = value;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowSummarizeButton"/> changes to show or hide the summarize button.
    /// </summary>
    private static void OnShowSummarizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && self._summarizeButton is not null)
        {
            self._summarizeButton.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowAttachButton"/> changes to show or hide the attach button.
    /// </summary>
    private static void OnShowAttachButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && self._attachButton is not null)
        {
            self._attachButton.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="InputAreaMinHeight"/> changes to set the text box minimum height.
    /// </summary>
    private static void OnInputAreaMinHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            var value = (double)e.NewValue;
            if (self._messageTextBox is not null)
                self._messageTextBox.MinHeight = value;
            else
                self._pendingMinHeight = value;
        }
    }

    /// <summary>
    /// Called when <see cref="AvailableTools"/> changes to rebuild the tool toggle flyout.
    /// </summary>
    private static void OnAvailableToolsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            // Reset enabled set — all tools enabled by default
            self._enabledToolSet = null;
            self.EnabledToolNames = null;
            self.BuildToolFlyout();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Sets a tooltip on a button with placement below the control.
    /// </summary>
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

    private static void SetTooltip(Button? button, string? text)
    {
        if (button is null || string.IsNullOrEmpty(text)) return;
        var tooltip = new ToolTip { Content = text, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse };
        ToolTipService.SetToolTip(button, tooltip);
    }

    /// <summary>
    /// Applies a visual disabled/enabled state to the summarize button without changing IsEnabled,
    /// so that the tooltip remains visible even when the button appears disabled.
    /// </summary>
    private void ApplySummarizeVisualState(bool enabled)
    {
        if (_summarizeButton is null) return;
        _summarizeButton.Opacity = enabled ? 1.0 : 0.4;
    }

    /// <summary>
    /// Updates the summarize button tooltip based on whether summarization is available.
    /// </summary>
    private void UpdateSummarizeTooltip(bool canSummarize)
    {
        if (_summarizeButton is null) return;
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            var text = canSummarize
                ? loader.GetString("InputContainer_SummarizeTooltip")
                : loader.GetString("InputContainer_SummarizeDisabledTooltip");
            SetTooltip(_summarizeButton, text);
        }
        catch { /* Resource not found */ }
    }

    /// <summary>
    /// Sends the current message text and attachments, then clears the input area.
    /// </summary>
    private void TrySend()
    {
        if (!IsInputEnabled) return;

        var text = _messageTextBox?.Text?.Trim() ?? "";
        var attachments = _previewBar?.Attachments.ToList() ?? [];

        if (string.IsNullOrEmpty(text) && attachments.Count == 0) return;

        if (_messageTextBox is not null)
            _messageTextBox.Text = string.Empty;
        _previewBar?.Clear();

        MessageSent?.Invoke(this, new MessageSentEventArgs(text, attachments));
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Populates the provider preset ComboBox with items from the given list.
    /// </summary>
    private void PopulatePresetCombo(IList presets)
    {
        if (_presetComboBox is null) return;

        _suppressPresetChanged = true;
        _presetComboBox.Items.Clear();
        foreach (var obj in presets)
        {
            if (obj is ProviderPreset preset)
            {
                var item = new ComboBoxItem { Content = preset.Name, Tag = preset };
                _presetComboBox.Items.Add(item);
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

    /// <summary>
    /// Creates a <see cref="ChatAttachment"/> from a storage file by reading its contents and determining its type.
    /// </summary>
    private static async Task<ChatAttachment?> CreateAttachmentAsync(StorageFile file)
    {
        var ext = file.FileType.ToLowerInvariant();
        var isImage = ImageExtensions.Contains(ext);
        var isText = TextExtensions.Contains(ext);
        var isDocument = DocumentExtensions.Contains(ext);

        if (!isImage && !isText && !isDocument) return null;

        var buffer = await FileIO.ReadBufferAsync(file);
        var data = buffer.ToArray();

        if (isDocument)
        {
            // PDF: preserve raw bytes for native provider support; text extraction deferred to provider
            if (ext == ".pdf")
            {
                return new ChatAttachment
                {
                    FileName = file.Name,
                    Type = AttachmentType.Document,
                    Data = data,
                    MimeType = "application/pdf"
                };
            }

            // DOCX: always text-extract (no provider supports native DOCX)
            var extractedText = ExtractTextFromDocx(data);
            return new ChatAttachment
            {
                FileName = file.Name,
                Type = AttachmentType.TextFile,
                Data = Encoding.UTF8.GetBytes(extractedText),
                MimeType = "text/plain"
            };
        }

        return new ChatAttachment
        {
            FileName = file.Name,
            Type = isImage ? AttachmentType.Image : AttachmentType.TextFile,
            Data = data,
            MimeType = isImage ? GetImageMimeType(ext) : "text/plain"
        };
    }

    /// <summary>
    /// Extracts plain text content from a DOCX file byte array using Open XML SDK.
    /// </summary>
    private static string ExtractTextFromDocx(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";
        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(paragraph.InnerText);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns the MIME type string for the given image file extension.
    /// </summary>
    private static string GetImageMimeType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// Retrieves the parent <see cref="Window"/> for the current XamlRoot, used for native interop.
    /// </summary>
    private Window GetWindow()
    {
        if (XamlRoot?.Content is FrameworkElement fe)
        {
            var window = GetWindowForElement(fe);
            if (window is not null) return window;
        }
        throw new InvalidOperationException("Unable to find the parent Window for FileOpenPicker.");
    }

    /// <summary>
    /// Finds the <see cref="Window"/> that owns the given UI element by matching XamlRoot instances.
    /// </summary>
    private static Window? GetWindowForElement(UIElement element)
    {
        if (element.XamlRoot is not null)
        {
            foreach (var window in WindowHelper.ActiveWindows)
            {
                if (window.Content?.XamlRoot == element.XamlRoot)
                    return window;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks whether the Shift key is currently pressed.
    /// </summary>
    private static bool IsShiftPressed()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    /// <summary>
    /// Builds or updates the tool toggle flyout based on <see cref="AvailableTools"/>.
    /// </summary>
    private void BuildToolFlyout()
    {
        if (_toolButton is null) return;

        var tools = AvailableTools;
        if (tools.Count == 0)
        {
            _toolButton.Visibility = Visibility.Collapsed;
            _toolButton.Flyout = null;
            return;
        }

        _toolButton.Visibility = Visibility.Visible;

        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(4) };

        // Toggle all link
        var allChecked = _enabledToolSet is null || _enabledToolSet.Count == tools.Count;
        string selectAllLabel, deselectAllLabel;
        try
        {
            var res = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            selectAllLabel = res.GetString("InputContainer_SelectAll");
            deselectAllLabel = res.GetString("InputContainer_DeselectAll");
        }
        catch
        {
            selectAllLabel = "Select all";
            deselectAllLabel = "Deselect all";
        }

        var toggleLink = new HyperlinkButton
        {
            Content = allChecked ? deselectAllLabel : selectAllLabel,
            Padding = new Thickness(0),
            FontSize = 12,
        };
        toggleLink.Click += (_, _) =>
        {
            var nowAllChecked = _enabledToolSet is null || _enabledToolSet.Count == tools.Count;
            foreach (var child in panel.Children)
            {
                if (child is CheckBox cb)
                    cb.IsChecked = !nowAllChecked;
            }
            toggleLink.Content = nowAllChecked ? selectAllLabel : deselectAllLabel;
        };
        panel.Children.Add(toggleLink);

        // Separator
        panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 1,
            Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(0, 2, 0, 2),
        });

        foreach (var tool in tools)
        {
            var checkBox = new CheckBox
            {
                Content = tool.DisplayName,
                IsChecked = _enabledToolSet is null || _enabledToolSet.Contains(tool.Name),
                Tag = tool.Name,
                MinWidth = 0
            };
            checkBox.Checked += ToolCheckBox_Changed;
            checkBox.Unchecked += ToolCheckBox_Changed;
            panel.Children.Add(checkBox);
        }

        var flyout = new Flyout
        {
            Content = panel,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top
        };
        _toolButton.Flyout = flyout;

        // Apply tooltip
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");
            SetTooltip(_toolButton, loader.GetString("InputContainer_ToolsTooltip"));
        }
        catch { /* Resource not found */ }
    }

    /// <summary>
    /// Handles tool checkbox toggle to update <see cref="EnabledToolNames"/>.
    /// </summary>
    private void ToolCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not string toolName) return;

        var tools = AvailableTools;
        // Initialize the enabled set from all tools if needed
        _enabledToolSet ??= new HashSet<string>(tools.Select(t => t.Name));

        if (checkBox.IsChecked == true)
            _enabledToolSet.Add(toolName);
        else
            _enabledToolSet.Remove(toolName);

        // If all enabled, set null (meaning "all"); otherwise publish the list
        EnabledToolNames = _enabledToolSet.Count == tools.Count
            ? null
            : _enabledToolSet.ToList();
    }

    #endregion
}
