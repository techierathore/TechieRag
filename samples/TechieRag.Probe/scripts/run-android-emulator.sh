#!/usr/bin/env bash
# Installs the probe's Debug APK on a running emulator, presses its button through the `autorun`
# intent extra (the same code path as the button), and waits for the result line in logcat
# (REQ-FN-057). Exit 0 only when the line starts with "OK". Used by .github/workflows/probe.yml and by
# the UsageGuide runbook.
#
# The first press downloads the embedding model, so the emulator must be online first. "Booted" is
# not "online": on the API 34 image the mobile-data network came up about 25 s after boot, and a press
# before that failed with "HttpRequestException: Connection failure" (UnknownHostException
# huggingface.co; CI run 43448574999, reproduced 13 Nov 2026). So the script turns data and Wi-Fi on
# and waits until the emulator resolves the model host, up to NET_TIMEOUT seconds (default 300).
#   run-android-emulator.sh <apk> <out-dir> [timeout-seconds]
set -u
APK="$1"; OUT="$2"; TIMEOUT="${3:-1200}"
PKG="com.techierathore.techierag.probe"
mkdir -p "$OUT"
NET_TIMEOUT="${NET_TIMEOUT:-300}"
MODEL_HOST="${MODEL_HOST:-huggingface.co}"
adb wait-for-device
until [[ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; do sleep 2; done
adb shell svc data enable >/dev/null 2>&1 || true
adb shell svc wifi enable >/dev/null 2>&1 || true
net_deadline=$(( $(date +%s) + NET_TIMEOUT ))
until adb shell ping -c 1 -W 2 "$MODEL_HOST" 2>/dev/null | grep -q "^PING $MODEL_HOST ("; do
  if [[ $(date +%s) -ge $net_deadline ]]; then
    adb shell ip route > "$OUT/android-network.txt" 2>&1 || true
    echo "TECHIERAG_PROBE_RESULT: NOT RUN - the emulator could not resolve $MODEL_HOST within ${NET_TIMEOUT}s, so the model download could not start (no network in the emulator, not an app failure)" | tee "$OUT/android-result.txt"
    exit 1
  fi
  sleep 5
done
echo "network: the emulator resolves $MODEL_HOST"
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
