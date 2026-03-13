using System.Collections;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentView.AI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace FluentView.AI.Controls;

public sealed partial class InputContainer : UserControl
{
    private static readonly HashSet<string> ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    private static readonly HashSet<string> TextExtensions =
        [".txt", ".csv", ".log", ".md", ".json", ".xml"];

    private static readonly HashSet<string> DocumentExtensions = [".pdf", ".docx"];

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(InputContainer),
            new PropertyMetadata("Reply..."));

    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(nameof(IsInputEnabled), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnIsInputEnabledChanged));

    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(InputContainer),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    public static readonly DependencyProperty AvailablePromptPresetsProperty =
        DependencyProperty.Register(nameof(AvailablePromptPresets), typeof(IList<PromptPreset>), typeof(InputContainer),
            new PropertyMetadata(null, OnAvailablePromptPresetsChanged));

    public static readonly DependencyProperty SelectedPromptPresetProperty =
        DependencyProperty.Register(nameof(SelectedPromptPreset), typeof(PromptPreset), typeof(InputContainer),
            new PropertyMetadata(null));

    private bool _suppressPresetChanged;

    public InputContainer()
    {
        InitializeComponent();
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    public IList? AvailablePresets
    {
        get => (IList?)GetValue(AvailablePresetsProperty);
        set => SetValue(AvailablePresetsProperty, value);
    }

    public ProviderPreset? SelectedPreset
    {
        get => (ProviderPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public IList<PromptPreset>? AvailablePromptPresets
    {
        get => (IList<PromptPreset>?)GetValue(AvailablePromptPresetsProperty);
        set => SetValue(AvailablePromptPresetsProperty, value);
    }

    public PromptPreset? SelectedPromptPreset
    {
        get => (PromptPreset?)GetValue(SelectedPromptPresetProperty);
        set => SetValue(SelectedPromptPresetProperty, value);
    }

    public event EventHandler<MessageSentEventArgs>? MessageSent;
    public event EventHandler<ProviderPreset>? PresetChanged;
    public event EventHandler<PromptPreset>? PromptPresetChanged;
    public event EventHandler? SummarizeRequested;

    public void FocusInput()
    {
        MessageTextBox.Focus(FocusState.Programmatic);
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
        var text = MessageTextBox.Text?.Trim() ?? "";
        var attachments = PreviewBar.Attachments.ToList();

        if (string.IsNullOrEmpty(text) && attachments.Count == 0) return;

        MessageTextBox.Text = string.Empty;
        PreviewBar.Clear();

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
                PreviewBar.AddAttachment(attachment);
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
        if (PresetComboBox.SelectedItem is ComboBoxItem item && item.Tag is ProviderPreset preset)
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
            PreviewBar.AddAttachment(attachment);
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
                        PreviewBar.AddAttachment(attachment);
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
                    PreviewBar.AddAttachment(attachment);
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
        foreach (var paragraph in body.Elements<Paragraph>())
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
            self.MessageTextBox.IsEnabled = enabled;
            self.AttachButton.IsEnabled = enabled;
            self.SummarizeButton.IsEnabled = enabled;
        }
    }

    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && e.NewValue is IList presets)
        {
            self.PopulatePresetCombo(presets);
        }
    }

    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self && e.NewValue is ProviderPreset preset)
        {
            self.SelectPresetInCombo(preset);
        }
    }

    private void PopulatePresetCombo(IList presets)
    {
        _suppressPresetChanged = true;
        PresetComboBox.Items.Clear();
        foreach (var obj in presets)
        {
            if (obj is ProviderPreset preset)
            {
                var item = new ComboBoxItem { Content = preset.Name, Tag = preset };
                PresetComboBox.Items.Add(item);
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
        _suppressPresetChanged = true;
        foreach (ComboBoxItem item in PresetComboBox.Items)
        {
            if (item.Tag is ProviderPreset p && p.Name == preset.Name)
            {
                PresetComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    private static void OnAvailablePromptPresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputContainer self)
        {
            self.PopulatePromptPresetCombo();
        }
    }

    private void PopulatePromptPresetCombo()
    {
        _suppressPresetChanged = true;
        PromptPresetComboBox.Items.Clear();
        var presets = AvailablePromptPresets;
        if (presets is null || presets.Count == 0)
        {
            _suppressPresetChanged = false;
            return;
        }

        foreach (var preset in presets)
        {
            var item = new ComboBoxItem { Content = preset.Name, Tag = preset };
            PromptPresetComboBox.Items.Add(item);
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
        _suppressPresetChanged = true;
        foreach (ComboBoxItem item in PromptPresetComboBox.Items)
        {
            if (item.Tag is PromptPreset p && p.Name == preset.Name)
            {
                PromptPresetComboBox.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChanged = false;
    }

    private void PromptPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChanged) return;
        if (PromptPresetComboBox.SelectedItem is ComboBoxItem item && item.Tag is PromptPreset preset)
        {
            SelectedPromptPreset = preset;
            PromptPresetChanged?.Invoke(this, preset);
        }
    }
}

/// <summary>
/// Helper to track active windows for FileOpenPicker initialization.
/// Consumers must call <see cref="TrackWindow"/> in their App.OnLaunched.
/// </summary>
public static class WindowHelper
{
    internal static readonly List<Window> ActiveWindows = [];

    public static void TrackWindow(Window window)
    {
        if (!ActiveWindows.Contains(window))
        {
            ActiveWindows.Add(window);
            window.Closed += (_, _) => ActiveWindows.Remove(window);
        }
    }
}
