# Lot — NCID 231 PVPRankLookEquip + fix routage broadcasts ZMis

## Bug découvert (live)

`ForwardMissionPacketAsync` (ZoneEntryFlow.Mission.cs) lit
`charId = u32(body[0..4])` et droppe tout paquet sans session —
mais les **broadcasts ZMis n'ont pas de charUid**. Log live :

```
[Zone-M] ncid=763 char#196612 sans session   ×8
```

→ Le 763 (classements) et le 779 (registre rooms) sont **droppés**
sauf coïncidence : fenêtre de classement vide en jeu. Fix :
intercepter les NCIDs broadcast (763, 779) AVANT le gate charUid,
comme les cas 58/20 déjà en place.

## NCID 231 — preuves natives

- `CNE_CZ_ZoneServerPVPRankLookEquip::Deserialize` @0x0820c?? :
  `[s16 type][str name]`.
- Execute @0x0826edb0 : char du type valide (vfunc+8==7), lookup
  `CZoneServer+0x61c` = `map<s16 type, vector<CCharacter*>>` (chars
  virtuels des rangés), trouve par nom (char+0x324), émet 571.
- Alimentation : `CNE_ZMis_ZoneServerPVPTopData` (ZMis 182) —
  la mission pousse par type les `CExecutionerData` + items des
  **top 3** de chaque rang PVP (`PVPTopIDs` rempli dans
  `UpdatePVPRankInfo` mission @0x0819f4xx, `min(3,count)`), chargés
  via `CExecutionerDBO` (GetExecutionerData + LoadEquipment +
  LoadPlayerCard), `BroadCastToZoneServer` (mission @0x08083b40).
- **Divergence interne assumée** : notre `RankSet` (cache 763)
  porte déjà `Cols[4]` = +0x14 = player_id pour les types PVP —
  la zone résout le perso depuis la DB au moment du 231 (données
  fraîches, pas de snapshot 182). Wire 182 non porté (interne,
  invisible au client).
- Types PVP (PVPTopIDs) : 301-306, 320-322, 340, 360, 361,
  400-420, 500 — remplis dans `LoadPvpRanksAsync` avec
  `F[4]`=player_id (PvpRow).

## Wire 571 `CNC_CZ_ClientPVPRankLookEquip` (Serialize @0x08205af0)

```
[i32 uid][str name]
[u16 class_id(+0x47c byte)][u16 char_lv(+0x470)]
[u16 class_lv(+0x47e)][u16 hero_feature(GetHeroFeature &0xff)]
[i32 score = Σ T(class_lv) sur la classlist (hashtable char+0xad8)]
[u8  page d'équipement courante (char+0xd9c[d98*0x14+4])]
[32B apparence (char+0x48e..+0x4aa = appearance_data)]
[u16 n][n×s16 dur(-ish) par slot]   (vec +0x3c)
[u16 n][n×s16 paire par slot]      (vec +0x48)
[i32 maskSlots1][items conteneur ≤0 — codec clone complet]
[i32 maskSlots2][items conteneur 1 — codec light]
```

Clone complet (chaîne-encodé vs baseline -1/0, mask u24 patché) :
`[u16 itemId][u8 flags&7][u16 len+str]` puis mask u24 :
- bits0-5 : 6×s16 +0x40 (embedded, sentinelles -1)
- bit6 : s16 +0x4c (amount, sent. 0)
- bit7 : s16 +0x10 (combo)
- bit8 : u16 +0x1c
- bits9-17 : 9×u16 +0x34[0..8]
- bit18 : u16 +0x2c (strengthen)
- bit19 : s16 +0x2e
- bit20 : i64 +0x50 (due_date)
queue fixe 34B : `[u16 +0x58][u32 +0x5c][u16 +0x60][u16 +0x66]
[u16 +0x62][u16 +0x68][u16 +0x64][u16 +0x6a][u16 +0x34[9]]
[u16 +0x34[10]][u16 +0x6c][3×s16 +0x70][u16 +0x7c][u16 +0x7e]`.

Light (set 2) : `[u16 itemId][i64 +0x50][3×s16 +0x70][u16 +0x7c]
[u16 +0x7e]` = 16B/item.

Vecs s16 parallèles : par slot conteneur, `clone+0xc`/`clone+0xe`
(0 si slot vide) ; maskSlots1 = bits des slots remplis.

## Implémentation

- [x] Analyse native complète.
- [x] Fix routage : intercept 763/779 avant gate charUid.
- [x] `NativeItemCodec.WriteEquipViewItem` (mask u24 chaîné +
      queue 34B partagée `WriteItemTail`, sans create_time) +
      `WriteLightItem` (20B : id + i64 due + 3×s16 mb + energy + star).
- [x] `PvpRankEquipPackets.Build571` (codec dédié `Eden.Zone/Codecs`).
- [x] `HandlePvpRankLookEquipAsync` dans ZoneEntryFlow (case 231) :
      type PVP → index nom dans RankSet.Names → requis index<3 et
      `Cols[4][i]` = pid → charge perso + classlist + équipement
      (conteneur 0 → records chaînés, conteneur 1 → light) → 571.
      Repo : `GetEquipmentEntitiesAsync(pid, container)`.
- [x] Stub MiscHandlers 231 → commentaire intercepté.
- [x] Tests : codec mask chaîné + wire 571 (2 verts, 917/917 suite).
- [x] Build 0/0 + suite + commit.
