# Story 5.2: Implement Settings Page

## Story Information
**Story ID:** STORY-5.2
**Epic:** EPIC-005 - TechieRagWeb Sample Application
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 5

## Description
Create Settings.razor page with full configuration UI for embedding providers, vector stores, and processing options.

## Acceptance Criteria
- [ ] Settings.razor exists in Components/Pages/
- [ ] Dropdown for embedding source (Ollama, LM Studio, ONNX, Azure OpenAI)
- [ ] Input fields for endpoint, model, API key based on source
- [ ] Dropdown for vector store type (SQLite-vec, PGVector, Qdrant)
- [ ] Input for connection string
- [ ] Inputs for chunk size and overlap
- [ ] Save button persists configuration
- [ ] Success/error feedback displayed

## Technical Requirements

Reference the roadmap for complete Settings.razor implementation:
C:\3AIGenCode\TechieRag\docs\trrag-refactoring-roadmap.md

Key features:
- Two-way binding with config object
- Conditional fields based on embedding source
- Validation of required fields
- TechieRagConfigService for persistence

## Definition of Done
- [ ] Settings page fully functional
- [ ] Configuration saves correctly
- [ ] `dotnet build` passes
