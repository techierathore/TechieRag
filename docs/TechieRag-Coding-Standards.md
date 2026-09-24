# TechieRag — Coding Standards

| | |
|---|---|
| App | TechieRag |
| Stack answer set | dotnet |
| Date | 2026-09-24 |

## Standards applied

| File | Applies | Notes |
|---|---|---|
| `.tfcore/standards/coding-standards-core.md` | yes | every project |
| `.tfcore/standards/coding-standards-dotnet.md` | yes | from the Stack answer set `dotnet`; where it assumes an application (Serilog, AppManager, a UI library) the Architecture's Stack decisions record the library override |

Per-project choices the stack file leaves open, taken from the drift scan of 2026-09-24 (256 of 256 private instance fields, 208 of 208 files) and the June 2026 standards this file replaces (`docs/OldDocs/TechieRag-Coding-Standards.md`):

| Choice | Decision |
|---|---|
| Instance-field prefix | **None.** Bare camelCase, no underscore, no `obj`/`a`/`v` (`private readonly ILlmProvider llmProvider;`). 100 percent of the codebase; constructor assignment uses `this.field = field`. |
| Static and const fields | PascalCase, no prefix (`private const string CachePrefix`). |
| Locals and parameters | camelCase; `var` for locals (about 88 percent today; new code always). |
| Namespaces | File-scoped, one class per file, file name equals type name. |
| Nullable and implicit usings | Enabled in every project; no `#nullable` directives, no `global using` files. |
| Async | Every async method ends in `Async`; library code awaits with `ConfigureAwait(false)` (about 55 percent today; new and touched code always, Architecture open question 9). |
| Test names | Short PascalCase, no underscores (`LoginRejectsBadPassword`); the full scenario in the XML `<summary>`. |
| Public API documentation | XML `<summary>`, `<param>`, `<returns>`, `<exception>` on every public type and member; `GenerateDocumentationFile` is on in every package project. |
| Environment variables the library reads | `TECHIERAG_` prefix, upper snake case (`TECHIERAG_MODEL_BASE_URL`, `TECHIERAG_RERANKER_BASE_URL`): these are the shipped public names and stay. Consumers configure everything else through `IConfiguration`. |
| Database object names | PascalCase, singular, no underscores (`Documents`, `Chunks`, `TrThread`, `IdxChunksDocument`, `IxTrThreadUserId`); library-owned tables created in a consumer's database carry the `Tr` prefix from the persistence stores onward. |

## Project rules

Rules that hold in this project only.

| Rule | Why | Since |
|---|---|---|
| LLM and embedding providers use raw `HttpClient` + `System.Text.Json`; no vendor AI SDK enters `TechieRag` | Keeps the core package light and uniform across vendors (ADR-003) | 2026-06-25 |
| A heavy or fast-moving dependency (ONNX Runtime, OpenTelemetry exporters, Microsoft Agent Framework, a local inference runtime) lives in a sibling package, never in core | Consumers of the core never inherit a monthly-churning dependency (ADR-008, ADR-014) | 2026-09-03 |
| Every change to `ILlmProvider`, `IEmbeddingProvider`, `IVectorStore` or `ITechieRag` is additive with a default interface implementation until a major version is decided | `LlmSource.None` keeps v1 behaviour; external implementers keep compiling (ADR-005, ADR-013) | 2026-06-25 |
| Model-facing text (tool descriptions, agent instructions) is invariant English; user-visible library messages are codes with arguments (`FlowMessage`, `FlowValidationCodes`), never English sentences | The library has no localisation; the host renders in the user's language (REQ-RAG-050) | 2026-08-02 |
| A read against an unreachable store throws; it never returns an empty list | A down database must not masquerade as an empty one (REQ-RAG-044) | 2026-08-04 |
| Every outbound HTTP the library makes on a consumer's behalf goes through the connect-time SSRF guard (`HttpWebContentFetcher.CreateGuardedHandler`) | Redirects and DNS rebinding cannot reach private addresses (BRD-121, BRD-126) | 2026-08 |
| Model weights are downloaded once to the model root and never packed into a NuGet package or an app bundle | Package size and licence terms (ADR-004, BRD-101) | 2026-06-25 |
| A live test is gated by a `FactAttribute` subclass that sets `Skip` with a reason when its credential, server or model is absent; it never fails for a missing environment | The suite stays green on any machine; `TechieRagLiveNetworkTests`, `TechieRagTestPostgres`, staged weights | 2026-07 |
| Download progress goes through `ModelDownloadService` events, never `Console.WriteLine` | A package must not write to a host's console (BRD-92; three legacy calls remain, Architecture open question 10) | 2026-09-24 |
| No `git` or `gh` from an agent; the owner commits | Framework rule, enforced by hook | 2026-06-25 |

## Enforcement

- **Editor configuration:** `.editorconfig` at the repository root carries the machine-checkable subset: file-scoped namespaces (warning), `Async` suffix (warning), `var` for locals (warning), nullable on, no `_` prefix on private fields (warning through the naming rule).
- **Analyzers:** none beyond the SDK's; `GenerateDocumentationFile` makes a missing XML comment a CS1591 warning in every package project.
- **Verifier checks:** the standards check runs the greps listed in the stack file's Enforcement section, plus:

```bash
# forbidden underscore-prefix fields
grep -rE "private(\s+readonly)?\s+\w+\s+_[a-z]" src/ tests/
# forbidden test-method underscores
grep -rE "public\s+(async\s+)?Task\s+\w+_\w+\s*\(" tests/
# a vendor AI SDK in the core project
grep -E "OpenAI|Anthropic|Azure\.AI|Google\.|Microsoft\.Agents|OnnxRuntime|OpenTelemetry" src/TechieRag/TechieRag.csproj | grep -v "Azure.AI.OpenAI"
# console output in a package
grep -rn "Console\.Write" src/
```
