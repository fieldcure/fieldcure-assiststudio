# Release Notes — AssistStudio App

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
- Settings panel: Models, Profile, Utility AI, Personalization, Advanced, About
- Provider preset management with PasswordVault-based API key storage
- Profile system with system prompt templates
- Conversation save/load (JSON) with recent files menu
- Theme switching (Light, Dark, System)
- Ollama model management dialog (pull, delete, search)
- Auto title generation via utility AI
- Single-instance enforcement
- Localization support (en-US, ko-KR)
