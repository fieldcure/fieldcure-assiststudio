# Release Notes — FieldCure.Ai.Execution

## v0.2.0

- `AgentLoopResult.Messages`: expose full conversation history (user, assistant, tool messages) accumulated during the loop for detailed audit trails and execution logging

## v0.1.0

- Initial release — agent loop and sub-agent execution engine
- `AgentLoop`: autonomous LLM tool-use loop (prompt - model call - tool execution - repeat)
- `SubAgentExecutor`: isolated sub-agent sessions with timeout management
- `ContextHintKeys` + `SystemPromptHints`: context propagation (RAG kb_id, etc.)
- Models: `SubAgentRequest`, `SubAgentResult`, `AgentLoopContext`, `AgentLoopResult`
- Depends on `FieldCure.Ai.Providers` only (no MCP SDK dependency)
