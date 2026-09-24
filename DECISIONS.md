# Decisions

Durable architectural and process decisions for this repository. Newest first. Each entry records
what was decided and, where it is not obvious, why — so that a future reader does not re-litigate it.

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
