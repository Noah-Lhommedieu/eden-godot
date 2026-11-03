# Lot 8 prompt8 — notes d'exécution G5-G7 (spawn/monde)

## Preuves natives

- `re/ghidra/zone/decompiled.c` `GetAllRebornMonsterInfo` @0x083688a0 :
  ligne [reborn_monsters] ignorée si `monster+0xb8 != 0 && ((mask >>
  (Locate&0x1f)) & 1) == 0`. +0xb8 ← 60e token (T59, row idx 59) de
  `CMonsterQuery::Deserialize` @0x860d790 (comptage vérifié via les 9
  résistances T25..T33 → +0x53..+0x5b).
- `GetAllNPCTemplate` @0x08368260 : NPC ignoré si `npc+0x38 != 0 &&
  ((mask >> Locate) & 1) == 0` — +0x38 ← token après le vecteur de 8
  strings (S_Npc joint idx 19 / body[17]).
- `lapis::ConvertLocate` @0x08589260 : US=5 (HK=1 JP=2 CN=3 MY=4 KR=6
  FR=7 DE=8 ES=9 PT=10 RU=11 TH=12 SG=13 ID=14 VN=15).
  `server_us/setup.ini` : `Locate=US` → notre serveur = 5.
- Données : S_Monster idx59 ∈ {0,2,16} (62 entrées masquées — citrouilles,
  dinde, noël, 2017, event JP/TW). S_Npc idx19 ∈ {0,2,4,8,64,160,4096,
  32768} — 119 NPCs région/event ; « 51254-6 美版專用NPC » mask=160
  (bit5=US) PREUVE que bit5=US.
- Impact réel sur nos scènes : 47 lignes NPC spawnées bloquées sur US
  (S001 festivals : 楊婆婆, 尼古拉斯, 朱雀… ; 網咖 50792/50793 S001-006)
  + 24 lignes mobs (南瓜 S001/S002, 觀光旅客, 巨大水晶, 天狗…).
- reborn_monsters : flag « né au boot » = byte +0xe du CRebornMonster,
  lu au token col10 (<19 champs) / col11 (=19) / col12 (≥20) —
  vérifié : cristaux bleus=1, rouges=0, escouades légion=0.
- G7 : scripts [event] type5 S005 — `If_RebornMonster g s` +
  `RebornMonster g 5|6 m` font basculer bleu(55xxx)↔rouge(60xxx) et
  spawnent les escouades légion (EV1-12). Cristaux : S_Monster ai=164,
  atk=1 — bleus portent RefIds 45404/58023, rouges non.

## Implémentation

- MonsterDef.LocateMask (row[59]) ; NpcDef.LocateMask (body[17]).
- MonsterSpawn.InitiallyActive (col born) — PopulateNode ne spawn que
  les lignes actives ; Event_RebornMonster/SpawnGroup reste libre.
- EdenConfig.ZoneLocate (EDEN_ZONE_LOCATE, défaut 5=US).
- IsStructure (atk≤1 || range≤0 || ai=164) : pas de wander, pas de
  riposte/sort, toujours attaquable. Crystal allié (ai=164 + RefIds) :
  non ciblable en mêlée (bleu).
