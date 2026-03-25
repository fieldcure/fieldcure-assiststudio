# Release Notes — FieldCure.DocumentParsers.Pdf

## [0.1.0] - 2026-03-25

### Added
- `PdfParser` — PDF text extraction via PdfPig with `## Page {n}` page headers
- `PdfParser.ExtractImages` — PDF page rendering to PNG via PDFtoImage (PDFium)
- `IMediaDocumentParser` support for combined text + image extraction
- `DocumentParserFactoryExtensions.AddPdfSupport()` — one-line factory registration
- Extracted from AssistStudio.Core to enable independent package consumption
