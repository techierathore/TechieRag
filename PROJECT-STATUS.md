---
project: TechieRag
stack: .NET 10 class library (NuGet) + TechieRag.Embedded (ONNX BGE-M3) + TechieDesk Blazor Server app (TrBlazeUI, apps/TechieDesk)
last_updated: 2026-07-20
current_phase: Build — UAT blocked by REQ-FN-032
last_verified_build: PASS
last_verified_date: 2026-07-20
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet) + TechieDesk product app. **This file is the dashboard only** —
per-REQ evidence lives in `docs/TechieDesk-Checklist.md` (Requirements Status table; single live tracker for
app + library + feedback); the frozen legacy scope in `docs/TechieRag-Checklist.md`; library defects in the
per-library feedback files.

## Where I am

**2026-07-20 `*build-phase TechieDesk` — 5 parallel clusters, scope = close both PARTIALs + 6 cross-cutting NFRs.**
Build 0-err, 0×NU1902; `dotnet test` **270/270** (66 lib + 204 app). Both PARTIALs cleared: REQ-RAG-011/033
(XLSX/PPTX/CSV processors, reusing existing OpenXml dep) and REQ-RAG-013 + TR-RAG-003 (workspace-scoped
`AskStreamWithSourcesAsync` merging pinned docs). Six NFRs moved off `Not Started`: NFR-003 (perf — 4.2ms UI, 2.9s
10-page embed, 100/100 circuits), NFR-007 (120/120 cross-engine cells, 0px overflow), NFR-008 (zero egress
violations), NFR-010 (restart-safety proven), NFR-004 + NFR-005 `PARTIAL`.

**Three prior rows were overstated and are corrected.** REQ-UI-007 `Implemented 90% → FAIL` (login "confirmed" was
the network call, not a usable session — REQ-FN-032). REQ-RAG-014 `Verified → Needs re-verify` (persistence was
verified, behavior recorded; `Workspace.RerankEnabled` is dead config — REQ-RAG-047). The 2026-07-18 verify ledger's
boot line omits `ASPNETCORE_ENVIRONMENT=Development`, under which Blazor never boots — its render-gate claims are
unfalsifiable (screenshots gone); Cluster D's 20-route × 3-engine re-sweep restores confidence in the render half.
Nothing was promoted to `Verified` this run — build-phase's ceiling is `Implemented`.

## Next command to run

⚠ The prior "Owner UAT — READY NOW" pointer was WRONG and is withdrawn — AppManager mode cannot be signed into
(REQ-FN-032). UAT is blocked until it is fixed.

`/TechieFlow:agents:flow-master *build-phase TechieDesk` — open: **REQ-FN-032** (login loop; needs an owner
architecture decision on session continuity first — see Known blockers), REQ-RAG-047, REQ-RAG-048, plus P2 scope
(REQ-RAG-016…020/031/032/034/035, REQ-FN-020).

Verify only after those: `/TechieFlow:agents:verifier *verify all TechieDesk`. Its promotion ceiling is capped by
three blockers — login loop (auth track), TR-008 TrBlazeUI a11y defects (NFR-005), no LLM provider (streamed rows).

Docs: `*devguide TechieDesk` still does not exist.

## Open requirements

- **TechieDesk: 90 open** of 130 in `docs/TechieDesk-Checklist.md#requirements-status` — **40 terminal**
  (29 `Verified` + 11 `Done (pre-existing)`). Open: 1 `FAIL` (UI-007 login loop); 1 `Needs re-verify` (RAG-014,
  needs RAG-047); 2 `PARTIAL` (NFR-004 owner decisions, NFR-005 blocked by TR-008); ~33 `Implemented` — auth track
  (UI-006/008…013, FN-002/003/013/014/015) now **UAT-blocked** by FN-032, plus RAG-010/011/013/033,
  NFR-003/007/008/010, FN-012/016/017/018/019/031, UI-017…023, NFR-002; NFR-001 `Blocked` (owner PAT);
  3 NEW `Not Started` (FN-032, RAG-047, RAG-048); ~50 `Not Started` in P2–P5.
- **TechieRag (legacy scope): none open.** All 37 REQs terminal in the frozen `docs/TechieRag-Checklist.md`.

## Known blockers

- 🚫 **BLOCKER (REQ-FN-032) — AppManager mode cannot be logged into.** `Login.razor:117` `NavigateTo(…, forceLoad: true)` destroys the circuit; `SessionTokenStore` is per-circuit by design, so the next circuit has no session and `IRouteGuard` loops to `/login`. Login itself succeeds (AppManager 200). Fix needs session continuity across circuits (auth cookie / protected browser storage), weakening the "tokens never leave the server" property FN-002/NFR-004 assume — usual reconciliation is a signed HttpOnly/Secure/SameSite cookie holding only a session handle. **OWNER ARCHITECTURE DECISION; not made unilaterally.** Blocks the entire ~13-row live AppManager UAT track.
- ⚠ **OWNER DECISION (REQ-NFR-004a):** the NU1902 fix pins `HtmlSanitizer 9.1.949-beta`, putting TWO pre-release packages (+ `AngleSharp.Css 1.0.0-beta.216`) into a distributable product. No stable HtmlSanitizer exists on AngleSharp ≥1.5.0; real fix is upstream (TR-009).
- ⚠ **OWNER DECISION (REQ-NFR-004b):** `techierag-config.json` persists provider API keys in cleartext at rest. Gitignored, so NFR-002 (no committed secrets) stands — but it wants encryption at rest.
- ⚠ **Security (REQ-NFR-001):** TrBlazeUI PAT tracked in `nuget.config` history — owner must `git rm --cached nuget.config` + revoke/rotate the PAT (git/GitHub actions, owner-only). Working tree is clean + gitignored.
- ⚠ **TR-008 (REQ-NFR-005):** 60 axe nodes (15 critical + 27 serious) remain inside TrBlazeUI 1.0.7 and are NOT app-fixable — Select triggers are unnameable, Tabs emit invalid `aria-controls`, Slider/Progress/FileUpload unnamed. NFR-005 cannot reach `Verified` without a TrBlazeUI release.
- ✅ **NU1902 CLEARED** 2026-07-20 — AngleSharp now resolves to 1.5.1; build 0×NU1902, `dotnet list --vulnerable` clean.
- **Live externals for UAT:** AppManager is **wired and live-proven** — `admin@appmanager.local` logged in end-to-end (public-key + login 200 over TLS to `192.168.1.14:5101`). Self-signed cert handled by the dev-only `AppManager:AllowUntrustedServerCertificate` flag (Development-only, AppManager client only). The auth network cluster (UI-006…013 / FN-001…007) is now UAT-reachable — run `*verify all` to promote. Still needed: an **LLM provider** for streamed-chat rows and **Docker** for the Postgres/compose rows.
- RAG retrieval runs offline via bundled BGE-M3 (cached under apps/TechieDesk/bin/.../models/bge-m3) — no external embed server.

## Library feedback summary

- **TrBlazeUI:** 0 major, 2 open minor (TR-003 SidebarInset; TR-004 DataTable scroll), 1 nice-to-have
  (TR-002 css 404 — `TechieDesk.styles.css` 404 re-confirmed this run, cosmetic, all pages render styled) — `docs/TechieRag-TrBlazeUI-Feedback.md`
- **TechieRag:** TR-RAG-001/002 fixed (REQ-RAG-024/029, Verified); TR-RAG-003/004 open minor (app worked around) — `docs/TechieRag-TechieRag-Feedback.md`

## Standards compliance

- Verifier greps clean 2026-07-18: 0 underscore-field violations, 0 test-method-underscore violations, 0 EF Core refs in apps/. Build 4 warnings (all NU1902 AngleSharp).

## Verification log

| Date | Phase | Result |
|------|-------|--------|
| 2026-07-02 | Full verify (`*verify all`, 36 REQs, live LM Studio + Qdrant) | Builds 0-err, xUnit, Playwright 37/37; live ingest/RAG/token/Qdrant proven — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-02 | Handoff | Ship-ready for UAT; all 36 REQs terminal — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-17 | Build+verify (`*build-phase`, REQ-UI-014 rename) | TechieDesk rename Verified; Playwright 10/10 @1280/390; all 37 legacy REQs terminal — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-17 | Split (`*split-brd TechieDesk`) | TechieDesk BRD + checklist: 127 REQs (40 UI/31 FN/46 RAG/10 NFR); 11 Done pre-existing, 2 PARTIAL, 114 Not Started |
| 2026-07-18 | Full verify (`*verify all` TechieDesk, Phase 1) | Build 0-err; 180/180 tests; greps clean; 19-route Playwright sweep @1280/390 + live drives. **26 REQs → Verified** (auth/persistence logic, RAG-024…030 lib primitives, UI-014/015/016 live, NFR-009). **REQ-UI-022 visual FAIL** (/setup stepper overflow @390) → Needs re-verify; NFR-006 PARTIAL. AppManager/LLM/Docker rows render-confirmed, pending owner UAT — [details](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-07-18 | Build Phase-1 (`*build-phase TechieDesk`, 6 waves + fix) | All Phase-1 features built via parallel subagents (trblazeui/techierag/flow-master). Clean rebuild 0-err; **204/204 tests** (53 lib + 151 app); greps clean. Waves 0-5 (foundation/auth/library/workspaces/doc-library/wizard-docker) + Wave 6 (thread export/delete FN-010/011, licensing FN-013/014/015, /setup @390 fix). **+4 Verified** (UI-022 re-verified, FN-010/011, NFR-006) → **30 Verified total**; FN-013/014/015 Implemented (AppManager UAT). 2 new lib gaps logged (TR-RAG-003/004) — [details](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-07-18 | Config + live AppManager login | Moved AppManager creds to gitignored `appsettings.Development.json` (no env vars, no committed secret; NFR-002 restored); added dev-only `AppManager:AllowUntrustedServerCertificate` (Development + AppManager-client-scoped) for the self-signed host. Build 0-err. **Live-proven:** boot → AppManager mode; `GET /AuthSvc/public-key` + `POST /AuthSvc/login` → 200 over TLS to `192.168.1.14:5101`; **superadmin login succeeded** (RSA pw accepted). Auth cluster (UI-006…013/FN-001…007) now UAT-reachable — next: `*verify all` to promote — [details](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-07-18 | Re-verify (`*verify all TechieDesk`, offline sweep) | Independent re-verification, no verdict changes. Clean build; **204/204 tests** (151 app + 53 lib) re-run green. Booted offline single-user Admin (port 5099) + 20-route Playwright render+visual sweep @1280/390: **0 horizontal overflow anywhere** (NFR-006), no blank/error screens; 14 screens visually confirmed look-right incl. all 11 `Done (pre-existing)` console rows + auth/workspace/doc/profile/wizard/pricing. **0 new defects.** No promotions possible offline — 27 `Implemented` rows need live AppManager/LLM/Docker (owner UAT). Re-confirmed known issues: TR-002 scoped-css 404 (cosmetic), NU1902 AngleSharp (NFR-004). No DevGuide exists → `*devguide TechieDesk` recommended — [details](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-07-20 | Build (`*build-phase TechieDesk`, 5 clusters: FIX + NFR audits) | Build 0-err, **0×NU1902**; **270/270 tests** (66 lib + 204 app). Closed both PARTIALs: RAG-011/033 (XLSX/PPTX/CSV processors, existing OpenXml dep reused) and RAG-013 + **TR-RAG-003** (workspace-scoped `AskStreamWithSourcesAsync` merging pinned docs; pinned chip live-observed pre-token). 6 NFRs off `Not Started`: NFR-003 (4.2ms UI / 2.9s 10-page embed / 100-circuit), NFR-007 (**120/120** cross-engine cells, 0px overflow), NFR-008 (0 egress violations), NFR-010 (restart-safety proven; vector-store outage mis-reported as "workspace does not exist" — **fixed**), NFR-004 + NFR-005 `PARTIAL`. **3 corrections to prior rows:** UI-007 → `FAIL` (**REQ-FN-032 login loop** — AppManager mode unusable; prior "live login confirmed" proved the network call, not a session), RAG-014 `Verified` → `Needs re-verify` (rerank is dead config → REQ-RAG-047), and the 2026-07-18 ledger boot line omits `ASPNETCORE_ENVIRONMENT=Development` (without it Blazor never boots) — corrected in `docs/.last-verify.json`. **Nothing promoted to `Verified`** (build ceiling = `Implemented`). 8 new library gaps logged (TR-RAG-005…008, TR-008/009/010) — [details](docs/TechieDesk-Checklist.md#requirements-status) |

## Deferred / future

- TechieDesk Phase 2–5 roadmap (connectors, REST API, widget, agents, i18n, billing/support) — per-phase REQs in `docs/TechieDesk-Checklist.md`.
- OpenTelemetry exporters (REQ-RAG-036), net8.0 TFM (REQ-RAG-037), more vector stores/providers (REQ-RAG-034/035/044).
