# Release Notes — FieldCure.Ai.Providers

## v0.6.0 (2026-04-27)

### Added
- **Anthropic prompt caching** — Claude provider paths now emit `cache_control` markers across system prompt, conversation prefix, and tool manifests. Reduces input-token cost on long-running conversations and tool-heavy turns. Cache hit metrics surface through usage events.
- **gpt-5+ reasoning support** — recognised as a reasoning-model family alongside the o-series. Reasoning models reject `max_tokens` (require `max_completion_tokens`) and only accept the default temperature (1.0). Tightened o-series detection so unrelated names starting with `o` no longer match.
- **Gemini `thoughtSignature` round-trip** — Gemini 2.x rejects follow-up tool requests with "Function call is missing a thought_signature" when the original signature isn't echoed back. The opaque token now flows through the streaming pipeline (`StreamEvent.ToolCallStart` → `StreamToolCallAccumulator` → `ToolCall.ProviderSignature`) and is replayed on the next request. Other providers leave the field null.

### Changed
- **Claude Opus 4.7+ drops `temperature`** — Anthropic now rejects explicit temperature on Opus 4.7 and later. The provider omits the field entirely for these models.

### Fixed
- **Gemini `function_response` JSON array handling** — when a tool returns a JSON array, the response is now wrapped correctly so Gemini does not reject the follow-up.

### Internal
- XML doc comments added across `Ai.Providers` internals (no public surface change).
- Rebuilt against `FieldCure.AssistStudio.Core` 0.18.0 (transitive `.Core` namespace rename — no code change in Providers).

---

## v0.5.0 (2026-04-21)

### Changed
- **FieldCure.DocumentParsers 1.x → 2.x** — PDF text extraction is now served by the core `DocumentParsers` package (PdfPig auto-registered); `IMediaDocumentParser` (used by `AttachmentProcessor.RenderPdfPages`) is resolved via the host-registered `FieldCure.DocumentParsers.Imaging` package. Consumers must rebuild against the new transitive type locations.

---

## v0.4.0 (2026-04-14)

### Added
- **Markdown export** — `ConversationExporter` with per-message provider attribution and `<details open>` structure
- **File attachment deduplication** — deduplicate by source path before sending to providers
- **Multi-attachment labeling** — unified numbering across all providers for multiple file attachments
- **Paste-as-attachment** — auto-convert long pasted text to attachment chip
- **Token compression summary** — `SummaryMeta` model with compression ratio display in summary header
- **Auxiliary provider settings** — per-task provider overrides with runtime fallback

### Changed
- **Frontmatter removal** — remove frontmatter provider/model from conversation format
- **Tool result deduplication fix** — prevent duplicate tool results in conversation history
- **Timestamp preservation** — preserve original response timestamp and elapsed time across save/load
- **`.astx` migration** — update stale `.astd` references and filter MRU by extension

---

## v0.3.0

### Added
- `CustomProviderConfig` model — configuration for user-registered OpenAI-compatible custom providers (Id, DisplayName, BaseUrl)
- `ProviderFactory.RegisterCustomProvider` / `UnregisterCustomProvider` / `ClearCustomProviders` — runtime custom provider registration
- `ThinkTagParser` — streaming parser that separates `<think>...</think>` blocks from regular content across chunk boundaries

### Changed
- `OpenAiProvider` — `reasoning_details` array parsing for MiniMax-style structured thinking; `StripThinkTags` fallback for providers without structured reasoning
- `OpenAiProvider` — `stream_options` (`include_usage`) scoped to OpenAI/Groq only; custom providers no longer receive unsupported options
- `ProviderPreset.RequiresApiKey` — now returns `true` for `Custom_*` provider types
- `ProviderFactory.ResolvePdfCapability` — custom providers default to `PdfCapability.TextExtraction`

---

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
