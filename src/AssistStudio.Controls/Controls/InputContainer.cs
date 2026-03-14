using System.Collections;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
public sealed class InputContainer : Control
{
    private static readonly HashSet<string> ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    private static readonly HashSet<string> TextExtensions =
        [".txt", ".csv", ".log", ".md", ".json", ".xml"];

    private static readonly HashSet<string> DocumentExtensions = [".pdf", ".docx"];

    /// <summary>Identifies the <see cref="Placeholder"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(InputContainer),
            new PropertyMetadata("Reply..."));

    /// <summary>Identifies the <see cref="IsInputEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(nameof(IsInputEnabled), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnIsInputEnabledChanged));

    /// <summary>Identifies the <see cref="AvailablePresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(InputContainer),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    /// <summary>Identifies the <see cref="AvailablePromptPresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePromptPresetsProperty =
        DependencyProperty.Register(nameof(AvailablePromptPresets), typeof(IList<PromptPreset>), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailablePromptPresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPromptPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPromptPresetProperty =
        DependencyProperty.Register(nameof(SelectedPromptPreset), typeof(PromptPreset), typeof(InputContainer),
            new PropertyMetadata(null));

    private bool _suppressPresetChanged;
    private bool _pendingPresetPopulate;
    private bool _pendingPromptPresetPopulate;

    private AttachmentPreviewBar? _previewBar;
    private TextBox? _messageTextBox;
    private Button? _attachButton;
    private Button? _summarizeButton;
    private ComboBox? _presetComboBox;
    private ComboBox? _promptPresetComboBox;
    private Border? _containerBorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputContainer"/> class.
    /// </summary>
    public InputContainer()
    {
        DefaultStyleKey = typeof(InputContainer);
        Loaded += OnLoaded;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old event handlers
        if (_messageTextBox is not null)
        {
            _messageTextBox.PreviewKeyDown -= MessageTextBox_PreviewKeyDown;
            _messageTextBox.Paste -= MessageTextBox_Paste;
        }
        if (_attachButton is not null)
            _attachButton.Click -= AttachButton_Click;
        if (_summarizeButton is not null)
            _summarizeButton.Click -= SummarizeButton_Click;
        if (_presetComboBox is not null)
            _presetComboBox.SelectionChanged -= PresetComboBox_SelectionChanged;
        if (_promptPresetComboBox is not null)
            _promptPresetComboBox.SelectionChanged -= PromptPresetComboBox_SelectionChanged;

        // Get template parts
        _previewBar = GetTemplateChild("PART_PreviewBar") as AttachmentPreviewBar;
        _messageTextBox = GetTemplateChild("PART_MessageTextBox") as TextBox;
        _attachButton = GetTemplateChild("PART_AttachButton") as Button;
        _summarizeButton = GetTemplateChild("PART_SummarizeButton") as Button;
        _presetComboBox = GetTemplateChild("PART_PresetComboBox") as ComboBox;
        _promptPresetComboBox = GetTemplateChild("PART_PromptPresetComboBox") as ComboBox;
        _containerBorder = GetTemplateChild("PART_ContainerBorder") as Border;

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
        }
        if (_attachButton is not null)
            _attachButton.Click += AttachButton_Click;
        if (_summarizeButton is not null)
            _summarizeButton.Click += SummarizeButton_Click;
        if (_presetComboBox is not null)
            _presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
        if (_promptPresetComboBox is not null)
            _promptPresetComboBox.SelectionChanged += PromptPresetComboBox_SelectionChanged;

        // Set up drag-drop on the control itself
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_pendingPresetPopulate && AvailablePresets is { } presets)
        {
            _pendingPresetPopulate = false;
            PopulatePresetCombo(presets);
        }

        if (_pendingPromptPresetPopulate)
        {
            _pendingPromptPresetPopulate = false;
            PopulatePromptPresetCombo();
        }
    }

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
    public IList<PromptPreset>? AvailablePromptPresets
    {
        get => (IList<PromptPreset>?)GetValue(AvailablePromptPresetsProperty);
        set => SetValue(AvailablePromptPresetsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected prompt preset.
    /// </summary>
    public PromptPreset? SelectedPromptPreset
    {
        get => (PromptPreset?)GetValue(SelectedPromptPresetProperty);
        set => SetValue(SelectedPromptPresetProperty, value);
    }

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
    public event EventHandler<PromptPreset>? PromptPresetChanged;

    /// <summary>
    /// Occurs when the user clicks the summarize button.
    /// </summary>
    public event EventHandler? SummarizeRequested;

    /// <summary>
    /// Sets keyboard focus to the message text box.
    /// </summary>
    public void FocusInput()
    {
        _messageTextBox?.Focus(FocusState.Programmatic);
    }

    private void MessageTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !IsShiftPressed())
        {
            e.Handled = true;
            TrySend();
        }
    }

    private void TrySend()
    {
        var text = _messageTextBox?.Text?.Trim() ?? "";
        var attachments = _previewBar?.Attachments.ToList() ?? [];

        if (string.IsNullOrEmpty(text) && attachments.Count == 0) return;

        if (_messageTextBox is not null)
            _messageTextBox.Text = string.Empty;
        _previewBar?.Clear();

        MessageSent?.Invoke(this, new MessageSentEventArgs(text, attachments));
    }

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

    private void SummarizeButton_Click(object sender, RoutedEventArgs e)
    {
        SummarizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChanged) return;
        if (_presetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is ProviderPreset preset)
        {
            SelectedPreset = preset;
            PresetChanged?.Invoke(this, preset);
        }
    }

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

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        await AddFilesAsync(items);
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
            var extractedText = ext switch
            {
                ".pdf" => ExtractTextFromPdf(data),
                ".docx" => ExtractTextFromDocx(data),
                _ => ""
            };

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

    private static string ExtractTextFromPdf(byte[] data)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(data);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(page.Text);
        }
        return sb.ToString();
    }

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

    private static string GetImageMimeType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private Window GetWindow()
    {
        if (XamlRoot?.Content is FrameworkElement fe)
        {
            var window = GetWindowForElement(fe);
            if (window is not null) return window;
        }
        throw new InvalidOperationException("Unable to find the parent Window for FileOpenPicker.");
    }

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

    private static bool IsShiftPressed()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private static void OnIsInputEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            var enabled = (bool)e.NewValue;
            if (self._messageTextBox is not null)
                self._messageTextBox.IsEnabled = enabled;
            if (self._attachButton is not null)
                self._attachButton.IsEnabled = enabled;
            if (self._summarizeButton is not null)
                self._summarizeButton.IsEnabled = enabled;
        }
    }

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

    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && e.NewValue is ProviderPreset preset)
        {
            if (!self.IsLoaded) return; // Will be set during pending populate
            self.SelectPresetInCombo(preset);
        }
    }

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

    private void SelectPresetInCombo(ProviderPreset preset)
    {
        if (_presetComboBox is null) return;

        _suppressPresetChanged = true;
        foreach (ComboBoxItem item in _presetComboBox.Items)
        {
            if (item.Tag is ProviderPreset p && p.Name == preset.Name)
            {
                _presetComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    private static void OnAvailablePromptPresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            if (!self.IsLoaded)
            {
                self._pendingPromptPresetPopulate = true;
                return;
            }
            self.PopulatePromptPresetCombo();
        }
    }

    private void PopulatePromptPresetCombo()
    {
        if (_promptPresetComboBox is null) return;

        _suppressPresetChanged = true;
        _promptPresetComboBox.Items.Clear();
        var presets = AvailablePromptPresets;
        if (presets is null || presets.Count == 0)
        {
            _suppressPresetChanged = false;
            return;
        }

        foreach (var preset in presets)
        {
            var item = new ComboBoxItem { Content = preset.Name, Tag = preset };
            _promptPresetComboBox.Items.Add(item);
        }

        if (SelectedPromptPreset is not null)
        {
            SelectPromptPresetInCombo(SelectedPromptPreset);
        }
        _suppressPresetChanged = false;
    }

    /// <summary>
    /// Selects the matching prompt preset in the ComboBox.
    /// </summary>
    public void SelectPromptPresetInCombo(PromptPreset preset)
    {
        if (_promptPresetComboBox is null) return;

        _suppressPresetChanged = true;
        foreach (ComboBoxItem item in _promptPresetComboBox.Items)
        {
            if (item.Tag is PromptPreset p && p.Name == preset.Name)
            {
                _promptPresetComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    private void PromptPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChanged) return;
        if (_promptPresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is PromptPreset preset)
        {
            SelectedPromptPreset = preset;
            PromptPresetChanged?.Invoke(this, preset);
        }
    }
}
