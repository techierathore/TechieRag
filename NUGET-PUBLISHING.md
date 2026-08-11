# TechieRag NuGet Publishing

How TechieRag packages get to consumers, and how to cut a public release.

TechieRag ships to **two feeds**. They are separate pipelines with separate credentials, separate
triggers, and separate audiences. This document is the runbook for both, with most of its weight on
the public one, because that is the one that is irreversible.

> Scope note: this file covers *publishing*. `docs/NUGET-PUBLISHING-GUIDE.md` is an older,
> broader tutorial about NuGet packaging in general; where the two disagree, **this file wins**.

---

## 1. The dual-feed model

| | GitHub Packages (private) | NuGet.org (public) |
|---|---|---|
| **Purpose** | Daily / dev feed | On-demand public releases |
| **Audience** | Us, and anything wired to the private feed | Everyone |
| **Workflow** | `.github/workflows/publish-github-packages.yml` | `.github/workflows/publish-nuget.yml` |
| **Trigger** | Automatic — push to `main`/`master`, `v*` tags, PRs | Manual only — `workflow_dispatch` |
| **Credential** | `GITHUB_TOKEN` (built in) | None stored — OIDC temp key (see §2) |
| **Cadence** | Every merge | Deliberate, occasional |
| **Reversible?** | Yes — versions can be deleted | **No** — see "unlist, not delete" in §6 |

Three rules hold this together:

1. **Public versions are always a subset of private-feed versions.** Nothing reaches NuGet.org that
   has not already existed on the GitHub feed. There is no NuGet.org-only build.
2. **Same version number = same commit.** If `1.0.0` exists on both feeds, both were built from the
   same commit. This is what makes the private feed a usable rehearsal for the public one.
3. **The public feed never publishes itself.** No push trigger, no tag trigger, no schedule. A human
   picks a ref and clicks Run workflow. Every public release is an intentional act.

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

**Version is deliberately untouched.** The first public release is the **current stable version**
(`1.0.0` at the time of writing), not a reset to `0.1.x`. TechieRag has been shipping on the private
feed and going public does not restart its history. Bump versions in the `.csproj` as normal;
the publish workflow reads whatever is there and prints it prominently.

---

## 4. Running a public release

Everything happens in the GitHub **Actions** tab → **Publish to NuGet.org (Trusted Publishing)** →
**Run workflow**.

### Inputs

| Input | Meaning |
|---|---|
| `ref` | **Required.** Tag, branch or SHA to publish. Defaults to `main`, but it must be confirmed on every run — that is the guardrail. A release is a choice of a specific commit, never "whatever `main` happens to be at the moment I clicked". |
| `dry_run` | `true` = build, test, pack, list package contents, then stop. No OIDC login, no push. `false` = the real thing. |

### Always do it in two passes

**Pass 1 — `dry_run: true`.** Costs nothing, touches nuget.org not at all, and is the last chance to
see what would ship.

**Pass 2 — `dry_run: false`.** Same ref. Publishes.

### What the log should show

1. **"Show what is being published"** — the requested ref plus the *resolved SHA*. Check that the SHA
   is the commit you meant.
2. **Restore / Build / Test** — tests are a blocking gate. Red tests, no release.
3. **Pack** — `Successfully created package …/TechieRag.<version>.nupkg` (and `.snupkg`), likewise for
   `TechieRag.Embedded`.
4. **"Resolve package version"** — a banner box:
   ```
   ==============================================
     PUBLISHING TechieRag VERSION 1.0.0
     from ref 'main' (e2e7218)
     dry_run = false
   ==============================================
   ```
   The same facts are written to the run summary page. **Read this before anything else** — it is the
   single most important line in the run.
5. **"Inspect package contents"** — a full `unzip -l` file listing of every `.nupkg` and `.snupkg`.
   This is what makes each run self-documenting: months later the job log still shows exactly which
   files shipped in that version. Sanity-check that `README.md`, `lib/net10.0/`, `lib/net8.0/` and the
   `build/` + `buildTransitive/` AI-agent content are present.
6. On a **dry run**, it stops here with *"dry_run was true: no OIDC login was performed and nothing
   was pushed."* That is success.
7. On a **real run**, next comes **"NuGet.org login (OIDC → temporary API key)"** — the `NuGet/login@v1`
   step exchanging the token. It should complete in seconds. The key itself is masked in the log; you
   will never see it.
8. **"Push to NuGet.org"** — `dotnet nuget push … --skip-duplicate` for each package. Expect
   `Your package was pushed.` (or, on a re-run of a version already up, a skip message rather than an
   error — that is `--skip-duplicate` doing its job, which makes re-running a run harmless).

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
it. Treat every public push as permanent. This is precisely what the dry run is for.

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
