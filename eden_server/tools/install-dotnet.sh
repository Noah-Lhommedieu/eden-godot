#!/usr/bin/env bash
# install-dotnet.sh — installe le SDK .NET 8 en userland (script officiel Microsoft).
# Pourquoi ? Les projets eden_server sont au format SDK-style (<Project Sdk="...">),
# incompatibles avec MSBuild classique / Mono. Il faut le SDK .NET + `dotnet build`.
# Aucun sudo requis. Installation dans ~/.dotnet + ajout au PATH via ~/.bashrc.
set -euo pipefail

CHANNEL="8.0"
INSTALL_DIR="$HOME/.dotnet"

if [ -x "$INSTALL_DIR/dotnet" ]; then
  echo "[=] SDK déjà présent : $("$INSTALL_DIR/dotnet" --version 2>/dev/null || echo '?')"
else
  echo "[+] Téléchargement du script d'installation Microsoft..."
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  echo "[+] Installation du SDK .NET $CHANNEL dans $INSTALL_DIR ..."
  /tmp/dotnet-install.sh --channel "$CHANNEL" --install-dir "$INSTALL_DIR"
fi

# Ajoute au PATH de façon idempotente
if ! grep -q 'DOTNET_ROOT' "$HOME/.bashrc" 2>/dev/null; then
  {
    echo ""
    echo "# >>> .NET 8 SDK (eden_server) >>>"
    echo 'export DOTNET_ROOT="$HOME/.dotnet"'
    echo 'export PATH="$HOME/.dotnet:$PATH"'
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo "# <<< .NET 8 SDK (eden_server) <<<"
  } >> "$HOME/.bashrc"
  echo "[+] ~/.bashrc mis à jour (PATH + DOTNET_ROOT)."
else
  echo "[=] ~/.bashrc contient déjà la config .NET."
fi

echo ""
echo "[✓] Terminé. Ouvre un NOUVEAU terminal, puis vérifie :"
echo "      dotnet --version"
echo "      cd server && dotnet build Eden.sln"
