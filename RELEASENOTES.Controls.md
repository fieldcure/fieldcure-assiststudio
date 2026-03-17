# Release Notes — FieldCure.AssistStudio.Controls.WinUI

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
