# Lot — Handlers famille natifs (zone)

Source : `re/ghidra/zone/decompiled.c` (Execute/Serialize),
`re/ghidra/mission/decompiled.c` (CFamilyDBO, CLogFactory),
schéma fndb1 (`family`, `family_cadre_members`, `family_storage_log`,
`guilds`, `guild_members`).

## Flux natifs décodés

- **UpdateFamily** `[u32 charId]` → **346 ClientChangeFamily**
  (`Execute` @0x829cf20, `Serialize` @0x81d9xxx) :
  `[u32 charId][i32 familyId][u8 fam+0x18][str familyName][i32 rank]
  [s16 emblem][u8 revision]` — la cible doit être en ligne sur le node
  (`CNode::GetCharacter`) ; défauts sans famille : id=0, byte=0,
  rank=-1, emblem=0xffff. Le dernier byte = `char+0x4de`, compteur de
  révision incrémenté à chaque changement d'affectation famille
  (ZW ChangeFamily, rank-change case 9).
- **FamilyStorageQueryLog** (vide) → **506 ClientFamilyStorageLog**
  (`Execute` @0x8259350, `Serialize` @0x81e82f0) :
  `[u16 count][count×{s16 log_type, str log}]` depuis le deque
  `SFamily+0xc8`. Source DB : `SELECT log_type, log, log_time FROM
  family_storage_log WHERE family_id=? ORDER BY log_time DESC
  LIMIT 50` (CFamilyDBO mission @0x81…).
- **FixEquipByFamily** (vide) : rate-limit 1 s par perso
  (`char+0x650/0x654` timestamps µs ; violation → texte 0x621),
  `GetItemRepairAllPrice`, puis délégation mission `AgentDBACommand
  "FamilyFixEquip Money = %d"` → réparation financée par la trésorerie
  (`fixequip_privilege`/`treasury` de `family`). Chez nous : débit
  direct `guilds.treasury` + `RepairAllAsync(apply:true)`.
- **FamilyTalkToNpc** `[u32 npcId]` : pose `CTalkOp` puis
  `CNC_ZMis_MissionServerListDialogue {u32 charId, u32 npcId,
  s16 mapId}` — même requête mission que le TalkToNpc générique.
- **Écriture du log** (`CLogFactory::FamilyStorageLog` mission
  @0x80708d0) : `log_type` ∈ {0 "Storage Insert", 1 "Delete",
  2 "Recycle"} ; `log` = `"<state>-ID:%d,ItemID:%hd,Dur:%hd,
  MaxDur:%hd,…"` (dump d'audit de l'item déplacé).

## Implémentation

1. `GuildService` : `Guild.Emblem` (colonne `guilds.emblem` — déjà
   lue par le repo mais ignorée au load), `GetMember(charId)`,
   compteur `GetFamilyRevision(charId)` (bump create/add/leave/rank),
   `TrySpendTreasuryAsync` (débit + persistance UpdateGuildAsync).
2. `FamilyBankRepository` : `AppendStorageLogAsync(familyId, type, log)`
   + `GetStorageLogAsync(familyId)` (SQL natif LIMIT 50 DESC).
3. `FamilyHandlers` :
   - UpdateFamily → 346 (vérif. cible en ligne via
     `Sessions.FindByCharacter`, envoi au demandeur).
   - StorageQueryLog → 506 depuis la table.
   - FixEquipByFamily → rate-limit 1 s (dict charId→ticks),
     coût RepairAll, trésorerie suffisante, débit persisté,
     réparation appliquée.
   - FamilyTalkToNpc → forward mission `SendListDialogueAsync`
     (npcId du wire, sans check de portée — le natif n'en fait pas).
4. `InventoryHandlers` put/get family storage → append log type
   0/1 au format natif (`"Storage Insert-ID:…,ItemID:…,Dur:…"`).
5. Tests + build + commit.

## État

- [x] `GuildService` : `Guild.Emblem`, `GetMember`, révision
      famille (bump create/add/leave/rank), `TrySpendTreasuryAsync`
- [x] `FamilyBankRepository` : `AppendStorageLogAsync` /
      `GetStorageLogAsync` (SQL natif, LIMIT 50 DESC)
- [x] `FamilyHandlers` : UpdateFamily 81→346, StorageQueryLog
      179→506, FixEquipByFamily (rate-limit 1 s + trésorerie +
      RepairAll), FamilyTalkToNpc 239 → dialogue mission
- [x] `InventoryHandlers` : log type 0/1 au format natif sur
      put/get coffre famille
- [x] Build 0 erreur / 0 avertissement
- [x] `FamilyHandlerTests` : 8/8 verts (révision, trésorerie,
      wire 346/506, log DB) — suite : 289 passants, 10 échecs
      = baseline pré-existante (zéro régression)
- [ ] Validation client live (référence + Reforged)

## Hors scope (lot ville/bâtiments ultérieur)
- `FamilyContainerPut/Get` : `CheckFamilyItemValid` / bâtiments de
  ville de famille (`family_building_container`, NPC placés x/y/dir).
- `FamilyTown*` (recherche/entrée/membres), `FamilyStatue`,
  `FamilyBuildingRepair`, `FamilyInstanceGetData` — requièrent le
  sous-système ville (S_FamilyTown, bâtiments, upgrades).
