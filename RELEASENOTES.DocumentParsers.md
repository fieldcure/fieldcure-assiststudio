# Release Notes — FieldCure.DocumentParsers

## [0.2.0] - 2026-03-25

### Added
- `XlsxParser` — XLSX spreadsheet extraction as markdown tables with SharedString resolution
- `PptxParser` — PPTX slide text, tables, and speaker notes extraction
- `IMediaDocumentParser` interface for parsers with image extraction capability
- `DocumentImage` record for extracted images with label and index
- `DocumentParserFactory.Register()` method for external parser registration (e.g., PDF)

### Changed
- `DocumentParserFactory` now uses `ConcurrentDictionary` for thread-safe dynamic registration
- `SupportedExtensions` now includes `.xlsx` and `.pptx`

## [0.1.0] - 2026-03-22

### Added
- `IDocumentParser` interface for extensible document text extraction
- `DocxParser` — DOCX text extraction with markdown table conversion (via OpenXML SDK)
- `HwpxParser` — HWPX (Korean OWPML) text extraction with markdown table conversion
- `DocumentParserFactory` — extension-based parser resolution with `SupportedExtensions` discovery
- Markdown table output with pipe escaping for LLM / RAG consumption
- NuGet package README with quick start guide
