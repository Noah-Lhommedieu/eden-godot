# 2026-10-23 — Système « binded item »

## Preuves natives (zone/decompiled.c)

- `CPlayerFactory::BindItem` @0x08116870 : gate `def+0x30` sign bit
  (S_Item col11 `Flags & 0x80`) + clone `+0x24 & 0x10 == 0` →
  `clone+0x24 |= 0x10` + `CItemDBO::SetItemBind` (UPDATE bind=1).
  Ne s'applique qu'au conteneur « inventory » (param_2==0).
- `Equip` @0x080b5810 : à l'équipement joueur, `local_38[0xc]` =
  `def+0x30` — `(char)flags < 0` && non lié → `|= 0x10`.
- `ElfEquip` @0x0808cb20 : idem pour l'équipement posé sur un elf.
- `DoUseItem`/`DoUseItemAfterCheck` @0x080b8a10/0x080b8340 : appels
  `BindItem` après application de l'effet d'usage.
- `DoItemShift` @0x080a4380 : item-on-item dont l'enchant d'usage
  (def+0x60) porte la commande 0x1f57 (8023, S_ItemMallEnchant) →
  bind au lieu d'eradicate.
- `CheckAutoStackItem` @0x08090410 :
  - merge de stacks : `(flagsA ^ flagsB) & 0x10` → refus (lié +
    non-lié ne fusionnent pas) — déjà couvert (`IsBound` comparé).
  - déplacement vers conteneurs 1/5/7 → auto-bind si Flags&0x80.
- `AddItemToTrade` @0x08122260 : `clone+0x24 & 0x10` → return 0
  (TradeAddItemFail) — déjà couvert (`inv.IsBound`).
- Vente PNJ `& 0x30` — déjà couvert (ShopHandlers:209).
- Famille storage `& 0x10` → 0x67f — déjà couvert.
- Mail pièce jointe — déjà couvert (SocialHandlers).
- DropItem natif : AUCUN check de bind — la destruction d'un item lié
  est permise. Ne pas bloquer.

## Manquants à coder

- [ ] `ItemParser.ItemDef.Bindable` = `Flags & 0x80` (documenté).
- [ ] `InventoryRepository.SetItemBindAsync(itemUid)` = SetItemBind
      par serial global (inventory1/2 + equipment1/2).
- [ ] Équipement (SwitchItem case 0→1) : après MoveToEquipmentAsync
      OK → bind si Flags&0x80 && !IsBound (avant EmitChangeEquipAsync
      pour que le 277 reflète le flag).
- [ ] Usage (ConsumeUsedItemAsync) : stack résiduel → bind si
      Flags&0x80 && !IsBound.
- [ ] Équipement de sac (EquipBagAsync, conteneur 5) : bind si
      Flags&0x80 — couvre CheckAutoStackItem type 5.

## Non objectifs

- Commande enchant 8023 (item-on-item bind) : surface rare
  (S_ItemMallEnchant), reporter.
- Lottery/luckybag bind : déjà géré (lottery_type 3 → isBound).
