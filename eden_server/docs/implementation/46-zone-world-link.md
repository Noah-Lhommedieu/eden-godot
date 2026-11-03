# 46 — Lien World↔Zone (ZW) et entrée en jeu

**Statut : fonctionnel — entrée en jeu E2E validée (test3, 15/10 ~18:25).**

Chaîne opérationnelle : `LoginServer natif → World C# (:5300/:5301) →
ZoneServer natif (:10022) + MissionServer natif (:7654)`. Le client entre en
jeu, prend des quêtes, se déplace, combat — session persistante.

## Table des commandes ZW

Extraite des slots `CNetCommand<...>::NCID` lus en mémoire dans le ZoneServer
vivant (vaddrs `ds:0x89bdXXX`, `InitZoneWorldCommandTable`) :

### Zone → Monde (C>S)

| NCID | Classe | Rôle |
|------|--------|------|
| 0 | `CNC_ZW_WorldServerIDReport` | `[u16 zoneId]` — réponse au NCID 16 |
| 1 | `CNC_ZW_WorldServerTell` | |
| 2 | `CNC_ZW_WorldServerAllUsers` | |
| 3 | `CNC_ZW_WorldServerNotifyEntry` | entrée effective du perso |
| 4 | `CNC_ZW_WorldServerCharacterArrived` | `[22B ticket]` |
| 5 | `CNC_ZW_WorldServerResetTime` | `[u32 charId]` — signal chargement |
| 6 | `CNC_ZW_WorldServerReturnToCharacterList` | |
| 7 | `CNC_ZW_WorldServerTeamLeavedNotify` | |
| 8 | `CNC_ZW_WorldServerChannelMsg` | |
| 9 | `CNC_ZW_WorldServerFamilyCreation` | |
| 10 | `CNC_ZW_WorldServerLogout` | |
| 11 | `CNC_ZW_WorldServerTextCommand` | `check_interface <charId>` etc. |
| 12 | `CNC_ZW_WorldServerItemMallReceieveAttempt` | |
| 13 | `CNC_ZW_WorldServerGetCloneItemID` | |
| 14 | `CNC_ZW_WorldServerTextExCmd` | |
| 15 | `CNC_ZW_WorldServerFBMsgUpdate` | |

### Monde → Zone (S>C)

| NCID | Classe | Rôle |
|------|--------|------|
| 16 | `CNE_ZW_ZoneServerReportID` | vide — demande d'identification |
| 17 | `CNE_ZW_ZoneServerDestShutdown` | |
| 18 | `CNE_ZW_ZoneServerCharacterArrivedDest` | `[u32 charId]` |
| 19 | `CNE_ZW_ZoneServerChannelMsg` | |
| 20 | `CNE_ZW_ZoneServerChangeFamily` | |
| 21 | `CNE_ZW_ZoneServerNewFamily` | |
| 22 | `CNE_ZW_ZoneServerRemoveFamily` | |
| 23 | `CNE_ZW_ZoneServerTextCommand` | `[u16 len][ascii]` ex. `update_prestige <id> 2 1 1 300` |
| 24 | `CNE_ZW_ZoneServerItemMallReceieveAttempt` | |
| 25 | `CNE_ZW_ZoneServerGetCloneItemID` | |
| 26 | `CNE_ZW_ZoneServerUpdateFortuneBag` | config FortuneBag |
| 27 | `CNE_ZW_ZoneServerUpdateLottery` | config Lottery |
| 28 | `CNE_ZW_ZoneServerTextExCmd` | |
| 29 | `CNE_ZW_ZoneServerUpdateStarWay` | config StarWay — **gate d'entrée** |

## Séquence d'enregistrement de la zone

```
W→Z  NCID 16   (vide) — le monde demande l'identification dès la crypto prête
Z→W  NCID 0    [u16 zoneId]
W→Z  NCID 26   FortuneBag   (CGlobalInfo::UpdateZoneFortuneBagData)
W→Z  NCID 27   Lottery      (CGlobalInfo::UpdateZoneLotteryData)
W→Z  NCID 29 × N StarWay    (CGlobalInfo::UpdateZoneStarWayData — 1 paquet/ligne)
```

Le monde natif envoie ces pushes depuis `CNE_ZW_WorldServerIDReport::Execute`.

## Format `CNC_ZW_ZoneServerUpdateStarWay` (NCID 29)

Désassemblé de `::Serialize` (WorldServer.bak 0x8067280) :

```
s16 week_type                     (starway.week_type)
s16 theme_type                    (starway.theme_type)
u16 n | n×s16                     effect_id1..3   (vector<s16>)
u16 m | m×s16                     item_id1..8     (vector<s16>)
u16 len | len bytes               remark (GBK brut)
```

Source : `CAccountDBO::GetStarWayData` →
`SELECT week_type, theme_type, effect_id1..3, item_id1..8, remark FROM starway`
(**fnaccount**). Le monde itère la `std::map<pair<s16,s16>, CStarWay>` triée par
`(week_type, theme_type)` et envoie **un paquet par ligne** à chaque zone
enregistrée.

Note encodage : le remark est du GBK stocké via une double conversion
Latin-1→UTF-8 dans la base (colonne `varchar`, `server_encoding=UTF8`) —
`Encoding.Latin1.GetBytes(str)` restaure les octets filaires originaux.

## Pourquoi StarWay bloquait l'entrée (le bug "Server connection: Failure")

Séquence client↔zone à l'entrée (réf. `captures/zsniff_native_entry.bin`) :

```
zone→client burst : 473, 302, 313, 404, 57, 310, 311, 351, 412, 424, 523, 593
client→zone 256   CNE_CZ_ZoneServerEnterNodeStarWay
zone→client 609 ×11   CNC_CZ_ClientEnterStarWayData
client→zone 133, 25 → zone 490, 312, 280 → rafale requêtes → 558 (prêt) → …
```

`CNE_CZ_ZoneServerEnterNodeStarWay::Execute` (zone 0x82511b0) itère la liste
StarWay globale (`0x89b9eb8`). **Liste vide → appel `ILifeEntity::Action(5)`
puis retour : zéro paquet 609.** Le client reste sur l'écran de chargement en
attendant des données qui n'arrivent jamais → timeout "Server connection:
Failure" ~45 s.

Sans le push NCID 29 du monde, la liste est vide. Avec, la zone émet les 11×
609 et le client déroule le reste de sa séquence d'initialisation.

## Séquence ZW d'entrée d'un personnage

```
Z→W  NCID 4   [22B ticket]        CharacterArrived (ticket client→zone validé)
W→Z  NCID 18  [u32 charId]        CharacterArrivedDest
Z→W  NCID 5   [u32 charId]        ResetTime — le perso est instancié
W→client 40   [u32 start][s32 -1] ClientResetTime (la fin du chargement)
Z→W  NCID 3   […]                 NotifyEntry
W→Z  NCID 23  "update_prestige <charId> 2 1 1 300"  (sur requête famille 10)
Z→W  NCID 11  "check_interface <charId>"  (keepalive ~30s, pas de réponse attendue)
```

## MissionServer — switches de features

La zone reçoit aussi des commandes texte à son enregistrement auprès du
MissionServer (`CNE_MW_WorldTextCommand`, log zone Test.log) :

```
InitInGameLotterySwitch  ingame_lottery_switch = 1
InitDiamondLotterySwitch diamond_lottery_switch = 1
UpdateStarWaySwitch      SwitchStatus = 1
DailyAwardState          open = 1 show = 1 begin_date = 0
```

Ces valeurs proviennent de l'état du MissionServer (rechargées à son boot).
**Attention** : un MissionServer figé (main loop bloquée, EVAL >200s) laisse la
zone sans réponse → redémarrage nécessaire ; la zone ne retente pas ses liens
sortants (ZW ni mission) après déconnexion → redémarrage zone obligatoire.

## État d'implémentation C# (Eden.World/ZoneLinkServer.cs)

- [x] Accept connexion zone, handshake RSA+RC4, NCID 16 → 0 (enregistrement)
- [x] Push StarWay NCID 29 (11 lignes `fnaccount.starway`)
- [x] NCID 4 → réponse 18 (CharacterArrivedDest)
- [x] NCID 5 → push client NCID 40 (ClientResetTime, `[start][-1]`)
- [x] Push `update_prestige` NCID 23 sur requête famille (10)
- [ ] Push FortuneBag NCID 26 (format complexe, 86k lignes — chunked)
- [ ] Push Lottery NCID 27
- [ ] NCID 28 TextExCmd (contenu à déterminer)
- [ ] Lien World→Mission :7654 (`CNC_MW_*`) — cross-zone, mail, famille
- [ ] NCID 10 logout → nettoyage `flags`/`online_user`

## Restes connus

- `test1` ne peut pas entrer : `cross zone revive area failed` →
  `CNE_DBA_ClientPlayerData` envoie `CNC_ZMis_MissionServerCrossZone` au
  mission qui forwarde `MW_WorldServerCrossZone` au monde — sans lien
  monde↔mission la réponse `ZoneServerCrossZoneResult` n'arrive pas.
  Le perso a une revive-area cross-zone héritée d'un `MissionGoto`
  (node_id/last_normal_area_id/revive_area_id à inspecter).
- Le `check_interface` (NCID 11) n'attend pas de réponse côté natif — ignoré
  à juste titre.
- LoginServer écrase `worlds.version='Unknown_Version'` et `state=0` à chaque
  démarrage → refixer `version`/`state` en base après chaque restart
  (`configuration.login_version` mis à `002.003.05.67`).

## Captures de référence

- `captures/zsniff_native_entry.bin` — flux client↔zone natif complet (test1)
- `captures/zsniff_cs_entry_success.bin` — flux client↔zone avec World C# (test3)
- `captures/mitm_native_entry.log` — MITM W_/ZW_/T_ du run natif
