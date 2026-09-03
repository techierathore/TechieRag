# TechieRag NuGet Publishing

How TechieRag packages get to consumers, and how to cut a public release.

TechieRag ships to **two feeds**. They are separate pipelines with separate credentials, separate
triggers, and separate audiences. This document is the runbook for both, with most of its weight on
the public one, because that is the one that is irreversible.

> Scope note: this file covers *publishing*. `docs/NUGET-PUBLISHING-GUIDE.md` is an older,
> broader tutorial about NuGet packaging in general; where the two disagree, **this file wins**.

---

## 1. The dual-feed model

| | GitHub Packages (internal) | NuGet.org (public) |
|---|---|---|
| **Purpose** | Internal dev / pre-release feed | The public feed consumers install from |
| **Audience** | Maintainers working on TechieRag itself | Everyone — `dotnet add package TechieRag`, no credentials |
| **Workflow** | `.github/workflows/publish-github-packages.yml` | `.github/workflows/publish-nuget.yml` |
| **Trigger** | Automatic — push to `main`/`master`, `v*` tags (GitHub Releases), PRs | **Manual only** — `workflow_dispatch` against a release tag (dry run from any ref; real run only from a tag) |
| **Credential** | `GITHUB_TOKEN` (built in) | None stored — OIDC temp key (see §2) |
| **Cadence** | Every merge and every release | When the owner decides a release goes public |
| **Reversible?** | Yes — versions can be deleted | **No** — see "unlist, not delete" in §6 |

Three rules hold this together:

1. **Public versions are always a subset of internal-feed versions.** Nothing reaches NuGet.org that
   has not already existed on the GitHub feed. There is no NuGet.org-only build. (The internal
   workflow also fires on the same `v*` tag, so a tag lands on both feeds from the same commit.)
2. **Same version number = same commit.** If `1.0.0` exists on both feeds, both were built from the
   same commit. This is what makes the internal feed a usable rehearsal for the public one.
3. **The public feed never publishes itself.** No push trigger, no tag trigger, no schedule. A
   GitHub Release creates the `v*` tag and feeds GitHub Packages automatically; nuget.org gets that
   version only when a human opens Actions → *Publish to NuGet.org (Trusted Publishing)* → Run
   workflow with the release tag as `ref`. (Reaffirmed 2026-09-03 after a one-day experiment with
   a tag trigger — see `DECISIONS.md` 2026-09-03, second entry — so the ceremony matches the
   owner's other libraries.) What changed on 2026-09-03 and stays: the version is derived from that
   tag, and the run refuses a version already on nuget.org or one not greater than the latest.

### Packages published

Both packable projects go to both feeds:

- **`TechieRag`** — `src/TechieRag/TechieRag.csproj` (targets `net10.0;net8.0`)
- **`TechieRag.Embedded`** — `src/TechieRag.Embedded/TechieRag.Embedded.csproj` (targets `net10.0`)

`TechieRag.Embedded` depends on `TechieRag`, so they are pushed in the same run and always share a
version number. Symbol packages (`.snupkg`) are pushed alongside to NuGet.org's symbol server.

### A note on the workflow file names

The private-feed workflow used to live at `.github/workflows/publish-nuget.yml`. It was **renamed to
`publish-github-packages.yml` with its contents byte-for-byte unchanged** — same `name:` header, same
triggers, same jobs, same behaviour. The rename was forced: the trusted publishing policy on
nuget.org is bound to the file name `publish-nuget.yml` (§2), and that name had to be freed for the
public pipeline. The only consequence is cosmetic — pre-rename runs are listed in the Actions
sidebar under the old file name.

The old private workflow still contains a dormant `publish-nuget-org` job gated on a `NUGET_API_KEY`
secret. **That secret does not exist and must never be created.** The job no-ops (it prints
"skipping") and was left untouched by design. The real public path is `publish-nuget.yml` and OIDC.

---

## 2. Trusted Publishing (OIDC) — why there is no API key

The public pipeline holds **no long-lived credential**. Not in a secret, not in a variable, not on
a laptop. Instead:

1. The job requests an **OIDC token** from GitHub. This requires `permissions: id-token: write` on
   the job — without it the token cannot be issued and the login step fails.
2. The `NuGet/login@v1` action presents that token to nuget.org, along with the nuget.org profile
   name in `secrets.NUGET_USER`.
3. nuget.org checks the token's claims against a **trusted publishing policy** already registered on
   the account. The policy is essentially a pair: **repository + workflow file name**
   (`techierathore/TechieRag` + `publish-nuget.yml`).
4. If they match, nuget.org mints a **temporary API key** and hands it back as the step output
   `NUGET_API_KEY`.

Properties of that temporary key that shape the workflow design:

- **It lives about 1 hour.**
- **Each OIDC token converts to a key exactly once.** A token is not reusable.

Hence the step ordering, which is not stylistic:

```
checkout → setup-dotnet → restore → build → test → pack → inspect → [login → push]
```

Login sits **immediately before push**, after every gate has already passed. A failing test never
burns a token, and the key never sits idle while a long build runs. On a dry run, login and push are
skipped entirely by `if:` conditions — a dry run makes no OIDC request at all.

**`secrets.NUGET_USER`** holds the nuget.org **profile name**, not a password or key. It identifies
which account's policy to evaluate.

### The thing that will break this

**Renaming or moving `.github/workflows/publish-nuget.yml` invalidates the policy.** The policy
matches on the file name. If the file is renamed, the OIDC exchange is rejected until the policy is
updated in the nuget.org UI to point at the new name. Treat the file name as part of the published
contract.

### "Temporarily active (7 days)"

Because **nothing has ever been published to nuget.org from this account**, the policy may display
as *temporarily active* with a 7-day window. That is nuget.org's anti-squatting guard for brand-new
accounts:

- **The first successful publish through the policy locks it permanently.** After that the warning
  disappears and the policy is durable.
- **If the 7-day window lapses before that first publish**, the policy goes inactive and OIDC login
  starts failing. Fix: go to the nuget.org UI → account → Trusted Publishing → restart / re-activate
  the policy, which opens a fresh window. No code change is needed; the policy content stays the same.

So: once the dry run looks right, do the real run reasonably promptly rather than letting it sit.

---

## 3. Package metadata checklist

Audited and enforced on both packable projects. Verified by unpacking a locally built `.nupkg`.

| Requirement | Property | Status |
|---|---|---|
| Package ID | `PackageId` | `TechieRag`, `TechieRag.Embedded` |
| Author | `Authors` | `Techie Rathor` |
| Description | `Description` | Present on both, consumer-facing |
| License | `PackageLicenseExpression` | `MIT` (renders as a license link on nuget.org) |
| Project URL | `PackageProjectUrl` | `https://github.com/techierathore/TechieRag` |
| Repository URL | `RepositoryUrl` + `RepositoryType` | `…/TechieRag.git`, `git` |
| Tags | `PackageTags` | Search terms for nuget.org |
| README on the listing page | `PackageReadmeFile` + `<None Include="..\..\README.md" Pack="true" PackagePath="\" />` | Root `README.md` ships inside the package |
| Source stepping | `PublishRepositoryUrl`, `EmbedUntrackedSources` | **Added** |
| Symbols | `IncludeSymbols`, `SymbolPackageFormat=snupkg` | **Added** |
| No accidental packing | `GeneratePackageOnBuild` | **Added, set to `false`** |
| XML docs | `GenerateDocumentationFile` | `true` |

**SourceLink:** the .NET 8+ SDK bundles the GitHub SourceLink provider, so no
`Microsoft.SourceLink.GitHub` PackageReference is needed — the switches above are sufficient. The
publish workflow additionally passes `-p:ContinuousIntegrationBuild=true` so recorded source paths
are deterministic rather than machine-local.

**Verified in a local pack** — the generated `.nuspec` contains:

```xml
<license type="expression">MIT</license>
<readme>README.md</readme>
<repository type="git" url="https://github.com/techierathore/TechieRag.git"
            branch="refs/heads/main" commit="e2e72183acc736539c9051aec941e27ec8fc0d2f" />
```

That `commit=` attribute is SourceLink working: consumers can step into the exact sources that built
the binary.

**The version comes from the tag, never from the csproj.** `publish-nuget.yml` runs
`.github/workflows/scripts/determine-version.sh` as its first step after checkout. That script takes
the `v*` tag the run sits on, strips the `v`, and passes the result as `-p:Version=` to build, test
and pack, so the assembly version, the test build and the package all agree. The `<Version>` in the
`.csproj` is a **dev-only number that never ships** to nuget.org — it only surfaces in a dry run from
a non-tag ref, which packs `<csprojVersion>-dryrun.<run>` for inspection. Before anything is built,
the same script queries nuget.org and **fails the run** if the tag's version is already published or
is not greater than the latest published version for either package — a release increments, and a
re-run of an old tag is an error, not a no-op. The first public release was `1.0.0`, the stable
version TechieRag had been shipping on the internal feed; going public did not restart its history.

---

## 4. Running a public release

Same ceremony as the owner's other libraries: **release first, publish second.**

### Step 1 — publish a GitHub Release (creates the tag, feeds GitHub Packages)

GitHub → **Releases** → *Draft a new release* → new tag `v1.0.7` on the merged `main` commit →
*Publish release*. GitHub creates the tag; `publish-github-packages.yml` fires on it and puts `1.0.7`
on the internal feed. Nothing reaches nuget.org yet.

### Step 2 — dispatch the public workflow against that tag

GitHub **Actions** tab → **Publish to NuGet.org (Trusted Publishing)** → **Run workflow**.

| Input | Meaning |
|---|---|
| `ref` | **The release tag** (`v1.0.7`). A real run must be a `v*` tag — any other ref fails in `Determine version` before anything is built. A dry run accepts any branch or SHA. |
| `dry_run` | `true` = build, test, pack, list package contents, then stop. No OIDC login, no push. From a non-tag ref it packs `<csprojVersion>-dryrun.<run>`, for inspection only. `false` = the real thing, and only from a tag. |

The version `1.0.7` is derived from the tag name (§3), checked against nuget.org, and published only if
it is new and greater than the latest. Pick the tag deliberately — once the push succeeds, that version
is on nuget.org for good.

### Recommended: rehearse before the real run

**Pass 1 — `dry_run: true`** on the release tag (or on the commit before tagging). Costs nothing,
touches nuget.org not at all, and is the last chance to see what would ship.

**Pass 2 — `dry_run: false`** on the same tag. Publishes.

### What the log should show

1. **"Checkout <ref>"** — the requested ref. Check the resolved SHA is the commit you meant.
2. **"Determine version"** — `determine-version.sh` prints where the version came from. On a tag:
   `TechieRag: latest on nuget.org = 1.0.6; 1.0.7 is new and greater.` (and the same line for
   `TechieRag.Embedded`). This is the **increment check**: if the tag's version is already on
   nuget.org, or is not greater than the latest published, the run fails **here**, before restore. On
   a dry run from a non-tag ref it reports the `-dryrun.<run>` version instead. On a real run from a
   non-tag ref it fails with *"A public release is cut from a v* tag…"* — dispatch the release tag instead.
3. **Restore / Build / Test** — tests are a blocking gate. Red tests, no release. Every `dotnet`
   invocation carries `-p:Version=<tag version>`.
4. **Pack** — `Successfully created package …/TechieRag.<version>.nupkg` (and `.snupkg`), likewise for
   `TechieRag.Embedded`.
5. **"Confirm packed version"** — verifies the `.nupkg` file names carry exactly the version
   `Determine version` decided on (a mismatch means a csproj property fought the `-p:Version`
   override, and the run stops), then prints the banner box:
   ```
   ==============================================
     PUBLISHING TechieRag VERSION 1.0.7
     from ref 'v1.0.7' (e2e7218)
     dry_run = false
   ==============================================
   ```
   The same facts are written to the run summary page. **Read this before anything else** — it is the
   single most important line in the run.
6. **"Inspect package contents"** — a full `unzip -l` file listing of every `.nupkg` and `.snupkg`.
   This is what makes each run self-documenting: months later the job log still shows exactly which
   files shipped in that version. Sanity-check that `README.md`, `lib/net10.0/`, `lib/net8.0/` and the
   `build/` + `buildTransitive/` AI-agent content are present.
7. On a **dry run**, it stops here with *"dry_run was true: no OIDC login was performed and nothing
   was pushed."* That is success.
8. On a **real run**, next comes **"NuGet.org login (OIDC → temporary API key)"** — the `NuGet/login@v1`
   step exchanging the token. It should complete in seconds. The key itself is masked in the log; you
   will never see it.
9. **"Push to NuGet.org"** — `dotnet nuget push …` for each package, **without `--skip-duplicate`**.
   Expect `Your package was pushed.` A `409 Conflict` here is a **real failure**, not a skip:
   `Determine version` already proved the version was absent, so a duplicate at this point means
   something raced or the guard was bypassed. Re-running a run is no longer "harmless" — a version
   that is already up fails in step 2, by design.

### After the push

- NuGet.org puts the package into **"validating"** — virus scan, signature and metadata checks.
  Usually a few minutes. You can watch it under your nuget.org profile → *Manage packages*.
- Once validated it is live, but **search indexing lags**: allow up to about **an hour** before
  `TechieRag` shows up in nuget.org search or in Visual Studio's package browser. The direct URL
  `https://www.nuget.org/packages/TechieRag` works sooner than search does. Do not re-run the
  workflow because search looks empty.

---

## 5. Verification

Once validation completes, verify as a *stranger would* — from a clean project that knows nothing
about the private feed:

```bash
cd $(mktemp -d)
dotnet new console -n PublicFeedCheck
cd PublicFeedCheck

# Ignore any machine-level NuGet.config; use nuget.org and nothing else.
cat > NuGet.config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

dotnet add package TechieRag
dotnet build
```

The `<clear />` matters — it guarantees the restore cannot silently fall back to the GitHub Packages
feed and give a false pass. Also clear the local cache first (`dotnet nuget locals http-cache
--clear`) if a previous failed attempt may have cached a 404.

Checklist on the nuget.org listing page:

- README renders on the package page
- License shows **MIT** and links out
- Repository / project links resolve to the GitHub repo
- Both `net10.0` and `net8.0` appear under supported frameworks for `TechieRag`
- `TechieRag.Embedded` lists `TechieRag` as a dependency at the same version

---

## 6. Troubleshooting

**`Determine version` failed: "already on nuget.org" / "is not greater than the latest published
version".** The tag's version has already shipped, or it is lower than / equal to what is live. This
is the increment guard working, not a bug. A published version is permanent (see "unlist, not
delete" below), so the fix is always to **bump the tag**: publish a new GitHub Release with the next version
(`v1.0.8`) and dispatch against that. Do not delete and re-create the same tag — the guard will
reject it again.

**`Determine version` failed: "A public release is cut from a v* tag, and '<ref>' is not one".** A
real (non-dry) dispatch run was started on a branch or SHA. Either push a `v*` tag on that commit and
let the tag path publish it, or re-run the dispatch with `dry_run: true` if you only wanted to
inspect the packages.

**Login step fails / OIDC rejected.**
Check in this order: (a) does the job have `permissions: id-token: write`? (b) is the workflow file
still named exactly `publish-nuget.yml`? (c) is `secrets.NUGET_USER` set to the nuget.org **profile
name**? (d) is the policy still active on nuget.org, or did the 7-day temporary window lapse (§2)?
Never "fix" this by adding an API key secret.

**Validation failure on nuget.org.** The package uploads fine and then fails validation minutes
later; you get an email. Common causes are metadata the validator rejects or a symbol package that
does not match its `.nupkg`. Fix the cause, **bump the version**, and publish again — a version that
failed validation cannot be re-uploaded with different content.

**Package ID conflict.** If `TechieRag` were already taken, the push would be rejected with a
403/409. It is not taken today, but note that ID ownership is claimed by first publish. This is one
more reason not to sit on the temporary policy window.

**Indexing delay.** "It pushed but I can't find it" is almost always the ~1 hour search index lag,
not a failure. Confirm via the direct package URL before doing anything else.

**Unlist, not delete.** NuGet.org does **not** allow deleting a published version — this is
deliberate, so that consumers' builds do not break. The most you can do is *unlist* it, which hides
it from search and the package browser while leaving it restorable for anyone who already depends on
it. Treat every public push as permanent. This is precisely what the dry run is for — and why the
push carries no `--skip-duplicate`: re-running a version that is already up is refused by the
`Determine version` guard rather than quietly skipped, so a failed re-run of an old tag is expected.

**"Policy inactive" warning on nuget.org.** See §2 — restart the policy from the nuget.org UI. No
repo change required.

**`NU5129` warning during pack.** The build emits
`At least one .targets file was found in 'buildTransitive/', but 'buildTransitive/TechieRag.targets'
was not.` This is **pre-existing** and comes from the AI-agent content `PackagePath` entries in
`TechieRag.csproj` producing doubled slashes; it predates the public pipeline and affects the private
feed identically. It is a warning, not an error, and does not block publishing. Worth cleaning up
separately if transitive auto-deploy of the agent files ever misbehaves.

**Break-glass: OIDC unavailable.** If trusted publishing is ever broken on nuget.org's side and a
release genuinely cannot wait, an API key can be created **manually and ad hoc** on nuget.org, scoped
as narrowly as possible (single package ID, shortest available expiry), used for that one push, and
then **deleted immediately**. Conditions on this escape hatch:

- The key is **created at the moment of need and never pre-created**. There is no standing key.
- It is never committed to a repo secret if it can be avoided — prefer a local
  `dotnet nuget push` from a machine.
- It is revoked on nuget.org the moment the push completes.
- The incident gets a line in `DECISIONS.md`.

The default remains: **no long-lived credentials anywhere.**

---

## 7. Backlog

**Joint ID prefix reservation.** Once both `TechieRag*` and `TrBlazeUI*` are live on NuGet.org, apply
to Microsoft for a **prefix reservation** covering both families in a single application. A reserved
prefix puts the blue "owner-verified" tick on the listings and blocks anyone else from publishing an
ID under those prefixes. It requires the packages to already be published, which is why it is backlog
rather than a prerequisite — hence "after both libraries are live".
