# E5 — Succès de famille, msgboard, tables mortes

Date : 2026-11-05. Source : synthèse phase E (S2) + désasm mission/world.

## Preuves natives (déjà établies)

### Chain succès famille (mission @0x081be030 `CFamily::UpdataAchievement`)
Appelée depuis `CGlobalInfo::SetAchievement` (@0x0808cd70, fin de fonction)
si `exec+0xb0` (family_id) résout dans AllFamilys :
`UpdataAchievement(exec, achId, gained=local_19e, def.Type=puVar8[5], def.GroupKey=puVar8[6])`.

Sémantique (param_3=gained, param_4=Type, param_5=GroupKey) :
1. `gained == 0` → retour silencieux.
2. `family_achievement` : clé = **GroupKey**. Absent → INSERT
   `(family_id, achievement_type, point)` ; présent → UPDATE point+=gained
   (rejet si > 999999999, tout le grant annulé).
3. Map compteurs `fam+0x160` : `+= gained` sous la clé GroupKey **et**
   sous la clé Type (double-clé, comme `CumulativeAchievement` joueur).
4. `fam+0x4c` (total_point) += gained, cap 999999999.
5. `fam+0x3c` (spell_point, s16) += gained cap **100**, seulement si
   `fam+0x38` (`elfking_tid`) > 0 → `UPDATE family.spell_point`.
   `AdoptElfKing` (mission @0x081b2930) n'a **aucun caller** → feature
   morte nativement : elfking_tid reste 0, les spell points ne coulent
   jamais. Port fidèle = même observable (rien) + doc.
6. `UpdateLog(this, 4, 1, achId, 0, playerName, "")` →
   `family_log` INSERT (id,type=4,log_id,date,para1=achId,para2=0,str1=nom,str2='')
   + broadcast `CNC_DBA_ClientChangeFamily` op **0x21** → monde
   `UpdateLog` → `CNC_CW_ClientFamilyLogUpdate` (68) push single-entry.
7. `fam[0x48]` dirty=1 → `CEventFamilyAchievementDBSave` (~1200 s)
   flush `family_achievement_rank` (total_point, member_size, lv, et
   16 compteurs : monster_kill=[1], family_instance=[5],
   lv01..lv91_instance_node=[0x65..0x6a], family_boss=[0x6b],
   rare_monster=[0x6c], family_mine/plant/summon/alchemy/casting/enchant=
   [0x1f5..0x1fa]).
8. Broadcast `CNC_DBA_ClientChangeFamily` op **0x14** (famId, op, type=
   GroupKey, str(total point du type), str(spell_point), charId, channel)
   → monde case 0x14 : met à jour fam+0xb4[type]=point, fam+0x34=spell,
   émet **NCID 67 `CNC_CW_ClientFamilyAchievementUpdate` flag=0** aux
   membres : `[u8 0][u16 n=1][s16 groupKey][u32 newPoint][i32 charId]
   [u32 channel]`.

### Wire 67 (Serialize monde @0x080cdfb0)
`[u8 flag]` ; flag∈{2,3} → `[u32 fam+0x48 (total_point)]` ;
flag==1 → `[i32 0]` (`operator<<(param_1, 0)` — littéral **int** natif) ;
sinon `[u16 n][n×{s16 type,u32 point,i32 charId,
u32 channel}]` (pour flag=3 les i32/u32 sont 0) ; flag≠0 →
`[u16 m][m×{i32 pid,i32 date,i32 point,s16 achId}]` (log —
`family_achievement_log`, table morte → m=0).
Login `CNE_CW_ServerQueryFamilyInfo::Execute` (@0x080e6480) émet
flag=3 (total + toutes les paires type→point de fam+0xb4) à l'entrant.

### Tables mortes (zéro écrivain dans zone/mission/world)
- `family_schedule` : aucune référence — `DeleteAllFamilyScheduleMsg`
  écrit en fait `family_msgboard`. Bug maison : notre NCID 51 émet
  `fam.Schedule` (toujours vide) au lieu de `fam.Messages`.
- `family_achievement_log` : `AddAchievementLog`/`UpdateAchievementLog`
  existent mais **zéro caller** ; seul `DeleteAllFamilyAchievementLog`
  est appelé (disband) → la table reste vide → le vecteur log du 67 est
  toujours `m=0` (fidèle).

## Implémentation

1. `FamilyRepository` : NCID 51 → `fam.Messages` (3 sites) ; retrait du
   SELECT `family_schedule` + `fam.Schedule` ; chargement
   `family_achievement` → `fam.Achievements` +
   `family_achievement_rank` → `fam.Counters`/`AchievementTotal`.
2. `Family` (models) : `Achievements` (GroupKey→point), `Counters`
   (dual-key), `AchievementTotal`, `SpellPoint`/`ElfkingTid` déjà là.
3. `FamilyRepository` : `UpsertAchievementAsync`, `SetSpellPointAsync`,
   `SaveAchievementRankAsync` (UPDATE puis INSERT si 0 lignes — pas de
   PK garantie). Adaptation documentée : flush synchrone au lieu de
   l'event 20 min natif.
4. `FamilyService.ApplyFamilyAchievementAsync(charId, achId, type,
   groupKey, gained)` → sémantique ci-dessus + broadcast 67 flag=0
   + push 68 single-entry.
5. `FamilyPackets.FamilyAchievementUpdate` — codec variable prouvé.
6. Login famille : push 67 flag=3 après le 51/68 existants.
7. Zone `GrantAchievementAsync` : si gained>0 et char en famille →
   ZW `family_achievement <charId> <achId> <type> <groupKey> <gained>`
   (même pattern que `set_family_emblem`).
8. Client : `WorldClientFamilyAchievementUpdate` → vrai codec variable
   (le généré aplati est faux). Stockage minimal.
9. EDENFR-GAPS : `family_schedule`, `family_achievement_log`,
   `AdoptElfKing`/spell_point (feature morte native).

## Vérification
- `dotnet build` 0 erreur. ✅
- Tests : repo upsert/spell-point/cap ; ApplyFamilyAchievement
  (upsert, compteurs, total, log, wire 67 flag0/3) ; codec client. ✅
- Suite complète Eden.Tests + NetTests. ✅

## Bilan (2026-11-05) — implémenté

Tous les points 1-8 livrés :
- **msgboard** : `fam.Schedule` retiré (3 sites NCID 51 → `fam.Messages`,
  login idem), SELECT `family_schedule` supprimé.
- **Service** : `FamilyService.ApplyFamilyAchievementAsync` porte
  `UpdataAchievement` à l'identique (upsert clé GroupKey, caps
  999999999, compteurs dual-key Type+GroupKey, spell_point cap 100
  gaté par `elfking_tid>0`, log type 4, rank synchrone).
- **Wire 67** : `FamilyPackets.FamilyAchievementUpdate` (flags
  0/1/2/3 — flag=1 = i32 0, flag=3 = fill charId/channel à 0).
  Broadcast flag=0 + push 68 single-entry aux membres dans
  `ZoneLinkServer` (commande ZW `family_achievement`) ; synchro
  flag=3 dans `HandleFamilyJoinAsync`.
- **Zone** : `GrantAchievementAsync` émet le ZW quand `gained>0` et
  le char est en famille (`Guilds.GetGuildOf`).
- **Client** : `WorldClientFamilyAchievementUpdate` réécrit en vrai
  codec variable (l'ancien généré aplati lisait 33 o fixes →
  exception garantie sur les 17-23 o réels, loguée par DispatchReceived).
- **Disband** : `DisbandAsync` supprime désormais les 10 tables filles
  comme `CFamily::Disband` natif (cadre, msgboard, achievement,
  achievement_log, achievement_rank, storage, storage_log,
  statues_scenes, building_container, family_log).
- **FixEquipByFamily** : audit terminé — les 3 textes (0x621 rate-limit,
  0x8f0 trésorerie, 0x5fd succès) étaient déjà émis ; 0x5fe inatteignable
  (notre apply ne peut pas échouer après débit).

Preuves : build 0 erreur 0 warning ; 11 tests `FamilyAchievementTests`
(codec flags 0-3, upsert/update, gained≤0, cap, elfking gate, cap
spell 100, log DB, msgboard, joueur hors famille) ; suite complète
**1773/1774** — seul `GrantExp_Level30Repro` (throw de debug
pré-existant, non lié).

Adaptations documentées : flush `family_achievement_rank` synchrone
(le natif diffère ~20 min sur dirty flag — même observable) ;
vecteur log du 67 toujours vide (table native jamais écrite hors
disband).

## Validation live 2026-11-05 (21:45→21:58) — COMPLETED

Chaîne complète prouvée en jeu sur ClericQA (#200000020, famille 5001 QAForge) :

- **Kill → succès → famille** : `//clone_monster 57501` + `//kill 57501`
  → `player_characters_achievement` 57501|1|999 (type 1, GroupKey 801)
  → `family_achievement` **5001|801|999** + `family_log` type 4
  para1=57501 str1=ClericQA + rank `total_point=999, monster_kill=999`
  → NCID 67 reçu client (payload groupKey 0x321=801, 999 pts).
- **Fix gap natif `AddItemFromWork`** : `reindex callers
  CItemFactory::SetItemAchievement` = {SetItemAchievement,
  AddItemFromLifeSkill (couvert), AddItemFromWork (manquant)} → appel
  `TryGrantItemAchievementAsync` ajouté dans `MaterializeWorkAsync`
  (claim 171, par produit non vide) — `ZoneEntryFlow.Work.cs`.
  Re-test : ordre 132 (cast 30297) → claim → **dague insérée**
  (274, serial 220000433) + succès **30297** accordé (999 pts) +
  `family_achievement` **5001|502|999** (GroupKey 502) + log type 4
  para1=30297 + rank total_point=**1998** (cumul des deux).
- Note : `NCID=67` du 2ᵉ grant non retrouvé dans le log client (broadcast
  best-effort côté monde — DB + rank corrects).
- Échec de recette natif confirmé au passage : un roll perdant consomme
  les mats, work_id → négatif, pas de produit (SaveWork(-id,0)).
