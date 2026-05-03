# JSX Artifact Preview Runtime

This folder contains the JavaScript libraries bundled into
`FieldCure.AssistStudio.Controls.WinUI` and served to the artifact-preview
iframe through the `https://assiststudio.vendor/` virtual host.

## Why vendor

The artifact preview is meant to mirror Claude.ai's artifact iframe so user
JSX/TSX renders identically. We embed every runtime library instead of loading
from a public CDN for three reasons:

1. **Data sovereignty.** Primary users (medical institutions, IP / patent
   firms) often run on locked-down networks. The preview must work with no
   outbound internet access.
2. **Time-travel reproducibility.** A `.astx` conversation saved today must
   render byte-identically two years from now. Pinning the runtime here makes
   that promise concrete; CDN versions drift.
3. **Predictable corporate firewall posture.** No third-party allow-listing
   conversation with IT.

## Version matrix (as of 2026-05-03)

Locked, no auto-bump. Versions match what Anthropic's artifact runtime served
on this date, so JSX written for Claude.ai works here without modification.

| Library         | Version                  | File                               | Role            |
| --------------- | ------------------------ | ---------------------------------- | --------------- |
| React           | 18.3.1 (development)     | `react-18.3.1.development.js`      | always          |
| ReactDOM        | 18.3.1 (development)     | `react-dom-18.3.1.development.js`  | always          |
| Babel standalone| 7.29.0                   | `babel-standalone-7.29.0.min.js`   | always          |
| Tailwind CSS    | 3.4.17 (last v3 release) | `tailwindcss-3.4.17.js`            | always          |
| lucide-react    | 0.383.0                  | `lucide-react-0.383.0.min.js`      | opt-in          |
| recharts        | 2.12.7                   | `recharts-2.12.7.js`               | opt-in          |
| d3              | 7.9.0                    | `d3-7.9.0.min.js`                  | opt-in          |
| three.js        | 0.128.0 (r128)           | `three-0.128.0.min.js`             | opt-in          |
| lodash          | 4.17.21                  | `lodash-4.17.21.min.js`            | opt-in          |
| mathjs          | 13.2.0                   | `mathjs-13.2.0.js`                 | opt-in          |
| papaparse       | 5.4.1                    | `papaparse-5.4.1.min.js`           | opt-in          |
| Chart.js        | 4.4.4                    | `chart-4.4.4.umd.js`               | opt-in          |
| Tone.js         | 15.0.4                   | `tone-15.0.4.js`                   | opt-in          |
| SheetJS (xlsx)  | 0.18.5                   | `xlsx-0.18.5.full.min.js`          | opt-in          |
| mammoth         | 1.8.0                    | `mammoth-1.8.0.browser.min.js`     | opt-in          |
| TensorFlow.js   | 4.22.0                   | `tfjs-4.22.0.min.js`               | opt-in          |
| prop-types      | 15.8.1                   | `prop-types-15.8.1.min.js`         | dep of recharts |

Total raw: ~11 MB. NuGet package: 8.93 MB compressed.

## Decisions explicitly made

### React: development builds (not production min)
- Clearer error messages when user code does invalid hooks / setState during
  render. The whole point of the preview is fast feedback.
- Costs ~870 KB over `react-dom.production.min.js`. Worth it.

### Tailwind: pinned v3.4.17 (not v4)
- v4 dropped the JIT runtime CDN model and removed the global
  `tailwind.config` hook.
- Our shadcn shim (`SHADCN_TAILWIND_CONFIG_JS` in
  `WebViewChatRenderer.ArtifactPreview.cs`) sets `tailwind.config` at runtime,
  which v4 no longer supports. Migrating means rewriting that block plus
  retesting every shadcn component shim.
- Backlog item, not blocking.

### Plotly: not bundled
- Anthropic's official matrix lists Plotly. We exclude it from the bundle:
  - Full build is 4.84 MB. Adding it pushes the nupkg from 8.93 MB to ~13.7 MB
    — heavier than React + ReactDOM + Babel combined.
  - Basic build is 1.11 MB but silently drops ~half the chart types (3D, geo,
    finance) at import time.
  - Recharts + Chart.js + D3 cover the artifact patterns seen in alpha so far.
- Plotly imports still *work* — they fall through to the auto-fallback path
  (see below) and load from esm.sh on demand. The header indicator surfaces
  this so users / IT can see exactly what hit the network.

### Auto-fallback for unmapped JSX imports
When user code imports a module that isn't in `JSX_LIB_CDN`,
`transformJsxArtifact` does **not** strip it. It rewrites the import to
reference `window.__ext_<sanitized_name>`, and the bootstrap dynamic-imports
the module from `https://esm.sh/<mod>` before user code runs. If the import
fails, the iframe shows a precise error naming the module and source so IT
can whitelist the exact host.

This mirrors Claude.ai's own behavior (it dynamically loads long-tail libs
from cdnjs). Air-gap users keep working renders for the bundled 17; everyone
else gets the rest of the npm ecosystem too.

The preview header surfaces a muted, italic note —
`Loaded externally: plotly (esm.sh), d3-cloud (esm.sh)` — whenever an
artifact pulls anything from outside the bundle. Source-aware so users can
distinguish esm.sh from cdnjs in the rare HTML-artifact path.

## How it's served

1. `<EmbeddedResource Include="Rendering\Resources\vendor\jsx\*.js" />` in
   `AssistStudio.Controls.csproj` embeds every `.js` file in this folder into
   the assembly. The `*.js` glob deliberately excludes this README.
2. `WebViewChatRenderer.OnWebResourceRequested` intercepts every WebView2
   request. Anything with host `assiststudio.vendor` is handed to
   `TryServeVendorResource` (in `WebViewChatRenderer.ArtifactPreview.cs`),
   which:
   - Looks up the requested filename in `VendorResourceMap` — a `Lazy`
     dictionary built once from `Assembly.GetManifestResourceNames()`.
   - Streams the embedded resource straight to WebView2 via
     `IRandomAccessStream`. Zero disk I/O.
   - Sets `Cache-Control: public, max-age=31536000, immutable` and
     `Access-Control-Allow-Origin: *` (sandbox iframes have a null origin
     and the script tags use `crossorigin`).
3. The bootstrap script tags inside `JsxPreviewBranchJs` use
   `https://assiststudio.vendor/<filename>` URLs, as does the `JSX_LIB_CDN`
   import map.

## Updating a library

When Anthropic bumps a library version (e.g. React 19) or a security fix
lands:

1. Download the new file into this folder. Keep the version in the filename
   (`react-19.0.0.development.js`) so audits stay readable.
2. Delete the old file.
3. Update the corresponding URL in `JSX_LIB_CDN`, `JSX_DEP_CDN`, or the
   bootstrap `<script src="…">` tags inside
   `WebViewChatRenderer.ArtifactPreview.cs`.
4. Rebuild. Load a sample artifact that exercises the library and confirm in
   the chat.
5. Update the version matrix above. Bump the
   `FieldCure.AssistStudio.Controls.WinUI` package version.

## External CDN allow-list

`AllowedCdnRoutes` (in `WebViewChatRenderer.ArtifactPreview.cs`) permits
three hosts. Vendored libs cover the locked 17, so these only fire for
unmapped JSX imports or raw `<script src="…">` tags inside HTML artifacts:

| Host                    | Why it's allowed                                           |
| ----------------------- | ---------------------------------------------------------- |
| `esm.sh`                | JSX import auto-fallback (modern ESM CDN, Cloudflare-backed). Primary. |
| `cdnjs.cloudflare.com`  | Anthropic's own artifact CDN. Claude.ai HTML often hard-codes these script tags. |
| `cdn.jsdelivr.net`      | The other widely-used npm CDN. Models pick this for raw `<script src=…>` of libs they don't trust the import system to resolve (mammoth, pdf.js, …). |
| `unpkg.com`             | Secondary fallback — esm.sh outage or packages it doesn't serve. |

Drop entries if MS Store review objects. Removing all three reverts the
preview to "vendored libs only" — JSX imports outside the bundled 17 will
fail with a clear error.
