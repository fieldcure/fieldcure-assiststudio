# AssistStudio — App Definition (v1.0)

> Source of truth for product positioning. All marketing copy, store
> listings, screenshots, and pitch materials should derive from this
> document. Update here first, propagate downstream.

## 1. Identity

**Provider-agnostic AI workspace for Windows.**

A Windows-native (WinUI 3) desktop application. Switch between Claude,
GPT, Gemini, Ollama, and other providers in one screen — while keeping
the same MCP tools, knowledge bases, and working files. Released
simultaneously on Microsoft Store and GitHub. MIT-licensed.

### Category coordinates

| Compared to | Difference |
|---|---|
| ChatGPT / Claude.ai | Cloud-locked, single-provider |
| Claude Desktop | Rich MCP support, but Anthropic-only |
| LibreChat / Open WebUI | Multi-provider, but weak desktop integration and shallow MCP |
| LangChain / LiteLLM | Developer SDK, not an end-user app |
| **AssistStudio** | **desktop-native ∩ multi-provider ∩ deep MCP ∩ workspace persistence** |

### Primary audience

- Power users who consciously rotate between AI providers
- Knowledge workers in Korea (primary launch market)
- Developers integrating AI into local workflows

## 2. Hero

- **Tagline (KO):** "AI는 갈아끼워도, 도구와 작업은 그대로"
- **Tagline (EN):** "Switch the AI. Keep the work."
- **Subtitle:** Same MCP tools, same knowledge base — across Claude, GPT, Gemini, and Ollama.

## 3. Architecture: Foundation + 3 Pillars

```
Foundation (invisible to the user):
  (1) Provider Abstraction
      |- Unified Conversation Schema   absorbs chat protocol differences
      `- MCP Universal Bridge          absorbs tool protocol differences

Pillars (user-visible value):
  (2) Ready-to-Work Capability
      "Get work done immediately."
      |- Built-in MCP Suite            5 servers + freedom to add external MCP
      |    Filesystem . RAG . Essentials . Outbox . Runner
      `- Capability-Aware UX           UI adapts to per-provider feature gaps

  (3) Parallel Agentic Workflow
      "See how the AI works - and stay in control."
      |- Role-Specialized Sub-Agents   parallel fan-out + context isolation
      `- Tool Approval / Elicitation   pre-execution approval + dynamic input forms

  (4) Persistent Workspace
      "Your work is yours."
      |- .astx archive                 zip package: conversation + media + manifest
      |- Branching conversation        explore multiple paths in one thread
      |- Per-conversation folder       working files bound to a conversation
      `- RAG + Memory                  persistent knowledge and recall
```

**Narrative arc:** Any AI you choose (1) → ready to work immediately (2) →
visible and controllable execution (3) → results stay yours (4).

### Resource allocation rule

- **RAG** belongs to both (2) (part of the MCP suite) and (4) (persistent
  asset), but is **promoted only in (4)** to avoid duplicate messaging.
- **Memory** follows the same rule — it is a feature of the Essentials
  MCP server, but user-facing positioning ties it to (4)'s persistence
  narrative.

## 4. What AssistStudio Is *Not* (Anti-positioning)

| Not this | Reason |
|---|---|
| **An SDK or library** | Unlike LangChain or LiteLLM, this is an end-user desktop app. |
| **A single-provider client** | Alternative to ChatGPT and Claude Desktop, but not bound to that category. |
| **A cloud SaaS** | Conversations, files, and KBs persist on the user's PC. No service-shutdown risk. |
| **A coding-only tool** | Different lane from Cursor. General productivity, with strong dev support. |
| **An autonomous agent platform** | User control and approval are core. Autonomous execution is limited to explicit Runner scheduling. |
| **Just another MCP client** | MCP is a means, not the identity. AssistStudio is a *workspace*, not an *MCP host*. |

## 5. One-Line Definition (for external citation)

> **AssistStudio** is a Windows-native AI workspace where the same tools,
> knowledge, and working files follow you across any AI model.
> Conversations persist on the user's PC as `.astx` files, five MCP
> servers run out of the box, and multiple specialist sub-agents run in
> parallel to deepen answers.

---

*Last updated: 2026-05-09 — v1.0 release cycle.*
