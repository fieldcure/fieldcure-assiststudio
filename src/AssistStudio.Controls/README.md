# FieldCure.AssistStudio.Controls.WinUI

**Drop-in AI Chat UI Controls for WinUI 3** — Markdown rendering, streaming, attachments, thinking blocks, conversation branching, tool approval, and theming out of the box.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **ChatPanel** — Complete chat experience: message list, input area, streaming, attachments, preset/profile selectors, workspace context.
- **WebView2 Rendering** — Markdown (marked.js), syntax highlighting (highlight.js), and LaTeX (KaTeX) in a single WebView2 instance.
- **Progressive Streaming** — Two-zone DOM rendering with typing cursor (▌). `StreamEvent` discriminated union drives text, thinking, and tool call display.
- **Extended Thinking** — Collapsible thinking/reasoning blocks with visual left-bar styling. Auto-collapses when streaming completes.
- **Conversation Branching** — Tree-based message editing. Edit any user message to create a new branch. Navigate between branches with the ◀ 1/2 ▶ navigator in message footers.
- **Code Copy-to-Clipboard** — One-click copy button on every rendered code block.
- **Folder Flyout** — XAML-based folder management flyout with workspace folders and Knowledge Archive controls. `{ThemeResource}` bindings for automatic light/dark theme support.
- **Multimedia Rendering** — MCP image, audio, and video content blocks rendered inline with native controls.
- **Image Hover Toolbar** — Zoom (popover viewer), save (`FileSavePicker`), and copy buttons on hover over inline images.
- **Knowledge Archive Selector** — `ComposeBar` flyout for per-conversation KB selection with `kb_id` system prompt hint injection.
- **Tool Approval** — Inline `ToolApprovalPanel` for user confirmation before tool execution, with expandable JSON arguments preview, user instruction input field, and MCP server name badge.
- **MCP Elicitation** — `ToolElicitationPanel` for MCP server user-input requests with multi-field selection and batch submit.
- **Tool Block Details** — Expandable tool blocks showing arguments, result, and execution duration with interleave rendering.
- **Streaming Elapsed Time** — Real-time elapsed timer displayed in `ComposeBar` during streaming responses.
- **TemplatedControls** — All controls are `TemplatedControl`s with `PART_` conventions. Override `Generic.xaml` to fully customize.
- **Theming** — Light, Dark, and System themes. Set `Theme="System"` to follow the app theme.
- **Localization** — Built-in en-US and ko-KR resource strings.
- **Multi-TFM** — Targets both `net8.0-windows10.0.19041.0` and `net9.0-windows10.0.19041.0`.

## Install

```bash
dotnet add package FieldCure.AssistStudio.Controls.WinUI
```

> This package depends on [FieldCure.AssistStudio.Core](https://www.nuget.org/packages/FieldCure.AssistStudio.Core) which is installed automatically.

## Quick Start

```xml
<!-- In your WinUI 3 Page -->
<Page xmlns:assist="using:FieldCure.AssistStudio.Controls">

    <assist:ChatPanel x:Name="Chat"
                      Placeholder="Ask anything..."
                      Theme="System" />
</Page>
```

```csharp
using FieldCure.Ai.Providers;

// Code-behind — assign a provider
Chat.Provider = new ClaudeProvider(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-6");
```

## Controls

### ChatPanel

The main control. Provides message list (WebView2), input area, streaming, attachments, thinking blocks, conversation branching, presets, and profiles.

```xml
<assist:ChatPanel Provider="{x:Bind ViewModel.Provider, Mode=OneWay}"
                  SystemPrompt="You are a helpful assistant."
                  Theme="Dark"
                  Placeholder="Type a message..."
                  AvailablePresets="{x:Bind ViewModel.Presets}"
                  SelectedPreset="{x:Bind ViewModel.CurrentPreset, Mode=TwoWay}"
                  RegisteredTools="{x:Bind ViewModel.Tools}"
                  WorkspaceContext="{x:Bind ViewModel.Workspace}" />
```

**Dependency Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Provider` | `IAiProvider` | Active AI provider for completions and streaming |
| `SystemPrompt` | `string` | System prompt prepended to every request |
| `Theme` | `ChatTheme` | Light / Dark / System |
| `Placeholder` | `string` | Input placeholder text |
| `Title` | `string` | Title bar text |
| `AvailablePresets` | `IList` | Provider presets for the selector |
| `SelectedPreset` | `ProviderPreset` | Currently active preset |
| `AvailableProfiles` | `IList<Profile>` | Profile list for the selector |
| `SelectedProfile` | `Profile` | Currently active profile |
| `RegisteredTools` | `IReadOnlyList<IAssistTool>` | Tools available to the provider |
| `WorkspaceContext` | `IWorkspaceContext` | Dynamic context injection |
| `ContextProvider` | `IContextProvider` | RAG context retrieval (optional) |
| `UtilityProvider` | `IAiProvider` | Provider for auto-titling and summarization |
| `AutoTitle` | `bool` | Auto-generate conversation titles |
| `AutoSummarize` | `bool` | Auto-summarize long conversations |
| `MaxInputTokens` | `int` | Token limit for input |
| `MaxToolCallRounds` | `int` | Max consecutive tool call rounds |
| `RecentTurnsToKeep` | `int` | Turns to keep after summarization |
| `IsDebugMode` | `bool` | Show debug info (raw request/response) |
| `ShowTitleBar` | `bool` | Show/hide the title bar |
| `AllowAttachments` | `bool` | Enable/disable file attachments |
| `IsReadOnly` | `bool` | Read-only conversation view |
| `WorkspaceFolders` | `IList<string>` | Workspace folder paths for the current tab |
| `IsWorkspaceEnabled` | `bool` | Enable/disable workspace folder features |
| `KnowledgeArchiveFolder` | `string` | Knowledge Archive folder path (kb_id) for the current conversation |
| `IsKnowledgeArchiveEnabled` | `bool` | Whether Knowledge Archive is enabled in the current profile |
| `IsArchiveIndexing` | `bool` | Whether the Knowledge Archive is currently indexing |
| `ArchiveIndexingProgress` | `double` | Indexing progress (0–100) |
| `ArchiveIndexingText` | `string` | Current indexing file name for tooltip display |
| `IsArchiveLocked` | `bool` | Whether the archive folder is locked by another process |
| `McpTools` | `IReadOnlyList<IAssistTool>` | MCP tools from connected servers |
| `MemoryText` | `string` | Persistent memory text injected into system prompt |
| `ChatZoomFactor` | `double` | CSS zoom factor for chat rendering (default 1.05) |
| `AllowAttachments` | `bool` | Enable/disable file attachments |
| `EmptyStateContent` | `object` | Custom empty state UI |
| `AvailableServers` | `IList<ServerInfo>` | MCP server status for tools flyout |

**Events:** `PresetChanged`, `ProfileChanged`, `MessageAdded`, `TitleGenerated`, `TitleEditRequested`, `KeyboardShortcutPressed`

### ComposeBar

Chat input area — text box, attach button, preset/profile selectors. Used internally by `ChatPanel`, but can be placed standalone.

**Dependency Properties:** `Placeholder`, `IsInputEnabled`, `AvailablePresets`, `SelectedPreset`, `AvailableProfiles`, `SelectedProfile`

### AttachmentPreviewBar

Horizontal scrollable bar showing thumbnails of attached files before sending. Supports images (thumbnails), text files (icon + name), and documents (icon + name).

**Dependency Properties:** `ThumbnailSize` (default 80px), `MaxTextWidth`

### ToolApprovalPanel

Inline confirmation panel for tools with `RequiresConfirmation = true`. Displays tool name, expandable JSON arguments, and Allow/Reject buttons. Replaces `ComposeBar` during confirmation.

**Dependency Properties:** `ToolName`, `ToolDisplayName`, `Arguments`, `IsExpanded`

**Events:** `Approved`, `Rejected`

### Conversation Branching

When a user edits a sent message, the original branch is preserved and a new sibling branch is created. The branch navigator appears in the message footer:

```
◀  1/2  ▶
```

- Branches are stored via `ChatMessage.ParentId` forming a tree structure
- Navigation is handled through WebView2 `WebMessageReceived` events
- The full tree persists in `.astx` (ZIP archive) files — no conversation history is lost

### Thinking Blocks

When a provider streams `ThinkingDelta` events, a collapsible thinking block renders above the response with a distinct left-bar style. The block auto-collapses when streaming completes, keeping the UI clean while preserving the reasoning for review.

### Re-templating

Override the default template in your app's resources:

```xml
<Style TargetType="assist:ChatPanel" BasedOn="{StaticResource DefaultChatPanelStyle}">
    <!-- Customize PART_MessageList, PART_InputArea, PART_TitleBar, etc. -->
</Style>
```

## Requirements

| Dependency | Minimum Version |
|------------|----------------|
| .NET | 8.0 |
| Windows App SDK | 1.7 |
| WebView2 Runtime | Evergreen |
| Target Platform | Windows 10 1903+ (10.0.19041.0) |

### Per-Monitor DPI Awareness

Your host application **must** declare `PerMonitorV2` DPI awareness.
`ChatPanel` renders through an embedded WebView2, and without this setting
the Chromium compositor receives a scaled-down viewport on high-DPI displays
(125 %+), causing blurry text and scroll/hit-test dead zones.

Add the following to your `app.manifest`:

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

> WinUI 3 project templates sometimes omit this declaration. At 100 % scaling
> everything works fine; the issue only appears at higher DPI settings.

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
