#!/usr/bin/env bash
# Remplacé (phase G) par tools/native-coverage : décodage de tous les fichiers natifs
# et résolution de chaque référence de table dans corpus/fr-full. Il n'y a plus de dérivés.
exec "$(dirname "$0")/native-coverage/native-coverage.sh" --all
