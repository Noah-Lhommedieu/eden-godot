# Récapitulatif des Spécifications et Plans d'Action

L'ensemble des serveurs natifs du jeu Eden Eternal a été décompilé, rétro-ingénié et reconstruit en .NET 8 selon les spécifications détaillées suivantes :

1. [01-EDEN-LOGIN-PLAN.md](01-EDEN-LOGIN-PLAN.md) : Protocole de connexion, handshake, authentification PostgreSQL `fnaccount`, liste des serveurs de monde et émission de billets.
2. [02-EDEN-WORLD-PLAN.md](02-EDEN-WORLD-PLAN.md) : Gestion du lobby, validation de ticket, chargement/création/suppression de personnages dans `fndb1`, et délivrance de billets de zone.
3. [03-EDEN-ZONE-PLAN.md](03-EDEN-ZONE-PLAN.md) : Partitionnement spatial en cellules de grille (AOI), diffusion de mouvements, moteur de calcul de dégâts et de combat, synchronisation de points de vie et d'expérience.
4. [04-EDEN-TICKET-MISSION-PLAN.md](04-EDEN-TICKET-MISSION-PLAN.md) : Système de billetterie inter-serveurs (protection anti-rejeu, expiration), messagerie en jeu (boîte aux lettres avec pièces jointes) et canaux de guilde.
