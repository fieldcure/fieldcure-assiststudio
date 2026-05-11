# Microsoft Store Listing Assets

This folder contains the **public, customer-facing** assets for the AssistStudio
Microsoft Store listing — paste-ready copy, image captions, screenshots, and the
store-tile logo masters.

> **Listing submitted:** 2026-05-08 (App v1.0.0, commit `f79bb2e`).
> **Status:** live in Microsoft Store as of 2026-05-11.
> **Store ID:** `9N09D0QGSTZD` — deep link `https://apps.microsoft.com/detail/9N09D0QGSTZD`.
> **IARC age rating:** 3+. **Global Rating ID:** `8f652986-296a-8e9e-86f0-3f2ccb43a96e` —
> reusable on other IARC-licensed storefronts (Google Play, Nintendo eShop, etc.) to
> skip re-filling the questionnaire. Re-rating is only required if a future update
> changes the questionnaire answers (e.g. adds in-app purchases, social features,
> ads, or user-generated content sharing).

## Public — `docs/store/` (this folder, tracked)

```
docs/store/
├── README.md                ← this file
├── copy-writing.txt         ← paste-ready listing text (EN + KO), no markdown
├── screenshot-captions.md   ← per-screenshot captions (EN + KO)
├── screenshots/             ← 1366×768 store screenshots, 01–08
│   ├── 01-hero-pkce.png
│   ├── 02-multi-provider.png
│   ├── 03-agent-github.png
│   ├── 04-tool-approval.png
│   ├── 05-vision-mockup.png
│   ├── 06-artifact-kpi.png
│   ├── 07-rag-rfc.png
│   └── 08-local-ollama.png
└── assets/
    └── logos/
        └── store-logo.png   ← 300×300 1:1 App tile icon (uploaded to Partner Center)
```

> Illustrator master (`store-logo.ai`) lives in `design/` alongside the other
> brand sources (`Logo.ai`, `AssistStudioEcosystem.ai`).

These files mirror what is **publicly displayed on the Store** (description,
screenshots, captions, app tile) and are safe to share, reference in
PRs, and accept external contributions on.

## Private — `todo/store/` (gitignored)

Cert-reviewer-facing or working-draft material that should **not** ship in the
public repo:

```
todo/store/
├── notes-for-certification.md             ← longer pre-submission draft
├── notes-for-certification-submitted.txt  ← actually-pasted trimmed version
├── restricted-capabilities.txt            ← runFullTrust + broadFileSystemAccess
│                                            justifications (Partner Center capability fields)
├── copy-writing.txt                       ← rich-markdown source draft (kept for reference)
└── generative-ai-declaration.md           ← drafted, not used (no AI declaration required)
```

The reason these stay private:
- **Notes for certification** is reviewer-only (not customer-visible) and historically
  carries scoped credentials in the Credentials section — keeping the body out of
  public history avoids accidental key leaks on future submissions.
- **Restricted capabilities justifications** are reviewer-only fields in Partner
  Center → Properties → Capabilities, not part of the customer listing.

## What goes where in Partner Center

| Partner Center field | Source file |
|---|---|
| Short description (EN/KO) | `copy-writing.txt` → SHORT DESCRIPTION |
| Long description (EN/KO) | `copy-writing.txt` → LONG DESCRIPTION |
| Features (EN/KO) | `copy-writing.txt` → FEATURES |
| Search keywords | `copy-writing.txt` → SEARCH KEYWORDS |
| Copyright / license / website / support | `copy-writing.txt` → COPYRIGHT |
| Screenshots | `screenshots/01–08*.png` |
| Screenshot image captions | `screenshot-captions.md` |
| 1:1 App tile icon (300×300) | `assets/logos/store-logo.png` |
| Properties → Capabilities → runFullTrust | `todo/store/restricted-capabilities.txt` |
| Properties → Capabilities → broadFileSystemAccess | `todo/store/restricted-capabilities.txt` |
| Submission options → Notes for certification | `todo/store/notes-for-certification-submitted.txt` |

## Caveats

- **Markdown is not rendered** in any Store listing field. `copy-writing.txt`
  uses `■` section markers (matches mainstream Korean Store listings) — do **not**
  paste the `------` separator lines, those are for navigation only.
- The 9:16 Poster art (720×1080 / 1440×2160) and 1:1 Box art (1080×1080 / 2160×2160)
  slots are **not yet uploaded** — listing falls back to the package logo. See
  open work below.

## Open work

All bundled into the **v1.1 submission** (v1.0.1 is being skipped). The
"Assist Studio" display name is also reserved on the same product (Partner
Center → Manage app names). Deadline for using the reserved name in a
submission: **2026-08-11** (3 months from reservation), so v1.1 must publish
before then to keep the name.

- [ ] **Display name change** — switch the Store-visible name from `AssistStudio`
  to `Assist Studio` (with a space) for better tokenized search. Reserved name
  already attached to the same package identity (PFN unchanged → existing users
  receive the v1.1 update automatically). Sync `Package.appxmanifest`
  `<DisplayName>` and `<uap:VisualElements DisplayName>` to the new name.
- [ ] **Kakao-style v2 screenshots** — large left-side copy + tilted app window on
  dark slate (#1F1F1F) + sparkle accents. Headlines come from `copy-writing.txt`
  long-description H2 sections. Replace `screenshots/01–08*.png`.
- [ ] **9:16 Poster art** (720×1080 + 1440×2160) for hero placements (Store carousel,
  Xbox display). Use the same dark-slate + sparkle motif.
- [ ] **1:1 Box art** (1080×1080 + 2160×2160) for variable layouts.
