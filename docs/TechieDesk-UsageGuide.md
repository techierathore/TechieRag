# TechieDesk — Usage & Testing Guide

> **What this is:** a case-study-driven runbook for a person doing UAT. It shows you how to stand TechieDesk up (offline in minutes, or wired to a live AppManager), then walks you through realistic scenarios that exercise every Phase-1 feature so you can tick each one Pass/Fail. Work top-to-bottom the first time; after that, jump to whichever case study you need. Markdown source — the orchestrator renders the HTML.

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Two run modes](#2-two-run-modes)
3. [Connecting TechieDesk to AppManager (credentials)](#3-connecting-techiedesk-to-appmanager-credentials)
4. [Test users](#4-test-users)
5. [Case studies (usage + testing)](#5-case-studies-usage--testing)
   - [CS-1 First-run offline setup](#cs-1-first-run-offline-setup-via-setup-wizard)
   - [CS-2 Provision AppManager & connect](#cs-2-provision-appmanager--connect)
   - [CS-3 Account lifecycle](#cs-3-account-lifecycle)
   - [CS-4 Roles & authorization](#cs-4-roles--authorization)
   - [CS-5 Workspace management](#cs-5-workspace-management)
   - [CS-6 Document library](#cs-6-document-library)
   - [CS-7 Chat with citations](#cs-7-chat-with-citations)
   - [CS-8 Threads & history](#cs-8-threads--history)
   - [CS-9 Licensing & feature gating](#cs-9-licensing--feature-gating)
   - [CS-10 Self-host with Docker + Postgres](#cs-10-self-host-with-docker--postgres)
   - [CS-11 Logging & resilience](#cs-11-logging--resilience)
6. [UAT traceability matrix](#6-uat-traceability-matrix)
7. [Known limitations & notes for testers](#7-known-limitations--notes-for-testers)

---

## 1. Prerequisites

You do not need everything below for every scenario — the offline path (CS-1, CS-5–CS-8) needs almost nothing. Grab the rest as the case studies call for them.

| # | What | Needed for | Notes |
|---|------|-----------|-------|
| 1 | **.NET 10 SDK** | Everything run from source | `dotnet --version` should report 10.x. |
| 2 | **An LLM provider for live chat** | CS-7 streamed answers, chat replies in CS-5/CS-8 | Ollama at `http://localhost:11434` with a chat model (`ollama pull llama3.1`), or LM Studio at `http://localhost:1234`. **Ingestion/embedding do NOT need this** — they run fully offline via the bundled BGE-M3 model. Without an LLM, chat replies with `No LLM provider is configured. Configure one in LLM Settings to chat.` |
| 3 | **A running AppManager instance** | CS-2, CS-3, CS-4, CS-9 (multi-user auth, roles, licensing) | Only required for AppManager-connected mode. If you don't have one, those scenarios are explicitly marked and you observe the offline/degraded behaviour instead. |
| 4 | **Docker + Docker Compose** (optional) | CS-10 (self-host + Postgres/pgvector), optional Qdrant/Ollama profiles | The daemon must be running. Without Docker, CS-10 is not exercisable — skip it and note "no daemon". |

The BGE-M3 embedding model (~2.3 GB ONNX) is bundled/downloaded on first use so that **ingestion, embedding, and retrieval work with zero external services**.

---

## 2. Two run modes

TechieDesk boots in one of two modes, decided by whether `AppManager:BaseUrl` is set.

- **Mode A — Offline single-user.** `AppManager:BaseUrl` is empty (the shipped default). The app signs you in automatically as the built-in **Administrator** — there is no login screen, and `/login` shows `This instance runs in offline single-user mode — no login is required.` Fastest path; zero external services. This is the mode CS-1 and CS-5–CS-8 assume.
- **Mode B — AppManager-connected.** `AppManager:BaseUrl` is set (plus API key/secret). The login/register screens activate, roles and licensing come from AppManager, and the user menu exposes `Log out` / `Log out — all devices`. This is the mode CS-2, CS-3, CS-4, and CS-9 assume.

### What each mode lets you test

| Capability | Offline single-user (A) | AppManager-connected (B) |
|-----------|:----------------------:|:------------------------:|
| First-run `/setup` wizard | ✅ (choose "Offline single-user mode") | ✅ (choose "Connect AppManager") |
| Workspaces: create/rename/delete/settings | ✅ (you are Admin) | ✅ (role-gated) |
| Document library: upload/embed/dedupe/delete | ✅ | ✅ |
| Threads: create/resume/export/delete | ✅ | ✅ |
| Chat with citations (needs an LLM) | ✅ | ✅ |
| Register / login / logout / forgot-reset | ❌ (no accounts) | ✅ |
| Profile edit / change password / GDPR export-delete | Local only (email "Managed by AppManager") | ✅ live |
| Roles: Admin vs Manager vs User, server-side authz | ❌ (always Admin) | ✅ |
| Licensing status, feature gating, upgrade prompts | Shows honest **Free (offline)** | ✅ live license + grace |
| Multi-user workspace scoping | Unit-asserted only (single user live) | ✅ live |

Run from source (either mode) with:

```bash
dotnet run --project apps/TechieDesk
```

It listens on **https://localhost:60355** (and http://localhost:60356). Or self-host via Docker (CS-10): `docker compose up -d --build`.

> **Secrets rule:** API keys/secrets must come from environment variables or `dotnet user-secrets` — never commit them. `apps/TechieDesk/appsettings.json` ships with `AppManager:ApiKey` / `ApiSecret` **empty on purpose**.

---

## 3. Connecting TechieDesk to AppManager (credentials)

TechieDesk authenticates, authorizes, and licenses through your **AppManager** instance. From AppManager you need three values for the TechieDesk application — its **Application ID**, an **API key**, and an **API secret** (the `ak_live_...` key + secret AppManager issues under the application's settings) — plus at least one **superadmin** account with the **Admin** role. How you obtain those inside AppManager depends on your AppManager build; this section shows **where each value goes in TechieDesk**.

TechieDesk reads the `AppManager` section straight from configuration (`appsettings.json` is the base layer) — **no environment variables are required and no code change is needed**. Wire the credentials one of three ways:

**(a) `appsettings.Development.json` — recommended for local development (this is how the app is configured now).** Create `apps/TechieDesk/appsettings.Development.json` (already **gitignored**, so the secret is never committed) with your real values. .NET automatically layers it over `appsettings.json` whenever `ASPNETCORE_ENVIRONMENT=Development`, which `launchSettings.json` sets by default — so you edit one JSON file, no env vars:

```json
// apps/TechieDesk/appsettings.Development.json  — gitignored, never committed
{
  "AppManager": {
    "BaseUrl": "https://192.168.1.14:5101/",
    "ApiKey": "ak_live_your_key",
    "ApiSecret": "sk_live_your_secret",
    "ApplicationId": "5"
  }
}
```
Then just `dotnet run --project apps/TechieDesk` — the startup log prints `auth mode: AppManager — login required, roles enforced server-side`, confirming AppManager mode is active. The **tracked** `apps/TechieDesk/appsettings.json` keeps its `AppManager` values **empty** (only the non-secret tuning defaults `TokenRefreshLeadSeconds`/`LicenseGraceHours`/`LicenseRevalidationMinutes` stay there).

> `appsettings.Development.json` loads only in the **Development** environment (the default via `launchSettings.json`). For a Production run or the Docker image it is not loaded — supply the credentials via (b) instead.

> **Self-signed AppManager certificate?** If your AppManager host serves a self-signed / untrusted TLS certificate (common for a LAN instance like `https://192.168.1.14:5101/`), add `"AllowUntrustedServerCertificate": true` to the `AppManager` block in `appsettings.Development.json`. TechieDesk then trusts that certificate **for the AppManager client only, and only in the Development environment** — the flag is ignored outside Development, so it can never weaken TLS validation in a real deployment. (In production, install a proper CA-signed or trusted certificate on the AppManager host instead.)

**(b) Environment variables / `.env` — for Docker & production.** Nested keys use `__` (double underscore); this is what `docker-compose.yml` consumes:

```dotenv
AppManager__BaseUrl=https://your-appmanager-host/
AppManager__ApiKey=ak_live_your_key
AppManager__ApiSecret=your_secret
AppManager__ApplicationId=5
```

**(c) User-secrets** (Development alternative to (a)): from `apps/TechieDesk`, `dotnet user-secrets set "AppManager:ApiKey" "ak_live_..."` and likewise for `AppManager:ApiSecret`, with the non-secret `BaseUrl`/`ApplicationId` in `appsettings.Development.json` or `appsettings.json`.

> The `/setup` wizard's **Connect AppManager** step (REQ-UI-023) can set the **BaseUrl** to enable AppManager mode, but it deliberately does **not** persist secrets to disk — you still provide `ApiKey`/`ApiSecret` via (a) or (b) and restart.

> Setting `AppManager:BaseUrl` (non-empty) switches TechieDesk from offline single-user mode into AppManager mode; leaving it empty keeps the app offline (see §2). The API key auto-resolves the Application ID on most calls, so `ApplicationId` is optional but recommended.

**Superadmin & roles.** Sign in first as the superadmin **`admin@appmanager.local`** (full credentials in §4), which must carry the AppManager **Admin** applicationRole for the TechieDesk application. TechieDesk maps the app-scoped `applicationRole` → its product roles **Admin / Manager / User** (REQ-FN-005). Additional Manager and User test accounts are listed in §4 — create them in AppManager with those roles, or register standard Users from the app's own `/register` screen (which defaults new users to the `User` role).

> **RSA password encryption is automatic.** Per §2.4 of the API guide, every password field is RSA-OAEP-SHA256 encrypted against the server's public key (`GET /AuthSvc/public-key`) before it leaves the browser. TechieDesk fetches and caches that key and encrypts for you (REQ-FN-001) — there is nothing to configure. The auth footer even says so: `Passwords are RSA-encrypted before transmission · Accounts managed by AppManager`.

---

## 4. Test users

> ⚠️ **These are LOCAL UAT credentials for a self-hosted test instance. Rotate or remove them before any real deployment. Never reuse them in production.**

Create these three accounts in your AppManager instance and assign the roles shown (wire the credentials per §3). The superadmin is owner-provided and must be entered verbatim.

| Account (email) | Password | Role (applicationRole → product role) | Purpose |
|-----------------|----------|---------------------------------------|---------|
| `admin@appmanager.local` | `Admin@123!` | **Superadmin (AppManager Admin)** → Admin | Owner/superadmin. Full instance + all workspaces; first-Admin login in CS-2; the "denied action succeeds here" control in CS-4. |
| `manager@appmanager.local` | `Manager@123!` | Manager | Workspace/document/connector management. Middle tier in CS-4. |
| `user@appmanager.local` | `User@123!` | User | Assigned workspaces + own data only. The "denied server-side" subject in CS-4; the scoping subject in CS-5. |

All three passwords satisfy AppManager complexity (8+ chars, 1 uppercase, 1 number, 1 special) so registration/reset won't bounce with `INVALID_PASSWORD`.

---

## 5. Case studies (usage + testing)

Each case study is a short story with the exact routes, controls, and the proof that it worked. Tick the **Result** line as you go. Where a scenario needs a live service (AppManager, an LLM, Docker), the **Preconditions** say so and note what to observe if it's absent — never assume a live check happened that didn't.

---

### CS-1 First-run offline setup (via /setup wizard)

**Scenario.** Priya just cloned the repo onto a laptop with no Ollama, no Docker, no AppManager. She wants a chat-able RAG instance in minutes, entirely offline.

**Covers:** REQ-UI-022, REQ-UI-023, REQ-FN-016, REQ-FN-009.

**Preconditions.** Mode A (offline). No accounts. No external services required. (Ollama optional — this scenario deliberately assumes it is absent to prove graceful fallback.)

**Steps.**
1. `dotnet run --project apps/TechieDesk`, then open **https://localhost:60355**. A fresh instance routes you to **`/setup`** (title *TechieDesk — First-run setup*).
2. **Step "Defaults":** confirm the green *Zero-config defaults ready* alert — "Offline embeddings (Embedded BGE-M3) + SqliteVec vector store". Click **Continue**.
3. **Step "AI Provider":** the wizard probes for a local Ollama (2s). With none present you get the info alert *"No Ollama detected. You can stay embedded-only (offline)…"*. Leave **Skip — embedded-only for now** selected. Click **Continue**.
4. **Step "AppManager":** choose **Offline single-user mode** ("No accounts — sign in automatically as the built-in Administrator."). Click **Continue**.
5. **Step "Admin account":** with offline mode chosen you see the info alert *"you'll sign in automatically as the built-in Administrator. No account setup is required."* Click **Continue**.
6. **Step "Workspace":** leave the name **Default** (or rename). Click **Finish setup**.

**Expected result.** The wizard applies Embedded BGE-M3 + SqliteVec, creates the default workspace, and drops you into the app shell (`TechieDesk` / *RAG + LLM Platform*) at `/workspace/default`. The sidebar shows your workspace under **Workspaces**; `/setup` is no longer forced on next launch. If Ollama *were* running, step 3 would instead show *"Ollama detected at … (N models)"* with a **Chat model** picker (that is REQ-FN-016's happy path). The instance is retrieval-ready with zero external services (chat itself waits for an LLM — see CS-7).

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-2 Provision AppManager & connect

**Scenario.** Owner wants the productized experience: real accounts, roles, licensing. He provisions the TechieDesk app in AppManager and connects the instance, then logs in as the first Admin.

**Covers:** REQ-FN-004, REQ-FN-001, REQ-UI-023, REQ-UI-007, REQ-FN-002, REQ-FN-003, REQ-NFR-002.

**Preconditions.** Mode B. **Requires a running AppManager instance.** If you have none, stop here and note "AppManager absent — connect step and first login not exercisable"; the app will simply remain in offline mode.

**Steps.**
1. In your AppManager instance, ensure the `TechieDesk` application exists with an **Application ID**, an `ak_live_...` **API key + secret**, and the superadmin `admin@appmanager.local` holding the **Admin** applicationRole; then wire those credentials into TechieDesk per §3 and restart.
2. Wire the credentials via §3 step 6 (any of the three forms). Confirm **`appsettings.json` still has empty `ApiKey`/`ApiSecret`** — the live values come from env/user-secrets only (REQ-NFR-002).
3. Restart the app. Because `AppManager:BaseUrl` is now set, the instance boots in AppManager mode.
4. Browse to any protected route (e.g. `/`). You are redirected to **`/login`** with a `returnUrl` (REQ-FN-003). The offline banner is gone.
5. On **`/login`** (*Welcome back* / *Sign in to your TechieDesk instance*) enter `admin@appmanager.local` / `Admin@123!` and click **Sign in**.

**Expected result.** Login succeeds and lands you on the originally requested route. Behind the scenes the single `AppManagerClient` sent `X-Api-Key`/`X-Api-Secret` on every call (REQ-FN-004), fetched the RSA public key and sent an `encryptedPassword` — no plaintext on the wire (REQ-FN-001, verify in browser dev-tools Network → the login request body carries `encryptedPassword`, not `password`). The header shows an **Admin** role badge; the user menu now exposes `Log out` and `Log out — all devices`. Sessions refresh silently before expiry (REQ-FN-002; `TokenRefreshLeadSeconds` = 120). A wrong password shows `Invalid email or password.`

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-3 Account lifecycle

**Scenario.** A new teammate, Meera, registers, signs in, tidies her profile, rotates her password, recovers a forgotten one, logs out everywhere, then exercises her GDPR rights.

**Covers:** REQ-UI-006, REQ-UI-007, REQ-UI-008, REQ-UI-009, REQ-UI-010, REQ-UI-011, REQ-UI-012, REQ-UI-013, REQ-FN-001, REQ-FN-002, REQ-FN-003.

**Preconditions.** Mode B. **Requires a running AppManager** with the TechieDesk app connected (CS-2 done). Without it, none of the round-trips below can complete — note "AppManager absent" and skip.

**Steps.**
1. **Register:** open **`/register`** (*Create your account*). Fill First name, Last name, Email, optional Mobile, Password (hint: *Min 8 chars, 1 uppercase, 1 number, 1 special character*), Confirm password. Click **Create account**. → `POST /AuthSvc/register`, auto-login on success.
2. **Login round-trip:** log out, then sign back in at **`/login`** to confirm credentials persist and the role/active-license are captured.
3. **Profile view/edit:** go to **`/profile`**. Edit First/Last name, Mobile, or **Change avatar URL**; note Email is disabled (*Managed by AppManager*). Click **Save changes**. → `PUT /UserSvc/profile`.
4. **Change password:** in the *Change password* card enter Current, New, Confirm; click **Update password**. → `POST /UserSvc/change-password` (both fields RSA-encrypted). A wrong current password maps to the field as an `INVALID_CURRENT_PASSWORD` error.
5. **Forgot/reset:** log out. On `/login` click **Forgot password?** → **`/forgot-password`**, submit the email, get the anti-enumeration success *"If that address exists, a reset email is on its way."* Use the emailed link to reach **`/reset-password?token=...`**, set a new password, click **Reset password**, confirm *"Your password has been reset. You can now sign in."*
6. **Logout (all devices):** sign in again; open the user menu → **Log out — all devices** → `POST /AuthSvc/logout` with `logoutAllDevices:true`.
7. **GDPR:** on `/profile` in the *Privacy (GDPR)* card click **Request data export** (→ `POST /UserSvc/data-export`) and **Request account deletion** — type your email to confirm the match (→ `POST /UserSvc/delete-request`, ~7-day completion).

**Expected result.** Every step round-trips cleanly: registration lands authenticated as **User**; profile edits persist; the password change and reset both let you log in with the new password; forgot-password never reveals whether the email exists; "all devices" revokes every refresh token (a second browser session is bounced to `/login` on its next action). Export/delete each return a request acknowledgement. Friendly auth-error banners (REQ-UI-013) appear on the matching AppManager responses — e.g. `Account locked — too many failed attempts. Try again in 15 minutes.` (ACCOUNT_LOCKED), `Account disabled — contact your administrator.` (ACCOUNT_DISABLED), and the NO_APP_ACCESS banner *"Your account doesn't have access to this application…"*.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-4 Roles & authorization

**Scenario.** Three people — Admin, Manager, User — sign into the same instance. You confirm each sees the right things, and critically that a plain **User is denied a role-gated action server-side**, not just hidden in the UI.

**Covers:** REQ-FN-005, REQ-FN-006, REQ-FN-007, REQ-FN-003, REQ-UI-013.

**Preconditions.** Mode B. **Requires a running AppManager** with all three §4 accounts and their applicationRoles assigned. Without it, the mapping and matrix are unit-asserted only (see the matrix); note "AppManager absent — live role check skipped".

**Steps.**
1. Sign in as `admin@appmanager.local`. Note the **Admin** role badge. Confirm the sidebar shows management affordances: **New workspace** button, per-workspace `⋯` menu (`Rename` / `Settings` / `Delete`), and the console groups.
2. Sign out; sign in as `manager@appmanager.local`. Confirm the **Manager** badge and that workspace/document management controls are present (create workspace, workspace `Settings`, document `Unembed`/`Delete`).
3. Sign out; sign in as `user@appmanager.local`. Confirm the **User** badge, that **only assigned workspaces** appear, and that management controls are absent (e.g. a document row shows **View only**, not `Unembed`/`Delete`; no **New workspace** button).
4. **Server-side denial:** while signed in as User, attempt a role-gated operation directly — e.g. navigate to **`/workspace/{slug}/settings`** for a workspace you can see. Expect the access-denied alert *"Not allowed — You need the Manager or Admin role to edit workspace settings."* For a true server-side proof, replay a manager-only request (e.g. the save-settings or delete-workspace call) with the User's session using dev-tools/curl.

**Expected result.** Roles map correctly (applicationRole → Admin/Manager/User, REQ-FN-005) and the capability matrix holds (REQ-FN-006): Admin = instance + all; Manager = workspace/doc/connector management; User = assigned workspaces + own data. The forged/replayed User request in step 4 is **rejected by the server** regardless of what the UI showed (REQ-FN-007) — UI hiding is never the only guard. As a sanity check, the same gated action **succeeds** when replayed as `admin@appmanager.local`.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-5 Workspace management

**Scenario.** Manager Arjun sets up a "Handbook" knowledge base, tunes how it retrieves, adds a teammate, and confirms a plain User sees only what they're assigned.

**Covers:** REQ-UI-014, REQ-UI-015, REQ-FN-008, REQ-RAG-014, REQ-RAG-015.

**Preconditions.** Either mode for the create/settings mechanics (offline you are Admin). The **User-scoping** check in step 6 needs Mode B with a real User account; offline it is single-user, so note "scoping unit-asserted only".

**Steps.**
1. In the sidebar **Workspaces** group click **New workspace**. In the dialog (*Create an isolated knowledge base…*) type `Handbook` and click **Create**. It appears in the switcher and you land on `/workspace/handbook`.
2. Rename via the workspace `⋯` menu → **Rename** (`Save`); confirm the sidebar and page title update.
3. Open **`/workspace/handbook/settings`** (button **Settings**). Walk the four tabs:
   - **General:** set a **System prompt** (e.g. *You are the company handbook assistant*), optionally an **LLM override** (blank = instance default), and pick a Mode: **Chat — general knowledge + documents** vs **Query — documents only, answers "not in my documents" honestly** (REQ-RAG-015).
   - **Retrieval:** drag **Similarity threshold** (e.g. 0.30), set **Top-K snippets** (e.g. 5), and try the **Accuracy optimized (reranker)** switch (REQ-RAG-014).
   - **Members:** under **Add**, enter a **User ID** and pick a **Role** (`User`/`Manager`/`Admin`), click **Add** (REQ-FN-008).
   - Click **Save changes**.
4. Confirm the header badge on `/workspace/handbook` reflects **Query mode** or **Chat mode** to match your choice.
5. Use the sidebar switcher to hop between workspaces and confirm the active one drives the Documents sub-link and chat scope.
6. **Scoping (Mode B):** sign in as `user@appmanager.local` who is a member of `Handbook` but not of another workspace. Confirm the sidebar lists **only** `Handbook`.
7. **Delete:** back as Manager/Admin, Settings → **Danger** tab → **Delete workspace** (or sidebar `⋯` → `Delete`). Confirm the *"Delete '{name}'?"* dialog before it removes the workspace (threads + doc links; files stay in the library).

**Expected result.** Create/rename/delete all work with confirmation on delete; the four settings tabs persist their values (re-open to confirm); the mode badge matches; and in Mode B the User sees only assigned workspaces (REQ-FN-008). Retrieval threshold/top-K/rerank persist per workspace (REQ-RAG-014).

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-6 Document library

**Scenario.** Arjun loads the Handbook workspace with files, watches them embed, uploads a duplicate to see dedupe, pins a key doc, tries an unsupported spreadsheet, and deletes a document cleanly.

**Covers:** REQ-UI-019, REQ-UI-020, REQ-UI-021, REQ-FN-012, REQ-RAG-011, REQ-RAG-012, REQ-RAG-013.

**Preconditions.** Either mode. **No LLM needed** — embedding runs offline via bundled BGE-M3 (first upload may show *Preparing embedding model (BGE-M3)* while it loads).

**Steps.**
1. Open **`/workspace/handbook/documents`** (title *Handbook — Documents*).
2. **Multi-upload:** drag several files (PDF, DOCX, MD, TXT) onto the dropzone, or **browse** — the hint reads *"Drag & drop or browse — PDF, DOCX, MD, TXT, HTML, CSV, JSON + 70 code types. Spreadsheets/presentations (XLSX/PPTX) are coming in a later release."* (max 64 MB/file). Watch the **Uploads (N)** card show per-file status **Queued → Embedding… → Embedded** (REQ-UI-019, REQ-UI-020).
3. **Dedupe:** upload one of the *same* files again (or into a second workspace). Its status should read **Reused (dedupe)** — embedded once, reused by content hash (REQ-RAG-012).
4. **Pin:** in the library table, pin a document (pin control in the first column); optionally toggle **Pin new uploads** for future uploads (REQ-RAG-013).
5. **Metadata table:** confirm the **Library (N documents)** table columns render — **Name · Type · Size · Chunks · Uploaded · Workspaces · Status** — and that on a 390px-wide viewport the table scrolls inside its container with no page overflow (REQ-UI-021). *(Note: **Size** shows a real byte size for anything added on or after 2026-07-30. Documents added before that date show "—" — see limitations.)*
6. **Unsupported type:** upload an `.xlsx` or `.pptx`. Expect a clear **Rejected** status (no crash) — the "coming in a later release" path (REQ-RAG-011).
7. **Delete:** on a document row click **Delete**; confirm the *"Delete '{name}'?"* dialog (it shows how many workspaces use it) and click **Delete everywhere**. This removes vectors from every using workspace (REQ-FN-012). Compare with **Unembed**, which only detaches the doc from *this* workspace.

**Expected result.** Multiple files queue with independent progress and reach **Embedded**; a re-upload shows **Reused (dedupe)**; pinning persists with an indicator; the metadata table renders and scrolls correctly at 390px; XLSX/PPTX are cleanly **Rejected**; and Delete-everywhere drops the document's vectors from all workspaces after confirmation, leaving the empty-state *"No documents yet"* when the last one goes.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-7 Chat with citations

**Scenario.** With the Handbook embedded, Meera asks questions and expects grounded, streamed answers with citation chips she can expand — and honest "not in my documents" when she asks something off-topic in Query mode.

**Covers:** REQ-RAG-007, REQ-RAG-010, REQ-UI-018, REQ-RAG-015.

**Preconditions.** Documents embedded (CS-6). **Live streamed answers need an LLM provider** (Ollama/LM Studio, prereq #2). **Without an LLM the chat replies `No LLM provider is configured…`** and citation chips cannot render (they need a live RAG answer) — in that case verify only that the chat pane and composer render, and note "LLM absent — streamed citations pending provider UAT".

**Steps.**
1. Open **`/workspace/handbook`**. Confirm the muted subtitle *"Chat — retrieval scoped to this workspace's documents."*
2. Ask a question answerable from the embedded docs, e.g. *"What is our leave policy?"* Send (Enter; Shift+Enter for a newline). Watch the states **Retrieving & thinking… → Streaming…**.
3. When the answer arrives, click **Show sources (N)** and expand a chip → confirm it shows the **document name**, a **snippet**, and a **relevance 0.00** score (REQ-UI-018). Sources are workspace-scoped (REQ-RAG-007).
4. Switch the workspace to **Query mode** (CS-5) and ask something clearly *not* in the documents, e.g. *"What's the weather in Paris?"* — expect the deterministic *"The workspace documents do not contain information relevant to this question."* (REQ-RAG-015).

**Expected result.** In Chat mode you get a streamed answer grounded in this workspace's documents, with native citation chips that expand to doc/snippet/score (REQ-RAG-010, REQ-UI-018) — no cross-workspace leakage (REQ-RAG-007). In Query mode, an off-topic question returns the honest not-in-documents answer rather than a hallucination (REQ-RAG-015).

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-8 Threads & history

**Scenario.** Meera keeps several conversations in the Handbook, resumes one after re-login, exports a thread for a colleague, and finally wipes her history.

**Covers:** REQ-UI-016, REQ-UI-017, REQ-FN-010, REQ-FN-011, REQ-RAG-008, REQ-RAG-009.

**Preconditions.** Either mode. Resuming with *populated* context (step 4) needs a prior LLM answer (an LLM); without one you can still create/rename/delete/export empty threads — note "no LLM: resume shows persisted messages only".

**Steps.**
1. In `/workspace/handbook`, in the **Threads** panel click **New thread** — a persisted *New conversation* row replaces the *No threads yet* empty state (REQ-UI-016, REQ-RAG-008).
2. Send a couple of messages, then create a second thread and send a different message. Confirm the list orders **newest-first** (REQ-UI-017).
3. Rename a thread: thread `⋯` menu → **Rename** → set a title → **Save**.
4. **Resume after re-login:** sign out (or restart the app in offline mode) and return to `/workspace/handbook`; select the earlier thread and confirm its full message history reloads with citations (REQ-UI-017, REQ-RAG-008), and that follow-up answers use prior turns as token-trimmed context (REQ-RAG-009).
5. **Export:** thread `⋯` menu → **Export as Markdown**, then **Export as JSON** — both download files carrying roles/content/sources/timestamps (REQ-FN-010).
6. **Delete one:** thread `⋯` → **Delete** → confirm *"This permanently removes the thread and all of its messages. This cannot be undone."* → **Delete**.
7. **Delete all:** the trash icon (title **Delete all my history**) → confirm *"This permanently removes every conversation thread and message you own across all workspaces…"* → **Delete everything** (REQ-FN-011).

**Expected result.** Threads create/rename/delete with confirmation; the list is newest-first; a resumed thread reloads its persisted messages and continues with context (REQ-RAG-008/009); Markdown and JSON exports download with full content; and "Delete all my history" wipes the store to zero threads/messages.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-9 Licensing & feature gating

**Scenario.** Owner checks the license status card, bumps into a gated feature, sees the upgrade path and pricing, then simulates an AppManager outage to confirm the cached-license grace behaviour.

**Covers:** REQ-FN-013, REQ-FN-014, REQ-FN-015.

**Preconditions.** Mode B for live license/features (**requires a running AppManager**). Offline, the card honestly shows **Free (offline)** and gates fall back to the free tier — still worth checking, but note "offline: license is Free (offline), not a live AppManager license". The outage simulation (step 4) needs a previously-cached live license, so requires AppManager to have been reachable at least once.

**Steps.**
1. Open **`/profile`** and read the **License** card (*Your current TechieDesk plan and its validation status*): license name · status badge, **Expires**, **Days remaining**, **Last validated … UTC** (REQ-FN-013). Offline it shows a **Free (offline)** style badge.
2. Hit a **gated feature** — e.g. a Connectors entry on Home. Expect an **upgrade prompt** rather than the feature (REQ-FN-014).
3. Follow the prompt (or the card's **View plans** button) to **`/pricing`** (*Plans & pricing*). Confirm the three tiers: **Free** ($0/forever), **Professional** ($99.99/yr, *Most popular*, lists *Connectors / Agents / Embed widget / 10k API requests/mo*), **Enterprise** (Custom, *White-label*). Your current tier shows a **Current plan** badge and its button reads **Your plan** (disabled).
4. **Simulate an AppManager outage:** stop AppManager (or block its URL) and reload `/profile`. Within the grace window (`AppManager:LicenseGraceHours` = 72) the card shows a **Cached** badge and the warning *"Running on cached license"*, and a top-of-shell cached-license banner appears with a **View plans** link (REQ-FN-015). Past the grace window it degrades to *"License verification unavailable"*.

**Expected result.** The status card reflects live/cached/offline honestly; a gated feature yields an upgrade prompt that routes to the three-tier `/pricing`; and an AppManager outage keeps the last-known-good license working under the grace banner, then degrades after expiry — no hard crash either way.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-10 Self-host with Docker + Postgres

**Scenario.** Owner deploys the instance the way a customer would: `docker compose up`, all config via env vars, on Postgres/pgvector, and confirms data survives a container recreate.

**Covers:** REQ-FN-017, REQ-FN-018, REQ-FN-019, REQ-FN-030, REQ-FN-031, REQ-FN-029, REQ-NFR-002.

**Preconditions.** **Requires Docker + Docker Compose running.** No daemon → this case study is not exercisable; note "no Docker daemon — validated by compose config inspection only". Run from the repo root.

**Steps.**
1. Copy `.env.example` → `.env` and set at least `POSTGRES_PASSWORD` (default `change-me-please`), and — for AppManager mode — `AppManager__BaseUrl` / `AppManager__ApiKey` / `AppManager__ApiSecret` / `AppManager__ApplicationId`. Leaving `AppManager__BaseUrl` empty keeps it offline single-user (REQ-FN-018, REQ-NFR-002 — secrets via env, none committed).
2. Bring it up: **`docker compose up -d --build`**. This starts the **app** (`techiedesk:latest`) and **postgres** (`pgvector/pgvector:pg16`, health-gated). Optional profiles: `docker compose --profile qdrant up -d`, `docker compose --profile ollama up -d`.
3. Browse to `http://localhost:${APP_PORT:-8080}`.
4. **Migrations on start:** check the app container logs for the DbUp run against Postgres — *"Beginning database upgrade"* … *"Database migrations applied successfully"* (REQ-FN-030). The Postgres schema is the per-provider equivalent of the SQLite one (REQ-FN-031).
5. **Volume persistence:** create a workspace + upload a doc, then recreate the app container: `docker compose up -d --force-recreate app`. Reload — your data is still there because the named volumes persist: `techiedesk-data` (`/app/data`), `techiedesk-uploads` (`/app/uploads`), `techiedesk-models` (`/app/models`, the ~2.3 GB BGE-M3), and `techiedesk-pgdata` (`/var/lib/postgresql/data`) (REQ-FN-019).

**Expected result.** One command brings up app + pgvector; the app is reachable on the mapped port; DbUp applies migrations idempotently at boot against pgvector (data access is Dapper-only, zero EF Core — REQ-FN-029); and workspaces/docs/DB survive `--force-recreate` thanks to the named volumes. All config comes from env vars (12-factor).

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

### CS-11 Logging & resilience

**Scenario.** Owner confirms the instance is observable and degrades gracefully: rolling Serilog logs on both executable heads, and a survivable AppManager outage / provider fallback.

**Covers:** REQ-NFR-009, REQ-FN-015, REQ-NFR-010.

**Preconditions.** Either mode for logs. The outage grace check reuses CS-9 step 4 (needs a cached live license); provider-fallback observation needs an LLM to fail over from. Note any absent service rather than assuming a check ran.

**Steps.**
1. After a run, find the **rolling Serilog files** on both heads:
   - App head: `apps/TechieDesk/logs/techiedesk-<yyyymmdd>.log`
   - Migrator head: `apps/TechieDeskDb/logs/techiedeskdb-<yyyymmdd>.log`
   Confirm startup and migration outcomes are logged (REQ-NFR-009).
2. **Outage grace:** repeat CS-9 step 4 — with a cached license and AppManager down, confirm the cached-license grace banner appears and the app keeps working within the grace window (REQ-FN-015).
3. **Provider fallback / restart-safety:** with an LLM configured, induce a provider failure (stop Ollama mid-chat) and confirm the app surfaces a visible status/fallback rather than crashing; then restart the app and confirm persisted workspaces/threads/docs are intact (REQ-NFR-010).

**Expected result.** Both heads write dated rolling logs under `logs/` capturing startup + migrations + unhandled exceptions; an AppManager outage is ridden out under the grace banner; a provider failure degrades visibly (retry/fallback) instead of crashing; and a restart loses no persisted data.

**Result:** ☐ Pass ☐ Fail — notes: ______________________

---

## 6. UAT traceability matrix

Every Phase-1 REQ, the case study that exercises it, and its current standing. **Status legend:** ✅ *Verified* = already confirmed by the agent/verifier (per `docs/TechieDesk-Checklist.md`, 2026-07-18); ⏳ *Owner UAT* = implemented but awaiting this live owner UAT (typically needs AppManager / an LLM / Docker); ⚠️ *Blocked* = owner action outstanding. Tick the **UAT** box as you pass each here.

| REQ ID | Requirement (short) | Case study | Prior status | UAT |
|--------|---------------------|-----------|:-----------:|:---:|
| REQ-UI-006 | Register screen & flow | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-007 | Login screen & flow | CS-2, CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-008 | Logout current/all devices | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-009 | Forgot/reset password | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-010 | Change password in profile | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-011 | Profile view/update | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-012 | GDPR export/delete requests | CS-3 | ⏳ Owner UAT | ☐ |
| REQ-UI-013 | Friendly auth-error states | CS-3, CS-4 | ⏳ Owner UAT | ☐ |
| REQ-UI-014 | Workspace create/rename/delete | CS-5 | ✅ Verified | ☐ |
| REQ-UI-015 | Workspace settings page | CS-5 | ✅ Verified | ☐ |
| REQ-UI-016 | Thread create/rename/delete | CS-8 | ✅ Verified | ☐ |
| REQ-UI-017 | Browse & resume past threads | CS-8 | ⏳ Owner UAT | ☐ |
| REQ-UI-018 | Expandable citation UI | CS-7 | ⏳ Owner UAT (LLM) | ☐ |
| REQ-UI-019 | Drag-drop multi-file upload | CS-6 | ⏳ Owner UAT | ☐ |
| REQ-UI-020 | Embed/unembed live status | CS-6 | ⏳ Owner UAT | ☐ |
| REQ-UI-021 | Document metadata list | CS-6 | ⏳ Owner UAT | ☐ |
| REQ-UI-022 | Wizard offline defaults | CS-1 | ✅ Verified | ☐ |
| REQ-UI-023 | Wizard AppManager/offline mode | CS-1, CS-2 | ⏳ Owner UAT | ☐ |
| REQ-FN-001 | RSA password encryption + key cache | CS-2, CS-3 | ✅ Verified | ☐ |
| REQ-FN-002 | Silent token refresh + session | CS-2, CS-3 | ⏳ Owner UAT | ☐ |
| REQ-FN-003 | Route protection + deep-link return | CS-2, CS-4 | ⏳ Owner UAT | ☐ |
| REQ-FN-004 | AppManagerClient (API-key headers) | CS-2 | ✅ Verified | ☐ |
| REQ-FN-005 | applicationRole → product-role map | CS-4 | ✅ Verified | ☐ |
| REQ-FN-006 | Role capability matrix | CS-4 | ✅ Verified | ☐ |
| REQ-FN-007 | Server-side authz on every op | CS-4 | ✅ Verified | ☐ |
| REQ-FN-008 | User↔workspace assignment | CS-5 | ✅ Verified | ☐ |
| REQ-FN-009 | Default workspace bootstrap | CS-1 | ✅ Verified | ☐ |
| REQ-FN-010 | Thread export (Markdown/JSON) | CS-8 | ✅ Verified | ☐ |
| REQ-FN-011 | Delete thread / full history | CS-8 | ✅ Verified | ☐ |
| REQ-FN-012 | Delete document + vectors, confirmed | CS-6 | ⏳ Owner UAT | ☐ |
| REQ-FN-013 | License validation + status UI | CS-9 | ⏳ Owner UAT | ☐ |
| REQ-FN-014 | Feature gating + upgrade prompts | CS-9 | ⏳ Owner UAT | ☐ |
| REQ-FN-015 | AppManager-outage grace period | CS-9, CS-11 | ⏳ Owner UAT | ☐ |
| REQ-FN-016 | Ollama detection in wizard | CS-1 | ⏳ Owner UAT | ☐ |
| REQ-FN-017 | Dockerfile + compose self-host | CS-10 | ⏳ Owner UAT (Docker) | ☐ |
| REQ-FN-018 | Env-var configuration (12-factor) | CS-10 | ⏳ Owner UAT | ☐ |
| REQ-FN-019 | Persistent volumes | CS-10 | ⏳ Owner UAT (Docker) | ☐ |
| REQ-FN-029 | Dapper-only data access (no EF Core) | CS-10 | ✅ Verified | ☐ |
| REQ-FN-030 | TechieDeskDb console + DbUp migrations | CS-10 | ✅ Verified | ☐ |
| REQ-FN-031 | Per-provider migration parity | CS-10 | ⏳ Owner UAT (Postgres) | ☐ |
| REQ-RAG-007 | Workspace-scoped retrieval | CS-7 | ✅ Verified | ☐ |
| REQ-RAG-008 | Persist messages via library memory | CS-8 | ✅ Verified | ☐ |
| REQ-RAG-009 | Context from history, token-trimmed | CS-8 | ✅ Verified | ☐ |
| REQ-RAG-010 | Native streaming citations | CS-7 | ⏳ Owner UAT (LLM) | ☐ |
| REQ-RAG-011 | Accept supported types; reject XLSX/PPTX | CS-6 | ⏳ PARTIAL (P2 for XLSX/PPTX) | ☐ |
| REQ-RAG-012 | Content-hash dedupe, embed-once | CS-6 | ✅ Verified | ☐ |
| REQ-RAG-013 | Document pinning | CS-6 | ⏳ PARTIAL (streaming pin, TR-RAG-003) | ☐ |
| REQ-RAG-014 | Retrieval tuning: threshold/topK/rerank | CS-5 | ✅ Verified | ☐ |
| REQ-RAG-015 | Chat vs query mode | CS-5, CS-7 | ✅ Verified | ☐ |
| REQ-NFR-001 | Revoke + untrack TrBlazeUI PAT | CS-10 / §7 | ⚠️ Blocked (owner action) | ☐ |
| REQ-NFR-002 | No committed secrets; env/user-secrets | CS-2, CS-10 | ⏳ Owner UAT | ☐ |
| REQ-NFR-006 | Responsive @1280/390, no overflow | All CS (visual gate) | ✅ Verified | ☐ |
| REQ-NFR-009 | Serilog rolling logs, every head | CS-11 | ✅ Verified | ☐ |

**Coverage:** 53 Phase-1 REQs — UI-006…023, FN-001…019 + 029/030/031, RAG-007…015, NFR-001/002/006/009 — each mapped to at least one case study.

---

## 7. Known limitations & notes for testers

- **XLSX / PPTX not yet ingestible.** Spreadsheets and presentations are cleanly **Rejected** with a "coming in a later release" message (REQ-RAG-011 PARTIAL); real support is P2 (REQ-RAG-033). Test the rejection, not the ingestion.
- **Pinned docs (REQ-RAG-013) are honored by non-streaming ask, not the app-side streaming path.** The pin toggle/indicator persists, but the scoped streaming path can't yet merge pinned context (library gap TR-RAG-003). Expect pinning to influence non-streaming answers.
- **Documents added before 2026-07-30 show "—" for Size.** The ingestion pipeline now records each document's byte size (library gap TR-RAG-038, fixed), but a size that was never recorded cannot be recovered — the original file is not retained — so older documents keep the em-dash. Re-add the file to give it a size. Anything added since shows a real size.
- **Docker / Postgres need a running daemon.** CS-10 (and the Postgres migration-parity half of REQ-FN-031) can't be exercised without Docker; without it they're validated only by compose-config inspection.
- **Live auth, licensing, and streamed chat need their services.** Anything in CS-2/CS-3/CS-4/CS-9 needs a reachable **AppManager**; CS-7's streamed citations and populated chat need an **LLM provider**. Where absent, verify render/offline behaviour only and say so — don't imply a live round-trip happened.
- **NU1902 dependency advisory.** The build surfaces `NU1902` — AngleSharp 0.17.1 has a known moderate-severity advisory (GHSA-pgww-w46g-26qg) on TechieDesk + TechieDesk.Tests. Upgrade AngleSharp before any distribution (tracked under REQ-NFR-004).
- **REQ-NFR-001 — owner security action (Blocked).** Before any public/real distribution the owner must `git rm --cached nuget.config` from history and **revoke/rotate the TrBlazeUI GitHub PAT**. The working tree is clean (no committed `nuget.config`), but the history + PAT rotation are manual owner/git actions this UAT cannot close.
- **UAT credentials are disposable.** The §4 accounts are for this self-hosted test instance only — rotate or delete them before any real deployment; never reuse in production.
