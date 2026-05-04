# Release Notes — FieldCure.AssistStudio.Anthropic

## v0.1.0-preview.3 (2026-05-04)

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.7.0** (`ProviderPreset → ProviderModel` rename, `ChatMessage.IsHidden` / `IsContinuation` / `IsTruncated`, audio attachment scaffold) and **FieldCure.AssistStudio.Core 0.19.0** (`Profile.PreferredModelName`). No public API changes in this package.

### Internal
- `AnthropicMessageConverter.ConvertAssistantMessage` `<remarks>` now spells out the precise rule that kept thinking blocks dropped: text-only multi-turn is fine without thinking blocks, but a `tool_use` block requires the preceding `thinking` block (with its original signature) to be included or the API rejects with 422. Tool support is currently out of scope for this converter; signature round-trip will land alongside tool support per a future ADR. No behavior change.

### Notes
- Preview-stability guarantees still apply: any release before `1.0.0` may break public API.

---

## v0.1.0-preview.2 (2026-04-27)

### Changed
- **Anthropic SDK 12.15.x → 12.16.0** — picks up upstream message and stream-event surface tweaks. The `RawMessageStreamEvent` envelope shape used by `AnthropicStreamEventMapper` is unchanged; rebuild only.
- Rebuilt against **FieldCure.Ai.Providers 0.6.0** (Anthropic prompt caching, gpt-5+ reasoning, Gemini `thoughtSignature` round-trip) and **FieldCure.AssistStudio.Core 0.18.0** (`.Core` namespace segment). Two `using` statements in `AnthropicStreamEventMapper` updated to the new `FieldCure.AssistStudio.Core.*` namespaces. No public API changes in this package.

### Notes
- This package is the platform-agnostic adapter; prompt-caching is a `Ai.Providers` (Claude provider) feature and is reached only through the AssistStudio Claude provider, not through this SDK adapter.
- Preview-stability guarantees still apply: any release before `1.0.0` may break public API.

---

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
