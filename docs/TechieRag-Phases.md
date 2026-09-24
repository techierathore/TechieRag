# TechieRag — Phases

| | |
|---|---|
| App | TechieRag |
| Kind | library |
| Size | Large |
| Date | 2026-09-24 |

## Phases

| Phase | Name | Screens | BRD range | Status |
|---|---|---|---|---|
| 1 | Shipped core library (v1.1 to v3) | Ingestion, Embedding providers, Vector stores, Semantic search, Configuration, Embedded package, LLM providers, RAG generation, Structured output, Agent loop, Conversation memory, Token tracking, Resilience, Fallback provider, Prompt templates, Autodistribution, Packaging, Sevak application (moved) | BRD-1 to BRD-82 | done |
| 2 | Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger | Agents package, Packaging and quality follow-ups, Repository separation, Platform groundwork, Local model, Typed streaming, Subscription sign-in, Ingestion breadth, Data connectors, MCP tools, Flow orchestration, Workspaces and memory, Reranking, Provider breadth | BRD-83 to BRD-165 | building |

Phase 1 is everything shipped and validated before 2026-09-03 (the items with ids 1 to 82, of which 62 to 73, 81 and 82 describe the application that moved to Sevak). Phase 2 holds the 2026-09-03 amendments (agents, repository separation), the 2026-09-24 amendments (four platforms, `TechieRag.Local`, typed streaming, subscription sign-in) and the library features built between July and September 2026 that were ledgered in the application's BRD and harvested on 2026-09-24. A "screen" in this library is a public surface: the builder methods, interface or package a developer reaches for.
