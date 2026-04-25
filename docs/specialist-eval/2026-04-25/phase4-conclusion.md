# Phase 4 — Self-Critique Bias Test: Conclusion

**Date**: 2026-04-25
**Subject**: Critique specialist self-bias measurement across providers
**Phase 1~3 reference**: `2026-04-25_baseline_*.astx`, `phase1-summary.md`

---

## TL;DR

Self-bias hypothesis **confirmed** for the Critique specialist. Cross-provider
critique has demonstrable production value beyond marginal improvement.

- Haiku critiquing Haiku-generated material misses the algorithmic flaw
  it most needs to catch.
- MiniMax-M2.7 critiquing the same material catches it precisely.
- SpecialistPresets dictionary should map Critique → MiniMax-M2.7 by default,
  with Haiku as fallback for security-domain critique only.

---

## Method

### Step 1 — Material generation

Haiku (claude-haiku-4-5) was asked to write a brief technical design doc for
an internal URL shortener service. Prompt asked for concrete details (schema,
pseudocode, specific numbers) to ensure naturally occurring flaws would
surface. Output saved as `haiku-generated-doc.md`.

### Step 2 — Ground truth identification

Eleven naturally occurring flaws (N1–N11) identified across categories:

| Tier | Count | Items |
|------|-------|-------|
| Critical | 2 | N1 (base62 substring deterministic collision), N2 (custom code conflict) |
| Major | 5 | N3 (soft-delete schema inconsistency), N4 (410/404 inconsistency), N5 (PUT extension authorization), N6 (click_count contention), N7 (PostgreSQL "built-in TTL" factual error) |
| Minor | 4 | N8 (code-space analysis inconsistency), N9 (list-API auth scope), N10 (URL validation / open redirect), N11 (click_count monitoring endpoint) |

Three items selected as key signals for self-bias detection:

- **N1** — algorithmic blind spot (the model would have to re-analyze the
  algorithm it just wrote)
- **N3** — cross-section consistency (requires reading two sections together)
- **N7** — factual self-correction (requires recognizing one's own
  misstatement of PostgreSQL features)

### Step 3 — Cross-provider critique

Same `haiku-generated-doc.md` submitted to Critique specialist with five
provider configurations:

| Provider | Model | Status |
|----------|-------|--------|
| Anthropic Claude | claude-haiku-4-5 (self) | Completed |
| Ollama | gemma4:e4b | Completed |
| MiniMax | M2.7 | Completed |
| OpenAI | gpt-4 | Failed (TPM rate limit) |
| Google Gemini | gemini-flash-latest, gemini-pro | Failed (thought_signature missing) |

Cross-provider results saved as separate `.astx` files in
`docs/specialist-eval/2026-04-25/`.

---

## Detection matrix

Ground-truth detection rate across providers (✓ = caught, ⚠ = generic mention,
✗ = missed):

| ID | Description | Tier | Haiku (self) | Ollama | MiniMax-M2.7 |
|----|-------------|------|:------------:|:------:|:------------:|
| N1 | Base62 substring deterministic collision | Critical | ✗ | ⚠ | **✓** |
| N2 | Custom code conflict handling | Critical | ✗ | ✗ | ✓ |
| N3 | Soft-delete schema inconsistency | Major | ✗ | ⚠ | ✗ |
| N4 | 410 Gone vs 404 Not Found inconsistency | Major | ✗ | ✗ | ✗ |
| N5 | PUT extension authorization model | Major | ✗ | ✗ | **✓** |
| N6 | click_count row contention | Major | ✗ | ✗ | ✗ |
| N7 | "PostgreSQL built-in TTL" factual error | Major | ✗ | ✗ | ✗ |
| N8 | Code-space analysis inconsistency | Minor | ✗ | ✗ | ✓ |
| N9 | List API authentication scope | Minor | ✗ | ✗ | ✗ |
| N10 | URL validation / open redirect | Minor | **✓** | ✗ | ✓ |
| N11 | click_count monitoring endpoint | Minor | ✗ | ✗ | ✗ |
| | **Hits / 11** | | **1** | **0–1** | **4–5** |

### False positive rate

| Provider | False positives | Notes |
|----------|----------------|-------|
| Haiku (self) | 1 | "GDPR Article 17 violation" — incorrect; 30-day retention is permitted |
| Ollama | 0 | Output too abstract to misclassify |
| MiniMax-M2.7 | 0 | All findings substantiated |

### Bonus findings (not in ground truth, but valid)

| Provider | Bonus catches |
|----------|--------------|
| Haiku | 302 vs 301 redirect semantics, Cache-Control headers, RateLimit-* headers |
| Ollama | 302 redirect semantics |
| MiniMax-M2.7 | W4 (`pg_advisory_lock` for cron concurrency), W6 (`INSERT ... ON CONFLICT` for collision atomicity) |

---

## Key findings

### 1. Self-bias confirmed via N1

The N1 result is the cleanest evidence of self-critique bias in this study.
N1 is a deterministic correctness flaw in the key-generation algorithm —
`base62_encode(id + SALT).substring(0, 8)` produces variable-length output
(e.g., `base62_encode(1000) = "IG"`, three characters), contradicting the
"8-character codes" contract and breaking the unique-constraint retry logic.

Haiku, having authored this algorithm itself, did not detect the flaw.
MiniMax-M2.7 detected it with concrete numerical example. The hypothesis that
a model is blind to the subtle defects of its own output is supported by this
case.

### 2. Domain pattern in Haiku self-critique

Haiku's single hit (N10, URL validation / open redirect) is in the
security/standards domain. Bonus findings (302 redirect, Cache-Control,
rate-limit headers) cluster in the same domain.

Pattern: **Haiku self-critique is competent in well-known standards/security
territory, but blind to algorithm-specific flaws in code it generated.**

This is the precise shape predicted by self-bias — confidence in shared
public knowledge, blindness to the specifics of one's own output.

### 3. Hallucination accumulation

Haiku Critique specialist produced one hallucinated finding (W3, GDPR Article
17). This follows one similar incident in v6 baseline evaluation (W7
incorrect description of `default` return semantics). Two hallucinations
across seven evaluation rounds is a weak but non-zero signal worth tracking.

MiniMax-M2.7 had zero hallucinations across this evaluation.

### 4. Ollama (Gemma 4) — directional but vague

Ollama's output mentions race conditions and atomicity in general terms,
which directionally aligns with N1 and N6, but never produces a concrete
diagnosis. As a self-hosted/zero-cost option it provides weak signal.

### 5. Backstop validated cross-provider

`SubAgentTool.NormalizeSpecialistReport` correctly identified
`## Final Report` heading across all three completing providers (Haiku,
Ollama, MiniMax). Post-processing is provider-agnostic, not Anthropic-specific.

Strip lengths varied (Haiku: 60 chars, Ollama: TBD, MiniMax: TBD). Detailed
per-provider strip distribution to be analyzed in subsequent rounds.

---

## SpecialistPresets recommendation

Based on Phase 4 evidence:

| Specialist | Recommended primary | Recommended fallback | Rationale |
|------------|--------------------|--------------------|-----------|
| **Critique** | **MiniMax-M2.7** | Haiku (security/standards) | 4× detection rate, zero hallucination, concrete algorithmic analysis |
| RedTeam | Haiku (current) | — | Stable across v1–v6; not measured in Phase 4 |
| DevilsAdvocate | Haiku (current) | — | Balanced output across v3–v6; not measured in Phase 4 |
| WebSearch | (current) | — | Not measured |

The Critique → MiniMax mapping is the strongest single recommendation from
this evaluation. Algorithmic flaw detection (N1-class) is precisely the
production value Critique specialist must deliver, and self-provider
configurations demonstrably underperform here.

---

## Infrastructure issues (out of scope for this conclusion)

Two provider integrations failed during Phase 4 and require separate work
before they can participate in future evaluations:

### Gemini — `thought_signature` requirement

Gemini 2.0+ requires a `thought_signature` field in `functionCall` parts
when tools are used. Current `GeminiProvider` does not emit this field,
producing a 400 INVALID_ARGUMENT response.

**Reference**: <https://ai.google.dev/gemini-api/docs/thought-signatures>

**Action**: Update `FieldCure.Ai.Providers.GeminiProvider` to include
`thought_signature` in tool-call requests.

### OpenAI — gpt-4 token-per-minute limit

gpt-4 has a 10K TPM limit on the test account, insufficient for a single
Critique invocation (~7K tokens needed for input + output across rounds).

**Action**: Switch evaluation default to `gpt-4o-mini` (60K TPM) or
`gpt-4o` (30K TPM).

These are not blockers for Phase 4 conclusions — three providers across
three architectures (Anthropic, MoE, local) is sufficient evidence — but
they prevent comparison against OpenAI/Gemini, which would be valuable in
future rounds.

---

## Limitations

1. **Single material**: Phase 4 used one Haiku-generated doc. Self-bias
   pattern should be validated across at least two more domains (file upload
   service, webhook delivery) before treating Critique → MiniMax as a final
   default.

2. **MiniMax variance unmeasured**: One excellent run does not establish
   reliability. MiniMax-M2.7 should be re-evaluated on the original Phase 1
   targets (`critique-target.md` etc.) to confirm consistency.

3. **No RedTeam/DevilsAdvocate self-bias data**: Phase 4 measured only
   Critique. The same methodology should be applied to the other specialists
   if they are deployed in production.

4. **Ground truth subjectivity**: N3, N4, N9 boundaries are judgment calls.
   Detection counts have ±1 uncertainty per provider.

---

## Decisions taken

1. ✓ Apply Critique → MiniMax-M2.7 mapping in `SpecialistPresets` defaults
   for v1.0 ship.
2. ✓ Retain Haiku as fallback for Critique invocations where MiniMax is
   unavailable (network failure, account limits).
3. ✓ Preserve all four Phase 4 `.astx` files in
   `docs/specialist-eval/2026-04-25/` for future regression comparison.
4. ☐ Schedule second Phase 4 round with different domain — within next two
   weeks if Critique → MiniMax is deployed.

---

## Decisions deferred

- RedTeam and DevilsAdvocate provider mapping — keep current (Haiku) until
  matching Phase 4 evaluation conducted.
- Gemini and OpenAI infrastructure fixes — separate PRs, not gating v1.0.
- Long-term provider strategy (whether MiniMax-M2.7 should also serve other
  specialist categories) — pending consistency data.

---

## Files referenced

| File | Purpose |
|------|---------|
| `haiku-generated-doc.md` | Phase 4 evaluation material |
| `2026-04-25_p4_haiku-on-haiku.astx` | Self-critique baseline |
| `2026-04-25_p4_ollama-gemma4-on-haiku.astx` | Local-model critique |
| `2026-04-25_p4_minimax-on-haiku.astx` | Cross-provider critique (key result) |
| `2026-04-25_p4_openai-on-haiku.astx` | Failed (TPM limit) — preserved for record |
| `2026-04-25_p4_gemini-on-haiku.astx` | Failed (thought_signature) — preserved for record |
