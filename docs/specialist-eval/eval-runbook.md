# Specialist Evaluation Runbook

## Goal

Verify that `critique`, `red_team`, and `devils_advocate` specialists produce
correct, well-formatted output with predictable token cost. Compare across
providers only after baseline behavior is confirmed.

## Setup — `eval-cheap` preset (one-time)

Create a provider preset:

- Provider: Claude Haiku (or Gemini 2.0 Flash, or Ollama local for smoke test)
- AllowedTools override: **remove `web_search` and `web_fetch`**
  - Test materials are self-contained — external lookups would only burn tokens
- MaxRounds: 8
- Timeout: 3 min

In Task Settings → Specialists, point all three specialists at `eval-cheap`.
This isolates evaluation traffic from your normal workflow.

## Phase 1 — Baseline (1 .astx, 3 specialist calls)

Open a new conversation. Parent provider can be anything (cheap is fine here too).

Send three prompts in sequence:

```
1. "Critique the following README and code:"
   [paste contents of critique-target.md]

2. "Red team the following design plan:"
   [paste contents of redteam-target.md]

3. "Play devil's advocate on this proposition:"
   [paste contents of debate-proposition.md]
```

Save as: `2026-04-25__baseline__eval-cheap.astx`

Estimated total cost: ~30K tokens across all three specialists.

## Phase 2 — Score against ground truth

Open `ground-truth.md`. Fill the scoring sheet below.

**Stop here if results are bad.** Fix the specialist prompt or the test material
before spending money on provider comparison. Bad detection on Haiku will be bad
on every provider — you have a prompt/material problem, not a model problem.

## Phase 3 — Provider comparison (only if Phase 2 passes)

Repeat Phase 1 with two additional provider presets:

- `2026-04-25__baseline__openai-mini.astx` (GPT-4o-mini or similar)
- `2026-04-25__baseline__gemini-flash.astx` (Gemini 2.0 Flash)

Phase 3 cost: ~30–60K tokens per provider × 2 = ~120K total.

## Phase 4 — Self-critique bias test (optional but high-value)

The cleanest experiment for "do we need different providers":

1. Ask Provider X to write a short design doc on any topic.
2. Run `critique` on that doc with Provider X.
3. Run `critique` on the same doc with Provider Y.
4. Compare: does Y find more or different issues than X?

Save as: `2026-04-25__bias-test__X-vs-Y.astx`

## Scoring sheet

| Specialist | Provider | Hits | False positives | Severity calibration | Format OK | Tool calls | Tokens |
|------------|----------|------|-----------------|----------------------|-----------|------------|--------|
| critique   |          | / 5  |                 |                      |           |            |        |
| red_team   |          | / 6  |                 |                      |           |            |        |
| devils_adv |          | F:/5 A:/5 |            | (balance OK?)        |           |            |        |

- **Hits**: items from `ground-truth.md` correctly identified
- **False positives**: invented issues not in ground truth (hallucination signal)
- **Severity calibration**: did Critical/Major/Minor labels match expected severity?
- **Format OK**: Final Report leads? SCAN/ATTACK/REMEDY (or equivalent) structure?
- **Tool calls**: should be 0 since `eval-cheap` removes web tools — non-zero means RAG/file tools, fine
- **Tokens**: from .astx metadata or provider dashboard

## Naming convention

```
<date>__<phase>__<provider>.astx
```

Examples:

- `2026-04-25__baseline__eval-cheap.astx`
- `2026-04-25__baseline__gemini-flash.astx`
- `2026-04-25__bias-test__claude-vs-openai.astx`

Keep all files for one eval session in a single dated folder under `docs/`.

## What to look for during scoring

**Critique** — beyond hit count, check whether REMEDY items are concrete.
"Consider improving thread safety" = vague (fail). "Replace `Dictionary<,>` with
`ConcurrentDictionary<,>` or wrap reads/writes in a lock" = concrete (pass).

**Red Team** — check for fake attacks. If the threat report lists vulnerabilities
that aren't actually findable in the source material, that's hallucination.
The "no fixes" rule should hold — if it suggests fixes, the system prompt isn't
landing.

**Devil's Advocate** — the key signal is **balance**. Count evidence pieces per
side. If FOR has 7 and AGAINST has 2, the specialist is leaning. Also check that
no recommendation appears — if it concludes "the proposition is correct/wrong",
that's a violation.
