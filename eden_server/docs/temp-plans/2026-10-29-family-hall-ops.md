# Plan — Hall de famille : opérations bâtiments (180/161/130/131/225)

Date : 2026-10-29. Suite des lots 173/176/174/175.

## NCID 180 — FamilyBuildingRepair (FAIT)

- Wire : `[s16 loc][u8 repairAll]` (généré + désasm zone @0x8253b80 :
  `this+0x12` loc, `this+0x14` flag).
- Zone native : char valide + famille dans la map d'instances
  `Ins+0x460` (key = char+0x5c) ; repairAll=0 →
  `GetItemFromBuildingContainer(fam, 0, loc)` + coût
  `(template+0x20 − item+0xc) × template+0x1c` ; repairAll≠0 →
  `GetAllBuildingRepaircosts` ; erreurs : trésorerie < coût → 0x8f0,
  rien à réparer → 0x8f1, déjà plein → 0x8f2 ; puis relais
  `AgentDBACommand "FamilyBuildingRepair Loc=%hd RepairAll=%hd"`.
- Mission `TC_FamilyBuildingRepair` @0x81ef3d0 :
  `CheckPrivilige(exec, 10)` — échec → TextOutputIndex **900** ;
  succès → `UpdateBuildingDurability(fam, loc, max)` +
  `DonateMoney(−cost)`.
- Implémentation C# (`FamilyHandlers.cs`) : privilège 10 fidèle
  (`GuildService.CheckPrivilege`, masque cadre + niveau ≥3 pour
  idx 9-12), coûts via `GameDataService.Buildings`
  (`S_Buliding.ini` : type col3, cost/pt col7, maxDur col8),
  débit trésorerie `TrySpendTreasuryAsync`, UPDATE
  `family_building_container.durability`, refresh 487 au demandeur
  (le push natif 757→486 n'est pas encore prouvé champ par champ —
  487 porte les mêmes données).

## NCID 161 — FamilyStatue (décodé, reporté)

Wire : `[i32 targetCharId][i32 position 0-4][str name][i32 rot? u16]
[f32 param]` — `Execute` zone @0x8257e80 :

- Gates : objet type 7, position ≤ 4, famille du char dans Ins+0x460,
  **`fam+0x10 == char+0x34` (leader seul)** sinon 0x382 ; node famille
  (`SFamily::GetNode` = house_open) + `IsInFamilyInstance` + node
  courant == node famille sinon 0x38e.
- **position 0 = retirer** : itère la map statues
  (`Ins+0x2b4`, clé `(model<<16 | nodeTid) × family_id`, bornes
  `-0x298b..-0x2986`), compare `statue+0x18` (nom) au param →
  nom vidé, `CNode::BornStatue` (respawn), `SaveStatueToDB` ;
  absent → 0x38f "%s" ; succès → mission `FamilyTextIndex 0x38b`.
- **position 1-4 = poser** : fam level ≥ 4 (fam+0x18) ; si < 6
  position ≤ 2 ; cible `GetCharacter(charId)` en ligne sinon 0x38e ;
  trésorerie ≥ 500 000 sinon 0x38c ; débit via
  `FamilyDonateMoney money = -500000` ; `CPlayerFactory::SetStatue`
  (target, nodeId, model 0xd675/6/7/8 par position, pos16, rot)
  → assigne nom du perso + model, `BornStatue`, `SaveStatueToDB` ;
  broadcast famille texte 0xb4e + `FamilyTextIndex 0x38a`.
- **Bloquants** : `BornStatue` crée une entité `CStatue` dans le
  node (système de sight — spawn broadcast non modélisé) + la
  persistance statues (`CStatueDBO`, table statues/`statues_scenes`).
  Aucune brique statue n'existe côté C# : lot dédié requis.

## NCID 130/131 — conteneur bâtiments (TODO)

- 130 put : exige op-target NPC type 0x3b (talk-op NPC du hall —
  S501 spawn), sélecteur conteneur 0/1/2, contrôle capacité, INSERT
  item → broadcast 757→486.
- 131 get : lookup conteneur/loc, protections linked-building,
  `FamilyBuildingContainerDeleteItem` mission.
- Bloquant de fidélité : le talk-op 0x3b (l'interaction NPC du hall)
  n'est pas encore modélisé — décoder la création du CTalkOp sur clic
  NPC de hall avant d'implémenter, sinon les puts ne peuvent pas être
  gatés correctement.

## NCID 225 — FamilyBattleAskInfo (TODO)

- `[s16 action]` ; zone @0x8253740 : node type 7 requis (contexte
  champ de bataille famille) ; action 1 → commande mission
  `"FamilyBattleCheckJoin"`, 2 → `Join` direct, 0 →
  `"FamilyBattleJudgeQuery"`. À implémenter avec la bataille de
  famille mission-side (561/562 réponses).

## Vérifs

- [x] `dotnet build` 0/0 après 180.
- [x] `dotnet test` verts (860/860).
- [x] Commit `a06f38d` — NCID 180.

## État au commit a06f38d

| NCID | État |
|---|---|
| 180 réparation bâtiments | ✅ implémenté (zone+mission fusionnés) |
| 161 statue | 🔍 décodé — bloqué (entité CStatue + sight + DB) |
| 130/131 conteneur | 🔍 décodé — bloqué (talk-op 0x3b + moves items) |
| 225 battle ask | 🔍 décodé — bloqué (FamilyBattle mission-side) |
