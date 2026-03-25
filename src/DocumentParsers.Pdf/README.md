# FieldCure.DocumentParsers.Pdf

**PDF text extraction and page image rendering** — an extension package for [FieldCure.DocumentParsers](https://www.nuget.org/packages/FieldCure.DocumentParsers).

[![NuGet](https://img.shields.io/nuget/v/FieldCure.DocumentParsers.Pdf)](https://www.nuget.org/packages/FieldCure.DocumentParsers.Pdf)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **Text extraction** — Page-by-page text extraction via PdfPig with `## Page {n}` headers
- **Page rendering** — Each page rendered as PNG via PDFtoImage (PDFium)
- **IMediaDocumentParser** — Implements both `ExtractText` and `ExtractImages` for PDF
- **Factory integration** — One-line registration with `DocumentParserFactory`

## Install

```bash
dotnet add package FieldCure.DocumentParsers.Pdf
```

## Quick Start

```csharp
using FieldCure.DocumentParsers;
using FieldCure.DocumentParsers.Pdf;

// Register PDF support (call once at startup)
DocumentParserFactoryExtensions.AddPdfSupport();

// Text extraction
var parser = DocumentParserFactory.GetParser(".pdf");
var text = parser!.ExtractText(File.ReadAllBytes("document.pdf"));

// Page image rendering
var mediaParser = (IMediaDocumentParser)parser;
var images = mediaParser.ExtractImages(File.ReadAllBytes("document.pdf"), dpi: 150);
foreach (var img in images)
    File.WriteAllBytes($"{img.Label}.png", img.Data);
```

## Dependencies

- [FieldCure.DocumentParsers](https://www.nuget.org/packages/FieldCure.DocumentParsers) — `IDocumentParser` interface
- [PdfPig](https://www.nuget.org/packages/PdfPig) — PDF text extraction
- [PDFtoImage](https://www.nuget.org/packages/PDFtoImage) — PDF page rendering (PDFium native)

## Related Packages

- [FieldCure.DocumentParsers](https://www.nuget.org/packages/FieldCure.DocumentParsers) — DOCX, HWPX, XLSX, PPTX text extraction
- [FieldCure.AssistStudio.Core](https://www.nuget.org/packages/FieldCure.AssistStudio.Core) — AI provider client library

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
