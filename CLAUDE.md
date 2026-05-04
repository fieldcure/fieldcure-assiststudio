# FieldCure AssistStudio

WinUI 3 AI chat control library — multiple NuGet packages plus a main desktop app and an SDK sample.

## Solution structure

```
FieldCure.AssistStudio.slnx              ← .slnx format (requires VS 17.12+)
src/
├── Ai.Providers/                        → NuGet: FieldCure.Ai.Providers (net8.0)
│   ├── Models/                          IAiProvider, IAssistTool, AiRequest, AiResponse, ChatMessage,
│   │                                    ProviderPreset, AiModel, MediaContent, ContextProvider
│   ├── Providers/                       ProviderFactory, IModelManager, OllamaModelManager,
│   │                                    PromptBuilder, SseReader, ThinkingCapability
│   ├── Helpers/                         AttachmentProcessor, ImageCompressor
│   └── Export/                          MarkdownExportResult and helpers
│
├── Ai.Execution/                        → NuGet: FieldCure.Ai.Execution (net8.0)
│   ├── AgentLoop, SubAgentExecutor      Tool-loop orchestration over IAiProvider
│   └── Models/                          SubAgentRequest, AgentLoopContext, ContextHintKeys
│
├── AssistStudio.Core/                   → NuGet: FieldCure.AssistStudio.Core (net8.0)
│   ├── ISpecialist                      Specialist agent abstraction
│   ├── Models/                          Profile, McpServerConfig, KnowledgeBase, WorkspaceContext, …
│   └── Helpers/                         ToolResolver, ToolCallExecutor, ModelCompatibility, OllamaHelper, …
│
├── AssistStudio.Controls/               → NuGet: FieldCure.AssistStudio.Controls.WinUI
│   │                                     (net8.0-windows10.0.19041.0 + net9.0-windows10.0.19041.0)
│   ├── ChatPanel/                       WebView2-based chat surface
│   ├── ComposeBar/                      Input area: text, attachments, preset/profile selectors
│   ├── Panels/                          ToolApprovalPanel, ToolElicitationPanel, AttachmentPreviewBar
│   ├── Rendering/                       WebView2 host, JS bridge, marked.js renderer
│   └── Themes/Generic.xaml              TemplatedControl defaults (PART_ convention)
│
├── AssistStudio.Anthropic/              → NuGet: FieldCure.AssistStudio.Anthropic (net8.0/net9.0)
│   ├── AnthropicMessageConverter        ChatMessage ↔ Anthropic SDK message params
│   └── AnthropicStreamEventMapper       Maps RawMessageStreamEvent → Controls.StreamEvent
│
├── AssistStudio.Controls.Anthropic/     → NuGet: FieldCure.AssistStudio.Controls.Anthropic.WinUI
│   └── ChatPanelExtensions              BeginAnthropicTurn, BuildAnthropicParams,
│                                        StreamAnthropicAsync (consumer-driven flow helpers)
│
└── AssistStudio/                        → Main desktop app (net9.0-windows10.0.19041.0)
    ├── Modules/{Views,ViewModels}/      MainWindow, ChatTabView, ChatTabViewModel, MainViewModel
    ├── Settings/                        ModelsPage, PersonalizationPage, SchedulePage, MemoryPage, …
    ├── Helpers/                         AppSettings, ConversationManager (.astx I/O), PasswordVaultHelper
    └── Mcp/                             MCP server lifecycle and process management

samples/
└── AnthropicSdkSample/                  → SDK demo app — uses ChatPanel with DisableInternalSendFlow,
                                           ShowPresetSelector="False", own model picker in title bar

tests/
├── Ai.Providers.Tests/, Ai.Execution.Tests/
├── AssistStudio.Anthropic.Tests/, AssistStudio.Core.Tests/, AssistStudio.App.Tests/
└── TestMcpServer/                       Test MCP server for elicitation / multi-tool scenarios
```

## Architecture

- **Core packages are platform-neutral** (`net8.0`). Windows-only members are gated by `[SupportedOSPlatform("windows")]`.
- **Controls are TemplatedControl**: `Themes/Generic.xaml` + `PART_` naming + `OnApplyTemplate()` overrides.
- **Single WebView2 renderer**: ChatPanel hosts one WebView2; messages append via JS DOM, raw text streams with a cursor (▌) and finalize through marked.js.
- **Streaming**: providers expose `IAsyncEnumerable<string>` plus structured `StreamEvent`s; SDK consumers can also pipe raw SDK events through `StreamAnthropicAsync`.
- **Provider pattern**: `IAiProvider` (in `Ai.Providers`) → `ProviderFactory.Create(ProviderPreset)`. Built-in providers: Claude, OpenAI, Gemini, Ollama, Mock, plus custom (Generic OpenAI- or Anthropic-compatible).
- **Tool calling**: `IAssistTool` + `RequiresConfirmation` → inline approval via `ToolApprovalPanel`; elicitation flows through `ToolElicitationPanel`.
- **SDK adapter packages** (`AssistStudio.Anthropic`, `AssistStudio.Controls.Anthropic`) let consumers drive the chat panel with a raw SDK client (set `DisableInternalSendFlow`, hide preset/profile selectors, own the request loop).

## Conversation persistence

- Conversations are saved as **`.astx`** files — ZIP archives containing `manifest.json`, `conversation.json`, and an optional `media/` directory for binary attachments.
- Owned by `ConversationManager` (`src/AssistStudio/Helpers/ConversationManager.cs`); current format version is `2`.
- File association is registered through `Package.appxmanifest`. No other extension (e.g., `.astd`) is used.

## Build & test

```bash
dotnet build                    # Build the whole solution
dotnet test                     # Run all test projects under tests/
```

## NuGet publishing

```powershell
# pack → sign (GlobalSign EV) → push
.\scripts\publish-nuget.ps1 -NuGetApiKey <key>

# Local-only test (no signing, no push)
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush
```

## Coding conventions

- C# 12, nullable enable, implicit usings.
- XML doc comments (`/// <summary>`) on every method including private and event handlers — `GenerateDocumentationFile` is enabled.
- `#region` blocks group code (Properties, Fields, Methods, Events, …).
- `INotifyPropertyChanged` paired with the local `SetField<T>()` helper.
- The main desktop app uses `CommunityToolkit.Mvvm`.
- Namespaces: `FieldCure.Ai.*` for newer packages, `FieldCure.AssistStudio.*` for the original ones, `AssistStudio` (no prefix) for the main app.

## Notes

- API keys live in `ProviderPreset.ApiKey` in memory only (`[JsonIgnore]`). The main app persists them in Windows PasswordVault; MCP servers receive them via environment variables per ADR-001.
- `.slnx` format requires Visual Studio 17.12+.
- Some NuGet csproj files may still have a stale `RepositoryUrl` pointing at `fieldlab-assiststudio`; verify before publishing.
