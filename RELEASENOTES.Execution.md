# Release Notes — FieldCure.Ai.Execution

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
