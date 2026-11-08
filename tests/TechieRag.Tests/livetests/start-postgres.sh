#!/usr/bin/env bash
# REQ-RAG-152 / BRD-210: start the live-test PostgreSQL 18 + pgvector in Docker with its own password,
# and write its connection string into tests/TechieRag.Tests/livetests.settings.json (git-ignored) for the
# live tests to read.
#
#   bash tests/TechieRag.Tests/livetests/start-postgres.sh          # port 5551
#   LIVETESTS_PG_PORT=5560 bash tests/TechieRag.Tests/livetests/start-postgres.sh
#
# Every start recreates the container on an empty database with a new random password and replaces the
# settings file's Postgres section; any other section (image keys) is kept. The connection string never
# goes into an environment variable the tests read: the settings file is the only channel (owner's rule,
# 2026-11-07). Stop with stop-postgres.sh.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
settings="$(dirname "$here")/livetests.settings.json"
compose_file="$here/docker-compose.yml"
port="${LIVETESTS_PG_PORT:-5551}"

password="$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')"
export LIVETESTS_PG_PASSWORD="$password"
export LIVETESTS_PG_PORT="$port"

docker compose -f "$compose_file" down --remove-orphans >/dev/null 2>&1 || true
docker compose -f "$compose_file" up -d --wait livetests-postgres

# The image ships pgvector; the stores create the extension themselves, this only proves it loads.
docker exec techierag-livetests-postgres \
  psql -v ON_ERROR_STOP=1 -U techierag -d techierag_live -c "CREATE EXTENSION IF NOT EXISTS vector;" >/dev/null

umask 077
pg_value="Host=localhost;Port=$port;Database=techierag_live;Username=techierag;Password=$password"
if command -v python3 >/dev/null 2>&1; then
  SETTINGS_FILE="$settings" PG_VALUE="$pg_value" python3 - <<'PY'
import json, os
path, value = os.environ["SETTINGS_FILE"], os.environ["PG_VALUE"]
try:
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
except (FileNotFoundError, ValueError):
    data = {}
data["Postgres"] = {"ConnectionString": value}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(data, handle, indent=2)
    handle.write("\n")
PY
else
  printf '{\n  "Postgres": {\n    "ConnectionString": "%s"\n  }\n}\n' "$pg_value" >"$settings"
fi

echo "live-tests postgres: up on localhost:$port (PostgreSQL 18 + pgvector); settings written to $settings"
