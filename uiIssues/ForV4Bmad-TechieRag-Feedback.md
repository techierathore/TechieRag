# TechieRag Feedback — surfaced during ForV4Bmad framework work

> For the TechieRag team. Found during the 2026-06-12 framework audit (see `WorkFlow-Issues.MD`).
> Per-app feedback lives in each consumer repo as `docs/<APP>-TechieRag-Feedback.md` — one file
> per library so it can be handed to (or picked up by) the owning team directly.

## Summary
- 1 major, 0 minor
- Last consolidated: 2026-06-12

## Issues

### TR-RAG-001 — Agent persona deploys to a path Claude Code never scans
- **Severity:** major
- **Repro:** `dotnet add package TechieRag && dotnet build` in any consumer repo. The MSBuild target writes `.claude/techierag.md` (plus `.opencode/command/techierag.md` and `.techierag/TechieRag-AI-Reference.md`). Open Claude Code → `/techierag` is not a registered command.
- **Expected:** the short slash form `/techierag` works in Claude Code right after `dotnet build`.
- **Actual:** Claude Code only registers commands found under `.claude/commands/`; files at the `.claude/` root are ignored, so the persona is invisible. (OpenCode is fine — `.opencode/command/` is correct for that harness.)
- **Encountered in:** ForV4Bmad framework audit 2026-06-12, issue I-13 in `WorkFlow-Issues.MD`.
- **Workaround:** the framework's `scaffold-*.sh` and `update-framework.sh` now shim-copy `.claude/techierag.md` → `.claude/commands/techierag.md` after every run (NuGet file stays authoritative). The shim only refreshes when a script runs, so a persona updated by `dotnet build` is stale in Claude Code until the next `update-framework.sh`.
- **Suggested fix:** change the MSBuild deploy target to write the Claude Code copy to `.claude/commands/techierag.md` (and clean up the legacy `.claude/techierag.md` on deploy). Apply the fix in the shared deploy-target template so future library agents inherit the correct path (WORKFLOW.html §9.1 pattern).
