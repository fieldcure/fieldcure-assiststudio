# Release Notes — AssistStudio App

## v0.16.0 (2026-04-24)

### Added
- **Parallel sub-agent UI feedback** — when the model dispatches multiple `delegate_task` calls in a single turn, each tool block renders immediately with a pulsing placeholder and resolves in place as results arrive. Slowest agent keeps pulsing until its own result lands; cancellation replaces any still-pending block with an `[interrupted]` marker so the pulse never hangs.
- **Truncation-aware sub-agent handling** — when a sub-agent's final report is cut off at `max_tokens`, the tool result now carries `status: "truncated"` instead of being silently classified as completed. The Judgment Specialist's routing guideline instructs the parent to surface the truncation to the user and retry with a tighter scope rather than forward a mid-markdown cutoff as the final answer.
- **Strengthened Judgment Specialist routing** — parent conversation now forwards the specialist's `report` verbatim inside a fenced block, caps its own commentary, and re-invokes with the same `specialist` parameter on incomplete reports (instead of falling through to raw `delegate_task` arguments and losing the specialist system prompt).

### Fixed
- **Continue flow no longer leaves orphaned user messages in saved conversations** — the hidden "Continue writing from where you left off." prompt used to survive into `.astx` when save raced with the stream (or when Continue was interrupted). Now pruned from both `_messages` and the conversation tree in a `finally` block so cancellation, exception, and partial completion all clean up. Also fixes the re-open case where the orphan was rendered as a visible user bubble with a branch indicator.

### Rebuilt against
- `FieldCure.AssistStudio.Controls.WinUI` 0.17.1
- `FieldCure.Ai.Execution` 0.3.2

---

## v0.15.0 (2026-04-10)

### Added
- **Custom OpenAI-compatible providers** — Register any OpenAI-compatible endpoint (Together AI, MiniMax, etc.) with custom base URL and display name. Categorized provider selector (Cloud / Custom / Local / Demo)
- **MCP Elicitation** — MCP servers can request user input via structured form fields; multi-field selection with batch submit
- **Tool block details** — Expandable tool blocks showing arguments, result, and execution duration. Tool blocks interleaved with text in correct position
- **Tool execution indicator** — Color pulse animation on tool headers during execution
- **Themed tooltips** — Hover tooltips across all chat elements (timestamps show full date+time)
- **MCP server badge** — Server name badge on tool approval panel for tool origin identification

### Changed
- **Conversation format migration** — `.astd` (JSON) replaced by `.astx` (ZIP archive) with manifest, conversation, and media bundled in a single file
- Custom provider thinking support — `reasoning_details` parsing and `<think>` tag streaming extraction for MiniMax and similar providers
- Delta storage migration for tool call messages

### Fixed
- ThinkingContent persisted through save/load cycle
- WebView2 ghosting on tab container recycling
- Image modal constrained to viewport bounds
- Thinking block targeting and shimmer animation

---

## v0.14.0 (2026-04-07)

### Added
- **Sub-Agent delegation** — `delegate_task` tool for autonomous sub-agent execution with parallel dispatch and specialist routing
- **Web Search Specialist** — dedicated search agent with `ISpecialist` architecture and external search MCP compatibility
- **Schedule page** — cron schedule management UI with bilingual cron descriptor (en/ko) and one-time schedule display
- **Knowledge Base page** — unified KB management with create/delete/settings dialog, `EmbeddingModelSelector`, progress/stop UI
- **Media persistence** — images, audio, and video saved in `.astd` files via `MediaStore` with per-conversation media directory
- **Image compression** — automatic JPEG compression and resize before sending to providers (`ImageCompressor` via SkiaSharp)
- **Memory → Essentials MCP** — memory tools migrated from in-process virtual server to Essentials MCP; system prompt injection via `list_memories`
- **MCP server auto-update** — built-in servers automatically updated to latest NuGet version on startup
- **Search engine selection UI** — Essentials MCP search engine configuration on Connect page
- **Ollama native thinking** — support for Ollama native `thinking` field and cloud model reasoning UI
- **Ollama login button** — Ollama.com model hub login, sorted model list, nested dialog crash fix
- **User instruction on tool approval** — free-text input field on `ToolApprovalPanel` for injecting user notes into tool results
- **Streaming elapsed time** — real-time elapsed timer displayed during streaming responses
- **Image hover toolbar** — zoom, save, and copy buttons on hover over inline images with popover viewer

### Changed
- **ModelsPage refactored** — split into `CloudProviderSection` and `OllamaProviderSection` standalone UserControls with deferred loading
- **RAG multi-KB** — shared RAG server with `kb_id` parameter; KB selector in `ChatPanel` flyout
- **MCP server startup parallelized** — `Task.WhenAll` for concurrent server initialization
- **Settings navigation reordered** — natural workflow order (Connect → Models → Profiles → Knowledge → Memory → Schedule → Tasks)
- **AppTasks page simplified** — lightweight flat list replacing complex grid
- **Preset management optimized** — debounced `PresetsChanged`, cached presets, suppressed initialization saves
- **DocumentParsers updated** to v1.x

### Fixed
- Completed one-time tasks toggle now disabled on Schedule page
- MCP server race condition on early message send prevented
- MCP server reconnection after edit even if previously failed
- Nested dialog crash in Ollama model management
- ko-KR punctuation localization warnings resolved
- User message hidden when assistant response starts
- Image save/copy routed through `ChatPanel` instead of renderer

---

## v0.13.0 (2026-03-31)

### Added
- **Essentials MCP server** — replaced in-process virtual server with external `FieldCure.Mcp.Essentials` v0.2.0 (7 tools: http_request, run_command, run_javascript, get_environment, read_file, write_file, search_files)
- Essentials and Runner server cards on ConnectPage
- Duplicate tool name resolution — Filesystem tools take precedence over Essentials when both active
- `http_request` auto-approved (no confirmation popup)

### Changed
- **AI Providers extracted** to new `FieldCure.Ai.Providers` v0.1.0 NuGet package
- Built-in Runner server bumped to v0.3.0 (default MCP servers, Core dependency removed)
- Profile `EnabledServers` migrated from `"essentials"` to `"builtin_essentials"` convention
- Streaming token batching (50ms intervals) for smoother UI during large responses

### Removed
- In-process tool classes (`ReadFileTool`, `WriteFileTool`, `SearchFilesTool`, `RunCommandTool`, `UrlFetchTool`) — replaced by Essentials MCP server

---

## v0.12.0 (2026-03-30)

### Added
- **MCP Outbox** built-in server — send messages via Slack, Telegram, Email (SMTP), and KakaoTalk through AI conversations
- Outbox card on Connect page with version display
- Outbox checkbox in Profile page (between Knowledge Folders and external servers)
- Outbox enabled by default in General and Task Planner profiles with system prompt guidance
- Built-in server version display on Connect page cards (fallback to NuGet package version)

### Changed
- Built-in MCP Outbox server v0.2.0
- User-server filters in ConnectPage and ProfilesPage now use `IsBuiltIn` flag instead of prefix matching

### Fixed
- Empty tool arguments crash for parameterless MCP tools (e.g., `list_channels`)
- `ToolApprovalPanel` showing wrong tool name on consecutive approvals
- ProfilesPage "Tools & Servers" section spacing increased

---

## v0.11.0 (2026-03-29)

### Added
- Persistent memory with `remember` / `forget` tools for cross-conversation context
- Essentials virtual server — bundles built-in tools (`read_file`, `remember`, `forget`) as an in-process MCP server
- `read_file` now supports PDF, DOCX, XLSX, PPTX, HWPX via `FieldCure.DocumentParsers`
- Document file size limit raised to 50 MB for `read_file`
- Indexing cancel button with RAG file count safety limits
- Chat UI scaled to 105% via CSS zoom (`ChatZoomFactor` dependency property)
- NuGet package version display on built-in MCP server cards
- Built-in MCP server version display on server cards
- Indexing progress in folder flyout and title bar
- Knowledge Base added as built-in profile
- Auto-deduplicate profile names with (2), (3) suffix

### Changed
- Built-in profiles redesigned: Chat, General, Analytical, Creative, Task Planner, Knowledge Base
- Tab-independent profiles — each tab owns its profile and system prompt (global broadcast removed)
- Send-time auto-connect for MCP servers (`PrepareToolsForSendAsync` replaces manual toggle)
- `InputContainer` renamed to `ComposeBar`
- Tool display name localization removed — raw function names used
- `Profile.Text` renamed to `Profile.SystemPrompt`
- `DocumentParsers` migrated to independent NuGet packages (`FieldCure.DocumentParsers` 0.3.x, `FieldCure.DocumentParsers.Pdf` 0.2.x)
- Built-in RAG server bumped to v0.10.1 (parallel contextualization, timing logs, indexing lock, PDF support, file count limits)
- Built-in Filesystem server at v0.5.0

### Fixed
- Save/load branch detection: `ActiveChildId` restore and `SiblingCount` guard prevent false branching from tool chains
- RAG server reconnection when loading conversations from `.astd` files
- Gemini API compatibility: MCP tool schema normalization strips unsupported keywords
- WebView2 blank screen on relative-path link clicks
- PasswordVault empty-value exception (`PasswordVaultHelper` guard)
- OpenAI RAG source links rendered as plain text before markdown conversion
- Custom profiles with built-in name collisions skipped on load
- Tab profile and system prompt preserved on settings page changes
- App tasks page flicker on navigation eliminated
- First-chance exceptions in `PasswordVaultHelper` when key not found

---

## v0.10.0 (2026-03-24)

### Added
- Knowledge Base (RAG) built-in MCP server with per-tab folder management
- Knowledge Base folder flyout with set/re-index/remove controls
- Per-tab RAG server lifecycle (connect, index, disconnect)
- Knowledge Base system prompt hint with `search_documents` instruction
- Embedding model selection with RadioButtons and auto-select
- `.astd` conversation persistence for Knowledge Base folder and built-in server configs

### Changed
- Folder flyout redesigned as XAML-based with proper light/dark theme support
- Folder icon swaps between FolderOpen (empty) and FolderFill (has folders)
- RAG tools (`index_documents`, `search_documents`, `get_document_chunk`) hidden from individual tool list — controlled by Knowledge Folders server checkbox
- Folder button tooltip changed to "Folder Settings"

### Fixed
- Folder icon invisible except on hover (VisualState replaces manual foreground assignment)
- Flyout theme not matching app theme (XAML `{ThemeResource}` replaces `Application.Current.Resources`)
- App exit hang from orphan MCP server tasks (`ExitProcess` via kernel32)
- Deleted folder crash on conversation restore (guard check added)

---

## v0.9.0 (2026-03-24)

### Added
- Built-in MCP server infrastructure with auto-install via `dotnet tool`
- MCP Filesystem server bundled as built-in (auto-install/update on startup)
- MCP Roots protocol support with per-tab filesystem instances
- Workspace folders button in title bar with folder add/remove flyout
- Workspace paths injected into system prompt and tool CWD
- Profile-level server toggles (enable/disable MCP servers per profile)
- Auto-enable MCP servers in tools flyout when they connect
- Reset-to-defaults button for built-in profiles
- `search_tools` meta-tool filtered by profile's enabled servers

### Changed
- Workspace folders refactored to conversation-only ownership
- InputContainer buttons reordered, summarize hidden until needed, tooltips added
- Built-in server card layout improved with 2-line formatting and title row badge
- All tooltips set to `PlacementMode.Mouse` for consistent positioning

### Fixed
- MCP tools not executable after `search_tools` discovery
- Tool buttons missing on new tabs (deferred push to DispatcherQueue)
- MCP server orphan task hang on app exit (force process exit)
- Filesystem crash on ConnectPage toggle without folders
- Null reference in `BuildToolsFlyout` servers iteration
- Tool result overflow truncated to prevent context overflow
- Tool result size guard improved with binary detection and lower threshold
- Analyzer messages: Lock type, static method, collection expressions

---

## v0.8.0 (2026-03-22)

### Added
- Protocol activation via `assiststudio://` URI scheme

### Changed
- Document parsers extracted to independent `FieldCure.DocumentParsers` package

### Fixed
- Active path message ordering in branch restoration
- Race condition in conversation restore rendering
- Title bar visibility and dirty tracking on conversation operations
- Conversation marked dirty on branch switch and title edit
- Fire-and-forget MCP server kill for fast app exit

---

## v0.7.0 (2026-03-21)

### Added
- Extended thinking support with per-provider toggle in Models settings
- MCP (Model Context Protocol) integration: server registry, connect page, edit dialog, and graceful shutdown
- `search_tools` meta-tool for token-efficient tool selection with large tool sets
- Conversation branching with tree-based edit flow and branch navigator
- `Ctrl+F` conversation search with highlight and navigation
- `CollapsibleSection` control for grouping Models page providers and ProfilesPage tools
- `ThemedContentDialog` base class for theme-aware dialogs
- `NotificationCenter` with slide-in/out animated InfoBar notifications
- Comprehensive structured logging across all subsystems
- Per-provider `MaxTokens` setting on Models page
- `UrlFetchTool` for web page content extraction
- Shimmer loading placeholders in ModelSelectionDialog
- MCP tool grouping on ProfilesPage with filter and tool counts
- Open Logs Folder button on Advanced settings page
- MCP server status notifications (connect/disconnect)
- Recent conversations menu styled like VS Recent Files

### Changed
- Settings panel refactored to Frame-based navigation with AppSettings events
- ProfilesPage UI improved: ComboBox selection, auto-height instructions, tool counts
- Ollama terminology unified (Pull → Download), download survives page navigation
- Conversation file extension renamed from `.astx` to `.astd` (AssistStudio Document)
- File picker label unified to "AssistStudio Document"
- `ConversationManager` and `AppJsonContext` moved from Core NuGet to App layer
- `SearchFilesTool` made async with cancellation support
- Helpers and Tools moved from Modules/ to standard root-level folders

### Fixed
- Conversation branch restoration losing messages and navigator state
- Tool calling conversation restoration showing blank on reopen
- Recent conversations MRU ordering on selection
- WebView2 clipboard shortcuts and streaming CSS styles
- Ollama status check deferred on Models page entry

---

## v0.6.0 (2026-03-17)

### Added
- `ChatTabView` UserControl for declarative tab content layout (MVVM pattern)
- Title edit button tooltip (localized en-US / ko-KR)

### Changed
- `ChatTabViewModel` exposes observable properties instead of directly creating `ChatPanel`
- Tab `DataTemplate` now uses `ChatTabView` with `x:Bind` instead of raw `ChatPanel` reference
- Profile changes in Settings no longer override profile on tabs with active conversations
- Title bar button tooltips use `PlacementMode.Mouse` for better positioning

### Fixed
- App crash on profile switch in packaged/installed builds (`0xc000027b`)
- `ContentDialog` always rendering in light theme regardless of app theme
- IL Trimming breaking `System.Text.Json` reflection in Release builds

---

## v0.5.0 (2026-03-17)

### Added
- `ThemeHelper` utility for theme-aware brush resolution across the app
- Custom ThemeDictionary brushes in App.xaml for status indicators (error, accent)
- Hide model/PDF options on Models page when API key is absent

### Changed
- Settings navigation icons: Models → sparkle (AI), Profile → people
- Ollama status text colors now use XAML ThemeResource for proper light/dark theme support
- Cloud provider status text uses `{ThemeResource SystemControlErrorTextForegroundBrush}`
- `App._window` → `App.MainWindow` public property

### Fixed
- Status text color contrast in dark mode (replaced hardcoded colors with theme-aware brushes)
- GitHub repository URL in About page (`fieldlab` → `fieldcure`)
- Removed publish/signing properties from csproj that caused build failures without USB dongle

---

## v0.4.0 (2026-03-17)

### Added
- NuGet publish automation (`publish-nuget.ps1` script)

---

## v0.3.0 (2026-03-17)

### Added
- ToolApprovalPanel for tool execution confirmation before running
- Summarize button on InputContainer
- External links in chat now open in default browser
- TextBox stays focused during streaming (prevents WebView2 focus steal)

---

## v0.2.0 (2026-03-16)

### Added
- PDF Handling setting per provider (Auto / Native PDF / Page as Image / Text Extraction)
- `DisplayName` shown for tools in Profile settings (instead of snake_case)
- Tool calling warning localized in Profile Tools section

---

## v0.1.0 (2026-03-15)

### Added
- Multi-tab chat interface with TabView
- Settings panel: Models, Profiles, App Tasks, Personalization, Advanced, About
- Provider preset management with PasswordVault-based API key storage
- Profile system with system prompt templates
- Conversation save/load (JSON) with recent files menu
- Theme switching (Light, Dark, System)
- Ollama model management dialog (pull, delete, search)
- Auto title generation via app tasks
- Single-instance enforcement
- Localization support (en-US, ko-KR)
