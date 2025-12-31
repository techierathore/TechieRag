# User Story: Text Ingestion UI Page

## Story ID: TRAG-001

## Title
As a user, I want to ingest raw text content directly into the vector database without needing to save it as a file first.

## Description
Users need the ability to copy-paste text content (such as articles, stories, or other text data fetched from databases) directly into a text area and ingest it into the vector database. This is useful when:
- Text content is fetched from an external database or API
- Users want to quickly test embeddings with sample text
- Content doesn't need to be persisted as a file before embedding

## Acceptance Criteria

### AC1: Text Ingestion Page
- [ ] New Blazor page at `/text-ingestion` route
- [ ] Large text area input for pasting content
- [ ] Document name input field (required)
- [ ] Optional metadata input
- [ ] "Ingest Text" button

### AC2: Functionality
- [ ] Calls existing `IngestTextAsync` method from ITechieRag
- [ ] Shows progress during ingestion
- [ ] Displays success/error messages
- [ ] Shows resulting document ID on success
- [ ] Integrates with model download progress (same as file ingestion)

### AC3: Navigation
- [ ] Link to text ingestion page from main navigation
- [ ] Link back to file ingestion and chat pages

### AC4: Statistics & Management
- [ ] Shows current vector store statistics
- [ ] Lists ingested documents (shared view with file ingestion)
- [ ] Ability to delete individual documents

## Technical Implementation

### Existing Infrastructure (Already Done)
The library already has the `IngestTextAsync` method:
```csharp
Task<string> IngestTextAsync(
    string text,
    string documentName,
    Dictionary<string, object>? metadata = null,
    CancellationToken cancellationToken = default);
```

### New Components Required
1. `TextIngestion.razor` - New Blazor page mimicking `Ingestion.razor` structure
2. Update navigation to include the new page

## Priority
Medium

## Story Points
3

## Status
Completed

## Created
2025-12-31

## Assignee
Dev Agent
