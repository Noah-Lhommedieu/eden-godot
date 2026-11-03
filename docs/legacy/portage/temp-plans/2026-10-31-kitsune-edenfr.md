# Plan — monture Kitsune de l'Aube (1290) EDENFR

Objectif : rendre la monture légendaire EDENFR visible dans l'archive,
giveable, invocable et utilisable.

## État des lieux

- `c_item.ini` officiel : 1290 présent (enchant 11354, icône I01488)
- `t_item.ini` FR : nom/desc présents ; `lang/en` : vérifier/ajouter
- `motion.ini` : DB11354=r532 → modèles `assets/models/ride/r532.glb`
  + anim convertis ✓
- Manque : `c_enchant.ini` 11354, `S_Item` 1290 (serveur),
  `c_collection.ini` carte 1290 (archive)

## Points d'édition

1. `corpus/official/data/db/c_enchant.ini` += 11354 (62 cols) :
   `2134|532010|1` @9 (Ride r532), `2051|90` @16 (+90 % vitesse),
   `2083|2|1` @23 (ATQ+2 %/DÉF+1 %), `2002|7` @30 (PV+7 %).
   → ramassé automatiquement par `EnchantService.ApplyClientOverlay`
   (CLIENT_DB=corpus/official, ids nouveaux ajoutés).
2. `corpus/official/data/db/S_ItemExtra.ini` (nouveau) : ligne 1290
   calquée sur S_Item 20051/c_item 1290 (35 cols, enchant col23=11354).
3. `GameDataService.LoadAll` : charger `S_ItemExtra.ini` via
   `ReforgedDataLocator.FindFile` — additif seul (oracle S_* intouché).
4. `corpus/official/data/db/c_collection.ini` += carte `1290` + étape
   EXTRAS dans `build_official.py` (rebuild durable).
5. `lang/en/t_item.ini` += 1290 si absent.

## Vérifs

- `dotnet build Eden.sln`, restart services, `//find_item`/give 1290,
  archive `--hud-open=archive:Mounts` + flameshot, invocation + screenshot.

## Résultat (correction d'identification)

La monture demandée n'était PAS le 1290 (太陽九尾金狐 = renard doré du
soleil, modèle r532 = créature volante rose). Le renard blanc légendaire
exclusif EDENFR = **48813 « Renard Blanc du blizzard aux neuf queues
(Légendaire) »** — enchant **60419** → rideKey **268010** → modèle
**r268** (blanc/crème, marquages roses), texture r26801.

- 48813 : placeholder US « 測試用編號 » écrasé par la def EDENFR via
  `S_ItemExtra.ini` (l'overlay remplace désormais les doublons d'id,
  pas seulement les ajouts — GameDataService).
- 60419 : ajouté à `c_enchant.ini` (2134|268010, +90 % vitesse,
  +7 % PV, +2 % ATQ/MAG — monture de combat légendaire).
- Carte archive 48813 ajoutée (Montures 38).
- 1290/11354 aussi implémentés (vrai enchant natif S_Enchant recopié
  en overlay avec rideKey corrigé) + cartes des 16 montures FR-only.
- 236 items EDENFR absents du serveur importés dans `S_ItemExtra.ini`.
- Provenu live : give → inventaire → utilisation → key=268010 →
  visuel r268 monté/descendu (flameshot).
