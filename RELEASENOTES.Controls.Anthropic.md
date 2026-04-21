# Release Notes — FieldCure.AssistStudio.Controls.WinUI.Anthropic

## v0.1.0-preview.1 (2026-04-21)

Initial preview — WinUI 3 `ChatPanel` integration for the official `Anthropic` NuGet SDK. Pairs with `FieldCure.AssistStudio.Anthropic` (platform-agnostic) to render vendor-SDK streams directly into the chat UI without a custom `IAiProvider`.

### Added
- **`ChatPanelExtensions`** — extension methods on `ChatPanel` / `AssistantTurnHandle`:
    - `BeginAnthropicTurn(providerName, modelId)` — opens an assistant turn on the panel.
    - `StreamAnthropicAsync(sdkStream, ct)` — consumes an `IAsyncEnumerable<RawMessageStreamEvent>` from the Anthropic SDK, maps it through `AnthropicStreamEventMapper`, and funnels events into the active turn. Returns a `StreamResult` for completion / usage inspection.
    - `GetConversationAsAnthropicMessages()` — snapshots the panel's current conversation and returns a `ConversionResult` (messages + system prompt) ready to pass to `client.Messages.CreateStreaming`.

### Supported targets
- `net8.0-windows10.0.19041.0`, `net9.0-windows10.0.19041.0` — matches the Controls package TFMs.

### Dependencies
- `FieldCure.AssistStudio.Anthropic 0.1.0-preview.1`
- `FieldCure.AssistStudio.Controls.WinUI 0.17.0+`

### Known limitations (preview scope)
- Inherits the conversion limits documented in `FieldCure.AssistStudio.Anthropic 0.1.0-preview.1`.
- No backward-compatibility guarantees for preview releases.
