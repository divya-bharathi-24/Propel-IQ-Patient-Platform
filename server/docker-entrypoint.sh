#!/bin/bash
# server/docker-entrypoint.sh
#
# Startup sequence for the Propel API Gateway container:
#   1. Wait for PostgreSQL to accept TCP connections (belt-and-suspenders alongside
#      Docker Compose depends_on condition: service_healthy).
#   2. Start the application. EF Core migrations and the Specialty seed are applied
#      programmatically inside Program.cs via db.Database.MigrateAsync() — equivalent
#      to running "dotnet ef database update" but without requiring the SDK at runtime.
#
# Edge case handled: PostgreSQL container may report healthy before it is fully ready
# to accept connections from the backend network interface; the 2-second post-ready
# sleep absorbs that window.

set -e

POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
MAX_RETRIES=30
SLEEP_SECONDS=2

echo "[entrypoint] Waiting for PostgreSQL at ${POSTGRES_HOST}:${POSTGRES_PORT} (max ${MAX_RETRIES} attempts)..."

for i in $(seq 1 ${MAX_RETRIES}); do
    # Use bash built-in TCP test — no postgresql-client package required in runtime image
    if (echo > /dev/tcp/${POSTGRES_HOST}/${POSTGRES_PORT}) 2>/dev/null; then
        echo "[entrypoint] PostgreSQL TCP port reachable on attempt ${i}. Allowing 2s for connection acceptance..."
        sleep 2
        break
    fi

    if [ "${i}" -eq "${MAX_RETRIES}" ]; then
        echo "[entrypoint] ERROR: PostgreSQL not reachable after ${MAX_RETRIES} attempts. Exiting."
        exit 1
    fi

    echo "[entrypoint] Attempt ${i}/${MAX_RETRIES} — PostgreSQL not yet reachable. Retrying in ${SLEEP_SECONDS}s..."
    sleep "${SLEEP_SECONDS}"
done

echo "[entrypoint] Starting Propel API Gateway (EF Core migrations + seed applied on startup)..."
exec dotnet Propel.Api.Gateway.dll
