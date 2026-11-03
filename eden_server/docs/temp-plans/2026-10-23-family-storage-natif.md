# P14.2b — coffre de famille natif (family_storage, 3 coffres)

## État constaté

- Les handlers zone 177/178 existent mais :
  - **mauvais mapping des champs** : le paquet natif est
    `[s16 storage_index][s16 bank_loc][s16 inv_bag][s16 inv_loc]`
    (désasm `CNE_CZ_ZoneServerFamilyStoragePutItem::Execute` zone
    @0x08258a00 + `FamilyStorageInsertItemRequest index= loc=
    inv_index= inv_loc=`). Notre code lit (bag,slot)=(F1,F2) et traite
    F3/F4 comme bankSlot/quantité — à l'envers.
  - **aucune quantité dans le paquet** : le natif déplace toute la
    ligne (stack entier).
  - Table `family_bank` maison (colonnes simplifiées) au lieu de la
    table native `family_storage` (items complets, `container_index` =
    index du coffre, `loc` = slot).
  - Aucun push de rafraîchissement vers le client.
  - `FamilyBankCapacity=100` vs cap natif **40 par coffre**.
  - Aucun gate privilège/niveau.

## Sémantique native (preuves)

Zone `CNE_CZ_ZoneServerFamilyStoragePutItem::Execute` @0x08258a00 :
- Requiert l'op storage ouverte (CFamilyStorageOp, type 7).
- Item à (inv_index, inv_loc) : `itemdata+0xf2 & 1` → erreur 0x20a3 ;
  `clone+0x24 & 0x10` ou `itemdata+0xf0 & 8|0x10` → erreur 0x7eb.
  `+0xf0`/`+0xf2` = **S_Item cols 69/70** (u16 consécutifs dans
  `CItemQuery::Deserialize` @0x8603xxx, run +0xec..+0xf4 = cols 67-71
  suivi de +0xf6=col72, +0xf7=col73, +0xf8=col74).
- Coffre index 1 → family level ≥5 ; index 2 → ≥6 (zone-side).
- Cap 0x28 = **40 items par coffre** ; `bank_loc==-1` → premier slot
  libre 0..39, sinon le slot doit être libre.

Mission `TC_FamilyStorageInsertItemRequest` @0x081f43c0 :
- `CFamily::CheckPrivilige(fam, char, index + 0xb)` → priv ids
  11/12/13 (`storage01/02/03_privilege`) — échec → code 900.
  `CheckPrivilige` @mission : priv 9-12 → level≥3, 12 → ≥5, 13 → ≥6
  → coffre 0 exige **family level ≥3**, 1 → ≥5, 2 → ≥6.
- Le coffre doit exister (map `family+0x1a0`), cap 40.

Push natifs (zone → client), émis à **tous les membres famille en
ligne** (`family+0x94`, `GetCharacterZone`) depuis
`CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute` @0x083188e0 :
- 505 `ClientFamilyStoragePutItem` : `[s16 index][s16 loc]` + record
  item fixe (pas de mask u24) — sérialiseur @0x8200600.
- 504 `ClientFamilyStorageDeleteItem` : `[s16 index][s16 loc]`.
- 506 `ClientFamilyStorageLog` : `[u16 n]{[s16 type][str log]}`.
- Entrée coffre (dialogue `family_storage`, OpenShop @0x08579440) :
  507 `ClientEnterFamilyStorage [i32 npcUid]` puis **508 par coffre
  non vide** : `[s16 0][s16 index][u16 n]{[s16 loc][record]}[s16 0]`
  — `+4` explicitement mis à 0 (`*(piVar8+1)=0`).

Record item fixe (505/508, désasm Serialize) :
`[u16 tid][s16 dur][s16 maxdur][str maker][u8 flags][6×s16 embedded]
[s16 emb_amount][u32 create_time][u16 +0x1c][s16 combo_id(+0x10)]
[9×u16 ec1-9][s16 strengthen][s16 +0x2e][i64 due_date]
[u16 t_lvl][u32 t_exp][u16 s1t][u16 s1l][u16 s2t][u16 s2l][u16 s3t]
[u16 s3l][u16 ec10][u16 ec11][u16 gold_ench][3×s16 mb_ench]
[u16 mb_energy][u16 mb_star]`

## Implémentation

- [x] `ItemParser` : `Col69Flags`/`Col70Flags` (cols 69/70 →
      itemdata+0xf0/+0xf2).
- [x] `FamilyBankRepository` : réécrit sur `family_storage`
      (container_index 0-2, loc 0-39) — rows complètes ItemEntity,
      id réutilisé (espace sérial global). Log conservé.
- [x] `FamilyStoragePackets` (Eden.Core/Game/Family) : builders
      504/505/506/508 + writer de record fixe.
- [x] `InventoryHandlers` 177/178 : mapping corrigé, gates
      priv/index/level/cap/flags item, déplacement de la ligne entière,
      push 505/504+506 aux membres en ligne (`s.Sessions.FindByCharacter`),
      notify inventaire de l'acteur.
- [x] Dialogue case 19 : 507 + 508 par coffre non vide.
- [x] `known_incomplete.json` : marquer 505/508 (codecs générés aplatis).
- [x] Tests : layouts 504/505/508, gate priv, cap 40, flags deny.
- [x] Build 0 warning + suite complète + matrice régénérée.
