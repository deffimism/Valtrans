#!/usr/bin/env bash
# Linux homelab deploy (no git). SMB workflow:
#   1) Windows: scripts/Package-SupportSite.ps1
#   2) Copy releases/valtrans-support-v0.4.0-beta.tar.gz to ~/AppData/Valtrans/
#   3) tar -xzf valtrans-support-v0.4.0-beta.tar.gz && bash scripts/sync-support-site.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SERVER_DIR="$ROOT/server"
DOWNLOAD_URL="${VALTRANS_DOWNLOAD_URL:-https://github.com/deffimism/Valtrans/releases/download/v0.4.0-beta/v0.4.0-beta.zip}"
SUPPORT_PORT="${VALTRANS_SUPPORT_PORT:-13020}"
SUPPORT_HOST="${VALTRANS_SUPPORT_HOST:-192.168.0.19}"

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

curl -fsS --retry 12 --retry-connrefused --retry-delay 2 "http://${SUPPORT_HOST}:${SUPPORT_PORT}/health"
echo "Support site sync complete."
