# 2026-10-23 — Crash capacité sac (déséquipement login, achat NPC, loot)

## Cause racine (PREUVE LIVE)

Trace `EDEN_NET_DEBUG` `/tmp/eden-zone.log` — 3 crashs, tous après un
déséquipement d'item chargé au login :

```
15:47:53  NCID=7 in  01 05 → 277 dstLoc=36 → 282 → reset
15:48:20  NCID=7 in  01 04 → 277 dstLoc=37 → 282 → reset
15:48:50  NCID=7 in  01 07 → 277 dstLoc=38 → 282 → reset
15:49:21  NCID=7 in  01 07 → 277 dstLoc=3  → OK (item équipé en session)
```

- `inventory_slots` n'a AUCUNE ligne pour test3 → `GetBagCapacity` = null
- `FirstFreeSlotAsync` : `cap = GetBagCapacity(...) ?? 64` → slots 20-63
- `GetAvailableSlotAsync` : `generate_series(0, 63)` — ignore la capacité
- Le 312 envoie bag0 = **20 slots** (GetAllBagCapacitiesAsync fallback 20)
- → tout ajout au-delà du slot 19 produit un 277/274 avec `loc >= 20`
  → `IItemContainer::PutIn` client hors limites → crash
  (game_.exe — copy-ctor clone sur slot absent, dumps `.rpt` 0x74dc0b)

DB live : test3 a **41 items en bag0 locs 0-40** (hors capacité 20).
Les items session-équipés marchent car ils repartent en bas slot libre (<20).

## Corrections

- [x] `GetAvailableSlotAsync` borné par la capacité réelle du sac
      (GetBagCapacity ?? (bag==0?20:0)) — plus de slots hors limite.
- [x] `GetAnyAvailableSlotAsync(charId)` : itère les sacs 0-4 ouverts
      (capacité > 0) — natif `IItemContainer::GetEmptySlot` /
      `CInventory::GetContainerWithEmptySlot`.
- [x] `AddItemAsync` : quand le sac préféré est plein, déborde sur les
      autres sacs ouverts (au lieu de "Inventory full" prématuré) +
      merge stacks dans TOUS les sacs (natif CheckAutoStackItem).
- [x] `FirstFreeSlotAsync` : fallback `bag==0 ? 20 : 0` (jamais 64).
- [x] `SwitchItemAsync` (1,0) : `dstIdx=-1` = « n'importe quel sac » —
      résoudre le premier sac ouvert avec slot libre (natif) et écho
      du bon dstIdx dans le 277. Idem MoveEquipToFreeBagAsync.
- [x] Call-sites Vending/Cube/LifeSkill/Work/Lottery → multi-sacs.
- [x] Réparation DB : inventory_slots (bag0=20, bag1=30) +
      sac 30 slots équipé (item 39356, holder 0 loc 1) pour
      test1/2/3 + 39 items relocalisés bag0→bag1.
- [x] Build Release 0 err/0 warn.
- [ ] Tests + redéploiement stack native.

## À retester en jeu

- Déséquiper un item équipé au login → pas de crash, item visible sac.
- Équiper/déséquiper en session → stats bougent.
- Achat NPC → pas de crash, item visible.
- Kill mob avec loot → pas de crash.
- Bag overflow → l'item part dans le sac d'extension (onglet 2).
