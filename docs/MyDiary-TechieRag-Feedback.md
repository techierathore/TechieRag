# TechieRag Feedback — surfaced during MyDiary

## Summary (filled by /flow-master on consolidation)
- 1 blocker, 0 major, 0 minor, 0 nice-to-have
- Last consolidated: 2026-09-05

## Issues

### TR-RAG-001 — No on-device (offline, no local server) LLM completion provider
- **Severity:** blocker
- **Repro:** `.techierag/TechieRag-AI-Reference.md` v1.0.2 lists exactly six LLM completion providers (`UseOllamaLlm`, `UseLmStudioLlm`, `UseOpenAICompatibleLlm`, `UseAzureAIFoundryLlm`, `UseGeminiLlm`, `UseAnthropicLlm`). Embeddings have a genuine offline path (`TechieRag.Embedded`, `UseEmbedded()`/`UseOnnx(modelPath)`), but no equivalent exists for completions.
- **Expected:** An on-device completion provider (e.g. an ONNX/GGUF/Phi-class runtime embedded the same way `TechieRag.Embedded` embeds an offline embedding model) that needs no local server process and no network call, runnable inside a MAUI app sandbox on Android/iOS/Windows/Mac Catalyst.
- **Actual:** Ollama and LM Studio require a local server process not available inside an iOS/Android app sandbox; the remaining four providers are cloud APIs, which MyDiary's product requirements rule out entirely (brief §B.8: "no cloud inference path at all").
- **Encountered in:** MyDiary Architecture §8 Open questions (Q6), ahead of BRD-100 (narrative On This Day) and BRD-77/78 (Ask's deterministic counting / on-the-fly fact extraction) — `docs/MyDiary-P2-BRD.md`.
- **Workaround:** None yet. The Phase 1 TechieRag on-device feasibility spike (brief §A.4 Priority 2) is the mechanism for resolving this before Phase 2 build starts; if TechieRag does not gain an on-device completion path, MyDiary's narrative and fact-extraction features need a different runtime (evaluated at spike time), never a silent cloud-AI substitution.
- **Suggested fix:** Add an embedded completion provider to TechieRag mirroring `TechieRag.Embedded`'s embedding story — e.g. an ONNX Runtime GenAI or llama.cpp-backed provider bundled the same way, selectable via a `UseEmbeddedLlm(modelPath)`-style call.
