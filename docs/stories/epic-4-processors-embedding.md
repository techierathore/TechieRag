# Epic 4: Document Processors and Embedding Providers

## Epic Overview
**Epic ID:** EPIC-004
**Title:** Document Processors and Embedding Providers
**Status:** Done
**Priority:** P0 - Critical Path

## Description
Implement document processors for various file formats and embedding providers for generating vector embeddings. This enables TechieRag to ingest diverse document types and use different embedding services.

## Business Value
- Support for multiple document formats (PDF, DOCX, TXT, MD, HTML, JSON, TOML, Code)
- Flexible embedding provider options (local: Ollama, LM Studio, ONNX; cloud: Azure OpenAI)
- Enables offline operation with local embedding models

## Stories in this Epic

| Story ID | Title | Status | Points |
|----------|-------|--------|--------|
| STORY-4.1 | Implement Document Processors | Ready | 8 |
| STORY-4.2 | Implement Embedding Providers | Ready | 8 |
| STORY-4.3 | Implement TechieRagClient | Ready | 8 |

## Dependencies
- EPIC-001, EPIC-002, EPIC-003 - Completed
