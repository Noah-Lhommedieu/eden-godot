#!/usr/bin/env bash
set -e

# ============================================================
#  Script de démarrage de la stack complète Eden Eternal (.NET 8 + PostgreSQL)
# ============================================================

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

echo "=========================================================="
echo "    DÉMARRAGE DU SERVEUR EDEN ETERNAL DOCKER (.NET 8)    "
echo "=========================================================="

if [ ! -f ".env" ]; then
    echo "[!] Fichier .env non trouvé, copie depuis .env.example..."
    cp .env.example .env
fi

echo "[*] Lancement de tous les conteneurs (PostgreSQL, Ticket, Gateway, Login, World, Zone, Mission)..."
docker compose up -d --build

echo ""
echo "[+] Statut des conteneurs :"
docker compose ps
echo ""
echo "[+] Le serveur Eden Eternal est opérationnel !"
