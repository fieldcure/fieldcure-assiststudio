# Release Notes — FieldCure.AssistStudio.Core

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
