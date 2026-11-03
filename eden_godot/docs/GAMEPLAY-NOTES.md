# Notes gameplay — comportements du client originel (spec)

Source : spécification utilisateur + captures natives `../ref-shots/`.

## Contrôles caméra / déplacement

| Input | Comportement |
|---|---|
| **Clic gauche + drag** | Rotation caméra (orbite) seule |
| **Clic droit + drag** | Rotation caméra **+ le joueur tourne** selon l'orientation horizontale de la caméra (steering — le perso suit la direction caméra pendant le drag) |
| **Clic gauche sur le sol** | Marche vers le point cliqué + **indicateur visuel d'arrivée** (marqueur au sol) |
| **Clic droit sur NPC** | Talk / interaction (ouvre le dialogue) |
| **Clic droit sur mob** | Approche + **auto-attack** |

## À implémenter (ordre)

1. ✅ Caméra : orbite clic gauche ; clic droit = orbite + personnage suit la direction (`ModelYaw` ← yaw caméra pendant le drag, TurnSelf 123 au relâchement).
2. ✅ Clic sol gauche : target pos + marqueur `MoveMarker` (anneau `effect/texture/mark-ground01.png`) + désactivation à l'arrivée.
3. ✅ Picking entités sous le curseur : projection écran (`PickCore`, ±36 px) — sol / NPC (kind 8) / mob (kind 12) ; curseur contextuel `SystemCursors.png` (cellules HYPOTHESIS).
4. ✅ Auto-attack : clic droit mob → TargetUpdate 124 + approche + NCID 5 `ZoneMeleeAttack` par tick (~1.2 s, y compris hors portée comme le natif) ; stop `[0][2]` à l'annulation/despawn. Prouvé live : 444 lock + 302 stance + **272 coups reçus** (dmg réels).
5. UI in-game : HUD (HP/MP, barres), chat, minimap, inventaire, profil personnage — **lot HUD en cours (autre agent)**.

## État animations (25/10)

- Parseur GLB maison `GlbAnimFile` (pas de GenerateScene — clés .kf dégénérées + fichiers 88-180 Mo) + retarget nom→bone/nœud `AnimRetarget`/`AnimPathMap`.
- Joueur : `anims/char/animation/<L>.glb` → 16 anims (stand01-02, move01-03, attack01-04, magic01, battle01, hurt01, death01, jump01, sit01, collect01) retargetées sur le Skeleton3D du corps ; locomotion auto (stand01/move01) + one-shot attack à l'auto-attack.
- Mobs/NPC : `anims/monster/animation/<stem>.glb` → 12 anims, stand01/move01 auto via delta de position (interp réseau).
- Fait (lot gameplay-2) : routing 272 → kfId résolu via `data/db/motion.ini` (MotionTable) → anim exact (ex. 2021→attack17) + hurt01/death01 sur la cible ; 305 ClientDie → death01 + cadavre gracié au 269 ; WhoIs NCID 2 → 263 apparence remote → CharVisual ; herbe GRL/GRS en MultiMesh par cellule (cap 5000) ; eau .ifl animée (sampler2DArray, `water.gdshader`) ; fog plafonné FogMax via post-process `FogCeiling`.
- Reste : channels cibles manquantes sur certains stems (warn), timing hit précis (point d'impact de l'anim).

## Références serveur pour les paquets

- `eden_server/server/src/Eden.Core/Networking/Messages/Generated/ZoneMessages.g.cs`
- `eden_server/docs/generated/ncid_map.json` (CNC_CZ_Client* = S→C, CNE_CZ_ZoneServer* = C→S)
- Captures : `eden_server/captures/zsniff_*.bin`, `docs/netsmoke_trace.log`
