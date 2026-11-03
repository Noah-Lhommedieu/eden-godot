---
name: godot-visual-regression
description: Prouver visuellement un changement de rendu, d'UI, de map ou d'asset du client Eden (capture in-engine, comparaison aux ref-shots et à l'avant). À utiliser avant de déclarer VISUALLY_VALIDATED, ou pour « capture », « screenshot », « vérifier à l'écran ».
---

# Régression visuelle

1. Avant le changement : `tools/capture.sh <MAP> avant`.
2. Après : `tools/capture.sh <MAP> apres`. Les captures vont dans
   `artifacts/shots/`, avec le log du run à côté.
3. **Lire les deux images** (outil Read), puis les comparer entre elles et
   à la référence d'origine `ref-shots/` (spec par image :
   `eden_godot/docs/client-assets/ref-shots-catalog.md`).
4. Différence suspecte : refaire la capture. L'eau animée d'S001 apparaît
   ou non selon le timing (non déterministe, vérifié). Pour isoler une
   cause, faire un A/B en ne changeant qu'un seul fichier, avec au moins
   2 captures de chaque côté.
5. Comparaison chiffrée d'une zone (couleur moyenne) :
   `python3 -c "from PIL import Image; …crop((x0,y0,x1,y1))…"`. Utile pour
   trancher entre « eau ou sol gris ».
6. Le `--headless` ne vaut jamais preuve de rendu. Un seul client graphique
   à la fois (`flock /tmp/godot.lock`, déjà fait par les scripts).

Livrer les chemins des images et la conclusion. Ne jamais livrer « ça doit
marcher ».
