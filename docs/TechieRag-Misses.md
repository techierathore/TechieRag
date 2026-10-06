# TechieRag — Misses

| | |
|---|---|
| App | TechieRag |
| Count | 20 logged: 1 open, 19 fixed, 0 will not fix |
| Source | `docs/metrics/misses.jsonl`, one row per miss record. Rewritten by `tf-misses-md.sh` on every new record. Never edit it: a wrong row is corrected by a new record. |
| Updated | 2026-10-06 |

**Whose gap** answers the four questions of the miss protocol: **the app's spec** did not say it, so the checklist line is fixed; **the framework never said it**, so one requirement line and a check are added; **the check was too weak** (a review, or a script that did not fire), so the check is fixed; **said and ignored**, so the rule becomes a hook or is deleted. **not sorted** means the record predates the sort or nobody has answered yet; `bash .tfcore/utils/tf-emit.sh --amend <miss> sort <spec|unsaid|weak-check|ignored>` completes it.

## Open (1)

| Miss | Found | Whose gap | What went wrong |
|---|---|---|---|
| MISS-TechieRag-20260924-06 (REQ-RAG-016) | 2026-09-24 by agent-review | not sorted | The Agents proposal gives UseLmStudio a default endpoint before a required model, which C# cannot express, so both arguments are now required. |

## Fixed (19)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
| MISS-TechieRag-20261006-02 (REQ-FN-006) | 2026-10-06 by owner | 2026-10-06 by fix-issues | the check was too weak | SevakConsumesReleasedPackages hard-codes Sevak's pin as 1.0.7; Sevak moved to 1.0.8 on 2026-10-05, so the row fails although Sevak still consumes released packages only |
| MISS-TechieRag-20261006-01 (REQ-FN-071) | 2026-10-06 by production | 2026-10-06 by fix-issues | the check was too weak | Every assembly in every package carries the package version; a pack-time check enforces it (Sevak TR-RAG-048) |
| MISS-TechieRag-20261001-04 | 2026-10-01 by owner | 2026-10-01 by log-miss | the check was too weak | The framework's feedback checks match TR-RAG ids across apps: Chatur's TR-RAG-003/004/005 were reported as already fixed because Sevak's feedback file uses the same ids for different problems, which made the owner think Chatur hit bugs that were fixed earlier |
| MISS-TechieRag-20261001-03 (REQ-RAG-069) | 2026-10-01 by owner | 2026-10-01 by fix-issues | the check was too weak | Chatur TR-RAG-005: nothing says what happens when the sign-in callback throws during a model turn; there is no coded 'sign in again' exception for the host to catch |
| MISS-TechieRag-20261001-02 (REQ-RAG-070) | 2026-10-01 by owner | 2026-10-01 by fix-issues | the check was too weak | Chatur TR-RAG-004: the ChatGPT subscription connector's name is not public; a host must hardcode "chatgpt-subscription" |
| MISS-TechieRag-20261001-01 (REQ-RAG-109) | 2026-10-01 by owner | 2026-10-01 by fix-issues | the app's spec | OpenAI-compatible model: extra request headers and a per-conversation session header |
| MISS-TechieRag-20260925-02 (REQ-FN-070) | 2026-09-25 by owner | 2026-09-25 by amend-docs | the app's spec | The /techierag persona and the AI reference the package installs into a consumer's repository still describe v2 only; nothing about the local model, the agents package, typed streaming, subscription sign-in, Hugging Face models or the platform work. |
| MISS-TechieRag-20260925-01 (REQ-RAG-044) | 2026-09-25 by self-smoke | 2026-09-25 by build-phase | the check was too weak | PgVectorStore failed its first save on a new PostgreSQL database because it did not reload the server's types after creating the vector extension, and the mocked tests could not see it. |
| MISS-TechieRag-20260924-09 (REQ-FN-056) | 2026-09-24 by self-smoke | 2026-09-25 by build-phase | the app's spec | The SQLite stores use Dapper, which generates code at run time, so a Release build of a Mac or iPhone app fails the first time it saves; no requirement said the stores must work in a Release build on Apple platforms. |
| MISS-TechieRag-20260924-08 (REQ-RAG-082) | 2026-09-24 by agent-review | 2026-09-24 by build-phase | not sorted | A connector run that hits its byte limit stops without an error code the host can check. |
| MISS-TechieRag-20260924-07 (REQ-RAG-071) | 2026-09-24 by agent-review | 2026-09-24 by build-phase | not sorted | Ingesting a markdown file with the markdown chunking strategy gave one chunk for the whole file instead of one per heading. |
| MISS-TechieRag-20260924-05 (REQ-FN-062) | 2026-09-24 by agent-review | 2026-09-25 by amend-docs | not sorted | The acceptance line for the vendor sign-in research row is a copy of the ChatGPT sign-in row, so it does not test that the research was recorded. |
| MISS-TechieRag-20260924-04 (REQ-FN-055) | 2026-09-24 by agent-review | 2026-09-25 by amend-docs | not sorted | BRD-90 lists the Sevak interpreter setting as ONNX Runtime wiring, but that setting belongs to Docker.DotNet, not ONNX Runtime. |
| MISS-TechieRag-20260924-03 (REQ-FN-054) | 2026-09-24 by agent-review | 2026-09-25 by amend-docs | not sorted | BRD-88 names a BRD section 9 that only exists in the phase-1 BRD and forbids supported cells without a recorded run, while the acceptance line allows supported. |
| MISS-TechieRag-20260924-02 (REQ-RAG-106) | 2026-09-24 by agent-review | 2026-09-25 by amend-docs | not sorted | The Architecture document still says every vector store is fixed at 1024 dimensions, which is wrong now that the builder passes the embedder dimensions through. |
| MISS-TechieRag-20260924-01 (REQ-RAG-076) | 2026-09-24 by owner | 2026-09-24 by log-miss | the app's spec | YouTube transcript ingestion was built into the TechieRag library in August 2026 from the competitor gap list without the owner ever asking for it; it is an application feature at most, and the owner removed it on 2026-09-24. |
| MISS-TechieRag-20260903-03 (REQ-FN-004) | 2026-09-03 by agent-review | 2026-09-03 by fix-issues | not sorted | no sentence recorded (hallucinated-api, other, why: insufficient-verify-method) |
| MISS-TechieRag-20260903-02 | 2026-09-03 by owner | 2026-09-03 by fix-issues | not sorted | no sentence recorded (unspecified-gap, brd, why: missing-checklist-item) |
| MISS-TechieRag-20260903-01 (REQ-FN-003) | 2026-09-03 by owner | 2026-09-03 by fix-issues | not sorted | no sentence recorded (regression, config, why: insufficient-verify-method) |
