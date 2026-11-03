# Plan — OpenFortuneBag (sacs instantanés [49001,50001)∪[51001,53001)) + compteurs fortune

Suite de `2026-10-28-luckybag.md` (ChanceLuckyBag + push world→zone,
commit `671ae4c`). Cible : `CItemFactory::OpenFortuneBag` zone
@0x080bb300, compteurs `char+0x574`, requête client 223 → 557,
persistance `player_fortune_bag` via DBA mission.

## Preuves natives nouvelles (ce lot)

- **bagdef+4 = `item_counter`** (seuil pity, colonne DB) —
  world PGAccountDBO @0x0808baad `*(short*)(local_4e4+1) = local_1c`.
- **bagdef+0x1c = nombre de sets** (slots vides requis — un lot par
  set ; rb-tree size de la map set).
- **holder+0x1c = nb de lots du set** (rb-tree size du pool).
- **prize+4 = cum basse** (avant ce lot), **prize+8 = cum haute**
  (clé map) — wire f11 = +4, f12 = +8 (≠ proba brute).
- `pool+0` = range du dé = cum totale ; set==99 → dé ∈ [0,100)
  (`DAT_087cc650`=1e6, `×1e-4`) ; sinon dé ∈ [0,range) (`×1e4`/`×1e-4`) ;
  `range==0 → dice=0` (`_DAT_087c2728`=0.0).
- Sélection = **upper_bound** (1ère clé > dice) ; aucune clé > dice :
  set≠99 → log « Invalid » + return 0 ; set==99 → set sauté.
- **Pity** (par set, si `item_counter>=1 && counter>=seuil &&
  !bulletinDrawn`) : map temporaire pondérée par (cumH-cumB = proba)
  sur les lots `bulletin==1` ; aucun lot bulletin → retombe au tirage
  normal ; dice ∈ [0,Σpoids) → upper_bound.
- Octroi par set : `SearchItem` merge / `AddCloneItemWorld` +
  `PutIntoContainer` (notre `AddItemAsync` couvre) ; bind =
  `srcItem.bind(bit5) || prize.bind>0` ; texte **0x6b1** `"%s x %d"`.
- `bulletin` → DBA `BroadCastScreenMessage Type= 8` (0x69e, noms
  joueur/sac/lot) + `HandleRAMarquee(1)` ; `bVar4=true` → pity unique.
- Fin : `item_counter>=1` → `counter = bulletinDrawn ? 0 : counter+1` ;
  écrit `char+0x574` ; DBA `FortuneBag Item= %hd Counter= %hd`
  (mission : UPSERT `player_fortune_bag` par `account_id`,
  @mission 0x081f8fxx) ; **557** `[u16 n][u16 bag][u16 counter]×n`
  (1 entrée ici — Serialize @0x080c3bd0).
- **223** `CNE_CZ_ZoneServerQueryFortuneBagCounter` @0x08277f00 :
  557 avec toutes les entrées de char+0x574.
- `param_3=true` → validation seule (slots) ; notre usage = false.
- `embedded` = 0 dans toute la data officielle (champ transporté).

## Implémentation

- [x] `FortuneBags.cs` : corriger `RecordsFromRows` (Field2=
  `item_counter`, RequiredSlots=nb sets, HolderField=nb lots/set,
  f11=cum basse, f12=cum haute) ; `FortuneBagDef.Field2`→
  `ItemCounter` ; `FortunePrize.Probability` = poids (f12−f11) ;
  commentaires corrigés (bagdef+0x1c = nb sets, holder+0x1c = nb lots).
- [x] `EntryState` : `FortuneCounters` map<bagId, count> (char+0x574).
- [x] `FortuneBagRepository` (Eden.Core) : `GetCountersAsync(accountId)`
  + `SetCounterAsync` (UPSERT on unique (account_id,item_id)).
- [x] `ZoneEntryFlow.FortuneBag.cs` : `OpenFortuneBagAsync`
  (verrous/lookup/slots/boucle sets pity+roll/grant/0x6b1/bulletin/
  compteurs/DBA/557) + `HandleFortuneCounterQueryAsync` (223→557).
- [x] Item-use : plage [49001,50001)∪[51001,53001) → callback
  `FortuneBagUse` (bind de l'item source) dans ItemActionHandlers +
  wiring `ZoneEntryFlow.cs` ; interception 223.
- [x] `Lifecycle` : charge les compteurs après `st.AccountId`.
- [x] Mission : `case "FortuneBag"` dans `OnDbaCommandAsync` +
  `MissionDb` UPSERT (charUid→account_id via player_characters).
- [x] Tests : `RecordsFromRows` champs corrigés, roll/pity purs,
  codec 557 ; build 0 warn ; suite complète (805/805).

## Approximations restantes (à noter)

- Durabilité aléatoire des lots non-stackables : le natif lance
  `random()%(max−min)+min+1` depuis les champs itemdef +0x8c/+0x90
  (format hi.lo) — notre AddItemAsync pose max d'office. Pas de lot
  équipement à plage variable dans la data officielle → chemin mort.
- Flag `embedded` (prize+0x10) transporté mais 0 partout en data —
  pas d'effet (natif poserait l'item flag 7).
