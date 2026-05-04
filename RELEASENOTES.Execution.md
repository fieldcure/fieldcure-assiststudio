# Release Notes — FieldCure.Ai.Execution

## v0.4.0 (2026-05-04)

### Breaking
- **`SubAgentRequest.PresetName` renamed to `ModelName`** — follows the `ProviderPreset → ProviderModel` rename in `Ai.Providers` 0.7.0.
- **`SubAgentResult.UsedPreset` renamed to `UsedModel`** — same rationale.

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.7.0**. Picks up `ChatMessage.IsHidden` / `IsContinuation` / `IsTruncated`, `ProviderModelBroadcast` helper, Gemini inline image output, and the audio attachment scaffold transitively.

### Migration
- Direct rename — fix-up is mechanical:
  - `req.PresetName = "Claude"` → `req.ModelName = "Claude"`
  - `result.UsedPreset` → `result.UsedModel`
- No semantic change; the field still holds the resolved provider/model identifier or `null` for inherit-from-parent.

---

## v0.3.3 (2026-04-27)

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.6.0** (Anthropic prompt caching, gpt-5+ reasoning, Gemini `thoughtSignature` round-trip) and **FieldCure.AssistStudio.Core 0.18.0** (`.Core` namespace segment). Two `using` statements in `AgentLoop` and `SubAgentExecutor` updated to the new `FieldCure.AssistStudio.Core.*` namespaces. No public API changes in this package.

---

## v0.3.2 (2026-04-24)

### Added
- **`AgentLoopStatus.Truncated`** / **`SubAgentStatus.Truncated`** — new terminal status emitted when the model's final (tool-free) response was cut off by the provider's `max_tokens` cap. Previously this case fell through to `Completed` with a partial summary, so callers could not distinguish a graceful end from a mid-generation cutoff.
- `AgentLoop` now logs the truncation event with the partial content length.

### Behavior
- Context-guard forced-finish rounds (`MaxContextChars` threshold) remain classified as `Completed` even if `IsTruncated` is set — the summary was requested on purpose, so the partial marker does not apply.

### Migration
- Additive change only. Existing consumers continue to compile; `switch` statements that are exhaustive over the status enum may raise a warning for the new value and should add a case if they care about the distinction.

---

## v0.3.1 (2026-04-21)

### Changed
- Rebuilt against **FieldCure.Ai.Providers 0.5.0** — picks up the transitive `FieldCure.DocumentParsers 2.x` upgrade. No public API changes in this package.
- RAG system-prompt hint label synced to "Knowledge Base" to match the rename in the rest of the stack.

---

## v0.3.0 (2026-04-14)

### Breaking
- `SubAgentExecutor.ProviderResolver` is now async (`Task<IAiProvider>`)
- Removed `_defaultPresetName`; null `PresetName` resolved by caller's fallback policy

### Added
- `LogCallback` on `AgentLoop` for host-injected logging
- Per-task auxiliary provider settings with runtime fallback

---

## v0.2.0

- `AgentLoopResult.Messages`: expose full conversation history (user, assistant, tool messages) accumulated during the loop for detailed audit trails and execution logging

---

## v0.1.0

- Initial release — agent loop and sub-agent execution engine
- `AgentLoop`: autonomous LLM tool-use loop (prompt - model call - tool execution - repeat)
- `SubAgentExecutor`: isolated sub-agent sessions with timeout management
- `ContextHintKeys` + `SystemPromptHints`: context propagation (RAG kb_id, etc.)
- Models: `SubAgentRequest`, `SubAgentResult`, `AgentLoopContext`, `AgentLoopResult`
- Depends on `FieldCure.Ai.Providers` only (no MCP SDK dependency)
