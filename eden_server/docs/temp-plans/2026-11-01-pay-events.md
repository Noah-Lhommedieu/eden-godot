# Lot : NCID 253 PayEventSelect (système pay-events complet)

Date : 2026-11-01. Suite du lot "NCIDs dépendant de sous-systèmes".

## Preuve native (zone server_us)

- `CNE_CZ_ZoneServerPayEventSelect::Deserialize` @0x0826e6b0 :
  `[u32 event_id][s16 choose]` = 6 octets.
- `Execute` @0x0826e530 : session→char type 7 →
  `CPayManager::TriggerPayEvent(char, event_id, choose)` @0x080eb510.
- `TriggerPayEvent` :
  1. Rate-limit : timestamp µs char+0x650/0x654, exige > 1 s depuis le
     dernier trigger (sinon return silencieux).
  2. Op courante char+0x670 IsA==0x2c → seul un DebugLog si absente
     (PAS un gate bloquant).
  3. `event_id > taille map` → return ; GameData vcall+0x1cc =
     `QueryPaySystem(id)` NULL → return ; cmd NULL → return.
  4. `CheckCondition` ko → texte 0x1e67 puis 604.
  5. `CheckAward`/`CheckChooseItem` ko → texte 0x1e65 puis 604
     (0x4f9 sac-plein émis EN PLUS par les checks eux-mêmes).
  6. 4 items de coût (`def+0x14` sPayItems, entrées 0..3, id u32@+0,
     count s16@+8) : `CheckItemNum` par id — manquant → return
     silencieux ; `DropItemByItemID(char,0,id,count,0x49)`.
  7. `ExecAwardCmd` puis 604 `CNC_CZ_ClientPayEventOK` (corps vide)
     puis texte 0x1e66.
- `S_PaySystem.ini` (db_decoded, `|V.1|18|`, 872 records) →
  `CPaySystemData` (Deserialize @0x8616ff0) :
  col0 id u32 | col1 type i32 | col2 nom | col3 desc | col4 cond `#:`
  | cols5-16 = 4×{item_id i32, nom str, count s16} | col17 award `#:`.
- `LoadAllPayEventCmd` @0x080ebbf0 (appelé `InitWorld`) →
  `ConvertCmd(def+0x10 cond, def+0x20 award, cmd)` @0x080ea500 :
  split `:` des deux strings (index0 `#` ignoré), ops en mots.
- `CPayEventCmd` (0x84) :
  - +0x00 vec<{s16 classId,s16 op,s16 lvl}> `If_CharClassLevel` (op:
    ">="→1, "="→2, "<="→3 sinon 0 — 4 tokens)
  - +0x0c vec<pair<s16,s16>> `If_CharLevel min max` (3 tokens,
    0=non borné, 0/0=ko)
  - +0x18 vec<{s16 repId,i32 lo,i32 hi}> `If_CharReputation` (4 tokens)
  - +0x24 vec<int> `GiveGold n` — ExecAwardCmd : FamilyLevyTaxes(n)
    (10% à la trésorerie famille, retour IGNORÉ → le joueur garde n)
    + GainGold(n)
  - +0x30 vec<pair<int,s16>> `GiveItem id n` — AddItemFromPaySystem
  - +0x3c vec<pair<int,int>> `ChooseItem id n` — index choisi du
    client (choose==-1 → liste vide requise sinon texte 0x6e2)
  - +0x48 vec<int> `CharExp n` — GainExp
  - +0x54 vec<int> `ClassPoint n` — GainClassPoint + GainNp (même n)
  - +0x60 vec<pair<s16,int>> `Reputation id +/- n` — AddPrestigeExp →
    DBA `"GainExp Prestige %hd = %d"` (CPlayerFactory::AddPrestigeExp
    @0x80xx — CDBAgent::Command vers mission)
  - +0x6c vec<pair<int,s16>> `GiveBuff id dur` — EffectToLife :
    dur==0 → reste -1 flag 0x2000 ; dur==-1 → -2 (permanent) flag
    0x2020 ; sinon dur*10 flag 0x2000
  - +0x78 vec<pair<u16,int>> `SpecialCoin type +/- n` (type<12,
    6-8 exclus) — DBA `"GMGainSpecialCoin Type = %hd Money = %d"`
- `CheckCondition` : char level (vcall+0x124) dans [lo,hi] par u32
  (lo=min, hi=max, 0=non borné) ; class cond : classId == classe
  courante (vcall+0x1d8) ET char+0x470 (class lv) op val ; rep :
  GetPrestigeExp(repId) dans [lo,hi].
- `CheckAward` : pour chaque GiveItem id≠0 : non-stackable
  (SItem+0x76<2) → +1 slot requis ; stackable sans stack pouvant
  absorber (GetContainerIndexByStackItem==-1) → +1 ; total >
  GetEmptySlotNum → texte 0x4f9 + false.
- `CheckChooseItem` : choose==-1 → si liste non vide texte 0x6e2+0 ;
  choose >= taille → 0 ; item 0 ou def absente → 1 ; sinon même
  calcul de slot que CheckAward (1 slot si pas de stack absorbant)
  → 0x4f9 si insuffisant.
- 254 `PayEventResponse` : Execute @0x0826e110 = tail-call vfunc+8
  (GetType) ignoré → no-op fidèle, stub conservé.
- 602/603 (ClientOpenPaySystem/ClientPayEvent) : corps vides, JAMAIS
  émis par la zone — la fenêtre s'ouvre côté client via les dialogues
  NPC (client a T_PaySystem.ini + PaySystemWnd.layout).
- Mission : `TC_GainPrestigeExp` gère "GainExp Prestige <n> = <n>"
  (strings 084e84a8) → CGlobalInfo::GainPrestigeExp (upsert prestige,
  clamp [0, Formula+0x5a4], level=1 si 0, SQL +
  CNC_MW_WorldServerUpdatePrestigeData).

## Plan

1. [x] `Eden.Core/Data/Parsers/PaySystemParser.cs` : `PayEventDef`
   {Id, Type, Name, Desc, CondCmd, CostItems[4]{ItemId,Name,Count},
   AwardCmd} via IniTable 18 col.
2. [x] `Eden.Core/Game/PayEventCmd.cs` : modèle + `PayEventCmdParser`
   (ConvertCmd : split ':', skip '#', dispatch ops par nom+arité).
3. [x] `GameDataService` : `PayEvents` (dict id→def avec Cmd préparsé)
   + chargement `S_PaySystem.ini` + fail-loud.
4. [x] `EntryState.LastPayEventAt` (rate-limit 1 s, char+0x650 natif).
5. [x] `ZoneEntryFlow.PayEvent.cs` : `HandlePayEventSelectAsync` —
   gates, CheckCondition (level/class/prestige via repo),
   CheckAward (slots libres), CheckChooseItem, consommation coûts
   (multi-stack + 275), ExecAwardCmd (gold+taxe famille, items,
   choose, exp, classpoint+NP via GrantClassPointAsync, reputation
   via AgentDBA `GainExp Prestige`, buffs via enchant, specialcoin
   via AgentDBA `GMGainSpecialCoin`), 604 + textes.
6. [x] Dispatch case 253 dans TryHandleOnLoopAsync + commentaire stub
   MiscHandlers.
7. [x] `MissionServer` : `case "GainExp"` (`Prestige <id> = <exp>` →
   AddPrestigeExpAsync — clamp omis comme quest-engine existant).
8. [x] Repo : `GetPrestigeExpAsync(charId, repId)` côté zone
   (table `prestige`, fndb1).
9. [x] Tests : parser S_PaySystem (18 col + DSL) — fixtures réelles.
10. [x] Build 0/0 + suite complète + commit.

## Hors scope (documenté)

- 254 no-op fidèle (désasm). 602/603 non émis (fenêtre client-side).
- 255 EvaluateLotteryOpen : sous-système CEvaluate (score activité +
  S_RewardScore) — lot suivant.
- 231 PVPRankLookEquip, 220 RecruitInstance, 163 NodeGroupReset :
  sous-systèmes encore absents (rangs PVP, board recrutement mission,
  CNodeGroup).
