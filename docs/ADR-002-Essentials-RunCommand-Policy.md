# ADR-002: RunCommandTool Cross-Platform & Observable Behavior

**Status:** Accepted
**Date:** 2026-04-27
**Deciders:** FieldCure team
**Applies to:** `FieldCure.Mcp.Essentials` — `Tools/RunCommandTool.cs` (`run_command`)
**Related:** [ADR-001](./ADR-001-MCP-Credential-Management.md) — same design philosophy (visible defaults, fail-fast on misconfiguration)

---

## Context

`Mcp.Essentials.RunCommandTool` (current implementation at
`src/FieldCure.Mcp.Essentials/Tools/RunCommandTool.cs:44-48`) executes shell
commands using a hardcoded shell selected purely by OS detection:

```csharp
var isWindows = OperatingSystem.IsWindows();
FileName  = isWindows ? "cmd.exe" : "/bin/sh";
Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";
```

Output handling silently truncates stdout/stderr at a hardcoded ~100 KB per
stream (`MaxOutputBytes = 102_400` at line 117) without surfacing a marker
or signal to the caller. This contrasts with `HttpRequestTool` which exposes
`max_response_chars` plus an explicit `Truncated` flag and `[Truncated: ...]`
marker — a pattern previously verified to reduce token usage by ~40% on
JSON API workloads while preserving caller awareness.

Two distinct problems result:

1. **Shell selection mismatch with LLM behavior.** Modern LLMs (Claude,
   GPT family) generate PowerShell-native syntax preferentially on Windows
   (`Get-ChildItem`, `Select-String`, object pipelines, `Set-Content
   -Encoding utf8`). Forcing these through `cmd.exe` causes:
   - Outright failures for cmdlet-only commands
   - Subtle quoting breakage (`cmd /c` strips quotes through multiple
     layers, sometimes silently)
   - Token waste from LLMs translating their natural output to `cmd`
     equivalents (`dir /b`, `findstr`)

2. **Silent output truncation.** Normal commands like `git log --since="1
   year"`, `pytest -v`, or `npm install` routinely exceed 100 KB. Current
   behavior cuts the buffer mid-stream with no marker, leaving the model
   unaware that output was truncated. The model may then issue confident
   conclusions based on partial data, or repeat the command without
   adjustment because it has no signal that truncation occurred.

The Unix path (`/bin/sh`) is intentionally a POSIX baseline — it covers
Alpine, musl-based containers, and minimal images consistently. This
decision retains that property and focuses on Windows.

## Decision

### 1. Shell selection — explicit `shell` parameter

Add a `shell` parameter to `RunCommandTool` with the following values:

| Value | Behavior |
|---|---|
| `"auto"` (default) | Windows: `cmd.exe /c` &nbsp;·&nbsp; Unix: `/bin/sh -c` |
| `"pwsh"` | `pwsh -NoProfile -NonInteractive -EncodedCommand <base64>` (PowerShell Core; cross-platform if installed) |
| `"powershell"` | `powershell.exe -NoProfile -NonInteractive -EncodedCommand <base64>` (Windows PowerShell; Windows only) |
| `"cmd"` | `cmd.exe /c <command>` (Windows only — fail-fast on Unix) |
| `"bash"` | `/bin/bash -c <command>` on Unix, first `bash` on PATH on Windows (fail-fast if absent) |
| `"sh"` | `/bin/sh -c <command>` (POSIX baseline; fail-fast on Windows unless a `sh` is on PATH) |

**Default policy rationale**: `"auto"` retains current behavior on both
platforms. PowerShell is reachable only via explicit `shell: "pwsh"`. This
preserves backward compatibility for existing callers (no behavior change
for `shell` omitted) while enabling PowerShell-native command generation
when the caller — typically guided by tool description — requests it.

**Fail-fast on missing shell**: If `shell` is set to a specific value
(`"pwsh"`, `"powershell"`, `"bash"`, etc.) and the binary is not available
on PATH, the tool returns a clear error rather than falling back. Silent
fallback would recreate the very class of bug this ADR exists to fix.

`"powershell"` is included separately from `"pwsh"` because many Windows
developer machines still have Windows PowerShell available even when
PowerShell Core is not installed. It is not part of `"auto"` because changing
the default would still break cmd-style callers; it is an explicit opt-in
compatibility escape hatch for PowerShell-native commands on Windows hosts
without `pwsh`.

**Tool description as steering signal**: The parameter description is
prescriptive about when to choose `"pwsh"` over `"auto"`. This applies
the same prescriptive-description pattern verified effective on
`HttpRequestTool`'s `max_response_chars`:

```
"shell": "Shell to execute the command in. Options:
  - 'auto' (default): cmd.exe on Windows, /bin/sh on Unix.
    Backward-compatible. Use this for simple cross-platform commands.
  - 'pwsh': PowerShell Core. Recommended on Windows for cmdlets
    (Get-ChildItem, Select-String, Set-Content -Encoding utf8) and
    object pipelines. Cross-platform if pwsh is installed.
  - 'powershell': Windows PowerShell. Use on Windows when pwsh is not
    installed and PowerShell syntax is needed.
  - 'cmd', 'bash', 'sh': Explicit shell. Fails if unavailable.
The 'auto' default uses cmd on Windows, which does not support cmdlets,
object pipelines, or modern quoting. Prefer 'pwsh' when generating
PowerShell-native commands; use 'powershell' as a Windows-only fallback
when pwsh is unavailable."
```

### 2. Output truncation — observable, configurable

Adopt the `HttpRequestTool` pattern verbatim:

- New parameter `max_output_chars` (nullable int, default 100_000)
- stdout and stderr truncated independently
- Response includes `stdout_truncated: bool` and `stderr_truncated: bool`
- Truncation marker: `[Truncated: N more chars omitted. Use a smaller
  max_output_chars or narrow the command.]`
- Output readers must continue draining stdout/stderr after the visible
  capture limit is reached. The tool should discard additional characters
  while counting them for the truncation marker, not stop reading the pipe.
  Otherwise a verbose child process can block on a full stdout/stderr pipe
  and appear to time out even though the command itself would have exited.

**Default value intentionally preserves the same order of magnitude while
making the unit explicit**: the current implementation uses a 102,400 byte
constant but converts it to a conservative character limit internally
(`maxBytes / 2`, approximately 51,200 chars). The new default is 100,000
chars: still roughly the existing 100 KB response budget for ASCII-heavy CLI
output, but no longer hidden behind a byte/char mismatch. Callers experience
no behavioral break unless they depend on silent clipping at the old lower
effective limit.

**Independent stream truncation**: Commands like `npm install` write
progress to stderr and results to stdout. A single shared buffer would let
verbose stderr starve stdout of meaningful content. Independent limits
prevent this asymmetry.

### 3. Response shape additions

Existing response fields retained. New fields:

```json
{
  "stdout": "...",
  "stderr": "...",
  "exit_code": 0,
  "stdout_truncated": false,   // NEW
  "stderr_truncated": false,   // NEW
  "shell_used": "cmd"          // NEW: actual shell that ran the command
}
```

`shell_used` is informational. Even with `shell: "auto"`, the response
makes the resolved shell explicit (`"cmd"` or `"sh"`, never the literal
`"auto"`) so the caller (LLM or human) can correlate command syntax with
execution context.

### 4. Preserved behavior (out of scope)

The following existing parameters and semantics carry over **unchanged**.
This ADR does not modify them and they remain part of the tool's contract:

- `working_directory` parameter
- `environment` parameter (JSON object of env-var overrides)
- `timeout_seconds` parameter (1–300 s, default 30)
- `Destructive = true` MCP annotation (and the host-side confirmation flow
  it triggers) — adding a `shell` parameter does not change the tool's
  destructiveness classification
- Process-tree kill on timeout
- UTF-8 stdout/stderr encoding

## Consequences

### Positive

- **PowerShell-native command support** — LLMs that generate `Get-ChildItem`
  or `Select-String` can request `shell: "pwsh"` and have those commands
  execute as written. Eliminates a class of silent semantic failures.
- **Truncation observability** — Models receive an explicit signal when
  output is incomplete and can react (narrow the command, increase
  `max_output_chars`, redirect to file).
- **Token efficiency** — Same prescriptive-description pattern that
  delivered ~40% token savings on `HttpRequestTool` applies here.
- **Backward compatibility preserved** — Default `shell: "auto"` keeps
  existing callers unchanged. New fields are additive.
- **Fail-fast on missing shells** — No silent fallback. If a caller
  requests `pwsh` on a host without it, the error is immediate and clear,
  not a confusing "Get-ChildItem is not recognized" message from cmd.

### Negative / Risks

- **Schema additions visible to all callers** — Even callers who don't use
  the new parameters see the new response fields. Most JSON consumers
  ignore unknown fields; strict-schema consumers may break. This is
  considered acceptable for an MCP tool whose primary consumer is LLMs.
- **`shell: "pwsh"` adoption depends on tool description quality** — LLMs
  must read the description and infer when to choose it. Initial adoption
  may be inconsistent. Tracked via dogfooding observations; if adoption is
  low, ADR-003 may revisit the default (see "Future revisit" below).
- **PowerShell as Windows option introduces an external dependency** —
  Hosts must install pwsh to use `shell: "pwsh"`. Failure mode is clear
  (fail-fast error) rather than silent, but adds setup friction. Mitigated
  by `auto` default not requiring pwsh.
- **`bash` on Windows is ambiguous** — `IsAvailableOnPath("bash")` resolves
  to whichever `bash` lands first on PATH: Git Bash, WSL `bash.exe`, or
  MSYS2 `bash`. These have meaningfully different filesystem semantics
  (path translation, line endings, available utilities). Callers should
  prefer `"pwsh"` or `"cmd"` for predictable Windows behavior; `"bash"` on
  Windows is best-effort. The response's `shell_used` echoes `"bash"` but
  not which flavor.

### Neutral

- `shell_used` field is informational only. Existing consumers ignore it
  by default; new consumers can rely on it for diagnostic logging.

## Alternatives Considered

### Alternative A: Default `shell: "auto"` becomes pwsh on Windows

Rejected. This would change behavior for all existing callers — LLM-
generated `dir /b` and `findstr` commands would fail in the new default,
environment variable syntax (`%PATH%` vs `$env:PATH`) would silently
produce different results, and quoting semantics would shift. Effectively
a major behavior change that does not justify a minor bump. May be
revisited after dogfooding data shows actual `shell: "pwsh"` adoption
patterns and Windows host pwsh availability.

A further structural problem with this alternative: hosts without `pwsh`
installed would either (a) hard-fail, breaking servers that worked
yesterday, or (b) fall back to `cmd` silently, recreating the dev/CI
divergence pattern this ADR exists to prevent. Neither is acceptable.

### Alternative B: `pwsh` as cross-platform default everywhere

Rejected. Adds a non-trivial install dependency on Linux/macOS hosts where
the current `/bin/sh` baseline is universal. Linux server environments
running `Mcp.Essentials` as a dotnet tool typically lack pwsh.

### Alternative C: max_output_chars only, defer shell selection

Considered. The truncation problem is more clearly a bug than the shell
selection issue, and shipping just truncation as a quick fix has appeal.
Rejected because:

- Both changes target the same tool and same release cycle
- Both follow the same design philosophy (HttpRequestTool patterns)
- Splitting incurs two RELEASENOTES entries and two consumer-side updates

The two changes are cohesive enough to justify a single ADR and a single
release.

### Alternative D: Server-startup shell config instead of per-call parameter

Rejected. Per-call shell selection is more flexible — a single LLM session
might run cmd-style queries (existing scripts) and pwsh-style queries
(new generation) interleaved. Forcing a server-wide choice loses this.

## Implementation Notes

### Detection logic

```csharp
private static (string fileName, string arguments) ResolveShell(
    string shellOption,
    string command)
{
    return shellOption switch
    {
        "pwsh" => ResolvePwsh(command),
        "powershell" => ResolveWindowsPowerShell(command),
        "cmd" => ResolveCmd(command),
        "bash" => ResolveBash(command),
        "sh" => ResolveSh(command),
        "auto" or null => ResolveAuto(command),
        _ => throw new ArgumentException(
            $"Unknown shell '{shellOption}'. Valid: auto, pwsh, powershell, cmd, bash, sh."),
    };
}

private static (string, string) ResolveAuto(string command)
{
    // Preserves current v1.x behavior exactly.
    return OperatingSystem.IsWindows()
        ? ("cmd.exe", $"/c {command}")
        : ("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");
}

private static (string, string) ResolvePwsh(string command)
{
    if (!IsAvailableOnPath("pwsh"))
        throw new InvalidOperationException(
            "Requested shell 'pwsh' is not available on this host. " +
            "Install PowerShell Core or use shell: 'auto' / 'cmd'.");

    // Use -EncodedCommand for arbitrary command strings to sidestep
    // PowerShell's quoting rules entirely. -Command "..." with naive
    // \" escaping breaks on backticks, $vars, multiline, here-strings.
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    return ("pwsh", $"-NoProfile -NonInteractive -EncodedCommand {encoded}");
}

private static (string, string) ResolveWindowsPowerShell(string command)
{
    if (!OperatingSystem.IsWindows())
        throw new InvalidOperationException(
            "Requested shell 'powershell' is only available on Windows hosts.");

    if (!IsAvailableOnPath("powershell"))
        throw new InvalidOperationException(
            "Requested shell 'powershell' is not available on this host. " +
            "Install Windows PowerShell or use shell: 'auto' / 'cmd'.");

    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    return ("powershell", $"-NoProfile -NonInteractive -EncodedCommand {encoded}");
}
```

### Quoting hazards

Each shell has subtly different quoting rules. The implementation should
test the following matrix at minimum:

- Single quotes inside command
- Double quotes inside command
- Backslashes in Windows paths (`C:\Users\...`)
- Backtick (pwsh escape character)
- Dollar signs in pwsh (`$variable` vs literal `$`)
- Windows PowerShell fallback (`shell: "powershell"`) on a host where
  `pwsh` is unavailable
- Chaining operators (`&&`, `||`, `;`) — semantics differ:
  - `cmd`: `&&`/`||`/`&`, no `;`
  - `pwsh` 7+: `;`, `&&`, `||` (PS6 and earlier: only `;`)
  - Windows PowerShell 5.1: `;`, but not modern `&&`/`||`
  - `sh`/`bash`: `;`, `&&`, `||`
- Commands that write only to stderr (e.g., `>&2 echo error`) — verify
  `stderr_truncated` independent of `stdout_truncated`
- Commands that emit far more than `max_output_chars` and then exit quickly
  — verify the reader drains the remaining pipe data and does not deadlock
  the child process

A regression test fixture per shell is required.

**Strong recommendation for `pwsh`**: use `-EncodedCommand <base64>` with
UTF-16 LE encoding (PowerShell's required input format) rather than
`-Command "..."` with manual escaping. EncodedCommand sidesteps every
quoting hazard by handing the parser the bytes directly. The same
technique is what `pwsh.exe -File` callers use to embed arbitrary
multiline payloads. `-EncodedCommand` does not bypass `ExecutionPolicy`
restrictions because inline commands are not subject to the script-file
policy gate.

### `IsAvailableOnPath` helper

Resolves the binary using `PATH` lookup. Cache the result per process to
avoid repeated PATH scans on hot paths. Invalidate cache on process
restart only — environment changes mid-process are out of scope.

For `pwsh` specifically, prefer `Process.Start("pwsh", "-NoProfile -Command exit")`
with a short timeout over a raw PATH scan: PATH scans miss App Execution
Aliases on Windows (the modern install method via the Microsoft Store).

## Migration

### For existing v2.x callers

No action required. `shell` omitted continues to behave as v1.x.
`max_output_chars` omitted preserves the 100 KB-scale default while making
truncation visible.

### For LLM tool consumers

Update tool description so LLMs see the new parameters. The prescriptive
description pattern is the primary steering signal — no system-prompt
changes needed for typical use.

### For diagnostic/logging consumers

The new response fields (`shell_used`, `*_truncated`) are additive. JSON
deserializers that ignore unknown fields work without change.

## Future Revisit

**Trigger**: After 2 weeks of dogfooding across the FieldCure ecosystem
(AssistStudio, charting workflows, internal automation), evaluate:

1. What percentage of LLM-generated `RunCommand` calls explicitly request
   `shell: "pwsh"` on Windows hosts?
2. How frequently does `auto` default lead to LLM-generated commands
   failing due to cmd syntax limits?
3. Is the truncation marker actually triggering useful retry behavior in
   LLM responses, or being ignored?

If `shell: "pwsh"` adoption exceeds ~70% of Windows calls and `auto`-with-
cmd commands show frequent silent failures, consider ADR-003 to flip the
Windows default to `pwsh`. This would be a major behavior change requiring
explicit BREAKING notes.

If truncation marker is ignored by LLMs (pattern: model continues with
partial output without acknowledging truncation), refine the marker text
or surface truncation more aggressively — possibly as an error condition
rather than informational flag.

## References

- `HttpRequestTool` `max_response_chars` design — verified token savings
- ADR-001 — establishes the broader credential and configuration policy
  context within which this tool operates
- Original `RunCommandTool.cs:44-48` (shell selection) and 117-138
  (truncation) — code locations addressed by this decision
- PowerShell `-EncodedCommand` documentation —
  https://learn.microsoft.com/powershell/scripting/learn/ps101/03-discovering-objects
  (search "EncodedCommand")
