# Release Notes — AssistStudio App

## [0.8.0] - 2026-03-22

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

## [0.7.0] - 2026-03-21

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

## [0.6.0] - 2026-03-17

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

## [0.5.0] - 2026-03-17

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

## [0.4.0] - 2026-03-17

### Added
- NuGet publish automation (`publish-nuget.ps1` script)

---

## [0.3.0] - 2026-03-17

### Added
- ToolApprovalPanel for tool execution confirmation before running
- Summarize button on InputContainer
- External links in chat now open in default browser
- TextBox stays focused during streaming (prevents WebView2 focus steal)

---

## [0.2.0] - 2026-03-16

### Added
- PDF Handling setting per provider (Auto / Native PDF / Page as Image / Text Extraction)
- `DisplayName` shown for tools in Profile settings (instead of snake_case)
- Tool calling warning localized in Profile Tools section

---

## [0.1.0] - 2026-03-15

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
