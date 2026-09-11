#!/usr/bin/env bash
# Run on the Linux homelab host (repo root or server/ directory).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SERVER_DIR="$ROOT/server"
DOWNLOAD_URL="${VALTRANS_DOWNLOAD_URL:-https://github.com/deffimism/Valtrans/releases/download/v0.4.0-beta/v0.4.0-beta.zip}"
SUPPORT_PORT="${VALTRANS_SUPPORT_PORT:-13020}"

cd "$SERVER_DIR"
export VALTRANS_DOWNLOAD_URL="$DOWNLOAD_URL"

if [[ -f .env ]]; then
  sudo env DOCKER_CONFIG="$PWD/.docker-client" VALTRANS_DOWNLOAD_URL="$DOWNLOAD_URL" \
    docker compose -f compose.yaml --env-file .env build --no-cache valtrans-support
  sudo env DOCKER_CONFIG="$PWD/.docker-client" VALTRANS_DOWNLOAD_URL="$DOWNLOAD_URL" \
    docker compose -f compose.yaml --env-file .env up -d --force-recreate --no-deps valtrans-support
else
  echo "Missing server/.env — copy .env.example first." >&2
  exit 1
fi

curl -fsS "http://127.0.0.1:${SUPPORT_PORT}/health"
echo "Support site sync complete."
