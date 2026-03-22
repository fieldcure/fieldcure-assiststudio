# Release Notes — FieldCure.DocumentParsers

## [0.1.0] - 2026-03-22

### Added
- `IDocumentParser` interface for extensible document text extraction
- `DocxParser` — DOCX text extraction with markdown table conversion (via OpenXML SDK)
- `HwpxParser` — HWPX (Korean OWPML) text extraction with markdown table conversion
- `DocumentParserFactory` — extension-based parser resolution with `SupportedExtensions` discovery
- Markdown table output with pipe escaping for LLM / RAG consumption
- NuGet package README with quick start guide
