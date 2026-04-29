using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Export;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Controls.Rendering;
using FieldCure.AssistStudio.Core.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ChatPanel
{
    #region Search

    /// <summary>
    /// Toggles the in-conversation search bar (Ctrl+F).
    /// </summary>
    public void ToggleSearchBar()
    {
        if (_searchBar is null) return;

        if (_searchBar.Visibility == Visibility.Visible)
        {
            CloseSearchBar();
        }
        else
        {
            _searchBar.Visibility = Visibility.Visible;
            _searchTextBox?.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Closes the in-conversation search bar and clears any highlighted matches in the WebView.
    /// </summary>
    private async void CloseSearchBar()
    {
        if (_searchBar is not null) _searchBar.Visibility = Visibility.Collapsed;
        if (_searchTextBox is not null) _searchTextBox.Text = string.Empty;
        if (_searchCount is not null) _searchCount.Text = string.Empty;
        _searchDebounceTimer?.Stop();

        if (_chatWebView?.CoreWebView2 is not null)
            await _chatWebView.ExecuteScriptAsync("window.assistChat.searchClear()").AsTask();
    }

    /// <summary>
    /// Executes a search in the WebView for <paramref name="query"/> and updates the match counter.
    /// </summary>
    private async Task ExecuteSearchAsync(string query)
    {
        if (_chatWebView?.CoreWebView2 is null) return;

        var escaped = System.Text.Json.JsonSerializer.Serialize(query);
        var result = await _chatWebView.ExecuteScriptAsync(
            $"window.assistChat.search({escaped})").AsTask();

        UpdateSearchCount(result);
    }

    /// <summary>
    /// Navigates to the previous (-1) or next (+1) search match and refreshes the counter.
    /// </summary>
    private async Task NavigateSearchAsync(int direction)
    {
        if (_chatWebView?.CoreWebView2 is null) return;

        var result = await _chatWebView.ExecuteScriptAsync(
            $"window.assistChat.searchNavigate({direction})").AsTask();

        UpdateSearchCount(result);
    }

    /// <summary>
    /// Parses the JSON result of a search/navigate call and updates the "current/total" counter text.
    /// </summary>
    private void UpdateSearchCount(string? jsonResult)
    {
        if (_searchCount is null || jsonResult is null) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                jsonResult.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\"));
            var root = doc.RootElement;
            var total = root.GetProperty("total").GetInt32();
            var current = root.GetProperty("current").GetInt32();
            _searchCount.Text = total > 0 ? $"{current}/{total}" : string.Empty;
        }
        catch
        {
            _searchCount.Text = string.Empty;
        }
    }

    #endregion

    #region Title

    /// <summary>
    /// Attempts to auto-generate a title for the conversation after the first assistant response.
    /// </summary>
    private async void TryGenerateTitleAsync()
    {
        if (_titleGenerated || !AutoTitle) return;
        _titleGenerated = true;
        await GenerateTitleCoreAsync();
    }

    /// <summary>
    /// Core logic for generating or regenerating the conversation title using the resolved auxiliary provider.
    /// </summary>
    private async Task GenerateTitleCoreAsync()
    {
        if (Provider is null or MockProvider) return;

        var provider = AuxiliaryProviderResolver is { } resolver
            ? await resolver.ResolveWithFallbackAsync(TitleModel, Provider, "Title")
            : Provider;

        // Build context from conversation history
        var userMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.User);
        var assistantMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.Assistant);
        if (userMsg is null || assistantMsg is null) return;

        // Use more context for regeneration -- include recent messages
        var contextParts = new List<string>();
        foreach (var msg in _messages.Take(6))
        {
            var role = msg.Role == ChatRole.User ? "User" : "Assistant";
            var content = msg.Content.Length > 150 ? msg.Content[..150] : msg.Content;
            contextParts.Add($"{role}: {content}");
        }
        var context = string.Join("\n", contextParts);

        try
        {
            var titleRequest = new AiRequest
            {
                Messages =
                [
                    new ChatMessage(ChatRole.User,
                        $"{context}\n\nGenerate a short title (max 6 words) for this conversation. Reply with ONLY the title, no quotes or punctuation.")
                ],
                SystemPrompt = "You generate concise conversation titles.",
                Temperature = 0.5,
                MaxTokens = 200
            };

            var titleResponse = await provider.CompleteAsync(titleRequest);
            var title = SanitizeTitle(titleResponse.Content);
            if (string.IsNullOrEmpty(title) || title == "Untitled")
            {
                title = userMsg.Content.Length > 40
                    ? userMsg.Content[..40].TrimEnd() + "\u2026"
                    : userMsg.Content;
            }

            DiagnosticLogger.LogInfo($"[Chat] Title generated: {title}");
            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, title));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning("[Chat] Title generation failed, using fallback");
            DiagnosticLogger.LogException(ex);
            var fallback = userMsg.Content.Length > 40
                ? userMsg.Content[..40].TrimEnd() + "\u2026"
                : userMsg.Content;
            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, fallback));
        }
    }

    /// <summary>
    /// Extracts a clean single-line title from an LLM response.
    /// Small models occasionally include extra content (markdown separators,
    /// "Title:" prefixes, explanations) despite prompt instructions.
    /// </summary>
    private static string SanitizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Untitled";

        var firstLine = raw
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l =>
                l.Length > 0 &&
                !l.StartsWith("---") &&
                !l.StartsWith("```"));

        if (string.IsNullOrWhiteSpace(firstLine)) return "Untitled";

        firstLine = firstLine.Trim('*', '_', '`', '#', ' ', '"', '\'', '.');

        string[] prefixes = ["Title:", "title:", "제목:", "Subject:"];
        foreach (var prefix in prefixes)
        {
            if (firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                firstLine = firstLine[prefix.Length..].Trim().Trim('*', '_', '"', '\'', '.');
                break;
            }
        }

        const int maxLength = 100;
        if (firstLine.Length > maxLength)
            firstLine = firstLine[..maxLength].TrimEnd() + "\u2026";

        return string.IsNullOrWhiteSpace(firstLine) ? "Untitled" : firstLine;
    }

    /// <summary>
    /// Regenerates the conversation title from the full message history.
    /// </summary>
    public async Task RegenerateTitleAsync()
    {
        await GenerateTitleCoreAsync();
    }

    /// <summary>
    /// Updates the title text, showing a greeting when no conversation is active
    /// or the actual title when a conversation has started.
    /// Also toggles edit/refresh button visibility accordingly.
    /// </summary>
    private void UpdateTitleDisplay()
    {
        if (_titleText is null) return;

        if (_isConversationActive)
        {
            _titleText.Text = Title ?? "";
            if (_titleEditButton is not null) _titleEditButton.Visibility = Visibility.Visible;
            if (_titleRefreshButton is not null) _titleRefreshButton.Visibility = Visibility.Visible;
        }
        else
        {
            _greetingText ??= LoadGreeting();
            _titleText.Text = _greetingText;
            if (_titleEditButton is not null) _titleEditButton.Visibility = Visibility.Collapsed;
            if (_titleRefreshButton is not null) _titleRefreshButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handles the title edit button click to raise the <see cref="TitleEditRequested"/> event.
    /// </summary>
    private void OnTitleEditClick(object sender, RoutedEventArgs e)
    {
        TitleEditRequested?.Invoke(this, Title ?? "");
    }

    /// <summary>
    /// Handles the title refresh button click to regenerate the conversation title.
    /// </summary>
    private async void OnTitleRefreshClick(object sender, RoutedEventArgs e)
    {
        await RegenerateTitleAsync();
    }

    /// <summary>
    /// Loads the greeting text from resources.
    /// </summary>
    private static string LoadGreeting()
    {
        return Res.GetString("ChatPanel_Greeting") ?? "How can I help you today?";
    }

    #endregion

    #region Export

    /// <summary>
    /// Exports the active conversation branch as a Markdown document.
    /// Pure in-memory operation — performs no file I/O.
    /// </summary>
    /// <returns>A <see cref="MarkdownExportResult"/> containing the Markdown text and extracted media blobs.</returns>
    public MarkdownExportResult ExportToMarkdown()
    {
        return MarkdownExporter.Export(
            GetMessages(),
            title: Title);
    }

    #endregion

    #region Workspace UI

    /// <summary>
    /// Handles the folder flyout Opened event (visual tree is ready).
    /// Resolves PART_ elements and wires click handlers on first open.
    /// </summary>
    private void OnFolderFlyoutOpened(object? sender, object e)
    {
        if (sender is not Flyout flyout) return;

        // Resolve PART_ elements on first open (VisualTree available only after Opened)
        if (_folderAddButton is null && flyout.Content is FrameworkElement root)
        {
            _folderAddButton = FindDescendantByName<Button>(root, "PART_FolderAddButton");
            _folderDisabledHint = FindDescendantByName<TextBlock>(root, "PART_FolderDisabledHint");
            _folderList = FindDescendantByName<ItemsControl>(root, "PART_FolderList");
            _folderEmpty = FindDescendantByName<TextBlock>(root, "PART_FolderEmpty");
            _kbDisabledHint = FindDescendantByName<TextBlock>(root, "PART_KbDisabledHint");
            _kbSelector = FindDescendantByName<ComboBox>(root, "PART_KbSelector");
            _kbEmpty = FindDescendantByName<TextBlock>(root, "PART_KbEmpty");

            // Localize flyout text (x:Uid doesn't work in ControlTemplate)
            LocalizeFlyoutText(root);

            // Wire click handlers (once)
            if (_folderAddButton is not null)
                _folderAddButton.Click += (s, e2) => WorkspaceFolderAddRequested?.Invoke(this, EventArgs.Empty);
            if (_folderList is not null)
                _folderList.ItemsSource = _folderItems;
            if (_kbSelector is not null)
            {
                _kbSelector.SelectionChanged += (s, e2) =>
                {
                    if (_kbSelector.SelectedItem is KbItem selected)
                    {
                        KnowledgeBaseId = selected.Id;
                        KnowledgeBaseIdChanged?.Invoke(this, selected.Id);
                    }
                    else
                    {
                        KnowledgeBaseId = null;
                        KnowledgeBaseIdChanged?.Invoke(this, null);
                    }
                };
            }

            // Populate now (Opening couldn't do it because parts weren't resolved yet)
            PopulateFolderFlyout();
        }
    }

    /// <summary>
    /// Handles the folder flyout Opening event.
    /// Populates dynamic content if PART_ elements are already resolved.
    /// </summary>
    private void OnFolderFlyoutOpening(object? sender, object e)
    {
        // Only populate if parts are already resolved (after first Opened)
        if (_folderList is not null)
            PopulateFolderFlyout();
    }

    /// <summary>
    /// Updates the folder flyout content based on current workspace folders and knowledge base state.
    /// Called on every Flyout.Opening event.
    /// </summary>
    private void PopulateFolderFlyout()
    {
        if (_folderList is null) return;

        var folders = WorkspaceFolders?.ToList() ?? [];
        var isEnabled = IsWorkspaceEnabled;

        // Workspace section visibility
        if (_folderDisabledHint is not null)
            _folderDisabledHint.Visibility = Visibility.Visible;
        if (_folderEmpty is not null)
            _folderEmpty.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var removeTooltipText = Res.GetString("FolderFlyout_RemoveTooltip") ?? "Remove";
        var removeFolderNameFormat = Res.GetString("FolderFlyout_RemoveFolderAccessibilityName");
        if (string.IsNullOrEmpty(removeFolderNameFormat)) removeFolderNameFormat = "Remove folder: {0}";

        _folderItems.Clear();
        foreach (var folder in folders)
        {
            var capturedFolder = folder;
            _folderItems.Add(new FolderFlyoutItemViewModel(
                folderPath: capturedFolder,
                removeTooltip: removeTooltipText,
                removeButtonName: string.Format(removeFolderNameFormat, capturedFolder),
                rowOpacity: isEnabled ? 1.0 : 0.5,
                removeAction: () =>
                {
                    var updated = folders.Where(f => f != capturedFolder).ToList();
                    WorkspaceFolders = updated.Count > 0 ? updated : null;
                    WorkspaceFoldersChanged?.Invoke(this, updated);
                }));
        }

        // KB section — KB selector (always visible, hint when profile doesn't enable RAG)
        var kbEnabled = IsKnowledgeBaseEnabled;
        var selectedKbId = KnowledgeBaseId; // stores KB ID

        if (_kbSelector is not null)
        {
            _kbSelector.Visibility = Visibility.Visible;

            // Populate KB list
            var kbItems = KbItemsProvider?.Invoke() ?? [];
            _kbSelector.ItemsSource = kbItems;

            // Restore selection
            if (!string.IsNullOrEmpty(selectedKbId))
            {
                var match = kbItems.FirstOrDefault(k => k.Id == selectedKbId);
                if (match is not null)
                    _kbSelector.SelectedItem = match;
            }

            if (_kbEmpty is not null)
                _kbEmpty.Visibility = kbItems.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // Hint when profile doesn't have RAG enabled (but KB is selected)
        if (_kbDisabledHint is not null)
            _kbDisabledHint.Visibility = !kbEnabled && !string.IsNullOrEmpty(selectedKbId)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Applies localized strings to flyout elements that cannot use x:Uid in a ControlTemplate.
    /// </summary>
    private void LocalizeFlyoutText(FrameworkElement root)
    {
        SetText(root, "PART_FolderHeaderText", "Folder_Header", "Workspace Folders");
        SetText(root, "PART_FolderAddButtonText", "Folder_AddButton", "Add Folder");
        SetText(root, "PART_KbHeaderText", "Folder_KbHeader", "Knowledge Base");

        // Elements inside Collapsed parents aren't in the visual tree yet,
        // so search within the referenced PART_ elements directly.
        if (_folderEmpty is not null)
            _folderEmpty.Text = Res.GetString("Folder_Empty") ?? "(empty)";
        if (_folderDisabledHint is not null)
            _folderDisabledHint.Text = Res.GetString("Folder_DisabledHint") ?? "Enable Workspace in your profile to use these folders.";
        if (_kbDisabledHint is not null)
            _kbDisabledHint.Text = Res.GetString("Folder_KbDisabledHint") ?? "Current profile does not have Knowledge Base enabled.";
        if (_kbEmpty is not null)
            _kbEmpty.Text = Res.GetString("Folder_KbNoKbs") ?? "No knowledge bases. Create one in Settings.";
        if (_kbSelector is not null)
            _kbSelector.PlaceholderText = Res.GetString("Folder_KbPlaceholder") ?? "Select knowledge base";

        static void SetText(FrameworkElement parent, string elementName, string resKey, string fallback)
        {
            var el = FindDescendantByName<TextBlock>(parent, elementName);
            if (el is not null)
                el.Text = Res.GetString(resKey) ?? fallback;
        }
    }

    /// <summary>
    /// Finds a descendant element by name using breadth-first traversal of the visual tree.
    /// </summary>
    private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
                return typed;

            var result = FindDescendantByName<T>(child, name);
            if (result is not null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// Updates the folder button visual state to indicate whether any folders are configured.
    /// Uses VisualStateManager so {ThemeResource} handles theme changes automatically.
    /// </summary>
    private void UpdateFolderButtonBadge()
    {
        var hasFolders = (WorkspaceFolders?.Count ?? 0) > 0
            || !string.IsNullOrEmpty(KnowledgeBaseId);

        VisualStateManager.GoToState(this, hasFolders ? "HasFolders" : "NoFolders", true);
    }

    /// <summary>
    /// Updates the folder button appearance based on current state.
    /// </summary>
    private void UpdateFolderButtonAppearance()
    {
        UpdateFolderButtonBadge();
    }

    /// <summary>
    /// Updates the indexing progress UI elements in the flyout and title bar.
    /// Call this after changing <see cref="IsKbIndexing"/>, <see cref="KbIndexingProgress"/>,
    /// or <see cref="KbIndexingText"/>.
    /// </summary>
    public void UpdateKbProgressUI()
    {
        // Indexing progress is now managed by the KB Settings page.
        // This method is kept for API compatibility but is a no-op.
    }

    #endregion

    #region Media Helpers

    /// <summary>
    /// Converts a large data URI media item to a temp file, returning a new <see cref="MediaContent"/>
    /// with a file:// URI. Returns the original if below the threshold or not a data URI.
    /// </summary>
    private static MediaContent ConvertLargeMediaToTempFile(MediaContent media)
    {
        if (!media.MediaUri.StartsWith("data:", StringComparison.Ordinal))
            return media;

        var commaIdx = media.MediaUri.IndexOf(',');
        if (commaIdx < 0)
            return media;

        var base64Part = media.MediaUri[(commaIdx + 1)..];
        // Approximate decoded size: base64 is ~4/3 of original
        var estimatedBytes = (long)base64Part.Length * 3 / 4;
        if (estimatedBytes < LargeMediaThreshold)
            return media;

        try
        {
            Directory.CreateDirectory(WebViewChatRenderer.TempRoot);
            var ext = GuessFileExtension(media.MimeType);
            var tempPath = Path.Combine(WebViewChatRenderer.TempRoot, $"{Guid.NewGuid()}{ext}");
            var bytes = Convert.FromBase64String(base64Part);
            File.WriteAllBytes(tempPath, bytes);
            // Use virtual host URL so WebView2 can load the file
            // (file:// URIs are blocked from data: origin pages)
            var fileName = Path.GetFileName(tempPath);
            var virtualUrl = $"https://assiststudio.temp/{fileName}";
            return new MediaContent(virtualUrl, media.MimeType, media.Kind);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Media] Failed to write temp file: {ex.Message}");
            return media;
        }
    }

    /// <summary>
    /// Guesses a file extension from a MIME type for temp file creation.
    /// </summary>
    private static string GuessFileExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "audio/wav" => ".wav",
        "audio/mp3" or "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        "audio/webm" => ".webm",
        "video/mp4" => ".mp4",
        "video/webm" => ".webm",
        "video/ogg" => ".ogv",
        "application/pdf" => ".pdf",
        _ => ".bin"
    };

    /// <summary>
    /// Guesses file extension and type label from a data URI header string (e.g. "data:audio/mpeg;base64").
    /// </summary>
    private static (string Extension, string TypeLabel) GuessFileTypeFromMime(string dataUriHeader)
    {
        // Extract MIME from "data:{mime};base64" or "data:{mime}"
        var mime = dataUriHeader.Replace("data:", "");
        var semiIdx = mime.IndexOf(';');
        if (semiIdx >= 0) mime = mime[..semiIdx];
        mime = mime.Trim().ToLowerInvariant();

        var ext = GuessFileExtension(mime);
        var label = mime switch
        {
            _ when mime.StartsWith("image/") => "Image",
            _ when mime.StartsWith("audio/") => "Audio",
            _ when mime.StartsWith("video/") => "Video",
            "application/pdf" => "PDF",
            _ => "File"
        };
        return (ext, label);
    }

    /// <summary>
    /// Cleans up the temporary media directory. Called at app startup and shutdown.
    /// </summary>
    public static void CleanupTempMedia()
    {
        try
        {
            if (Directory.Exists(WebViewChatRenderer.TempRoot))
                Directory.Delete(WebViewChatRenderer.TempRoot, recursive: true);
        }
        catch
        {
            // Ignore — files may be locked or already deleted
        }
    }

    #endregion

    #region Image & Copy Handlers

    /// <summary>
    /// Handles the image save request by presenting a FileSavePicker and writing bytes to the chosen file.
    /// </summary>
    private async void OnImageSaveRequested(object? sender, string source)
    {
        try
        {
            byte[] bytes;
            string ext;
            string fileTypeLabel;

            if (source.StartsWith("data:"))
            {
                var commaIdx = source.IndexOf(',');
                var header = source[..commaIdx];
                (ext, fileTypeLabel) = GuessFileTypeFromMime(header);
                bytes = Convert.FromBase64String(source[(commaIdx + 1)..]);
            }
            else if (source.StartsWith("https://assiststudio.temp/", StringComparison.OrdinalIgnoreCase))
            {
                // Virtual host URL — resolve to local temp file
                var fileName = source["https://assiststudio.temp/".Length..];
                var localPath = Path.Combine(WebViewChatRenderer.TempRoot, fileName);
                bytes = await File.ReadAllBytesAsync(localPath);
                ext = Path.GetExtension(localPath);
                fileTypeLabel = ext switch
                {
                    ".mp3" or ".wav" or ".ogg" or ".webm" => "Audio",
                    ".mp4" or ".ogv" => "Video",
                    ".pdf" => "PDF",
                    _ => "File"
                };
            }
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(source);
                ext = source.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                   || source.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                fileTypeLabel = "Image";
            }

            var prefix = fileTypeLabel.ToLowerInvariant() switch
            {
                "audio" => "audio",
                "video" => "video",
                _ when ext == ".pdf" => "document",
                _ => "image"
            };

            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.SuggestedFileName = $"{prefix}_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
            picker.FileTypeChoices.Add(fileTypeLabel, [ext]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(GetWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            // Atomic write: defer → write → complete to prevent partial files
            Windows.Storage.CachedFileManager.DeferUpdates(file);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            var status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status != Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                DiagnosticLogger.LogWarning($"[Image] Save not completed: {status}");
                return;
            }

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageSaved") ?? "Image saved",
                file.Name));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Image] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the image copy request by decoding/downloading the image and placing it on the clipboard.
    /// </summary>
    private async void OnImageCopyRequested(object? sender, string source)
    {
        try
        {
            byte[] bytes;

            if (source.StartsWith("data:"))
            {
                var commaIdx = source.IndexOf(',');
                bytes = Convert.FromBase64String(source[(commaIdx + 1)..]);
            }
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(source);
            }

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var dp = new DataPackage();
            dp.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(dp);
            Clipboard.Flush();

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageCopied") ?? "Copied to clipboard",
                ""));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Image] Copy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the SVG diagram save request by writing the markup to a user-chosen .svg file.
    /// </summary>
    private async void OnDiagramSvgSaveRequested(object? sender, string svgMarkup)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.SuggestedFileName = $"diagram_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
            picker.FileTypeChoices.Add("SVG Image", [".svg"]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(GetWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            Windows.Storage.CachedFileManager.DeferUpdates(file);
            await Windows.Storage.FileIO.WriteTextAsync(file, svgMarkup);
            var status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status != Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                DiagnosticLogger.LogWarning($"[Diagram] SVG save not completed: {status}");
                return;
            }

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageSaved") ?? "Saved",
                file.Name));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Diagram] SVG save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the SVG diagram copy request by placing the raw SVG markup on the clipboard as text.
    /// </summary>
    private void OnDiagramSvgCopyRequested(object? sender, string svgMarkup)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(svgMarkup);
            Clipboard.SetContent(dp);
            Clipboard.Flush();

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageCopied") ?? "Copied to clipboard",
                ""));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Diagram] SVG copy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the message copy request from the renderer by copying message content to the clipboard.
    /// </summary>
    private void OnMessageCopyRequested(object? sender, string messageId)
    {
        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null || string.IsNullOrEmpty(message.Content)) return;

        var dp = new DataPackage();
        dp.SetText(message.Content);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

    /// <summary>
    /// Retrieves the parent <see cref="Window"/> for the current XamlRoot, used for native picker interop.
    /// </summary>
    private Window GetWindow()
    {
        if (XamlRoot?.Content is FrameworkElement fe)
        {
            if (fe.XamlRoot is not null)
            {
                foreach (var window in WindowHelper.ActiveWindows)
                {
                    if (window.Content?.XamlRoot == fe.XamlRoot)
                        return window;
                }
            }
        }
        throw new InvalidOperationException("Unable to find the parent Window for FileSavePicker.");
    }

    #endregion

    #region Tool Name Utilities

    /// <summary>
    /// Returns a display name for a tool. Tool names are technical identifiers and
    /// are intentionally not localized — translating only a hand-picked subset would
    /// be inconsistent with dynamic MCP tools, which always carry English identifiers.
    /// </summary>
    private string GetToolDisplayName(string toolName)
    {
        var tool = RegisteredTools.FirstOrDefault(t => t.Name == toolName)
            ?? McpTools.FirstOrDefault(t => t.Name == toolName);
        if (tool is null) return toolName;

        // For MCP tools, use Name instead of DisplayName to avoid
        // duplicating the server name (shown separately in the badge).
        return tool is McpToolAdapter ? FormatToolName(tool.Name) : tool.DisplayName;
    }

    /// <summary>
    /// Converts a snake_case tool name to a Title Case display name.
    /// Example: "write_file" → "Write File".
    /// </summary>
    private static string FormatToolName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i][1..];
        }
        return string.Join(' ', parts);
    }

    #endregion
}

/// <summary>
/// XAML-facing projection of one workspace-folder row inside the ChatPanel flyout.
/// </summary>
internal sealed class FolderFlyoutItemViewModel
{
    /// <summary>Initializes a new folder flyout row view model.</summary>
    public FolderFlyoutItemViewModel(
        string folderPath,
        string removeTooltip,
        string removeButtonName,
        double rowOpacity,
        Action removeAction)
    {
        FolderPath = folderPath;
        RemoveTooltip = removeTooltip;
        RemoveButtonName = removeButtonName;
        RowOpacity = rowOpacity;
        RemoveCommand = new DelegateCommand(removeAction);
    }

    /// <summary>Gets the displayed folder path.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the tooltip shown on the remove button.</summary>
    public string RemoveTooltip { get; }

    /// <summary>Gets the accessibility name for the remove button.</summary>
    public string RemoveButtonName { get; }

    /// <summary>Gets the row opacity used when workspace access is disabled.</summary>
    public double RowOpacity { get; }

    /// <summary>Gets the command that removes the folder from the conversation workspace list.</summary>
    public ICommand RemoveCommand { get; }
}
