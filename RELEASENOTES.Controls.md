# Release Notes — FieldCure.AssistStudio.Controls.WinUI

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
