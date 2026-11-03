# Leçons des agents (portage 4.7, Flax, eden-godot)

Statut : **verified** (faits relevés dans les dépôts et les sessions).

1. **« Fait » sans image = faux.** Flax a validé M3-M6 sur logs et compilation
   alors que le terrain n'était visible que par en dessous (winding).
   Exiger `tools/capture.sh` et **regarder** l'image.
2. **Mesurer avant d'expliquer.** Le « 4× Flax vs Godot » venait du CPU
   côté client : le GPU Godot ne prenait que 5,1 ms sur 17,4. Toujours
   séparer CPU et GPU (`--gpu-profile`) avant d'accuser le moteur.
3. **Une moyenne sur 30 s inclut le chargement.** Lire le régime établi
   (t ≥ 15 s). `tools/benchmark/summarize.py` le fait.
4. **Une hypothèse plausible n'est pas une cause.** Le « plafond VSync
   60 Hz » a été réfuté en 1 run. Le « FsmFile Flax cause l'absence d'eau »
   a été réfuté par un A/B, puisque la sortie du parseur était identique.
5. **Avant d'écrire du code, chercher si le dépôt frère a déjà résolu le
   problème.** La casse, le CP1252, le FSM V7 et `NativeOnly` existaient
   déjà dans le Core Flax : `diff` d'abord.
6. **Les liens physiques et la copie en place** : délier avant d'écrire.
7. **Pas de chiffre sans étiquette** : mesuré (commande), repris d'un doc,
   ou estimé. Les estimations de durée en « semaines » se sont révélées
   fausses d'un ordre de grandeur, dans les deux sens.
8. **Ne pas adapter un test au résultat sans raison écrite.** Les seuils
   recalés sur l'EDENFR citent le compte mesuré et l'ancien compte du
   merge (`IniTableTests`, `ZoneAchievementTests`).
9. **`/tmp/ride_key.txt` persiste entre les sessions.** Le debug monture
   applique `Yaw=90` (vue de profil) ; `--walk-test` suivant le yaw caméra,
   un fichier oublié fait marcher le perso vers le mur du spawn (~0 u sur
   toutes les maps — diagnostiqué « régression collision » à tort). Clé
   `-1` = caméra seule ; `rm /tmp/ride_key.txt` avant tout run mouvement.
