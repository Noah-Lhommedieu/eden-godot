# V7-PROPS-SOURCE — d'où viennent les props des 15 maps « V7 »

Date : 2026-11-01. Réponse à la question ouverte de M4 (voir
`V7-TAIL-NOTES.md`, dont la conclusion §3 est **réfutée** ici).

## Réponse

Les props/herbe de ces maps sont dans le **même `.fsm`**, en chunks
`GRL `/`GRS `/`OBJ ` classiques. Ils n'existent pas dans les copies
`corpus/fr-full` / `corpus/original-fr` : **ces 15 fichiers sont
corrompus** (octets écrasés par un motif répété), pas une « variante
V7 ». Les originaux EDENFR sont intacts dans
`~/Téléchargements/EDENFR/extracted/map/model/<map>/<map>.fsm`
(= `qb_out/rest/…`, md5 identique ; aussi identique au
`portage/corpus/official` pour s002/s021/s030/s032/s054/s225/s307).

## Preuves

- Chaîne de chunks du client (`re/ghidra/rf_game/decompiled.c`
  `FUN_00b130b0` : balayage `tag u32 + size u32` depuis `0x58`, puis
  `LAY0` = `0x3059414c`, `GRL ` = `0x204c5247` lus avec records 0x2c) :
  si le tag n'est pas à l'offset attendu, le chunk est absent. La queue
  FR (`0xd72b8` o pour s002) ne commence pas par un tag → le fichier lu
  par le client réel ne peut pas être celui-là.
- s002 : fr-full md5 `ac3b34…` ≠ extracted `b64be4…` ; mêmes 7 454 808
  o. Octets différents de `0x2ec0bb` à `0x71c056` (3 108 457 o), soit
  dans LAY0 **et** toute la zone GRL/GRS/OBJ. Le « motif de 10 f32
  répété » de V7-TAIL-NOTES est ce remplissage.
- Extracted, 15/15 : LAY0 + GRL + GRS + OBJ, **0 octet de queue**.
  s002 : GRL 7 / GRS 21 597 / OBJ 2 064 (= oracle US). Autres :
  s003 8/13 677/6 191 · s004 9/25 141/2 296 · s005 8/7 039/3 037 ·
  s010 5/19 327/4 094 · s011 2/3 668/3 070 · s021 6/27 034/3 194 ·
  s030 2/6 656/2 744 · s032 3/51 765/2 635 · s051 8/34 597/4 276 ·
  s054 7/11 861/5 904 · s119 3/1 269/554 · s225 5/3 381/353 ·
  s307 2/23 263/2 064 · s501 5/2 243/1 462.
- Autres fichiers `s002` du corpus (`.csm` = ombres CSM ini, `.fsmp`
  = SMP2, `.pgp` = PathMap, `map.nif` = stub PE `MZ`, `treeobject.ini`
  = liste de noms) : aucun ne place d'objets.

## Conséquence terrain (M3)

La corruption démarre **dans LAY0** : le terrain s002 chargé en M3
depuis fr-full était lui aussi partiellement faux (tuiles + queue de
tuiles). Les « 49 tuiles / colonne x=448 manquante » de V7-TAIL-NOTES
sont des artefacts de ce fichier. Charger l'original corrige terrain
et props.

## Correctif

- `Eden.Core/Formats/FsmSource.cs` : `Candidates(roots, rel)` +
  `PickIntact` (premier fsm avec `TrailingBytes == 0` et OBJ/GRS).
- `MapLoaderFlax.Load` : fsm résolu via `NifObjectLoader.NifRoots`
  (extracted d'abord, fr-full en repli) — aucun parseur nouveau,
  `FsmFile.SceneObjects/GrassInstances` suffisent.
- Tests `Tests/V7PropsTests.cs` (3, skippés si extracted absent).
- Le corpus n'est pas modifié (règle « corpus lecture seule »). Pour
  réparer à la source : recopier les 15 fsm depuis `extracted/`.
