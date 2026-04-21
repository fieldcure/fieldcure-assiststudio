# ADR-001: MCP Server Credential Management Strategy

**Status:** Accepted
**Date:** 2026-04-18
**Amended:** 2026-04-19 — Static/dynamic credential distinction, Outbox OAuth phased rollout
**Amended:** 2026-04-20 — Outbox local-trust exception for `channels.json`; Essentials Phase 2 split into auto/explicit cases (Elicitation required only on explicit paid-engine intent, implementation deferred to v2.2); Outbox Phase 3c reframed — OAuth host constraint is "local browser + localhost callback", not Windows. v2.1 folds CLI-equivalent flow into MCP `add_channel`; v2.2 adds device-code flow for headless
**Applies to:** All FieldCure MCP servers (Essentials, Outbox, PublicData.Kr, RAG, future servers)

---

## Context

FieldCure MCP servers require external API keys and credentials. Prior implementations diverged across servers:

| Server | Previous approach | Issue |
|--------|----------|--------|
| Essentials | `ResolveArg` (CLI → env var → engine-specific env var) + host-side PasswordVault → env var bridge | Server itself is env-var based; fine, but client-dependent |
| Outbox | `add_channel` → re-spawn self → `UseShellExecute` console for credential entry | Does not work on remote or headless hosts — "half-server" |
| PublicData.Kr | env var only (`PUBLICDATA_API_KEY`) | Hard-fails at startup when unset |
| RAG | `CredentialService` with `advapi32.dll` P/Invoke | Windows-only; `DllNotFoundException` on Linux/macOS |

On 2026-04-17 the Windows CredentialManager (DPAPI) dependency was removed from Essentials and Outbox. This established a principle that servers do not manage OS-specific secret stores. A general credential strategy is now needed so FieldCure MCP servers can be published to — and function inside — any MCP host (local, remote, Docker, CI).

## Decision

### Priority chain

```
env var (primary)
  └─▶ MCP Elicitation (interactive fallback)
        └─▶ CLI arg (manual test / debug only — not an official path)
```

This chain is consistent with 12-factor app principles and is non-breaking for users who already set env vars.

### Credential classification

Credentials fall into two categories with different persistence rules:

| Classification | Examples | Persistence | Server behavior |
|---|---|---|---|
| **Static secret** | API key, client secret, webhook URL, SMTP app password, bot token | Host responsibility (env var, credential store) | In-memory cache only |
| **Dynamic credential** | OAuth access token, refresh token | Server responsibility (`tokens.json` + file permissions) | Refresh cycle management, persist after renewal |

Static secrets are issued once and do not change until the user explicitly rotates them.
Dynamic credentials are renewed by the server during protocol execution; after a refresh
the new token set must be persisted because the old one is invalidated.

Both classifications can coexist within a single channel:

```
Microsoft channel:
  client_id / client_secret   → static  → env var (host responsibility)
  access_token / refresh_token → dynamic → tokens.json (server responsibility)
```

### Core principles

1. **Servers are stateless with respect to static secrets.** A key obtained via Elicitation is cached in process memory (session lifetime) and never written to disk by the server. Dynamic credentials (OAuth tokens) are the exception — the server must persist them to survive refresh cycles.

2. **Persistence is split.** Persistence of **static secrets** (API keys, tokens, passwords) is a host responsibility. A host such as AssistStudio stores them in its own credential store (e.g. Windows PasswordVault) and injects them as env vars when launching the server. Servers do not own OS-specific secret stores. **Non-secret configuration** (channel list, types, endpoint URLs, etc.) may be persisted by the server (e.g. `channels.json`). **Dynamic credentials** (OAuth tokens) are persisted by the server in `tokens.json` with file-permission protection:
   - Windows: current-user ACL only
   - Linux/macOS: `chmod 0600`
   - Security boundary: does not defend against same-user compromise (same level as GitHub CLI, Docker CLI, AWS CLI)
   - Optional secure backend (e.g. ASP.NET Core DataProtection) may be added later if needed

   **Local-trust exception (Outbox CLI `add_channel`).** The interactive CLI setup
   flow for static-secret channels (Slack bot token, Discord webhook URL, SMTP
   password, Telegram API hash) writes the secret directly into `channels.json`
   alongside the non-secret config. `OutboxSecretResolver` reads this as the last
   fallback in the resolution chain (`cache → env var → channels.json`). This is a
   deliberate concession to single-user-machine usability: it matches the same-user
   security boundary already documented for `tokens.json`, and is consistent with
   the disk-plaintext convention of `~/.docker/config.json`, `~/.config/gh/hosts.yml`,
   and similar local CLI tools. Production, shared-host, and headless deployments
   should set env vars (or use an Elicitation-capable client) exclusively, leaving
   the secret fields in `channels.json` empty — the resolver will then pick the key
   up from the environment and never touch the file.

3. **Lazy elicitation.** Servers do not elicit at startup. They elicit on the first tool call that requires the key. `tools/list` must always succeed without a key. **Batch/headless modes** (e.g. RAG exec, exec-queue) where no MCP client is present must not attempt Elicitation — they use env var only and soft-fail if absent.

4. **Soft fail.** If no env var is set and elicitation fails (client does not support it, user declines, or the retry cap is exhausted), the server stays up. Only the affected tool returns a structured error:
   ```
   { "error": "API key not configured. Set SERPER_API_KEY environment variable." }
   ```
   `list_tools` continues to respond.

5. **Self-recovery on key invalidation.** On `401/403` from upstream, the server invalidates the cached key and issues a new elicitation. Session-level cap: **2 re-elicits** (the initial elicit is not counted) — this bounds the loop at up to three total elicitation attempts per session. The constant in code (`MaxReElicits = 2`) reflects this semantic.

6. **Env var naming follows service convention.** Use the per-service canonical name (`SERPER_API_KEY`, `TAVILY_API_KEY`, `DATA_GO_KR_API_KEY`). Do not prefix with `FIELDCURE_*`; users who already set the canonical name via the service's own docs would otherwise have to configure it twice.

### Persistence summary by component

| Component | Credential source |
|---|---|
| AssistStudio | PasswordVault (interactive GUI, Windows-only app — appropriate) |
| AssistStudio → MCP spawn | Reads PasswordVault, injects via `ProcessStartInfo.EnvironmentVariables` |
| Mcp.Rag | env var → Elicitation fallback (static secrets only) |
| Mcp.Outbox (static channels) | env var → Elicitation fallback → `channels.json` (local-trust plaintext fallback, see Principle 2) + non-secret config |
| Mcp.Outbox (OAuth channels) | static part via env var + dynamic part via `tokens.json` (file-permission protected) |

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

**Phase 1 — PublicData.Kr (pilot)** ✅ Complete (v1.0.0)

- Single key, simplest case. Validated the common pattern.
- Replaced startup hard-fail with soft-fail.
- Renamed env var `PUBLICDATA_API_KEY` → `DATA_GO_KR_API_KEY` (non-breaking at `v0.x`; aligns with the external Korean government API naming convention). The legacy name remains accepted.

**Phase 2 — Essentials** 🚧 Partially Complete (amended 2026-04-20)

Essentials resolves a **search engine** at startup, then each tool call uses it. The
credential need is therefore conditional on *which* engine the user picked. Two
cases must be distinguished:

*Case A — Engine unspecified (auto-detect mode).*
The user did not pass `--search-engine` and did not set `ESSENTIALS_SEARCH_ENGINE`.
The server scans the environment for any paid-engine key (`SERPER_API_KEY`,
`SERPAPI_API_KEY`, `TAVILY_API_KEY`). If one is present, that engine is used.
Otherwise the `FallbackSearchEngine` (Bing → DuckDuckGo) is used. **No
Elicitation.** Rationale: with no explicit intent, free fallback is the lowest-
friction default and matches the user's implicit "just work" expectation.

*Case B — Explicit engine selected.*
The user specified a paid engine (e.g. `--search-engine serper`). Key resolution:
`--search-api-key` CLI → `ESSENTIALS_SEARCH_API_KEY` → engine-specific env var →
**Elicitation** (first tool call) → **fallback-consent Elicitation** (if the user
declines the key) → soft fail. Rationale: explicit engine selection is a paid-
intent signal; silently swapping to a free fallback without the user's consent
would misrepresent the result. Elicitation is skipped if the client does not
advertise the `elicitation` capability — in that case the server behaves as in
Case A (falls back to free search) and logs a stderr warning.

| Case | Resolution order | Elicitation |
|---|---|---|
| A (auto) | env scan → free fallback | ❌ |
| B (explicit) | CLI → env → engine env → Elicit → fallback-consent Elicit → soft fail | ✅ |

**Status:** Case A is implemented. Case B currently silently falls back without
eliciting (see `Program.cs:161-169` — `LogAndFallback`). Elicitation implementation
is tracked at `fieldcure-mcp-essentials/todo/5. elicitation-for-explicit-engine.md`
and will ship as v2.2 (minor, non-breaking — hosts without Elicitation support
continue to see the existing free-fallback behaviour).

**Shared-package extraction trigger:** once Essentials v2.2 ships with its own
`ApiKeyResolverRegistry` (ported from Rag), two production uses will exist. At
that point, extract `FieldCure.Mcp.Common.Credentials`. Use it twice before
abstracting — a single use does not reveal the right abstraction.

**Phase 3a — RAG** (credential cross-platform)

- Removes `CredentialService.cs` (advapi32.dll P/Invoke) entirely.
- Removes `#pragma warning disable CA1416`.
- Credential resolution depends on execution mode:

  | Mode | Context | Credential chain |
  |---|---|---|
  | `serve` | Interactive MCP server (stdio), client present | env var → Elicitation fallback |
  | `exec` / `exec-queue` | Headless batch, no MCP client | env var only → soft-fail |

  Rationale: exec/exec-queue are non-interactive batch processes launched by Runner via schtasks.
  There is no MCP client to receive an Elicitation request. Attempting to elicit would block indefinitely.
  These modes require the embedding API key to be provided via env var; if absent, the operation
  soft-fails with a clear message naming the expected env var.

**Phase 3b — Outbox, static channels** (Slack, Telegram, Discord, Gmail)

- Removes `CredentialManagerStore.cs` (advapi32.dll P/Invoke).
- Removes `add_channel` subprocess-console pattern.
- Removes `#pragma warning disable CA1416`.
- Static channels use Elicitation-based `add_channel` with per-channel-type schemas:
  ```
  Slack:    workspace_url, bot_token (password), channel_id
  Telegram: bot_token (password), chat_id
  Discord:  webhook_url (password)
  Gmail:    sender_email, app_password (password)
  ```
- Secret vs non-secret split: secrets → env var (host responsibility), non-secrets → `channels.json`.

**Phase 3c — Outbox, OAuth channels** (Microsoft, KakaoTalk)

The real constraint for OAuth authorization-code flows is **not the operating system** but whether the MCP server host can (a) launch a local default browser and (b) receive an `http://localhost:9876/callback` redirect from that browser. Both CLI-based and MCP-tool-internal implementations share this constraint; earlier drafts of this ADR misattributed it to Windows.

Supported host environments (identical for v2.0 CLI path and v2.1 MCP path):

| Host | Status | Reason |
|---|:---:|---|
| Windows desktop | ✅ | `ShellExecute` opens default browser |
| macOS desktop | ✅ | `.NET` falls back to `open` |
| Desktop Linux (GNOME/KDE with xdg-open) | ✅ | `.NET` falls back to `xdg-open` |
| Headless Linux / Docker / CI runner | ❌ | No browser on host |
| Remote MCP server (SSH tunnel / networked) | ❌ | `localhost:9876` on server is not reachable from user's browser |

**v2.0 — CLI retained as fallback path.** Sub-process `fieldcure-mcp-outbox add {kakaotalk|microsoft}` runs the interactive masked-input setup, opens the browser, and catches the callback. The MCP `add_channel` tool returns a soft error pointing users at the CLI command.

**v2.1 — OAuth folds into `add_channel` MCP tool.** The same browser + `HttpListener` pattern runs *inside* the MCP tool call, racing the listener callback against a second Elicitation prompt so the user can confirm progress or cancel. Host-session requirement is identical to v2.0; the difference is that the user stays inside the MCP host UI (Claude Desktop, Claude Code, AssistStudio, etc.) with no terminal context switch. CLI remains as a developer-diagnostic path. Tracked in `fieldcure-mcp-outbox/todo/3. oauth-channels-via-mcp-elicitation.md`.

**v2.2 — Device-code flow (RFC 8628) removes the host-browser constraint.** Device-code replaces `redirect_uri` with an out-of-band user-to-browser handoff (server returns a URL + user code; user authorizes on any device; server polls the token endpoint). This unlocks headless / Docker / remote / CI deployments.
- Prerequisite investigation: KakaoTalk device-code support. Azure AD (Microsoft) is confirmed.
- Static part (client_id / client_secret) → env var (host responsibility).
- Dynamic part (access/refresh tokens) → `tokens.json` (server responsibility, unchanged).
- Token refresh failure → automatic re-authentication trigger.

### Version impact

| Server | Current | After ADR | Compatibility |
|--------|------|------------|--------|
| PublicData.Kr | 0.x → 1.0.0 | ✅ Done | New — non-breaking |
| Essentials | 2.0.x → 2.1.0 | ✅ Done (cross-platform, PasswordVault removal) | Non-breaking |
| Essentials | 2.1.x → 2.2.0 | 🚧 In design | Non-breaking — Elicitation on explicit paid-engine intent; hosts without Elicitation fall back silently as today |
| RAG | 1.x | 2.0.0 (major) | CredentialService removal is breaking |
| Outbox (static) | 1.x | 2.0.0 (major) | Subprocess removal is breaking |
| Outbox (OAuth) | — | 2.1.0 | MCP-internal OAuth (browser + listener + elicit race); host-session requirement unchanged from v2.0 |
| Outbox (OAuth headless) | — | 2.2.0 | Device-code flow (RFC 8628) — removes host-browser requirement |

### Success criteria

**Phase 1:** ✅ All passed
1. PublicData answers `tools/list` with no env var configured.
2. After elicitation, `call_api` succeeds.
3. A bad key yields 401 → re-elicit → success (verified against AssistStudio).

**Phase 2:**
1. ✅ Auto-detect mode: Essentials picks up paid engines from env vars when present; falls back to Bing/DuckDuckGo otherwise.
2. 🚧 Explicit-engine mode: Elicits the engine-specific key when none is supplied via CLI or env var; on decline, asks for fallback consent; soft-fails otherwise. **Not yet implemented — tracked in `fieldcure-mcp-essentials/todo/5. elicitation-for-explicit-engine.md`.**
3. 🚧 Switching engines elicits the new engine's key as needed (requires 2 above).

**Phase 3a:**
1. RAG starts on Linux/macOS without `DllNotFoundException`.
2. `serve` mode: embedding API key elicitation works on first `index_document` or `search` call.
3. `exec` / `exec-queue` mode: missing env var produces clear soft-fail message, no hang.

**Phase 3b:**
1. Outbox `add_channel` (Slack/Telegram/Discord/Gmail) works end-to-end via Elicitation.
2. Works in external hosts such as Claude Desktop (no subprocess/console dependency).

**Phase 3c (v2.1 — MCP-internal OAuth):**
1. `add_channel(type="kakaotalk"|"microsoft")` completes entirely inside the MCP tool on hosts with a local browser — no CLI terminal required.
2. Browser + `HttpListener` + second Elicitation race correctly handles completion, cancel, and timeout.
3. Token refresh persists correctly in `tokens.json`.
4. CLI path continues to work as a diagnostic fallback.

**Phase 3c (v2.2 — device-code flow):**
1. Microsoft/KakaoTalk OAuth via device-code flow works on headless / Docker / remote MCP servers.
2. Re-authentication triggers automatically on refresh-token expiry.

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

Note: this cache design applies to **static secrets** only. Dynamic credentials (OAuth tokens) follow a separate lifecycle managed by the OAuth flow implementation, not the `ResolveApiKey` path.

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
- Outbox OAuth channels (Microsoft, KakaoTalk) are constrained to MCP server hosts with a local desktop browser in v2.0 and v2.1 alike; the difference is whether the user drives the flow from CLI (v2.0) or from inside the MCP host UI (v2.1). Full cross-platform (headless / remote / container) arrives with v2.2 device-code flow.
- Dynamic credential storage (`tokens.json`) protects only at the file-permission level (same-user boundary). This matches the security posture of GitHub CLI, Docker CLI, and AWS CLI, and is appropriate for MCP stdio servers' threat model.

## Phase 1.1 backlog (record only — none urgent)

- [ ] ODCloud (`api.odcloud.kr`) HTTP 400 invalid-key detection
- [ ] HTTP 401/403 body analysis to distinguish invalid-key vs unsubscribed-API
- [ ] Elicit `api_key` field masking (propose MCP spec `format: "password"`)
- [ ] SubAgentTool `ParseStringArray` object array fix

## Phase 3c follow-up work

### v2.1 (MCP-internal OAuth) — tracked in [`fieldcure-mcp-outbox/todo/3. oauth-channels-via-mcp-elicitation.md`](../../fieldcure-mcp-outbox/todo/3.%20oauth-channels-via-mcp-elicitation.md)
- [ ] Extract `BrowserOAuthFlow` helper (listener + `Process.Start` + elicit race)
- [ ] Replace CLI-dispatch branches in `AddChannelTool.cs` with MCP-internal Kakao / Microsoft flows
- [ ] Keep CLI path as developer-diagnostic fallback
- [ ] README / RELEASENOTES update: end-user path is MCP `add_channel`; CLI is diagnostic

### v2.2 (device-code flow)
- [x] Microsoft: Device Authorization Grant (RFC 8628) Azure AD support — confirmed
- [ ] KakaoTalk: device-code flow support — unconfirmed, alternative needed if unsupported
- [x] `tokens.json` file-permission utility (Windows ACL / Unix 0600) — shipped in v2.0 `OAuthTokenStore`
- [ ] Token refresh failure → re-authentication auto-trigger UX design

## References

- [MCP Elicitation Spec (2025-06-18)](https://modelcontextprotocol.io/specification/2025-06-18/server/elicitation)
- [12-Factor App: Config](https://12factor.net/config)
- AssistStudio `ToolElicitationPanel` implementation (FieldCure.AssistStudio.Controls.WinUI)
- `BuiltInServerHelper.cs:821-830` — `InjectEssentialsApiKeys` (env var bridge pattern)
