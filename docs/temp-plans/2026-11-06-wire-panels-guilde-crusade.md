# Plan — câblage panels guilde/VDG/crusade + réparation bâtiments

Date : 2026-11-06. Suite de `2026-11-06-reverification-totale.md`.

## Constat (audits 2026-11-06)

- 347 `ClientEnterFamilyCreation` reçu par MiscPackets mais **personne ne le consomme** → création guilde chez le PNJ morte.
- 501 `ClientEnterFamilyInstanceCreation` idem → achat VDG impossible en jeu.
- 617 `ClientOpenBattleCrusadeDialog` idem → croisade de bataille invisible.
- Boutons Repair/RepairAll de `familyadministration` **Disabled** côté client alors que zone 180 est prêt serveur (privilège 10, coût trésorerie).

## Travaux

### Client — protocole (`ZoneClientToServerMessages.cs`)

- [ ] `ZoneServerFamilyBuildingRepair` (180) : `[s16 loc][u8 repairAll]` — miroir du codec serveur `CNE_CZ_ZoneServerFamilyBuildingRepair`.
- [ ] `ZoneServerBattleCrusadeJoin` (259) : `[i32 crusadeId]` — miroir `CNE_CZ_ZoneServerBattleCrusadeJoin`.

### Client — `GuildWindow.cs`

- [ ] `OnMisc` : `ZoneClientEnterFamilyCreation` (347) → `Open()` sur l'onglet création (le panneau gère déjà l'état « sans guilde »).
- [ ] `OnMisc` : `ZoneClientEnterFamilyInstanceCreation` (501) → dialogue achat VDG : choix type (1→501 forêt / 0→502 montagne — `FamilyService.InstanceNode`) + vip u8 → `FireZone(ZoneServerFamilyInstanceCreate)`.
- [ ] Repair : dé-Disabled, envoyer 180 `[loc du bâtiment sélectionné][0]` ; RepairAll → `[0][1]` ; puis redemander 132 pour rafraîchir. Sélection = clic sur `House/Slot{i}` → mémorise `Loc`.
- [ ] Après 173/180 : réémettre la requête de données hall (132) pour rafraîchir le rendu.

### Client — fenêtre croisade (617)

- [ ] Nouvelle fenêtre `CrusadeWindow` (layout `battlecrusadewnd.layout`) : parse natif `[i32 npcUid][u16 n1][n1×i32][u16 n2][n2×i32]` via `OnZone(617)` dédié (le codec généré ne lit que les champs fixes), listes → ComboBox, `JoinBtn` → 259 `[crusadeId]`, `CloseBtn` → fermer.
- [ ] Vérifier que MiscPackets + handler dédié cohabitent (dispatch = liste — OK vérifié `EdenSession.cs:100`).

## Preuves

- [ ] `dotnet build` client vert.
- [ ] Live : PNJ bureau guilde → 347 → fenêtre ouverte (capture Flameshot vs ref-shot création guilde).
- [ ] Live : 501 → dialogue type → 173 → `house_open` persisté (log world + DB).
- [ ] Live : Repair envoie 180 → log zone `[Zone] FamilyBuildingRepair`.
- [ ] Live : 617 → fenêtre croisade (capture), Join → 259 logué mission.
