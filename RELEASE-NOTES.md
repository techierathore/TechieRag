# TechieRag release notes

Paste the section for a release into its GitHub Release when you publish it. All five packages (TechieRag, TechieRag.Embedded, TechieRag.Telemetry, TechieRag.Local, TechieRag.Agents) ship together at one version. The newest release is first.

## Next release after 1.1.2

### Breaking changes

- **`WithPersistence(StoreProvider provider, string defaultUserId)` is removed** (Sevak TR-RAG-049). In 1.1.2 it captured every existing two-argument call `WithPersistence(provider, "Data Source=…")`, so the connection string became a user id and the tables moved to the per-user TechieRag folder. Now one method takes an optional connection string: `WithPersistence(StoreProvider provider, string? connectionString = null, string defaultUserId = "default")`.
  - A two-argument call means a connection string again, as in 1.0.8.
  - To name a user with the per-app default database, name the argument: `WithPersistence(StoreProvider.Sqlite, defaultUserId: "alice")`.
  - Code compiled against 1.1.2 that called the removed method throws `MissingMethodException` until it is rebuilt. A 1.1.2-era source call `WithPersistence(StoreProvider.Sqlite, "alice")` now passes "alice" as a connection string.

### Behaviour, stated plainly

- **`RerankConfig.Enabled` is the default for searches, not the switch that builds a reranker** (since REQ-RAG-047; Sevak TR-RAG-012). It decides whether a search reranks when the call passes no `SearchOptions.Rerank` and no workspace decides. A reranker is built whenever `Rerank.Source` is usable, even with `Enabled = false`. To build no reranker, which avoids the embedded model's download and any API key check, leave `Rerank.Source = None`. The XML documentation on `Enabled` now says this.
- **An unset `VectorStore.ConnectionString` reads the absolute per-app path** (from 1.1.2, REQ-RAG-122). `VectorStoreConfig.IsConnectionStringSet` is now public, so a host can tell "unset" from a value.
- **A local model that cannot fit is refused before its download** (REQ-RAG-125). `LocalLlmProvider.LoadAsync` throws `LocalModelMemoryException` before the terms are asked and before the first byte, then checks again after the download.

### New

- `ModelChooser` lets a small model choose which of several models does a piece of work. `ChooseAsync(request, candidates)` returns one candidate's id, a reason to show and log, and whether the small model chose or the fallback was used. A failure of the small model throws (Chatur TR-RAG-006).
- `LlmProviderFactory.ListModelsAsync(route or connector, apiKey, endpoint?)` lists the models an LM Studio, Ollama or OpenAI-compatible service reports (Lekhak TR-RAG-004).
- `LlmProviderFactory.CreateForModel(…, endpoint: url)` creates a provider for another host without rebuilding the route (Lekhak TR-RAG-005).
- `LocalLlmProvider.IsRuntimeAvailable`, `LocalModel.EstimateRequiredMemoryBytes(contextSize?)`, `AvailableMemory.Read()` and `LocalLlmOptions.DownloadProgress` tell a host before any download whether the local model will run and fit (Sevak TR-RAG-047).
- A release test fails on any public overload pair that gives one call two meanings, which is the defect class behind TR-RAG-049.

The call examples are in the AI reference the package installs (`.techierag/TechieRag-AI-Reference.md`, section "Phase-3 Additions").
