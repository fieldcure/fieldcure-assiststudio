# Release Notes — FieldCure.Ai.Providers

## v0.2.0

### Added
- `ImageCompressor` — automatic JPEG compression and resize for large images before sending to providers (via SkiaSharp)
- `IMultiContentTool` interface — tools returning structured multimedia content (images, audio, video) alongside text
- `MediaContent` model — typed media payload (MimeType, Base64Data, FileName) for tool results
- `ToolExecutionResult` model — combined text + media content return type for multi-content tools
- `ChatMessage.MediaItems` property — media attachments persisted with conversation messages

### Changed
- `ClaudeProvider`, `OpenAiProvider`, `GeminiProvider` — automatic image compression applied before API calls
- `McpToolAdapter` — handles MCP multimedia content blocks (image, audio) and converts to `ToolExecutionResult`
- `OllamaProvider` — native `thinking` field support for reasoning models

---

## v0.1.0

- Initial release — extracted from AssistStudio.Core
- AI providers: Claude, OpenAI, Gemini, Ollama, Groq, Mock
- Shared models: ChatMessage, AiRequest, AiResponse, ProviderPreset, ToolCall, IAssistTool
- Streaming support via StreamEvent
- Document attachment processing (PDF, DOCX, images)
