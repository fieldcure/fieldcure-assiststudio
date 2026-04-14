using FieldCure.Ai.Providers.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Helpers;
using FieldCure.DocumentParsers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ComposeBar
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
    /// Includes PDF (handled natively) and all extensions supported by DocumentParserFactory.
    /// </summary>
    private static readonly HashSet<string> DocumentExtensions =
        [".pdf", .. DocumentParserFactory.SupportedExtensions];

    #endregion

    #region Attach Button

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

    #endregion

    #region Add Files

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

    #endregion

    #region Clipboard Paste

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
            var rawBytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(rawBytes);

            // Clipboard bitmaps are BMP/DIB format — compress to a proper
            // image format (JPEG/PNG) so providers accept the media_type.
            try
            {
                var (compressedData, compressedMime) = ImageCompressor.CompressForApi(rawBytes);
                _previewBar?.AddAttachment(new ChatAttachment
                {
                    FileName = "clipboard-image.png",
                    Type = AttachmentType.Image,
                    Data = compressedData,
                    MimeType = compressedMime
                });
            }
            catch (NotSupportedException ex)
            {
                DiagnosticLogger.LogWarning($"[Paste] {ex.Message}");
                _previewBar?.AddAttachment(new ChatAttachment
                {
                    FileName = "clipboard-image.png",
                    Type = AttachmentType.Image,
                    IsUnsupported = true,
                });
            }
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

        // Long text paste → convert to attachment chip.
        // Must set Handled synchronously BEFORE any await to prevent
        // the default paste from inserting text into the TextBox.
        if (PasteAsAttachmentThreshold > 0 && content.Contains(StandardDataFormats.Text))
        {
            e.Handled = true;

            try
            {
                var text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text) && text.Length >= PasteAsAttachmentThreshold)
                {
                    var attachment = CreatePastedTextAttachment(text);
                    _previewBar?.AddAttachment(attachment);
                    return;
                }

                // Below threshold: manually insert at caret since we cancelled default
                InsertTextAtCaret(_messageTextBox, text ?? string.Empty);
            }
            catch
            {
                // Clipboard read failure — nothing to insert, nothing to attach
            }
        }
    }

    #endregion

    #region Drag & Drop

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

    #endregion

    #region Attachment Creation

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

            // Structured documents (DOCX, HWPX, etc.): extract text via DocumentParserFactory
            var parser = DocumentParserFactory.GetParser(ext);
            if (parser is not null)
            {
                var extractedText = parser.ExtractText(data);
                return new ChatAttachment
                {
                    FileName = file.Name,
                    Type = AttachmentType.TextFile,
                    Data = Encoding.UTF8.GetBytes(extractedText),
                    MimeType = "text/plain"
                };
            }
        }

        if (isImage)
        {
            try
            {
                var originalSize = data.Length;
                var (compressedData, compressedMime) = ImageCompressor.CompressForApi(data);
                if (compressedData.Length < originalSize)
                    DiagnosticLogger.LogInfo($"[Image] Compressed {originalSize:N0} → {compressedData.Length:N0} bytes ({GetImageMimeType(ext)} → {compressedMime})");
                return new ChatAttachment
                {
                    FileName = file.Name,
                    Type = AttachmentType.Image,
                    Data = compressedData,
                    MimeType = compressedMime
                };
            }
            catch (NotSupportedException ex)
            {
                DiagnosticLogger.LogWarning($"[Attach] {ex.Message}");
                return new ChatAttachment
                {
                    FileName = file.Name,
                    Type = AttachmentType.Image,
                    IsUnsupported = true,
                };
            }
        }

        return new ChatAttachment
        {
            FileName = file.Name,
            Type = AttachmentType.TextFile,
            Data = data,
            MimeType = "text/plain"
        };
    }

    /// <summary>
    /// Creates a <see cref="ChatAttachment"/> representing pasted text content from the clipboard.
    /// </summary>
    private ChatAttachment CreatePastedTextAttachment(string text)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"Pasted-{timestamp}.txt";

        // Same-second collision avoidance
        if (_previewBar is not null)
        {
            for (var n = 1; _previewBar.Attachments.Any(a => a.FileName == fileName); n++)
                fileName = $"Pasted-{timestamp}-{n}.txt";
        }

        return new ChatAttachment
        {
            FileName = fileName,
            Type = AttachmentType.TextFile,
            Data = Encoding.UTF8.GetBytes(text),
            MimeType = "text/plain",
            Source = AttachmentSource.Pasted,
            CharCount = text.Length,
            LineCount = text.AsSpan().Count('\n') + 1
        };
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Manually inserts text at the current caret position in a TextBox,
    /// replacing any selected text. Used when default paste is pre-empted.
    /// </summary>
    private static void InsertTextAtCaret(TextBox? textBox, string text)
    {
        if (textBox is null || string.IsNullOrEmpty(text)) return;

        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;
        var current = textBox.Text ?? string.Empty;

        textBox.Text = current[..start] + text + current[(start + length)..];
        textBox.SelectionStart = start + text.Length;
        textBox.SelectionLength = 0;
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

    #endregion
}
