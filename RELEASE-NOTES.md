# TechieRag release notes

Paste the section for a release into its GitHub Release when you publish it. All five packages (TechieRag, TechieRag.Embedded, TechieRag.Telemetry, TechieRag.Local, TechieRag.Agents) ship together at one version. The newest release is first.

## Next release after 1.1.5

### Behaviour, stated plainly

- **A failed LLM call now says why** (REQ-RAG-134, Lekhak TR-RAG-007). Chats, streams, `ListModelsAsync` and the OpenAI-compatible speech calls throw the new `LlmHttpException` on a non-success status. It is still an `HttpRequestException`. It carries `StatusCode`, `ResponseBody` (cut to 4000 characters) and `ProviderMessage` (the service's own `error.message`, `error`, `message` or `detail`). Its `Message` keeps the old status line and adds ` Provider message: …`, for example `Response status code does not indicate success: 404 (Not Found). Provider message: Specified model not found: wrong-model`. `LlmRateLimitException` now derives from it. Code that checks for the exact type `HttpRequestException` (`ex.GetType() == typeof(HttpRequestException)`, or xUnit's `Assert.ThrowsAsync<HttpRequestException>`) needs to accept the subtype; `catch (HttpRequestException)` is unaffected.

The call example is in the AI reference the package installs (`.techierag/TechieRag-AI-Reference.md`, section "Why a call failed: `LlmHttpException`").

## Next release after 1.1.2

### Breaking changes

- **`WithPersistence(StoreProvider provider, string defaultUserId)` is removed** (Sevak TR-RAG-049). In 1.1.2 it captured every existing two-argument call `WithPersistence(provider, "Data Source=…")`, so the connection string became a user id and the tables moved to the per-user TechieRag folder. Now one method takes an optional connection string: `WithPersistence(StoreProvider provider, string? connectionString = null, string defaultUserId = "default")`.
  - A two-argument call means a connection string again, as in 1.0.8.
  - To name a user with the per-app default database, name the argument: `WithPersistence(StoreProvider.Sqlite, defaultUserId: "alice")`.
  - Code compiled against 1.1.2 that called the removed method throws `MissingMethodException` until it is rebuilt. A 1.1.2-era source call `WithPersistence(StoreProvider.Sqlite, "alice")` now passes "alice" as a connection string.

### Behaviour, stated plainly

- **`RerankConfig.Enabled` is the default for searches, not the switch that builds a reranker** (since REQ-RAG-047; Sevak TR-RAG-012). It decides whether a search reranks when the call passes no `SearchOptions.Rerank` and no workspace decides. A reranker is built whenever `Rerank.Source` is usable, even with `Enabled = false`. To build no reranker, which avoids the embedded model's download and any API key check, leave `Rerank.Source = None`. The XML documentation on `Enabled` now says this.
- **An unset `VectorStore.ConnectionString` reads the absolute per-app path** (from 1.1.2, REQ-RAG-122). `VectorStoreConfig.IsConnectionStringSet` is now public, so a host can tell "unset" from a value.
- **Disposing a local model while it answers no longer crashes the app** (REQ-RAG-129, Sevak TR-RAG-053). `LocalLlmProvider.Dispose` cancels the calls inside; each ends with `ObjectDisposedException`, and the native model is freed after the last has left. The new `DisposeAsync` completes once the memory is freed. `EstimateTokenCount` after a dispose now throws `ObjectDisposedException` instead of using the freed tokenizer.
- **The Anthropic provider keeps its endpoint's path** (REQ-RAG-133). It now posts to `{endpoint}/v1/messages`, or to `{endpoint}/messages` when the endpoint already ends in `/v1`. Before this, `https://api.z.ai/api/anthropic` went to `https://api.z.ai/v1/messages`. Endpoints with no path, such as Anthropic's own, behave as before.
- **`ListModelsAsync` now lists Anthropic models** through `GET /v1/models` with `x-api-key`, where it used to throw `NotSupportedException` (REQ-RAG-130).
- **A local model that cannot fit is refused before its download** (REQ-RAG-125). `LocalLlmProvider.LoadAsync` throws `LocalModelMemoryException` before the terms are asked and before the first byte, then checks again after the download.

### New

- Two generic routes reach any service without a release. `openai-compatible/<model>` uses the chat/completions format and `anthropic-compatible/<model>` uses the Anthropic Messages format, for example GLM at `https://api.z.ai/api/anthropic`. The endpoint comes from the app's Base URL (`CreateForModel(…, endpoint:)`, the new `UseLlmForModel(route, key, endpoint)`) or from an `@https://…` suffix on the route, which works on any route and wins. Both need an API key. Without an endpoint or a key, the call is refused before any request; the endpoint message names both ways to give one (REQ-RAG-130).
- `venice/<model>` reaches Venice AI at `https://api.venice.ai/api/v1` with Venice's own system prompt switched off (`venice_parameters.include_venice_system_prompt: false`). Tool calls and streaming work. For Venice's other switches, add a suffix to the model id (REQ-RAG-131).
- `LlmConnectorCatalog.Describe()` gives a settings page one row per connector: display name, whether it needs a key, an endpoint or a sign-in, local or hosted, the default endpoint and an example route (REQ-RAG-132).
- `ModelChooser` lets a small model choose which of several models does a piece of work. `ChooseAsync(request, candidates)` returns one candidate's id, a reason to show and log, and whether the small model chose or the fallback was used. A failure of the small model throws (Chatur TR-RAG-006).
- `LlmProviderFactory.ListModelsAsync(route or connector, apiKey, endpoint?)` lists the models an LM Studio, Ollama or OpenAI-compatible service reports (Lekhak TR-RAG-004).
- `LlmProviderFactory.CreateForModel(…, endpoint: url)` creates a provider for another host without rebuilding the route (Lekhak TR-RAG-005).
- `LocalLlmProvider.IsRuntimeAvailable`, `LocalModel.EstimateRequiredMemoryBytes(contextSize?)`, `AvailableMemory.Read()` and `LocalLlmOptions.DownloadProgress` tell a host before any download whether the local model will run and fit (Sevak TR-RAG-047).
- A release test fails on any public overload pair that gives one call two meanings, which is the defect class behind TR-RAG-049.

The call examples are in the AI reference the package installs (`.techierag/TechieRag-AI-Reference.md`, section "Phase-3 Additions").
