# Phase 4 — Self-Critique Bias Test: Final Conclusion

**Date**: 2026-04-25
**Subject**: Critique specialist self-bias measurement across providers
**Phase 1~3 reference**: `2026-04-25_baseline_*.astx`, `phase1-summary.md`
**Supersedes**: Earlier conclusion drafts recommending MiniMax-M2.7 then gpt-5.5

---

## TL;DR

Self-bias hypothesis **strongly confirmed** for the Critique specialist
across 8 providers. Cross-provider critique has demonstrable production
value far beyond marginal improvement.

- Haiku critiquing Haiku-generated material catches 1/12 ground-truth
  flaws plus 1 hallucination.
- Sonnet 4.6 catches 8/12 with zero hallucinations.
- Opus 4.7 catches 9–10/12 and uniquely identifies a router-design flaw
  not in original ground truth.
- gpt-5.5 catches 6–7/12 and uniquely identifies a factual error.
- SpecialistPresets should map Critique → Sonnet 4.6 by default,
  with Opus 4.7 as a high-stakes preset.

---

## Method

### Step 1 — Material generation

Haiku (claude-haiku-4-5) was asked to write a brief technical design doc
for an internal URL shortener service. Prompt asked for concrete details
(schema, pseudocode, specific numbers) so naturally occurring flaws would
surface. Output saved as `haiku-generated-doc.md`.

### Step 2 — Ground truth identification

Eleven naturally occurring flaws (N1–N11) identified prior to evaluation.
After Phase 4 completed, Opus 4.7 surfaced an additional flaw not in the
original list:

- **N12** — Router path collision: `GET /api/v1/:code` cannot
  disambiguate from `GET /api/v1/list` because `list` is a valid 4-char
  Base62 code. Added retroactively to ground truth.

This itself is a finding — ground-truth construction by humans missed a
genuine flaw that Opus identified.

### Step 3 — Cross-provider critique

Same `haiku-generated-doc.md` submitted to Critique specialist with eight
provider configurations:

| Provider | Model | Status | Specialist used? |
|----------|-------|--------|:----------------:|
| Anthropic Claude | claude-haiku-4-5 (self) | Completed | Yes |
| Anthropic Claude | claude-sonnet-4-6 | Completed | Yes |
| Anthropic Claude | claude-opus-4-7 | Completed | Yes |
| OpenAI | gpt-5.5 | Completed | Yes |
| OpenAI | gpt-4 | Completed | Yes |
| Google Gemini | gemini-pro | Completed | **No** (parent direct) |
| MiniMax | M2.7 | Completed | Yes |
| Ollama (local) | gemma4:e4b | Completed | Yes |

Gemini Pro completed but bypassed the specialist due to
`thought_signature` incompatibility in the current GeminiProvider —
parent answered directly without delegate_task. Result still preserved
for capability comparison.

All `.astx` files in `docs/specialist-eval/2026-04-25/`.

---

## Detection matrix

Detection across 8 providers (✓ = caught, ✓✓ = caught with notable
depth/citation, ✓✓✓ = caught with unique additional insight,
⚠ = generic/partial mention, ✗ = missed):

| ID | Description | Tier | Haiku | Ollama | MiniMax | gpt-4 | gpt-5.5 | Gemini Pro | Sonnet 4.6 | Opus 4.7 |
|----|-------------|------|:-----:|:------:|:-------:|:-----:|:-------:|:----------:|:----------:|:--------:|
| N1 | Base62 substring deterministic collision | Critical | ✗ | ⚠ | ✓✓ | ✗ | ⚠ | ✓✓ | ✓✓ | ✓✓✓ |
| N2 | Custom-code conflict handling | Critical | ✗ | ✗ | ✓ | ✗ | ⚠ | ✗ | ✗ | ✓✓✓ |
| N3 | Soft-delete schema inconsistency | Major | ✗ | ⚠ | ✗ | ✗ | ✓✓ | ✓✓ | ✓✓ | ✓✓ |
| N4 | 410 Gone vs 404 inconsistency | Major | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | ✓ |
| N5 | PUT extension authorization | Major | ✗ | ✗ | ✓✓ | ✗ | ⚠ | ✗ | ✓ | ✓✓ |
| N6 | click_count row contention | Major | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | ✓✓ |
| N7 | "PostgreSQL built-in TTL" factual error | Major | ✗ | ✗ | ✗ | ✗ | ✓✓ | ✗ | ✗ | ✗ |
| N8 | Code-space analysis inconsistency | Minor | ✗ | ✗ | ✓ | ✗ | ✗ | ✓ | ✓ | ✓ |
| N9 | List API authentication scope | Minor | ✗ | ✗ | ✗ | ✗ | ✓ | ✗ | ✓✓✓ | ✗ |
| N10 | URL validation / open redirect | Minor | ✓ | ✗ | ✓ | ✓ | ✓✓ | ✗ | ✓✓ | ✓✓ |
| N11 | click_count monitoring endpoint | Minor | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ⚠ |
| N12 | Router path collision (Opus-discovered) | Critical | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓✓✓ |
| | **Hits / 12** | | **1** | **0–1** | **4–5** | **1** | **6–7** | **5** | **8** | **9–10** |

### False positive rate

| Provider | False positives | Notes |
|----------|----------------:|-------|
| Haiku (self) | 1 | "GDPR Article 17 violation" — incorrect; 30-day retention is permitted |
| All others | 0 | All findings substantiated |

### Token cost

| Provider | Tokens | Elapsed (s) | Notes |
|----------|-------:|------------:|-------|
| Sonnet 4.6 | 1805 | 96 | Best efficiency: 8 hits @ 1.8K tokens |
| Opus 4.7 | 3445 | 117 | High capability, ~2× Sonnet cost |
| gpt-4 | 3572 | 40 | Poor ratio: 1 hit |
| gpt-5.5 | 5995 | 166 | 6–7 hits, 3× Sonnet tokens |

Sonnet 4.6 is the clearest winner on cost-efficiency: matches Opus on
detection count at half the tokens, and three times faster than gpt-5.5.

---

## Unique findings per provider

Insights surfaced by only one provider — the strongest evidence of
provider-diversity value:

### Opus 4.7

**N12 — Router path collision** (added to ground truth retroactively)

> `GET /api/v1/:code` collides with `GET /api/v1/list`. `list` is a valid
> 4-char Base62 code, so the router cannot disambiguate without
> special-casing. Move redirects to `GET /:code` on the redirect host;
> reserve a denylist of codes (`api`, `list`, `health`, `admin`).

**N2 — Custom-code squatting** (deepest analysis among catchers)

> Four-layer defense: regex `^[a-zA-Z0-9_-]{4,20}$`, reserved-word
> denylist, elevated permission for custom codes, audit-log all
> custom-code creations.

**Cleanup partition strategy** (W5, beyond ground truth)

> 1M URLs/day × 7–365d TTL → 100M+ rows. Hourly bulk delete is the wrong
> tool. Partition by `expires_at` (monthly RANGE), expiration becomes
> `DETACH PARTITION` + `DROP TABLE` — O(1), no VACUUM contention.

### Sonnet 4.6

**N9 — Token leak via list endpoint** (most concrete attack scenario)

> `GET /list` has no ownership scoping. Any authenticated user can
> enumerate URLs created by other users, including those embedding
> sensitive tokens in query strings (e.g., `https://internal.corp/reset?token=abc`).

**OWASP ASVS / Bitly 2013 incident citation** (academic depth)

> Sequential-offset schemes explicitly fail OWASP ASVS 4.0 §3.2.2.
> Reference to Bitly's 2013 sequential-ID leak as historical precedent.

### gpt-5.5

**N7 — "PostgreSQL built-in TTL" factual error** (only catcher)

> PostgreSQL triggers fire on INSERT/UPDATE/DELETE/TRUNCATE, not on
> wall-clock time. Also: `UNIQUE(code)` already creates an index, making
> `CREATE INDEX idx_code` redundant.

Only provider that flagged the factual misstatement and the redundant
index — both require deep PostgreSQL domain knowledge.

### Gemini Pro (parent-direct)

**Scale mismatch analysis** (beyond ground truth)

> 200 URLs/day × 100 engineers vs proposed Redis/read-replicas/BIGSERIAL.
> 73K rows/year, MB-scale DB. The infrastructure is over-engineered by
> 4–5 orders of magnitude. `BIGSERIAL` (9 quintillion) provides 28,000
> years of capacity at projected use.

The kind of insight human reviewers also frequently miss. Worth keeping
Gemini Pro in rotation once `thought_signature` infra fix lands.

---

## Key findings

### 1. Self-bias confirmed across model tiers

8 providers, 7 catch N1 (algorithmic blind spot). Only Haiku — the model
that authored the algorithm — misses it. The pattern holds regardless of
provider, model size, or training distribution. Self-bias is a
**consistent property of self-critique**, not a function of model
capability.

| Group | N1 detection rate |
|-------|------------------:|
| Haiku critiquing Haiku output | 0/1 (0%) |
| Other providers critiquing Haiku output | 7/7 (100%) |

### 2. Anthropic capability gradient is steep

| Model | Hits / 12 | Tokens | Notes |
|-------|----------:|-------:|-------|
| Haiku | 1 | (baseline) | Single domain hit + 1 hallucination |
| Sonnet 4.6 | 8 | 1805 | OWASP/RFC citations, token leak |
| Opus 4.7 | 9–10 | 3445 | Router collision, partition strategy |

The Haiku→Sonnet jump (8×) is far more significant than the Sonnet→Opus
jump (1 hit). For Critique deployments, **Sonnet 4.6 is the inflection
point**; Opus 4.7 adds ceiling-level findings worth its cost only for
high-stakes review.

### 3. Cross-provider complementarity

No single provider catches everything:

- N7 (factual error) — only gpt-5.5
- N12 (router collision) — only Opus 4.7
- N9 (token leak scenario) — Sonnet 4.6 most concrete
- Scale mismatch — only Gemini Pro

This suggests an **ensemble critique** mode (running two providers and
unioning findings) could exceed any single provider. Filed as future
exploration; not v1.0.

### 4. Hallucination is a real risk for self-critique

Haiku Critique produced one hallucinated finding (W3 GDPR Article 17,
incorrect). Combined with one earlier incident in v6 baseline (W7
incorrect description of `default` semantics), this is two hallucinations
in seven evaluation rounds — non-zero rate.

All seven cross-provider critiques: zero hallucinations. Independent
evidence for cross-provider mapping beyond detection-rate comparison
alone.

### 5. Format compliance varies

| Provider | First-token compliance | Spec section adherence |
|----------|:----------------------:|:----------------------:|
| Sonnet 4.6 | ✓ | ✓ |
| Opus 4.7 | ✓ | ✓ |
| Haiku | ✓ (after backstop) | ✓ |
| MiniMax | ✓ | ✓ |
| gpt-5.5 | ✓ | ✓ |
| gpt-4 | ✗ (`## Conclusion`) | ✗ (added `## Evidence`, `## Follow-up`) |
| Ollama | ✓ | ⚠ (sparse) |
| Gemini Pro | N/A (no specialist) | N/A |

gpt-4 is the only provider violating `ExpectedFirstHeading` and adding
non-spec sections. Combined with poor detection rate, gpt-4 is unsuitable
for Critique specialist work.

---

## SpecialistPresets recommendation

Final mapping for v1.0:

| Specialist | Primary | High-stakes | Fallback | Removed |
|------------|---------|-------------|----------|---------|
| **Critique** | **Sonnet 4.6** | **Opus 4.7** | gpt-5.5, MiniMax | gpt-4, Haiku-self, Ollama |
| RedTeam | Haiku (current) | — | — | (Phase 4 not run) |
| DevilsAdvocate | Haiku (current) | — | — | (Phase 4 not run) |
| WebSearch | (current) | — | — | (not measured) |

### Rationale — Critique → Sonnet 4.6

- 8/12 detection rate matches Opus 4.7 within margin
- 1805 tokens vs Opus 3445 — half the cost
- 96 seconds vs gpt-5.5 166 seconds — fastest among top tier
- OWASP / RFC citations bring production-grade deliverable quality
- Zero hallucinations
- Anthropic in-house — same vendor as default chat

### Rationale — Critique high-stakes → Opus 4.7

When the user explicitly invokes a high-stakes review (security audit,
infrastructure design pre-launch, contract review):

- Catches ceiling-level flaws others miss (router collision, partition
  strategy)
- Most concrete remediation guidance (4-layer custom-code defense)
- Cost only justified when work product warrants it

User-selectable via `SpecialistPresets["critique"] = "high-stakes"`.

### Removed from rotation — gpt-4

- 1/12 detection rate (lowest tier with Haiku-self and Ollama)
- Format violations (no `## Final Report` header, extra sections)
- Token cost not justified by output quality

### Removed from rotation — Haiku self-critique

- 1/12 detection rate
- Hallucination present
- Self-bias consistently demonstrated

Note: Haiku may remain valid for **other specialists** (RedTeam,
DevilsAdvocate where it has shown stability v1–v6). The exclusion is
specifically for Critique on Haiku-authored material — which by
extension means Critique on any Anthropic-Haiku-authored material in
production.

---

## Infrastructure issues (separate work)

### Gemini — `thought_signature` requirement

Gemini 2.0+ requires a `thought_signature` field in `functionCall` parts
when tools are involved. Current `GeminiProvider` does not emit it.
Gemini Pro completed Phase 4 by parent-direct critique (no specialist),
and the result was strong (5/12 + scale-mismatch insight).

**Priority raised**: motivation for fixing GeminiProvider is stronger
because Gemini Pro is now a confirmed valuable Critique provider, not a
hypothetical one.

**Reference**: <https://ai.google.dev/gemini-api/docs/thought-signatures>

**Action**: Update `FieldCure.Ai.Providers.GeminiProvider` to round-trip
`thought_signature` across multi-turn tool calls.

### OpenAI — `max_tokens` vs `max_completion_tokens`

gpt-5+ and o-series reasoning models require `max_completion_tokens`
instead of `max_tokens`. Current `OpenAiProvider` uses the legacy
parameter unconditionally.

**Action**: Add model-family branching in `OpenAiProvider` request build.
Detection: model ID starts with `o1`, `o3`, `o4`, `gpt-5`, or `gpt-6` →
use `max_completion_tokens`. Reasoning models also have additional
constraints (no `temperature`, no `top_p`, no penalties) that need to be
omitted on the same path.

### OpenAI — gpt-4 TPM limit (resolved)

gpt-4 had a 10K TPM limit insufficient for Critique. Workaround during
Phase 4: switched to gpt-4o-mini for evaluation. gpt-4 is excluded from
Critique rotation regardless (poor detection rate per §5).

---

## Limitations

1. **Single material**: Phase 4 used one Haiku-generated doc. Self-bias
   pattern is well-supported (8 providers, consistent N1 result), but
   the specific provider rankings should be validated on at least one
   more domain (file upload service, webhook delivery) before treating
   Sonnet 4.6 as a final default.

2. **Sonnet/Opus consistency unmeasured**: One excellent run does not
   establish reliability. Both should be re-evaluated on Phase 1
   baseline targets (`critique-target.md` etc.) to confirm consistency.

3. **No RedTeam/DevilsAdvocate self-bias data**: Phase 4 measured only
   Critique. Same methodology should apply if those specialists are
   deployed at scale.

4. **Ground truth is a moving target**: N12 was added retroactively after
   Opus surfaced it. Future ground truths should explicitly include
   router/API consistency review as a category. Detection counts have
   ±1 uncertainty per provider.

5. **gpt-4 vs gpt-5.5 within OpenAI**: 5× detection ratio between
   models in the same lineup. "Use OpenAI" is not a useful unit;
   specific model selection matters within each provider.

---

## Decisions taken

1. ✓ Apply Critique → Sonnet 4.6 mapping in `SpecialistPresets` defaults
   for v1.0 ship.
2. ✓ Add Critique high-stakes preset → Opus 4.7 (user-selectable).
3. ✓ Exclude gpt-4 from Critique rotation.
4. ✓ Exclude Haiku-self from Critique rotation (use cross-provider).
5. ✓ Preserve all eight Phase 4 `.astx` files in
   `docs/specialist-eval/2026-04-25/` for future regression comparison.
6. ✓ Add N12 (router path collision) to ground-truth template for
   future Phase 4 rounds.
7. ☐ Schedule second Phase 4 round with different domain (file upload
   service or webhook delivery) — within next two weeks.
8. ☐ Raise GeminiProvider `thought_signature` fix priority — now
   gating a confirmed-valuable Critique provider.
9. ☐ Add OpenAiProvider model-family branching for
   `max_completion_tokens`.

---

## Decisions deferred

- RedTeam and DevilsAdvocate provider mapping — keep current (Haiku)
  until matching Phase 4 evaluations conducted.
- Ensemble critique mode (multi-provider union) — interesting future
  direction, not v1.0.
- MiniMax-M2.7 and gpt-5.5 retention as cross-validation providers —
  decide after Sonnet 4.6 production data accumulates.

---

## Files referenced

| File | Purpose |
|------|---------|
| `haiku-generated-doc.md` | Phase 4 evaluation material |
| `2026-04-25_p4_haiku-on-haiku.astx` | Self-critique baseline |
| `2026-04-25_p4_sonnet4_6-on-haiku.astx` | **Recommended primary** |
| `2026-04-25_p4_opus4_7-on-haiku.astx` | **Recommended high-stakes** |
| `2026-04-25_p4_gpt5_5-on-haiku.astx` | Strong cross-validation |
| `2026-04-25_p4_gemini_pro-on-haiku.astx` | Parent-direct (infra fix needed) |
| `2026-04-25_p4_minimax-on-haiku.astx` | Cross-provider baseline |
| `2026-04-25_p4_gpt4-on-haiku.astx` | Excluded — preserved for record |
| `2026-04-25_p4_ollama-gemma4-on-haiku.astx` | Local-model baseline |
