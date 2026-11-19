# TechieRag feedback — found while building MyDiary

| | |
|---|---|
| App | MyDiary |
| Upstream | TechieRag |
| Updated | 2026-11-19 (copy refreshed in the TechieRag repository from MyDiary's `docs/MyDiary-TechieRag-Feedback.md` of 2026-10-25, whose three entries replace the one this copy held; replies added under each entry; the earlier entry and its reply are kept at the end) |

## Summary

Nothing is blocked upstream any more. 3 entries: TR-RAG-001 (exact counts and abstention) fixed upstream on 2026-11-19 for TechieRag 1.3.0, not yet re-checked here; TR-RAG-002 answered without a change (.NET 9 is out of support); TR-RAG-003 (Intel Macs) not planned upstream, by the TechieRag owner's decision of 2026-11-19, so BRD-37 stays unavailable on Intel Macs.

## Entries

### TR-RAG-001 — RAG does not document exact counts or guaranteed abstention — **fixed upstream 2026-11-19, ships in TechieRag 1.3.0 (not yet published), not yet re-checked here**

- **Severity:** blocker
- **Blocks:** yes — implementation of MyDiary Brief item 17's counted, sourced answer and no-match refusal without MyDiary-owned AI logic
- **Repro:** `AskAsync` returns generated `Answer` and retrieved `Sources`; the reference promises neither exact counts nor abstention.
- **Expected:** TechieRag guarantees an exact count, lists its source entries, and abstains when none match.
- **Actual:** retrieved sources do not prove a complete count; the public local model also refuses tool calls.
- **Encountered in:** `docs/MyDiary-Brief.md`, item 17 (Stage 1)
- **Workaround:** none; MyDiary cannot add its own AI code.
- **Suggested fix:** provide a TechieRag-supported count, source-evidence and abstention feature that also works with `TechieRag.Local`.

> **Reply from TechieRag, 2026-11-19 — fixed for 1.3.0 (`TechieRag#REQ-RAG-172`).** `FindMatchesAsync(query, new MatchOptions { MinScore = … })` returns every entry with a chunk at or above the score and counts them, with no model involved: it reads matches best-first until one scores below `MinScore`, so none is left out (`IsComplete` says if the 10,000 cap was hit); `MetadataFilter` scopes it, for example to a month. `AskWithEvidenceAsync` counts first: with no match it **abstains** (`RagResponse.Abstained`, empty answer and sources, no model call); otherwise it answers from the matches with the exact count in the prompt and sets `RagResponse.Matches`, which MyDiary shows as the count and sources. No tool call, so it works on `TechieRag.Local`. `MinScore` is required (for bge-m3 start near 0.5 to 0.6). **Tests:** `REQ-RAG-172 CountIsEveryMatchingEntry` (40 of 100 entries), `AnswerCarriesTheLibrarysCountAndSources`, `NoMatchAbstainsWithoutCallingTheModel`, `ScopeNarrowsTheCount`, and `LiveLocalModelStatesTheLibrarysCount` on Qwen2.5 0.5B. Not on 1.2.0.

### TR-RAG-002 — Local inference packages target only .NET 10

- **Severity:** major
- **Blocks:** no — MyDiary's Architecture selects .NET 10, so its local-model packages are compatible.
- **Repro:** `TechieRag.Local` and `TechieRag.Embedded` 1.1.5 target only `net10.0`; core also targets `net8.0`.
- **Expected:** local inference packages compatible with the app's selected target framework.
- **Actual:** a generic template says .NET 9, which is incompatible with these local packages.
- **Encountered in:** MyDiary Brief item 19 and the Stage 1 stack decision
- **Workaround:** MyDiary targets .NET 10, so its local model can use these packages.
- **Suggested fix:** add a `net9.0` target to both packages if .NET 9 consumers are intended.

> **Reply from TechieRag, 2026-11-19 — not changed, on purpose.** I disagree with adding `net9.0`: .NET 9 reached end of support on 10 November 2026 (the 24-month STS policy put it on the same day as .NET 8), so a `net9.0` build would be one no app should ship on, and no security fix reaches its runtime. MyDiary already targets .NET 10, which both packages support. If a .NET 9 consumer turns up, the answer is to move it to .NET 10. Nothing to re-check; close when you agree. Tracked in TechieRag's 1.3.0 release notes, no requirement row.

### TR-RAG-003 — Local inference does not run on Intel Macs — **not planned upstream: TechieRag owner's decision of 2026-11-19 (REQ-RAG-173)**

- **Severity:** blocker
- **Blocks:** yes — BRD-37's free on-device AI cannot run on Intel Macs; the shared screens and other targets continue.
- **Repro:** the public reference says `LocalLlmProvider` throws `PlatformNotSupportedException` on an Intel Mac; local inference is listed for Apple-silicon macOS.
- **Expected:** the built-in on-device model runs on every Mac covered by the brief.
- **Actual:** the documented local runtime excludes Intel Macs.
- **Encountered in:** `docs/MyDiary-Brief.md`, items 19 and 24
- **Workaround:** none; BRD-37 remains blocked on Intel Macs, and no alternate AI runtime is added to MyDiary.
- **Suggested fix:** support Intel Mac in the local model package or provide an upstream-supported on-device inference path for Intel Macs.

> **Reply from TechieRag, 2026-11-19 — not planned (`TechieRag#REQ-RAG-173`, N/A by the owner's decision).** Neither engine underneath ships an Intel Mac build: ONNX Runtime GenAI 0.16.0 (the built-in model) has none, and ONNX Runtime 1.30.0 (the embedded models) dropped it. Supporting Intel Macs would need a second engine for that platform alone, and on 2026-11-19 the TechieRag owner decided against it. On an Intel Mac a host can use a model on another machine (Ollama, LM Studio) or a hosted one. BRD-37 stays unavailable on Intel Macs; close this entry when you agree.

## Replies from TechieRag

### 2026-11-19 — copy refreshed; the earlier TR-RAG-001 kept here

MyDiary's file of 2026-10-25 reuses the id TR-RAG-001 for a new entry. The entry this copy held under that id, and TechieRag's reply to it, are kept here as the record; it was delivered as the `TechieRag.Local` package.

#### Earlier TR-RAG-001 — No on-device (offline, no local server) LLM completion provider

- **Severity:** blocker
- **Blocks:** yes — MyDiary Phase 2 (BRD-100 narrative On This Day; BRD-77/78 Ask counting and fact extraction)
- **Repro:** `.techierag/TechieRag-AI-Reference.md` v1.0.2 lists exactly six LLM completion providers (`UseOllamaLlm`, `UseLmStudioLlm`, `UseOpenAICompatibleLlm`, `UseAzureAIFoundryLlm`, `UseGeminiLlm`, `UseAnthropicLlm`). Embeddings have a genuine offline path (`TechieRag.Embedded`, `UseEmbedded()`/`UseOnnx(modelPath)`), but no equivalent exists for completions.
- **Expected:** An on-device completion provider (e.g. an ONNX/GGUF/Phi-class runtime embedded the same way `TechieRag.Embedded` embeds an offline embedding model) that needs no local server process and no network call, runnable inside a MAUI app sandbox on Android/iOS/Windows/Mac Catalyst.
- **Actual:** Ollama and LM Studio require a local server process not available inside an iOS/Android app sandbox; the remaining four providers are cloud APIs, which MyDiary's product requirements rule out entirely (brief §B.8: "no cloud inference path at all").
- **Encountered in:** MyDiary Architecture §8 Open questions (Q6), ahead of BRD-100 (narrative On This Day) and BRD-77/78 (Ask's deterministic counting / on-the-fly fact extraction) — `docs/MyDiary-P2-BRD.md`.
- **Workaround:** None yet. The Phase 1 TechieRag on-device feasibility spike (brief §A.4 Priority 2) is the mechanism for resolving this before Phase 2 build starts; if TechieRag does not gain an on-device completion path, MyDiary's narrative and fact-extraction features need a different runtime (evaluated at spike time), never a silent cloud-AI substitution.
- **Suggested fix:** Add an embedded completion provider to TechieRag mirroring `TechieRag.Embedded`'s embedding story — e.g. an ONNX Runtime GenAI or llama.cpp-backed provider bundled the same way, selectable via a `UseEmbeddedLlm(modelPath)`-style call.

#### Reply to the earlier entry

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->

##### 2026-09-24 — TR-RAG-001 accepted

**Accepted, tracked as TechieRag BRD-96 to BRD-109 (feature area F-LOCAL-LLM, a new package `TechieRag.Local`; checklist `REQ-RAG-057` to `REQ-RAG-066` and `REQ-FN-058` to `REQ-FN-061`), with the platform groundwork it needs as BRD-88 to BRD-95 (F-PLATFORM).** The call is `UseLocalLlm()` rather than `UseEmbeddedLlm(modelPath)`, with overloads for a model id or a folder, and a platform-appropriate default model (small on Android and iOS). One public provider, identical on Windows, macOS, Android and iOS; underneath, an internal runtime interface with one implementation per platform, chosen after a measured comparison of llama.cpp (LLamaSharp, GGUF) and ONNX Runtime GenAI on real devices. It needs no server process and no network after a one-time download to the per-user app data folder (writable inside the iOS and Android sandboxes); the download requires the user's acceptance of the model's terms, and weights are never inside the package or your app bundle. Before loading, it checks memory and refuses clearly rather than letting the OS kill MyDiary. `CompleteAsync<T>` and JSON mode return valid JSON for your fact-extraction schemas; tool calling reports unsupported until a test proves it. Your Phase 1 spike becomes the probe app `samples/TechieRag.Probe`: the same two buttons, then inside `MyDiary.Rag` on your phone. MyDiary is the first mobile consumer.

Decision record: `docs/TechieRag-Update-Brief.md` (TechieRag repository), decisions 5 and 7; plan `08-techierag-local-model.md`.
