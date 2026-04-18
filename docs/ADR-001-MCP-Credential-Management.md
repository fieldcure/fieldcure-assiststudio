# ADR-001: MCP Server Credential Management Strategy

**Status:** Proposed
**Date:** 2026-04-18
**Accepted:** After Phase 1 pilot completes
**Applies to:** All FieldCure MCP servers (Essentials, Outbox, PublicData.Kr, RAG, future servers)

---

## Context

FieldCure MCP servers require external API keys and credentials. Prior implementations diverged across servers:

| Server | Previous approach | Issue |
|--------|----------|--------|
| Essentials | `ResolveArg` (CLI → env var → engine-specific env var) + host-side PasswordVault → env var bridge | Server itself is env-var based; fine, but client-dependent |
| Outbox | `add_channel` → re-spawn self → `UseShellExecute` console for credential entry | Does not work on remote or headless hosts — "half-server" |
| PublicData.Kr | env var only (`PUBLICDATA_API_KEY`) | Hard-fails at startup when unset |

On 2026-04-17 the Windows CredentialManager (DPAPI) dependency was removed from Essentials and Outbox. This established a principle that servers do not manage OS-specific secret stores. A general credential strategy is now needed so FieldCure MCP servers can be published to — and function inside — any MCP host (local, remote, Docker, CI).

## Decision

### Priority chain

```
env var (primary)
  └─▶ MCP Elicitation (interactive fallback)
        └─▶ CLI arg (manual test / debug only — not an official path)
```

This chain is consistent with 12-factor app principles and is non-breaking for users who already set env vars.

### Core principles

1. **Servers are stateless with respect to secrets.** A key obtained via Elicitation is cached in process memory (session lifetime) and never written to disk by the server.

2. **Persistence is split.** Persistence of **secrets** (API keys, tokens, passwords) is a host responsibility. A host such as AssistStudio stores them in its own credential store (e.g. Windows PasswordVault) and injects them as env vars when launching the server. Servers do not own OS-specific secret stores. **Non-secret configuration** (channel list, types, endpoint URLs, etc.) may be persisted by the server (e.g. `channels.json`). Any secret components embedded in such configuration (e.g. tokens inside a webhook URL) must still be delegated to the host.

3. **Lazy elicitation.** Servers do not elicit at startup. They elicit on the first tool call that requires the key. `tools/list` must always succeed without a key.

4. **Soft fail.** If no env var is set and elicitation fails (client does not support it, user declines, or the retry cap is exhausted), the server stays up. Only the affected tool returns a structured error:
   ```
   { "error": "API key not configured. Set SERPER_API_KEY environment variable." }
   ```
   `list_tools` continues to respond.

5. **Self-recovery on key invalidation.** On `401/403` from upstream, the server invalidates the cached key and issues a new elicitation. Session-level cap: **2 re-elicits** (the initial elicit is not counted) — this bounds the loop at up to three total elicitation attempts per session. The constant in code (`MaxReElicits = 2`) reflects this semantic.

6. **Env var naming follows service convention.** Use the per-service canonical name (`SERPER_API_KEY`, `TAVILY_API_KEY`, `DATA_GO_KR_API_KEY`). Do not prefix with `FIELDCURE_*`; users who already set the canonical name via the service's own docs would otherwise have to configure it twice.

### Elicitation schema standard

Every FieldCure MCP server uses the same elicitation schema convention:

```json
{
  "message": "Enter your Serper API key (sign up at https://serper.dev).",
  "schema": {
    "type": "object",
    "properties": {
      "api_key": {
        "type": "string",
        "title": "API Key",
        "description": "Serper API Key"
      }
    },
    "required": ["api_key"]
  }
}
```

- Field name: `api_key` for single-credential cases; use meaningful names (`webhook_url`, `bot_token`) for multi-field flows.
- **Known limitation — MCP SDK 1.2:** `StringSchema.Format` only accepts `"email"`, `"uri"`, `"date"`, and `"date-time"`. `"password"` is **not supported** in the spec/SDK at the time of writing. Until a future SDK/spec adds first-class password support, hosts determine whether to render the field as masked based on two heuristics:
  1. The property name matches a known secret pattern (`api_key`, `token`, `secret`, `password`, or ends with `_key`).
  2. The `title` or `description` contains one of `Key`, `Token`, `Secret`, `Password`.

  Servers should help the host by (a) using the canonical field names above and (b) keeping titles/descriptions descriptive (`"API Key"`, not just `"value"`). Revisit once the SDK adds a canonical masked-input hint.
- Shared helper package: see *Extraction trigger* below.

### Clients that do not support Elicitation

MCP Elicitation is a spec 2025-06-18+ feature. On non-supporting clients:

1. `elicitation/create` returns an error / unsupported response.
2. The server falls through to the soft-fail path.
3. The error message names the expected env var so the user can configure it manually:
   ```
   "API key not configured. Set SERPER_API_KEY environment variable,
    or use a client that supports MCP Elicitation."
   ```

### Per-server rollout

**Phase 1 — PublicData.Kr (pilot)**

- Single key, simplest case. Validates the common pattern.
- Replaces startup hard-fail with soft-fail.
- Renames env var `PUBLICDATA_API_KEY` → `DATA_GO_KR_API_KEY` (non-breaking at `v0.x`; aligns with the external Korean government API naming convention). The legacy name remains accepted.

**Phase 2 — Essentials**

- Adds Elicitation as the final link of the existing `ResolveArg` chain.
- Elicitation happens per selected engine, after the engine has been chosen.
- Full chain: `--search-api-key` CLI → `ESSENTIALS_SEARCH_API_KEY` → engine-specific env var → Elicitation.

**Shared-package extraction trigger:** at Phase 2 completion, if the Elicitation implementations in PublicData and Essentials show real duplication, extract them into `FieldCure.Mcp.Common.Credentials`. Use it twice before abstracting — a single use does not reveal the right abstraction.

**Phase 3 — Outbox**

- Replaces the `add_channel` subprocess-console pattern with Elicitation.
- Multi-field elicitation, with per-channel-type schemas.
- The subprocess path stays in place for one `v1.x` compatibility window and is removed in `v2.0`.
- Per Principle 2: channel list/type/endpoint (non-secret config) may live in `channels.json` on the server side; secret components (tokens, etc.) are delegated to the host.

### Version impact

| Server | Current | After ADR | Compatibility |
|--------|------|------------|--------|
| PublicData.Kr | 0.x | 1.0 (debut) | New — non-breaking |
| Essentials | 2.0.x | 2.1.0 (minor) | Non-breaking — Elicitation is an added fallback |
| Outbox | 1.x | 2.0.0 (major) | Requires a `v1.x` deprecation window |

### Success criteria

**Phase 1:**
1. PublicData answers `tools/list` with no env var configured.
2. After elicitation, `call_api` succeeds.
3. A bad key yields 401 → re-elicit → success (verified against AssistStudio).

**Phase 2:**
1. Essentials elicits the engine-specific key when none is supplied via CLI or env var.
2. Switching engines elicits the new engine's key as needed.

**Phase 3:**
1. Outbox `add_channel` works end-to-end via Elicitation, with no subprocess console.
2. Works in external hosts such as Claude Desktop.

## In-memory cache design

The cache key is the env var name (`SERPER_API_KEY`, `DATA_GO_KR_API_KEY`, etc.). Env var and Elicitation paths write to the same slot, so invalidation on 401 → re-elicit → overwrite is consistent.

```
┌─────────────────────────────────────────────┐
│  MCP Server Process (session lifetime)      │
│                                             │
│  Dictionary<string, string> _keyCache       │
│    key = env var name (e.g. SERPER_API_KEY) │
│  Dictionary<string, int> _reElicitCount     │
│                                             │
│  ResolveApiKey(envVarName):                 │
│    1. _keyCache[envVarName] hit? → return   │
│    2. env var present? → cache & return     │
│    3. Elicitation supported?                │
│       → elicit (initial or re-elicit),      │
│         cache & return                      │
│    4. Soft-fail with env var hint           │
│                                             │
│  On 401/403:                                │
│    _keyCache.Remove(envVarName)             │
│    (on next ResolveApiKey call:)            │
│    _reElicitCount[envVarName]++ < 2?        │
│       → re-elicit                           │
│       → fail with "invalid key" message     │
└─────────────────────────────────────────────┘
```

Note: static sources (CLI arg, env var) are skipped after an invalidation in the same session. The initial 401 proved them to be bad, and re-reading the same env var would return the same bad value.

## Consequences

**Upsides:**
- Works in every host environment (local, remote, Docker, CI, Claude Desktop, AssistStudio).
- Servers stay pure .NET, free of OS-specific secret-store dependencies.
- Non-breaking for users who already set env vars.
- Self-recovery on key invalidation (an improvement over the previous PasswordVault model).

**Tradeoffs:**
- Clients that do not yet support Elicitation require manual env var configuration (which is most MCP clients today).
- Outbox's multi-channel configuration may feel heavier through Elicitation alone — per-channel-type schema design needs care.
- Cached keys are lost on server restart — the host must continue to provide env vars across restarts.

## References

- [MCP Elicitation Spec (2025-06-18)](https://modelcontextprotocol.io/specification/2025-06-18/server/elicitation)
- [12-Factor App: Config](https://12factor.net/config)
- AssistStudio `ToolElicitationPanel` implementation (FieldCure.AssistStudio.Controls.WinUI)
- `BuiltInServerHelper.cs:821-830` — `InjectEssentialsApiKeys` (env var bridge pattern)
