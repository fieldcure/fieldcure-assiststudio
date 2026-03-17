# FieldCure AssistStudio

**AI Chat Controls for WinUI 3 — Bring Your Own Key, pick any provider.**

[![NuGet Core](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core?label=Core)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core)
[![NuGet Controls](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI?label=Controls.WinUI)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-purple)](https://dotnet.microsoft.com/)

![Chat conversation with Markdown and syntax highlighting](docs/chat-conversation.png)

---

## Features

- **BYOK (Bring Your Own Key)** — Users supply their own API keys. No proxy, no middleman.
- **Multi-Provider** — Claude, OpenAI, Gemini, and Ollama out of the box. Implement `IAiProvider` to add your own.
- **Streaming** — Real-time token-by-token responses via `IAsyncEnumerable<string>`.
- **Vision & Documents** — Attach images (PNG, JPG, WebP, …), PDFs, and DOCX files to any conversation.
- **Token Tracking** — Input/output token counts exposed after every request.
- **Re-templatable WinUI 3 Controls** — `ChatPanel`, `InputContainer`, `AttachmentPreviewBar`, and `ToolApprovalPanel` are `TemplatedControl`s with no app dependency. Override `Generic.xaml` to fully customize the UI.
- **Profiles & Presets** — Save provider configurations as presets; switch system prompts with profiles.
- **Conversation Persistence** — Save and load conversations in `.astx` (JSON) format.
- **Localization** — Built-in en-US and ko-KR resource strings.
- **Tool / Function Calling** — Define tools with `IAssistTool` and let providers invoke them. Tools that require confirmation show an inline `ToolApprovalPanel` for user approval before execution.

---

## Screenshots

| Empty State | Tool Approval |
|:-----------:|:-------------:|
| ![Empty state](docs/empty-state.png) | ![Tool approval panel](docs/tool-approval.png) |

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  AssistStudio (Demo App)                        │
│  WinUI 3 desktop app — showcases all controls   │
├─────────────────────────────────────────────────┤
│  AssistStudio.Controls        ← NuGet package   │
│  ChatPanel · InputContainer · ToolApprovalPanel │
│  WebView2 rendering · Themes · Localization     │
├─────────────────────────────────────────────────┤
│  AssistStudio.Core            ← NuGet package   │
│  IAiProvider · IAssistTool · Models · Helpers   │
│  Claude │ OpenAI │ Gemini │ Ollama providers    │
└─────────────────────────────────────────────────┘
```

| Project | NuGet Package | TFM | Key Types |
|---------|--------------|-----|-----------|
| **AssistStudio.Core** | `FieldCure.AssistStudio.Core` | `net8.0` | `IAiProvider`, `IAssistTool`, `AiRequest`, `AiResponse`, `ChatMessage`, `TokenUsage`, `ProviderPreset`, `Profile`, `ConversationManager` |
| **AssistStudio.Controls** | `FieldCure.AssistStudio.Controls.WinUI` | `net8.0-windows10.0.19041.0`<br>`net9.0-windows10.0.19041.0` | `ChatPanel`, `InputContainer`, `AttachmentPreviewBar`, `ToolApprovalPanel`, `WebViewChatRenderer`, `ChatTheme` |
| **AssistStudio** | *(demo app, not published)* | `net9.0-windows10.0.19041.0` | Reference implementation with settings, dialogs, and `PasswordVaultHelper` |

> **Core is platform-agnostic** (`net8.0`). It has no Windows-specific dependencies — you can reference it from a console app, a server, or any .NET project.

---

## Quick Start

### 1. Install packages

```bash
dotnet add package FieldCure.AssistStudio.Core
dotnet add package FieldCure.AssistStudio.Controls.WinUI
```

### 2. Create a provider and wire up the control

```csharp
using FieldCure.AssistStudio.Providers;

// Pick a provider — API key comes from the user
var provider = new ClaudeProvider(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-20250514");
```

```xml
<!-- In your WinUI 3 Page -->
<Page xmlns:assist="using:FieldCure.AssistStudio.Controls">

    <assist:ChatPanel x:Name="Chat"
                      Placeholder="Ask anything..."
                      Theme="System" />
</Page>
```

```csharp
// Code-behind
Chat.Provider = provider;
```

That's it — you have a fully functional AI chat with streaming, Markdown rendering, and syntax highlighting.

---

## Providers

### Supported providers

| Provider | Streaming | Vision | Documents | Tool Calling | API Key Required |
|----------|:---------:|:------:|:---------:|:------------:|:----------------:|
| **Claude** (Anthropic) | Yes | Yes | Yes | Yes | Yes |
| **OpenAI** (+ compatible endpoints) | Yes | Yes | Yes | Yes | Yes |
| **Gemini** (Google) | Yes | Yes | Yes | Yes | Yes |
| **Ollama** (local) | Yes | Yes | Yes | Yes | No |

> OpenAI provider works with any OpenAI-compatible API (Groq, Azure OpenAI, etc.) by setting a custom `baseUrl`.

### Implementing a custom provider

Implement `IAiProvider` to integrate any AI service:

```csharp
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;

public class MyCustomProvider : IAiProvider
{
    public string ProviderName => "MyService";
    public string ModelId => "my-model-v1";
    public TokenUsage? LastUsage { get; private set; }
    public bool IsTruncated { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastRawResponse { get; private set; }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        // Call your API, return an AiResponse
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AiRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Yield tokens as they arrive
        yield return "Hello ";
        yield return "from MyService!";
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>(
            [new AiModel("my-model-v1", "My Model", "myservice")]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(true, null, null, null));
}
```

Then assign it to a `ChatPanel`:

```csharp
Chat.Provider = new MyCustomProvider();
```

---

## Controls

All controls are **TemplatedControls** defined in `Generic.xaml`. They carry no app-level dependency — reference the NuGet package and use them in any WinUI 3 project.

### ChatPanel

The main control. Provides a complete chat experience: message list (WebView2), input area, streaming, attachments, presets, and profiles.

```xml
<assist:ChatPanel Provider="{x:Bind ViewModel.Provider, Mode=OneWay}"
                  SystemPrompt="You are a helpful assistant."
                  Theme="Dark"
                  Placeholder="Type a message..."
                  AvailablePresets="{x:Bind ViewModel.Presets}"
                  SelectedPreset="{x:Bind ViewModel.CurrentPreset, Mode=TwoWay}" />
```

**Key dependency properties:** `Provider`, `SystemPrompt`, `Theme`, `Placeholder`, `AvailablePresets`, `SelectedPreset`, `AvailableProfiles`, `SelectedProfile`, `MessageFontSize`, `ShowTitleBar`

**Events:** `MessageSent`, `PresetChanged`, `FilesDropped`, `TitleBarRequested`

### InputContainer

The chat input area — text box, attach button, preset/profile selectors. Used internally by `ChatPanel`, but can be placed standalone.

### AttachmentPreviewBar

Horizontal scrollable bar showing thumbnails of attached files before sending.

### ToolApprovalPanel

Inline confirmation panel shown when a tool with `RequiresConfirmation = true` is invoked. Displays the tool name, an expandable JSON arguments preview, and Allow/Reject buttons. Replaces `InputContainer` during confirmation and restores it after.

### Re-templating

Override the default template in your app's resources:

```xml
<Style TargetType="assist:ChatPanel" BasedOn="{StaticResource DefaultChatPanelStyle}">
    <!-- Customize template parts: PART_MessageList, PART_InputArea, PART_TitleBar, etc. -->
</Style>
```

---

## Configuration

### Provider presets

```csharp
var preset = new ProviderPreset
{
    Name = "Claude Sonnet",
    ProviderType = "Claude",
    ApiKey = "sk-ant-...",
    ModelId = "claude-sonnet-4-20250514",
    Temperature = 0.7,
    MaxTokens = 4096,
    StreamingEnabled = true
};
```

### Profiles

Profiles pair a system prompt with optional provider/model preferences and tool selections:

```csharp
var profile = new Profile
{
    Name = "Task Planner",
    Text = "You are a task planner that breaks down complex requests into steps and executes them using available tools.",
    ToolNames = ["search_files", "read_file", "write_file", "run_command"]
};
```

---

## Requirements

| Dependency | Minimum Version |
|------------|----------------|
| .NET | 8.0 |
| Windows App SDK | 1.7 |
| WebView2 Runtime | Evergreen |
| Target Platform | Windows 10 1903+ (10.0.19041.0) |

---

## Contributing

Contributions are welcome! Please open an issue first to discuss what you'd like to change.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes
4. Push to the branch and open a Pull Request

---

## License

[MIT](LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
