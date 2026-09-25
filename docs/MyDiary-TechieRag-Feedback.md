# TechieRag feedback — found while building MyDiary

| | |
|---|---|
| App | MyDiary |
| Upstream | TechieRag |
| Updated | 2026-09-24 |

## Summary

1 entry: 1 blocking now, 0 filed and not blocking, 0 fixed upstream.

TR-RAG-001 (no on-device completion provider) blocks MyDiary Phase 2. Accepted upstream on 2026-09-24 as TechieRag BRD-96…109 (`TechieRag.Local`); it stays blocking until that package ships. Last consolidated 2026-09-05; reply added 2026-09-24.

## Entries

### TR-RAG-001 — No on-device (offline, no local server) LLM completion provider

- **Severity:** blocker
- **Blocks:** yes — MyDiary Phase 2 (BRD-100 narrative On This Day; BRD-77/78 Ask counting and fact extraction)
- **Repro:** `.techierag/TechieRag-AI-Reference.md` v1.0.2 lists exactly six LLM completion providers (`UseOllamaLlm`, `UseLmStudioLlm`, `UseOpenAICompatibleLlm`, `UseAzureAIFoundryLlm`, `UseGeminiLlm`, `UseAnthropicLlm`). Embeddings have a genuine offline path (`TechieRag.Embedded`, `UseEmbedded()`/`UseOnnx(modelPath)`), but no equivalent exists for completions.
- **Expected:** An on-device completion provider (e.g. an ONNX/GGUF/Phi-class runtime embedded the same way `TechieRag.Embedded` embeds an offline embedding model) that needs no local server process and no network call, runnable inside a MAUI app sandbox on Android/iOS/Windows/Mac Catalyst.
- **Actual:** Ollama and LM Studio require a local server process not available inside an iOS/Android app sandbox; the remaining four providers are cloud APIs, which MyDiary's product requirements rule out entirely (brief §B.8: "no cloud inference path at all").
- **Encountered in:** MyDiary Architecture §8 Open questions (Q6), ahead of BRD-100 (narrative On This Day) and BRD-77/78 (Ask's deterministic counting / on-the-fly fact extraction) — `docs/MyDiary-P2-BRD.md`.
- **Workaround:** None yet. The Phase 1 TechieRag on-device feasibility spike (brief §A.4 Priority 2) is the mechanism for resolving this before Phase 2 build starts; if TechieRag does not gain an on-device completion path, MyDiary's narrative and fact-extraction features need a different runtime (evaluated at spike time), never a silent cloud-AI substitution.
- **Suggested fix:** Add an embedded completion provider to TechieRag mirroring `TechieRag.Embedded`'s embedding story — e.g. an ONNX Runtime GenAI or llama.cpp-backed provider bundled the same way, selectable via a `UseEmbeddedLlm(modelPath)`-style call.

## Replies from TechieRag

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->

### 2026-09-24 — TR-RAG-001 accepted

**Accepted, tracked as TechieRag BRD-96 to BRD-109 (feature area F-LOCAL-LLM, a new package `TechieRag.Local`; checklist `REQ-RAG-057` to `REQ-RAG-066` and `REQ-FN-058` to `REQ-FN-061`), with the platform groundwork it needs as BRD-88 to BRD-95 (F-PLATFORM).** The call is `UseLocalLlm()` rather than `UseEmbeddedLlm(modelPath)`, with overloads for a model id or a folder, and a platform-appropriate default model (small on Android and iOS). One public provider, identical on Windows, macOS, Android and iOS; underneath, an internal runtime interface with one implementation per platform, chosen after a measured comparison of llama.cpp (LLamaSharp, GGUF) and ONNX Runtime GenAI on real devices. It needs no server process and no network after a one-time download to the per-user app data folder (writable inside the iOS and Android sandboxes); the download requires the user's acceptance of the model's terms, and weights are never inside the package or your app bundle. Before loading, it checks memory and refuses clearly rather than letting the OS kill MyDiary. `CompleteAsync<T>` and JSON mode return valid JSON for your fact-extraction schemas; tool calling reports unsupported until a test proves it. Your Phase 1 spike becomes the probe app `samples/TechieRag.Probe`: the same two buttons, then inside `MyDiary.Rag` on your phone. MyDiary is the first mobile consumer.

Decision record: `docs/TechieRag-Update-Brief.md` (TechieRag repository), decisions 5 and 7; plan `08-techierag-local-model.md`.
