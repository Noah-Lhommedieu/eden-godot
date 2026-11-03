# Chantier 3 — Consolider la connaissance protocole (audit §3)

Source : `docs/AUDIT_PROJET_ETAT_ACTUEL.md` §Plan d'action, chantier 3.
Chantiers 1 (concurrence) et 2 (harnais wire) terminés — reste la
consolidation de la connaissance pour gagner en vélocité.

## Tâches
- [x] 3.1 Régénérer `docs/generated/ncid_map.json` (TicketServer vide
      à combler — vérifier la présence du binaire).
- [x] 3.2 `docs/generated/constants.md` — lexique unique : états elf,
      types d'attributs 418, types de containers, EquipLoc, indices de
      texte, types de dialogues/services, flags enchant, actionIds —
      sourcé désasm + captures + S_*.ini.
- [x] 3.3 `docs/generated/FEATURE-MATRIX.md` — NCID → classe générée →
      sites d'émission/handlers → statut golden (prouvé / codec dédié /
      passthrough / non prouvé). Généré par script `tools/gen_feature_matrix.py`
      pour rester frais.
- [x] 3.4 Dissector Wireshark `.lua` du protocole lapis (u16 len + RC4)
      — audit §3.2.3, optionnel mais peu coûteux.
- [x] 3.5 Documenter le choix hybride "C# hôte + Postgres docker" dans
      ARCHITECTURE.md (audit §5.2 : "juste à documenter").

## Hors périmètre
- `System.IO.Pipelines`, EF Core, DI container — déconseillés par
  l'audit lui-même.
