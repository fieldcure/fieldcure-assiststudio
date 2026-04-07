# Release Notes — FieldCure.AssistStudio.Core

## [0.14.0] - 2026-04-07

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

## [0.13.0] - 2026-03-31

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

## [0.12.0] - 2026-03-30

### Added
- MCP Outbox built-in server support — shared (folderless) server pattern with `IsSharedServer()` helper
- On-demand connect for shared built-in servers in `PrepareToolsForSendAsync`

### Fixed
- Empty tool arguments crash for parameterless MCP tools (`list_channels`) — guard `JsonSerializer.Deserialize` and `JsonNode.Parse` with `"{}"` fallback
- `ClaudeProvider`, `GeminiProvider`, `OllamaProvider`: same empty arguments guard in `BuildRequestBody`

---

## [0.11.0] - 2026-03-29

### Added
- Essentials virtual server — bundle built-in tools (`read_file`, `remember`, `forget`) as an in-process server
- Persistent memory tools (`remember`, `forget`) for cross-conversation context retention
- `Profile.ToolSettingsChanged` event and shared profile cache for per-profile server toggles

### Changed
- `Profile.Text` renamed to `Profile.SystemPrompt` for clarity (`JsonPropertyName("Text")` preserved for backward compatibility)
- `DocumentParsers` migrated to independent NuGet package (`FieldCure.DocumentParsers` 0.3.x)
- `IAssistTool` logging enhanced for Knowledge Archive lifecycle and tool execution

### Fixed
- MCP tool schemas normalized for Gemini API compatibility (strip unsupported keywords)
- `ActiveChildId` restore on load — prevent false branch detection from tool call chains

---

## [0.10.0] - 2026-03-24

### Added
- `IAssistTool.DisplayName` used for UI grouping — server-owned tools hidden from individual tool list when server checkbox exists

### Changed
- `McpToolAdapter.ServerName` used to filter tools from server-level toggles in tool flyout

---

## [0.9.0] - 2026-03-24

### Added
- `BuiltInServerConfig` model for built-in MCP server configuration (enabled, folders)
- `McpToolAdapter.ServerName` property for server-level tool grouping
- `ToolResolver` server-level tool resolution with `search_tools` default

### Changed
- Tool resolution switched from tool-level to server-level toggles

---

## [0.8.0] - 2026-03-22

### Added
- `FieldCure.DocumentParsers` extracted as independent NuGet package (`v0.1.0`)
- `DocumentParserFactory.SupportedExtensions` for dynamic extension discovery

### Changed
- Document parsing moved from `AssistStudio.Controls` to `FieldCure.DocumentParsers` package
- Core now references `DocumentParsers` project for document text extraction

---

## [0.7.0] - 2026-03-21

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

## [0.6.0] - 2026-03-17

### Added
- `AppJsonContext` and `IndentedJsonContext` source-generated JSON serializer contexts for trim-safe serialization

### Changed
- `ConversationManager` uses `IndentedJsonContext.Default.ConversationData` instead of reflection-based `JsonSerializerOptions`

---

## [0.5.0] - 2026-03-17

### Added
- Dedicated NuGet package README with Core-specific usage examples and API reference

### Fixed
- GitHub repository URL corrected (`fieldlab` → `fieldcure`)

---

## [0.4.0] - 2026-03-17

### Added
- NuGet package metadata (Company, Copyright, Icon, README, Repository URL, Tags)
- Release notes auto-inclusion in NuGet package
- `publish-nuget.ps1` script for pack → sign → push workflow

---

## [0.3.0] - 2026-03-17

### Added
- Generic file and command tools for agentic workflows (`ReadFileTool`, `WriteFileTool`, `RunCommandTool`, etc.)
- `broadFileSystemAccess` support for tool file operations

---

## [0.2.0] - 2026-03-16

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

## [0.1.0] - 2026-03-15

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
