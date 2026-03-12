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
            new PropertyMetadata("Type a message..."));

    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(nameof(IsInputEnabled), typeof(bool), typeof(InputContainer),
            new PropertyMetadata(true, OnIsInputEnabledChanged));

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

    public event EventHandler<MessageSentEventArgs>? MessageSent;

    private void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !IsShiftPressed())
        {
            e.Handled = true;
            TrySend();
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        TrySend();
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

        // WinUI 3: need to initialize picker with window handle
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
            // Extract text from PDF/DOCX and treat as TextFile
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
        // Walk up the XamlRoot to find the Window
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
            self.SendButton.IsEnabled = enabled;
            self.AttachButton.IsEnabled = enabled;
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
