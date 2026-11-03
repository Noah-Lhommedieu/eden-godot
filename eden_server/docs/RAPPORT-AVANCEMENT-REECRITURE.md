# Rapport d'Avancement — Réécriture du Serveur Eden en C# (.NET 8)

> **Date** : 14 Octobre 2026  
> **Branche Git** : `reverse-ia`  
> **Objectif** : Remplacement complet des 7 binaires C++ (`server_us`) par une architecture moderne C# .NET 8, propre, fortement typée et corrélée à la base de données PostgreSQL.

---

## 1. Tableau de Bord d'Avancement

| Domaine | Statut | Métriques & Réalisations |
|---|:---:|---|
| **Extraction du Protocole Réseau** | ✅ 100 % | **700 messages uniques** extraits et générés via script batch |
| **Typage Métier & Corrélation DB** | ✅ 100 % | Propriétés explicites (`Username`, `Password`, `WorldId`, `CharacterCount`...) |
| **Sérialisation BinStream** | ✅ 100 % | Support complet (byte, short, int, long, float, double, string) |
| **Base de Données PostgreSQL** | ✅ Connecté | Requêtes Dapper sur `accounts`, `worlds`, et `character_number` |
| **Pipeline Login (`Eden.Login`)** | ✅ **Complet (100 %)** | **Hello → Auth DB → Liste Mondes → Sélection → Ticket 22 octets** |
| **Passerelle (`Eden.Gateway`)** | 🟡 Squelette | Écoute TCP 5560 prête |
| **Serveurs Suivants (`World`, `Zone`)** | ⏳ Prochaine étape | Prêts pour la création/sélection de perso et le spawn map |
| **Tests Automatisés (xUnit)** | ✅ 100 % | 5 tests unitaires et d'intégration validés |

---

## 2. Découvertes Clés & Corrélation Binaire ↔ Base de Données

En décompilant les fonctions `lapis::CAccountDBO` et en croisant avec `database.out` et `Database/worlds.sql`, les vrais noms de variables et structures ont été établis :

### A. Structure des Mondes (`lapis::SWorldServerStates` & table `worlds`)
Décompilation de `lapis::CAccountDBO::GetWorldServerStates` (`0x08050210`) :
```c
SELECT account_id, world_id, character FROM character_number WHERE account_id = %d;
SELECT id, name, ip, port, online_user, maxnum_user, state, show_order FROM worlds;
```
Champs binaires envoyés au client dans `NC_CL_ClientGetWorldServers` :
- `short WorldId` : ID unique du serveur de monde (ex: 1010).
- `string WorldName` : Nom public du monde (ex: "EternityT").
- `string IpAddress` : IP d'écoute du serveur de monde.
- `short Port` : Port TCP (ex: 5712 ou 6544).
- `int Population` : Joueurs actuellement en ligne.
- `short Status` : État opérationnel du serveur.
- `short CharacterCount` : **Nombre de personnages possédés par le joueur sur ce monde** (issu de `character_number.character`).
- `short State` : Charge du serveur (0 = Normal, 1 = Nouveau, 2 = Occupé, 3 = Plein).
- `short ShowOrder` : Ordre d'affichage dans la liste client.
- `short Ping` : Indicateur de latence.

### B. Cycle de Connexion Intégral Reversé
1. **Étape 1 : Handshake** :
   - Client envoie `NC_CL_ClientHello` (`float ClientVersion`).
   - Serveur répond `NC_CL_ServerHello` (`float ServerVersion = 9.9f`).
2. **Étape 2 : Authentification** :
   - Client envoie `NC_CL_ServerLoginAccount` (`Username`, `Password`, `SecurityCode`).
   - Serveur interroge la table `fnaccount.accounts`. Si échec : `NC_CL_ClientLoginFail` (`ErrorCode = 1`).
   - Si succès : envoi de `NC_CL_ClientGetWorldServers` avec les mondes et le compte de personnages du joueur.
3. **Étape 3 : Sélection du Monde** :
   - Client envoie `NC_CL_ServerLoginWorld` (`WorldId`, `Username`, `SessionToken`).
   - Serveur met à jour `accounts.worldserver` via `SaveLastWorldAsync`.
4. **Étape 4 : Émission du Ticket de Transfert** :
   - Serveur génère un token cryptographique de **22 octets exacts** (décompilation de `lapis::CNC_CL_ClientReceiveTicketToWorldServer::Serialize`).
   - Expédie `NC_CL_ClientReceiveTicketToWorldServer` au client.
   - Le client bascule automatiquement sur le `WorldServer` avec ce ticket.

---

## 3. Architecture Réécrite & Fichiers Clés

- [LoginMessages.cs](server/src/Eden.Core/Networking/Messages/LoginMessages.cs) : Messages typés proprement avec vrais noms de variables.
- [AccountRepository.cs](server/src/Eden.Core/Database/AccountRepository.cs) : Dépôt de données PostgreSQL (`accounts`, `worlds`, `character_number`).
- [Program.cs (Eden.Login)](server/src/Eden.Login/Program.cs) : Serveur de login 100 % opérationnel sur le port 6543.
- [ProtocolAndSerializationTests.cs](server/tests/Eden.Tests/ProtocolAndSerializationTests.cs) : Tests automatisés validant la conformité binaire.

---

## 4. Prochaine Étape : Microservice `Eden.World`

Le flux Login étant 100 % résolu et fonctionnel :
1. Créer le microservice `Eden.World` (port 6544).
2. Valider le ticket reçu (22 octets).
3. Décompiler et recoder la liste des personnages (`CNC_CW_CharacterList`) et la création de personnage (`CNC_CW_CreateCharacter`) connectées à la table `fndb1.characters`.
