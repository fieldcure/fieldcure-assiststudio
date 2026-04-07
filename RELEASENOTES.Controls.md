# Release Notes — FieldCure.AssistStudio.Controls.WinUI

## [0.14.0] - 2026-04-07

### Added
- **Multimedia rendering** — MCP `ImageContentBlock` inline display, audio/video player elements with native controls
- **Image hover toolbar** — zoom (popover viewer), save (`FileSavePicker`), and copy buttons on image hover
- **Knowledge Base selector** — flyout in `ComposeBar` for per-conversation KB selection with `kb_id` system prompt hint
- **Tool approval user instruction** — free-text input field on `ToolApprovalPanel` for injecting user notes as transient messages
- **Streaming elapsed time** — real-time elapsed timer in `ComposeBar` during streaming responses
- **Sub-Agent tool result blocks** — collapsible delegate_task result display with restored labels on conversation load

### Changed
- Streaming consumption moved off UI thread via `Channel<T>` producer-consumer pattern for reduced UI thread contention
- Media file download routed through `FileSavePicker` (audio/video) and virtual host (temp files)
- Image save/copy logic moved from `WebViewChatRenderer` to `ChatPanel` for cleaner separation
- `search_tools` dynamically promoted based on available tool count

### Fixed
- Base64 double-encoding on MCP image content blocks
- Media element positioning after `finalizeMessage` call
- Tab switch now pauses playing audio/video elements
- Tool result images preserved during message finalization (no longer stripped)
- User message no longer hidden when assistant response starts streaming
- User instruction injected as transient user message instead of tool result append

---

## [0.13.0] - 2026-03-31

### Changed
- Streaming token rendering batched at 50ms intervals to reduce WebView2 `ExecuteScriptAsync` calls — prevents UI thread saturation during large responses
- `await Task.Delay(1)` yield during tool call argument streaming to keep UI responsive

---

## [0.12.0] - 2026-03-30

### Fixed
- `ToolApprovalPanel` prompt text not updating when tool name changes — added `PropertyChanged` callbacks for `ToolName` and `ToolDisplayName` dependency properties
- Built-in server version fallback using `config.Name` (display name) instead of extracting key from `config.Id` — now correctly resolves NuGet package version

---

## [0.11.0] - 2026-03-29

### Added
- `ChatZoomFactor` dependency property for dynamic chat UI scaling (default 1.05)
- Collapsible long user messages with Show more / Show less toggle
- URL and content length display in `fetch_url` tool result blocks
- Collapsible `search_documents` results in WebView2 chat UI
- Indexing cancel button + RAG file count safety limits in folder flyout
- Indexing progress display in folder flyout and title bar
- Dynamic tooltip on folder icon showing indexing file name
- Lock icon on archive folder rows
- Server placeholder entries in tools flyout with localized tool names
- Persistent memory tools (`remember`, `forget`) UI support

### Changed
- `InputContainer` renamed to `ComposeBar`
- Tool display name localization removed — raw function names used instead
- Server toggle removed from tools flyout, replaced with `PrepareToolsForSendAsync` send-time auto-connect
- `Profile.Text` → `Profile.SystemPrompt` in bindings
- ProgressRing replaced with folder icon opacity animation for indexing state
- Folder icon VisualState renamed to `Idle`

### Fixed
- WebView2 blank screen when clicking relative-path links (navigation intercepted)
- OpenAI RAG source links converted to plain text before rendering
- ProgressRing and folder button layout wrapped in StackPanel
- Progress ring placement, percent color, InfoBar messages localized
- PasswordVault empty-value exception prevention

---

## [0.10.0] - 2026-03-24

### Added
- Folder flyout converted from code-behind to XAML `<Button.Flyout>` with `{ThemeResource}` bindings
- `FolderStates` VisualStateGroup (`HasFolders`/`NoFolders`) for folder icon glyph swap (FolderOpen ↔ FolderFill)
- Folder flyout localization via `ResourceLoader` (x:Uid not supported in ControlTemplate)
- Remove button tooltips with Mouse placement in folder flyout
- Static `ResourceLoader` field replacing per-call instantiation

### Changed
- Folder icon: FolderOpen (`E838`) when empty, FolderFill (`E8D5`) when folders exist — replaces accent color indicator
- Folder button tooltip: "Folder Settings" (was "Workspace Folders")
- Server-owned MCP tools (e.g., RAG tools) hidden from individual tool checkboxes — controlled by server checkbox only

### Fixed
- Folder icon invisible in default state (`Foreground = null` → VisualState with `{ThemeResource}`)
- Flyout text not following theme (`Application.Current.Resources` → XAML `{ThemeResource}` bindings)
- Flyout PART_ elements not resolved (`Flyout.Opening` → `Flyout.Opened` for visual tree availability)

### Removed
- `PART_FolderBadge` (unused numeric badge overlay)
- `BuildFolderFlyout()` code-behind method (~270 lines)

---

## [0.9.0] - 2026-03-24

### Added
- `AvailableServers` property on `ChatPanel` for MCP server status display
- `ServerInfo` model for lightweight server descriptor (Id, DisplayName, IsConnected, IsBuiltIn)

### Changed
- Tool flyout redesigned with server-level toggles replacing tool-level selection

---

## [0.8.0] - 2026-03-22

### Changed
- Document parser code extracted from `InputContainer` to `FieldCure.DocumentParsers` package
- `DocumentExtensions` now dynamically derived from `DocumentParserFactory.SupportedExtensions` — new parsers are auto-registered

### Fixed
- HWPX table extraction failing due to nested element structure (`hp:p > hp:run > hp:tbl`)

---

## [0.7.0] - 2026-03-21

### Added
- Extended thinking UI with collapsible left-bar styled thinking blocks
- Streaming tool call display as themed blocks with tool name labels
- Progressive streaming render (tokens displayed as they arrive, markdown finalized on complete)
- Tool toggle UI in `InputContainer` with select all/deselect all and `search_tools` policy support
- Conversation branching with tree-based edit flow and branch navigator
- `Ctrl+F` conversation search with highlight and prev/next navigation
- Shimmer loading placeholder control
- `IsReadOnly`, `ShowTitleBar`, `AllowAttachments`, `EmptyStateContent`, `FontFamily`, `FontSize` dependency properties on `ChatPanel`
- `RegisteredTools`, `WorkspaceContext`, `ContextProvider`, `UtilityProvider` properties on `ChatPanel`
- `KeyboardShortcutPressed` event on `ChatPanel`
- MCP server disconnect notification on app exit

### Changed
- **Breaking:** `IAiProvider.StreamAsync` now returns `IAsyncEnumerable<StreamEvent>` (consumers must update)
- Controls RootNamespace changed from `FieldCure.AssistStudio` to `FieldCure.AssistStudio.Controls`
- `SubtleButtonStyle` aligned with WinAppSDK 1.8 built-in specification
- Chat font size increased for readability; input area sizing improved
- Edit/resend button renamed to "Send" with settings hint
- `GeneratedRegex` source generator used for all regex patterns (SYSLIB1045)
- `CreatePreviewItem` return type narrowed from `UIElement` to `Grid` (CA1859)

### Fixed
- Stream cancellation now finalizes message (removes blinking cursor)
- WebView2 clipboard shortcuts (`Ctrl+C/V/X`) and streaming CSS styles
- Conversation branch restoration losing messages and navigator state
- Tool calling conversation restoration showing blank on reopen
- Post-stream input focus restored automatically

---

## [0.6.0] - 2026-03-17

### Added
- Title edit button tooltip with localized resource strings (`ChatPanel_EditTitleTooltip`)
- `SetBottomRightToolTip` helper for consistent tooltip placement on title bar buttons

### Changed
- Title bar button tooltips use `PlacementMode.Mouse` instead of default top placement

### Fixed
- Missing `SubtleButtonStyle` causing crash in packaged builds

---

## [0.5.0] - 2026-03-17

### Added
- Dedicated NuGet package README with Controls-specific XAML examples and API reference

---

## [0.4.0] - 2026-03-17

### Added
- NuGet package metadata (Company, Copyright, Icon, README, Repository URL, Tags)
- Release notes auto-inclusion in NuGet package
- `publish-nuget.ps1` script for pack → sign → push workflow

---

## [0.3.0] - 2026-03-17

### Added
- `ToolApprovalPanel` templated control for tool execution confirmation UI
- Summarize button wired from `InputContainer` to `ChatPanel`
- External link navigation redirected to default browser from WebView2

### Fixed
- WebView2 stealing focus from TextBox during streaming response

---

## [0.2.0] - 2026-03-16

### Fixed
- Duplicate file attachment on drag-and-drop (event bubbling from InputContainer to ChatPanel)
- Consecutive tool results merged into single user message for Claude compatibility

---

## [0.1.0] - 2026-03-15

### Added
- `ChatPanel` templated control with WebView2-based message rendering
- `InputContainer` templated control with provider/profile selectors and attachment support
- `AttachmentPreviewBar` templated control for file previews
- Markdown rendering via marked.js with code syntax highlighting (highlight.js)
- LaTeX/math rendering via KaTeX
- Code block copy-to-clipboard
- Streaming display with cursor indicator
- Image paste and file picker attachment
- PDF and DOCX text extraction for document attachments
- Localization support (en-US, ko-KR)
