# Release Notes — FieldCure.AssistStudio.Anthropic

## v0.1.0-preview.1 (2026-04-21)

Initial preview — platform-agnostic adapter that lets any .NET host stream from the official `Anthropic` NuGet SDK into the AssistStudio pipeline without implementing `IAiProvider`.

### Added
- **`AnthropicStreamEventMapper`** — stateful, per-stream mapper that converts `IAsyncEnumerable<RawMessageStreamEvent>` into `IAsyncEnumerable<StreamEvent>`. Handles `text_delta`, `thinking_delta`, `input_json_delta`, content-block lifecycle, `usage` rollups, truncation, and graceful fallback for unknown block types.
- **`AnthropicMessageConverter`** — converts `ChatMessage` lists to Anthropic SDK `MessageParam` format. Extracts system prompts, groups messages by role, and handles image/text attachments via the SDK's content block model. Returns a `ConversionResult` with messages + system prompt pair.

### Supported targets
- `net8.0`, `net9.0` — no WinUI / Windows dependency. Console apps, ASP.NET, MAUI hosts supported.

### Known limitations (preview scope)
- Cache-control headers and prompt-caching metadata not yet threaded through; plan to cover in a follow-up preview.
- Tool-result message conversion covers text only — image/audio tool results go through the general attachment path rather than a dedicated content block.
- No backward-compatibility guarantees for preview releases.
