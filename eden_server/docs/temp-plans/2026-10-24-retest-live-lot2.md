# Retests live lot 2 — retours joueur Reforged

> Rappel méthodologie : headless = preuve **côté serveur** seulement.
> Le constat joueur est visuel — un bug remonté peut être serveur,
> client, ou les deux. Toujours vérifier les deux couches.
> (Aussi inscrit dans `server/AGENTS.md` — section « Validation
> headless vs client réel ».)

## Retours joueur (constat visuel Reforged)

- [~] AoE : sorts de zone sur cible — splash implémenté pour shapes
      2/5/6 (direction/croix/secteur à la position de la cible,
      `CastToLife` @0x081545a0) ; shape 1 = ToLocation via NCID 16
      (au sol) ; rayon = col30×0.5 (`SSpellData+0x48`). **RETEST REQUIS**
      — si encore KO : capture du NCID réellement émis par Reforged.
- [~] Monture strafe 90° : gate client `+0xe3&0x40` NOPé
      (`0x75dbd6` → `90 90` vérifié sur disque) — SetDir s'exécute
      désormais monté. **RETEST REQUIS** — si KO : le facing est
      probablement réécrit ailleurs (réseau 326/441 ou ancre monture).
- [x] Talents stats non appliquées : le natif applique l'enchant
      **1× par point investi** (`InvokeAAEnchants` @0x8417830) →
      stage pipeline « talents » = `args × points`, filtré aux 3
      branches de la classe/race courantes ; les ids de talent sont
      exclus du stage « buffs » (anti double-compte).
- [x] Talents en liste de status : `IsHideIcon` @0x855efa0 =
      `SEffectData+0x118&2` OU `CEnchant+0x28 bit 0x1000` — l'AA est
      appliqué avec flags=0x1000 (`AddAAEffectPoint` @0x841ebb0) →
      `AddPlayerEnchantAsync(hidden:true)` : pas de 299/broadcast,
      pas de ligne `player_enchants`.
- [x] Memory Tome : enchant 55149 = cmd 6003 `ResetAA`
      (`CEC_ResetAA` @0x0854bad0 → `CCharacter::ResetAA` @0x0841fca0)
      — refund `AARequestTableTotal[point]` par effet de la classe
      courante, retrait enchants (silencieux, icônes cachées),
      delete `advanced_ability`, 471 UpdateType=1 + attr 0x31.
- [~] Pets : `SeeMob+0x50` isEnemy=0 + `+0x48` = owner charId
      (`CMonster::OnSeen` @0x0849e100) ; ré-invocation = rappel/dédup
      ; immunité mêlée/cast/AoE ; suivi 3 u/goal+1 u ; combat =
      cibles en combat contre le owner (pas la simple sélection).
      **RETEST REQUIS** (couleur, sélection, doublons, follow).
- [~] Combat : micro-stop cast-en-mouvement — le 278
      `ClientSpellCast` était émis AU LANCEUR avec `st.PosX/Z` serveur
      en retard → ré-ancrage client. Natif `NotifyCastToOthers`
      @0x84ee500 → `BroadCastToCanSee(entity, cmd, false)` exclut le
      lanceur → 278 maintenant relayé aux témoins seulement (il
      n'était JAMAIS montré aux autres avant). Client : les CALLs
      de reset `FUN_0075ad10` (`0x75AC17`, `0x75DCB2`) restaurés
      depuis le backup — les NOP cassaient l'état de file.
      **Caméra bloquée/saccadée** : probablement la séquence 302 —
      comparaison capture native requise, non traitée ici.
- [x] Charmes EXP : 2911 `ItemExpRateUp` var2/var4 (paires
      chance|taux : var1/2=EXP, var3/4=NP/CP) + 2912/2914
      `ItemWakenNpRateUp` → enchant temporisé réel (durée = S_Item
      cols 24/25, table 2=min/3=h/4=j), bonus sommé sur les statuts
      actifs dans `GrantCharExpAsync`/`GrantClassPointAsync` —
      `AddPlayerEnchantAsync` + tick d'expiration existant.
- [x] Bind monture : bit bind natif = `0x10` (clone+0x24,
      `FastDressDoItemBind` @0x080b9d60) — corrigé de 0x40 ; +
      **369 mode 8** `[s16 8][u16 contType][s16 contIdx][u16 slot]
      [u8 bind]` — paquet dédié émis à l'usage (le record mode -1 ne
      porte PAS le flag → l'item restait « unbound » visuellement).
      Idem déliement (bind=0) et stacks résiduels.
- [x] Vente NPC : **cause racine trouvée** — `item.Quantity` =
      `durability` (durabilité 100 pour l'équipement) → vendre 1
      équipement donnait `left=99` → on émettait 291 (update count)
      au lieu de 275 (drop slot) → l'item restait affiché. Fix :
      `stackable` = MaxStack>1 && !EquipLoc>0 (même règle que
      `RemoveItemAsync`) → `left=0` → 275. Format 275 natif vérifié
      par désasm (`SellItems::Execute` @0x08287fe2 : `[s16 0]
      [s16 loc][s16 contIdx]`) — identique au nôtre. Vente partielle
      291 Field4 = MaxDurability ; ordre 280 avant 309.

## À valider en jeu par le joueur (non prouvable headless)

- [ ] AoE ciblée tape plusieurs mobs autour de la cible
- [ ] Strafe monture : le perso pivote ~90° vers la direction
- [ ] Stats talents visibles sur la fiche perso (×points) + plus
      d'icônes talents dans la liste des status
- [ ] Memory Tome reset l'arbre courant + refund les points
- [ ] Pets : bleu/non-sélectionnable, 1 seul pet, follow passif,
      actif tape les mobs en combat avec le joueur
- [ ] Cast en mouvement : plus de micro-stop ; caméra combat
- [ ] Charmes : tooltip % effectivement appliqué sur l'exp
- [ ] Monture bindée affiche « already binded » tout de suite
- [ ] Vente NPC : items disparaissent de l'inventaire (équipement,
      stack complet, stack partiel, sell-all)

## Travaux restants

- [ ] Caméra combat saccadée — séquence 302 : capturer natif vs
      notre émission (sel=4/sel=0, act) avant tout changement
- [ ] Buyback list packet 512 après vente (fidélité native)
- [ ] Pet mode bleu/rouge : champ du SeeMob ou enchant reflétant le
      mode passif/actif si le rouge persiste après isEnemy=0
- [ ] EXP Waken CP : point d'application du bonus (GrantClassPoint
      couvre CP courant — chemin éveil à identifier)
