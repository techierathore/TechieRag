# Decisions

Durable architectural and process decisions for this repository. Newest first. Each entry records
what was decided and, where it is not obvious, why — so that a future reader does not re-litigate it.

---

## 2026-09-25 (later) — Any Hugging Face model by name; the phone default stays our own conversion (owner decisions 1–2)

Source: `docs/TechieRag-Decision-Request.md` (second request of the day, both option A). BRD-166 / REQ-RAG-108.

**Why it came up.** The owner asked why the phone model must be published under his account and whether that ties
the library down. It did in one way: `TechieRag.Local` offered only its two built-in models or a folder the app
filled itself, with no way to name any other model, as LM Studio allows.

**1. `LocalModel.FromHuggingFace(repository, folder, version)`.** Any ONNX Runtime GenAI model on Hugging Face,
by name. Files are listed through Hugging Face's public API; the model card's licence is shown for acceptance
before any download (BRD-101 unchanged); every file is checked against the fingerprint Hugging Face publishes
(SHA-256 for large files, the git checksum for small ones); an unpinned name is resolved to one version and
recorded, so a later change on the page never reaches an installed app. The app developer answers for the model
they name.

**2. The built-in phone default stays our own Qwen2.5 0.5B conversion**, published once on the owner's Hugging
Face account and pinned by version (steps: `docs/MODEL-PUBLISHING-GUIDE.md`). Measured alternative on the M4 Max
the same day, Arm's `gemma-3-1b-instruct-onnx-genai-int4-emb-int8` (the only phone-sized model in this format from
a known publisher; Microsoft and the ONNX Runtime team publish nothing that small, onnx-community's Qwen is in a
format this engine cannot load): 866 MB against 317 MB, 150–157 against 330–360 tokens/s, 1.7 GB against 0.5 GB
peak memory, Gemma terms against Apache-2.0, equal score on eight factual questions. A curated default hosted by
the maker is also how Ollama (its own registry) and LM Studio (its `lmstudio-community` organisation) do it.

---

## 2026-09-25 — Local model engine, Mac support, phone model source, ChatGPT client id (owner decisions 1–4)

Source: `docs/TechieRag-Decision-Request.md` (answered 2026-09-25, all four option A). This is the measured
comparison entry 2026-09-24 point 3 asked for. Blocks REQ-RAG-057, 058, 063, 065, REQ-FN-058, 059 lifted.

**Measurement.** LLamaSharp 0.27.0 against ONNX Runtime GenAI 0.16.0, the same two models in each engine's
format (Qwen2.5-0.5B-Instruct; Phi-3-mini-4k-instruct), every file SHA-256 pinned, a 128-token greedy answer,
three runs each. Raw data: `tests/.artifacts/local-llm-bench/` on each machine (not committed).

| Machine, date | Model | ONNX Runtime GenAI | LLamaSharp |
|---|---|---|---|
| Windows 11, Core i5-11300H, 16 GB, 2026-09-24 | Qwen 0.5B | 45–52 tok/s, first token 0.2–0.4 s, load 1.1–3.3 s, 555 MB | 25–31 tok/s, 0.4–0.8 s, 0.4–1.5 s, 510 MB |
| same | Phi-3 mini | 7.5 tok/s, 3.0–3.3 GB | 5.9 tok/s, 3.6 GB |
| same, WSL Linux | Qwen / Phi-3 best | 86 / 8.4 tok/s | 64 / 6.7 tok/s |
| macOS 27, Apple M4 Max, 36 GB, 2026-09-25 | Qwen 0.5B | 307–331 tok/s, 0.03 s, 0.2 s, 506 MB | CPU 295–298 tok/s, 0.05–0.07 s, 803 MB; Metal 191–225 |
| same | Phi-3 mini | 61–69 tok/s, 0.12 s, 0.7 s, 2.7 GB | CPU 61–73, 6.1 GB; Metal 96–112, 3.9 GB |
| same, inside a Mac Catalyst app | Qwen / Phi-3 | 360–398 / 73 tok/s | Phi-3 Metal 102–107 (loader workaround) |

Both engines tokenize identically on every test sentence. Platform availability on nuget.org: ONNX Runtime
GenAI ships Windows, Android, iOS and — inside its iOS xcframework — an `ios-arm64_x86_64-maccatalyst` slice
(its `maccatalyst` build folder is an empty `_._`); LLamaSharp ships nothing for iOS or Mac Catalyst.

**1. ONNX Runtime GenAI is the local engine on Windows, Android and iOS.** Faster answers on Windows, lowest
memory everywhere, the only one of the two on iOS, and it shares ONNX Runtime with `TechieRag.Embedded`,
whose `Microsoft.ML.OnnxRuntime` moves 1.24.1 → 1.30.0 (the version GenAI 0.16.0 requires).

**2. The same engine on Mac Catalyst.** `TechieRag.Local`'s buildTransitive targets declare the GenAI
xcframework's Mac Catalyst slice as a static `NativeReference`, as `TechieRag.Embedded` already does for ONNX
Runtime (REQ-FN-055). Rejected: LLamaSharp with Metal on the Mac only (about 40% faster on Phi-3, but a second
engine, a bypassed library loader, and loose macOS-platform dylibs in a Catalyst bundle whose App Store
acceptance is unknown); "not supported on the Mac". The missing Mac wiring is filed with ONNX Runtime GenAI.
LLamaSharp and GGUF leave the catalogue: one format per model.

**3. The phone model's ONNX files are our own conversion, hosted on the owner's mirror.** Converted from
Qwen's official release (`Qwen/Qwen2.5-0.5B-Instruct` commit `7ae557604adf67be50417f59c2c2f167def9a775`,
Apache-2.0) with `onnxruntime_genai.models.builder` 0.16.0, `-p int4 -e cpu`, `int4_block_size=32`. Result
317 MB against 837 MB for the third-party copy (`xiaoyao9184`, no licence tag) it replaces, same token
counts, same answers on eight factual questions, speed equal or better, about 100 MB more working memory.
The default download address is the owner's mirror; the file list and fingerprints are pinned in
`LocalModel.Qwen25Instruct05B`.

**4. The ChatGPT sign-in keeps Codex's public client id as its default.** Unchanged from the build: it rests on
OpenAI staff's public statements (entry below); a host overrides it with `ChatGptSubscriptionOptions.ClientId`,
and the library names itself `techierag` on every call.

---

## 2026-09-24 (later) — Subscription sign-in: vendor policy and flow, checked live (research findings, REQ-FN-062 / BRD-114)

**This entry records facts with their sources, not a new product decision.** It is the check BRD-114 requires
before BRD-112 is built. All sources were read on **2026-09-24**. What the library does with these facts is
already decided (entry below, point 5): terms are dated facts in `LlmConnectorCatalog`, a vendor with no
permitted flow gets a catalog row saying so and no builder method, and no workaround is ever written.

| Vendor | Third-party app may use the consumer subscription? | For whom | Flow and client id | Library outcome |
|---|---|---|---|---|
| OpenAI (ChatGPT) | **Yes** — stated publicly by OpenAI, not in a contract clause | Individual ChatGPT plans (Free, Go, Plus, Pro); a Business / Enterprise workspace only where its admin enables device-code sign-in | OAuth device code at `https://auth.openai.com`: `POST /api/accounts/deviceauth/usercode` `{client_id}` → `device_auth_id`, `user_code`, `interval`; user opens `https://auth.openai.com/codex/device`; poll `POST /api/accounts/deviceauth/token` (403/404 = pending, 15-minute limit) → `authorization_code` + PKCE `code_verifier`; exchange at `POST /oauth/token` (`authorization_code`, `redirect_uri=https://auth.openai.com/deviceauth/callback`); refresh with `grant_type=refresh_token`. Model calls go to the Codex backend (`https://chatgpt.com/backend-api/codex/responses`, Responses-API wire format, `ChatGPT-Account-Id` header), not `api.openai.com`. **Client id:** OpenAI runs no third-party client registration; the one client is Codex's public PKCE client `app_EMoamEEZ73f0CkXaXp7hrann`, and OpenAI's statements endorse the open-source tools that use it (OpenCode, Cline, pi, OpenClaw). | `chatgpt-subscription` row, **permitted**; `UseChatGptSubscriptionLlm` built |
| Anthropic (Claude) | **No** | Free, Pro, Max | OAuth exists only for Claude Code and Claude.ai. Quote: *"The use of OAuth tokens obtained via Claude Free, Pro, or Max accounts in any other product, tool, or service — including the Agent SDK — is not permitted and constitutes a violation of the Consumer Terms of Service."* Terms updated 2026-02-20, server-side enforcement completed 2026-04-04. Matches the entry below. | `claude-subscription` row, **not permitted**; no method |
| Google (Gemini) | **No** | Personal Google accounts and Google AI Pro / Ultra | Gemini CLI's Google sign-in reaches the Gemini Code Assist service. Quote: *"Directly accessing the services powering Gemini CLI (for example, the Gemini Code Assist service) using third-party software, tools, or services (for example, using OpenClaw with Gemini CLI OAuth) is a violation of applicable terms and policies."* Third-party access is the Gemini API key or Vertex AI. | `gemini-subscription` row, **not permitted**; no method |
| xAI (Grok) | **Not confirmed** — no xAI-published terms or client registration found | SuperGrok and X Premium+ (as third parties describe it) | Several third-party apps (LobeHub, Hermes Agent, Kilo Code, Zed) run an RFC 8628 device-code flow against `auth.x.ai` / `accounts.x.ai`. None cites an xAI document permitting it or issuing it a client id; the xAI Grok FAQ is silent on third-party use and treats API credits as separate from subscriptions. | `grok-subscription` row, **not confirmed**; no method until xAI publishes one |
| Groq | **No consumer subscription exists** | — | GroqCloud is API-key only; its Services Agreement says the Cloud Services "are not for consumer use". There is no sign-in flow to use. | `groq-subscription` row, **no flow**; no method |
| Meta (Meta AI / Llama) | **No flow exists** | — | The Meta AI assistant is free with no subscription sign-in for other apps; the Llama API was an API-key developer preview (secondary sources report it retired 2026-07-06; Meta's own page did not state this when checked). | `meta-subscription` row, **no flow**; no method |

**Where live sources and the entry below agree and differ.** OpenAI permits and Anthropic prohibits:
**both confirmed.** Two points the entry below did not carry: (1) OpenAI's permission rests on public statements
by OpenAI staff and an official OpenAI programme page, not on a clause in its terms, and OpenAI issues no client
id to third parties — the library therefore uses Codex's public client id by default, and a host can set its own
through `ChatGptSubscriptionOptions.ClientId` should OpenAI ever issue one; (2) "personal use" is how third
parties summarise it — OpenAI's own words are about using "your ChatGPT account" in other tools, and workspace
accounts depend on their admin. Neither changes the build; both are flagged for the owner in the checklist.

**Sources (read 2026-09-24).**
- OpenAI: [Codex authentication](https://learn.chatgpt.com/docs/auth.md) (redirected from `developers.openai.com/codex/auth`) · [Codex for Open Source — "Developers should code in the tools they prefer, whether that's Codex, OpenCode, Cline, pi, OpenClaw, or something else"](https://developers.openai.com/community/codex-for-oss) · [Tibo Sottiaux, OpenAI: "Reminder you can use your ChatGPT account in a flourishing set of other tools"](https://x.com/thsottiaux/status/2058071172361998482) · [Tibo Sottiaux: "You can already build on top of [Codex] directly, which includes ChatGPT [sign-in]"](https://x.com/thsottiaux/status/2009714843587342393) · [Codex device-code source](https://github.com/openai/codex/blob/main/codex-rs/login/src/device_code_auth.rs) · [Codex login server source](https://github.com/openai/codex/blob/main/codex-rs/login/src/server.rs) · [Using Codex with your ChatGPT plan](https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan) (returned 403 to the checker)
- Anthropic: [Claude Code — Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) · [The Register, 2026-02-20](https://www.theregister.com/2026/02/20/anthropic_clarifies_ban_third_party_claude_access/)
- Google: [Gemini CLI — Terms of Service and Privacy Notice](https://geminicli.com/docs/resources/tos-privacy/) · [same, in the repository](https://github.com/google-gemini/gemini-cli/blob/main/docs/resources/tos-privacy.md)
- xAI: [xAI Grok FAQ](https://docs.x.ai/grok/faq) · third-party only: [Hermes Agent guide](https://hermes-agent.nousresearch.com/docs/guides/xai-grok-oauth), [LobeHub SuperGrok](https://lobehub.com/docs/usage/providers/supergrok), [Kilo Code xAI](https://kilo.ai/docs/ai-providers/xai)
- Groq: [Groq Services Agreement](https://console.groq.com/docs/legal/services-agreement)
- Meta: [Meta developer site](https://dev.meta.ai/) (redirected from `llama.developer.meta.com`) · secondary: [Promptfoo — Meta Llama API](https://www.promptfoo.dev/docs/providers/llamaApi/)

---

## 2026-09-24 — Sevak, a fourth package, and two contract additions (brainstorm on plans 08/09)

Source: `docs/TechieRag-Update-Brief.md` (ten decisions), `docs/OldDocs/Sevak-Decision-Request.md` D1. BRD-87 and
BRD-81 amended; BRD-88…114 added; Architecture ADR-012…016.

**1. TechieDesk is renamed Sevak, in full, at the split.** The application repository created today is
`Sevak` (`/mnt/c/1MyCode/Sevak`); projects, namespaces, bundle id, artefacts and documents follow;
requirement ids keep their numbers. There is no separate future product. Sevak is *the application showing
the full capabilities of TechieRag* and stays the library's test bed. It is **freemium: features limited,
never gated** (limits set later). Its repository is **private until Sevak v0.1**. Cross-repository
references carry the repository name: `Sevak#REQ-FN-054`, `TechieRag#REQ-RAG-054`.

**2. The split executed today on the app side.** App paths copied into Sevak (build output excluded), goal
`Sevak/docs/Sevak-Split-Goal.md` running: full rename, `PackageReference` to `TechieRag` /
`TechieRag.Embedded` 1.0.7 (the published baseline), central pins, three-source `NuGet.config`, `Sevak.slnx`,
app-only release workflow. The deletion here (`REQ-FN-006`, `docs/TechieRag-Split-Goal.md`) runs only after
Sevak builds and tests green from packages.

**3. Local model: one public interface, platform implementations underneath** (`TechieRag.Local`, F-LOCAL-LLM).
`UseLocalLlm()` is the only thing a consumer sees; the runtime behind it is chosen per platform after a
measured comparison (LLamaSharp vs ONNX Runtime GenAI) that will be recorded here with numbers before the
provider is built. Weights download once, never packed. Sevak is the first desktop consumer, MyDiary the
first mobile one. Closes MyDiary feedback TR-RAG-001.

**4. Streaming contract changes now, additively, before `TechieRag.Local` exists** (BRD-110/111). A new
method yields typed events (text delta, tool call, completed); `ChatStreamAsync` stays. Six providers once
instead of seven twice. Closes Chatur feedback TR-RAG-002.

**5. Subscription sign-in is in scope** (BRD-112…114). Host app drives the browser; library yields an
`ILlmProvider`; the library is flexible for personal and team use and who signs in decides which applies.
Vendor terms are dated facts in the connector catalog, never rules in code. Known today: OpenAI permits
external-tool use for personal use; Anthropic prohibits it for Free/Pro/Max. Google, Groq, xAI, Meta:
research before build. Closes Chatur feedback TR-RAG-001.

**6. Dates and sequencing are the owner's.** The documents record decisions and the work they imply; the
owner re-plans from them.

---

## 2026-09-03 (later) — Public feed stays manual dispatch, same ceremony as the other libraries

The entry below added a `v*` tag trigger to `publish-nuget.yml` so that a tag push publishes to nuget.org.
The owner reversed that the same day: the process for every TechieRathore library is **publish a GitHub
Release (creates the tag, feeds GitHub Packages automatically) → then dispatch the nuget.org workflow by
hand, selecting that release tag as `ref`**. TechieRag now matches. The 2026-08-09 rule "the public feed
never publishes itself" is therefore **reaffirmed**, and the trigger is `workflow_dispatch` only.

**What stays from the entry below** — the actual defect fix: the version is derived from the selected tag
by `determine-version.sh`, never from the csproj; a real run on a non-tag ref fails; a version already on
nuget.org or not greater than the latest fails before anything is built; `-p:Version` reaches build, test
and pack; the push carries no `--skip-duplicate`. The README / docs changes are unaffected.

Runbook: [NUGET-PUBLISHING.md](NUGET-PUBLISHING.md) §1 rule 3 and §4.

---

## 2026-09-03 — The tag is the public version (a tag push published it — superseded above)

Two defects the owner found on the public path (`REQ-FN-003`, `REQ-FN-004` in `docs/TechieRag-Checklist.md`),
one decision each.

**1. `publish-nuget.yml` derives the version from the `v*` tag and runs on a tag push.** The workflow
never had a version step: it packed whatever `<Version>` the csproj carried (`1.0.0`, never bumped) and
only *read the number back* from the packed filename, then pushed with `--skip-duplicate`. nuget.org
therefore holds exactly `1.0.0` of both packages while tags reached `v1.0.6` — every public dispatch
after the first was a silent no-op. Now `.github/workflows/scripts/determine-version.sh` (a plain bash
script so it can be replayed locally, `tests/verify/publish-nuget-version.sh`) is the single source:
the version is the tag minus its `v`; a real run on a non-tag ref fails; a version already on nuget.org
fails before anything is built; a version not greater than the latest published fails ("a release
increments"); `-p:Version` is passed to build, test **and** pack so the assembly and the nuspec agree;
and the push no longer carries `--skip-duplicate`, so a duplicate that gets that far is loud.

This **reverses rule 3 of the 2026-08-09 decision** ("the public feed never publishes itself").
The rule was written to make a public release a deliberate act; the tag already is one — nobody pushes
`v1.0.7` by accident, and the manual dispatch stayed for six versions without ever shipping. The
dry-run dispatch remains for inspecting packages from any ref. The other two 2026-08-09 rules stand:
public versions are a subset of private-feed versions (`publish-github-packages.yml` still publishes
every tag there too), and same version = same commit. The csproj `<Version>` is now a development
number only; it is never what ships to nuget.org.

**2. The README leads with `dotnet add package TechieRag` from nuget.org — no authentication.** Both
packages have been public since 2026-08-09, yet every install instruction (README, both user guides,
the AI-reference and the agent command files that ship *inside* the package) documented GitHub
Packages only: create a PAT, add a source, edit `nuget.config`. GitHub Packages is now a clearly
labelled *"internal development builds only"* section for maintainers wanting pre-release builds;
public consumers never touch it. Also found and fixed on the same walk: the README's search samples
read `result.Content` / `result.DocumentId` / `result.Metadata`, none of which exist on
`SearchResult` (it exposes `Chunk` + `Score`), so the first copy-paste failed to compile.

**Done when (owner's words):** a reader with no GitHub account can go from README to a working search
in under fifteen minutes, and a tagged push publishes an incremented version. The first was walked
end-to-end from a fresh console project with a `<clear/>`-ed `NuGet.config` (evidence in the
checklist Remarks); the second is proven by replaying the version script against nuget.org's live
index and by packing at the derived version.

Runbook: [NUGET-PUBLISHING.md](NUGET-PUBLISHING.md) §1, §3, §4, §6 updated to match.

---

## 2026-08-09 — Dual-feed publishing

Dual-feed publishing adopted: GitHub Packages remains primary dev feed; NuGet.org added as public
feed via on-demand `publish-nuget.yml` (`workflow_dispatch`) using Trusted Publishing/OIDC — no
long-lived credentials. Public versions are a subset of private-feed versions; first public release
is the current stable version (earlier v0.1 framing retired). Aug 27 target = first real dispatch run.

**Consequences**

- The pre-existing private-feed workflow was renamed `.github/workflows/publish-nuget.yml` →
  `.github/workflows/publish-github-packages.yml`, contents byte-for-byte unchanged. The nuget.org
  trusted publishing policy binds to the workflow **file name** `publish-nuget.yml`, so that name had
  to be freed for the public pipeline. Renaming `publish-nuget.yml` again would invalidate the policy.
- Both `TechieRag` and `TechieRag.Embedded` publish to both feeds, always at the same version.
- No `NUGET_API_KEY` secret exists or should be created. The dormant API-key job left inside the
  renamed private workflow is a no-op and is not the public path.

Runbook: [NUGET-PUBLISHING.md](NUGET-PUBLISHING.md).
