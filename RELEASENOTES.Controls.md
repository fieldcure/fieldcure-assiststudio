# Release Notes — FieldCure.AssistStudio.Controls.WinUI

## v0.21.0 (2026-05-07)

### Added
- **`ChatPanel.EnabledToolNames` getter** + **`ChatPanel.EnabledToolsChanged` event** + **`ComposeBar.EnabledToolsChanged` event** — hosts can now react to per-conversation tool-flyout toggles. Read `EnabledToolNames` live (`null` = all enabled, otherwise the strict subset the user wants visible to the model on the next send) for lifecycle decisions tied to a per-conversation tool override; subscribe to the event to fire side effects when the user checks or unchecks a server. The compose-bar flyout already raised `SelectionChanged` internally; this release re-emits it as a public event chain (ToolSelectionFlyout → ComposeBar → ChatPanel) so the host does not have to subclass either control. Notably consumed by AssistStudio's per-tab Filesystem MCP reconciler.
- **Workspace folder missing-on-disk indicator** — folder flyout now renders Segoe Fluent Icons E7BA (Warning) next to any folder that does not exist on disk (e.g., moved, renamed, or unmounted external drive). New resw key `FolderFlyout_FolderMissing` carries the tooltip; existence is re-evaluated each time the flyout opens, so a re-attached drive clears the icon naturally without a host-side refresh. Companion to host-side toast logic that downgrades severity to Warning when alive folders < total.

### Fixed
- **Workspace folder removal lingered in the flyout until reopen** — the trash button updated `WorkspaceFolders` and fired `WorkspaceFoldersChanged` but did not touch the bound `ObservableCollection<FolderFlyoutItemViewModel>`. The deleted row now disappears in the same frame.
- **Multiple removals in one flyout open could resurrect a previously deleted folder** — the remove lambda used the snapshot captured when the flyout opened, so a second click would compute its update against the original list and write the first deletion back. The lambda now reads `WorkspaceFolders` live each click.
- **Pasted-text attachment chips were inert** — clicking a pasted-text chip did nothing where file chips opened a preview. The chip now wires the same OpenAttachment handler as file attachments.
- **Attachment previews now render inside the user bubble** — image and document previews previously sat above the bubble; they now appear inline alongside the message text, matching standard chat UX.
- **Branch nav arrows lock during streaming** — clicking a branch arrow mid-stream could leave the conversation tree inconsistent. Arrows now disable while a turn is streaming.

### Changed
- Rebuilt against same **FieldCure.AssistStudio.Core 0.19.2** and **FieldCure.Ai.Providers 0.7.2**. No new transitive deps.

---

## v0.20.0 (2026-05-05)

### Added
- **`ChatPanel.GroupDisplayNameResolver` / `ComposeBar.GroupDisplayNameResolver`** — optional `Func<string, string?>` delegates the host injects so user-defined custom-provider IDs (e.g., `Custom_046A…`) display as readable names (e.g., `MiniMax`) in the model picker group headers and the selected-model summary. Falls back to the raw `ProviderType` string (with a `Mock → "demo"` alias) when no resolver is supplied, preserving the standalone Controls behavior. `ModelPicker` group headers (`ObservableGroup.Key`) now also pick up the resolved name so flyout headers and the selected-model line match.
- **Seven greeting variants** — the empty-state header used to read a single fixed string (`"How can I help you today?"`); a fresh tab now picks one of seven localized variants (en-US + ko-KR) at random per render. New resw keys `ChatPanel_Greeting_1` … `ChatPanel_Greeting_7`; the legacy `ChatPanel_Greeting` key is removed.

### Fixed
- **Empty-state header leaked the model id** — switching the model mid-conversation overwrote `Title` with the provider name, and a transient empty title (e.g., a tool approval that opened before the AI's first reply landed) flashed the model id instead of the greeting. The header now falls back to the greeting whenever `Title` is empty (regardless of conversation activity), and edit/refresh affordances appear only once a real title exists.
- **Thinking block anchored below streamed text** — `appendThinkingBlock` used to push the disclosure section to the end of the bubble, so once streaming text started arriving the thinking block landed below the assistant's reply. It now inserts above the `.content` span, matching tool-block placement.
- **Tab recycle leaked previous tab's UI state** — `ChatPanel.Dispose` now resets the imperative per-tab fields that don't flow through bindings (current turn, editing-message reference, draft text, attachments, edit-mode flag, conversation-active flag, cached greeting, workspace folders, knowledge-base selection) so a recycled container starts at empty-state instead of inheriting the previous tab's compose-bar contents and header text.

### Changed
- Rebuilt against the same **FieldCure.AssistStudio.Core 0.19.1** and **FieldCure.Ai.Providers 0.7.2**. No new transitive deps.

---

## v0.19.0 (2026-05-04)

### Added
- **HTML / JSX artifact preview** — fenced ```html``` and ```jsx``` code blocks now render in a sandboxed (`allow-scripts`, no `same-origin`) iframe with a Preview / Code toggle and live theme sync. The iframe ships React 18, ReactDOM, Babel-standalone, Tailwind, recharts, lucide-react, three.js, d3, lodash, mathjs, papaparse, Chart.js, Tone, SheetJS (xlsx), mammoth, TensorFlow.js, and prop-types as vendored UMD bundles served from `https://assiststudio.vendor/` via a strict CDN whitelist; unmapped imports fall back to `https://esm.sh/` with a clear error if the fetch fails. shadcn/ui imports resolve against an inline component shim (~25 primitives) so artifacts written for the Claude hosted environment render visually faithful without a real component library. Hooks (`useState`, `useEffect`, `useRef`, …) are also lifted onto `window` so import-less artifacts work as written, and an artifact's own `ReactDOM.render(...)` tail call is now accepted as the entry point in addition to `export default` / `App` / `Component`.
- **`ModelPicker` control** — replaces the per-tab provider dropdown with a multi-model selector (one provider can list N enabled models). Hosted inside `ComposeBar` and the auxiliary-task model selector. Includes a `ModelPickerEntry` view-model and reusable templates.
- **Continue split into its own bubble** — the synthetic "Continue writing…" user turn that the Continue button used to inject is now hidden (`ChatMessage.IsHidden`), and the resumed assistant reply renders as a separate bubble with a small "↪ continued" chip (`ChatMessage.IsContinuation`). Reloading a saved conversation that was cut off at `max_tokens` shows a discreet "truncated" hint on the last assistant bubble (`ChatMessage.IsTruncated`) instead of a phantom Continue button.
- **In-chat scroll-to-bottom button** — small floating chevron appears when the conversation is scrolled away from the latest message.
- **Document and audio attachment chips** — DOCX/PDF/etc. and audio files now render with proper chip styling alongside images. Audio chips include a send-time reject bar for providers that don't accept audio.
- **Gemini inline image output** — assistant messages from Gemini image-generation models render the inlined image into the bubble, not as a tool result, and persist through `.astx` save/reload like other attached media.
- **Elicitation `SubmitMode`** — `ToolElicitationPanel` exposes a Submit mode (raise event vs. self-close) so external hosts can mount the panel inside their own dialog without the panel forcing the close. Required-field validation runs before Submit fires.
- **Explicit `AutomationProperties.Name` on icon-only buttons** — templated controls (ChatPanel/ComposeBar/ToolApprovalPanel/etc.) wire their PART_* icon buttons through `AutomationHelper.SetAutomation`, and previously-tooltip-only buttons in the host app namespace now carry `<x:Uid>.AutomationProperties.Name` resw entries in en-US and ko-KR.

### Fixed
- **JSX import rewriter** consolidated into a single multi-form pass that handles `default`, `named`, `namespace`, `default + named`, `default + namespace`, and bare side-effect imports. Multi-line brace bodies (`import { a, b,\n  c } from "mod"`) and `import X, { ... }` combinations no longer fall through to Babel and crash with `SyntaxError: Cannot use import statement outside a module`.
- **Artifact preview iframe width** — assistant messages carrying a `.diagram-block` now claim the full chat column width so 700×500 canvases don't end up cramped with horizontal scroll. Plain-text replies are unaffected.
- **TDZ shadowing** in default/namespace import rewrites — duplicate `const` bindings collapsed to a single declaration when the module is imported in more than one form.
- **ChatPanel duplicated provider placeholder** when the provider name equals the model id (no separate model line collapsed correctly).
- **Latent `PasswordBox` elicitation regression** — secret-looking fields rendered as `TextBox` in some host configurations.

### Changed
- **Chat column widened** from 800px to 1100px so artifact iframes have room for typical dashboards / docx viewers without the first paint forcing a horizontal scroll. Artifact iframe default height lifted from 400px to 540px (still drag-resizable).
- **Copy feedback** — wide buttons keep the "Copied" label, narrow icon buttons swap to a ✔ glyph (text wrapped to two lines in the 24×24 footprint and looked broken). Toast tone toned down.
- **Public-repo prep** — Korean strings in XML doc comments and developer-facing comments translated to English. The lone intentional Korean literal (`ChatPanel.Features.cs:200` Title prefix list) is kept — it matches LLM responses written in Korean.
- Rebuilt against **FieldCure.AssistStudio.Core 0.19.0** (Profile `PreferredModelName`) and **FieldCure.Ai.Providers 0.7.0** (`ProviderPreset → ProviderModel` rename, `ChatMessage.IsHidden` / `IsContinuation` / `IsTruncated`, audio attachments, Gemini inline images).

### Internal
- Artifact preview JS literals split out of `WebViewChatRenderer.cs` into `WebViewChatRenderer.ArtifactPreview.cs` for reviewability.

---

## v0.18.0 (2026-04-27)

### Added
- **Mermaid diagram rendering** — fenced code blocks with `lang=mermaid` render inline as SVG via bundled mermaid.min.js (UMD v10), initialised with the current chat theme. `lang=svg` blocks render the inline SVG directly with `<script>` tags stripped.
- **Diagram action header** — Mermaid and SVG blocks expose **Copy SVG**, **Save as SVG**, and **Save as PNG** actions in the block header. Tool-result inline images reuse the same header (replacing the modal-based viewer) so save/copy actions are consistently one click away.
- **Compose-bar reuse for editing** — clicking *Edit* on a prior user message rehydrates the compose bar (text + attachments) instead of opening a separate editor surface, so editing runs through the same input pipeline as a fresh message.

### Fixed
- **Tool-result image header** restored after the diagram-block refactor (regression introduced earlier in this cycle).
- **Transparent tooltip background** on hover-over diagram actions and other custom tooltip surfaces.

### Changed
- Rebuilt against **FieldCure.AssistStudio.Core 0.18.0** (`.Core` namespace segment) and **FieldCure.Ai.Providers 0.6.0** (Anthropic prompt caching, gpt-5+ reasoning, Gemini `thoughtSignature` round-trip).

---

## v0.17.1 (2026-04-24)

### Added
- **`WebViewChatRenderer.BeginToolBlockAsync` / `ResolveToolBlockAsync`** — public wrappers for a two-stage tool block render. `Begin` places a pulsing placeholder tagged with a call id; `Resolve` rewrites the same block in place when the result arrives. `ChatPanel` now uses this pair for sub-agent (`delegate_task`) calls so parallel fan-outs no longer leave the UI silent for tens of seconds.
- Cleanup safety net for sub-agent tool blocks: anything still pending when Phase 2 exits (cancellation, exception, partial completion) is swept with an `[interrupted]` block so the pulse never hangs.

### Fixed
- **Orphaned "Continue writing from where you left off." user message** — the ephemeral continue prompt survived into `.astx` saves because it was only removed from `_messages` and not from the children tree (`GetAllMessages` reads from the tree). Added `UnregisterFromTree` that prunes both sides, recomputes sibling indices, and rewires the parent's `ActiveChildId` if it pointed to the removed message. Continue flow also moves `ResumeMessageAsync` / CTS setup inside the `try` block so the `finally`-driven cleanup runs on every path.
- Added diagnostic logs around the Continue stream round, which previously bypassed `StreamAndExecuteAsync` and so produced no "Request start" / "Stream completed" entries.

### Migration
- Additive API surface. Existing callers of `AppendToolBlockAsync` are unchanged. The new `Begin`/`Resolve` pair is opt-in.

---

## v0.17.0 (2026-04-21)

### Added
- **`ToolElicitationPanel`** — secret-looking string fields (names matching `password`/`secret`/`token`/`apiKey`/etc.) auto-render as `PasswordBox` so values don't leak via shoulder-surfing or screen capture.

### Changed
- **Knowledge Archive → Knowledge Base** — UI strings and localized resources renamed across `ChatPanel`, dialogs, and related panels.
- **Anthropic model IDs** — migrated built-in presets to the Claude 4.6 family.
- **ComposeBar tool flyout** — tool name localization dropped in favor of raw names; matches the simpler tool picker UX used elsewhere.
- **ResourceLoader** — migrated to `Microsoft.Windows.ApplicationModel.Resources` and consolidated to static fields across the Controls project (avoids the `CoreWindow`-dependent legacy path that failed from `Task.Run`).
- Rebuilt against **Core 0.17.0** and **Ai.Providers 0.5.0** (transitive DocumentParsers 2.x). `DocumentParserFactory` public API surface unchanged; consumers recompile transparently.

### Fixed
- AuxProvider preset lookup race during first tab activation.
- Title sanitization for titles generated by small models that ignored the instruction.

---

## v0.16.0 (2026-04-14)

### Added
- **Markdown export** — conversation export with per-message attribution
- **External streaming API** — `AssistantTurnHandle`, `StreamResult`, and visibility DPs for SDK consumers
- **Sub-agent report** — render sub-agent report as markdown in tool block details
- **Structured ToolApprovalPanel** — XAML DataTemplates with Expander-based parameters

### Changed
- **Folder reorganization** — Controls project restructured by concern (ChatPanel, ComposeBar, Rendering, etc.)
- **ChatPanel/ComposeBar refactoring** — split into concern-based partial classes
- **Style extraction** — per-control XAML files (ComposeBar, AttachmentPreviewBar, SubtleButton, Shared.xaml)
- **Attachment chip** — move remove button to right of name instead of overlaying thumbnail

### Fixed
- **Tool error JSON injection** — replace string interpolation with `JsonSerializer.Serialize` for safe error messages
- **Unicode escape in WebView2** — fix tool result rendering with special characters
- **Tool result duplication** — prevent duplicate tool results in conversation
- **DPI scaling** — add PerMonitorV2 to sample app manifest for WebView2

---

## v0.15.0 (2026-04-10)

### Added
- **MCP Elicitation** — `ToolElicitationPanel` for MCP server user-input requests with multi-field selection and batch submit
- **MCP server badge** — server name badge on `ToolApprovalPanel` for tool origin identification
- **Tool block details** — expandable tool blocks showing arguments, result, and duration
- **Tool block interleave** — tool blocks rendered in correct position between text segments
- **Tool execution pulse** — color pulse animation on tool headers during execution
- **Categorized provider combobox** — Cloud / Custom / Local / Demo separators in provider selector
- **Themed tooltips** — `data-tooltip` attribute-based tooltips across all WebView2 elements
- **Timestamp tooltip** — full date+time tooltip on message timestamp hover

### Changed
- `RenderAssistantBubbleAsync` extracted for branch switch interleave rendering
- Delta storage migration for tool call messages

### Fixed
- ThinkingContent persistence through save/load cycle
- WebView2 ghosting on TabView container recycling
- Thinking block targeting, shimmer animation, and preset list casting
- Image modal constrained to viewport bounds

---

## v0.14.0 (2026-04-07)

### Added
- **Multimedia rendering** — MCP `ImageContentBlock` inline display, audio/video player elements with native controls
- **Image hover toolbar** — zoom (popover viewer), save (`FileSavePicker`), and copy buttons on image hover
- **Knowledge Base selector** — flyout in `ComposeBar` for per-conversation KB selection with `kb_id` system prompt hint
- **Tool approval user instruction** — free-text input field on `ToolApprovalPanel` for injecting user notes as transient messages
- **Streaming elapsed time** — real-time elapsed timer in `ComposeBar` during streaming responses
- **Sub-Agent tool result blocks** — collapsible delegate_task result display with restored labels on conversation load

### Changed
- Streaming consumption moved off UI thread via `Channel<T>` producer-consumer pattern for reduced UI thread contention
- Media file download routed through `FileSavePicker` (audio/video) and virtual host (temp files)
- Image save/copy logic moved from `WebViewChatRenderer` to `ChatPanel` for cleaner separation
- `search_tools` dynamically promoted based on available tool count

### Fixed
- Base64 double-encoding on MCP image content blocks
- Media element positioning after `finalizeMessage` call
- Tab switch now pauses playing audio/video elements
- Tool result images preserved during message finalization (no longer stripped)
- User message no longer hidden when assistant response starts streaming
- User instruction injected as transient user message instead of tool result append

---

## v0.13.0 (2026-03-31)

### Changed
- Streaming token rendering batched at 50ms intervals to reduce WebView2 `ExecuteScriptAsync` calls — prevents UI thread saturation during large responses
- `await Task.Delay(1)` yield during tool call argument streaming to keep UI responsive

---

## v0.12.0 (2026-03-30)

### Fixed
- `ToolApprovalPanel` prompt text not updating when tool name changes — added `PropertyChanged` callbacks for `ToolName` and `ToolDisplayName` dependency properties
- Built-in server version fallback using `config.Name` (display name) instead of extracting key from `config.Id` — now correctly resolves NuGet package version

---

## v0.11.0 (2026-03-29)

### Added
- `ChatZoomFactor` dependency property for dynamic chat UI scaling (default 1.05)
- Collapsible long user messages with Show more / Show less toggle
- URL and content length display in `fetch_url` tool result blocks
- Collapsible `search_documents` results in WebView2 chat UI
- Indexing cancel button + RAG file count safety limits in folder flyout
- Indexing progress display in folder flyout and title bar
- Dynamic tooltip on folder icon showing indexing file name
- Lock icon on archive folder rows
- Server placeholder entries in tools flyout with localized tool names
- Persistent memory tools (`remember`, `forget`) UI support

### Changed
- `InputContainer` renamed to `ComposeBar`
- Tool display name localization removed — raw function names used instead
- Server toggle removed from tools flyout, replaced with `PrepareToolsForSendAsync` send-time auto-connect
- `Profile.Text` → `Profile.SystemPrompt` in bindings
- ProgressRing replaced with folder icon opacity animation for indexing state
- Folder icon VisualState renamed to `Idle`

### Fixed
- WebView2 blank screen when clicking relative-path links (navigation intercepted)
- OpenAI RAG source links converted to plain text before rendering
- ProgressRing and folder button layout wrapped in StackPanel
- Progress ring placement, percent color, InfoBar messages localized
- PasswordVault empty-value exception prevention

---

## v0.10.0 (2026-03-24)

### Added
- Folder flyout converted from code-behind to XAML `<Button.Flyout>` with `{ThemeResource}` bindings
- `FolderStates` VisualStateGroup (`HasFolders`/`NoFolders`) for folder icon glyph swap (FolderOpen ↔ FolderFill)
- Folder flyout localization via `ResourceLoader` (x:Uid not supported in ControlTemplate)
- Remove button tooltips with Mouse placement in folder flyout
- Static `ResourceLoader` field replacing per-call instantiation

### Changed
- Folder icon: FolderOpen (`E838`) when empty, FolderFill (`E8D5`) when folders exist — replaces accent color indicator
- Folder button tooltip: "Folder Settings" (was "Workspace Folders")
- Server-owned MCP tools (e.g., RAG tools) hidden from individual tool checkboxes — controlled by server checkbox only

### Fixed
- Folder icon invisible in default state (`Foreground = null` → VisualState with `{ThemeResource}`)
- Flyout text not following theme (`Application.Current.Resources` → XAML `{ThemeResource}` bindings)
- Flyout PART_ elements not resolved (`Flyout.Opening` → `Flyout.Opened` for visual tree availability)

### Removed
- `PART_FolderBadge` (unused numeric badge overlay)
- `BuildFolderFlyout()` code-behind method (~270 lines)

---

## v0.9.0 (2026-03-24)

### Added
- `AvailableServers` property on `ChatPanel` for MCP server status display
- `ServerInfo` model for lightweight server descriptor (Id, DisplayName, IsConnected, IsBuiltIn)

### Changed
- Tool flyout redesigned with server-level toggles replacing tool-level selection

---

## v0.8.0 (2026-03-22)

### Changed
- Document parser code extracted from `InputContainer` to `FieldCure.DocumentParsers` package
- `DocumentExtensions` now dynamically derived from `DocumentParserFactory.SupportedExtensions` — new parsers are auto-registered

### Fixed
- HWPX table extraction failing due to nested element structure (`hp:p > hp:run > hp:tbl`)

---

## v0.7.0 (2026-03-21)

### Added
- Extended thinking UI with collapsible left-bar styled thinking blocks
- Streaming tool call display as themed blocks with tool name labels
- Progressive streaming render (tokens displayed as they arrive, markdown finalized on complete)
- Tool toggle UI in `InputContainer` with select all/deselect all and `search_tools` policy support
- Conversation branching with tree-based edit flow and branch navigator
- `Ctrl+F` conversation search with highlight and prev/next navigation
- Shimmer loading placeholder control
- `IsReadOnly`, `ShowTitleBar`, `AllowAttachments`, `EmptyStateContent`, `FontFamily`, `FontSize` dependency properties on `ChatPanel`
- `RegisteredTools`, `WorkspaceContext`, `ContextProvider`, `UtilityProvider` properties on `ChatPanel`
- `KeyboardShortcutPressed` event on `ChatPanel`
- MCP server disconnect notification on app exit

### Changed
- **Breaking:** `IAiProvider.StreamAsync` now returns `IAsyncEnumerable<StreamEvent>` (consumers must update)
- Controls RootNamespace changed from `FieldCure.AssistStudio` to `FieldCure.AssistStudio.Controls`
- `SubtleButtonStyle` aligned with WinAppSDK 1.8 built-in specification
- Chat font size increased for readability; input area sizing improved
- Edit/resend button renamed to "Send" with settings hint
- `GeneratedRegex` source generator used for all regex patterns (SYSLIB1045)
- `CreatePreviewItem` return type narrowed from `UIElement` to `Grid` (CA1859)

### Fixed
- Stream cancellation now finalizes message (removes blinking cursor)
- WebView2 clipboard shortcuts (`Ctrl+C/V/X`) and streaming CSS styles
- Conversation branch restoration losing messages and navigator state
- Tool calling conversation restoration showing blank on reopen
- Post-stream input focus restored automatically

---

## v0.6.0 (2026-03-17)

### Added
- Title edit button tooltip with localized resource strings (`ChatPanel_EditTitleTooltip`)
- `SetBottomRightToolTip` helper for consistent tooltip placement on title bar buttons

### Changed
- Title bar button tooltips use `PlacementMode.Mouse` instead of default top placement

### Fixed
- Missing `SubtleButtonStyle` causing crash in packaged builds

---

## v0.5.0 (2026-03-17)

### Added
- Dedicated NuGet package README with Controls-specific XAML examples and API reference

---

## v0.4.0 (2026-03-17)

### Added
- NuGet package metadata (Company, Copyright, Icon, README, Repository URL, Tags)
- Release notes auto-inclusion in NuGet package
- `publish-nuget.ps1` script for pack → sign → push workflow

---

## v0.3.0 (2026-03-17)

### Added
- `ToolApprovalPanel` templated control for tool execution confirmation UI
- Summarize button wired from `InputContainer` to `ChatPanel`
- External link navigation redirected to default browser from WebView2

### Fixed
- WebView2 stealing focus from TextBox during streaming response

---

## v0.2.0 (2026-03-16)

### Fixed
- Duplicate file attachment on drag-and-drop (event bubbling from InputContainer to ChatPanel)
- Consecutive tool results merged into single user message for Claude compatibility

---

## v0.1.0 (2026-03-15)

### Added
- `ChatPanel` templated control with WebView2-based message rendering
- `InputContainer` templated control with provider/profile selectors and attachment support
- `AttachmentPreviewBar` templated control for file previews
- Markdown rendering via marked.js with code syntax highlighting (highlight.js)
- LaTeX/math rendering via KaTeX
- Code block copy-to-clipboard
- Streaming display with cursor indicator
- Image paste and file picker attachment
- PDF and DOCX text extraction for document attachments
- Localization support (en-US, ko-KR)
