#!/usr/bin/env bash
# Selects the Xcode that the installed .NET for iOS or Mac Catalyst SDK asks for (REQ-FN-057).
#
# .NET for iOS 26.5 refuses to build under any other Xcode major.minor (error E0191, "requires
# Xcode 26.5"), so "select the newest Xcode" is wrong whenever the runner is ahead or behind: the
# macos-15 image tops out at 26.3 (CI run 36148218069 failed both Apple heads that way) and the
# owner's Mac runs 27.0. This script reads the wanted version from the installed SDK pack, selects
# /Applications/Xcode_<wanted>.app when it exists, and otherwise keeps the current Xcode and hands
# back `-p:ValidateXcodeVersion=false` so the build still runs. Either way it writes the choice as a
# run-page annotation, which is readable without signing in to GitHub.
#
#   select-xcode.sh ios|maccatalyst
#
# Output: one line `PROBE_BUILD_FLAGS=<flags>`; under GitHub Actions the same goes to $GITHUB_ENV,
# and the build step appends $PROBE_BUILD_FLAGS to its dotnet build. Run after the workload install.
set -u

case "${1:-}" in
  ios) PACK="Microsoft.iOS.Sdk" ;;
  maccatalyst) PACK="Microsoft.MacCatalyst.Sdk" ;;
  *) echo "usage: select-xcode.sh ios|maccatalyst" >&2; exit 2 ;;
esac

major_minor() { awk -F. '{ printf "%s.%s", $1, ($2 == "" ? 0 : $2) }' <<< "$1"; }

DOTNET_ROOT_DIR="$(dirname "$(dirname "$(dotnet --info | sed -n 's/^ *Base Path: *//p' | head -1)")")"
WANT="$(ls -d "$DOTNET_ROOT_DIR"/packs/"$PACK".net10.0_* 2>/dev/null | sed 's/.*_//' | sort -V | tail -1)"
if [[ -z "$WANT" ]]; then
  echo "::error::No $PACK pack under $DOTNET_ROOT_DIR/packs; install the workload before selecting Xcode"
  exit 1
fi

CURRENT="$(xcodebuild -version 2>/dev/null | sed -n 's/^Xcode //p')"
FLAGS=""
if [[ "$(major_minor "${CURRENT:-0}")" != "$WANT" ]]; then
  CHOSEN=""
  for candidate in "/Applications/Xcode_$WANT.app" "/Applications/Xcode_$WANT.0.app" "/Applications/Xcode-$WANT.app"; do
    if [[ -d "$candidate" ]]; then CHOSEN="$candidate"; break; fi
  done
  if [[ -n "$CHOSEN" ]]; then
    sudo xcode-select -s "$CHOSEN"
  else
    echo "::warning::$PACK $WANT asks for Xcode $WANT; this machine has Xcode ${CURRENT:-none} and no /Applications/Xcode_$WANT.app, so the build runs with -p:ValidateXcodeVersion=false"
    FLAGS="-p:ValidateXcodeVersion=false"
  fi
fi

echo "::notice::$PACK $WANT wants Xcode $WANT; building with $(xcodebuild -version 2>/dev/null | tr '\n' ' ')at $(xcode-select -p)"
if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "PROBE_BUILD_FLAGS=$FLAGS" >> "$GITHUB_ENV"
fi
echo "PROBE_BUILD_FLAGS=$FLAGS"
