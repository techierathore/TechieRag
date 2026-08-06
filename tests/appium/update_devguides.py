#!/usr/bin/env python3
"""Write the 2026-07-29 runtime observations back into the split DevGuides (verify-phase §6b).

Markdown only — the .html is re-rendered by the orchestrator.
"""
import re

D = "2026-07-29"
BASE = "/Users/MyCode/TechieRag/docs/devguides/TechieDesk-DevGuide-"

# route -> (observed line body, visual line body, optional known-issues bullet)
OBS = {
 "/workspace/{Slug}": (
   f"renders ✓ (runtime-confirmed {D}) — 109 a11y content nodes / 68 interactive, all composer "
   "controls present: mode Select, model override, retrieval scope, agents, Attach, Prompts, mic, send, "
   "Threads panel. Reached from the sidebar AND from the native Go menu.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600×1240 and 1024×720: 0 interactive overlaps, "
   "0 zero-size boxes, composer reflows to two rows at 1024 with no horizontal overflow.",
   "Streamed answers, citation chips, read-aloud and the agent execution trace could not be exercised: "
   "no chat provider is reachable on this host (LLM Settings Source = None; no Ollama/LM Studio listening)."),
 "/workspace/{Slug}/documents": (
   f"renders ✓ (runtime-confirmed {D}) — `Library (3 documents)` with 3 real rows; Name, Type, "
   "Chunks, Uploaded, Workspaces, Status (`Embedded` badges) and the Unembed/Delete actions all populated; "
   "count badge matches visible rows. **`Size` column: renders-empty (DEFECT — see Known issues).**",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size, table stays "
   "inside its card at 1024.",
   "\U0001f534 **renders-empty (DEFECT, {d}) — `DataTableColumn Size` (`DocumentLibrary.razor:194`) shows "
   "`—` on every row.** `SizeFromMetadata` (`:735`) probes `FileSize`/`Size`/`fileSize`/`size`/`ByteSize` in "
   "`Document.Metadata` and no ingestion path writes any of them, so the fallback `—` is what every document "
   "will ever show. REQ-UI-021 names *size* as a required column. Corroborated on `/settings/data`, where the "
   "`uploads` artefact reads `not created yet`.".format(d=D)),
 "/workspace/{Slug}/documents/web": (
   f"renders ✓ (runtime-confirmed {D}) — source picker (Web page / Website crawler), page-address "
   "Input, private-address Switch, pin Switch and `Read page and add` all present.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.", None),
 "/workspace/{Slug}/connectors": (
   f"renders ✓ (runtime-confirmed {D}) — 5 source cards, `Saved connectors` with a real mailbox "
   "connector (last run, item count, Edit/Test/Sync/Delete), `Running now` honest empty state, and "
   "`Recent runs` with 5 populated rows (Status/Source/Items/Started/Took/Result).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, cards reflow cleanly.", None),
 "/workspace/{Slug}/connectors/new": (
   f"renders ✓ (runtime-confirmed {D}) — Git-repository form with Name, Project path, Branch, "
   "include/exclude globs, Access token, API/Web base URL, private-network Switch, pin Switch, "
   "Save/Test/Sync. Reached from a source card's `Add` link.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.", None),
 "/workspace/{Slug}/agents": (
   f"renders ✓ (runtime-confirmed {D}) — **all four tabs driven**: *Agents* (table with the built-in "
   "`@agent` row, Model/Skills/Knowledge/Last-used populated), *Skill catalogue* (8 skills with toggles, "
   "guardrail limits, `Show the execution trace in chat`), *MCP servers* (honest “not available yet — "
   "REQ-RAG-023”), *Run history* (honest “Agent runs are not persisted yet”).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size on every tab.",
   "**REQ-RAG-006 / REQ-UI-034 evidence is NOT obtainable here.** *Run history* states that runs are not "
   "persisted and that the execution trace lives only in the chat thread while it is open; with no chat "
   "provider reachable no trace can be produced. The trace is therefore **unexercised**, not confirmed."),
 "/workspace/{Slug}/settings": (
   f"renders ✓ (runtime-confirmed {D}) — **all three tabs driven**: *General* (Display name `Default`, "
   "Slug, System prompt, LLM override, Chat/Query RadioGroup), *Retrieval* (Top-K, similarity threshold, "
   "reranker option), *Danger* (Delete workspace).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 zero-size; the only geometry hit is a "
   "3×32 px adjacency between the *Retrieval* and *Danger* tab hit boxes, invisible in the screenshot — "
   "**not** a visual defect.",
   "The acceptance clause `members` has no control on this screen. That is by design: REQ-FN-041 deleted the "
   "role/capability and user↔workspace assignment stack outright."),

 "/profile": (
   f"renders ✓ (runtime-confirmed {D}) — offline banner, Personal information (Avatar, First/Last name, "
   "Email, Mobile), Change password (3 fields + rule hint), License panel with real values, Privacy (GDPR) "
   "with export/delete + email-confirm Input.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size; two-column layout "
   "holds at 1024.",
   "Every write path is disabled: `AppManager` is unreachable and no `AppManager:BaseUrl` ships, so profile "
   "update, password change and GDPR requests are **unexercised** (REQ-UI-010/011/012)."),
 "/pricing": (
   f"renders ✓ (runtime-confirmed {D}) — three tier cards with prices and feature ticks, `Current plan` "
   "badge on Free, `Most popular` on Professional, currency Select (USD), promo-code Input + Apply.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, cards reflow.",
   "Prices are TechieDesk's published list, not a live `GET /LicenseSvc/types` quote — the screen says so. "
   "Multi-currency conversion is **unexercised**."),
 "/billing": (
   f"renders ✓ (runtime-confirmed {D}) — License panel populated (Key/Status/Plan/Expires/Devices); "
   "Active subscription, Transactions and Invoices each render a labelled empty state that names *why* "
   "(“nothing was fetched — this instance has no licence server to ask”).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Subscription cancel and invoice-PDF download (REQ-UI-030/031) are **unexercised** — no AppManager."),
 "/support": (
   f"renders ✓ (runtime-confirmed {D}) — gated correctly: `New issue` is disabled, the status filter is "
   "disabled and the list shows a labelled empty state explaining that no support account exists.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "The create-issue Dialog, comment thread, attachments and change-priority (REQ-UI-032/033/047) sit behind "
   "the disabled `New issue` button and are **unreachable on this install**."),
 "/login": (
   f"renders ✓ (runtime-confirmed {D}) — AuthLayout (no sidebar), Email + Password Inputs, "
   "`Forgot password?`, `Sign in`, `Create one`, `Continue without an account`, and the offline banner.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, card stays centred.",
   "✅ The sidebar `Sign in` link **is** actionable — driven this run with W3C pointer actions at the "
   "element centre. The older “not clickable” claim was a harness artifact (`element/click` no-ops on "
   "WebView content), not an app defect. Sign-in itself is **unexercised**: AppManager is unreachable and no "
   "test account may be invented (`_smoke-test-policy.md`)."),
 "/register": (
   f"renders ✓ (runtime-confirmed {D}) — First/Last name, Email, optional Mobile, Password + Confirm "
   "with the complexity hint, `Create account`, and the banner explaining there is no account to create.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Registration is **unexercised** — no licence server."),
 "/forgot-password": (
   f"renders ✓ (runtime-confirmed {D}) — Email Input, `Send reset link`, `← Back to sign in`. "
   "Reached from `/login` → `Forgot password?`.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.", None),
 "/reset-password": (
   f"⚠ **NOT RUNTIME-VERIFIED ({D})** — the route is reachable only from an emailed reset link "
   "carrying a token, and no mail path exists on this install. Render-status remains unconfirmed.",
   f"visual gate not run ({D}) — screen not reached.", None),

 "/admin/events": (
   f"renders ✓ (runtime-confirmed {D}) — **proven with real data.** The screen was empty on arrival "
   "(the `EventLog` table held 0 rows), so a real config change was made on `/admin/settings` and saved; the "
   "row then rendered with every column populated (Time / Category `Configuration` / Actor `you` / Event / "
   "Source `admin:settings`), the header count `1 events · configuration` matched `Showing 1–1 of 1`, "
   "and the **Details Dialog** rendered its Summary / Raw record / Related events tabs with the correlation id.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size, dialog centred.",
   "⚠ **Coverage gap (not a render defect).** `IEventLogRepository` has exactly **one** producer — "
   "`AppSettingsChangeLog`, driven only by the `/admin/settings` save. The screen's own subtitle and REQ-UI-026 "
   "both promise “auth, ingestion and configuration changes”, yet 12 ingested documents and 5 connector "
   "runs on this install produced **zero** events. Auth and ingestion have no writer at all."),
 "/admin/settings": (
   f"renders ✓ (runtime-confirmed {D}) — **all three tabs driven.** *Defaults*: Default LLM, Default "
   "embeddings, Vector store Selects and the Max-upload NumericInput all show live values; Save works and is "
   "audited. *Branding*: AppearancePanel (Theme radios, 5 accent swatches, Language picker) **plus** the "
   "`WHITE_LABEL` FeatureGate rendering its upgrade prompt — the correct render for a Free install, not a "
   "missing form. *Updates*: the hosted `AppUpdates Embedded=\"true\"` surface.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size on every tab.",
   "✅ **`ForceMount=\"true\"` on all three `<TabsContent>` (the 2026-07-29 `aria-controls` fix) causes no "
   "visual regression and no duplicate heading.** Verified directly: inactive panels mount hidden and contribute "
   "**nothing** to the accessibility tree (the Defaults fields disappear from the element dump the moment "
   "Branding is active), exactly one `App settings` `<h1>` is present on every tab, and the embedded "
   "`AppUpdates` emits no second `<h1>`/`<PageTitle>` — its standalone `/settings/updates` copy does show "
   "its own `Updates` heading, confirming the `Embedded` switch works."),
 "/automations": (
   f"renders ✓ (runtime-confirmed {D}) — *Schedules* tab shows a real scheduled job (plain-language "
   "“Every 5 minutes”, last run, paused state) with working pagination; *Run history* shows 5 populated "
   "runs (Outcome/Items/Started/Duration/Trigger); the `New schedule` Dialog renders the natural-language "
   "authoring surface (free-text description + `Interpret`).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size on every tab.",
   "The *Flows* tab renders an honest “**Flows are not part of this build**” panel — consistent with "
   "REQ-UI-040 `Not Started`. NL interpretation is **unexercised**: the dialog itself reports “No local model "
   "is configured”."),
 "/settings/data": (
   f"renders ✓ (runtime-confirmed {D}) — data-directory path with Copy, `Healthy` badge, disk-usage "
   "summary, and a 9-row artefact table with real sizes and timestamps plus per-row `Reveal`.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps; the table compresses without "
   "horizontal overflow at 1024.",
   "The `uploads` artefact reads `not created yet` — original source documents are not retained, which is the "
   "same root cause as the blank `Size` column on `/workspace/{Slug}/documents`."),
 "/settings/updates": (
   f"renders ✓ (runtime-confirmed {D}) — `Installed` badge, `Version 1.0`, `Not checked yet`, "
   "`Check for updates`, and three preference Switches with their explanations.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Standalone it emits its own `Updates` `<h1>`; hosted on `/admin/settings` with `Embedded=\"true\"` it emits "
   "none. Both were observed this run."),

 "/qdrant-admin": (
   f"renders ✓ (runtime-confirmed {D}) — Docker-daemon card (endpoint-kind Select, address Input, "
   "TLS Switch, Test/Use buttons, active-endpoint panel, `Not connected` badge) and the Qdrant connection card "
   "(Host/Port/API key, 4 status tiles, connection string + Copy). Failures render honestly and specifically "
   "(“The Docker socket /var/run/docker.sock is not present…”).",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Collection CRUD, point browse/detail and container lifecycle (REQ-UI-003) are behind a live connection and "
   "are **unreachable on this host** — there is no Docker daemon and no Qdrant. This is an environment "
   "dependency, not a defect."),
 "/llm-settings": (
   f"renders ✓ (runtime-confirmed {D}) — **all three tabs driven.** *Provider*: Source Select with 7 "
   "providers plus the Resilience card. *Usage*: token-tracking Switch, Max total tokens, Max cost, the "
   "alert-threshold Slider and `Block Requests When Exceeded`. *Prompts*: system prompt, RAG and context "
   "templates, context limits.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size on every tab.",
   "✅ **REQ-UI-043 proven end-to-end.** Selecting `OpenAI-compatible endpoint` swapped the visible field set "
   "(Base URL / Model / API key / Max tokens / Temperature / Test connection) — no union-of-all-providers "
   "form — and `Save & apply` was **refused** with the error named on each offending field "
   "(`Base URL required`, `Model required`, `API key required`) plus a `This provider is not fully configured` "
   "summary. The named regression (saving OpenAI-compatible with no endpoint) is prevented. Configuration was "
   "restored to `None` afterwards."),
 "/token-usage": (
   f"renders ✓ (runtime-confirmed {D}) — 4 stat tiles populated (Total tokens, Input/Output, Estimated "
   "cost, Operations), `Usage by Model` with a labelled empty state, `Reset Session`. **Budget Status proven**: "
   "setting a budget on `/llm-settings` → *Usage* made the card appear with both `<Progress>` bars "
   "(`Token budget used`, `Cost budget used`) and correct `0 / 10,000` and `$0.0000 / $5.00` values.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size, with and without "
   "the Budget Status card.",
   "The Budget Status card is **correctly conditional**, not renders-empty: it appears only once a budget is "
   "configured. Budgets were reverted to 0 after the check. Block-on-exceed enforcement needs a live provider "
   "and is **unexercised**."),
 "/llm-playground": (
   f"renders ✓ (runtime-confirmed {D}) — 3 tabs (Completion / Structured Output / Chat), system and "
   "user prompt Textareas, Temperature, Max tokens, Streaming Switch, `Generate`.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Generation is **unexercised** — no LLM provider is reachable on this host."),
 "/ingestion": (
   f"renders ✓ (runtime-confirmed {D}) — **now observed** (was `not observed` on 2026-07-28). Driven "
   "through the native Go menu → *Document Ingestion* ⌣03. Folder-path and pattern Inputs, "
   "`Choose files…`/`Choose folder…`, `Ingest Now`, a Vector-store statistics card (12 documents / 13 "
   "chunks / 100.0 KB / last ingestion) and an `Ingested Documents` table with 12 rows across 3 pages.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "This screen's `Storage Size` column **does** show real byte sizes, unlike the workspace document library — "
   "the two read different stores."),
 "/text-ingestion": (
   f"renders ✓ (runtime-confirmed {D}) — Document-name Input, content Textarea with a live "
   "character/word counter, source Input, `Ingest Text`/`Clear Form`/`Clear All Data`, a Statistics card with "
   "real values and a Documents list with per-row delete.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.", None),
 "/chat": (
   f"renders ✓ (runtime-confirmed {D}) — **now observed** (was `not observed` on 2026-07-28). Driven "
   "through the native Go menu → *RAG Chat* ⌣02. Chat-configuration card (Mode, Doc filter, Top-K, "
   "Streaming), session counters, message Input and `New Conversation`/`Clear Chat`.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.",
   "Answering is **unexercised** — no LLM provider is reachable."),
 "/rag-config": (
   f"renders ✓ (runtime-confirmed {D}) — Embedding configuration (Source `Ollama`, model `bge-m3`, "
   "endpoint), Vector store (`SqliteVec`, `techierag.db`), Document processing (chunk size 500 / overlap 50), "
   "Advanced settings (telemetry Switch), `Reset to Defaults` / `Save Configuration`.",
   f"looks-right ✓ (runtime-confirmed {D}) — 1600 and 1024: 0 overlaps, 0 zero-size.", None),

 "/": (
   f"renders ✓ (runtime-confirmed {D}) — **now observed** (was `not observed` on 2026-07-28). Driven "
   "through the native Go menu → *Home* ⌣01. Behaves exactly as documented: `/` is a redirect, and with "
   "one workspace present it lands on `/workspace/default` (breadcrumb `Workspace: Default › Chat`). The "
   "`Spinner` and failure `Alert` are transient/failure-only and did not appear — correct, not renders-empty.",
   f"looks-right ✓ (runtime-confirmed {D}) — the redirect target passes both widths; the redirect itself "
   "paints no lasting UI.", None),

 "/setup": (
   f"⚠ **NOT RUNTIME-VERIFIED ({D})** — `/setup` has no inbound link and `MainLayout.GuardFirstRunAsync` "
   "returns early whenever a workspace exists, so the only way in from this install is to delete its single "
   "`Default` workspace (3 embedded documents, connectors and run history). That is destructive to the owner's "
   "data and was **not** performed. Render-status remains unconfirmed.",
   f"visual gate not run ({D}) — screen not reached.", None),
}

HDR = (f"> ✅ **Runtime-verified {D}** — re-swept on the live **Mac Catalyst** head over Appium "
       f"(`mac2`), bound by `appPath` to the universal Release bundle, driving **28 of the 30 screens** at "
       f"**1600×1240** and **1024×720** (the REQ-UI-041 floor). Every `Observed` line below is what the "
       f"running app did; `Visual (§4b)` is the overlap / zero-size / off-viewport geometry check plus a "
       f"human look at the screenshot. Screens that could not be reached say so and are **not** claimed as "
       f"verified. Screenshots: `test-results/ui-verify/`.\n")

FILES = {
 "Workspace.md": ["/workspace/{Slug}", "/workspace/{Slug}/documents", "/workspace/{Slug}/documents/web",
                  "/workspace/{Slug}/connectors", "/workspace/{Slug}/connectors/new",
                  "/workspace/{Slug}/agents", "/workspace/{Slug}/settings"],
 "Account.md": ["/profile", "/pricing", "/billing", "/support", "/login", "/register",
                "/forgot-password", "/reset-password"],
 "Operator.md": ["/admin/events", "/admin/settings", "/automations", "/settings/data", "/settings/updates"],
 "Console.md": ["/qdrant-admin", "/llm-settings", "/token-usage", "/llm-playground", "/ingestion",
                "/text-ingestion", "/chat", "/rag-config"],
 "Shell.md": ["/"],
 "FirstRun.md": ["/setup"],
}


def patch(fname, routes):
    path = BASE + fname
    text = open(path, encoding="utf-8").read()

    # 1. replace the file-level runtime-verified banner (the first '> ✅' or '> ⚠' block line)
    text = re.sub(r"^> [✅⚠][^\n]*\n(?:>[^\n]*\n)*", HDR, text, count=1, flags=re.M)

    # 2. per-screen Observed / Visual / Known issues
    for route in routes:
        head = f"## `{route}` —"
        i = text.index(head)
        j = text.find("\n## ", i + 1)
        j = len(text) if j == -1 else j
        block = text[i:j]
        obs, vis, known = OBS[route]

        new_obs = f"- **Observed:** {obs}\n- **Visual (§4b):** {vis}\n"
        if known:
            new_obs += f"- **Known issues ({D}):** {known}\n"

        block2, n = re.subn(r"- \*\*Observed:\*\*[^\n]*\n(?:- \*\*Visual \(§4b\):\*\*[^\n]*\n)?"
                            r"(?:- \*\*Known issues[^\n]*\n)?", new_obs, block, count=1)
        assert n == 1, f"no Observed line in {route}"
        text = text[:i] + block2 + text[j:]

    open(path, "w", encoding="utf-8").write(text)
    print(f"updated {path} ({len(routes)} screens)")


for f, r in FILES.items():
    patch(f, r)
