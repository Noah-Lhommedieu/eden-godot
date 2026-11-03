#!/usr/bin/env bash
set -e

# ============================================================
#  Script d'arrêt de la stack Eden Eternal Docker
# ============================================================

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

echo "Arrêt de tous les conteneurs Eden..."
docker compose down

echo "Tous les services sont arrêtés."
