# Release Notes — FieldCure.AssistStudio.Core

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
