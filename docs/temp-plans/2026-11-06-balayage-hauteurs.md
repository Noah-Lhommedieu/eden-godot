# 2026-11-06 — Balayage statique hauteurs d'entités (sous/au-dessus des maps)

## Chaîne de production du z wire (correction du postulat)

- **NodeMap `.smp` n'a aucun z** — grille 2 bits de murs invisibles
  (`NodeMapFile.cs`, `NodeMapGrid.cs`). Les `.fsmp`/`.pgp` ne sont pas
  utilisés par le serveur.
- **`reborn_monsters` col7 = facing, pas z** — `SceneParser.cs:647-649` :
  z résolu post-parse. `[npc]` n'a pas de colonne z non plus (`:668-672`).
- **z wire de tout mob/NPC = `Scene.FloorZ(x,y)`** (`SceneParser.cs:345-359`)
  = z du point `[position]`/`[event_area]` le plus proche en (x,y), appliqué
  fin de parse (`:711-713`) + statues (`ZoneEntryFlow.Combat.Death.cs:493`),
  migration (`ZoneEntryFlow.Migration.cs:373`), pets (`ZoneLiveWorld.cs:900`).
- **z joueur jamais envoyé** (3e champ = facing).
- Client : `EntityRegistry.PhysicsGround` → `MapLoader.SampleEntityGround`
  (:240-251) → `WalkableGround.PeekNearestY` (:146-177) — scans non bornés,
  candidat `|y−refY|` minimal parmi terrain + faces OBJ, repli PeekLowest.

**Critères de suspicion réels** :
1. Étages multiples : spread des z de référence > ~15-24 u → un mob peut
   hériter le z d'un autre étage (FloorZ = plus proche en 2D, aveugle aux
   étages).
2. Spawn éloigné (>~50 u) de tout point porteur de z → z hérité sans rapport.
3. z < 0 → refY négatif, résolution fragile.
4. Symptôme pré-fix : `dy max` ≈ z de la map, compteur « sous sol ».

## Table des suspicions (scènes FR `/tmp/edenfr_scene_links`, dataset live)

| Node | z de référence | Étages | n lignes reborn | Verdict |
|---|---|---|---|---|
| **S178** | aile N 70.6-76.1, aile S 34.1-39.7 | 2 zones | ~87 | **P0 multi-étage** |
| **S050** | 177.9-180.1 (2 pts seuls) | 1 | 75 | **P0** — 1 mob à (317.8,171.7) à ~490 u de tout point z |
| **S219** | 118.3-121.1 + 144-149.5 | 2 niveaux | 45 | **P0 multi-étage** |
| **S227** | 59.9, 60.3, 67.8, 72, 76 (5 ailes) | ≥3 | 159 | **P0 multi-étage** (159 mobs, z sparse) |
| **S784** | 2.4 → 16.0 sur ~16 passerelles | multi | 66 | **P0 multi-étage** |
| **S179** | −0.3 → 55 (pts 0,11,16,30,40,44,52,55) | multi | 6 vus | **P0 multi-étage** |
| **S281/282/283** (tour) | RDC ~61-64 / ~72.7-73.2 / ~77.6-80.4 | 3 étages | ~20 chacun | **P0 multi-étage** |
| **S284** | 84.0 / 91.2 / 97.1 | multi | 0 | P1 |
| **S786** | **tous négatifs −1.4 → −9.2** | sous zéro | 2 | **P1 z<0** |
| **S177** | 6.7-6.9 + outlier ea4 z=−28.3 à (202.7,74) | 1+outlier | 63 | **P1 outlier résiduel** |
| **S773** | −0.64, 17-19, 24.8, 42.4-42.75 | multi (îles) | 36 | P1 résiduel |
| **S766** | 120.5-135 | 1 | 27 | P2 |
| **S778** | 70.4-72.7 | 1 | 3 | P2 |
| **S129** | 52.3-56 | 1 | 2 | P2 |
| **S131** | 55.1-57.1 | 1 | 1 | P2 |
| **S046** | 51.2-54.2 | 1 | 4 | P2 |
| **S047** | 22.4-24.6 | 1 | 5 | P2 |
| **S222** | 203.4-204.6 | 1 | 54 | P2 |
| **S206** | 147.9-148.6 | 1 | 93 | P2 |
| **S125** | 108.3-108.9 | 1 | 72 | P2 |
| **S136** | 93.5 | 1 | 38 | P2 |
| **S124** | 68.0-70.2 | 1 | 54 | P2 |
| **S120** | 67.5 / 72.1 (revive +5 u) | 2 léger | 47 | P2 |
| **S103** | 41.2-41.5 | 1 | 25 | P2 |
| **S116 / S716** | 17.4-18 | 1 | 64 | P2 |
| **S607/634/703** | 41.2-41.4 (scène quasi identique) | 1 | 25 | P2 |
| **S189** | 2.4-11 | dispersé | 10 | P2 |
| **S770** | 10.7-17 | 1-2 | 9 | P2 |
| **S299** | 81 (1 pt) | 1 | ? | P2 |
| **S280** | 63.0 (tour, entrée) | 1 | ~20 | P1 jamais balayé |
| **S002** | −30,−16,−15, ~10, 25, 50 | 3 niveaux | 33 | **OK vérifié** |
| **S191** | 559, 651.7, 697.9-714.2 | 3+ niveaux | 25 | **OK vérifié** |
| **S110** | 31.5-32.5 | 1 | 23 | **OK vérifié** |
| **S764** | 0.03-9 | plat | 66 | **OK vérifié** |
| **S045** | 58.8-87.9 | 2 zones | 11 | **OK vérifié** |

Non analysable : **S228** (.smp existe, pas de scène FR — cité dans
`WalkableGround.cs:44` « spawn S228 sous la salle »), **S229-231, S744-745**
(pas de scène FR). **S198** (1 pt z=22.4, 0 mob — plateforme +195 u déjà
traitée pour la marche).

## Maps pour balayage visuel futur

- **NodeMap .smp présents (~145)** : S001-S016, S018-S044, S051-S057,
  S095/097/099, S100-S127, S135, S198-199, S201-S213, S215-S223, S225-S231,
  S280-S284, S299, S301-S308, S401-S406, S501-502, S619-S623, S744-745,
  S994/995/999.
- **Déjà balayés donjons** (~140 nodes) : 35-50, 100-137, 176-197, 201-227,
  284, 603-650, 701-721, 764-791, 801-805 (+ ~40 nodes type-5 sans scène).
- **Jamais visités — priorité** : S280-S285 (tour), S198-199, S212, S216,
  S224, S299, S301-S308, S401-S406, S501-502, S619-623, S994-999, overworld
  S001-S034, S036-038, S042, S044, S051-S057, S093-099 (heightfield →
  auto-correction probable ; risque résiduel = structures/ponts type S002).

## Recommandations

1. **Ordre de capture live** : S281→S283 (tour jamais vue, 3 étages) →
   S227 → S178 → S219 → S784 → S179 → S050 (mob isolé y≈172) →
   S284/S280 → S786 (z<0) → S177 (outlier −28) → spot-check P2
   (S766, S222, S206).
2. Signal à exploiter : `mob_patrol_mode` a un 3e champ par waypoint ignoré
   (`SceneParser.cs:603-621`) — valeurs 0-1 sur donjons, 5-13 sur S002 ;
   sémantique non établie (flag/zone ?, PAS une hauteur fiable).
3. Amélioration serveur possible : FloorZ par plus-proche voisin échoue sur
   maps à étages disjoints en (x,y) ; découpage par clusters spatiaux
   réduirait les outliers type S050/S177.
