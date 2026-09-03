# Decisions

Durable architectural and process decisions for this repository. Newest first. Each entry records
what was decided and, where it is not obvious, why — so that a future reader does not re-litigate it.

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
