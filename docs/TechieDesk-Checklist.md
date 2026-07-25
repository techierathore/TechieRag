# TechieDesk — Checklist

> Split from `docs/TechieDesk-BRD.md` (BRD-1…BRD-127) on 2026-07-17. Pre-existing statuses seeded from `docs/TechieRag-Checklist.md` (all 37 REQs terminal as of 2026-07-17) — carried as `Done (pre-existing)`; build agents must NOT rebuild these. The TechieRag checklist stays in place as the frozen historical record of the shipped v1.1/v2 scope (BRD §1 decision 3) — it is NOT a live tracker; THIS checklist is the single live tracker for app work, library gap closure (F-LIB), and all feedback (existing + future).

## Table of Contents

1. [Goal](#goal)
2. [Requirements Status](#requirements-status)
3. [UI / Pages](#ui--pages)
4. [Functional requirements](#functional-requirements)
5. [RAG / AI requirements (→ /techierag)](#rag-ai-requirements-techierag)
6. [Non-functional](#non-functional)
7. [Feedback tracker](#feedback-tracker)

## Goal

Turn TechieDesk from a verified single-user operator console into a productized, self-hostable AnythingLLM alternative (BRD §1): AppManager-backed auth/roles/licensing/billing/support, workspaces + threads with persistent history, a drag-drop document library with native streaming citations, connectors, a developer REST API + embeddable widget, agents, branding/i18n, and Docker distribution — with all reusable capability built inside the TechieRag library (F-LIB items, tracked here), data access via Dapper + TechieDeskDb/DbUp (no EF Core), and Serilog logging throughout.

## Requirements Status

<!-- ============================================================
     SINGLE SOURCE OF TRUTH for the WHOLE app (UI + functional +
     RAG + NFR) AND the open TechieRag library gaps (F-LIB → REQ-RAG-024…046).
     Build, self-smoke, and the verifier ALL write their outcomes into THIS
     table — never into a separate dated results file. One row per REQ:
       REQ-UI-*  → built by /trblazeui
       REQ-FN-*  → built by the unified build-phase
       REQ-RAG-* → built by /techierag (incl. all library-side F-LIB work in src/TechieRag*)
       REQ-NFR-* → built by the unified build-phase
     Phase tags (P0=pre-existing, P1…P5, DEF=deferred) drive build order.
     ============================================================ -->

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-UI-001 | App shell, nav, responsive @1280/390 (BRD-1, P0) | Done (pre-existing) | 100% | Verified 2026-07-17; runtime render-confirmed 2026-07-18 (verifier re-swept all 9 console routes — qdrant-admin/token-usage/llm-settings/ingestion/text-ingestion/llm-playground/tool-demo/chat/settings — render clean @1280/390, 0 horizontal overflow) | [view](#d-req-ui-001) |
| REQ-UI-002 | Home dashboard status cards (BRD-2, P0) | Done (pre-existing) | 100% | Verified 2026-07-17 (rename regression-clean); runtime render+visual-confirmed 2026-07-18 (offline sweep — all status/feature cards render + look right @1280/390) | [view](#d-req-ui-002) |
| REQ-UI-003 | Qdrant admin console + Docker lifecycle (BRD-8, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 live (create/delete + 1,043-pt browse); runtime render+visual-confirmed 2026-07-18 (offline sweep @1280/390 — connection config + Docker/Qdrant status cards render, honest Disconnected offline) | [view](#d-req-ui-003) |
| REQ-UI-004 | Provider settings + connection test (BRD-9, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 (912ms live connection test); runtime render+visual-confirmed 2026-07-18 (offline sweep @1280/390 — embedding + vector-store config render) | [view](#d-req-ui-004) |
| REQ-UI-005 | Token/cost dashboard + budgets (BRD-10, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 (non-zero live usage); runtime render+visual-confirmed 2026-07-18 (offline sweep @1280/390 — all 4 metric cards + Usage-by-Model render, zero-state on fresh session) | [view](#d-req-ui-005) |
| REQ-UI-006 | Register screen & flow (BRD-12, P1) | Implemented | 75% | 2026-07-18 /register to mockup; per-field + PasswordPolicy validation, encrypted pw, auto-login. verifier 2026-07-18: render+visual @1280/390 pass live (matches mockup). Live register round-trip = AppManager UAT | [view](#d-req-ui-006) |
| REQ-UI-007 | Login screen & flow (BRD-13, P1) | FAIL | 50% | ⚠ BLOCKER found 2026-07-20 — AppManager mode CANNOT BE LOGGED INTO. Login.razor:117 `NavigateTo(…, forceLoad: true)` tears down the Blazor circuit, and SessionTokenStore is per-circuit by design, so the new circuit has no session and RouteGuard bounces back to /login — infinite loop. Server logs 'Login succeeded' + AppManager 200, then GET / → 302 → /login. The prior '✅ LIVE login CONFIRMED' proved the NETWORK CALL succeeded, not that a user can sign in. Fix = REQ-FN-032 (session continuity across circuits: auth cookie or protected browser storage — architecture decision, trades away tokens-never-leave-server). BLOCKS all live AppManager UAT (UI-006/008…013, FN-002/003/013/014/015) | [view](#d-req-ui-007) |
| REQ-UI-008 | Logout current/all devices (BRD-16, P1) | Implemented | 75% | 2026-07-18 shell user-menu logout + all-devices → /AuthSvc/logout. Offline hides items (no session) so verifier can't render live. Live logout = AppManager UAT | [view](#d-req-ui-008) |
| REQ-UI-009 | Forgot/reset password screens (BRD-17, P1) | Implemented | 75% | 2026-07-18 /forgot-password (anti-enumeration) + /reset-password to mockup. verifier 2026-07-18: both render+visual @1280/390 pass live. Live token flow = AppManager UAT | [view](#d-req-ui-009) |
| REQ-UI-010 | Change password in profile (BRD-18, P1) | Implemented | 75% | 2026-07-18 profile change-pw (both encrypted) → /UserSvc/change-password; field-mapped errors. verifier 2026-07-18: change-password section renders on /profile @1280/390. Live change = AppManager UAT | [view](#d-req-ui-010) |
| REQ-UI-011 | Profile view/update (BRD-19, P1) | Implemented | 75% | 2026-07-18 profile view/update (name/mobile/avatar) → PUT /UserSvc/profile; email read-only. verifier 2026-07-18: /profile render+visual @1280/390 pass live (offline banner shown, all cards render). Live update = AppManager UAT | [view](#d-req-ui-011) |
| REQ-UI-012 | GDPR export/delete requests (BRD-22, P1) | Implemented | 75% | 2026-07-18 export + account-deletion w/ email-match confirm. verifier 2026-07-18: Privacy (GDPR) card renders on /profile (Request data export + Request account deletion + email confirm) @1280/390. Live requests = AppManager UAT | [view](#d-req-ui-012) |
| REQ-UI-013 | Friendly auth-error states (BRD-26, P1) | Implemented | 75% | 2026-07-18 distinct banners NO_APP_ACCESS/ACCOUNT_DISABLED/ACCOUNT_LOCKED/INVALID_CREDENTIALS. Banners require an AppManager error response to render — verifier can't trigger offline. Live trigger = AppManager UAT | [view](#d-req-ui-013) |
| REQ-UI-014 | Workspace create/rename/delete UI (BRD-27, P1) | Verified | 100% | Verified 2026-07-18 (verifier live): sidebar switcher renders, New-workspace dialog opens live w/ input; render+visual @1280/390 clean; scoping unit-tested (FN-008/RAG-028). render+visual gate: all controls render and look right | [view](#d-req-ui-014) |
| REQ-UI-015 | Workspace settings page (BRD-28, P1) | Verified | 100% | Verified 2026-07-18 (verifier live): all 4 tabs switch+render live — General (name/slug/prompt/mode), Retrieval (threshold/topK), Members, Danger (Delete workspace); render+visual @1280/390 clean. render+visual gate: all controls render and look right | [view](#d-req-ui-015) |
| REQ-UI-016 | Thread create/rename/delete UI (BRD-30, P1) | Verified | 100% | Verified 2026-07-18 (verifier live): New-thread created a persisted 'New conversation' row via IConversationStore (empty-state replaced); render+visual @1280/390 clean. render+visual gate: all controls render and look right | [view](#d-req-ui-016) |
| REQ-UI-017 | Browse & resume past threads (BRD-34, P1) | Implemented | 75% | 2026-07-18 thread list newest-first; select reloads persisted messages. verifier 2026-07-18: thread list renders newest-first live (created thread appears w/ date); resume-reload backed by RAG-008/027 unit tests but not driven live (needs an LLM answer to populate) | [view](#d-req-ui-017) |
| REQ-UI-018 | Expandable citation UI (BRD-39, P1) | Implemented | 75% | 2026-07-18 citation chips (docName·score) + expand (name/snippet/relevance) from persisted sources. verifier 2026-07-18: chips need a live RAG answer to render (no LLM up) — pending provider UAT; chat pane renders | [view](#d-req-ui-018) |
| REQ-UI-019 | Drag-drop multi-file upload (BRD-40, P1) | Implemented | 75% | 2026-07-18 FileUpload(Multiple, drag-drop+browse)+queue with per-file progress; multi-file batch smoked (xlsx+txt). verifier 2026-07-18: dropzone renders+visual @1280/390 clean; live ingest round-trip not re-driven by verifier | [view](#d-req-ui-019) |
| REQ-UI-020 | Embed/unembed with live status (BRD-42, P1) | Implemented | 75% | 2026-07-18 live status Queued→Embedding→Embedded/Reused/Rejected/Failed; row Unembed removes from this ws only. Smoked | [view](#d-req-ui-020) |
| REQ-UI-021 | Document metadata list (BRD-46, P1) | Implemented | 75% | 2026-07-18 DataTable (pin/name/type/size/chunks/uploaded/ws-using/status) in overflow wrapper; @390 scrolls in-container, 0 page overflow. verifier 2026-07-18: /documents render+visual @1280/390 clean, 0 page overflow; empty-state correct (0 docs, fresh DB) — column render needs data (not re-driven). Size shows '—' (see TR-RAG-004) | [view](#d-req-ui-021) |
| REQ-UI-022 | First-run wizard: offline defaults (BRD-52, P1) | Verified | 100% | 2026-07-18 re-verified: /setup stepper @390 fixed (base.css min-width:0 + label wrap) — scrollWidth 446→390 (overflow 0), all 5 steps visible; @1280 unchanged. Offline-defaults logic OK. Data+visual gates pass | [view](#d-req-ui-022) |
| REQ-UI-023 | Wizard: AppManager setup / offline mode (BRD-54, P1) | Implemented | 75% | 2026-07-18 step3 offline vs Connect-AppManager (url+key/secret), step4 first-Admin bootstrap; secrets not persisted. verifier 2026-07-18: /setup @1280 renders clean; ⚠ visual: shared /setup stepper overflows @390 (see REQ-UI-022). Live AppManager provisioning = owner UAT | [view](#d-req-ui-023) |
| REQ-UI-024 | Widget appearance config (BRD-71, P3) | Not Started | 0% | — | [view](#d-req-ui-024) |
| REQ-UI-025 | Admin: users + workspace assignment (BRD-72, P3) | Not Started | 0% | — | [view](#d-req-ui-025) |
| REQ-UI-026 | Admin: event log viewer (BRD-73, P3) | Not Started | 0% | — | [view](#d-req-ui-026) |
| REQ-UI-027 | Admin: chat log viewer/export (BRD-74, P3) | Not Started | 0% | — | [view](#d-req-ui-027) |
| REQ-UI-028 | Admin: instance defaults (BRD-75, P3) | Not Started | 0% | — | [view](#d-req-ui-028) |
| REQ-UI-029 | Pricing page (license types) (BRD-76, P3) | Not Started | 0% | — | [view](#d-req-ui-029) |
| REQ-UI-030 | Subscriptions view/cancel (BRD-77, P3) | Not Started | 0% | — | [view](#d-req-ui-030) |
| REQ-UI-031 | Transactions/invoices + PDF (BRD-78, P3) | Not Started | 0% | — | [view](#d-req-ui-031) |
| REQ-UI-032 | Support: create issue (BRD-80, P3) | Not Started | 0% | — | [view](#d-req-ui-032) |
| REQ-UI-033 | Support: issue list + comments (BRD-81, P3) | Not Started | 0% | — | [view](#d-req-ui-033) |
| REQ-UI-034 | Agent execution trace in chat (BRD-85, P4) | Not Started | 0% | — | [view](#d-req-ui-034) |
| REQ-UI-035 | Mic dictation (browser STT) (BRD-87, P4) | Not Started | 0% | — | [view](#d-req-ui-035) |
| REQ-UI-036 | Response read-aloud (browser TTS) (BRD-88, P4) | Not Started | 0% | — | [view](#d-req-ui-036) |
| REQ-UI-037 | White-label settings (BRD-89, P4) | Not Started | 0% | — | [view](#d-req-ui-037) |
| REQ-UI-038 | Theme toggle + accent color (BRD-90, P4) | Not Started | 0% | — | [view](#d-req-ui-038) |
| REQ-UI-039 | Localization en+2, language picker (BRD-91, P4) | Not Started | 0% | — | [view](#d-req-ui-039) |
| REQ-UI-040 | No-code agent flow builder UI (BRD-92, P5) | Not Started | 0% | — | [view](#d-req-ui-040) |
| REQ-FN-001 | RSA password encryption + key cache (BRD-14, P1) | Verified | 100% | Verified 2026-07-18 (verifier): RsaEncryptionTests + WireContractTests green in 180-pass run (127 app + 53 lib). RSA-OAEP-SHA256 + cached key + DECRYPTION_FAILED refetch/retry-once asserted | [view](#d-req-fn-001) |
| REQ-FN-002 | Silent token refresh + session (BRD-15, P1) | Implemented | 75% | 2026-07-18 pre-expiry /AuthSvc/refresh, session cleared on failure, tokens server-side (per-circuit SessionTokenStore); TokenRefreshTests. Live refresh = AppManager UAT | [view](#d-req-fn-002) |
| REQ-FN-003 | Route protection + deep-link return (BRD-20, P1) | Implemented | 75% | 2026-07-18 interactive-layer IRouteGuard in MainLayout → /login?returnUrl (endpoint [Authorize] wrong-fit: JWTs in circuit store not HttpContext); RouteGuardTests. Console routes gated when AppManager configured; offline=Admin | [view](#d-req-fn-003) |
| REQ-FN-004 | AppManagerClient (API-key headers, secrets from env) (BRD-21, P1) | Verified | 100% | Verified 2026-07-18 (verifier): WireContractTests green in 180-pass run; single client sends X-Api-Key/X-Api-Secret every call; creds env/user-secrets (appsettings ApiKey/ApiSecret empty, confirmed) | [view](#d-req-fn-004) |
| REQ-FN-005 | applicationRole → product-role mapping (BRD-23, P1) | Verified | 100% | Verified 2026-07-18 (verifier): RoleMappingTests green in 180-pass run; ProductRoleMapper → Admin/Manager/User asserted | [view](#d-req-fn-005) |
| REQ-FN-006 | Role capability matrix (BRD-24, P1) | Verified | 100% | Verified 2026-07-18 (verifier): CapabilityMatrixTests green in 180-pass run; CapabilityService frozen matrix (Admin/Manager/User) asserted | [view](#d-req-fn-006) |
| REQ-FN-007 | Server-side authorization on every operation (BRD-25, P1) | Verified | 100% | Verified 2026-07-18 (verifier): AuthGuardTests green in 180-pass run — assert User denied gated capability server-side (IAuthGuard.Require throws CapabilityDeniedException regardless of UI) | [view](#d-req-fn-007) |
| REQ-FN-008 | User↔workspace assignment (BRD-29, P1) | Verified | 100% | Verified 2026-07-18 (verifier): WorkspaceAssignmentRepository scoping tests green in 180-pass run; Members editor renders live (workspace-settings Members tab). Multi-user scoping is unit-asserted (single-user offline live) | [view](#d-req-fn-008) |
| REQ-FN-009 | Default workspace bootstrap (BRD-31, P1) | Verified | 100% | Verified 2026-07-18 (verifier live): boot log 'TechieRag persistence store initialized (threads/workspaces)'; Default workspace renders in sidebar and /workspace/default is reachable+chat-able | [view](#d-req-fn-009) |
| REQ-FN-010 | Thread export (Markdown/JSON) (BRD-35, P1) | Verified | 100% | 2026-07-18 ThreadExporter MD+JSON (role/content/sources/timestamps) → browser download; 5 unit tests; live smoke downloaded 566B MD + 845B JSON. Data+visual pass @1280/390 | [view](#d-req-fn-010) |
| REQ-FN-011 | Delete thread / full history (BRD-36, P1) | Verified | 100% | 2026-07-18 per-thread delete (confirmed) + 'Delete all my history' (DeleteAllForUserAsync) behind destructive confirm; live smoke wiped store to 0 threads/0 messages | [view](#d-req-fn-011) |
| REQ-FN-012 | Delete document + vectors, confirmed (BRD-45, P1) | Implemented | 75% | 2026-07-18 confirm dialog (shows usage count) → removes membership from every ws + ITechieRag.DeleteDocumentAsync drops shared vectors; delete→empty smoked. (Store RemoveDocumentAsync alone doesn't drop vectors — app loops+DeleteDocumentAsync; TR-RAG-004) | [view](#d-req-fn-012) |
| REQ-FN-013 | License validation + status UI (BRD-49, P1) | Implemented | 75% | 2026-07-18 ILicenseService validate+cache; LicenseStatusCard on /profile (honest 'Free (offline)'); on-nav + timed revalidation. Unit-tested status mapping. ⚠ live POST /LicenseSvc/validate = AppManager UAT | [view](#d-req-fn-013) |
| REQ-FN-014 | Feature gating + upgrade prompts (BRD-50, P1) | Implemented | 75% | 2026-07-18 IFeatureGate + <FeatureGate> component; gated CONNECTORS on Home → upgrade prompt → /pricing (3 tiers). Allow/deny-by-level unit-tested; renders @1280/390. ⚠ live FeatureSvc = AppManager UAT | [view](#d-req-fn-014) |
| REQ-FN-015 | AppManager-outage grace period (BRD-51, P1) | Implemented | 75% | 2026-07-18 last-known-good via LicenseCacheRepository; configurable grace (AppManager:LicenseGraceHours, 72h) + cached-license banner + degrade-after-expiry. Grace math unit-proven. ⚠ cache seed + live outage = AppManager UAT | [view](#d-req-fn-015) |
| REQ-FN-016 | Ollama detection in wizard (BRD-53, P1) | Implemented | 75% | 2026-07-18 OllamaProbe GET /api/tags (2s timeout); absent → graceful info Alert + embedded fallback, no crash (smoked) | [view](#d-req-fn-016) |
| REQ-FN-017 | Dockerfile + compose one-command self-host (BRD-55, P1) | Implemented | 60% | 2026-07-18 multi-stage Dockerfile + compose (app+pgvector, qdrant/ollama profiles, named volumes, migration-on-start). Docker daemon absent here → validated by inspection/compose-config; ⚠ container boot = OWNER UAT | [view](#d-req-fn-017) |
| REQ-FN-018 | Env-var configuration (12-factor) (BRD-56, P1) | Implemented | 75% | 2026-07-18 config from IConfiguration (`__` env mapping); compose passes AppDb__*/AppManager__*; full surface in .env.example | [view](#d-req-fn-018) |
| REQ-FN-019 | Persistent volumes (DB/uploads/model) (BRD-57, P1) | Implemented | 60% | 2026-07-18 named volumes: data (App DB+RAG store), uploads, models (BGE-M3 ~2.3GB, path confirmed). Upgrade-safety by inspection; ⚠ recreation test = OWNER UAT | [view](#d-req-fn-019) |
| REQ-FN-020 | Connector background jobs + progress (BRD-65, P2) | Not Started | 0% | — | [view](#d-req-fn-020) |
| REQ-FN-021 | REST API: workspaces/docs/threads/chat+SSE (BRD-66, P3) | Not Started | 0% | — | [view](#d-req-fn-021) |
| REQ-FN-022 | API key create/label/revoke + auth (BRD-67, P3) | Not Started | 0% | — | [view](#d-req-fn-022) |
| REQ-FN-023 | Swagger/OpenAPI at /api/docs (BRD-68, P3) | Not Started | 0% | — | [view](#d-req-fn-023) |
| REQ-FN-024 | API metering vs API_REQUESTS level (BRD-69, P3) | Not Started | 0% | — | [view](#d-req-fn-024) |
| REQ-FN-025 | Widget script serving, workspace-scoped (BRD-70, P3) | Not Started | 0% | — | [view](#d-req-fn-025) |
| REQ-FN-026 | Promo-code validation (BRD-79, P3) | Not Started | 0% | — | [view](#d-req-fn-026) |
| REQ-FN-027 | Close own support issue (BRD-82, P3) | Not Started | 0% | — | [view](#d-req-fn-027) |
| REQ-FN-028 | Cron-scheduled agent/ingestion jobs (BRD-93, P5) | Not Started | 0% | — | [view](#d-req-fn-028) |
| REQ-FN-029 | Dapper-only data access (no EF Core) (BRD-102, P1) | Verified | 100% | Verified 2026-07-18 (verifier): acceptance grep re-run — 0 EntityFrameworkCore refs in apps/; all repos Dapper+parameterized over IAppDbConnectionFactory | [view](#d-req-fn-029) |
| REQ-FN-030 | TechieDeskDb console + DbUp migrations (BRD-103, P1) | Verified | 100% | Verified 2026-07-18 (verifier live): app boot applied DbUp migrations idempotently — Serilog logged 'Beginning database upgrade', 'No new scripts need to be executed', 'Database migrations applied successfully'; migrator log techiedeskdb-*.log written | [view](#d-req-fn-030) |
| REQ-FN-031 | Per-provider migration scripts, equivalent schemas (BRD-104, P1) | Implemented | 75% | 2026-07-18 SQLite+Postgres 0001 schemas equivalent (parity verified line-by-line). verifier 2026-07-18: SQLite migrations applied live at boot; Postgres boot needs Docker (absent here) = owner UAT | [view](#d-req-fn-031) |
| REQ-FN-032 | ⚠ BLOCKER: session continuity across circuits (login loop) | Not Started | 0% | ⚠ NEW 2026-07-20, discovered by NFR-010 resilience work. AppManager mode is UNUSABLE — see REQ-UI-007. Login succeeds (AppManager 200, 'Login succeeded' logged) but `NavigateTo(forceLoad: true)` destroys the circuit holding the per-circuit SessionTokenStore, so the next circuit has no session and RouteGuard redirects to /login forever. Fix requires session continuity across circuits — an auth cookie or protected browser storage — which TRADES AWAY the 'tokens never leave the server' property REQ-FN-002/REQ-NFR-004 were designed around. **Architecture decision for the owner; deliberately not made unilaterally.** Blocks the entire live AppManager UAT track | [view](#d-req-fn-032) |
| REQ-RAG-001 | Direct-LLM streaming chat (BRD-3, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 live; runtime render+visual-confirmed 2026-07-18 (offline sweep — /chat renders config + empty-state + composer @1280/390; live stream needs provider = UAT) | [view](#d-req-rag-001) |
| REQ-RAG-002 | Auto-RAG chat + citations (BRD-4, P0) | Done (pre-existing) | 100% | Verified 2026-07-02; citations use app-side workaround while streaming — native = REQ-RAG-010/024. runtime render+visual-confirmed 2026-07-18 (offline sweep) | [view](#d-req-rag-002) |
| REQ-RAG-003 | Folder/pattern ingestion (BRD-5, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 (live ingest write cycle); runtime render+visual-confirmed 2026-07-18 (offline sweep — /ingestion renders @1280/390) | [view](#d-req-rag-003) |
| REQ-RAG-004 | Paste-text ingestion (BRD-6, P0) | Done (pre-existing) | 100% | Verified 2026-07-02; runtime render+visual-confirmed 2026-07-18 (offline sweep — /text-ingestion renders @1280/390) | [view](#d-req-rag-004) |
| REQ-RAG-005 | LLM playground (completion/structured/chat) (BRD-7, P0) | Done (pre-existing) | 100% | Verified 2026-07-02; runtime render+visual-confirmed 2026-07-18 (offline sweep — 3 tabs + prompt/temp/max-tokens/streaming controls render @1280/390) | [view](#d-req-rag-005) |
| REQ-RAG-006 | Tool demo + execution trace (BRD-11, P0) | Done (pre-existing) | 100% | Verified 2026-07-02 live (real tool steps); runtime render+visual-confirmed 2026-07-18 (offline sweep — /tool-demo renders @1280/390) | [view](#d-req-rag-006) |
| REQ-RAG-007 | Workspace-scoped retrieval (BRD-32, P1) | Verified | 100% | Verified 2026-07-18 (verifier): workspace-store scoping/isolation tests green in 180-pass run; WorkspaceManager.SearchAsync scoped to workspace doc set (topK+threshold) | [view](#d-req-rag-007) |
| REQ-RAG-008 | Persist messages via library memory (BRD-33, P1) | Verified | 100% | Verified 2026-07-18 (verifier): conversation store+memory tests green in 180-pass run; also live — New-thread persisted a conversation row via IConversationStore | [view](#d-req-rag-008) |
| REQ-RAG-009 | Context from history, token-trimmed (BRD-37, P1) | Verified | 100% | Verified 2026-07-18 (verifier): token-trimmed history tests green in 180-pass run; GetTrimmedHistoryAsync(4000, EstimateTokenCount) + scoped prompt | [view](#d-req-rag-009) |
| REQ-RAG-010 | Native streaming citations in app (BRD-38, P1) | Implemented | 80% | 2026-07-20 scoped streaming moved INTO the library (no app-side composition); chips render off the native Sources event. Fixed 2 real bugs never caught before (this path was never live-verified): chips showed raw GUIDs because Chunk.Metadata arrives as JsonElement so `is string` fell through to DocumentId; and chips never painted when the provider failed fast (StateHasChanged not flushed before the LLM call — added render yield). Live-verified at 1280+390. ⚠ live token stream still provider UAT | [view](#d-req-rag-010) |
| REQ-RAG-011 | Accept all supported types incl. XLSX/PPTX (BRD-41, P1) | Implemented | 75% | 2026-07-20 PARTIAL cleared — removed the XLSX/PPTX 'coming in a later release' rejection now that REQ-RAG-033 processors exist; accept filter + hint copy updated. Accept/reject matrix extracted to UploadTypePolicy (unit-testable, 27 new app tests); legacy binary .xls/.ppt/.doc still get a clear per-file rejection. Smoked on 5111: xlsx/pptx/csv all reach Embedded with non-zero chunks; .png rejected by name and never lands | [view](#d-req-rag-011) |
| REQ-RAG-012 | Content-hash dedupe, embed-once (BRD-43, P1) | Verified | 100% | Verified 2026-07-18 (verifier): content-hash dedupe tests green in 180-pass run; SHA-256 pre-check via FindDocumentIdByHashAsync, embed-once in library | [view](#d-req-rag-012) |
| REQ-RAG-013 | Document pinning (BRD-44, P1) | Implemented | 75% | 2026-07-20 PARTIAL cleared — pinning now honored while STREAMING: app calls the new WorkspaceManager.AskStreamWithSourcesAsync (pinned merged via shared ComposeContextAsync seam), replacing the app-composed path that silently dropped pins. Unit-proven: a doc scoring 0.05 against a 0.5 threshold enters context AND prompt. Live-observed on 5112 — pinned doc streamed as citation chip 'trrag003-smoke-tmp.txt · 0.62' before any token (offline BGE-M3). Closes TR-RAG-003. ⚠ completed streamed answer = provider UAT | [view](#d-req-rag-013) |
| REQ-RAG-014 | Retrieval tuning: threshold/topK/rerank (BRD-47, P1) | Needs re-verify | 65% | ⚠ DEMOTED 2026-07-20 from Verified — the 2026-07-18 evidence proved per-ws **persistence** of threshold/topK/rerank and was recorded as proving behavior. Threshold + topK are genuinely honored; `Workspace.RerankEnabled` is DEAD CONFIG — it round-trips but nothing ever reads it, because ITechieRag.SearchAsync exposes no per-call rerank switch (rerank stays global config only). BRD-47 acceptance names a rerank toggle, so 2 of 3 met. Needs REQ-RAG-047 (F-LIB) then re-verify | [view](#d-req-rag-014) |
| REQ-RAG-015 | Chat vs query mode (BRD-48, P1) | Verified | 100% | Verified 2026-07-18 (verifier): chat-vs-query mode tests green in 180-pass run; Query returns deterministic 'not in documents' when nothing passes threshold; also live — mode selector renders (workspace-settings General + workspace Chat-mode toggle) | [view](#d-req-rag-015) |
| REQ-RAG-016 | URL scrape ingestion (BRD-60, P2) | Not Started | 0% | Needs REQ-RAG-031 | [view](#d-req-rag-016) |
| REQ-RAG-017 | Site crawler (depth/max-links) (BRD-61, P2) | Not Started | 0% | Needs REQ-RAG-031 | [view](#d-req-rag-017) |
| REQ-RAG-018 | YouTube transcript ingestion (BRD-62, P2) | Not Started | 0% | Needs REQ-RAG-031 | [view](#d-req-rag-018) |
| REQ-RAG-019 | GitHub/GitLab connector (BRD-63, P2) | Not Started | 0% | Needs REQ-RAG-032 | [view](#d-req-rag-019) |
| REQ-RAG-020 | Confluence connector (BRD-64, P2) | Not Started | 0% | Needs REQ-RAG-032 | [view](#d-req-rag-020) |
| REQ-RAG-021 | @agent invocation in workspace chat (BRD-83, P4) | Not Started | 0% | — | [view](#d-req-rag-021) |
| REQ-RAG-022 | Per-workspace skill toggles (library ITools) (BRD-84, P4) | Not Started | 0% | — | [view](#d-req-rag-022) |
| REQ-RAG-023 | MCP server registration for agent (BRD-86, P4) | Not Started | 0% | Needs REQ-RAG-038 | [view](#d-req-rag-023) |
| REQ-RAG-024 | LIB: streaming RAG returns sources + template fix (BRD-105, P1) | Verified | 100% | Verified 2026-07-18 (verifier): streaming-sources tests green in 53-test lib suite; RagStreamEvent(Sources→Token→Completed) via PromptTemplateEngine. Closes TR-RAG-001. (Live LLM stream = provider UAT, tracked at REQ-RAG-010) | [view](#d-req-rag-024) |
| REQ-RAG-025 | LIB: IReranker + local ONNX + API reranker (BRD-106, P1) | Verified | 100% | Verified 2026-07-18 (verifier): reranker tests green in 53-test lib suite; IReranker + Cohere + Jina in-core (WithReranker). LocalONNX in TechieRag.Embedded — RerankSource.LocalOnnx + factory hook present | [view](#d-req-rag-025) |
| REQ-RAG-026 | LIB: pluggable IChunker strategies (BRD-107, P1) | Verified | 100% | Verified 2026-07-18 (verifier): chunker tests green in 53-test lib suite; IChunker Recursive/Token/Sentence/Markdown via WithChunking | [view](#d-req-rag-026) |
| REQ-RAG-027 | LIB: persistent IConversationMemory + threads (BRD-108, P1) | Verified | 100% | Verified 2026-07-18 (verifier): conversation store+memory+threads tests green in 53-test lib suite; SQLite also live-confirmed (thread persisted). Postgres path code-complete, live boot = UAT | [view](#d-req-rag-027) |
| REQ-RAG-028 | LIB: workspace/collection primitives (BRD-109, P1) | Verified | 100% | Verified 2026-07-18 (verifier): workspace-store tests green in 53-test lib suite; IWorkspaceStore+WorkspaceManager isolation, per-ws threshold/topK/rerank, dedup, pinning, chat-vs-query | [view](#d-req-rag-028) |
| REQ-RAG-029 | LIB: cost table config + streamed usage (BRD-110, P1) | Verified | 100% | Verified 2026-07-18 (verifier): cost-math tests green in 53-test lib suite; pricing externalized to config + WithModelPricing; streamed usage non-zero (include_usage + estimation fallback). Closes TR-RAG-002 | [view](#d-req-rag-029) |
| REQ-RAG-030 | LIB: unit-test coverage (continuous) (BRD-111, P1→) | Verified | 100% | Verified 2026-07-18 (verifier): 53/53 lib tests pass (chunkers, reranker, conversation store+memory, workspace store, cost math, streaming sources) — re-run green by verifier. Grows every phase | [view](#d-req-rag-030) |
| REQ-RAG-031 | LIB: web ingestion (URL/crawler/YouTube) (BRD-112, P2) | Not Started | 0% | — | [view](#d-req-rag-031) |
| REQ-RAG-032 | LIB: IDataConnector + GitHub/Confluence (BRD-113, P2) | Not Started | 0% | — | [view](#d-req-rag-032) |
| REQ-RAG-033 | LIB: XLSX/PPTX/CSV processors (BRD-114, P2) | Implemented | 75% | 2026-07-20 XlsxProcessor + PptxProcessor (reuse existing DocumentFormat.OpenXml 3.4.1 — no new package) + dependency-free quote-aware CsvProcessor (.csv/.tsv); registered ahead of GenericTextProcessor in TechieRagBuilder. 6 lib tests build real OpenXml fixtures in-test (no binary assets committed). Smoked: all three ingest with non-zero chunks and render in the library table with correct type badges | [view](#d-req-rag-033) |
| REQ-RAG-034 | LIB: provider expansion + model-name routing (BRD-115, P2) | Not Started | 0% | — | [view](#d-req-rag-034) |
| REQ-RAG-035 | LIB: more embedding providers (BRD-116, P2) | Not Started | 0% | — | [view](#d-req-rag-035) |
| REQ-RAG-036 | LIB: OpenTelemetry exporters (BRD-117, P3) | Not Started | 0% | — | [view](#d-req-rag-036) |
| REQ-RAG-037 | LIB: net8.0 TFM (BRD-118, P3) | Not Started | 0% | — | [view](#d-req-rag-037) |
| REQ-RAG-038 | LIB: MCP client in agent loop (BRD-119, P4) | Not Started | 0% | — | [view](#d-req-rag-038) |
| REQ-RAG-039 | LIB: multimodal input (vision first) (BRD-120, P4) | Not Started | 0% | — | [view](#d-req-rag-039) |
| REQ-RAG-040 | LIB: audio-transcription processor (BRD-121, P4) | Not Started | 0% | — | [view](#d-req-rag-040) |
| REQ-RAG-041 | LIB: ITextToSpeech/ISpeechToText (BRD-122, P4) | Not Started | 0% | — | [view](#d-req-rag-041) |
| REQ-RAG-042 | LIB: agent orchestration framework (BRD-123, P5) | Not Started | 0% | — | [view](#d-req-rag-042) |
| REQ-RAG-043 | LIB: prompt-caching passthrough (BRD-124, P5) | Not Started | 0% | — | [view](#d-req-rag-043) |
| REQ-RAG-044 | LIB: more vector stores (BRD-125, DEF) | Not Started | 0% | Deferred — pull forward on demand | [view](#d-req-rag-044) |
| REQ-RAG-045 | LIB: Microsoft.Extensions.AI interop (BRD-126, DEF) | Not Started | 0% | Deferred | [view](#d-req-rag-045) |
| REQ-RAG-046 | LIB: image-gen/realtime/batch/etc. endpoints (BRD-127, DEF) | Not Started | 0% | Deferred — re-scope per demand | [view](#d-req-rag-046) |
| REQ-RAG-047 | LIB: per-workspace rerank switch on SearchAsync (F-LIB) | Not Started | 0% | ⚠ NEW 2026-07-20 — root cause of the REQ-RAG-014 demotion. `Workspace.RerankEnabled` persists and round-trips but is DEAD CONFIG: ITechieRag.SearchAsync has no per-call rerank switch, so rerank stays global-config-only and the per-workspace toggle drives nothing. Needed to restore REQ-RAG-014 to Verified | [view](#d-req-rag-047) |
| REQ-RAG-048 | LIB: MaxContextChunks silent truncation signal (F-LIB) | Not Started | 0% | ⚠ NEW 2026-07-20 — PromptConfig.MaxContextChunks (default 5) silently truncates the merged context. Pinned chunks survive only because ComposeContextAsync orders them first; >5 pinned documents would push out EVERY retrieved result with no signal to caller or user. Needs an overflow indication (event, flag, or log) | [view](#d-req-rag-048) |
| REQ-NFR-001 | Revoke + untrack TrBlazeUI PAT (BRD-58, P1) | Blocked | 25% | ⚠ SECURITY OWNER-RUN — 2026-07-18 working tree clean (no nuget.config; gitignored). Residual = `git rm --cached nuget.config` from history + revoke/rotate the PAT on GitHub (manual git/owner actions) | [view](#d-req-nfr-001) |
| REQ-NFR-002 | No committed secrets; env/user-secrets only (BRD-59, P1) | Implemented | 75% | 2026-07-18 tracked appsettings.json AppManager ApiKey/ApiSecret/BaseUrl/ApplicationId all empty; real AppManager creds live in gitignored apps/TechieDesk/appsettings.Development.json (auto-layered in Development; explicit .gitignore entry + broad *.json rule). Live-boot confirmed AppManager mode from the override — no committed secret, no env vars, no code change. Pending verifier | [view](#d-req-nfr-002) |
| REQ-NFR-003 | Performance targets (BRD-94) | Implemented | 85% | 2026-07-20 measured live (Apple M4 Max dev box, loopback — favourable-conditions upper bound). UI interactions worst **4.2ms** vs <200ms (in-page MutationObserver over 5 runs; Playwright expect() poll backoff yields a false ~200ms floor — do not use it here). 10-page PDF / 57 chunks embedded **2.88–3.03s** warm, 4.3–5.0s cold vs <60s, via bundled local BGE-M3 ONNX. **25/25** concurrent Blazor circuits in <850ms, 0 drops, p95 99–135ms; probed to **100/100** still 0 drops (RSS 1.13GB, ~2.5MB/circuit). ⚠ <2s streaming overhead PARTIAL — no LLM on host, generation latency NOT measurable and not synthesised; isolatable retrieval+context slice = 55–116ms. Harnesses: tests/verify/req-nfr-003-{perf,concurrency}.spec.ts | [view](#d-req-nfr-003) |
| REQ-NFR-004 | Security: TLS, server-side tokens/authz, OWASP, hashed keys (BRD-95) | PARTIAL | 70% | 2026-07-20 NU1902 CLEARED — AngleSharp 0.17.1 was transitive (TrBlazeUI 1.0.7 → HtmlSanitizer 9.0.892, exact pin [0.17.1]); pinning HtmlSanitizer 9.1.949-beta resolves AngleSharp 1.5.1; build 0×NU1902, `dotnet list --vulnerable` clean. Fixed 2 REAL TLS holes not previously logged: AppManager:BaseUrl accepted ANY scheme (http would have sent X-Api-Key/X-Api-Secret + JWTs in cleartext) — now https-enforced at DI and boot, loopback http Dev-only; both Qdrant clients hardcoded `https:false` forcing cleartext even for https endpoints — now scheme-derived + plaintext-credential warning. Audited CLEAN with evidence: tokens server-side only (0 localStorage/sessionStorage/cookie writes), all SQL parameterized incl. the dynamic EventLog builder, 0 MarkupString/Html.Raw/innerHTML, dev cert flag already Development-gated + AppManager-client-scoped. +20 tests. ⚠ 2 OPEN OWNER DECISIONS: (1) techierag-config.json persists provider apiKeys in cleartext (outbound creds — hashing N/A, needs encryption-at-rest); (2) the pin ships TWO pre-release packages (HtmlSanitizer 9.1.949-beta + AngleSharp.Css 1.0.0-beta.216) — no stable HtmlSanitizer exists on AngleSharp ≥1.5.0; real fix is upstream (TR-009) | [view](#d-req-nfr-004) |
| REQ-NFR-005 | Accessibility WCAG 2.1 AA basics (BRD-96) | PARTIAL | 70% | 2026-07-20 axe (@axe-core/playwright, WCAG 2.0/2.1 A+AA) across all 20 shipped routes: **403 violation nodes → 60 (85% reduction)**; /login /register /forgot-password /reset-password fully clean. Fixed app-side: AA contrast 4.34:1→~4.9:1, 40 nested-interactive, 97 decorative icons aria-hidden, 19 missing h1 (which also repaired FocusOnNavigate — a no-op on 19/20 routes), AuthLayout landmarks, named inputs + icon-only buttons, skip link. Highest-value fix was NOT axe-detectable: keyboard focus was COMPLETELY INVISIBLE on the entire sidebar nav, sidebar toggle and user menu (outline:none + transparent ring, WCAG 2.4.7 AA) — found by tabbing the shell; now a 2px outline on every tab stop, test-asserted. ⚠ 60 nodes OPEN, ALL inside TrBlazeUI 1.0.7 and NOT app-fixable (TR-008): 6 critical unnameable Select triggers (role=combobox is nameFrom:author and SelectTrigger silently ignores AriaLabel), 8 critical Tabs aria-controls, FileUpload input label, Slider/Progress names. Do NOT mark Verified until a TrBlazeUI release lands. tests/verify/req-nfr-005-007-a11y-browsers.spec.ts 13/13 | [view](#d-req-nfr-005) |
| REQ-NFR-006 | Responsive @1280/390, no horizontal overflow (BRD-97) | Verified | 100% | 2026-07-18 all shipped screens pass @390 (scrollWidth==viewport): auth/workspace/doc/wizard/pricing/console. /setup overflow fixed + re-verified. Continuous — re-checked per new screen (P2+) | [view](#d-req-nfr-006) |
| REQ-NFR-007 | Evergreen browser support (BRD-98) | Implemented | 90% | 2026-07-20 cross-engine render+visual smoke on chromium, firefox and webkit (Safari's engine) at 1280 and 390: **120/120 route-cells pass** (3 engines × 2 viewports × 20 routes) with measured scrollWidth vs viewport, error/blank-page detection, pageerror capture, 120 screenshots. Max horizontal overflow across all cells = **0px**; zero JS errors; Blazor interactive in all 120. No engine-specific breakage, so no cross-engine fixes needed. Edge is Chromium and is covered by the chromium engine — a separate Edge binary was NOT driven and that coverage is NOT claimed. Doubles as the valid re-smoke of the HtmlSanitizer/AngleSharp 0.17.1→1.5.1 swap under a verified-healthy Development boot: no rendering regression on any route | [view](#d-req-nfr-007) |
| REQ-NFR-008 | Privacy/data locality, no telemetry (BRD-99) | Implemented | 85% | 2026-07-20 enumerated EVERY outbound call site across apps/ and src/ (18 HttpClient consumers + all SDKs) and classified each — **NO VIOLATIONS**. All egress is LLM-provider, embedding/rerank-provider, AppManager, or loopback/local infra; committed defaults ship AppManager:BaseUrl empty = zero egress. Telemetry sweep clean: zero AppInsights/OpenTelemetry/OTLP/Sentry/Datadog/NewRelic refs, no exporters, Serilog is Console+File only, the `EnableTelemetry` config flag is inert (nothing reads it). Added DOTNET_CLI_TELEMETRY_OPTOUT to both Dockerfile stages + compose. Only non-allowlisted egress is the first-run huggingface.co model-weights download (GET only, no instance data leaves) — now redirectable to an internal mirror via TECHIERAG_MODEL_BASE_URL. Durable guard: OutboundEgressTests asserts an HttpClient-consumer allowlist + no telemetry assembly linked + offline zero-egress default | [view](#d-req-nfr-008) |
| REQ-NFR-009 | Serilog rolling-file logging, every head (BRD-100) | Verified | 100% | Verified 2026-07-18 (verifier live): rolling-file logs confirmed on disk for BOTH heads — apps/TechieDesk/logs/techiedesk-20260718.log (written during this run) + apps/TechieDeskDb/logs/techiedeskdb-20260718.log; startup + migration outcomes logged. REST/widget heads future (P3) | [view](#d-req-nfr-009) |
| REQ-NFR-010 | Resilience: AM-outage grace, provider fallback, restart-safe (BRD-101) | Implemented | 80% | 2026-07-20 restart-safety PROVEN end-to-end twice: workspace, system prompt, Top-K=7, pinned 57-chunk PDF, renamed thread and persisted message ALL survived a real process kill/restart (req-nfr-010-restart.spec.ts phase A/B). Provider failures surface specifically and never drop the circuit: LLM 'Error: Connection refused (192.168.1.13:1234)', embedding 'Failed — Connection refused'. **GAP FOUND AND FIXED:** a vector-store outage rendered as 'This workspace does not exist or you are not assigned to it' (blanket `catch` setting notFound), sending the operator to entirely the wrong problem — DocumentLibrary.razor + WorkspaceChat.razor now distinguish backend unavailability and name the real error; regression-guarded, genuine not-found path re-verified. ⚠ AppManager-outage grace NOT live-verifiable — blocked by the REQ-UI-007 login loop; covered instead by 3 new integration tests driving LicenseService against the REAL SQLite LicenseCacheRepository across a simulated restart (Cached <72h, GraceExpired past). Live harness written and ready: req-nfr-010-appmanager-outage.spec.ts | [view](#d-req-nfr-010) |

**Status values:** `Not Started` · `In Progress` · `Implemented` (code done, not yet verified) · `Verified` (self-smoke or verifier PASS — acceptance AND data-render AND visual gates all pass) · `Done (pre-existing)` (migrated from an earlier dev plan as already complete — build agents must NOT rebuild; terminal like `Verified`) · `Needs re-verify` (a defect or change was logged — must be re-run before it can return to `Verified`) · `PARTIAL` (some acceptance unmet — say what in Remarks) · `FAIL` (verifier ran and failed — bug in Remarks) · `Blocked` (external/library gap — link the TR-/TR-RAG- entry in Remarks) · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug or library reference. This is the home for bugs and change notes — do not spawn a separate file. Visual-gate failures are prefixed `⚠ visual:`; security findings `⚠ SECURITY`.

## UI / Pages

<!-- No mockups exist yet (brownfield). Pre-existing screens are the visual baseline; NEW screens
     (auth, workspaces, doc library, admin, billing, support, wizard) should get mockups via
     *mockups TechieDesk --update before /trblazeui builds them. -->

### Pages: Existing console (10 routes, pre-existing)

<a id="d-req-ui-001"></a>
- **REQ-UI-001** — TrBlazeUI sidebar shell + all-route navigation, responsive @1280/390 (BRD-1).
  - *Acceptance:* all routes render, no horizontal overflow at 390px (visual gate). Already Verified.
<a id="d-req-ui-002"></a>
- **REQ-UI-002** — Home dashboard with instance status cards (BRD-2). Already Verified.
<a id="d-req-ui-003"></a>
- **REQ-UI-003** — Qdrant admin: collection CRUD, point browse/detail, Docker container lifecycle (BRD-8). Already Verified live.
<a id="d-req-ui-004"></a>
- **REQ-UI-004** — Runtime provider settings + connection test (BRD-9). Already Verified. Post-P1: role-gate to Admin.
<a id="d-req-ui-005"></a>
- **REQ-UI-005** — Token/cost dashboard, budget alerts, block-on-exceed (BRD-10). Already Verified. Post-P1: instance-wide for Admin, per-user view.

### Page: Auth screens (`/login`, `/register`, `/forgot-password`, `/reset-password`, `/profile`) — Phase 1

<a id="d-req-ui-006"></a>
- **REQ-UI-006** — Register: email, first/last name, optional mobile, password per AppManager complexity; calls `POST /AuthSvc/register`; auto-login on success (BRD-12).
  - *Acceptance:* new user lands authenticated with default `User` role; validation errors per field; no plaintext password on the wire (see REQ-FN-001).
<a id="d-req-ui-007"></a>
- **REQ-UI-007** — Login: email + password → `POST /AuthSvc/login`; captures role + activeLicense (BRD-13).
  - *Acceptance:* valid creds land on last-requested route; INVALID_CREDENTIALS shows friendly error.
<a id="d-req-ui-008"></a>
- **REQ-UI-008** — Logout menu action with "all devices" option → `POST /AuthSvc/logout` (BRD-16).
  - *Acceptance:* session cleared; all-devices revokes every refresh token.
<a id="d-req-ui-009"></a>
- **REQ-UI-009** — Forgot/reset password pages using AppManager token flow (BRD-17).
  - *Acceptance:* forgot always shows success (no enumeration); reset with valid token logs in-able with new password.
<a id="d-req-ui-010"></a>
- **REQ-UI-010** — Change password in profile (both fields encrypted) via `POST /UserSvc/change-password` (BRD-18).
  - *Acceptance:* INVALID_CURRENT_PASSWORD / INVALID_PASSWORD map to field errors.
<a id="d-req-ui-011"></a>
- **REQ-UI-011** — Profile view/update: name, mobile, avatar URL (BRD-19).
<a id="d-req-ui-012"></a>
- **REQ-UI-012** — GDPR data-export + account-deletion request actions with confirmation (email match) (BRD-22).
<a id="d-req-ui-013"></a>
- **REQ-UI-013** — Distinct friendly pages/banners for NO_APP_ACCESS, ACCOUNT_DISABLED, ACCOUNT_LOCKED (BRD-26).

### Page: Workspaces (`/workspace/{slug}`, `/workspace/{slug}/settings`) — Phase 1

<a id="d-req-ui-014"></a>
- **REQ-UI-014** — Workspace switcher in sidebar + create/rename/delete (role-gated Manager/Admin) (BRD-27).
  - *Acceptance:* User role sees only assigned workspaces; delete asks confirmation.
<a id="d-req-ui-015"></a>
- **REQ-UI-015** — Workspace settings page: name/slug, system prompt, LLM override, chat/query mode, retrieval tuning, members (BRD-28).
<a id="d-req-ui-016"></a>
- **REQ-UI-016** — Threads panel: create/rename/delete threads within workspace (BRD-30).
<a id="d-req-ui-017"></a>
- **REQ-UI-017** — Thread history list (most recent first), resume with full context after re-login (BRD-34).
<a id="d-req-ui-018"></a>
- **REQ-UI-018** — Citation chips on streamed answers; expand → document name, snippet, score (BRD-39).
  - *Acceptance:* citations appear during streaming (needs REQ-RAG-010/024).

### Page: Document library (`/workspace/{slug}/documents`, `/admin/documents`) — Phase 1

<a id="d-req-ui-019"></a>
- **REQ-UI-019** — Drag-drop + file-picker multi-upload into workspace library (BRD-40).
  - *Acceptance:* multiple files queue; per-file progress; TrBlazeUI gaps → custom component fallback allowed.
<a id="d-req-ui-020"></a>
- **REQ-UI-020** — Per-workspace embed/unembed toggle with live status pending/embedding/embedded/failed (BRD-42).
<a id="d-req-ui-021"></a>
- **REQ-UI-021** — Library table: name, type, size, chunk count, upload date, workspaces using (BRD-46).
  - *Acceptance:* table scrolls inside its container @390 (TR-004 pattern — inline style).

### Page: First-run wizard (`/setup`) — Phase 1

<a id="d-req-ui-022"></a>
- **REQ-UI-022** — Wizard applies offline defaults (Embedded BGE-M3 + SqliteVec), creates default workspace (BRD-52).
  - *Acceptance:* fresh instance → chat-able with zero external services.
<a id="d-req-ui-023"></a>
- **REQ-UI-023** — Wizard step: AppManager connection (base URL, key/secret) + first-Admin bootstrap, or explicit offline single-user mode (BRD-54).

### Pages: Admin console (`/admin/*`) — Phase 3

<a id="d-req-ui-025"></a>
- **REQ-UI-025** — Users list (identity from AppManager) + workspace assignment editor (BRD-72).
<a id="d-req-ui-026"></a>
- **REQ-UI-026** — Filterable event log viewer (auth, ingestion, admin actions) (BRD-73).
<a id="d-req-ui-027"></a>
- **REQ-UI-027** — Cross-workspace chat log inspection/export (BRD-74).
<a id="d-req-ui-028"></a>
- **REQ-UI-028** — Instance defaults: default providers, upload limits (BRD-75).

### Pages: Billing & support (`/pricing`, `/billing`, `/support`) — Phase 3

<a id="d-req-ui-029"></a>
- **REQ-UI-029** — Pricing page from `GET /LicenseSvc/types`, multi-currency (BRD-76).
<a id="d-req-ui-030"></a>
- **REQ-UI-030** — Subscriptions list + cancel (immediate/period-end) (BRD-77).
<a id="d-req-ui-031"></a>
- **REQ-UI-031** — Transactions + invoices with PDF download (BRD-78).
<a id="d-req-ui-032"></a>
- **REQ-UI-032** — Create support issue: title/description/type/priority (BRD-80).
<a id="d-req-ui-033"></a>
- **REQ-UI-033** — Issue list with status filter + comment thread (BRD-81).

### Widget & chat extras — Phases 3–4

<a id="d-req-ui-024"></a>
- **REQ-UI-024** — Widget appearance config: color, logo, welcome, position (BRD-71, P3).
<a id="d-req-ui-034"></a>
- **REQ-UI-034** — Agent execution trace rendering in product chat (BRD-85, P4). Reuses proven F-TOOLS trace.
<a id="d-req-ui-035"></a>
- **REQ-UI-035** — Mic dictation via browser speech recognition (BRD-87, P4).
<a id="d-req-ui-036"></a>
- **REQ-UI-036** — Read-aloud via browser speech synthesis (BRD-88, P4).

### Appearance, i18n, flows — Phases 4–5

<a id="d-req-ui-037"></a>
- **REQ-UI-037** — White-label settings: logo, display name, welcome, footer links; gated `WHITE_LABEL` (BRD-89, P4).
<a id="d-req-ui-038"></a>
- **REQ-UI-038** — Light/dark theme toggle + Admin accent color (BRD-90, P4).
<a id="d-req-ui-039"></a>
- **REQ-UI-039** — .resx localization of all strings; en + 2 locales; language picker (BRD-91, P4).
<a id="d-req-ui-040"></a>
- **REQ-UI-040** — Visual no-code flow builder over the library orchestration engine (BRD-92, P5; needs REQ-RAG-042).

## Functional requirements

<a id="d-req-fn-001"></a>
- **REQ-FN-001** — RSA-OAEP-SHA256 encryption of every password field; public key fetched from `GET /AuthSvc/public-key` and cached; DECRYPTION_FAILED → refetch key, retry once; no plaintext password ever transmitted/stored (BRD-14).
<a id="d-req-fn-002"></a>
- **REQ-FN-002** — Silent access-token refresh via `POST /AuthSvc/refresh` before expiry; refresh failure → login redirect preserving route; tokens held server-side (BRD-15).
<a id="d-req-fn-003"></a>
- **REQ-FN-003** — All product routes require auth; unauthenticated → `/login` with deep-link return (BRD-20).
<a id="d-req-fn-004"></a>
- **REQ-FN-004** — Single `AppManagerClient` sends `X-Api-Key`/`X-Api-Secret` on every call; credentials from env/user-secrets (BRD-21). Isolates the v1.4 wire contract (a-prefixed URL params).
<a id="d-req-fn-005"></a>
- **REQ-FN-005** — Map app-scoped `applicationRole` → Admin/Manager/User per BRD §5 (BRD-23).
<a id="d-req-fn-006"></a>
- **REQ-FN-006** — Enforce the role capability matrix (Admin: instance + all; Manager: workspace/doc/connector mgmt; User: assigned workspaces + own data) (BRD-24).
<a id="d-req-fn-007"></a>
- **REQ-FN-007** — Every authorization check server-side; UI hiding never sufficient (BRD-25).
  - *Acceptance:* forging a request to a role-gated operation as User returns denied.
<a id="d-req-fn-008"></a>
- **REQ-FN-008** — User↔workspace assignment stored in App DB; User sees only assigned (BRD-29).
<a id="d-req-fn-009"></a>
- **REQ-FN-009** — Default workspace auto-created on first run (BRD-31).
<a id="d-req-fn-010"></a>
- **REQ-FN-010** — Export thread as Markdown or JSON (BRD-35).
<a id="d-req-fn-011"></a>
- **REQ-FN-011** — Permanently delete a thread or entire own history (BRD-36).
<a id="d-req-fn-012"></a>
- **REQ-FN-012** — Delete document removes vectors from all using workspaces, with confirmation (BRD-45).
<a id="d-req-fn-013"></a>
- **REQ-FN-013** — License validated at login + periodically via `POST /LicenseSvc/validate`; name/status/expiry shown (BRD-49).
<a id="d-req-fn-014"></a>
- **REQ-FN-014** — Feature gating via FeatureSvc (binary + level); gated features show upgrade prompt (BRD-50).
<a id="d-req-fn-015"></a>
- **REQ-FN-015** — AppManager unreachable → last-known-good license honored for configurable grace period; banner shown (BRD-51).
<a id="d-req-fn-016"></a>
- **REQ-FN-016** — Wizard detects local Ollama and offers discovered models (BRD-53).
<a id="d-req-fn-017"></a>
- **REQ-FN-017** — Dockerfile + compose: app + Postgres/pgvector, optional Qdrant/Ollama profiles; one-command boot incl. TechieDeskDb migration on start (BRD-55).
<a id="d-req-fn-018"></a>
- **REQ-FN-018** — All deployment config via environment variables (BRD-56).
<a id="d-req-fn-019"></a>
- **REQ-FN-019** — Named volumes for App DB, uploads, ONNX model — upgrade-safe (BRD-57).
<a id="d-req-fn-020"></a>
- **REQ-FN-020** — Connector runs as background jobs: progress, per-item results + failure reasons (BRD-65).
<a id="d-req-fn-021"></a>
- **REQ-FN-021** — REST API for workspaces/documents/threads/chat incl. streaming (SSE) (BRD-66).
<a id="d-req-fn-022"></a>
- **REQ-FN-022** — Admin API-key management (create/label/revoke); key auth on every API call; keys stored hashed (BRD-67, BRD-95).
<a id="d-req-fn-023"></a>
- **REQ-FN-023** — Swagger/OpenAPI UI at `/api/docs` (BRD-68).
<a id="d-req-fn-024"></a>
- **REQ-FN-024** — API usage metered; `API_REQUESTS` license level enforced when licensing active (BRD-69).
<a id="d-req-fn-025"></a>
- **REQ-FN-025** — Widget script served by instance; workspace-scoped; API-key-authenticated; license-gated `EMBED_WIDGET` (BRD-70).
<a id="d-req-fn-026"></a>
- **REQ-FN-026** — Promo-code validation via `POST /PaymentSvc/promo-codes/validate` (BRD-79).
<a id="d-req-fn-027"></a>
- **REQ-FN-027** — Close own support issue (`POST /IssueSvc/{id}/close`); ALREADY_CLOSED handled (BRD-82).
<a id="d-req-fn-028"></a>
- **REQ-FN-028** — Cron scheduling for agent/ingestion jobs with run history (BRD-93).
<a id="d-req-fn-029"></a>
- **REQ-FN-029** — All app data access via Dapper: parameterized SQL (SQLite) / stored procedures or queries (PostgreSQL + pgvector); zero EF Core references (BRD-102).
  - *Acceptance:* `grep` of all TechieDesk csproj/code finds no EntityFrameworkCore reference.
<a id="d-req-fn-030"></a>
- **REQ-FN-030** — TechieDeskDb console project applies versioned idempotent DbUp migrations (journaled, ordered); runs standalone + at container start; non-zero exit blocks app start; outcomes via Serilog (BRD-103).
<a id="d-req-fn-031"></a>
- **REQ-FN-031** — Migration scripts per provider (SQLite + PostgreSQL) produce equivalent schemas; verified by booting the app against each (BRD-104).
<a id="d-req-fn-032"></a>
- **REQ-FN-032** — ⚠ **BLOCKER (new 2026-07-20).** Authenticated session must survive the post-login navigation so AppManager mode is actually usable.
  - *Root cause:* `Login.razor:117` `Nav.NavigateTo(SafeReturnUrl(), forceLoad: true)` destroys the Blazor circuit; `SessionTokenStore` is per-circuit by design, so the replacement circuit has no session and `IRouteGuard` redirects to `/login` — indefinitely.
  - *Acceptance:* a user who authenticates successfully against AppManager lands on the requested route in an authenticated session, and a page refresh does not sign them out.
  - *Design tension (owner decision):* the fix needs session continuity across circuits — an auth cookie or ASP.NET Core protected browser storage — which weakens the "tokens are never written to browser storage, cookies, or exposed to JavaScript" property that REQ-FN-002 and REQ-NFR-004 were built around. A signed, HttpOnly, Secure, SameSite cookie carrying only a session handle (not the JWTs) is the usual reconciliation, keeping the tokens themselves server-side. **Not chosen unilaterally.**
  - *Blast radius:* blocks live UAT for REQ-UI-006…013 and REQ-FN-002/003/013/014/015 — i.e. the entire ~27-row "pending owner UAT" track.

## RAG / AI requirements (→ /techierag)

<!-- REQ-RAG-001…023 are app-side RAG behaviors; REQ-RAG-024…046 are the F-LIB library
     gap-closure items — implemented in src/TechieRag* (library-first boundary, BRD §3),
     tracked HERE per the single-checklist governance decision. -->

<a id="d-req-rag-001"></a>
- **REQ-RAG-001** — Direct-LLM streaming chat (BRD-3). Pre-existing, Verified.
<a id="d-req-rag-002"></a>
- **REQ-RAG-002** — Auto-RAG chat with retrieved context + citations (BRD-4). Pre-existing, Verified (workaround citations).
<a id="d-req-rag-003"></a>
- **REQ-RAG-003** — Folder/pattern ingestion via 9 processors (BRD-5). Pre-existing, Verified.
<a id="d-req-rag-004"></a>
- **REQ-RAG-004** — Paste-text ingestion (BRD-6). Pre-existing, Verified.
<a id="d-req-rag-005"></a>
- **REQ-RAG-005** — LLM playground: completion/structured/chat (BRD-7). Pre-existing, Verified.
<a id="d-req-rag-006"></a>
- **REQ-RAG-006** — Tool demo with live execution trace (BRD-11). Pre-existing, Verified.
<a id="d-req-rag-007"></a>
- **REQ-RAG-007** — Retrieval strictly scoped to active workspace's documents (BRD-32; needs REQ-RAG-028).
<a id="d-req-rag-008"></a>
- **REQ-RAG-008** — Persist all messages (incl. citations) via library conversation memory, per user/workspace/thread (BRD-33; needs REQ-RAG-027).
<a id="d-req-rag-009"></a>
- **REQ-RAG-009** — LLM context built from persisted history with token-aware trimming (BRD-37; needs REQ-RAG-027).
<a id="d-req-rag-010"></a>
- **REQ-RAG-010** — App streams answers with native citations, no post-hoc workaround (BRD-38; blocked by REQ-RAG-024).
<a id="d-req-rag-011"></a>
- **REQ-RAG-011** — Upload accepts all supported types (9 + XLSX/PPTX per REQ-RAG-033); clear per-file rejection (BRD-41).
<a id="d-req-rag-012"></a>
- **REQ-RAG-012** — Content-hash dedupe: embed once, reuse across workspaces (BRD-43; needs REQ-RAG-028).
<a id="d-req-rag-013"></a>
- **REQ-RAG-013** — Document pinning: always in workspace context (BRD-44; needs REQ-RAG-028).
<a id="d-req-rag-014"></a>
- **REQ-RAG-014** — Per-workspace similarity threshold, top-K, rerank toggle (BRD-47; needs REQ-RAG-025/028).
<a id="d-req-rag-015"></a>
- **REQ-RAG-015** — Chat mode vs query mode (context-only answers "not in my documents" honestly) (BRD-48; needs REQ-RAG-028).
<a id="d-req-rag-016"></a>
- **REQ-RAG-016** — URL scrape → clean text → embed into workspace (BRD-60; needs REQ-RAG-031).
<a id="d-req-rag-017"></a>
- **REQ-RAG-017** — Site crawler with depth + max-links (BRD-61; needs REQ-RAG-031).
<a id="d-req-rag-018"></a>
- **REQ-RAG-018** — YouTube transcript ingestion by URL (BRD-62; needs REQ-RAG-031).
<a id="d-req-rag-019"></a>
- **REQ-RAG-019** — GitHub/GitLab repo connector (branch + glob filters) (BRD-63; needs REQ-RAG-032).
<a id="d-req-rag-020"></a>
- **REQ-RAG-020** — Confluence space connector (BRD-64; needs REQ-RAG-032).
<a id="d-req-rag-021"></a>
- **REQ-RAG-021** — `@agent` invocation in any workspace chat (BRD-83).
<a id="d-req-rag-022"></a>
- **REQ-RAG-022** — Per-workspace skill toggles; every skill a library `ITool` (RAG search, web search/scrape, SQL, charts, file ops) (BRD-84).
<a id="d-req-rag-023"></a>
- **REQ-RAG-023** — Admin-registered MCP servers expose tools to agent (BRD-86; needs REQ-RAG-038).

### F-LIB — library gap closure (built in `src/TechieRag*`)

<a id="d-req-rag-024"></a>
- **REQ-RAG-024** — LIB: streaming RAG returns sources + honors PromptTemplateEngine — closes TR-RAG-001 (BRD-105, GAP-LIB-01).
  - *Acceptance:* streamed RAG response carries sources; app-side workaround removed; unit + live smoke.
<a id="d-req-rag-025"></a>
- **REQ-RAG-025** — LIB: `IReranker` abstraction; local ONNX cross-encoder + ≥1 API reranker (BRD-106, GAP-LIB-02).
<a id="d-req-rag-026"></a>
- **REQ-RAG-026** — LIB: pluggable `IChunker`: recursive, token-based, markdown/code-aware, sentence (BRD-107, GAP-LIB-03).
<a id="d-req-rag-027"></a>
- **REQ-RAG-027** — LIB: DB-backed `IConversationMemory` (SQLite/Postgres) with threads (BRD-108, GAP-LIB-07).
<a id="d-req-rag-028"></a>
- **REQ-RAG-028** — LIB: workspace/collection primitives: isolated docs+settings, pinning, threshold, query-vs-chat (BRD-109, GAP-LIB-08).
<a id="d-req-rag-029"></a>
- **REQ-RAG-029** — LIB: pricing table externalized to config; streamed-token usage correct on all providers — closes TR-RAG-002 (BRD-110, GAP-LIB-19).
<a id="d-req-rag-030"></a>
- **REQ-RAG-030** — LIB: unit tests for processors, providers, agent loop, memory, cost math — continuous per phase (BRD-111, GAP-LIB-22).
<a id="d-req-rag-031"></a>
- **REQ-RAG-031** — LIB: URL scraper, site crawler (depth/maxLinks), YouTube transcripts (BRD-112, GAP-LIB-05).
<a id="d-req-rag-032"></a>
- **REQ-RAG-032** — LIB: `IDataConnector` + GitHub/GitLab + Confluence connectors (BRD-113, GAP-LIB-06).
<a id="d-req-rag-033"></a>
- **REQ-RAG-033** — LIB: XLSX/PPTX/CSV processors (BRD-114, GAP-LIB-11).
<a id="d-req-rag-034"></a>
- **REQ-RAG-034** — LIB: named connectors (Bedrock, Groq, Mistral, Cohere, DeepSeek, xAI, OpenRouter, Together, Perplexity…) + model-name→provider routing (BRD-115, GAP-LIB-04).
<a id="d-req-rag-035"></a>
- **REQ-RAG-035** — LIB: Cohere/Voyage/Mistral/Gemini embedding providers (BRD-116, GAP-LIB-15).
<a id="d-req-rag-036"></a>
- **REQ-RAG-036** — LIB: OpenTelemetry metric + trace exporters (BRD-117, GAP-LIB-18).
<a id="d-req-rag-037"></a>
- **REQ-RAG-037** — LIB: add net8.0 TFM (netstandard2.0 if feasible) (BRD-118, GAP-LIB-21).
<a id="d-req-rag-038"></a>
- **REQ-RAG-038** — LIB: MCP client consuming MCP tool servers in agent loop (BRD-119, GAP-LIB-12).
<a id="d-req-rag-039"></a>
- **REQ-RAG-039** — LIB: multimodal chat input — vision first, then audio/docs (BRD-120, GAP-LIB-09).
<a id="d-req-rag-040"></a>
- **REQ-RAG-040** — LIB: Whisper (ONNX/API) audio-transcription processor (BRD-121, GAP-LIB-10).
<a id="d-req-rag-041"></a>
- **REQ-RAG-041** — LIB: `ITextToSpeech`/`ISpeechToText` abstractions + API providers (BRD-122, GAP-LIB-16).
<a id="d-req-rag-042"></a>
- **REQ-RAG-042** — LIB: agent orchestration: graphs, handoffs, guardrails, agent-as-tool (BRD-123, GAP-LIB-13).
<a id="d-req-rag-043"></a>
- **REQ-RAG-043** — LIB: prompt-caching passthrough (Anthropic/Gemini) (BRD-124, GAP-LIB-17).
<a id="d-req-rag-044"></a>
- **REQ-RAG-044** — LIB (deferred): Chroma/Milvus/Pinecone/Weaviate-or-LanceDB behind `IVectorStore` (BRD-125, GAP-LIB-14).
<a id="d-req-rag-045"></a>
- **REQ-RAG-045** — LIB (deferred): Microsoft.Extensions.AI interop package (BRD-126, GAP-LIB-20).
<a id="d-req-rag-046"></a>
- **REQ-RAG-046** — LIB (deferred): image-gen / realtime audio / batch / fine-tuning / moderation / OCR (BRD-127, GAP-LIB-23).
<a id="d-req-rag-047"></a>
- **REQ-RAG-047** — LIB: per-workspace rerank switch (new 2026-07-20, TR-RAG-005).
  - *Problem:* `Workspace.RerankEnabled` persists and round-trips but nothing reads it — `ITechieRag.SearchAsync` exposes no per-call rerank switch, so reranking is global configuration only and the per-workspace toggle is inert.
  - *Acceptance:* enabling/disabling rerank on one workspace changes that workspace's retrieval ordering and leaves other workspaces unaffected; asserted by a test that fails if the flag is ignored. Restores REQ-RAG-014 to a verifiable state.
<a id="d-req-rag-048"></a>
- **REQ-RAG-048** — LIB: signal context truncation (new 2026-07-20, TR-RAG-006).
  - *Problem:* `PromptConfig.MaxContextChunks` (default 5) silently truncates the merged pinned+retrieved context. Pinned chunks survive only as a side effect of ordering; more than 5 pinned documents evict every retrieved result with no signal.
  - *Acceptance:* callers can detect that truncation occurred (event/flag/log) and pinned-vs-retrieved eviction is deliberate rather than incidental.

## Non-functional

<a id="d-req-nfr-001"></a>
- **REQ-NFR-001** — Revoke the TrBlazeUI PAT and remove `nuget.config` from git tracking before any public distribution (BRD-58). ⚠ SECURITY.
<a id="d-req-nfr-002"></a>
- **REQ-NFR-002** — All secrets from env/user-secrets; none committed (BRD-59).
<a id="d-req-nfr-003"></a>
- **REQ-NFR-003** — Performance: <2s streaming overhead; <200ms UI interactions; 10-page PDF embedded <60s (local BGE-M3); ≥25 concurrent chat users (BRD-94).
<a id="d-req-nfr-004"></a>
- **REQ-NFR-004** — Security: TLS to AppManager/providers; tokens server-side only; server-side authz; OWASP input hygiene; API keys hashed (BRD-95).
<a id="d-req-nfr-005"></a>
- **REQ-NFR-005** — Accessibility: keyboard nav, focus states, ARIA, WCAG 2.1 AA contrast (BRD-96).
<a id="d-req-nfr-006"></a>
- **REQ-NFR-006** — Every screen usable @1280 and @390 with `scrollWidth == viewport` (BRD-97). Playwright-gated.
<a id="d-req-nfr-007"></a>
- **REQ-NFR-007** — Evergreen Chrome/Edge/Firefox/Safari (BRD-98).
<a id="d-req-nfr-008"></a>
- **REQ-NFR-008** — Data locality: nothing leaves the instance except LLM/embedding-provider and AppManager calls; no telemetry (BRD-99).
<a id="d-req-nfr-009"></a>
- **REQ-NFR-009** — Serilog rolling-file logging under `logs/` in every executable head (app, TechieDeskDb, future heads); unhandled exceptions logged (BRD-100).
<a id="d-req-nfr-010"></a>
- **REQ-NFR-010** — Resilience: AppManager outage → grace (REQ-FN-015); provider failures surface library retry/fallback with visible status; restart loses no persisted data (BRD-101).

## Feedback tracker

<!-- Single-checklist governance (BRD §1 decision 3): ALL library feedback — existing and future —
     is tracked here. TechieRag defects that map to REQs are absorbed above (TR-RAG-001 →
     REQ-RAG-024, TR-RAG-002 → REQ-RAG-029). Items below are third-party (TrBlazeUI) or
     informational; new feedback gets a row here (or a REQ if it's our code to fix). -->

| Item | Source | Severity | Status | Where handled |
|------|--------|----------|--------|---------------|
| TR-RAG-001 streaming RAG sources | TechieRag | Major | Fixed (pending verify) | REQ-RAG-024 (BRD-105) — RagStreamEvent sources 2026-07-18 |
| TR-RAG-002 streamed 0-usage | TechieRag | Minor | Fixed (pending verify) | REQ-RAG-029 (BRD-110) — include_usage + estimation 2026-07-18 |
| TR-RAG-003 no workspace-scoped streaming-with-sources | TechieRag | Minor | **Fixed (pending verify)** | 2026-07-20 FIXED — WorkspaceManager.AskStreamWithSourcesAsync(workspaceId, question, history?, options?, ct) emits RagStreamEvent in the REQ-RAG-024 order Sources→Token(s)→Completed, scoped to the workspace doc set, honoring per-ws topK/threshold/system-prompt/model/chat-vs-query, and merging pinned docs ahead of retrieved results. Private BuildContextAsync refactored into a shared ComposeContextAsync seam + a public XML-documented BuildContextAsync for callers running their own generation loop. Back-compat preserved (66/66 lib tests incl. the 3 pre-existing REQ-RAG-024 tests). Closes the REQ-RAG-013 PARTIAL. ⚠ rerank half NOT addressed → REQ-RAG-047 |
| TR-RAG-005 per-workspace rerank not honored | TechieRag | Major | Open | 2026-07-20: Workspace.RerankEnabled is dead config — no per-call rerank switch on ITechieRag.SearchAsync. Caused REQ-RAG-014 to be demoted from Verified. Tracked as REQ-RAG-047 |
| TR-RAG-006 MaxContextChunks silent truncation | TechieRag | Minor | Open | 2026-07-20: PromptConfig.MaxContextChunks (default 5) truncates merged context with no signal; >5 pinned docs evict all retrieved results silently. Tracked as REQ-RAG-048 |
| TR-RAG-007 Chunk.Metadata is JsonElement, not string | TechieRag | Minor | Open | 2026-07-20: metadata values round-trip from the store as JsonElement, so consumers pattern-matching `value is string` hit a silent fallback — this is exactly why every citation chip rendered a bare GUID instead of the document name (fixed app-side). Library should expose a typed metadata accessor |
| TR-RAG-008 streaming UI needs explicit render yield | TechieRag | Nice-to-have | Open | 2026-07-20: StateHasChanged() after the Sources event is not flushed before the LLM call, so citation chips never paint when the provider fails fast; needed `await Task.Yield()`. Worth documenting for SDK consumers building streaming UIs |
| TR-008 TrBlazeUI a11y defects (60 axe nodes) | TrBlazeUI | **Major** | Open (upstream) | 2026-07-20, blocks REQ-NFR-005 reaching Verified. SelectTrigger: AriaLabel compiles and runs but is NEVER emitted, and role=combobox is nameFrom:author, so 6 controls are UNNAMEABLE from app code (WCAG 4.1.2 critical). Slider + FileUpload have NO AriaLabel property — passing it throws InvalidOperationException at RUNTIME while the build succeeds. Progress accepts AriaLabel but never forwards it. FieldLabel renders `<label>` with no `for` and no For parameter (98 sites app-wide). Tabs aria-controls points at unmounted panels (8 critical). Pagination ul/li malformed + unnamed icon buttons. CardTitle hardcodes h3, AlertTitle h5 → correct document outline impossible. LucideIcon emits role="img" unconditionally with no name. Sidebar focus rings depend on Tailwind JIT utilities the prebuilt bundle never emits → invisible keyboard focus for every consumer |
| TR-009 TrBlazeUI ships a vulnerable transitive dependency | TrBlazeUI | **Major** | Open (upstream) | 2026-07-20: TrBlazeUI.Components 1.0.7 → HtmlSanitizer 9.0.892 → AngleSharp `[0.17.1]` exact pin, which carries NU1902 / GHSA-pgww-w46g-26qg (moderate mXSS). No STABLE HtmlSanitizer release exists on AngleSharp ≥1.5.0, so consumers have no clean stable path. App worked around it by pinning HtmlSanitizer 9.1.949-beta (pulls AngleSharp 1.5.1 + AngleSharp.Css 1.0.0-beta.216 — two PRE-RELEASE packages in a distributable product). Please upgrade the HtmlSanitizer dependency |
| TR-010 DropdownMenuTrigger emitted twice into the DOM | TrBlazeUI | Minor | Open (upstream) | 2026-07-20: trigger button appears twice (in-grid + portaled duplicate) — breaks getByRole counting in tests and is an a11y concern (duplicate interactive controls with identical accessible names) |
| TR-RAG-004 workspace doc API gaps | TechieRag | Minor | Open | 2026-07-18 (Wave 4): IWorkspaceStore.RemoveDocumentAsync deletes only the membership row (not vectors, not other ws) despite contract; WorkspaceDocument is a thin membership record (no name/type/size/chunkCount/uploadDate → app joins global ListDocumentsAsync, no size field anywhere); IngestFileAsync takes a path not a Stream; dedupe is silent (no reused flag). App worked around all four. Suggest richer WorkspaceDocument + delete-vectors-everywhere API + Stream ingest + dedupe result flag |
| TR-002 scoped-css 404 | TrBlazeUI | Nice-to-have | Open (upstream) | Await TrBlazeUI fix; no app action |
| TR-003 SidebarInset min-width | TrBlazeUI | Minor | Open (upstream) | App-side workaround in place; re-check per TrBlazeUI release |
| TR-004 DataTable scroll wrapper | TrBlazeUI | Minor | Open (upstream) | Inert (purged CSS); inline-style pattern mandatory on new DataTables (see REQ-UI-021) |
| TR-007 grid-cols utilities missing | TrBlazeUI | Minor | Open (upstream) | 2026-07-18: prebuilt trblazeui.css (1.0.7) ships flex/gap/space-y but NOT grid-cols-*/lg:grid-cols-* → any `grid-cols-N` collapses to 1 col (also affects pre-existing Settings.razor). App workaround: `.td-grid-2`/`.td-grid-2col` in base.css. Ask upstream to ship grid-cols utils or document app-side Tailwind build |
