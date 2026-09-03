#!/usr/bin/env bash
# [REQ-FN-003] Replays .github/workflows/scripts/determine-version.sh - the exact script the
# `Determine version` step of publish-nuget.yml runs - against a local fixture of nuget.org's
# flat-container index, so the version rules are asserted without a GitHub runner:
#
#   1. dispatch on tag v1.0.7 with 1.0.0 published      -> version=1.0.7
#   2. tag push v1.0.0 (already on nuget.org)          -> FAIL, "ALREADY on nuget.org"
#   3. tag push v0.9.9 (not greater than latest 1.0.0) -> FAIL, "not greater"
#   4. dispatch on a branch, real run                  -> FAIL, "not one" (dispatch the release tag)
#   5. dispatch on a branch, dry run                   -> version=<csproj>-dryrun.N, no nuget.org check
#   6. tag push v2.0.0-preview.1 (prerelease semver)   -> version=2.0.0-preview.1
#   7. tag push v1.0.7 when the package was never published (404 -> empty list) -> version=1.0.7
#
# Then one LIVE case against the real nuget.org index, so the fixture cannot drift from reality:
#   8. tag push v<latest+1 patch>                      -> accepted (proves the curl + parse path)
#
# Usage: bash tests/verify/publish-nuget-version.sh      (exit 0 = all cases held)
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SCRIPT="$ROOT/.github/workflows/scripts/determine-version.sh"
FIX="$(mktemp -d)"
trap 'rm -rf "$FIX"' EXIT
mkdir -p "$FIX/techierag" "$FIX/techierag.embedded" "$FIX/never/techierag" "$FIX/never/techierag.embedded"
echo '{"versions":["1.0.0"]}' > "$FIX/techierag/index.json"
echo '{"versions":["1.0.0"]}' > "$FIX/techierag.embedded/index.json"
# "never published": no index.json at all -> the script's `cat` fallback yields an empty list

pass=0; fail=0
run() { # name, expect(ok|fail), expected-substring, env...
  local name="$1" expect="$2" want="$3"; shift 3
  local out rc
  out="$(cd "$ROOT" && env -i PATH="$PATH" HOME="$HOME" CORE_PROJECT=src/TechieRag/TechieRag.csproj GITHUB_OUTPUT=/dev/stdout GITHUB_RUN_NUMBER=42 "$@" bash "$SCRIPT" 2>&1)"; rc=$?
  local rc_ok=1
  if [ "$expect" = ok ] && [ $rc -eq 0 ]; then rc_ok=0; fi
  if [ "$expect" = fail ] && [ $rc -ne 0 ]; then rc_ok=0; fi
  if [ $rc_ok -eq 0 ] && grep -q -- "$want" <<<"$out"; then
    echo "PASS  $name"; pass=$((pass+1))
  else
    echo "FAIL  $name (rc=$rc, wanted '$want')"; echo "$out" | sed 's/^/      /'; fail=$((fail+1))
  fi
}

run "1 tag v1.0.7 increments past 1.0.0"      ok   "version=1.0.7"            GITHUB_REF=refs/tags/v1.0.7 GITHUB_REF_NAME=v1.0.7 NUGET_FLAT_BASE="$FIX"
run "2 tag v1.0.0 already published"          fail "ALREADY on nuget.org"     GITHUB_REF=refs/tags/v1.0.0 GITHUB_REF_NAME=v1.0.0 NUGET_FLAT_BASE="$FIX"
run "3 tag v0.9.9 not greater than latest"    fail "not greater"              GITHUB_REF=refs/tags/v0.9.9 GITHUB_REF_NAME=v0.9.9 NUGET_FLAT_BASE="$FIX"
run "4 real dispatch on a branch"             fail "is not one"               GITHUB_REF=refs/heads/main GITHUB_REF_NAME=main DRY_RUN=false NUGET_FLAT_BASE="$FIX"
run "5 dry-run dispatch on a branch"          ok   "version=1.0.0-dryrun.42"  GITHUB_REF=refs/heads/main GITHUB_REF_NAME=main DRY_RUN=true NUGET_FLAT_BASE="$FIX"
run "6 prerelease tag accepted"               ok   "version=2.0.0-preview.1"  GITHUB_REF=refs/tags/v2.0.0-preview.1 GITHUB_REF_NAME=v2.0.0-preview.1 NUGET_FLAT_BASE="$FIX"
run "7 never-published package (404 path)"    ok   "version=1.0.7"            GITHUB_REF=refs/tags/v1.0.7 GITHUB_REF_NAME=v1.0.7 NUGET_FLAT_BASE="$FIX/never"

# 8. LIVE: read the real latest, tag one patch above it, expect acceptance.
live="$(curl -sS https://api.nuget.org/v3-flatcontainer/techierag/index.json 2>/dev/null | python3 -c 'import sys,json; v=json.load(sys.stdin)["versions"]; print(sorted(v, key=lambda s: [int(x) if x.isdigit() else x for x in s.replace("-",".").split(".")])[-1])' 2>/dev/null || true)"
if [ -n "$live" ]; then
  next="$(python3 -c "import sys; p='${live}'.split('-')[0].split('.'); p[-1]=str(int(p[-1])+1); print('.'.join(p))")"
  run "8 LIVE nuget.org: v$next past $live"  ok "version=$next" GITHUB_REF=refs/tags/v$next GITHUB_REF_NAME=v$next
  run "9 LIVE nuget.org: v$live is a duplicate" fail "ALREADY on nuget.org" GITHUB_REF=refs/tags/v$live GITHUB_REF_NAME=v$live
else
  echo "SKIP  8/9 live cases: nuget.org unreachable"
fi

echo "== $pass passed, $fail failed"
[ $fail -eq 0 ]
