# Dossier d'Implémentation - Eden Eternal Server

Ce dossier contient la documentation détaillée de l'implémentation de chaque système du serveur Eden Eternal en C#/.NET 8.

## Structure

Chaque fichier `XX-system-name.md` correspond à un système spécifique du jeu et contient:

1. **Description du système** - Fonctionnalités et objectifs
2. **Symboles lapis:: analysés** - Classes et fonctions identifiées dans les binaires
3. **Fichiers de données** - Fichiers .ini associés
4. **Protocole réseau** - Messages CNC_*/CNE_* concernés
5. **Base de données** - Tables PostgreSQL concernées
6. **Implémentation C#** - Code et classes créées
7. **Tests** - Validation du système
8. **Status** - État d'avancement

## Progression

- [ ] 01-class-system.md - Système de classes et changement de classe
- [ ] 02-character-creation.md - Création et suppression de personnage
- [ ] 03-player-stats.md - Stats et profil joueur
- [ ] 04-inventory-system.md - Inventaire et sacs
- [ ] 05-bank-storage.md - Banque et stockage
- [ ] 06-trade-system.md - Système d'échange P2P
- [ ] 07-auction-system.md - Hôtel des ventes
- [ ] 08-item-mall.md - Boutiques AP/EP
- [ ] 09-crafting-basic.md - Craft de base
- [ ] 10-race-crafting.md - Craft de race
- [ ] 11-enchanting-system.md - Enchantement d'équipement
- [ ] 12-item-fusion.md - Fusion et upgrade d'items
- [ ] 13-spell-system.md - Skills et sorts
- [ ] 14-item-procs.md - Proc d'items et enchants
- [ ] 15-combat-breakpoints.md - Breakpoints de combat
- [ ] 16-party-system.md - Système de groupe (party)
- [ ] 17-raid-system.md - Raid (25+ joueurs)
- [ ] 18-matchmaking.md - Random et matchmaking
- [ ] 19-guild-system.md - Guildes et familles
- [ ] 20-dungeon-system.md - Donjons solo et multi
- [ ] 21-colosseum-pvp.md - Colosseum PvP
- [ ] 22-battle-crusade.md - Battle Crusade
- [ ] 23-battle-royale.md - Battle Royale
- [ ] 24-friend-system.md - Système d'amis
- [ ] 25-marriage-system.md - Mariage et couple
- [ ] 26-community-search.md - Recherche community
- [ ] 27-monster-system.md - Monstres et boss
- [ ] 28-loot-system.md - Drops et loot
- [ ] 29-pet-system.md - Familiers (pets/elfs)
- [ ] 30-mount-system.md - Montures
- [ ] 31-housing-system.md - Maisons personnalisables
- [ ] 32-achievement-titles.md - Succès et titres
- [ ] 33-dream-destruction.md - Dream of Destruction
- [ ] 34-ranking-system.md - Ranking et leaderboards
- [ ] 35-map-teleportation.md - Maps et téléportation
- [ ] 36-fast-travel.md - Fast travel
- [ ] 37-mail-system.md - Système de mail
- [ ] 38-taxation-system.md - Taxation et revenus
- [ ] 39-quest-system.md - Système de quêtes
- [ ] 40-dialogue-system.md - Système de dialogue
- [ ] 41-life-skills.md - Skills de vie
- [ ] 42-gm-commands.md - Commandes GM
- [ ] 43-security.md - Anti-cheat et sécurité
- [ ] 44-performance.md - Performance et optimisation

## Ordre Recommandé d'Implémentation

### Phase 1 (Mois 1-2): Fondamentaux
01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12

### Phase 2 (Mois 3-4): Combat et Groupes
13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23

### Phase 3 (Mois 5-6): Social et Contenu
24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34

### Phase 4 (Mois 7-8): Avancé
35, 36, 37, 38, 39, 40, 41

### Phase 5 (Mois 9-10): Finalisation
42, 43, 44

## Méthodologie

Pour chaque système:

1. **Analyse des symboles** - Extraire les classes lapis:: concernées
2. **Décompilation** - Analyser le code assembleur avec Ghidra
3. **Corrélation données** - Identifier les fichiers .ini et tables DB
4. **Protocole** - Mapper les messages réseau
5. **Implémentation** - Coder les classes C#
6. **Tests** - Valider le fonctionnement
7. **Documentation** - Rédiger ce fichier markdown

## Références

- Plan maître: `../MASTER-IMPLEMENTATION-PLAN.md`
- Symboles: `../generated/ZoneServer-symbols.txt`
- Symboles: `../generated/WorldServer-symbols.txt`
- Symboles: `../generated/MissionServer-symbols.txt`
- Protocole: `../protocole/protocol_spec.json`
- Fichiers données: `../../server_us/Data/db/`
