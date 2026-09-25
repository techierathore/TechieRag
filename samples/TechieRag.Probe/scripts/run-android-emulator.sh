#!/usr/bin/env bash
# Installs the probe's Debug APK on a running emulator, presses its button through the `autorun`
# intent extra (the same code path as the button), and waits for the result line in logcat
# (REQ-FN-057). Exit 0 only when the line starts with "OK". Used by .github/workflows/probe.yml and by
# the UsageGuide runbook.
#   run-android-emulator.sh <apk> <out-dir> [timeout-seconds]
set -u
APK="$1"; OUT="$2"; TIMEOUT="${3:-1200}"
PKG="com.techierathore.techierag.probe"
mkdir -p "$OUT"
adb wait-for-device
adb install -r "$APK" || { echo "install failed"; exit 1; }
adb logcat -c
adb shell am start -n "$PKG/.MainActivity" --ez autorun true
deadline=$(( $(date +%s) + TIMEOUT ))
line=""
while [[ $(date +%s) -lt $deadline ]]; do
  line="$(adb logcat -d | grep -o 'TECHIERAG_PROBE_RESULT:.*' | tail -1)"
  [[ -n "$line" ]] && break
  sleep 5
done
adb logcat -d > "$OUT/android-logcat.txt"
adb exec-out screencap -p > "$OUT/android-probe.png" 2>/dev/null || true
echo "${line:-NO RESULT within ${TIMEOUT}s}" | tee "$OUT/android-result.txt"
[[ "$line" == *"TECHIERAG_PROBE_RESULT: OK"* ]]
