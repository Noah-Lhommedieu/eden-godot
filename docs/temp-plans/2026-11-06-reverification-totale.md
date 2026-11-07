# 2026-11-06 — REVÉRIFICATION TOTALE (retour utilisateur : « 95 % ne marche pas »)

Méthode : vérifier CHAQUE système en jeu réel (pas wire, pas headless) —
capture avant/après comparée aux ref-shots. Statuts : CASSÉ /
PARTIEL / OK-prouvé. Ne pas marquer OK sans image regardée.

## Matrice de revérification

| # | Système | Vérif live | Statut | Détail |
|---|---------|-----------|--------|--------|
| 1 | Entités sous/au-dessus des maps | S002 (canopée/village multi-niveaux) + S191 (+710) + S110 : captures regardées | PARTIEL→OK sur 3 maps | fix `PeekNearestY` (surface la plus proche du z, terrain+OBJ candidats) ; reste à balayer les maps restantes |
| 2 | Inventaire / ItemSlot / MCL / tabs | ouvrir + drag + capture | À VÉRIFIER | |
| 3 | Shop PNJ (acheter/vendre/racheter) | cycle réel | À VÉRIFIER | |
| 4 | Work/craft (plans, ouvrir, lancer, produit) | craft réel | À VÉRIFIER | |
| 5 | Combine/fusion | live 2026-11-06 : sac → « Fusion » → 432 op10 → `combinewnd.layout` natif → drag catalyseur 20580 → recette 1588 affichée → CommissionBtn → 170 → 433 cast 4 s → 435 résultat=1 | OK-prouvé | produit 20577 dans le sac, 4 matériaux consommés, or -100 000 ; captures `godot-shots/combine-09/10` ; bugs corrigés : lookup feuille (Static aplatis), `FindChildren` type C#, 120 parasite à l'ouverture (StrengthenWindow aussi) |
| 6 | Strengthen +14 | réel pierres 100 % | OK-prouvé | live 2026-11-06 : action 13 → 432 op9 → `strengthenwnd_na.layout` natif → drag 28597/40009/39260 → 14× `StrengthenBtn` (NCID 119 valides, casts 433, updates 436, résultats 435) → paliers +6/+8/+10/+12/+14 : 5× NCID 369 acceptés sans « trame tronquée » ; DB `strengthen=14`, combos 195/145/2795/245/95, pierres consommées 10→4 puis 14→6 ; bug codec client corrigé : `ZoneClientModifyItemInfo.Field25` `i32→i64` + queue native + branche mode 8 ; captures `godot-shots/2026-11-06-strengthen-plus14-ncid369.png` + `…-tooltip.png`/`…-crop.png`. Inlay gemme séparé : PROUVÉ live UX réelle (rclick gemme→click équipement→NCID 19→embedded_id persisté, 2 châsses remplies visibles au tooltip — capture 2026-11-06-inlay-sockets4.png) |
| 7 | LifeSkill craft racial | réel | OK-prouvé | live 2026-11-06 : action 16 → 432 op0x68 → `jewelrystudy.layout` natif → analyse : drag gemme 36792 → StudyBtn → 214 op=0 recette 11 → 433/544(xp)/435, item consommé, exp→200 → **maîtrise** : apperception remplace recette 11→1089 (DB `player_life_skill` renommée) → production : clic ligne 1089 → CreateBtn → 214 op=2 → 433/435 → matériaux 30961/36801/36810 consommés, produit **36915** en sac (capture `godot-shots/lifeskill-production-36915.png`) ; bugs corrigés : bind misc perdu quand `SessionCarry.Session` est remplacé (rebind `ReferenceEquals`), `_rowProto` construit avant le ctx CEGUI (build paresseux), clic ligne absorbé par les enfants (Pass), `c_lifeskill.ini` lu sans `ResolveRawAsset` (0 recette), `FillRows` hors thread principal (CallDeferred) |
| 8 | Guilde complète (créer, VDG, bâtiments, stockage, battle) | réel | PARTIEL | création guilde par NPC 50340 (347→GuildWindow, NCID 49, `devinguild` id 5002, -50k or) ; achat VDG par dialogue 4188 (173, gates : niv≥30, chef, lv≥3, tréso≥500k, ≥30 membres — fixture membres documentée) → house_open=501 ; entrée 176 → migration S501 ; fenêtre bâtiments via housekeeper 53007 (service 4521 type 11 → 483/487) ; blueprint 35071 draggé sac→Building1 → NCID 130 → DB loc=10 + NPC 0x2a auto-posé (captures). Reste : stockage, battle/croisade (617→fenêtre codée, non testée live), leveling bâtiment, contrats NPC |
| 9 | Récolte VDG (pioche/gant/boule) | réel | À VÉRIFIER | |
| 10 | Cube op0x52 | réel | OK-prouvé | live 2026-11-06 : icône sac « Magic Cube » → action 101 → 432 → `newmagiccubewnd.layout` → drag 23223→StuffSlot01/02 → StartButton → NCID 232 → 433 cast → 435 + 274 → produit 23224 créé (slot 0), matériau consommé — DB confirmée. Capture `/tmp/cube-shot.png`. Gaps notés : nom de recette en chinois (t_itemchest non résolu FR), `@dragitem` prend tjs le 1er cell du même itemId (le 2e item identique exige @drag positionnel) ; ramassage trésor silencieux quand TryGet rate (diagnostic difficile) |
| 11 | Magicbox op0x58 | réel | OK-prouvé (conversion) | live 2026-11-06 : NPC 51872 → service 31 → NCID 612 → `magicboxwnd.layout` natif (onglets Infusion/Remaniement) → drag 28594→UseItem01 + 32041(q2)→Second01 → NCID 257 avec vrais slots → 433 cast → 613 résultat → DB : sacrifice consommé, arme `magicbox_energy=4`. Bugs corrigés : `ItemDropped`/`RightClicked` jamais câblés (miroir MagicCube). Refus natifs vérifiés en route : FlagsHi=0 → « OpFlag type not match », qualité sacrifice insuffisante → « need quality error ». Capture `godot-shots/2026-11-06_14-52.png`. Reste : remaniement (tab 2), i18n miss « Magic Box »/« x1111..x3333 » |
| 12 | Monnaies spéciales | réel | À VÉRIFIER | |
| 13 | Item mall | réel | À VÉRIFIER | |
| 14 | Trade joueur | réel | À VÉRIFIER | |
| 15 | HdV + filtres | réel | À VÉRIFIER | |
| 16 | Étals | réel | À VÉRIFIER | |
| 17 | Tooltips | survol | À VÉRIFIER | |
| 18 | Quêtes Goss Mountains | chaîne complète rejouée live | OK-prouvé | 1490→1108→1109→1110 + 1111-1114 + 1115→1116→1117 + chaîne donjon 1103→1104→1105 + 1878 (multi S105) ; tous types d'objectifs (kills, NPCTalk, ItemCollect clé→coffre, NPCItem, accept-gift) ; fixes : lucky-bag kind48+enchant (roll sauté), S_Mission Big5, sac plein → trésors au sol |
| 19 | Donjons (tous mondes) | sweep complet portails | OK-prouvé (entrée) | 44 paires Solo/Multi validées offre→9061→MigrationDone ; campagnes 212/216 OK niv.85, 224 gated Event_If_Monster 197 (refus natif) ; multi-only 126/225/226/227/232 = service 35 instance_dialog → 564 (prouvé NPC 50055), flow InstanceDungeonControl non implémenté → gap documenté `donjons-balayage-2026-11-05.md` |
| 20 | Mort / « Revivre ici » | rejouer | À VÉRIFIER | mort naturelle observée live (S105, eject savepoint S002 natif correct) ; chemin « Revivre ici » non re-capturé |
| 21 | Evaluate/trial + illusions | fait ce jour | OK-prouvé | ledger (dialogs 55/59 : wire complet + transport S107/S281/S283 — voir `2026-11-06-dialogs-55-59.md`) |
| 22 | XP/level/stats/sorts | rejouer | À VÉRIFIER | |

## Bugs à corriger
- [~] Entités sous/au-dessus des maps — **cause racine trouvée 2026-11-06** :
  `SnapToGround`/`SampleEntityGround` faisaient `max(z serveur, terrain)`
  → toute map où le heightfield fsm code une surface HAUTE (canopée de
  l'arbre géant S002 = 25.8 alors que la place du village est en dessous)
  posait joueur et entités SUR le toit. Inversement `PeekLowest` posait
  sous les donjons multi-étages. Fix : `WalkableGround.PeekNearestY` —
  la surface (terrain fsm OU face OBJ floor) la plus proche du z reçu,
  scans non bornés des deux côtés, repli PeekLowest.
  Preuves : S002 spawn (332,298) → y=25.8 place du village (était 35.9
  sur la branche) — capture `/tmp/s002-after.png` ; S191 → y=709.9 sur
  la plateforme haute (z réel serveur) ; S110 → y=31.5 sur la plateforme
  donjon. Wire : le z serveur n'est JAMAIS envoyé pour le joueur
  (reposition = x,y,facing ; le « z » affiché = facing) — les entités
  distantes ont un vrai z (zone résout depuis la scène).
  `EntityRegistry` : refY de la requête = z wire si réel, sinon plancher
  du joueur.
- [x] Position persistée z=0 en DB — corrigé + prouvé : `z=62.14`
  persisté en DB (S281, relog OK).
- [x] UI création de perso : champs coupés hors fenêtre 1600×900 —
  CORRIGÉ + PROUVÉ visuel (`/tmp/charcreate-fr.png` : panneau complet,
  boutons Créer/Dos accessibles). Bonus : labels cyclers + races étaient
  en anglais hardcodé → `UiTextEn.Translate` + 3 entrées TSV ajoutées
  (Coiffure/Couleur de cheveux/Anura) — tout FR à l'écran.
- [x] TPA cross-node — corrigé + prouvé live NPC 50378→S014.
- [x] Combine/fusion op 10 — handler client ajouté, prouvé live
  (produit 20577).
- [x] Dialogues 55/59 — wire natif complet + fenêtres dédiées,
  prouvé live S107 + S281/S283 (voir `2026-11-06-dialogs-55-59.md`).
- [x] Loot N+1 — `AddItemsAsync` bulk (2 SELECTs/kill), prouvé live
  mob 59046 → 274×2 + items DB.
- [x] Gossip mission `Talk L` ignoré — corrigé.
- [x] Lucky-bag kind-48 + enchant : le roll `S_DropItem[itemId]` était
  sauté quand `UseEnchantId != 0` (clé de quête 32811 → summon seul).
  Corrigé : roll post-enchant pour les kind-48, prouvé live
  (`Box 32811 -> 32812`, quête 1104 bouclée).
- [x] Aires gateway imbriquées (`MissionBridge.WatchGateways`
  first-match → front montant par aire) — prouvé live S007×2 + S009.
- [x] Fenêtre offre donjon persistante — fermeture sur
  `PortalArrival.Version`, prouvé visuel.
- [x] float.Parse/TryParse locale FR (GmFileProbe + Admin //goto) —
  InvariantCulture, prouvé live.
- [ ] (à remplir au fil des constats)
