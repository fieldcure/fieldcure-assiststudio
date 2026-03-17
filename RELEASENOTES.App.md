# Release Notes — AssistStudio App

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
