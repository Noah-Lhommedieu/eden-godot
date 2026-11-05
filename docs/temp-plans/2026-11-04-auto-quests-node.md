# 2026-11-04 — Auto-quêtes nodales (déblocage chaîne tutoriel S002)

## Symptôme

TestGirl (niv 1, node 2) ne peut démarrer aucune quête : le dialogue de
Cynthia (50003) revient `talks=0 quests=0 ctx=4003` (mission.log). La
quête 1009 « Votre première Quête I » est de type auto
(`S_Mission col6==10`, donneur `col11` vide, rendu `col17`=50003) :
aucun NPC ne l'offre jamais → toute la chaîne 1009→1010→… est morte
(1010 exige `If_FMS 1009 done`).

## Cause

`QuestEngine.OnMigrationDoneAsync` a retiré l'auto-acceptation (« à la
demande du live : le jeu prend des quêtes sans que je le veuille »).
Le retrait était global — il a aussi tué les quêtes tutorielles que
le natif accorde par node.

## Preuve native

- `lapis::CGlobalInfo::AutoAcceptByNode` @ `mission` 0x08081f20 :
  à l'entrée d'un node, itère le `CMLNodeSort` du node courant
  (missions triées par NPC mission+0x34), `FMSCheckApproval` puis
  `AddPreview`.
- `lapis::CMissionDBO::CheckAutoAcceptNode` @ 0x0818b090 : gate
  complémentaire par table `transport` (node débloqué).
- `MigrationDone` natif (Deserialize @0x813e1f0) : node lu en DB
  (`player_characters.node_id`), pas dans le paquet.
- S_Mission : les 66 missions col6==10 ont toutes un NPC de rendu
  (col17) et un niveau requis (col8) — quêtes « guide » nivelées.

## Implémentation

1. `QuestEngine` : carte `npc → nodes` construite au boot depuis
   `Data/scene/*.ini` (`[npc]` → NpcId × MapCode) — même résolution de
   chemin que la zone (`SCENE_DIR` ?? `realDataDir/../scene`).
2. `OnMigrationDoneAsync` : node réel du perso lu en DB
   (`player_characters.node_id`, parité native) au lieu de
   `GlobalNodes.FirstOrDefault()` (renvoyait 1 alors que le perso est
   en node 2). Étend `GetLevelGenderAsync` → `GetLevelGenderNodeAsync`.
3. Grant : pour chaque mission `col6==10 && col11==0` dont le NPC de
   rendu (col17) est placé dans le node courant, niveau (col8) +
   conditions (col13) + FMS vierge/reprenable → `AcceptMissionAsync`
   (existant : persist, FMS, seeds PMS, symboles NPC).
4. La boucle de purge au-dessus de niveau reste — cohérente
   (« re-accordées quand le niveau sera là » se réalise à l'entrée
   du node concerné).

## Hors scope / limites

- Gate `transport` natif (CheckAutoAcceptNode) non porté : la présence
  dans le node implique déjà l'accès.
- `If_Gender` natif = « nibble ∈ liste » ; le C# ne lit que a[0] —
  suffisant pour les données actuelles (0 1 9 8 inclut les deux sexes).
- Pas de grant mi-session au level-up (TC_PlayerLevel natif gère le
  FMS 3↔4, pas l'auto-grant — les quêtes nivelées arrivent à l'entrée
  du node suivant, comme le natif).

## Vérification

- [x] build serveur 0 erreur 0 warning
- [x] tests serveur verts (39/39 filtre Quest|Mission)
- [x] live : TestGirl node 2 → `accept 1009 status=2`, tracker HUD
      « Nv. 1 Votre première Quête I — Terminé », option quête chez
      Cynthia (`opts=1`, titre résolu) — capture `/tmp/g_quest1009.png`.
- [x] fix crédit NPCTalk : `finish 1009 REFUSÉ — objectifs incomplets`
      car le PMS 50000+off n'était crédité qu'à status==1 (les quêtes
      talk-only passent status=2 à l'acceptation) → crédit étendu à
      status 2 → `finish 1009 +7g +38xp`.
- [x] chaîne complète live : `finish 1010 +7g +38xp`,
      `accept 1011 req=3` → 3 kills réels (kill-loop=3,56001)
      compteur 391 1→2→3 → `1011 objectifs remplis` →
      `finish 1011 +14g +100xp` — captures g_quest1010b/1011b.png.
- [x] persistance : `migré (0 actives, N faites)` progresse 1→2→3
      faites ; FMS `final_mission_state_v2` bit posé ; trace vidée
      à la remise.
- [ ] suite de la chaîne 1012+ (même mécanique — objectif
      « gameplay niv 1→25 » en cours, hors commit de ce plan).
