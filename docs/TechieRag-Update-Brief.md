# TechieRag Update Brief

**Session Date:** 2026-09-24
**Facilitator:** Business Analyst Chanakya
**Participant:** Owner (TechieRag)
**Purpose:** The one page that tells `*amend-docs` (both repositories) what changed and why. Dates and sequencing are the owner's and are not in this document.

## Executive Summary

**Topic:** How TechieRag absorbs four incoming demands: plan 08 (on-device local model, four platforms), plan 09 (TechieDesk repository split, Windows head, offline v0.1), Chatur feedback (subscription sign-in, streaming with tool calls), MyDiary feedback (on-device completion provider). What to build, what to say no to, and which documents change.

**Session Goals:** Focused ideation. Settle the decisions the two plans left open.

**Constraints taken as fixed:** solo developer; TechieFlow agents do the building; owner runs real devices; no cloud fallback for MyDiary; repository split before anything else. Added in session: **TechieDesk is renamed Sevak**.

**Inputs read:**
- `/mnt/c/1MyCode/MyAiChats/ClaudeDocs/plans/08-techierag-local-model.md`
- `/mnt/c/1MyCode/MyAiChats/ClaudeDocs/plans/09-techiedesk-offline-v01.md`
- `docs/Chatur-TechieRag-Feedback.md` (TR-RAG-001 sign-in blocker, TR-RAG-002 streaming tool calls)
- `docs/MyDiary-TechieRag-Feedback.md` (TR-RAG-001 on-device completion blocker)
- `docs/TechieRag-BRD.md` (highest ID today: BRD-126; feature areas F-LLM, F-AGENT, F-REPO, F-WEB)

**Techniques Used:** Assumption Reversal (seven claims, ten decisions); Question Storming (thirteen questions, answered from the sources); Forced Ranking (document changes, by dependency).

**Total Ideas Generated:** 10 decisions, 13 answered questions, 9 document-change items, 1 research item, 1 owner decision.

### Key Themes Identified:
- **One product, one name.** Sevak is TechieDesk renamed at the moment of the split. There is no second product to grow later.
- **Showcase, not gatekeeper.** Sevak exists to show every TechieRag capability. Freemium with limits, never locks.
- **The library says yes to both worlds.** Local model with no network, and subscription sign-in to hosted vendors. Same `ILlmProvider` contract for both.
- **Fix the contract before the seventh provider.** Streaming that carries tool calls goes in before `TechieRag.Local` exists.
- **Plans answer more than they seemed to.** Most "open" questions were already settled in plans 08 and 09; the brief points at the answer rather than reopening it.

## Technique Sessions

### Assumption Reversal
**Description:** Take the load-bearing claims in the two plans, state the opposite, and see what each reversal teaches.

#### Decisions taken:

1. **Sevak is TechieDesk, renamed. There is no separate future product.** Reverses plan 09's "a future Sevak grows out of TechieDesk". The rename happens when the new repository is created (plan 09 part A step 2), the cheapest moment it will ever have.

2. **Rename everything, not just the outside.** Repository, solution, project and namespace names, bundle ID, display name, DMG/zip names, workflow and tag prefix, all `TechieDesk-*` docs, mockups, devguides, screenshots folder. Requirement IDs keep their numbers; the repository prefix in cross-repo references becomes `Sevak#REQ-FN-054`.

3. **Sevak is "the application showing the full capabilities of TechieRag".** It stays the library's test bed. Every TechieRag capability is expected to have a visible home in Sevak.

4. **Sevak is freemium: features are limited, not gated.** Corrects plan 09 part B3, which lists six feature flags an offline install still gates. Every capability is present and usable in the free tier, with limits rather than locks. Exact limits are a later decision.

5. **Sevak is the first desktop consumer of `TechieRag.Local`; MyDiary is the first mobile consumer.** Confirms plan 08's build order (Mac and Windows first, then Android, then iOS).

6. **The repository split comes first.** Plan 09 part A stands, new repository named Sevak.

7. **Local model: one public interface, platform-specific implementations underneath.** `UseLocalLlm()` is the single contract; the runtime behind it is chosen per platform. Plan 08 step 4 item (2) stands as written.

8. **Subscription sign-in is in scope for TechieRag.** Closes Chatur TR-RAG-001. The library is flexible enough for personal and team use; which applies is decided by who signs in and what licence they hold, not by the library. Vendor policy is a fact to research (R1), not a reason to refuse the feature.

9. **Change the streaming contract now, before `TechieRag.Local` exists.** Streaming yields typed events (text delta, tool call). Six existing providers updated; `TechieRag.Local` implements the new contract from day one. Closes Chatur TR-RAG-002.

10. **Dates and sequencing are the owner's.** This document records decisions and document changes; the owner re-plans from it.

#### Insights Discovered:
- The rename and the split are the same event. Doing them together costs one name in one command.
- "Showcase" is a stronger governance rule than "test bed": it creates a standing expectation that library features get Sevak screens (plan 09 already has the mechanism: a library requirement plus a matching app requirement that references it by repository and id).
- Two of the three upstream blockers (MyDiary on-device, Chatur sign-in) are the same shape: a new `LlmSource`, a new builder method, a new `LlmProviderFactory` arm, a new `LlmConnectorCatalog` row. They can share one amendment pattern.

#### Notable Connections:
- Decision 9 (streaming contract) is a prerequisite for decision 7 (`TechieRag.Local`): the new provider must implement the final contract, not a contract about to change.
- Decision 3 (showcase) means the local model (plan 09 B3) and subscription sign-in both need Sevak screens, so both produce paired requirements in both repositories.

### Question Storming
**Description:** List every question the documents must answer once the ten decisions are folded in, then answer each from the sources rather than reopening it.

| # | Question | Answer | Source |
|---|---|---|---|
| 1 | Is the Sevak repository public on day one or private until v0.1? | **Decided: private until v0.1.** Goes public with the first app tag, after secret rotation and the handoff test (plan 09 part C). Recorded in `docs/OldDocs/Sevak-Decision-Request.md` D1. | Owner, 2026-09-24 |
| 2 | Which docs are Sevak's source of truth after the split? | The existing `TechieDesk-BRD`, `-Checklist`, `-Architecture`, `-UIDesign`, `-UsageGuide` renamed in place to `Sevak-*`, then amended for the freemium model. Rows marked "Moved 2026-09-03" stay as pointers. | Plan 09 part A, "Ending the two-checklist confusion" |
| 3 | What are the free-tier limits? | Later decision. The BRD states "limited, not gated; limits set in a later revision". | Decision 4 |
| 4 | Where does `UseLocalLlm()` live and which platform ships first? | In the new package `TechieRag.Local`, never in core, registered via `UseCustomLlmProvider`, with an internal runtime interface and one backend per platform. Mac and Windows first. | Plan 08 step 4 items (1), (2) and "Order inside the build" |
| 5 | When does Sevak's "Built-in local model" screen become a requirement? | After `TechieRag.Local` is published; plan 09 B3 is written to run at that point. The requirement can be authored earlier and sit Blocked on the package. | Plan 09 B3 |
| 6 | Where do weights download to, and who owns terms acceptance? | Per-user application data folder (`LocalApplicationData`), host-overridable, same root for embeddings and reranker. Library requires explicit acceptance before download; the app shows the dialog. Weights never ship inside package or app bundle. | Plan 08 step 1 item (2), step 4 items (5), (6); plan 09 B3 item (4) |
| 7 | Builder method shape for subscription sign-in? | One method per vendor that supports it, sharing one callback shape, e.g. `UseChatGptSubscriptionLlm(deviceCodeCallback)` as Chatur suggested. Architect confirms the shape against each vendor's live documentation before code (R1). | Chatur TR-RAG-001 "Suggested fix" |
| 8 | Who drives the browser step? | The host app. The library returns the sign-in URL and code, waits for completion, and yields an `ILlmProvider`. | Chatur TR-RAG-001 "Suggested fix" |
| 9 | What does the library do when a vendor forbids subscription use (Anthropic today)? | It does not police policy. The connector catalog row carries the vendor's stated terms ("personal use only", "not permitted for third-party tools"). Where a vendor offers no flow, no method exists. The app decides what to show. | Decision 8 |
| 10 | Streaming: new method or breaking change? | Additive. A new streaming method returning typed events, with the existing text-only `ChatStreamAsync` kept and implemented on top of it. Avoids a major-version bump for consumers. Architect confirms. | Decision 9; package version is 1.0.x |
| 11 | Does TechieRag's own agent loop (F-AGENT) move to the streaming path? | Yes, once the method exists, so that F-AGENT and `TechieRag.Agents` (F-AGENTS) can stream a tool-using turn the same way Chatur wants to. | Decision 9; BRD F-AGENT, F-AGENTS |
| 12 | Which items are TechieRag BRD IDs, which are Sevak requirements? | TechieRag: platform groundwork (plan 08 step 1), `TechieRag.Local` (plan 08 step 4), streaming contract, subscription sign-in, and the F-REPO/F-WEB wording change for the rename. Sevak: rename, Windows parity (plan 09 B1), built-in local model (B3), offline mode (B3), subscription sign-in screen, freemium wording. | Plans 08, 09; decisions 2, 3, 4, 8, 9 |
| 13 | Do the feedback files get replies now? | Yes. Each entry gets a "Replies from TechieRag" block once `*amend-docs` has assigned BRD IDs: "Accepted, tracked as BRD-N". Both Chatur entries and the MyDiary entry. | Owner-language rule: a reported defect always gets an answer |

### Forced Ranking
**Description:** Rank the document changes by what depends on what, not by date.

1. **Rename + split** (Sevak repository). Everything downstream references the new name.
2. **Streaming contract** in TechieRag BRD/Architecture. `TechieRag.Local` must be written against it.
3. **Platform groundwork** (plan 08 step 1, F-PLATFORM). `TechieRag.Local` inherits every one of its problems.
4. **`TechieRag.Local`** (plan 08 step 4, F-LOCAL-LLM).
5. **Subscription sign-in** in TechieRag. Independent of 2 to 4; can run alongside.
6. **Sevak Windows parity** (plan 09 B1). Independent of the library items.
7. **Sevak built-in local model + offline mode** (plan 09 B3). Depends on 4.
8. **Sevak subscription sign-in screen.** Depends on 5.
9. **Feedback replies** in both feedback files. Depends on IDs from 2, 4, 5.

## Idea Categorization

### Immediate Opportunities
*Ready to fold into the documents now*

1. **Sevak rename inside the split**
   - Description: Plan 09 part A executed under the name Sevak; full rename per decision 2.
   - Why immediate: the new repository does not exist yet; the rename is free only now.
   - Resources needed: owner runs the git steps (plan 09 part A table); agents do the rest via `*amend-docs` on both sides.

2. **Streaming contract amendment**
   - Description: TechieRag BRD F-LLM and F-AGENT gain a requirement for typed streaming events; Architecture gains the event shape.
   - Why immediate: blocks nothing yet, and must land before `TechieRag.Local` is written.
   - Resources needed: `*amend-docs TechieRag`; architect confirms additive vs breaking.

3. **Freemium wording in Sevak docs**
   - Description: replace any "gate", "locked", "unlock" language with "limited"; the six-flag list in plan 09 B3 is dropped.
   - Why immediate: one sentence of principle, applies to every Sevak requirement written after it.
   - Resources needed: part of the same `*amend-docs Sevak` run as the rename.

### Future Innovations
*Need development or research first*

1. **Subscription sign-in (TechieRag)**
   - Description: per-vendor builder methods with a shared device-code callback; new `LlmSource`, factory arm, catalog row with vendor terms.
   - Development needed: R1 research on each vendor's current policy and flow (OpenAI known permitted for personal use; Anthropic known prohibited; Google, Groq, xAI, Meta to check); architect confirms method shape.
   - Timeline estimate: owner's call.

2. **`TechieRag.Local` and platform groundwork**
   - Description: as plan 08 steps 1 and 4, unchanged by this session except that the streaming contract (above) is now a stated prerequisite.
   - Development needed: as plan 08.
   - Timeline estimate: owner's call.

3. **Sevak local model and offline mode**
   - Description: plan 09 B3, two amendments, renamed to Sevak, with "Built-in local model" also visible as a showcase feature per decision 3.
   - Development needed: waits on `TechieRag.Local`.
   - Timeline estimate: owner's call.

### Moonshots
*Ambitious concepts noted, not adopted*

1. **Operating-system built-in models as a backend**
   - Description: Apple's on-device foundation model and Android's Gemini Nano behind the same `UseLocalLlm()` interface, no download at all.
   - Transformative potential: zero-download offline AI on phones.
   - Challenges to overcome: plan 08 found no verified .NET bindings; stays a later backend behind the interface decision 7 already provides for.

### Insights & Learnings
- **The interface decision already covers the unknowns**: because `UseLocalLlm()` hides the runtime, the runtime comparison, a late platform, and OS-native models are all implementation choices, not requirement changes.
- **Vendor policy belongs in the catalog, not in the code**: the library records what each vendor permits and lets the app decide how to present it.
- **A showcase app creates paired requirements**: each library feature that needs UI proof gets a Sevak requirement that names it by repository and id, which plan 09 already prescribes.

## Action Planning

### Top 3 Priority Ideas

#### #1 Priority: Sevak rename and split
- Rationale: everything else lands in one of two repositories that do not both exist yet.
- Next steps: owner cuts baseline release and creates the Sevak repository (plan 09 part A steps 1, 2). Then `*amend-docs Sevak` (rename, freemium wording, cross-repo reference rule) and `*amend-docs TechieRag` (F-REPO and F-WEB wording: TechieDesk becomes Sevak; F-WEB rows already "Moved" stay as pointers).
- Resources needed: owner, about an hour; agents for the rest.
- Timeline: owner's call.

#### #2 Priority: Streaming contract
- Rationale: prerequisite for `TechieRag.Local`; cheapest before the seventh provider exists.
- Next steps: `*amend-docs TechieRag "F-LLM and F-AGENT: streaming that yields typed events (text delta, tool call); additive method, existing ChatStreamAsync preserved; all six providers implement it; F-AGENT loop and F-AGENTS consume it; closes Chatur TR-RAG-002"`. Then reply in `docs/Chatur-TechieRag-Feedback.md`.
- Resources needed: architect confirmation of additive shape.
- Timeline: owner's call.

#### #3 Priority: Subscription sign-in
- Rationale: unblocks Chatur REQ-FN-016; independent of the local-model track so it can run alongside.
- Next steps: R1 research first (`*research-prompt` on vendor subscription-auth policies and flows). Then `*amend-docs TechieRag "F-LLM: subscription sign-in providers, host-driven device-code flow, new LlmSource, factory arm, catalog row carrying vendor terms; closes Chatur TR-RAG-001"`. Then a paired Sevak requirement for the sign-in screen (the mockup `settings-providers.html`, `data-testid="signin-browser"`, already exists in Chatur's design and can be mirrored).
- Resources needed: R1 research; architect for method shape.
- Timeline: owner's call.

### Research item
- **R1. Vendor subscription sign-in policy and flow**, per vendor: OpenAI (known: permitted in external tools for personal use; production expected to use the platform API), Anthropic (known: prohibited for Free/Pro/Max since April 2026), Google, Groq, xAI (Grok), Meta. For each: is there an official sign-in flow usable outside the vendor's own client, what does the token permit, personal vs team terms, and what the flow returns. Output feeds question 7 and 9 above.

### Owner decision
- **Sevak repository visibility:** decided 2026-09-24, private until v0.1. `docs/OldDocs/Sevak-Decision-Request.md` D1.

## Reflection & Follow-up

### What Worked Well
- Assumption Reversal on the plans' unstated claims produced ten decisions in one round.
- Answering the question list from the sources instead of asking it back.

### Areas for Further Exploration
- Free-tier limit list for Sevak: deferred by the owner; needed before the Sevak BRD can be fully verified.
- Whether `TechieRag.Agents` (F-AGENTS) should adopt the streaming events at the same time as F-AGENT or in a later revision.

### Recommended Follow-up Techniques
- None needed for this topic. Next step is execution: `*amend-docs` in both repositories in the ranked order above.

### Questions That Emerged
- Does the rename change the `.techierag/TechieRag-AI-Reference.md` text that downstream apps (Chatur, MyDiary) read, or only the app-side docs? (Likely only where TechieDesk is named as an example; check during `*amend-docs TechieRag`.)

### Next Session Planning
- **Suggested topics:** Sevak free-tier limits, once v0.1 scope is known.
- **Recommended timeframe:** owner's call.
- **Preparation needed:** the 101 open Sevak rows reviewed once against the finish-line sentence (plan 09 part C).

---

*Session facilitated using the TechieFlow™ brainstorming framework*
