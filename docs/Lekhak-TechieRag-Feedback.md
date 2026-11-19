# TechieRag feedback — found while building Lekhak

| | |
|---|---|
| App | Lekhak |
| Upstream | TechieRag |
| Updated | 2026-11-19 (copy created in the TechieRag repository from Lekhak's `docs/Lekhak-TechieRag-Feedback.md` of 2026-11-17; TR-RAG-011 answered under the entry: fixed for TechieRag 1.3.0) |

## Summary

11 entries: 0 blocking, 0 open, 10 closed, 1 fixed upstream and not yet re-checked here: TR-RAG-011, fixed 2026-11-19 for TechieRag 1.3.0 (`ListModelDetailsAsync`; the reply is under the entry). Lekhak runs TechieRag 1.1.7 (moved 2026-10-18); TR-RAG-008 to TR-RAG-010 were re-checked against Venice on it and closed. TR-RAG-007 closed 2026-10-16. TR-RAG-006 was withdrawn on 2026-10-07. TR-RAG-002 to TR-RAG-005 were re-checked on 1.1.4 against loopback stubs and closed on 2026-10-09.

- 0 blockers, 7 majors, 4 minors, 0 nice-to-haves.
- Last consolidated: 2026-10-27; TR-RAG-011 added 2026-11-17.

<!-- Ids are append-only, never renumbered. TrBlazeUI → TR-NNN · TechieRag → TR-RAG-NNN -->

## Entries

### TR-RAG-001 — TechieRag.Embedded 1.0.2 pulls a vulnerable SQLitePCLRaw (NU1903, high)

> ✅ **Closed 2026-09-26** — re-checked here: 2026-09-26: StoryEngine.csproj now references TechieRag, TechieRag.Embedded and TechieRag.Local at 1.0.8 and the SQLitePCLRaw.lib.e_sqlite3 pin is removed. Ran bash .tfcore/utils/tf-build.sh (rung 4, cmd.exe dotnet build Lekhak.slnx): PASS, zero NU1903/NU190x audit warnings in the log; test StoryEngineReferencesTechieRag108WithoutPin passes (759/759 in tests/Lekhak.Tests).

- **Severity:** major
- **Blocks:** no — a direct package pin in `source/StoryEngine/StoryEngine.csproj` overrides the vulnerable build, and the work carried on.
- **Repro:**
  ```xml
  <PackageReference Include="TechieRag.Embedded" Version="1.0.2" />
  ```
  ```
  dotnet build
  ```
- **Expected:** A clean restore with no NuGet audit warnings; the package resolves a SQLitePCLRaw build outside any published advisory.
- **Actual:** Every consuming project emits, once per target framework:
  ```
  warning NU1903: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high
  severity vulnerability, https://github.com/advisories/GHSA-2m69-gcr7-jv3q
  ```
  The advisory covers `<= 2.1.11`; 1.0.2 resolves exactly 2.1.11. In Lekhak this was 8 of the solution's 16 audit warnings, propagated into StoryEngine, BlogUI, Lekhak and BlogAdmin.
- **Encountered in:** REQ-NFR-017 (BRD-238 — known-vulnerable and beta packages upgraded to supported stable versions)
- **Workaround:** `<PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.12" />` in `StoryEngine.csproj`, flagged with a comment pointing here. A pin in application code that patches a library dependency; it must be removed later.
- **Suggested fix:** Ship a release whose SQLitePCLRaw dependency floats to 2.1.12 or later.

#### Detail

**Fixed upstream (2026-09-26).** TechieRag 1.0.7's package manifest depends on `SQLitePCLRaw.lib.e_sqlite3` 3.50.3, verified in the local NuGet cache at `~/.nuget/packages/techierag/1.0.7/techierag.nuspec`. The pin is removed and this entry is closed when the 1.0.8 upgrade lands (BRD-285, checklist row REQ-RAG-048). Until then the workaround stays in place.

### TR-RAG-002 — Ollama provider reports "stop" for a generation cut off at the token limit

> ✅ **Closed 2026-10-09** — re-checked here: 2026-10-09 re-checked on TechieRag 1.1.4 against a loopback stub in Ollama's /api/chat shape: REQ-RAG-062 OllamaLengthIsTruncated (done_reason length -> Lekhak FinishReason length, IsTruncated true through TechieRagLlmService.ChatAsync) and REQ-RAG-062 OllamaStreamLengthOnCompleted (NDJSON stream -> deltas 'Once upon', single Completed event FinishReason length) both pass (tests/Lekhak.Tests/Services/TechieRagFeedbackRecheckTests.cs); full suite 898/898.

- **Severity:** major
- **Blocks:** no — re-assessed 2026-10-04: Lekhak no longer keeps its own Ollama client (owner decision, ADR-022), so there is no fallback path; it blocks nothing today only because no Lekhak provider is an Ollama route. Lekhak will not work around it.
- **Repro:** (TechieRag 1.0.8, measured 2026-09-26 against a loopback stub answering like Ollama)
  ```csharp
  var vRoute = ModelRouter.Require("ollama/llama3.2");
  vRoute = vRoute with { Connector = vRoute.Connector with { Endpoint = "http://127.0.0.1:48123" } };
  var vLlm = LlmProviderFactory.Create(vRoute, null);
  var vReply = await vLlm.ChatAsync(new[] { ChatMessage.User("hi") });
  // stub body: {"message":{"content":"Hello"},"done":true,"done_reason":"length",...}
  ```
- **Expected:** `vReply.FinishReason == "length"` (and the same on the streamed `Completed` event), as the LM Studio / OpenAI-compatible providers already report.
- **Actual:** `FinishReason == "stop"` from both `ChatAsync` and `ChatStreamEventsAsync`. A truncated story is indistinguishable from a complete one — the defect Lekhak fixed in its own providers under REQ-NFR-041.
- **Encountered in:** REQ-RAG-062 (BRD-313 — TechieRagLlmService maps finish reason onto Lekhak's vocabulary)
- **Workaround:** none inside the `techierag` path; `TechieRagLlmService` maps whatever the library reports. Use the direct `ollama` type where truncation detection matters.
- **Suggested fix:** Map Ollama's `done_reason` onto `LlmResponse.FinishReason` and the `Completed` event's `FinishReason`.

### TR-RAG-003 — Ollama provider sends no num_ctx, so Ollama's small default context window applies

> ✅ **Closed 2026-10-09** — re-checked here: 2026-10-09 re-checked on TechieRag 1.1.4 against a loopback Ollama stub: REQ-RAG-062 OllamaSendsNumCtxWhenGiven passes — CreateForModel('ollama/llama3.2', …, contextTokens: 32768, endpoint: stub) sends options.num_ctx 32768; without contextTokens no num_ctx is sent (tests/Lekhak.Tests/Services/TechieRagFeedbackRecheckTests.cs). Lekhak itself passes no context size today (contextTokens: null in TechieRagLlmService; TechieRag's LlmConfig.MaxContextTokens is not used by Lekhak), so an Ollama row would still get Ollama's default window until the owner decides to pass one; no Lekhak provider is an Ollama route.

- **Severity:** major
- **Blocks:** no — re-assessed 2026-10-04: Lekhak no longer keeps its own Ollama client (owner decision, ADR-022), so there is no fallback path; it blocks nothing today only because no Lekhak provider is an Ollama route. Lekhak will not work around it.
- **Repro:** same setup as TR-RAG-002; captured request body: `{"model":"llama3.2","messages":[…],"stream":false,"options":{"temperature":0.1,"num_predict":50}}`.
- **Expected:** a way to set the context window (`num_ctx`) — from `LlmConfig.MaxContextTokens`, or a factory parameter.
- **Actual:** no `num_ctx` is sent, so Ollama uses its own default (2k–4k tokens) and silently drops the front of a long story prompt.
- **Encountered in:** REQ-RAG-062 (BRD-313)
- **Workaround:** use the direct `ollama` type for long prompts.
- **Suggested fix:** send `options.num_ctx` from the configured context size on Ollama requests.

### TR-RAG-004 — No model-listing API for a route or connector — **fixed upstream 2026-10-08 (1.1.4), re-checked and closed here 2026-10-09**

> ✅ **Closed 2026-10-09** — re-checked here: 2026-10-09 re-checked on TechieRag 1.1.4: TechieRagLlmService.FetchModelIdsAsync now calls LlmProviderFactory.ListModelsAsync(route, key, endpoint) (own HTTP calls removed; NotSupportedException re-thrown with Lekhak's message, HttpRequestException passes through). Loopback-stub tests pass: REQ-RAG-066 REQ-RAG-062 TechieRagListsLmStudioModels (/v1/models, sorted), REQ-UI-136 TechieRagListsOllamaTags (/api/tags), ListingDownEndpointThrowsHttpError, NonListableRouteKeepsLekhakMessage, ListingNoteNamesRowEndpoint (tests/Lekhak.Tests/Services/TechieRagFeedbackRecheckTests.cs); full suite 898/898. Not run against a live LM Studio: none answered GET /v1/models within 5 s at 192.168.1.8, 192.168.1.5, 192.168.1.7 or localhost:1234.

- **Severity:** minor
- **Blocks:** no — Lekhak lists models itself.
- **Repro:** look for a listing member on `ILlmProvider`, `LlmProviderFactory`, `LlmConnectorCatalog` or `ModelRouter` in 1.0.8: there is none.
- **Expected:** e.g. `Task<IReadOnlyList<string>> LlmProviderFactory.ListModelsAsync(ModelRoute route, string? apiKey)` covering LM Studio, Ollama and OpenAI-compatible connectors.
- **Actual:** the host must know each service's listing endpoint.
- **Encountered in:** REQ-UI-136 (BRD-316 — List models for the techierag type)
- **Workaround:** `TechieRagLlmService.FetchModelIdsAsync` calls `GET {endpoint}/v1/models` (LM Studio, OpenAI-compatible) or `GET {endpoint}/api/tags` (Ollama) directly; the provider form says which call it used.
- **Suggested fix:** add the listing call above.

#### Fixed upstream 2026-10-08 (TechieRag)

- **Built as suggested, as `TechieRag#REQ-RAG-126` (Verified 2026-10-08).**
  ```csharp
  Task<IReadOnlyList<string>> LlmProviderFactory.ListModelsAsync(ModelRoute route, string? apiKey, string? endpoint = null, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<string>> LlmProviderFactory.ListModelsAsync(LlmConnectorDescriptor connector, string? apiKey, string? endpoint = null, CancellationToken cancellationToken = default);
  ```
  - LM Studio is asked `GET {endpoint}/v1/models`, Ollama `GET {endpoint}/api/tags`, and every OpenAI-compatible connector `GET {endpoint}/models` (base path kept, key sent as a bearer token). These are the same calls `FetchModelIdsAsync` makes.
  - The ids come back distinct and sorted. Ollama's are the names `ollama/<name>` takes.
  - A server that is down or refuses throws `HttpRequestException` (429 throws `LlmRateLimitException`), never an empty list. Anthropic, Gemini, subscription and in-process connectors throw `NotSupportedException`. A connector that needs a key throws `InvalidOperationException` without one, before any request. The key never appears in a message.
  - **Tests:** `REQ-RAG-126 LmStudioModelsAreListed`, `OllamaModelsAreListed`, `OpenAICompatibleModelsUseBasePathAndKey`, `ListingRefusalsAreExplicit`, `PublicListingReachesGivenEndpoint` (a loopback HTTP server).
  - **Verify from here:** after moving Lekhak to the new package, replace `TechieRagLlmService.FetchModelIdsAsync`'s two HTTP calls with `LlmProviderFactory.ListModelsAsync(route, key, baseUrl)` and run REQ-UI-136's test against LM Studio. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Lekhak --close TR-RAG-004 "<what you ran and what it showed>"`.

### TR-RAG-005 — CreateForModel cannot take an endpoint, so a LAN LM Studio / Ollama host needs a record `with` — **fixed upstream 2026-10-08 (1.1.4), re-checked and closed here 2026-10-09**

> ✅ **Closed 2026-10-09** — re-checked here: 2026-10-09 re-checked on TechieRag 1.1.4: the record with in TechieRagRouteSpec.Parse is gone; the spec keeps the catalog route plus Endpoint, and TechieRagLlmService creates the provider with LlmProviderFactory.CreateForModel(routeText, key, loggerFactory, maxTokens, contextTokens: null, endpoint: rowEndpoint). REQ-RAG-062/063 tests pass against a loopback stub: REQ-RAG-063 REQ-RAG-062 RowEndpointPassedAsArgument (Route.Endpoint stays the catalog default, chat reaches the row's host), TechieRagSendsRouteModelToEndpoint, TechieRagOllamaRouteUsesApiChat, REQ-RAG-065 REQ-RAG-066 FallbackRouteOnWireCompletesCall (@http suffix), REQ-RAG-069 OldTypeConvertsToResolvableRoute; full suite 898/898.

- **Severity:** minor
- **Blocks:** no — the route is rebuilt with the row's endpoint before creating the provider, and the work carried on.
- **Repro:** `LlmProviderFactory.CreateForModel("lmstudio/qwen3", null)` always targets `http://localhost:1234`; there is no endpoint parameter.
- **Expected:** an endpoint override on `CreateForModel`, or `ModelRouter.Require(name, endpoint)`.
- **Actual:** the host resolves the route and rebuilds it: `route with { Connector = route.Connector with { Endpoint = baseUrl } }`, then calls `LlmProviderFactory.Create(route, key)`.
- **Encountered in:** REQ-RAG-062 / REQ-RAG-063 (BRD-313, BRD-314 — endpoint "where the route needs it")
- **Workaround:** the record `with` above, in `TechieRagRouteSpec.Parse`.
- **Suggested fix:** an optional `endpoint` argument on `CreateForModel`.

#### Fixed upstream 2026-10-08 (TechieRag)

- **Built as suggested, as `TechieRag#REQ-RAG-127` (Verified 2026-10-08).** `LlmProviderFactory.CreateForModel(modelName, apiKey, loggerFactory = null, maxTokens = 2048, contextTokens = null, endpoint = null)`.
  - `CreateForModel("lmstudio/qwen3", null, endpoint: baseUrl)` sends to `baseUrl`; the record `with` in `TechieRagRouteSpec.Parse` can go.
  - `endpoint` must be an absolute http or https URL, otherwise `ArgumentException`. The in-process `local/…` model ignores it.
  - Code compiled against the five-argument `CreateForModel` keeps working: that shape is kept, hidden from IntelliSense.
  - **Tests:** `REQ-RAG-127 CreateForModelSendsToGivenEndpoint` (a chat request reaches a loopback server), `EndpointOverrideIsValidatedAndOldCallStillWorks`.
  - **Verify from here:** after moving Lekhak to the new package, drop the `with` in `TechieRagRouteSpec.Parse`, pass the row's Base URL as `endpoint:`, and run the REQ-RAG-062/063 tests. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Lekhak --close TR-RAG-005 "<what you ran and what it showed>"`.

### TR-RAG-006 — No OpenAI-compatible connector for an endpoint that needs no API key

> ✅ **Closed 2026-10-07 — will not fix, withdrawn by the owner.** Lekhak reaches its local models through the keyless `lmstudio` and `ollama` connectors, which already work. A keyless self-hosted server of another kind (vLLM, llama.cpp server, LocalAI) is not a use case for Lekhak, so this is a library limitation Lekhak does not need, not a defect. TechieRag need not act on it.

- **Severity:** major
- **Blocks:** no — re-assessed 2026-10-07: no Lekhak provider is a keyless `generic` row, and the owner does not plan one. (Was: yes, for keyless `generic` rows only.)
- **Repro:**
  ```csharp
  var route = ModelRouter.Require("openai/local-model");
  route = route with { Connector = route.Connector with { Endpoint = "http://10.0.0.5:8000/v1" } };
  LlmProviderFactory.Create(route, null, null, 2048);
  // InvalidOperationException: An API key is required for the 'openai' connector.
  ```
- **Expected:** a route for "any OpenAI-compatible endpoint" (a self-hosted server such as vLLM or llama.cpp) that takes the endpoint from the host and works with no key, as `UseOpenAICompatibleLlm(endpoint, null, model)` does.
- **Actual:** the catalog's only connector that accepts any endpoint is `openai`, and it has `RequiresApiKey = true`; `openai-compatible/<model>` and `custom/<model>` do not resolve. A keyless endpoint can be reached only through `UseOpenAICompatibleLlm`, which is not a route.
- **Encountered in:** REQ-RAG-069 (BRD-328) — migration 042 converts `generic` rows to `openai/<model>` with the row's Base URL as endpoint; that runs when the row has a key and fails with the message above when it does not. Pinned by the test `KeylessGenericRowIsLibraryGap`.
- **Workaround:** none. Lekhak does not send a placeholder key.
- **Suggested fix:** add a catalog connector, e.g. `openai-compatible`, with `Source = OpenAICompatible`, no default endpoint (the host must supply one) and `RequiresApiKey = false`.

### TR-RAG-007 — A failed chat throws HttpRequestException without the provider's error text

> ✅ **Closed 2026-10-15** — re-checked here: 2026-10-16 on TechieRag 1.1.6: tightened TestConnectionWrongModelShowsProviderError to assert the provider text 'Specified model not found: wrong-model' is in the error; ProviderConnectionTestTests 3/3 pass.

- **Severity:** minor
- **Blocks:** no — Lekhak's Test connection still fails on a wrong model and shows the status line ("404 (Not Found)") with the route; only the provider's own explanation is missing.
- **Repro:**
  ```csharp
  // An OpenAI-compatible endpoint answers POST /chat/completions with
  // 404 {"error":{"message":"Specified model not found: wrong-model"}}
  var provider = LlmProviderFactory.CreateForModel("openai/wrong-model", "key", null, 2048, null, endpoint: "http://127.0.0.1:5000/v1");
  await provider.ChatAsync(new[] { ChatMessage.User("Say OK.") }, new LlmCompletionOptions { MaxTokens = 5 });
  // HttpRequestException: "Response status code does not indicate success: 404 (Not Found)." (body discarded)
  ```
- **Expected:** the exception carries the response body (or its `error.message`), in the message or in a typed exception with `StatusCode` and `Body`, as the API rerankers' error type already does.
- **Actual:** the standard `HttpRequestException` from `EnsureSuccessStatusCode`; the body the provider sent is lost.
- **Encountered in:** REQ-UI-141 — owner UAT 11-12 Oct 2026: Test connection now sends one short chat; a wrong Venice model name should say why it failed. Pinned by `TestConnectionWrongModelShowsProviderError` (`tests/Lekhak.Tests/Services/ProviderConnectionTestTests.cs`).
- **Workaround:** none. Lekhak shows the library's message and the route; it does not call the provider itself.
- **Suggested fix:** read the body on a non-success chat or stream response and include it (trimmed) in the thrown exception.
- **Re-checked 2026-10-15:** NuGet's latest TechieRag, TechieRag.Embedded and TechieRag.Local are still 1.1.5, the version this entry was filed against, and no reply names it; still open, still does not block.

#### Fixed upstream 2026-10-15 (TechieRag)

- **Built as suggested, as `TechieRag#REQ-RAG-134` (Verified 2026-10-15).** It ships in the next TechieRag package after 1.1.5.
  ```csharp
  // namespace TechieRag.Models
  public class LlmHttpException : HttpRequestException
  {
      public const int MaxBodyLength = 4000;
      public string? ResponseBody { get; }      // what the service sent, cut to 4000 characters
      public string? ProviderMessage { get; }   // the service's own explanation
      // StatusCode is the HttpRequestException property
  }
  public class LlmRateLimitException : LlmHttpException { public TimeSpan? RetryAfter { get; } }
  ```
  - Every LLM provider (`ChatAsync` and the streamed calls), `LlmProviderFactory.ListModelsAsync` and the OpenAI-compatible speech clients throw it on a non-success status. A stream throws before its first event.
  - `ProviderMessage` is read from `error.message` (OpenAI-compatible services such as Venice, Anthropic, Gemini), from `error` as text (Ollama, LM Studio), from `message` or from `detail`. It is null when the body has none of these.
  - `Message` keeps the old status line and adds the provider's text. Your repro now gives: `Response status code does not indicate success: 404 (Not Found). Provider message: Specified model not found: wrong-model`. A plain-text body is added as ` Response body: …`. An HTML error page adds nothing. The added text is cut to 500 characters.
  - It is still an `HttpRequestException`, so `catch (HttpRequestException)` keeps working. A check for the exact type (`Assert.ThrowsAsync<HttpRequestException>`, `GetType() ==`) must accept the subtype.
  - Not covered: rerankers and embedding providers still throw the plain `HttpRequestException`. TechieRag has no reranker error type that carries the body today; the entry's "as the API rerankers' error type already does" does not match TechieRag's code.
  - **Tests:** `REQ-RAG-134 FailedChatCarriesProviderMessage` (this entry's repro against a loopback server), `FailedStreamCarriesProviderMessage` (Ollama), `RateLimitAndListingKeepProviderMessage`, `ProviderMessageShapesAreRead`.
- **How to use it in Lekhak** (Test connection, REQ-UI-141):
  ```csharp
  try
  {
      var provider = LlmProviderFactory.CreateForModel(route, key, null, 2048, null, endpoint: baseUrl);
      await provider.ChatAsync([ChatMessage.User("Say OK.")], new LlmCompletionOptions { MaxTokens = 5 });
      return ConnectionResult.Ok();
  }
  catch (LlmHttpException ex)
  {
      // Show the service's reason; fall back to the status line when it sent none.
      var reason = ex.ProviderMessage ?? ex.Message;
      return ConnectionResult.Failed($"{(int?)ex.StatusCode} — {reason} ({route})");
  }
  catch (HttpRequestException ex)   // the server could not be reached at all: no status, no body
  {
      return ConnectionResult.Failed($"{ex.Message} ({route})");
  }
  ```
  `ex.ResponseBody` holds the raw body if you want to log it. Keep the key out of the UI; TechieRag never puts it in a message.
- **Verify from here:** after moving Lekhak to the new package, run `TestConnectionWrongModelShowsProviderError` (`tests/Lekhak.Tests/Services/ProviderConnectionTestTests.cs`) and expect the Test connection text to contain `Specified model not found: wrong-model`. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Lekhak --close TR-RAG-007 "<what you ran and what it showed>"`.

## Replies from TechieRag

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->

### 2026-10-15 — TR-RAG-007 fixed

TR-RAG-007 → `TechieRag#REQ-RAG-134` (BRD-194), Verified on 2026-10-15. It ships in the next TechieRag package after 1.1.5. A failed chat, stream, model listing or speech call now throws `LlmHttpException`. It is still an `HttpRequestException`, and it carries `StatusCode`, `ResponseBody` and `ProviderMessage`. Its message keeps the old status line and adds the provider's text. The members, the tests, a Test connection example and the close step are in the entry. The installed `/techierag` agent files and the AI reference (section "Phase-3 Additions", "Why a call failed: `LlmHttpException`") describe it, and `RELEASE-NOTES.md` in the TechieRag repository lists it. Rerankers and embedding providers are not covered and still throw the plain `HttpRequestException`. With this, every open Lekhak entry against TechieRag is answered.

### 2026-10-08 — TR-RAG-004 and TR-RAG-005 fixed

TR-RAG-004 → `TechieRag#REQ-RAG-126` and TR-RAG-005 → `TechieRag#REQ-RAG-127`, both Verified on 2026-10-08. They ship in the next TechieRag package after 1.1.2. `LlmProviderFactory.ListModelsAsync` lists an LM Studio, Ollama or OpenAI-compatible service's models, and `CreateForModel(…, endpoint:)` reaches another host without rebuilding the route. The members, the tests and the close steps are in each entry. The installed `/techierag` agent files and the AI reference (section "Phase-3 Additions", "Model listing and endpoints") describe both, and `RELEASE-NOTES.md` in the TechieRag repository lists them. With these two, every open Lekhak entry against TechieRag is answered.

### Resolution status (TechieRag, 2026-10-04)

TR-RAG-002 and TR-RAG-003 are fixed in TechieRag source and ship in the next package after 1.0.8. TechieRag tracks them as REQ-RAG-112 / BRD-171 and REQ-RAG-113 / BRD-172. Both were Verified by TechieRag's verifier on 2026-10-04: five tests (`OllamaRequestOptionsTests`) pass against a stub answering in Ollama's `/api/chat` shape. The TechieRag agent guide (`.claude/commands/techierag.md`, deployed on build) and `TechieRag-AI-Reference.md` describe both features from the next package on.

How to use them in Lekhak once the package is adopted:

```csharp
var vRoute = ModelRouter.Require("ollama/llama3.2");
vRoute = vRoute with { Connector = vRoute.Connector with { Endpoint = vBaseUrl } };
var vLlm = LlmProviderFactory.Create(vRoute, null, null, 2048, contextTokens: 32768);   // sends options.num_ctx
var vReply = await vLlm.ChatAsync(vMessages);
// vReply.FinishReason == "length" when the story was cut off; the streamed Completed event says the same
```

TR-RAG-004, TR-RAG-005 and TR-RAG-006 are still open; nothing in this release touches them.

- **TR-RAG-002 — fixed.** `OllamaLlmProvider` reads Ollama's `done_reason`. A reply cut off at the token limit now reports `FinishReason == "length"` from `ChatAsync`, and the same on the streamed `Completed` event. Tool calls still report `tool_calls`; everything else reports `stop`.
  - *Verify from here:* repeat the entry's repro with the stub body `{"message":{"content":"Hello"},"done":true,"done_reason":"length"}`. Expect `vReply.FinishReason == "length"`.
- **TR-RAG-003 — fixed.** The Ollama provider sends `options.num_ctx` when a context size is given. On the route path there is a new optional argument: `LlmProviderFactory.Create(route, apiKey, loggerFactory, maxTokens, contextTokens)`, and the same on `CreateForModel`. On the builder path it comes from `LlmConfig.MaxContextTokens`.
  - With no size given, no `num_ctx` is sent and Ollama keeps its own default. This is deliberate: Ollama allocates the whole window up front, and an unasked-for 128k window can stop a local model from loading.
  - **Breaking change:** `LlmConfig.MaxContextTokens` is now `int?` with default null, not `int` with default 128000. Code that reads it into an `int` needs `?? <value>`. It was not read anywhere in the library before this change.
  - *Verify from here:* `LlmProviderFactory.Create(vRoute, null, contextTokens: 32768)` against the same stub. The captured body should contain `"options":{…,"num_ctx":32768}`.

### TR-RAG-008 — A streamed reply's completion-token count is far below the real count (Venice, TechieRag 1.1.6)

> ✅ **Closed 2026-10-18** — fixed in TechieRag 1.1.7. OpenAI-compatible providers now send `stream_options.include_usage`; the final stream chunk's usage is reported on `LlmStreamEvent.Completed.Usage` and marked `IsEstimated = false`. When no usage chunk arrives, the count is estimated and `IsEstimated = true`.
>
> **Re-checked here 2026-10-18:** Lekhak now runs TechieRag 1.1.7; the package's AI reference describes `stream_options.include_usage` and `Completed.Usage.IsEstimated`, and the full unit suite passes (1,137 of 1,137). A live Venice streamed count was not measured in this run.

- **Severity:** major
- **Blocks:** no — Lekhak marks every log row's token count "reported" or "estimated" (REQ-FN-158) and judges cut-offs by finish reason, not by the count. Waits for the weekly library night.
- **Repro:** (TechieRag 1.1.6, route `venice/venice-uncensored-1-2`, streaming on, 16 Oct 2026, session 19 in TkStoriesDb)
  ```csharp
  // StoryElementExtractionService: summary and character-update calls
  var vOptions = new LlmOptions { MaxTokens = AgentTokenBudget.ForExtraction(8) }; // 1,500 or more
  await llm.ChatAsync(system, user, vOptions, ct);   // streamed
  ```
- **Expected:** A reply that ends with finish reason `length` reports completion tokens equal to the requested budget (the outliner on `qwen-3-8-27b` does: 4,000 of 4,000).
- **Actual:** Five calls ended with finish reason `length` and reported 182, 324, 328, 368 and 468 completion tokens against budgets of at least 1,500 (`blogadmin-20261016_00*.log`, 01:54–01:57 IST). Session 51's writer rows report 5,967 completion tokens for 8,338 Hindi words, 0.7 tokens per word. Lekhak cannot tell whether Venice sends the low figure or the stream's `Completed` usage is computed in TechieRag.
- **Encountered in:** REQ-FN-158 (BRD-378) and REQ-RAG-081 (BRD-374), Lekhak phase 3
- **Workaround:** none in code; the count is shown and marked, and decisions use the finish reason.
- **Suggested fix:** Say where `LlmStreamEvent.Completed.Usage` comes from on OpenAI-compatible routes; request `stream_options.include_usage` where the provider supports it, and mark the usage as estimated when the provider sent none.

### TR-RAG-009 — A reasoning model's reasoning is billed as completion tokens, never returned, and cannot be bounded (Venice, TechieRag 1.1.6)

> ✅ **Closed 2026-10-18** — re-checked here: 2026-10-18 re-checked on TechieRag 1.1.7 (GitHub feed; nuget.org stops at 1.1.5) with a throwaway console probe against Venice, route venice/qwen-3-6-plus, the entry's repro prompt: thinking on -> finish stop, out 290, Usage.ReasoningTokens 276, IsEstimated false; DisableReasoning=true -> out 13, reasoning 0, full answer; MaxReasoningTokens=64 -> reasoning 0. Reasoning is now reported apart from the answer and can be switched off or capped per call. Lekhak still runs 1.1.6 and passes none of these options; adopting them is Lekhak work, and 1.1.7 needs Npgsql >= 10.0.1 where BlogDa pins 9.0.2 (NU1605).

- **Severity:** major
- **Blocks:** no — Lekhak now measures the gap itself (completion tokens reported minus the tokens its returned text accounts for), adds it as headroom to the agent's next ceiling within the row's Max Tokens, and asks a reply that came back empty at "length" once more (fix-1, 2026-10-17). The owner can also switch thinking off on the provider row's route (`venice/qwen-3-6-plus:disable_thinking=true`); Lekhak's code names no provider.
- **Repro:** (TechieRag 1.1.6, route `venice/qwen-3-6-plus`, 17 Oct 2026, sessions 63, 68, 70, 72, 74 and 79 in TkStoriesDb)
  ```csharp
  var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", key, maxTokens: 8192);
  var reply = await llm.ChatAsync(messages, new LlmCompletionOptions { MaxTokens = 2800 });
  // reply.FinishReason == "length", reply.Usage.OutputTokens == 2802, reply.Content is (almost) empty
  ```
  The raw Venice reply to a 22-token prompt ("Count from 1 to 5") carries `message.reasoning_content` (476 characters) beside `message.content` (13 characters), and `usage.completion_tokens_details.reasoning_tokens: 173` of `completion_tokens: 191`. `qwen-3-8-27b` (the outliner row) does the same (368 of 374). `venice-uncensored-1-2`, `venice-uncensored-role-play` and `gemma-4-uncensored` return no reasoning.
- **Expected:** `LlmResponse` says how much of the output was reasoning (e.g. `Usage.ReasoningTokens` from `completion_tokens_details.reasoning_tokens`, and optionally the reasoning text), and `LlmCompletionOptions` has a provider-neutral way to bound or switch reasoning off (OpenAI-style `reasoning_effort`, a reasoning-token budget, or Venice's `venice_parameters.disable_thinking`), mapped per connector.
- **Actual:** `LlmResponse` has `Content` only; `Usage.OutputTokens` includes the reasoning. On session 68 every writer call used its whole ceiling (2,802 of 2,800, finish "length") and the 5,000-word Extend reached human review with 63 words. On session 79 one Hindi section's reasoning took more than 7,600 tokens. The reference lists `disable_thinking` and `strip_thinking_response` as "not exposed as options".
- **Encountered in:** REQ-RAG-081, REQ-RAG-088 (BRD-374), Lekhak phase 3 fix cycle 1
- **Workaround:** Lekhak measures the hidden output by token arithmetic (`AgentTokenBudget.MeasureHiddenOutput`) and raises the next ceiling; the owner may put the suffix on the row's route. Neither bounds the reasoning itself.
- **Suggested fix:** Read `completion_tokens_details.reasoning_tokens` (OpenAI-compatible routes) into `TokenUsage`, expose the reasoning text separately, and add a `ReasoningEffort` / `MaxReasoningTokens` / `DisableReasoning` option on `LlmCompletionOptions` that each connector maps to its own field.

### TR-RAG-010 — A `CreateForModel` provider cuts every call at 120 s, whatever the host allows (TechieRag 1.1.6)

> ✅ **Closed 2026-10-18** — re-checked here: 2026-10-18 re-checked on TechieRag 1.1.7 with the same probe: CreateForModel and LlmConfig now take a request timeout and LlmCompletionOptions.RequestTimeout overrides the 120 s default per call. RequestTimeout=10s on a long Hindi call -> cancelled at 10.0 s (the per-call value is applied, not the fixed 120 s); RequestTimeout=300s -> finished in 85.2 s, stop, 4,908 tokens (1,254 reasoning). Not reached: a call that actually runs past 120 s. The cut now surfaces as TaskCanceledException 'The operation was canceled.' rather than 'The request timed out.', which Lekhak's REQ-FN-160 text must be re-checked against after the upgrade.
>
> **Re-checked here 2026-10-18 after the upgrade:** `TechieRagLlmService` passes the row's Timeout to `CreateForModel`/`CreateSubscription` (`requestTimeout:`) and on every call (`LlmCompletionOptions.RequestTimeout`). `TechieRagRequestTimeoutTests`: a 300 s row sends 300 s; a 1 s row against an endpoint answering after 5 s fails in under 4 s reading "The request timed out." on the chat path, and throws `TimeoutException` with the same text on the streamed path; a caller's cancel stays a cancellation. 1,137 of 1,137 unit tests pass.

- **Severity:** major
- **Blocks:** no — runs on a model that does not reason finish well inside 120 s (session 80: two 1,000-word Hindi sections written in 39 s and 36 s). It bites only when a reasoning model (TR-RAG-009) is given the headroom it needs.
- **Repro:** (TechieRag 1.1.6, route `venice/qwen-3-6-plus`, provider row TimeoutSeconds 300, 17 Oct 2026, session 79)
  ```csharp
  var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", key, maxTokens: 8192);
  using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));   // Lekhak's row timeout
  await llm.ChatAsync(messages, new LlmCompletionOptions { MaxTokens = 7602 }, cts.Token);
  ```
- **Expected:** The call runs until the host's token cancels it (300 s here), or `CreateForModel` / `LlmConfig` takes a request timeout.
- **Actual:** Two writer calls failed with "The request timed out." at 120,046 ms and 120,018 ms (AgentExecutionLog, session 79), well before the host's 300 s. `CreateForModel` has no timeout argument and `LlmConfig` has no timeout property; the 120 s matches `ResilienceConfig.TimeoutSeconds`' default, which the route path does not let the caller set.
- **Encountered in:** REQ-RAG-081 (BRD-374), Lekhak phase 3 fix cycle 1
- **Workaround:** none in code; the run fails naming the section and "The request timed out." (fix-1). A model without reasoning stays under the limit.
- **Suggested fix:** Add `timeout` (or `TimeSpan? requestTimeout`) to `CreateForModel` / `Create` and to `LlmConfig`, applied to the provider's HttpClient or resilience pipeline; or honour the caller's cancellation token alone when no timeout is given.

### TR-RAG-011 — The model list gives ids only: no price, no word on who sees the text, no word on reasoning (TechieRag 1.1.7) — **fixed upstream 2026-11-19, ships in TechieRag 1.3.0 (not yet published), not yet re-checked here**

- **Severity:** minor
- **Blocks:** no — Lekhak's Model test (phase 5, BRD-447 and BRD-449) takes a typed price per model and shows "Not known" for who sees the text until the library reports them.
- **Repro:** (TechieRag 1.1.7, any route; seen on 17 Nov 2026 with the default provider row)
  ```csharp
  IReadOnlyList<string> ids = await LlmProviderFactory.ListModelsAsync(ModelRouter.Require("venice/x"), key);
  // 153 ids; nothing else about any of them
  ```
- **Expected:** For each model the provider describes, the list also carries what the provider itself publishes: the price per million tokens in and out, whether the provider runs the model itself or passes the text on to another company, and whether the model reasons before it answers. A provider that publishes none of these returns them empty.
- **Actual:** `ListModelsAsync` returns `IReadOnlyList<string>`. A host that wants a cost estimate before a run, or wants to warn that a text leaves the provider, has to know each provider's own model page, which Lekhak must not (it holds no knowledge of any one provider).
- **Encountered in:** Lekhak phase 5, BRD-447, BRD-449, BRD-439 (amend-docs, 2026-11-17); the 17 Nov hand-built run read prices from the provider's public page instead.
- **Workaround:** the Admin types the price once per model; the label reads "Not known" and needs a tick before a test starts; the reasoning setting is offered for every model and the reasoning tokens each call reports show whether it took effect.
- **Suggested fix:** Add `ListModelDetailsAsync(route or connector, apiKey, endpoint?)` returning, per model, `Id`, `InputPricePerMillionUsd`, `OutputPricePerMillionUsd`, a data-handling value (runs it itself, passes it on, local, not known) and `SupportsReasoning`, each empty when the provider says nothing.

> **Reply from TechieRag, 2026-11-19 — fixed for 1.3.0 (`TechieRag#REQ-RAG-166`).**
>
> `LlmProviderFactory.ListModelDetailsAsync(route or connector, apiKey, endpoint?)` (namespace `TechieRag.Llm`), the call the entry suggests, returns one `LlmModelDetails` per model: `Id`, `InputPricePerMillionUsd`, `OutputPricePerMillionUsd` (US dollars per million tokens, `decimal?`), `DataHandling` (`LlmModelDataHandling.NotKnown`, `RunsItself`, `PassesOn`, `Local`) and `SupportsReasoning` (`bool?`). Each is empty (`null`, or `NotKnown`) when the service says nothing; the library keeps no price table for this list, so every figure is the service's own. It makes the same one request as `ListModelsAsync` and returns exactly the same ids.
>
> What each service says, read from their public model lists on 2026-11-19:
>
> | Service | Price | Who runs it | Reasoning |
> |---|---|---|---|
> | Venice | `model_spec.pricing.input.usd` / `output.usd`, per million | `privacy`: `private` → `RunsItself`, `anonymized` → `PassesOn` (148 text models: 77 private, 71 anonymized) | `capabilities.supportsReasoning` |
> | OpenRouter | `pricing.prompt` / `completion`, per token, scaled to a million; `-1` ("varies") is empty | not published: `NotKnown` | `supported_parameters` names `reasoning` |
> | LM Studio, Ollama | none | `Local` | none |
> | OpenAI, Anthropic, every other service | none | `NotKnown` | none |
>
> The shapes are recognised by their fields, not by the service's name, so Lekhak still names no provider.
>
> **Proving tests:** `REQ-RAG-166 VeniceModelsCarryPriceDataHandlingAndReasoning`, `REQ-RAG-166 OpenRouterPerTokenPricesAreScaledToAMillion`, `REQ-RAG-166 SilentServiceLeavesEveryValueEmpty` and `REQ-RAG-166 PublicCallReturnsTheSameIdsWithDetails`, on bodies cut from the services' own lists. On 1.2.0 the call does not exist (the entry's `IReadOnlyList<string>` is all there is), so the tests do not compile there; they pass now.
>
> **Close step:** on 1.3.0, list the default row's models with `ListModelDetailsAsync`; a Venice model shows its price and "passes the text on" or "runs it itself" without the Admin typing either.
