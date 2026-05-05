# Contributing to FieldCure AssistStudio

Thanks for your interest in improving AssistStudio. This document covers what you need to know before opening an issue or pull request.

## Ways to Contribute

- **Bug reports** — File a GitHub issue with a clear title, repro steps, expected vs. actual behavior, and your environment (OS build, .NET SDK, Visual Studio version, affected package + version).
- **Feature ideas** — Open an issue to discuss before writing code, especially for changes that touch the public API of any NuGet package.
- **Documentation** — Fixes to `README.md`, code-level XML doc comments, and sample apps are all welcome.
- **Pull requests** — See the workflow below.

## Project Layout

This is a multi-package solution. The most important targets:

| Project | Package | TFM |
|---|---|---|
| `src/Ai.Providers` | `FieldCure.Ai.Providers` | `net8.0` |
| `src/Ai.Execution` | `FieldCure.Ai.Execution` | `net8.0` |
| `src/AssistStudio.Core` | `FieldCure.AssistStudio.Core` | `net8.0` |
| `src/AssistStudio.Controls` | `FieldCure.AssistStudio.Controls.WinUI` | `net8.0-windows10.0.19041.0`, `net9.0-windows10.0.19041.0` |
| `src/AssistStudio.Anthropic` | `FieldCure.AssistStudio.Anthropic` | `net8.0`, `net9.0` |
| `src/AssistStudio.Controls.Anthropic` | `FieldCure.AssistStudio.Controls.Anthropic.WinUI` | `net8.0-windows10.0.19041.0`, `net9.0-windows10.0.19041.0` |
| `src/AssistStudio` | (main desktop app) | `net9.0-windows10.0.19041.0` |

See `README.md` for a full overview.

## Development Environment

- **Visual Studio 2022 17.12+** (required for the `.slnx` solution format) or the .NET CLI.
- **.NET 9 SDK** (the app and `net9.0` library targets) plus the **.NET 8 SDK** for the platform-neutral packages.
- **Windows App SDK / WinUI 3 workload** — required to build the Controls and the desktop app.
- **Windows 10 build 19041 (20H1) or later** to run the app and the WinUI controls.

The Core, Providers, and Execution packages are platform-neutral; only the Controls and the app require Windows.

## Build and Test

```bash
dotnet build                              # Whole solution
dotnet test                               # All test projects under tests/
dotnet test tests/Ai.Providers.Tests      # A single project
```

Make sure `dotnet build` is warning-free and `dotnet test` is green before opening a PR. `GenerateDocumentationFile` is enabled across the libraries, so any missing XML doc comment will surface as a warning.

## Coding Conventions

- **C# 12**, `nullable enable`, implicit usings.
- **XML doc comments** (`/// <summary>`) on every method including private methods and event handlers.
- **`#region` blocks** group code by role (Properties, Fields, Methods, Events, ...).
- **`INotifyPropertyChanged`** is paired with the local `SetField<T>()` helper; the main desktop app uses `CommunityToolkit.Mvvm`.
- **Templated controls** follow the `Themes/Generic.xaml` + `PART_` naming + `OnApplyTemplate()` convention.
- **Platform gating** — Windows-only members in platform-neutral assemblies must be marked `[SupportedOSPlatform("windows")]`.
- **Namespaces** — `FieldCure.Ai.*` for newer packages, `FieldCure.AssistStudio.*` for the original ones, `AssistStudio` (no prefix) for the main app.
- **Localization** — UI strings live in `.resw` files. Both `en-US` and `ko-KR` should be updated when you add or change visible text.

## Pull Request Workflow

1. Fork the repository and branch from `main`.
2. Keep changes focused — one feature or fix per PR. If a refactor is needed to support the change, prefer a separate prep PR.
3. Update or add tests under `tests/` for behavioral changes.
4. Update `README.md` and the relevant package's release notes if the public surface changes.
5. Run `dotnet build` and `dotnet test` locally.
6. Open the PR with a clear description: what changed, why, and how to verify.

## Commit Messages

Write commit messages in **English**. Conventional Commits prefixes are encouraged but not required:

- `feat: add streaming cancellation to ChatPanel`
- `fix: resolve WebView2 disposal race in tab close`
- `docs: clarify ProviderPreset.ApiKey lifetime`
- `refactor: extract MarkdownExportResult helpers`

Keep the subject under ~70 characters and use the body to explain *why*.

## License and DCO

By submitting a pull request, you agree that your contribution is licensed under the project's MIT license (see `LICENSE`). This project does not currently require a Developer Certificate of Origin sign-off.

## Code of Conduct

Participation in this project is governed by the [Code of Conduct](CODE_OF_CONDUCT.md). Please read it before contributing.
