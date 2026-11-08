#!/usr/bin/env bash
# REQ-RAG-152 / BRD-210: stop and remove the live-test PostgreSQL started by start-postgres.sh, and drop
# the Postgres section of the settings file it wrote, so the live PostgreSQL tests skip again instead of
# pointing at a dead server. Other sections (image keys) stay; an emptied file is removed.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
settings="$(dirname "$here")/livetests.settings.json"

LIVETESTS_PG_PASSWORD=unused docker compose -f "$here/docker-compose.yml" down --remove-orphans

if [ -f "$settings" ] && command -v python3 >/dev/null 2>&1; then
  SETTINGS_FILE="$settings" python3 - <<'PY'
import json, os
path = os.environ["SETTINGS_FILE"]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)
data.pop("Postgres", None)
if data:
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2)
        handle.write("\n")
else:
    os.remove(path)
PY
elif [ -f "$settings" ]; then
  rm "$settings"
fi

echo "live-tests postgres: stopped; Postgres removed from $settings"
