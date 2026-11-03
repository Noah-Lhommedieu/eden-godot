# Plan de lot — Sockets / Inlay (gemmes kind 45 → équipement)

## Contexte

Le système de sockets natif = **gemmes incrustées** via item-sur-item
(`CNE_CZ_ZoneServerItemUse` NCID 19, `UseItemToItemControl`, case
`GetTemplateType()==0x2d`). Le NCID 67 `InlayItem` est un **stub natif**
(retourne 0 — désactivé) ; `CDrillItem` (kind 0xf3) est un chemin mort :
aucun item kind 0xf3 dans S_Item.

## Preuves natives (zone/decompiled.c)

- `CNE_CZ_ZoneServerItemUse::Deserialize` : `[s16 contType][s16 contIdx]
  [s16 slot][i32 target][string u8len][s16 tType][s16 tIdx][s16 tLoc]`
  — les 3 s16 finaux = coordonnées de l'item cible.
- `CItemFactory::UseItemParam` : `itemdata+0x38` (S_Item **col12**) =
  use-type ; 3/5 → `UseItemToItemControl` (target coords stockées
  +0x38/+0x3a/+0x3c, socket choisi +0x3e).
- `UseItemToItemControl::CheckExecute` case **0x2d** (source kind 45) :
  - cible via `GetCloneItemFromContainer` (types 0=sac,1=équip,4,5)
  - `target.GetType()` ∈ {0=CWeapon,1=CArmor} sinon texte **0xc2**
    (194 "cannot be inlaid")
  - niveau gemme `CAddAttrItem+0x124` (itemdata+0x8c = **col35**) ≤
    niveau requis cible (`itemdata+0xfc+0x22`, ou niveau perso si
    `item+0x114` grow-equip) sinon texte **0xcce** (3278 "level too low")
  - `clone+0x24 & 7` (embedded_amount) > 0, scan `embedded[0..count)` :
    première valeur **0** = slot libre (stocké +0x3e), sinon **0xc3**
    (195 "No available slots")
- `UseItemToItemControl::UseItem` case 0x2d : `embedded[idx] =
  item_id` de la gemme → `UpdateCloneItemAddAttr` → texte **0xc1**
  (193 "inlaid") → `CItemDBO::ModifyItemEmbedded` (persiste
  embedded_amount + 6 ids dans la table du conteneur).
- `DoUseItemAfterCheck` : durabilité gemme −1 → 291
  `ClientModifyItemDurability`, stack épuisé → 275 `ClientDropItem`,
  puis ack 303 `ClientItemUse`. **Pas de 369** — le client met à jour
  son modèle local.
- `CItem::Clone` : `clone+0x24` bit3 = `lapis+0xf6` (socket-capable,
  posé à 1 dans les ctor CWeapon/CArmor — désasm @0x835c7xx/@0x835ccxx).
- `CItemFactory::ChangeEmbeddedAmount` @0x809baf0 : à la création,
  arme (GetType 0) toujours / armure seulement kinds 1-16 ;
  max=`lapis+0x12c|+0x126` (itemdata+0xf6 = **col72**), taux
  `+0x12d|+0x127` (itemdata+0xf7 = **col73**) ; lance count dés en
  séquence (stop au 1er échec), slots ouverts = embedded[i]=0,
  le reste reste −1 ; count clampé 7→6.
- `CDrillItem::CheckExecute` : exige bit3 + `max(lapis+0xf8) > count` +
  un slot −1 — chemin mort (pas de kind 0xf3 en data).
- Classes item (`GetType`) : CWeapon=0 kinds **17-34** (GetAllWeapons
  `kind-0x11<0x12`), CArmor=1 kinds **{1-16,37-40,78, 81@loc10/34}**
  (GetAllArmors), CAddAttrItem=6 kinds **{45,46,47,56,59,79}**.
- Wire 369 (`CNC_CZ_ClientModifyItemInfo::Serialize`/ctor) : `this[8]`
  = **bit3 socket-capable** (pas bind !), `this+0xa` = **embedded_amount
  &7** (pas rank !), puis 6×s16 embedded_id. → fix builder existant.
- Textes T_TextIndex : 193/194/195/3278 confirmés.

## Colonnes S_Item verrouillées (comptage Deserialize, 78 cols)

| col | itemdata | rôle |
|-----|----------|------|
| 8   | +0x20 | kind (déjà `Kind`) |
| 9   | +0x28 | equip loc / loc cible gemme (déjà `EquipLoc`) |
| 12  | +0x38 | **use-type** (dispatch UseItemParam) |
| 35  | +0x8c | **niveau de gemme** (CAddAttrItem+0x124) — ex. 30937 = 35 |
| 72  | +0xf6 | **max sockets** (byte) |
| 73  | +0xf7 | **taux ouverture socket** (byte, 30/50/70) |
| 75  | +0x100 | groupes cube (déjà `CubeGroupIds`) |

## Éditions — ✅ toutes faites

- [x] `ItemDef` : +`UseType` (col12), `MaxSocket` (col72), `SocketRate`
      (col73), `GemLevel` (col35, alias de `LearnSpellId` — même champ
      itemdata+0x8c partagé avec les livres de sorts)
- [x] `ItemEntity` + `NativeItemRow` + `LiveSelect` + `ToEntity` :
      `EmbeddedAmount`, `EmbeddedId1..6` (+ helpers Get/SetEmbeddedId,
      défaut −1 = socket scellée)
- [x] `InventoryRepository` : `UpdateEmbeddedAsync` (serial global →
      inventory1/2 + equipment1/2 + storage1/2), `GetEquipmentItemAsync`
      (type cible 1), `GetStorageItemAsync` (type 4), `GetItemFullRowAsync`
      étendu equipment/storage, `AddItemAsync` colonnes embedded ;
      `UpdateItemAsync` ne touche pas les colonnes embedded (vérifié)
- [x] `InlayService` (Eden.Core/Game/Inlay) : map kind→GetType natif,
      `RollSockets` (ChangeEmbeddedAmount), `ValidateInlay`,
      `ApplyInlay`, ids de textes natifs
- [x] `InventoryService.AddItemAsync` : roll des sockets à la création
      pour les defs éligibles (armes toujours, armures kinds 1-16)
- [x] `ItemActionHandlers` : branche `UseType ∈ {3,5}` → item-sur-item ;
      kind 45 → inlay (résolution cible cont 0/1/4 → validate →
      ApplyInlay → UpdateEmbeddedAsync → texte 0xc1/erreurs →
      refresh profil si équipé → 291/275 → ack 303)
- [x] `MagicBoxPackets.BuildModifyItemInfo` : u8 = socket-capable
      (param `IsSocketCapable(mainDef)`), s16 = embedded_amount &7 —
      signature + call site corrigés (était bind/rank, faux)
- [x] Tests : 15 cas `InlayServiceTests` — filtres de kinds, parse
      colonnes sur vraie data (30425 arme 6 sockets/70, 30937 gemme
      use=5 lvl=35), roll (tout-ouvert, stop 1er échec, gating
      armure 1-16, max=0), validation (type cible, niveau gemme,
      premier slot libre, scan stoppé par −1, grow-equip),
      apply, défauts entité
- [x] Build zéro erreur/warning + commit

## Vérifications

- `dotnet build` : 0 erreur, 0 warning.
- `dotnet test` : 15/15 tests inlay ; suite complète 238/248 — les
  10 échecs sont **pré-existants sur la baseline** (stash + re-test :
  même compte, aucune régression).

## Hors scope (documenté)

- `CDrillItem`/`DrillItemPlus` : chemin mort, aucun item kind 0xf3.
- NCID 67 `InlayItem` : stub natif (inlay shop désactivé).
- Kinds 56/59/79 (witchcraft) : écrivent `clone+0x4c` — système
  witchcraft séparé (suivi plus tard) ; kinds 46/47 (autres add-attr)
  ont leurs cases natifs propres non portés.
- Cible conteneur type 5 (2e coffre ?) : non mappé côté repo — ack
  neutre si reçu.
- Effets passifs des gemmes (S_Enchant 57484+ : exp/drop/résistances) :
  nécessite le framework "enchant passif d'équipement" (à part —
  contribution stats des gemmes actuelles = 0 dans S_Item).
