# Release Notes — FieldCure.AssistStudio.Core

## v0.19.0 (2026-05-04)

### Breaking
- **`Profile.PreferredProviderType` and `Profile.PreferredModelId` removed**, replaced by a single `PreferredModelName` that points at a `ProviderModel.Name` directly. Old profile JSON keeps loading via the legacy fields (`LegacyPreferredProviderType` / `LegacyPreferredModelId`), which the host migrates once and clears on next save. External consumers reading these fields directly need to switch to `PreferredModelName`.

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.7.0** (`ProviderPreset → ProviderModel` rename, `ChatMessage.IsHidden` / `IsContinuation` / `IsTruncated`, `ProviderModelBroadcast`, Gemini inline image output, audio attachment scaffold).

### Migration
- For external code that read profiles directly:
  - `profile.PreferredProviderType` → use `profile.PreferredModelName` and look up the `ProviderModel` to derive the type.
  - `profile.PreferredModelId` → same `PreferredModelName` lookup gives you the model id.
- For `.astx` / saved profile JSON: no action needed — first load runs the legacy → `PreferredModelName` migration in-place.

---

## v0.18.0 (2026-04-27)

### Breaking — namespace rename

Public namespaces now include the `.Core` segment, matching the package
name `FieldCure.AssistStudio.Core`. External consumers must update
`using` statements:

| Before | After |
|---|---|
| `FieldCure.AssistStudio.Helpers` | `FieldCure.AssistStudio.Core.Helpers` |
| `FieldCure.AssistStudio.Models`  | `FieldCure.AssistStudio.Core.Models`  |
| `FieldCure.AssistStudio.Tools`   | `FieldCure.AssistStudio.Core.Tools`   |

Quick migration (PowerShell, run from your repo root):

```powershell
Get-ChildItem -Recurse -Include *.cs |
  ForEach-Object {
    (Get-Content $_.FullName) `
      -replace 'FieldCure\.AssistStudio\.Helpers','FieldCure.AssistStudio.Core.Helpers' `
      -replace 'FieldCure\.AssistStudio\.Models','FieldCure.AssistStudio.Core.Models' `
      -replace 'FieldCure\.AssistStudio\.Tools','FieldCure.AssistStudio.Core.Tools' |
    Set-Content $_.FullName
  }
```

No type names or signatures changed — only the namespace prefix.

### Added
- **`invoke_tool` dispatcher** — meta tool exposed alongside `search_tools`
  so Claude-class models can reach external MCP tools (PublicData.Kr,
  user-added servers) without going through `delegate_task`. Fixed schema
  `(name, args)`; `ToolCallExecutor` unwraps the inner call and re-dispatches
  through the same path as built-in tools (confirmation, multimedia routing,
  fallback resolution all unchanged).
- **`ToolCall.ProviderSignature`** — opaque round-trip token for Gemini 2.x
  `thoughtSignature`. Other providers leave it null.
- **Judgment specialist hardening** — `ISpecialist` gains
  `ExpectedFirstHeading` and `ForbiddenTrailingHeadings` for output-discipline
  post-processing; per-specialist constants (`SpecialistName`) replace
  duplicate string literals across routing/settings.
- XML doc comments across `AssistStudio.Core.Helpers` (surface unchanged).

### Changed
- Documented union semantics on `ISpecialist`: `FallbackServers` /
  `AllowedTools` are the specialist's responsibility, never gated by the
  parent profile.
- `StreamToolCallAccumulator` carries `thoughtSignature` for Gemini and
  recognises gpt-5+ as a reasoning-model family alongside the o-series.

---

## v0.17.0 (2026-04-21)

### Added
- **`KnowledgeBase.IndexedWith`** (nullable) — snapshot of the embedding and contextualizer configuration captured at the last re-index launch, so Knowledge Bases page cards stay stable when the user edits the top-level fields without triggering a re-index.
- **`IndexedWithSnapshot`** class — holds `Embedding` and `Contextualizer` `KbProviderConfig` values. Populated by the host app right before calling `StartExecAsync`; ignored by the RAG exec itself.

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.5.0** — picks up the transitive `FieldCure.DocumentParsers 2.x` upgrade. Core does not touch the relocated types directly.

---

## v0.16.0 (2026-04-14)

### Fixed
- Update stale `.astd` references to `.astx` and filter MRU by extension

### Changed
- Ai.Providers 0.3.0 → 0.4.0

---

## v0.15.0 (2026-04-10)

### Changed
- Updated transitive dependency: Ai.Providers 0.2.0 → 0.3.0
- No source changes in Core itself

---

## v0.14.0 (2026-04-07)

### Added
- `ISpecialist` interface — specialist agent abstraction with `Name`, `Description`, `Icon`, `AllowedTools`, and `ExecuteAsync` for domain-specific sub-agent routing
- `KnowledgeBase` model — persistent KB metadata (Id, Name, FolderPath, EmbeddingModel, CreatedAt) for multi-KB management
- `BuiltInServerConfig.RequiredVersion` property for minimum version enforcement on built-in MCP servers

### Changed
- `ToolCallExecutor` — supports multimedia tool results (`IMultiContentTool`), parallel execution for multiple tool calls in a single round
- `ToolResolver` — stable sort order for deterministic tool list presentation

### Fixed
- `HardwareProbe` — WMIC fallback for GPU detection on systems without WMI CIM provider

---

## v0.13.0 (2026-03-31)

### Changed
- **Providers extracted** — AI providers, shared models, and streaming types moved to new `FieldCure.Ai.Providers` package. Core now references Providers as a dependency.
- **Essentials MCP server** — in-process virtual Essentials server replaced with external `FieldCure.Mcp.Essentials` MCP server process
- Built-in tool classes removed (`ReadFileTool`, `WriteFileTool`, `SearchFilesTool`, `RunCommandTool`, `UrlFetchTool`) — now provided by Essentials MCP server

### Added
- `http_request` added to ReadOnlyToolNames (no confirmation required, matching previous `fetch_url` behavior)
- Essentials and Runner server cards on ConnectPage
- Profile `EnabledServers` migration from `"essentials"` to `"builtin_essentials"` convention
- Duplicate tool name resolution — stateful servers (Filesystem) take precedence over shared servers (Essentials)

### Fixed
- Streaming token batching (50ms intervals) to reduce WebView2 calls and improve UI responsiveness

---

## v0.12.0 (2026-03-30)

### Added
- MCP Outbox built-in server support — shared (folderless) server pattern with `IsSharedServer()` helper
- On-demand connect for shared built-in servers in `PrepareToolsForSendAsync`

### Fixed
- Empty tool arguments crash for parameterless MCP tools (`list_channels`) — guard `JsonSerializer.Deserialize` and `JsonNode.Parse` with `"{}"` fallback
- `ClaudeProvider`, `GeminiProvider`, `OllamaProvider`: same empty arguments guard in `BuildRequestBody`

---

## v0.11.0 (2026-03-29)

### Added
- Essentials virtual server — bundle built-in tools (`read_file`, `remember`, `forget`) as an in-process server
- Persistent memory tools (`remember`, `forget`) for cross-conversation context retention
- `Profile.ToolSettingsChanged` event and shared profile cache for per-profile server toggles

### Changed
- `Profile.Text` renamed to `Profile.SystemPrompt` for clarity (`JsonPropertyName("Text")` preserved for backward compatibility)
- `DocumentParsers` migrated to independent NuGet package (`FieldCure.DocumentParsers` 0.3.x)
- `IAssistTool` logging enhanced for Knowledge Base lifecycle and tool execution

### Fixed
- MCP tool schemas normalized for Gemini API compatibility (strip unsupported keywords)
- `ActiveChildId` restore on load — prevent false branch detection from tool call chains

---

## v0.10.0 (2026-03-24)

### Added
- `IAssistTool.DisplayName` used for UI grouping — server-owned tools hidden from individual tool list when server checkbox exists

### Changed
- `McpToolAdapter.ServerName` used to filter tools from server-level toggles in tool flyout

---

## v0.9.0 (2026-03-24)

### Added
- `BuiltInServerConfig` model for built-in MCP server configuration (enabled, folders)
- `McpToolAdapter.ServerName` property for server-level tool grouping
- `ToolResolver` server-level tool resolution with `search_tools` default

### Changed
- Tool resolution switched from tool-level to server-level toggles

---

## v0.8.0 (2026-03-22)

### Added
- `FieldCure.DocumentParsers` extracted as independent NuGet package (`v0.1.0`)
- `DocumentParserFactory.SupportedExtensions` for dynamic extension discovery

### Changed
- Document parsing moved from `AssistStudio.Controls` to `FieldCure.DocumentParsers` package
- Core now references `DocumentParsers` project for document text extraction

---

## v0.7.0 (2026-03-21)

### Added
- Extended thinking support with `ThinkingOverride` and per-provider `ThinkingCapability` architecture
- `StreamEvent` replaces raw `string` in `IAiProvider.StreamAsync` (`IAsyncEnumerable<StreamEvent>`)
- Streaming tool call accumulation via `StreamToolCallAccumulator`
- Ollama `<think>` tag parsing into `ThinkingDelta` stream events
- `UrlFetchTool` built-in tool for web page content extraction
- `McpServerConfig` and `McpToolAdapter` for MCP (Model Context Protocol) integration
- `search_tools` meta-tool support with dynamic MCP metadata injection
- `DiagnosticLogger` structured logging hooks (`OnInfo`, `OnWarning`, `OnException`)
- `ModelCompatibility` helper for provider-specific feature detection
- `ContextProvider` delegate and `WorkspaceContext` model for tool context injection
- `ConversationToolState` model for tracking tool state across conversation turns

### Changed
- **Breaking:** `IAiProvider.StreamAsync` returns `IAsyncEnumerable<StreamEvent>` instead of `IAsyncEnumerable<string>`
- Providers implement proper `IDisposable` pattern (CA1816)
- Ollama remote host UX improved with timeout, error messages, and custom URL support
- Conversation file extension renamed from `.astx` to `.astd` (AssistStudio Document)

### Removed
- `ConversationManager` and `AppJsonContext` moved to App layer (not part of NuGet package)

---

## v0.6.0 (2026-03-17)

### Added
- `AppJsonContext` and `IndentedJsonContext` source-generated JSON serializer contexts for trim-safe serialization

### Changed
- `ConversationManager` uses `IndentedJsonContext.Default.ConversationData` instead of reflection-based `JsonSerializerOptions`

---

## v0.5.0 (2026-03-17)

### Added
- Dedicated NuGet package README with Core-specific usage examples and API reference

### Fixed
- GitHub repository URL corrected (`fieldlab` → `fieldcure`)

---

## v0.4.0 (2026-03-17)

### Added
- NuGet package metadata (Company, Copyright, Icon, README, Repository URL, Tags)
- Release notes auto-inclusion in NuGet package
- `publish-nuget.ps1` script for pack → sign → push workflow

---

## v0.3.0 (2026-03-17)

### Added
- Generic file and command tools for agentic workflows (`ReadFileTool`, `WriteFileTool`, `RunCommandTool`, etc.)
- `broadFileSystemAccess` support for tool file operations

---

## v0.2.0 (2026-03-16)

### Added
- Tool calling support for `ClaudeProvider` and `GeminiProvider`
- `PdfCapability` enum with `Auto`, `TextExtraction`, `NativePdf`, `PageAsImage` options
- `PdfCapability` property on `ProviderPreset` for per-preset PDF handling
- `AttachmentProcessor.RenderPdfPages()` for PDF-to-image conversion (via PDFtoImage)
- `PageAsImage` PDF handling in `OpenAiProvider` and `OllamaProvider` for vision models
- `DisplayName` default interface member on `IAssistTool` for human-readable UI labels
- `ProviderFactory` auto-resolves `PdfCapability.Auto` based on provider type

### Fixed
- Gemini tool call ID uniqueness for parallel calls

---

## v0.1.0 (2026-03-15)

### Added
- `IAiProvider` abstraction with `StreamAsync` returning `IAsyncEnumerable<string>`
- Provider implementations: `ClaudeProvider`, `OpenAiProvider`, `GeminiProvider`, `OllamaProvider`
- SSE and NDJSON streaming support
- Model listing (`ListModelsAsync`) for all providers
- `OllamaManager` for local model management (pull, delete, search)
- `OllamaFitPolicy` for automatic model selection based on hardware
- Token tracking (`TokenUsage` model)
- Conversation persistence (`ConversationManager`)
- Hardware detection helpers (`HardwareInfo`)
- Image and document attachment models (`ChatAttachment`)
- Profile and ProviderPreset models
- Tool use abstractions (`IAssistTool`, `ToolCall`, `ToolResult`)
