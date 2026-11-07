# 2026-11-06 — B1 + B8 : validation live finale

## B1 — Élites d'invasion → cristaux (S005) : PROUVÉ live complet

La mécanique existait déjà ; manquait la preuve live. Boot zone frais
(PID relancé avec env complet — `EDEN_DB_CONN` requis, sinon fallback
legacy `edenfr_db` → perso stub lv1), population node 5 :

- 00:29:10 : born events 1/5/9 → `Event_RebornMonster` mode 5 spawn
  groupes 117/122/127 → boss 60047/60048/60049 `EventSpawned=true`.
- Marches uniques (MobWander, pas de loop) :
  - 60048 route 313 → fin (75,184) en 36 s = position exacte cristal 55070
  - 60047 route 308 → fin (387,100) en 91 s = cristal 55068
  - 60049 route 318 → fin (376,350) en 96 s = cristal 55069
- Ancre + siège (`HomeX/Y` = endpoint, `FindSiegeTarget`,
  `NextSiegeAt`) → cristaux bleus détruits par les handles boss →
  events 7/3/11 → spawn cristaux rouges (annonces WorldBoss
  60021/60019/60020).
- +420 s : events 2/6/10 (fermeture générateurs).
- +1200 s : rouges auto-détruits → events 8/4/12 → retour état bleu.

Boucle [event] 1-12 de s005.ini entièrement observée en live.
Subtilité consignée : `ScheduleRespawn` garde `HomeX`=ancre → un boss
tué au cristal y respawne (siège plausible, natif non tranché) — pas
de problème live observé, pas de fix.

## B8 — Drop/ramassage trésor : PROUVÉ live complet

- `//give 33631 5` sac plein → trésor 1900000003 au sol (329,
  entité kind 0, coffre doré + label FR « Arc de Patrouille
  Sylvestre ×5 ») — `godot-shots/b8-treasure-chest-ground.png`.
- Clic gauche sur le coffre → approche à portée 4 u → NCID 35.
- Sac plein → serveur renvoie `316` texte 0x4f9 → client affiche
  « Pas assez de place dans le sac à dos. » — trésor CONSERVÉ.
- `//clear_bag 0` → re-clic → `274 ×5` (nouveaux slots 0-4) +
  `386 PickupOK` + `330 despawn` → chat « Obtenu : Arc de
  Patrouille Sylvestre ×5. » — `godot-shots/b8-treasure-pickup-ok.png`.
- Merge stack (32632 → loc12) → `291` au bon slot (pas de clone).
- Serveur : `treasure 1900000003 ramassé par char=200000020`.

## Outil

`ukey.py` : + mode `wheel x y n` (curseur en x,y puis n crans de
molette — zoom caméra dans les tests).

## État global

Phases A→G + bugs B1-B13 : tous VALIDÉS live. Restes consignés :
306 NCID NON PROUVE hors domaine client↔zone, rendu 3D bâtiments
VDG limité, `material is null` ×4 au boot (cosmétique, site non
localisé), break-au-mouvement non implémenté (règle maison).
