# AssistStudio Dependency Graph

Cross-repository dependency map for the AssistStudio ecosystem.

> **Legend** — Solid line: NuGet PackageReference / Dashed line: ProjectReference (same solution)  
> All packages use the `FieldCure.` prefix (omitted for readability)

## Internal Structure (assiststudio solution)

```mermaid
graph TD
    App["AssistStudio App"]

    App -.->|ProjectRef| Controls["Controls.WinUI"]
    App -.->|ProjectRef| Exec["Ai.Execution"]
    Controls -.-> Core["Core"]
    Core -.-> Prov["Ai.Providers"]
    Exec -.-> Prov
```

## Cross-Repository Dependencies

```mermaid
graph TD
    App["AssistStudio App"]
    Runner["Runner"]
    Ess["Mcp.Essentials"]
    Rag["Mcp.Rag"]
    FS["Mcp.Filesystem"]

    Prov["Ai.Providers"]
    Exec["Ai.Execution"]

    DPOcr["DocParsers.Pdf.Ocr"]
    DPPdf["DocParsers.Pdf"]
    DP["DocumentParsers"]

    DPOcr -.-> DPPdf -.-> DP

    App --> DPPdf
    Prov --> DP

    Runner --> Exec
    Runner --> Prov

    Ess --> DPOcr
    Rag --> DPOcr
    FS --> DP
```

> Mcp.Outbox, Mcp.PublicData.Kr — No FieldCure internal dependencies (external NuGet only)

## Package Index

| Package | Version | Repository | Type |
|---|---|---|---|
| AssistStudio (App) | — | fieldcure-assiststudio | WinUI App |
| Controls.WinUI | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI?label=)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) | fieldcure-assiststudio | Library |
| Core | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core?label=)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core) | fieldcure-assiststudio | Library |
| Ai.Providers | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Ai.Providers?label=)](https://www.nuget.org/packages/FieldCure.Ai.Providers) | fieldcure-assiststudio | Library |
| Ai.Execution | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Ai.Execution?label=)](https://www.nuget.org/packages/FieldCure.Ai.Execution) | fieldcure-assiststudio | Library |
| Runner | [![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Runner?label=)](https://www.nuget.org/packages/FieldCure.AssistStudio.Runner) | fieldcure-assiststudio-runner | dotnet tool |
| DocumentParsers | [![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers?label=)](https://www.nuget.org/packages/FieldCure.DocumentParsers) | fieldcure-document-parsers | Library |
| DocumentParsers.Pdf | [![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers.Pdf?label=)](https://www.nuget.org/packages/FieldCure.DocumentParsers.Pdf) | fieldcure-document-parsers | Library |
| DocumentParsers.Pdf.Ocr | [![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers.Pdf.Ocr?label=)](https://www.nuget.org/packages/FieldCure.DocumentParsers.Pdf.Ocr) | fieldcure-document-parsers | Library |
| Mcp.Essentials | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Essentials?label=)](https://www.nuget.org/packages/FieldCure.Mcp.Essentials) | fieldcure-mcp-essentials | dotnet tool |
| Mcp.Rag | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Rag?label=)](https://www.nuget.org/packages/FieldCure.Mcp.Rag) | fieldcure-mcp-rag | dotnet tool |
| Mcp.Filesystem | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Filesystem?label=)](https://www.nuget.org/packages/FieldCure.Mcp.Filesystem) | fieldcure-mcp-filesystem | dotnet tool |
| Mcp.Outbox | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Outbox?label=)](https://www.nuget.org/packages/FieldCure.Mcp.Outbox) | fieldcure-mcp-outbox | dotnet tool |
| Mcp.PublicData.Kr | [![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.PublicData.Kr?label=)](https://www.nuget.org/packages/FieldCure.Mcp.PublicData.Kr) | fieldcure-mcp-publicdata | dotnet tool |

## Notes

- **Essentials / Rag**: Both explicitly reference all three DocumentParsers packages. Referencing Pdf.Ocr alone would be sufficient via transitive dependencies.
- **Outbox / PublicData.Kr**: No FieldCure internal package dependencies (external NuGet only).
