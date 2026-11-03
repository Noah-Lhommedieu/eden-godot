# Master plan de secours — sous-systèmes de jeu complets

## Porte d'entrée

Inventaire prévisionnel uniquement tant que le `docs/MASTER-PLAN.md` contient
une tâche P0 ouverte ou un défaut visuel rapporté non reproduit et clos. Le
plan ne peut démarrer qu'après captures Reforged/Godot acceptées, parcours
connexion→monde stable, animations/facing/terrain/HUD validés et exports testés.

Le code existant et les tests de codec ne prouvent pas le fonctionnement
intégré. À cette révision, aucun système ci-dessous n'est certifié accepté
end-to-end malgré les implémentations partielles antérieures. Les données,
binaires et le serveur `eden_server/` restent en lecture seule. Paquets au
format incertain interdits en live jusqu'à preuve golden/désassemblage.

Légende : `[ ]` à faire ; `[~]` code présent mais validation intégrée
incomplète ; `[!]` défaut constaté ; `[x]` critère intégré prouvé.

## S1. Combat, compétences, ciblage et mort
- [x] S1.1 Attaque auto : `--mob-test --combat-test` live sur `Red Cap Mushroid` — approche, NCID 5, dégâts serveur, sort `50011` `279→281`, mort.
- [~] S1.2 Animations locomotion/combat : input local immédiat et facing corrigés ; attaque/mort prouvées live sur mob, vidéo et cas hurt/death joueur + variantes restent à faire.
- [~] S1.3 Skills : 141 sorts reçus et cast `50011` confirmé `279→281` live ; matrice classe/niveau/coût/cooldown/interruption/effet à auditer.
- [ ] S1.4 Dégâts/miss/crit/heal/floater affichés en accord avec HP/MP server-side et formule native.
- [ ] S1.5 Auto-target, changement cible, mob mort, aggro/reset, portée, line-of-sight, collision, knockback et loot.
- [~] S1.6 Mort, revive, spawn, perte/retour de contrôle, animations et reconnexion.
- [ ] S1.7 Cas joueur, NPC, boss, ranged/magic, armes, classes, mob volant et mount.
- [ ] S1.8 Test 30/60/144 FPS + jitter : aucune répétition de swing, soft lock ou action fantôme.

## S2. Équipement, apparence et sac
- [~] S2.1 Equip/unequip validé par serveur ; race/classe/niveau/slot/refus affichés correctement.
- [~] S2.2 Modèle visuel item réel, bones, offsets et animations pour armure, arme, casque, cape, accessoire.
- [~] S2.3 Drag/drop, swap, split stack, multi-sac, hotbar, équipement et ack ; pas de perte/duplication.
- [~] S2.4 Consommables : action, cible, effet, stack, cooldown et synchronisation UI.
- [ ] S2.5 Durabilité, réparation, enchant/refine, bind, identify, dye, costume et itemmall selon preuves.
- [ ] S2.6 Icônes et modèles exacts, descriptions EN, prix/stats/restrictions et qualité visuelle.

## S3. Quêtes et dialogues
- [~] S3.1 Dialogue NPC : clic droit live `PickArea → TalkToNpc` validé ; pages/choix/navigation/fermeture et services hors shop restent à éprouver.
- [~] S3.2 Accept/abandon/progression/complete/reward persistés ; objectifs kill/talk/collect/transport.
- [ ] S3.3 Prérequis, branches, quotidiennes/répétables, mission d'instance, conditions event et level.
- [ ] S3.4 Tracker/mission log : ordre, compteurs, objectifs, icônes, marqueurs NPC, états disponibles/rendre.
- [ ] S3.5 Échantillons de chaque type de quête comparés aux tables/serveur natifs et testés après relog.

## S4. NPC, shops et économie
- [~] S4.1 NPC : mapping `npcId→stem/skin/scale` résolu via `c_npc.ini`/`npclist.ini`, 3127 entrées et 53/53 GLB sur S002 ; spawn live et marchand `TalkToNpc` prouvés, noms/services/variantes restent à auditer.
- [~] S4.2 Shop : achat/vente, quantité, stock, devise, remise, erreur, argent/inventaire et ack réel.
- [ ] S4.3 Prix, durabilité, réparation, identification et arrondis alignés sur les formules natives.
- [~] S4.4 NPC services : marchand live `EnterShop` prouvé après clic droit ; achat/vente/banque/craft/transport/class restent à tester sur les NPC réels.
- [ ] S4.5 Auction house, buyback, mail, message board, player stalls et réparateur si disponibles.
- [ ] S4.6 Économie persistée sur deux sessions, transactions concurrentes, refus fonds insuffisants/inventaire plein.

## S5. Transport et cartes
- [~] S5.1 Routes transport NPC, confirmation/coût/prérequis, migration map, loading et spawn destination.
- [ ] S5.2 Transportation array / portail / pierre portail : destination, liste, restriction, cooldown et retour serveur.
- [ ] S5.3 Portails scène, exit checks, collision, conditions, instance et rollback d'échec.
- [ ] S5.4 World map/minimap : labels EN, marqueurs, joueurs/NPC, zoom, route, clic, fog-of-war si natif.
- [ ] S5.5 Tester voyages inter-map avec reconnexion, position, canal et déchargement de scène sans fuite.

## S6. Races, création et classes
- [~] S6.1 Human, Zumi, Anuran, Ursun, Halfkin : règles, corps, genre, head/hair/face/palette et apparence serveur.
- [ ] S6.2 Spawn map, caméra, animations, collision et restrictions spécifiques à chaque race.
- [~] S6.3 Changement classe : liste, conditions, skills, stats, portrait, gear, hotbar et persistance.
- [ ] S6.4 Matrice race×body×skin×tenue×arme ; aucune combinaison ne perd les yeux ou le haut de tête.
- [ ] S6.5 Elf/partner : données, modèle, commande, attributs, équipement et UI après preuve native.

## S7. Banque et stockage
- [~] S7.1 Ouvrir via NPC bank réel, charger onglets/items, dépôt/retrait/or et ack.
- [ ] S7.2 Extension, plusieurs tabs/sacs, règles item bound, limites et frais natifs.
- [ ] S7.3 Drag/drop atomique inventaire↔équipement↔banque, erreurs/relogin sans perte ni duplication.
- [ ] S7.4 Tester sur map avec vrai banquier ; mock ne ferme pas le flux.

## S8. Craft et collecte
- [~] S8.1 Recipes, ingrédients, catalysts, prérequis classe/niveau et coût.
- [~] S8.2 Start/progress/cancel/collect, temps, réussite/échec/critique, résultat persistant.
- [ ] S8.3 Plusieurs slots, travaux offline, expiration, inventaire plein, reconnexion.
- [ ] S8.4 Gathering nodes, outil, animation, temps de respawn, loot et compétence collecte.
- [ ] S8.5 Valider sur station/NPC réel et comparer consommation/résultat au serveur.

## S9. Montures et mouvement spécial
- [~] S9.1 Item mount/use/dismount, modèle, selle, joueur attaché, caméra et état réseau.
- [ ] S9.2 Tous les stems ride : rig, textures, bones, idle/move/turn/death, offsets et scale.
- [ ] S9.3 Combat monté, restrictions skill, cible/facing, mort, changement map et retour à pied.
- [ ] S9.4 Séparer les véhicules/transport NPC des montures personnelles.

## S10. Party, guild et interactions joueurs
- [~] S10.1 Invitation/join/leave, leader, roster, stats, map, distance et loot rules en deux clients.
- [ ] S10.2 Guild/membres/rangs/invitation/chat/storage/actions suivant capacités prouvées.
- [ ] S10.3 Trade atomique : invite, accept/refuse, items/or, double confirmation, cancel, stale state.
- [ ] S10.4 Friends/ignore/whisper/channel/emotes ; modération et messages d'erreur EN.
- [ ] S10.5 Chaque parcours social testé sur deux sessions, pas seulement données mock.

## S11. Interfaces gameplay
- [ ] S11.1 Hotbars d'actions/items/sorts, assignation drag, keybind, cooldowns, charges et tooltips.
- [ ] S11.2 Buffs/debuffs, combat log, cast/target/death/loot feedback lié à l'état serveur.
- [ ] S11.3 Les nouvelles fenêtres gardent le skin, l'échelle, le drag et le layout validés du master plan.
- [ ] S11.4 Textes, devises, erreurs, tooltips et labels résolus en anglais.

## S12. Robustesse et livraison
- [ ] S12.1 Matrice features×races×maps×résolutions×qualité×réseau avec inconnues explicites.
- [ ] S12.2 Golden byte-level codecs, tests composants UI, parcours live des transactions, relog et migrations.
- [ ] S12.3 Déconnexion/crash à chaque étape : rollback sûr et état serveur canonique à la reconnexion.
- [ ] S12.4 Exports Linux/Windows autonomes avec données, tables, fonts et icônes ; lancement Wine vérifié si disponible.
- [ ] S12.5 Budget CPU/GPU/RAM multi-instance mesuré sans réduire qualité/stabilité silencieusement.
- [~] S12.6 `ETAT-FINAL.md` distingue build/tests, sondes, parcours live `login→S002`, clic NPC réel et limites restantes.

## Critère de clôture

Un système est accepté seulement si data/protocole, UI, feedback, état serveur,
cas d'erreur, relog et client live fonctionnent ensemble avec captures. Les
mocks sont étiquetés comme mocks ; les éléments `[~]` et `[!]` restent ouverts.
Après chaque lot, rejouer tous les P0 du master plan.
