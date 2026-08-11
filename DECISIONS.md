# Decisions

Durable architectural and process decisions for this repository. Newest first. Each entry records
what was decided and, where it is not obvious, why — so that a future reader does not re-litigate it.

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
