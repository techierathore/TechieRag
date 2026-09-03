#!/usr/bin/env bash
# Determine the package version for a public NuGet.org release - called by the
# `Determine version` step of .github/workflows/publish-nuget.yml.
#
# Inputs (environment, all as GitHub Actions provides them):
#   GITHUB_REF          refs/tags/v1.0.7 when the dispatch `ref` is a tag; refs/heads/... from a branch
#   GITHUB_REF_NAME     v1.0.7 / main / ...
#   GITHUB_RUN_NUMBER   used only for the dry-run fallback version
#   GITHUB_OUTPUT       the step-output file (falls back to /dev/stdout when unset, for local replay)
#   DRY_RUN             "true" | "false"
#   CORE_PROJECT        path to TechieRag.csproj (dry-run fallback reads its <Version>)
#   PACKAGE_IDS         space-separated nuget.org ids to gate the increment check on
#   NUGET_FLAT_BASE     override the flat-container base URL (tests point it at a fixture dir)
#
# Rules (DECISIONS.md 2026-09-03):
#   1. The version is the `v*` tag with the v stripped. Never the csproj's <Version>.
#   2. A real run on a ref that is not a v* tag FAILS. Dispatch against the release tag.
#   3. A dry run on a non-tag ref packs <csproj Version>-dryrun.<run> so the packages can be
#      inspected, and skips the nuget.org checks (that version is never pushed).
#   4. A tag version already on nuget.org FAILS before anything is built.
#   5. A tag version not greater than the latest published FAILS - a release increments.
set -euo pipefail

OUT="${GITHUB_OUTPUT:-/dev/stdout}"
DRY_RUN="${DRY_RUN:-false}"
CORE_PROJECT="${CORE_PROJECT:-src/TechieRag/TechieRag.csproj}"
PACKAGE_IDS="${PACKAGE_IDS:-techierag techierag.embedded}"
NUGET_FLAT_BASE="${NUGET_FLAT_BASE:-https://api.nuget.org/v3-flatcontainer}"

fail() { echo "::error::$*" >&2; exit 1; }

# --- 1. Which tag, if any -------------------------------------------------------------------
TAG=""
if [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then
  TAG="${GITHUB_REF_NAME}"
else
  # Dispatch on a ref: honour it only if HEAD sits exactly on a v* tag.
  TAG="$(git tag --points-at HEAD 2>/dev/null | grep -E '^v[0-9]' | sort -V | tail -1 || true)"
fi

SOURCE=""
if [[ "$TAG" == v* ]]; then
  VERSION="${TAG#v}"
  SOURCE="tag $TAG"
elif [[ "$DRY_RUN" == "true" ]]; then
  CSPROJ_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CORE_PROJECT" | head -1)"
  [ -n "$CSPROJ_VERSION" ] || fail "dry run on a non-tag ref, and $CORE_PROJECT carries no <Version> to fall back on"
  VERSION="${CSPROJ_VERSION}-dryrun.${GITHUB_RUN_NUMBER:-0}"
  SOURCE="csproj fallback (dry run only, never pushed)"
else
  fail "A public release is cut from a v* tag, and '${GITHUB_REF_NAME:-HEAD}' is not one. Dispatch this workflow with the release tag (e.g. v1.0.7) as ref, or run with dry_run to inspect the packages."
fi

# --- 2. Shape ----------------------------------------------------------------------------------
SEMVER='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$'
[[ "$VERSION" =~ $SEMVER ]] || fail "'$VERSION' (from $SOURCE) is not a SemVer version like 1.0.7 or 1.1.0-preview.1"

# --- 3. Increment guard against nuget.org (tag-derived versions only) --------------------------
if [[ "$SOURCE" == tag* ]]; then
  for id in $PACKAGE_IDS; do
    url="${NUGET_FLAT_BASE}/${id}/index.json"
    if [[ "$url" == /* || "$url" == ./* ]]; then
      body="$(cat "$url" 2>/dev/null || echo '{"versions":[]}')"
    else
      body="$(curl -sS --fail-with-body "$url" 2>/dev/null || echo '{"versions":[]}')"   # 404 = never published
    fi
    published="$(printf '%s' "$body" | tr -d ' \n\r\t' | sed -n 's/.*"versions":\[\(.*\)\].*/\1/p' | tr ',' '\n' | tr -d '"' | sed '/^$/d')"
    if printf '%s\n' "$published" | grep -qx "$VERSION"; then
      fail "$id $VERSION is ALREADY on nuget.org. A published version is immutable - bump the tag (latest: $(printf '%s\n' "$published" | sort -V | tail -1))."
    fi
    latest="$(printf '%s\n' "$published" | sort -V | tail -1)"
    if [ -n "$latest" ]; then
      top="$(printf '%s\n%s\n' "$latest" "$VERSION" | sort -V | tail -1)"
      [ "$top" = "$VERSION" ] || fail "$id $VERSION is not greater than the latest published version $latest. A release increments - tag past $latest."
    fi
    echo "$id: latest on nuget.org = ${latest:-<none>}; $VERSION is new and greater."
  done
fi

echo "version=$VERSION" >> "$OUT"
echo "source=$SOURCE" >> "$OUT"
echo "Publishing version $VERSION (from $SOURCE)"
