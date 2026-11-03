# Index de la documentation

> Deux publics, deux portes d'entrée. Tout est lisible par tous — la
> séparation évite au lead dev de traverser les pavés techniques.

## 🧑 [`humain/`](humain/) — pour le lead dev

Docs de synthèse écrites pour être lues :

- [`humain/BASE-DE-DONNEES.md`](humain/BASE-DE-DONNEES.md) — chaque
  table Postgres : utilité en jeu + usage C# + relations.
- [`humain/NCID-EN-JEU.md`](humain/NCID-EN-JEU.md) — tous les NCIDs
  réimplémentés traduits en actions de jeu.
- [`humain/NCID-ETAT.md`](humain/NCID-ETAT.md) — état d'implémentation
  de chaque NCID par domaine (régénéré par `tools/gen_ncid_etat.py`).
- [`humain/MAINTENABILITE.md`](humain/MAINTENABILITE.md) — état du
  projet, modding client, ajout de panels UI.
- [`humain/REFORGED-UI.md`](humain/REFORGED-UI.md) — modifier l'UI
  du client Reforged : pkgfs `mode=loose` + `uidata\`, DLL de mods
  `RfgModInit`, toggles `eden_patch.ini`.

## 🤖 Racine `docs/` — mémoire de travail de l'agent

Plans, méthodo et rapports de reverse (denses, orientés preuve) :

- `MASTER-IMPLEMENTATION-PLAN.md`, `ACTION-PLAN.md`,
  `action_plans/`, `temp-plans/` — plans par lots.
- `METHODOLOGIE-REVERSE-DEV.md`, `REVERSE-ENGINEERING-GUIDE.md`,
  `RECONSTRUCTION-GUIDE.md` — comment le reverse est mené.
- `AUDIT-*.md`, `PHASE*-STATUS.md`, `RAPPORT-*.md`, `PROGRESSION.md`,
  `NEXT-*.md` — historique d'avancement.
- `WIRE-EVIDENCE.md`, `SYMBOLS-INVENTORY.md`, `S_CLASS_DECODING_STATUS.md`
  — preuves protocole/symboles.
- `ANALYSE-MODDING-ET-OUTILS.md`, `ANALYSE-REFORGED-CLIENT.md`,
  `TUTO-CLIENT.md`, `QUICKBMS-USAGE.md` — utiles **aussi** à l'humain
  (modding client, édition de contenu).
- `implementation/` (~100 fichiers) — spec technique par feature.
- `protocole/` — protocole réseau détaillé.
- `generated/` — sorties automatiques (FEATURE-MATRIX, constantes).
