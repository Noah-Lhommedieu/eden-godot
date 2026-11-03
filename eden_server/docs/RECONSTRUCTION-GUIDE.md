# Guide de Reconstruction et Déploiement : Eden Eternal Server Suite (.NET 8)

## 1. Architecture Globale des Microservices

La suite de serveurs Eden Eternal (.NET 8) est reconstruite fidèlement depuis les binaires natifs Linux (`server_us/*`) selon une architecture distribuée :

```
                                  +-------------------+
                                  |    PostgreSQL     |
                                  | (fnaccount/fndb1) |
                                  +---------+---------+
                                            |
                                            v
+------------------+            +-----------------------+
|  Client de Jeu   | ---------> |  Eden.Login (5100)    |
|  (Eden Eternal)  |            +-----------+-----------+
+--------+---------+                        | Émet World Ticket (22 bytes)
         |                                  v
         |                      +-----------------------+
         +--------------------> |  Eden.World (5200)    |
         |                      +-----------+-----------+
         |                                  | Émet Zone Ticket (22 bytes)
         |                                  v
         +--------------------> +-----------------------+ <---> +--------------------+
                                |  Eden.Zone (5201)     |       | Eden.Mission (5202)|
                                +-----------+-----------+       +--------------------+
                                            | Valide
                                            v
                                +-----------------------+
                                |  Eden.Ticket (2067)   |
                                +-----------------------+
```

---

## 2. Ports et Configuration Réseau

| Service | Port Écoute | Base de Données | Rôle Principal |
|---|---|---|---|
| **Eden.Login** | `5100` | `fnaccount` | Handshake (0x01), Authentification, Liste des mondes |
| **Eden.World** | `5200` | `fndb1`, `fnaccount` | Lobby, Roster des personnages, Création/Suppression |
| **Eden.Zone** | `5201` | `fndb1` | Instanciation de carte, Moteur AOI, Déplacements, Combat |
| **Eden.Ticket** | `2067` | En-mémoire (TTL 60s) | Validation atomique des billets inter-serveurs |
| **Eden.Mission** | `5202` | `fndb1` | Messagerie interne, Guildes et Annonces globales |
| **Eden.Gateway** | `6543` | Proxy | Point d'entrée passerelle de rétro-compatibilité |

---

## 3. Démarrage et Déploiement Docker

### Lancement Rapide du Cluster
```bash
# Lancement de l'ensemble des conteneurs (PostgreSQL + tous les microservices)
docker compose up -d --build

# Vérification du statut des services
docker compose ps

# Consultation des journaux d'exécution
docker compose logs -f eden-login eden-world eden-zone
```

---

## 4. Tests et Validation
Pour exécuter l'ensemble de la suite de tests unitaires et du scénario d'intégration bout-en-bout (Gameplay Loop complète) :
```bash
cd server
dotnet test Eden.sln
```
