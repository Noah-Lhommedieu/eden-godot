# Plan — freeze combat/migration : bit fantôme sel=1 du NCID 302

Date : 2026-11-04. Statut : fait (build + 1066 tests verts, zone relancée).

## Symptômes live
- Perso + caméra figés (jeu qui tourne) à l'entrée/sortie de combat,
  post-portail ; « unable to use skills while moving » au cast.
- Icônes fantômes dans le panneau équipement (épée en slot tissu,
  pet en slot armure) impossibles à déséquiper.

## Preuves natives
- `CNC_CZ_ClientFlagModify::Serialize` @0x81e9940 : `[u32][u32 mot][u8]`.
- `CStateController::NodifyFlagChange` @0x84e3c00 : mot = `entity+0x3a`
  COMPLET, émis BroadCastToCanSee + NotifyClient.
- Captures : profils joueur = 0x01000000 / 0x01002000 / 0x01040000 /
  0x01042000 ; mob = 0x00040000 / 0x00140000 / 0. Jamais sel=1.
- `CheckSwitchItem` @0x8127300 : 277 renvoyé aussi en échec, écho
  des valeurs brutes (notre comportement = conforme).
- Client `game_.exe` @~0x75c6xx : « Unable to use skills while moving »
  (texte 2322) = gate LOCAL (mouvement réel + champ C_Spell row
  +0xCC non vide) — aucun paquet n'est émis, pas un bug serveur.

## Fixes
- [x] Stop-attaque (Mob.cs) : sel=1 → mot courant (sel=4|0) — freeze.
- [x] FinishMobCombatAsync : op=1 → op=0 (clear complet mob).
- [x] Migration : sel=1 → sel=0 (base joueur).
- [x] Resync 311 du panneau équipement sur switch refusé/divergent
      (purge les icônes optimistes côté client).
- [x] WIRE-EVIDENCE : modèle mot-d'état documenté.

## 2026-11-05 — désync sac0/sac1 : byte d'index de conteneur (FIX RACINE)

**Symptôme** : « j'équipe une arme, elle se place dans le slot cloth
armor hand » puis « clic droit profil → le slot se remplace par un
enchant ». DB toujours saine — désync vue client.

**Preuve native** (`CNC_CZ_ClientEnterNodeInventory`, ctor
@0x08234520 + Serialize @0x082333f0, zone/decompiled.c:402126+) :
l'en-tête de section bag est `[u16 capacity][u8 containerIdx]
[s16 bmpLen][bitmap]` — le 2ᵉ champ est la **clé de la map
CInventory (char+0x4e0)**, poussée par le ctor depuis
`*(short*)(node+0x10)` dans le vecteur `this+0x14`. Pas un flag.

**Bug** : on émettait `(byte)0` pour toutes les sections → le client
route les N sections vers le même conteneur 0 → le sac1 (plein)
écrasait la vue du sac0. Clic sur « l'arme » affichée (= Steel Sword
bag1/22) → serveur résout bag0/22 = gants 32306 → équipe loc4 =
« cloth armor hand ». Cohérent avec : ItemUse slot20 (bag1/20 =
Book utilisable), drops slots 19/21 « vides en DB », « inventaire
toujours plein » (bag1 = 30/30).

**Fix** : `w.Write((byte)bagId)` — index réel du conteneur.
1073 tests verts, zone redéployée.
