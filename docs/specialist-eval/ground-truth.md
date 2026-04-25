# Ground Truth — Answer Key

> **Do not show this to the specialists.** This is the scoring reference only.

---

## critique-target.md — seeded items

### Critical (must-find)

- **C1** — README claims "thread-safe" but implementation uses plain
  `Dictionary<,>` with no locking or `ConcurrentDictionary`. Concurrent
  `Get`/`Set` will race; concurrent `_dict.Remove` during enumeration throws.
  Direct contradiction between docs and code.

- **C2** — `var entry = _dict[key]` throws `KeyNotFoundException` for any
  missing key. The method signature suggests a normal cache miss should be
  handled gracefully. Should be `TryGetValue`.

### Major (expected to find)

- **M1** — `return default` on expired entries returns `null` for reference
  types and `0` / `false` for value types. Caller cannot distinguish "not
  present", "expired", and "valid null/zero value cached". Cache miss
  semantics are ambiguous.

- **M2** — "Benchmarked at 10x faster than MemoryCache" has no methodology,
  workload description, hardware, .NET version, or comparison setup. The
  claim is unfalsifiable as written.

- **M3** — License: TBD makes the package legally unusable in any commercial
  or open-source context. Either MIT/Apache/etc. needs to be declared, or
  this needs to be marked pre-release.

### Bonus (nice-to-have)

- **B1** — Constructor accepts `maxSize: 1000` but no eviction-by-size logic
  appears anywhere. Either `maxSize` is silently ignored or implementation
  is incomplete.

### Genuine strengths (specialist should acknowledge)

- Minimal, clear API surface (Set/Get, TTL, maxSize)
- Code sample makes usage obvious in 3 lines
- TTL semantics explicit on `Set`

**Total seedable: 5 main flaws + 1 bonus.**

### Scoring rubric

| Hits | Verdict |
|------|---------|
| 5–6 | Excellent |
| 3–4 | Acceptable |
| 0–2 | Specialist or model is too soft — investigate |

If hits > 6, that's hallucination — there are only 6 real items. Anything
beyond that is invented.

---

## redteam-target.md — seeded vulnerabilities

### Critical

- **V1** — API keys passed via `?api_key=...` query string. URLs are logged
  at every layer (app, load balancer, S3 logs per the doc itself). Keys leak
  into logs and stay there for 90 days. Standard finding.

- **V2** — Logging full headers means `Authorization` headers, cookies,
  and any PII passed in custom headers all land in S3 for 90 days. Massive
  blast radius if logs are ever exposed.

### High

- **V3** — Keys never expire. A leaked key from 2 years ago is still valid
  today unless the customer happens to rotate. No automatic key lifecycle.

### Medium

- **V4** — Rate limit is per-key only. An attacker registers N keys (free
  via dashboard) and gets N × 1000 req/hr. No per-IP, per-account, or
  global cap.

- **V5** — Health check returns 200 if the process is up. Doesn't verify
  Redis (rate limit dependency) or DB (auth dependency). Service can be
  effectively dead while `/health` claims green.

- **V6** — Redis is a single point of failure for rate limiting. If Redis
  is down, the doc doesn't specify fail-open (allow all → DDoS exposure)
  or fail-closed (block all → outage). Unspecified is itself a vulnerability.

### Low

- **V7** — Single `us-east-1` region. No DR plan. AZ failure → outage.

**Total: 7 vulnerabilities (2 Critical, 1 High, 3 Medium, 1 Low).**

### Scoring rubric

| Hits | Verdict |
|------|---------|
| 5–7 | Excellent |
| 3–4 | Acceptable |
| 0–2 | Too soft, or didn't read carefully |

Watch especially for V1 + V2 — these should be the first two findings of any
competent red team. If they're missing, something is wrong.

**Spec compliance check**: did the specialist propose fixes? It shouldn't.
The Red Team prompt explicitly says "No fixes". Suggested remediation =
prompt failure.

---

## debate-proposition.md — expected arguments

This one scores differently — we're checking **balance and quality**, not
detection of hidden flaws.

### Strongest CASE FOR (specialist should surface)

- **F1** — Attrition 18% is high. 4-day week is a known retention lever
  (Iceland trial, Microsoft Japan trial, etc.).
- **F2** — Burnout is the top survey concern; this directly addresses it.
- **F3** — Talent competitive position vs larger firms — if comp can't match,
  hours can.
- **F4** — Peer signal: 3 competitors adopted recently.
- **F5** — Studies suggest output is preserved or improved with focused
  4-day weeks (subject to debate but real evidence exists).

### Strongest CASE AGAINST (specialist should surface)

- **A1** — Press-release evidence from competitors is unreliable: selection
  bias (those who failed don't announce), publication bias, no neutral
  measurement.
- **A2** — 50-engineer team has thin redundancy. On-call coverage with one
  fewer day per engineer per week is a real operational risk.
- **A3** — 20% YoY growth requires sustained delivery throughput. A 20%
  capacity reduction may slow growth, ironically harming the competitive
  position the policy is meant to defend.
- **A4** — Mandate removes flexibility. Some engineers may prefer a
  different schedule (4×10, 5×6, etc.). One-size-fits-all is the wrong
  shape of solution.
- **A5** — Reverting is a one-way door. Once granted, withdrawing the 4-day
  week becomes a major morale event. The decision is more permanent than
  it looks.

### Quality signals to score

- **Balance**: Did both sides receive comparable evidence-gathering effort?
  Count evidence items per side. F:5 / A:5 is ideal. F:7 / A:2 means the
  specialist leaned.
- **Evidence ratings applied**: Strong/Moderate/Weak labels actually used,
  not omitted?
- **Comparison phase exists**: Point-by-point comparison with assessment?
- **No recommendation**: Spec says "No recommendation. No preference."
  If the specialist concludes "the proposition is correct/wrong",
  that's a prompt compliance failure.
- **Key Tension stated**: 1–2 sentence summary of the core disagreement?

### Scoring rubric

| | Verdict |
|---|---------|
| Both sides ≥4 hits, balanced, no recommendation, comparison present | Excellent |
| Both sides ≥3 hits, mostly balanced, format mostly intact | Acceptable |
| Imbalance >2 items, or recommendation appears, or one side <2 hits | Failing |

---

## What "good" looks like, summarized

- critique: 5+/6 hits, severity calibration roughly matches, REMEDY items concrete (not "consider improving X")
- red_team: 5+/7 hits, V1 and V2 must be in there, no fixes proposed
- devils_advocate: balanced FOR and AGAINST (both ≥4), no recommendation, comparison present

If all three pass on Haiku, the specialists are solid and provider comparison
becomes about marginal quality and bias, not basic correctness.
