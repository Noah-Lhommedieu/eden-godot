# 2026-10-24 — Bugs remontés au retest live client

Bugs constatés en jeu réel par l'utilisateur après le dernier déploiement.
Chaque point = preuve native + fix + validation headless si possible.

## Régressions (à traiter en premier — causées par mes changements)

- [ ] **Cast-move Reforged pire qu'avant** : l'animation de cast
  (timed skill) persiste APRÈS la fin du cast, même hors combat.
  Suspect : NOP de `FUN_0075ad10` (5 call sites) qui servait aussi
  de reset d'état à la fin du cast — patch trop large.
  Micro-stop toujours présent au lancement d'un timed cast en bougeant.
- [ ] **Strafe à dos de monture** : le perso ne tourne pas à 90°
  gauche/droite en strafe. Vérifier si le natif broadcast un facing
  (441) ou un flag strafe que nous n'émettons pas.

## Bugs combat / entités

- [ ] **Pet invocable tué** : l'ElfPet spawné est ciblable/attaquable
  → doit être intouchable (flag type dans le spawn) ET suivre le
  joueur comme l'auto-follow P→P.
- [ ] **Mob meurt 1-2 s après un simple clic gauche** : un clic
  sélection tue le mob. Auditer le chemin clic→attaque (auto-attack
  lancée à tort ou dégâts infinis).
- [ ] **Sorts de zone (AoE) pas corrigés** — le TODO
  `ZoneEntryFlow.Combat.Spells.Resolution.cs` restant.

## Systèmes

- [ ] **Talents de classe — bouton valider inopérant** : SetAAPoint
  (148) implémenté mais le client n'envoie peut-être pas ce NCID
  pour la validation (autre NCID ou batch ?). Tracer ce que le
  client envoie à l'appui sur « valider ».
- [ ] **Bouton « Max »** (upgrade skill de classe au max selon CP) :
  +1 level marche, Max non. Trouver le NCID/handler natif du max.
- [ ] **Bind monture** : persisté + 369 émis en headless mais pas
  visible en jeu ? Vérifier flag bind dans les records item émis
  (le flag est-il posé AVANT l'envoi de l'inventaire ?).
- [ ] **Vente NPC** : SellItems (23) ne vend pas ; bouton
  « Sell all WHT & GRN in backpack » idem. Natif : 2 NCIDs ?
- [ ] **Consommables/box** : ouverture ne donne rien ; aucune vérif
  de slots libres → ouvre sans donner quand sac plein. Natif vérifie
  probablement les slots avant de consommer.

## Question utilisateur
- « gaps combat restant » = les TODO marqués dans
  `ZoneEntryFlow.Combat.Spells.Resolution.cs` et
  `ZoneEntryFlow.Combat.Rewards.cs` (résolution de sorts multi-cibles
  / drops quête-conditionnés).
