# FieldCure.AssistStudio.Controls.WinUI

**Drop-in AI Chat UI Controls for WinUI 3** — Markdown rendering, streaming, attachments, tool approval, and theming out of the box.

[![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **ChatPanel** — Complete chat experience: message list, input area, streaming, attachments, preset/profile selectors.
- **WebView2 Rendering** — Markdown, syntax highlighting (highlight.js), and LaTeX (KaTeX) rendered in a single WebView2 instance.
- **TemplatedControls** — All controls are `TemplatedControl`s with `PART_` conventions. Override `Generic.xaml` to fully customize.
- **Theming** — Light, Dark, and System themes. Set `Theme="System"` to follow the app theme.
- **Localization** — Built-in en-US and ko-KR resource strings.
- **Tool Approval** — Inline `ToolApprovalPanel` for user confirmation before tool execution.
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
using FieldCure.AssistStudio.Providers;

// Code-behind — assign a provider
Chat.Provider = new ClaudeProvider(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-20250514");
```

## Controls

### ChatPanel

The main control. Provides message list (WebView2), input area, streaming, attachments, presets, and profiles.

```xml
<assist:ChatPanel Provider="{x:Bind ViewModel.Provider, Mode=OneWay}"
                  SystemPrompt="You are a helpful assistant."
                  Theme="Dark"
                  Placeholder="Type a message..."
                  AvailablePresets="{x:Bind ViewModel.Presets}"
                  SelectedPreset="{x:Bind ViewModel.CurrentPreset, Mode=TwoWay}" />
```

**Dependency Properties:** `Provider`, `SystemPrompt`, `Theme`, `Placeholder`, `AvailablePresets`, `SelectedPreset`, `AvailableProfiles`, `SelectedProfile`, `MessageFontSize`, `ShowTitleBar`

**Events:** `MessageSent`, `PresetChanged`, `FilesDropped`, `TitleBarRequested`

### InputContainer

Chat input area — text box, attach button, preset/profile selectors. Used internally by `ChatPanel`, but can be placed standalone.

### AttachmentPreviewBar

Horizontal scrollable bar showing thumbnails of attached files before sending.

### ToolApprovalPanel

Inline confirmation panel for tools with `RequiresConfirmation = true`. Displays tool name, expandable JSON arguments, and Allow/Reject buttons.

### Re-templating

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

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
