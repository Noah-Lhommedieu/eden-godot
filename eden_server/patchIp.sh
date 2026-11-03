#!/bin/bash

# IP cible à injecter (ex: 217.144.154.250)
TARGETIP="217.144.154.250"
TARGETLEN=${#TARGETIP}

# Liste des binaires à traiter
BINLIST=(
  "./server_us/WorldServer/WorldServer"
  "./server_us/ZoneServer/ZoneServer"
)

echo "------ Patch automatique d'IP dans binaires Eden Eternal ------"
echo "IP cible : $TARGETIP (longueur $TARGETLEN)"
echo

for BIN in "${BINLIST[@]}"; do
  if [ -f "$BIN" ]; then
    cp "$BIN" "$BIN.bak"
    echo "[+] Recherche des IP en ASCII dans le binaire $BIN ..."
    # Extraction brute de toutes les IPV4 ASCII
    IPSFOUND=$(strings "$BIN" | grep -Eo '([0-9]{1,3}\.){3}[0-9]{1,3}' | sort | uniq)
    for FOUNDIP in $IPSFOUND; do
      IPLEN=${#FOUNDIP}
      if [ "$IPLEN" -eq "$TARGETLEN" ] && [ "$FOUNDIP" != "$TARGETIP" ]; then
        echo "--> Remplacement $FOUNDIP ($IPLEN) par $TARGETIP"
        sed -i "s/$FOUNDIP/$TARGETIP/g" "$BIN"
      fi
    done
    echo "Backup : $BIN.bak | Fichier patché : $BIN"
    echo
  else
    echo "[-] Binaire $BIN introuvable, skip."
  fi
done

echo "------ Patch terminé ------"

