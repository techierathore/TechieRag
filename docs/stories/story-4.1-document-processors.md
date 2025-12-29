# Story 4.1: Implement Document Processors

## Story Information
**Story ID:** STORY-4.1
**Epic:** EPIC-004 - Document Processors and Embedding Providers
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 8

## User Story
As a developer, I want to ingest various document formats so that I can build RAG systems over diverse content.

## Description
Implement document processors for PDF, DOCX, plain text, Markdown, HTML, JSON, TOML, and source code files.

## Acceptance Criteria
- [ ] PdfProcessor.cs - PDF document processing with PdfPig
- [ ] DocxProcessor.cs - Word document processing with OpenXml
- [ ] TextProcessor.cs - Plain text file processing
- [ ] MarkdownProcessor.cs - Markdown file processing
- [ ] HtmlProcessor.cs - HTML file processing (strip tags)
- [ ] JsonProcessor.cs - JSON file processing
- [ ] TomlProcessor.cs - TOML file processing with Tomlyn
- [ ] CodeProcessor.cs - Source code processing (.cs, .js, .ts, .py, etc.)
- [ ] All processors implement IDocumentProcessor
- [ ] All have text chunking logic
- [ ] Complete XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### NuGet Packages Required
Add to TechieRag.csproj:
- PdfPig (0.1.9)
- DocumentFormat.OpenXml (3.2.0)
- Tomlyn (0.19.0)
- Markdig (0.38.0) - for Markdown parsing
- HtmlAgilityPack (1.11.72) - for HTML parsing

### Base Chunking Logic
Create a shared chunking utility:
```csharp
// src/TechieRag/Processors/TextChunker.cs
public static class TextChunker
{
    public static IEnumerable<string> ChunkText(string text, int maxSize, int overlap)
    {
        // Split by sentences/paragraphs, respecting maxSize and overlap
    }
}
```

### Processor Implementation Pattern
```csharp
namespace TechieRag.Processors;

public class PdfProcessor : IDocumentProcessor
{
    public IReadOnlyList<string> SupportedExtensions => [".pdf"];

    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content, string fileName,
        DocumentProcessingOptions? options, CancellationToken ct)
    {
        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();

        // 1. Extract text from document
        // 2. Chunk the text
        // 3. Create TextChunk objects with metadata

        return chunks;
    }
}
```

### Supported Extensions by Processor
- PdfProcessor: .pdf
- DocxProcessor: .docx
- TextProcessor: .txt
- MarkdownProcessor: .md, .markdown
- HtmlProcessor: .html, .htm
- JsonProcessor: .json
- TomlProcessor: .toml
- CodeProcessor: .cs, .js, .ts, .jsx, .tsx, .py, .java, .go, .rs, .cpp, .c, .h

## Definition of Done
- [ ] All 8 processors implemented
- [ ] Chunking works correctly
- [ ] `dotnet build` passes
- [ ] XML documentation complete
