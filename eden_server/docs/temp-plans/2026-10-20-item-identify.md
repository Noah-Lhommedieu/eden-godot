# Lot — Identification d'items native (NCID 65 → 364)

## Symptômes

- `ItemActionHandlers` NCID 65 : débite l'or mais ne marque **rien** —
  TODO « flag identifié + révéler stats ». L'item reste non identifié.
- `ItemEntity`/`NativeItemRow` ne portent pas `identify` (colonne
  native existante, default 1).
- Les codecs item n'émettent pas le bit d'identification.

## Preuves natives (désasm zone)

- `CNE_CZ_ZoneServerIdentifyItem::Execute` @0x825fb90 : wire
  `[u16 containerId][u16 loc][s16 ?]` — container doit être 0
  (inventaire), op identify NPC (type 0x2c) à portée.
- `CItemFactory::IdentifyItem` @0x809da40 :
  - non éligible (`itemdata+0x3c & 1` ou type lapis ≥ 2) → texte 0x5a45
  - déjà identifié (`clone+0x24 & 8`) → texte 0x5a43
  - or < coût (`max(1, SysPrice×0.05)` — `_DAT_087cc628`, pas
    param346) → texte 0x5a44
  - `ChangeEmbeddedAmount` (re-roll du nombre de sockets ouvertes)
  - `GainGold(-cost)`, `clone+0x24 |= 8` (bit 3 = identifié),
    `UpdateCloneItemAddAttr`, `ModifyItemIdentify(...,1)` (colonne
    `identify`), `ModifyItemEmbedded` si sockets roulées,
    `CCharacterDBO::FlushAll`, texte succès 0x5a46.
- `CNC_CZ_ClientIdentifyItem` (364) — corps 22 o, mappé sur le ctor :
  `[s16 containerType][s16 loc][u16 socketCount][6×s16
  embedded_id1-6][s16 containerIndex]`.
- `clone+0x24` = byte flags du record item : bits 0-2 = nb sockets,
  **bit 3 = identifié** (émis dans WriteItem/BuildPutItem).

## Implémentation

- `ItemEntity.Identify` (int, 1 par défaut = identifié — colonne
  native default 1) + `NativeItemRow` + `LiveSelect` + `ToEntity` +
  INSERT + `UpdateItemAsync` (identify=@Identify).
- Codec : bit3 du flag byte = `meta.Identify != 0` quand meta fourni
  (WriteItem + BuildPutItem — appelants passent déjà 0x08).
- `RestoreItemAsync` : propager `Identify` du snapshot.
- Handler 65 : checks natifs (éligible via `ItemIdentifyCost`,
  `item.Identify != 0` → 0x5a43, or → 0x5a44), débit, roll sockets
  si capables et aucune ouverte, persistance, 364 + texte 0x5a46.
- Textes via `CNC_CZ_ClientTextOutputIndex` (316).

## Vérification

- Tests : coût+débit, déjà identifié refusé, flag persisté, bit3 codec.
- Build 0 erreur ; suite sans régression.

## État — FAIT (2026-10-20)

- `ItemEntity.Identify` (1 par défaut) + `NativeItemRow` +
  `LiveSelect` + `ToEntity` + INSERT + `UpdateItemAsync`.
- `NativeItemCodec` (passé public) : bit3 du flag byte =
  `meta.Identify != 0` dans `WriteItem` et `BuildPutItem`.
- `RestoreItemAsync` : propage `Identify` du snapshot buyback.
- Handler NCID 65 réécrit : rejet container≠0, éligibilité via
  `ItemIdentifyCost` (texte 0x5a45), déjà identifié (0x5a43),
  or (0x5a44), débit, re-roll sockets si aucune ouverte
  (`InlayService.RollSockets` + `UpdateEmbeddedAsync`),
  `identify=1`, 364 (22 o mappé ctor) + texte 0x5a46.
- Tests : 4/4 `ItemIdentifyTests` (bit3 codec ×3 + propagation
  restore). Suite : 303 passés / 9 échecs = baseline inchangée.
