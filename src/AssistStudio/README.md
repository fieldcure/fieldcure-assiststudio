# AssistStudio

**A Windows-native AI workspace for cloud and local models, profiles, tools, and structured conversations.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

AssistStudio is a desktop AI chat app that puts you in control. Bring your own API keys, choose from multiple providers, connect external tool servers, and manage structured conversations with branching and persistence — all from a native Windows interface.

> If LM Studio is the engine room, AssistStudio is the driver's seat.

---

## Features

### Multi-Provider AI

Chat with five AI providers from a single app. Switch providers mid-conversation with saved presets.

- **Claude** (Anthropic) — Extended thinking, native PDF support
- **OpenAI** — GPT-4o, o-series reasoning models
- **Gemini** (Google) — Multimodal vision and documents
- **Ollama** — Run local models on your own hardware
- **Groq** — Ultra-fast inference via OpenAI-compatible API

### Streaming & Extended Thinking

- Real-time token-by-token streaming with typing indicator
- Extended thinking/reasoning visualization — collapsible thinking blocks show the model's step-by-step reasoning
- Supports Claude extended thinking, OpenAI o-series reasoning, and Ollama think tags

### Conversation Management

- **Multi-tab interface** — Work on multiple conversations simultaneously
- **Save & Load** — Persist conversations as `.astd` files (JSON format)
- **Conversation Branching** — Edit any message to explore alternatives. The original path is preserved, and you can switch between branches with the ◀ 1/2 ▶ navigator
- **Auto-titling** — AI-generated conversation titles

### Attachments

Attach files to any message via drag-and-drop, paste, or file picker:

- Images: PNG, JPG, WebP, GIF, BMP
- Documents: PDF (native, text extraction, or page-as-image), DOCX
- Text files: TXT, CSV, LOG, MD, JSON, XML

### MCP Integration (Model Context Protocol)

Connect to external MCP servers to extend AI capabilities with custom tools:

- **Stdio and HTTP transports** supported
- **Tool aggregation** — Tools from all connected servers appear alongside built-in tools
- **Tool approval** — Tools that require confirmation show an inline approval panel before execution
- **Search tools** — Meta-tool for efficiently searching across large tool sets
- **Built-in servers** — Filesystem server auto-installed via `dotnet tool`, per-tab instances with MCP Roots protocol

### Workspace Folders

- **Per-conversation folders** — Each conversation tab has its own set of workspace folders
- **Title bar folder button** — Add/remove folders from the title bar flyout
- **System prompt injection** — Workspace folder paths are automatically injected into the system prompt
- **Tool CWD** — File and command tools operate within workspace folder context

### Built-in Tools

- **URL Fetch** — Extract web page content with SSRF protection
- **File operations** — Read, write, and search files (via MCP Filesystem)
- **Command execution** — Run shell commands (with approval)
- **Search Tools** — Find the right tool across large MCP tool sets

### Profiles & Presets

- **Provider Presets** — Save multiple provider configurations (model, temperature, max tokens, thinking settings)
- **Profiles** — System prompt templates with tool selection. Switch behavior instantly
- **Tool permissions** — Control which tools each profile can access

### Settings

- **Models** — Configure providers, API keys, model selection, Ollama model pulling
- **Profiles** — Create and manage system prompt profiles with tool bindings
- **Connect** — Add and manage MCP server connections (built-in and external)
- **Personalization** — Light / Dark / System theme, language (English, Korean)
- **Advanced** — Debug mode, conversation pruning, thinking budget, tool call limits

### Security

- **BYOK** — Your API keys are yours. No proxy, no cloud relay
- **Windows Credential Manager** — API keys and MCP environment variables stored in Windows PasswordVault, never serialized to disk
- **SSRF Protection** — URL fetch tool blocks private/internal network addresses

---

## Supported Providers

| Provider | Streaming | Vision | Documents | Tool Calling | Thinking | API Key |
|----------|:---------:|:------:|:---------:|:------------:|:--------:|:-------:|
| **Claude** | Yes | Yes | Yes | Yes | Yes | [anthropic.com](https://console.anthropic.com/) |
| **OpenAI** | Yes | Yes | Yes | Yes | o-series | [platform.openai.com](https://platform.openai.com/) |
| **Gemini** | Yes | Yes | Yes | Yes | No | [aistudio.google.com](https://aistudio.google.com/) |
| **Ollama** | Yes | Dep. | Dep. | Dep. | think tags | Not required |
| **Groq** | Yes | Yes | Yes | Yes | Dep. | [console.groq.com](https://console.groq.com/) |

---

## System Requirements

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10 version 1903 or later |
| **Runtime** | .NET 9.0 Desktop Runtime |
| **WebView2** | Evergreen (included with Windows 11, auto-installed on Windows 10) |

---

## File Association

AssistStudio registers the `.astd` file extension (AssistStudio Document). Double-click any `.astd` file to open it directly in the app.

---

## Built With

AssistStudio is built on two open-source NuGet packages that you can use in your own WinUI 3 apps:

- [FieldCure.AssistStudio.Core](https://www.nuget.org/packages/FieldCure.AssistStudio.Core) — Platform-agnostic AI provider library
- [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) — WinUI 3 chat UI controls

---

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
