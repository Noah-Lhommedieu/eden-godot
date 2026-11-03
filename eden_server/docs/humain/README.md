# Docs lisibles — pour le lead dev

> **Ce dossier = les docs faits pour être lus par un humain.**
> Le reste de `docs/` est surtout la mémoire de travail de l'agent IA
> (plans, rapports de reverse, matrices). Tu peux lire les deux — la
> séparation sert juste à t'éviter les pavés techniques.

| Doc | Contenu |
|---|---|
| [`BASE-DE-DONNEES.md`](BASE-DE-DONNEES.md) | **Chaque table Postgres** : à quoi elle sert en jeu, si le serveur C# l'utilise, schéma de relations |
| [`NCID-EN-JEU.md`](NCID-EN-JEU.md) | **Tous les NCIDs** réimplémentés, traduits en « ce que c'est en jeu » |
| [`NCID-ETAT.md`](NCID-ETAT.md) | **État de chaque NCID** (fait/replay/absent) par domaine + verdict combat & équipement |
| [`MAINTENABILITE.md`](MAINTENABILITE.md) | Maintenabilité serveur, modifier le client, ajouter/modifier des panels UI |

## Où est quoi (carte rapide)

- **`docs/humain/`** ← tu es ici. Docs de synthèse lisibles.
- **`docs/`** (racine) = docs de travail agent : plans
  (`MASTER-IMPLEMENTATION-PLAN.md`, `action_plans/`, `temp-plans/`),
  méthodo reverse, rapports d'avancement.
- **`docs/implementation/`** = spec détaillée par feature (~100 fichiers
  — la doc technique exhaustive, pas la lecture la plus digeste).
- **`docs/protocole/`** = protocole réseau détaillé.
- **`docs/generated/`** = sorties automatiques (matrice des NCIDs,
  constantes) — données, pas de la doc.

## Pour toi en priorité

- Tu veux comprendre une feature en jeu → `NCID-EN-JEU.md`
- Tu veux savoir où est stockée une donnée → `BASE-DE-DONNEES.md`
- Tu veux modifier le client/l'UI → `REFORGED-UI.md` (Reforged :
  pkgfs, mods DLL) ou `docs/ANALYSE-MODDING-ET-OUTILS.md` (agent)
- Tu veux éditer du contenu (mobs, maps, textes) → `docs/TUTO-CLIENT.md`
- Tu veux créer un compte/personnage (Reforged n'a plus l'UI de
  création) → `EDEN-ADMIN.md`
- Tu veux comprendre les .ini / où sont les données → `GAMEDATA.md`
