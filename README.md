# FieldCure AssistStudio

**AI Chat Controls for WinUI 3 + Windows AI Workspace App — Bring Your Own Key, pick any provider.**

[![NuGet Providers](https://img.shields.io/nuget/v/FieldCure.Ai.Providers?label=Ai.Providers)](https://www.nuget.org/packages/FieldCure.Ai.Providers)
[![NuGet Execution](https://img.shields.io/nuget/v/FieldCure.Ai.Execution?label=Ai.Execution)](https://www.nuget.org/packages/FieldCure.Ai.Execution)
[![NuGet Core](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core?label=Core)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core)
[![NuGet Controls](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI?label=Controls.WinUI)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-purple)](https://dotnet.microsoft.com/)

AssistStudio is two things:

1. **A reusable WinUI 3 library** (two NuGet packages) for building desktop AI assistants with multi-provider support, tool approval, and profile-based behavior.
2. **A Windows-native AI workspace app** for cloud and local models, profiles, tools, and structured conversations.

![Chat conversation with Markdown and syntax highlighting](docs/chat-conversation.png)

### Ecosystem

![AssistStudio Ecosystem](docs/ecosystem.png)

---

## Features

- **BYOK (Bring Your Own Key)** — Users supply their own API keys. No proxy, no middleman.
- **Multi-Provider** — Claude, OpenAI, Gemini, Ollama, and Groq out of the box. Implement `IAiProvider` to add your own.
- **Streaming** — Real-time structured event streaming via `IAsyncEnumerable<StreamEvent>` — a discriminated union covering text, thinking, tool calls, usage, and completion. Elapsed time display during streaming.
- **Extended Thinking** — Per-provider thinking/reasoning support (Claude extended thinking, OpenAI o-series reasoning, Ollama native thinking field). Configurable via `ThinkingOverride` and `ThinkingBudget`.
- **Conversation Branching** — Tree-based message editing with branch navigator (◀ 1/2 ▶). Edit any message to explore alternatives without losing history.
- **MCP Integration** — Connect to MCP servers (Stdio / HTTP) to aggregate tools from any Model Context Protocol source. `McpToolAdapter` bridges MCP tools to the `IAssistTool` pipeline.
- **Built-in MCP Servers** — Essentials, Filesystem, Knowledge Archive (RAG), and Outbox servers ship as built-ins, auto-installed and auto-updated via `dotnet tool`. Per-tab or shared instances with MCP Roots protocol support.
- **Vision & Documents** — Attach images (PNG, JPG, WebP, GIF), PDFs, DOCX, HWPX, XLSX, and PPTX files. Automatic image compression via `ImageCompressor`. Document text extraction via [FieldCure.DocumentParsers](https://github.com/fieldcure/fieldcure-document-parsers). Per-provider `PdfCapability` (Auto / TextExtraction / NativePdf / PageAsImage).
- **Multimedia Tool Results** — MCP tools returning image, audio, and video content are rendered inline with native controls. Image hover toolbar with zoom (popover viewer), save, and copy.
- **Sub-Agent Delegation** — `delegate_task` tool for autonomous sub-agent execution with parallel dispatch. `ISpecialist` interface for domain-specific routing (e.g., Web Search Specialist).
- **Tool / Function Calling** — Define tools with `IAssistTool`. `ToolCallExecutor` orchestrates execution with confirmation flow and parallel execution. `ToolApprovalPanel` shows inline approval UI with user instruction input.
- **Token Tracking** — Input/output token counts exposed after every request.
- **Re-templatable WinUI 3 Controls** — `ChatPanel`, `ComposeBar`, `AttachmentPreviewBar`, and `ToolApprovalPanel` are `TemplatedControl`s. Override `Generic.xaml` to fully customize the UI.
- **Profiles & Presets** — Save provider configurations as presets; switch system prompts and tool selections with profiles.
- **Workspace Context** — `IWorkspaceContext` for dynamic system prompt injection based on app state.
- **Conversation Persistence** — Save and load conversations in `.astd` (JSON) format with full branching tree and media persistence.
- **Knowledge Archive** — Multi-KB management with create/delete/settings UI, embedding model selection, and per-conversation KB selector.
- **Schedule** — Cron schedule management with bilingual (en/ko) human-readable cron descriptions.
- **Localization** — Built-in en-US and ko-KR resource strings.
- **Structured Logging** — `DiagnosticLogger` with pluggable `OnException`, `OnWarning`, `OnInfo` callbacks.

---

## Screenshots

<table>
  <tr>
    <th align="center">Empty State</th>
    <th align="center">Tool Approval</th>
  </tr>
  <tr>
    <td align="center"><img src="docs/empty-state.png" width="400" alt="Empty state" /></td>
    <td align="center"><img src="docs/tool-approval.png" width="400" alt="Tool approval panel" /></td>
  </tr>
</table>

---

## Architecture

```
                        ┌──────────────────────┐
                        │   MCP Servers        │
                        │  (Stdio / HTTP)      │
                        └──────────┬───────────┘
                                   │ McpToolAdapter
┌──────────────────────────────────┼──────────────────┐
│  AssistStudio (Workspace App)                       │
│  WinUI 3 — MCP · SubAgent · Schedule · KB · Media  │
├─────────────────────────────────────────────────────┤
│  AssistStudio.Controls            ← NuGet package   │
│  ChatPanel · Multimedia · Branching · ThinkingBlock │
│  WebView2 rendering · Themes · Localization         │
├─────────────────────────────────────────────────────┤
│  AssistStudio.Core                ← NuGet package   │
│  ISpecialist · ToolCallExecutor · ToolResolver      │
├─────────────────────────────────────────────────────┤
│  Ai.Execution                     ← NuGet package   │
│  AgentLoop · SubAgentExecutor                       │
├─────────────────────────────────────────────────────┤
│  Ai.Providers                     ← NuGet package   │
│  IAiProvider · StreamEvent · IAssistTool · Models   │
│  Claude │ OpenAI │ Gemini │ Ollama │ Groq           │
└─────────────────────────────────────────────────────┘
```

| Project | NuGet Package | TFM | Key Types |
|---------|--------------|-----|-----------|
| **AssistStudio.Core** | `FieldCure.AssistStudio.Core` | `net8.0` | `ISpecialist`, `KnowledgeBase`, `ToolCallExecutor`, `ToolResolver`, `McpToolAdapter`, `IWorkspaceContext`, `BuiltInServerConfig`, `Profile` — depends on [`FieldCure.Ai.Providers`](https://www.nuget.org/packages/FieldCure.Ai.Providers) |
| **AssistStudio.Controls** | `FieldCure.AssistStudio.Controls.WinUI` | `net8.0-windows10.0.19041.0`<br>`net9.0-windows10.0.19041.0` | `ChatPanel`, `ComposeBar`, `AttachmentPreviewBar`, `ToolApprovalPanel`, `ChatTheme` |
| **Ai.Providers** | `FieldCure.Ai.Providers` | `net8.0` | `IAiProvider`, `StreamEvent`, `IAssistTool`, `AiRequest`, `AiResponse`, `ChatMessage`, `ProviderPreset`, `ImageCompressor`, `IMultiContentTool`, `MediaContent` |
| **Ai.Execution** | `FieldCure.Ai.Execution` | `net8.0` | `IAgentLoop`, `AgentLoop`, `ISubAgentExecutor`, `SubAgentExecutor`, `AgentLoopContext`, `AgentLoopResult` |
| **AssistStudio** | *(workspace app)* | `net9.0-windows10.0.19041.0` | Reference implementation with settings, MCP server management, sub-agent delegation, schedule, and `PasswordVaultHelper` |

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

That's it — you have a fully functional AI chat with streaming, Markdown rendering, syntax highlighting, thinking blocks, and conversation branching.

### 3. Streaming with StreamEvent

```csharp
var request = new AiRequest("Explain quantum computing.");

await foreach (var evt in provider.StreamAsync(request))
{
    switch (evt)
    {
        case StreamEvent.ThinkingDelta t:
            Console.Write($"[think] {t.Text}");
            break;
        case StreamEvent.TextDelta d:
            Console.Write(d.Text);
            break;
        case StreamEvent.ToolCallStart s:
            Console.WriteLine($"\n→ Calling {s.FunctionName}...");
            break;
        case StreamEvent.Usage u:
            Console.WriteLine($"\nTokens: {u.TokenUsage.TotalTokens}");
            break;
    }
}
```

---

## Providers

### Supported providers

| Provider | Streaming | Vision | Documents | Tool Calling | Thinking | API Key Required |
|----------|:---------:|:------:|:---------:|:------------:|:--------:|:----------------:|
| **Claude** (Anthropic) | Yes | Yes | Yes | Yes | Yes | Yes |
| **OpenAI** (+ compatible) | Yes | Yes | Yes | Yes | o-series | Yes |
| **Gemini** (Google) | Yes | Yes | Yes | Yes | No | Yes |
| **Ollama** (local) | Yes | Dep. | Dep. | Dep. | think tags | No |
| **Groq** | Yes | Yes | Yes | Yes | Dep. | Yes |

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
    public PdfCapability PdfCapability => PdfCapability.TextExtraction;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        // Call your API, return an AiResponse
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamEvent.TextDelta("Hello ");
        yield return new StreamEvent.TextDelta("from MyService!");
        yield return new StreamEvent.StreamCompleted(false);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>(
            [new AiModel("my-model-v1", "My Model", "myservice")]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(true, null, null, null));

    public ThinkingSupport GetThinkingSupport(string modelId)
        => ThinkingSupport.NotSupported;
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

The main control. Provides a complete chat experience: message list (WebView2), input area, streaming, thinking blocks, conversation branching, attachments, presets, and profiles.

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

**Key dependency properties:** `Provider`, `SystemPrompt`, `Theme`, `Title`, `Placeholder`, `AvailablePresets`, `SelectedPreset`, `AvailableProfiles`, `SelectedProfile`, `RegisteredTools`, `WorkspaceContext`, `UtilityProvider`, `AutoTitle`, `AutoSummarize`, `MaxInputTokens`, `MaxToolCallRounds`, `RecentTurnsToKeep`, `IsDebugMode`, `ShowTitleBar`, `AllowAttachments`, `IsReadOnly`, `FontFamily`, `FontSize`, `ChatZoomFactor`, `EmptyStateContent`

**Events:** `PresetChanged`, `ProfileChanged`, `MessageAdded`, `TitleGenerated`, `TitleEditRequested`, `KeyboardShortcutPressed`

### ComposeBar

The chat input area — text box, attach button, preset/profile selectors. Used internally by `ChatPanel`, but can be placed standalone.

### AttachmentPreviewBar

Horizontal scrollable bar showing thumbnails of attached files before sending.

### ToolApprovalPanel

Inline confirmation panel shown when a tool with `RequiresConfirmation = true` is invoked. Displays the tool name, an expandable JSON arguments preview, and Allow/Reject buttons. Replaces `ComposeBar` during confirmation and restores it after.

### Re-templating

Override the default template in your app's resources:

```xml
<Style TargetType="assist:ChatPanel" BasedOn="{StaticResource DefaultChatPanelStyle}">
    <!-- Customize template parts: PART_MessageList, PART_InputArea, PART_TitleBar, etc. -->
</Style>
```

---

## MCP Integration

The workspace app demonstrates full MCP (Model Context Protocol) integration:

- **McpServerRegistry** — Singleton server connection manager with observable tool collection
- **McpServerConnection** — Per-server lifecycle (Disconnected → Connecting → Connected / Error)
- **McpToolAdapter** — Bridges MCP tools to `IAssistTool` without Core SDK dependency
- **ToolResolver** — Merges built-in + MCP tools, prefixing MCP tool names with server name on conflict
- **SearchToolsTool** — Meta-tool for searching across large MCP tool sets to save tokens

MCP servers are configured with Stdio or HTTP transport and connected at app startup. Tools from all connected servers are aggregated and made available to providers.

### Built-in MCP Servers

The workspace app bundles MCP servers that are auto-installed and managed:

- **Essentials** ([`FieldCure.Mcp.Essentials`](https://www.nuget.org/packages/FieldCure.Mcp.Essentials)) — 12–16 tools: HTTP client, web search (Bing/Serper/SerpApi/Tavily + category search for news/images/scholar/patents), web/document fetching, shell commands, JavaScript sandbox, environment info, file I/O, persistent memory (`remember`/`forget`).
- **Filesystem** ([`FieldCure.Mcp.Filesystem`](https://www.nuget.org/packages/FieldCure.Mcp.Filesystem)) — Secure file operations within workspace folders. Per-tab instances with MCP Roots protocol for dynamic folder updates.
- **Knowledge Archive** ([`FieldCure.Mcp.Rag`](https://www.nuget.org/packages/FieldCure.Mcp.Rag)) — Multi-KB document indexing and search for RAG. Shared server with `kb_id` parameter, per-conversation KB selection, and embedding model configuration.
- **Outbox** ([`FieldCure.Mcp.Outbox`](https://www.nuget.org/packages/FieldCure.Mcp.Outbox)) — Send messages via Slack, Telegram, Discord, Email (SMTP), and KakaoTalk. Shared instance across all tabs.
- **BuiltInServerHelper** — Auto-installs and auto-updates built-in servers to latest NuGet version via `dotnet tool` on app startup. Read-only tools (read, list, search) skip user approval.

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
    StreamingEnabled = true,
    ThinkingEnabled = true,
    ThinkingBudget = 8192
};
```

### Profiles

Profiles pair a system prompt with optional provider/model preferences and tool selections:

```csharp
var profile = new Profile
{
    Name = "Task Planner",
    SystemPrompt = "You are a task planner that breaks down complex requests into steps.",
    EnabledServers = ["essentials", "memory", "builtin_filesystem"],
    UseSearchTools = true  // Use meta-tool instead of sending all tool definitions
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

## Ecosystem

### MCP Servers

| Package | Version | Description |
|---------|---------|-------------|
| [FieldCure.Mcp.Essentials](https://github.com/fieldcure/fieldcure-mcp-essentials) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Essentials)](https://www.nuget.org/packages/FieldCure.Mcp.Essentials) | 12–16 tools — HTTP, web search (+ news/images/scholar/patents), web/document fetching, shell, JavaScript sandbox, environment info, file I/O, persistent memory |
| [FieldCure.Mcp.Outbox](https://github.com/fieldcure/fieldcure-mcp-outbox) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Outbox)](https://www.nuget.org/packages/FieldCure.Mcp.Outbox) | Multi-channel messaging — Slack, Telegram, Discord, Email (SMTP/Graph), KakaoTalk |
| [FieldCure.Mcp.Filesystem](https://github.com/fieldcure/fieldcure-mcp-filesystem) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Filesystem)](https://www.nuget.org/packages/FieldCure.Mcp.Filesystem) | Sandboxed file/directory operations with built-in document parsing (DOCX, HWPX, XLSX, PDF) |
| [FieldCure.Mcp.Rag](https://github.com/fieldcure/fieldcure-mcp-rag) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Rag)](https://www.nuget.org/packages/FieldCure.Mcp.Rag) | Document search — hybrid BM25 + vector retrieval, multi-KB, incremental indexing |
| [FieldCure.Mcp.PublicData.Kr](https://github.com/fieldcure/fieldcure-mcp-publicdata) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.PublicData.Kr)](https://www.nuget.org/packages/FieldCure.Mcp.PublicData.Kr) | Korean public data gateway — data.go.kr (80,000+ APIs) |
| [FieldCure.AssistStudio.Runner](https://github.com/fieldcure/fieldcure-assiststudio-runner) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Runner)](https://www.nuget.org/packages/FieldCure.AssistStudio.Runner) | Headless LLM task runner with scheduling via Windows Task Scheduler |

### Libraries

| Package | Version | Description |
|---------|---------|-------------|
| [FieldCure.Ai.Providers](https://github.com/fieldcure/fieldcure-assiststudio) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Ai.Providers)](https://www.nuget.org/packages/FieldCure.Ai.Providers) | Multi-provider AI client — Claude, OpenAI, Gemini, Ollama, Groq with streaming and tool use |
| [FieldCure.Ai.Execution](https://github.com/fieldcure/fieldcure-assiststudio) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Ai.Execution)](https://www.nuget.org/packages/FieldCure.Ai.Execution) | Agent loop and sub-agent execution engine for autonomous tool-use workflows |
| [FieldCure.AssistStudio.Core](https://github.com/fieldcure/fieldcure-assiststudio) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core) | MCP server management, tool orchestration, and conversation persistence |
| [FieldCure.AssistStudio.Controls.WinUI](https://github.com/fieldcure/fieldcure-assiststudio) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) | WinUI 3 chat UI controls — WebView2 rendering, streaming, conversation branching |
| [FieldCure.DocumentParsers](https://github.com/fieldcure/fieldcure-document-parsers) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers)](https://www.nuget.org/packages/FieldCure.DocumentParsers) | Document text extraction — DOCX, HWPX, XLSX, PPTX with math-to-LaTeX |
| [FieldCure.DocumentParsers.Pdf](https://github.com/fieldcure/fieldcure-document-parsers) | [![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers.Pdf)](https://www.nuget.org/packages/FieldCure.DocumentParsers.Pdf) | PDF text extraction add-on for DocumentParsers |

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
