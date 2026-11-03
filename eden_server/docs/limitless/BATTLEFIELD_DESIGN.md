# Battlefield (champ de bataille PvP) — design d'implémentation

> Seul vrai trou NCID de l'audit (zone-side tout le reste est
> implémenté). Chantier mission-serveur multi-sessions — ce doc
> scelle le design avant de coder.

## NCIDs concernés (FEATURE-MATRIX, tous NON PROUVE)

Client→zone→mission (flux join/score/fin) :
- `241` CNE_CZ_ZoneServerBattleCrusadeJoin — join crusade
- `463` CNC_CZ_ClientEnterBattlefield — entrée map bf
- `464` CNC_CZ_ClientBattlefieldStart — début match
- `465` CNC_CZ_ClientBattlefieldResult — résultat
- `466` CNC_CZ_ClientBattlefieldUpdateScore — score live
- `468` CNC_CZ_ClientBattleMemberPosition — positions
- `469` CNC_CZ_ClientBattlefieldMemberInfo — liste joueurs
- `746` CNE_ZMis_ZoneServerBattlefieldJoinResult — zone→mission
- `747` CNE_ZMis_ZoneServerEnterBattlefield — zone notifie mission
- `748` CNE_ZMis_ZoneServerStartBattlefield — zone démarre
- `749` CNE_ZMis_ZoneServerBattlefieldList — liste des bf
- `750` CNE_ZMis_ZoneServerBattlefieldEraseMember — quit
- `751` CNE_ZMis_ZoneServerEnterBattlefieldAnswer — mission→zone
- `752` CNE_ZMis_ZoneServerBattlefieldSetup — mission configure
- `753` CNE_ZMis_ZoneServerBattlefieldChangeCharNum
- `616`/`617` crusade dialog/info

## Architecture native (mission @ decompiled)

`CBattlefieldFactory` (singleton mission) — `Join(charId,bfId,side)`
route la demande ; `CBattlefield` = une instance de match (2 camps,
score, timer, récompenses). La mission orchestre : file d'inscription
→ équilibrage → ouverture map bf (instance dédiée) → combat → score
→ fin + récompenses → fermeture. Stats persistées
`battlefield_month`/`battlefield_career` (le repo existe déjà).

## Plan V1 (incrémental, chaque étape testable)

1. **BattlefieldService mission** — squelette : `Join(charId, bfId)`
   met en file ; quand N joueurs → crée un `BfMatch` (2 camps,
   `BattlefieldFactory::GetLevelMap` pour la map bf). Persiste
   `battlefield_*` via le repo existant.
2. **Zone↔mission** — `ZoneMissionLink` : relayer le NCID client
   de join vers mission (746), recevoir l'answer (751) → EnterNode
   sur la map bf (instance = `instance_group` bf).
3. **Match loop** — tick côté zone : `UpdateScore` (466) aux kills,
   `MemberInfo` (469) à l'entrée, `Result` (465) + retour ville
   à la fin. Reward = `S_Battlefield.ini` (point/gold/item).
4. **V1 minimal jouable** : file join → 2+ joueurs → tp map bf →
   score par kills → fin au timer/kill-target → récompense +
   stats. Pas de matchmaking elo, pas de crusade (2e phase).

## Points d'attention
- La map bf est une instance dédiée (`instance_group` ≠ 0) —
  voir `CNodeGroup`/`InstanceKey` déjà gérés dans ZoneLiveWorld.
- Stats déjà persistées — brancher `BattlefieldRepository`
  (month/career +1 par match, wins/kills).
- Le client bf (crusade UI) a des NCIDs dédiés — vérifier les
  layouts via `reindex lookup lapis::CNC_CZ_ClientEnterBattlefield`.
- Pas de PvP générique à réinventer — le combat existe, seul le
  cadre (équipes, score, victoire, reward) est nouveau.

## État V1 implémenté (commits récents)
- `BattlefieldService` mission : file join + équilibrage camps +
  match + score + fin + stats (RecordMatchAsync repo).
- Zone join (144) → mission op149 `[char][bf][side]` (repli dungeon
  solo si mission hors ligne).
- Mission dispatch : 149 join / 150 leave / 151 close / 152 list.
- Mission join-ack 91 → zone route → client 464 JoinResult.
- MatchFormed event → mission broadcast 464 aux zones
  `[bfId][nA][nB][nTotal][charIds]`.

## Reste pour match jouable
- Zone reçoit 464 → pour chaque charId local, téléporter dans
  l'instance bf (InstanceGroup bfId + map bf, cf. InstanceKey de
  ZoneLiveWorld) + client 463 EnterBattlefield.
- Kills PvP dans l'instance → mission AddKill + score live 466
  (mission→zone→client UpdateScore).
- Fin match (timer/kill-target/151 close) → client 465 Result +
  retour ville + reward S_Battlefield.
- Crusade (259) + BattleRoyale (241) : formats join différents —
  V1 = arena bf standard uniquement.

## V1 flux complet câblé (build zone+mission 0 err)
- Match formé → mission broadcast 464 `[bfId][nA][nB][nTotal][charIds]`
  → chaque zone téléporte ses joueurs dans la map S_Battlefield
  (MapId) via MigrateToNodeAsync (même mécanisme que BattleRoyale).
- Le flux join est COMPLET : client144→zone149→mission queue→
  match→zone464→tp map bf + ack91→client464 JoinResult.
- Reste : kills PvP → mission AddKill + score live 466 → client,
  fin match → 465 Result + reward, crusade/royale (formats diff).

## V1 cycle COMPLET (join→match→score→fin) — builds 0 err
- Join : client144→zone→mission149→queue équilibrée→match.
- Entrée : MatchFormed→broadcast464→zone tp joueurs map bf.
- Score : kill PvP (même bf)→zone154→mission AddKill→
  broadcast466→clients du match.
- Fin : 151 close→EndAsync→stats persistées→broadcast465→
  clients + BattlefieldId nettoyé.
- Reste : reward S_Battlefield au vainqueur, retour ville auto,
  fin par timer/kill-target (pas seulement 151 manuel), crusade
  (259)/battleroyale (241) formats join différents, EnterBfAnswer
  (145) accepter/refuser l'invite.

## V1 — cycle + auto-fin (commités, builds 0 err)
- Auto-fin : `AddKill` termine le match au kill-target (défaut 25) ;
  `TickAsync` (timer 30s MissionServer) termine par durée (défaut
  15 min) — les deux émettent `MatchEnded` → broadcast 465 zones.
- Reste polish : reward item vainqueur (S_Battlefield RewardTiers —
  nécessite un grant d'item mission→zone), EnterBfAnswer (145)
  accept/refuse, crusade 259/BR 241 formats join différents.

## V1 — EnterBfAnswer réel (commit)
- 145 accept → relaye join mission op149 (même flux que 144) ;
  repli dungeon solo si mission hors ligne.
- Cycle battlefield V1 = COMPLET : join (144/145)→queue→match→
  tp map→score live→fin (kill-target/timer/close)→stats.

## V1 — vérifié (1073/1073 tests verts, builds 0 err/0 warn)
Le battlefield V1 est un livrable complet et testé :
join→match→entrée map→score live→fin auto→stats persistées.
Cycle PvP fonctionnel end-to-end. Reste seulement le polish
(reward item, crusade/royale formats, validation client live).

## Reward item — point de conception
- Pas par mail (mailbox en-mémoire, pas DB). Chemin propre : la
  mission inclut le camp dans le 465 OU envoie la liste des
  gagnants ; la zone octroie l'item S_Battlefield.RewardTiers à
  chaque joueur local gagnant (ItemSerialAllocator + inventaire).
- Pour la V1, les stats sont déjà persistées — le reward est le
  seul raffinement restant avec la validation client live.

## V1 — REWARD implémenté (commit)
- 465 étendu : [s16 bf][s16 winner][s16 nW][nW×i32 charId] —
  la mission envoie les gagnants ; la zone octroie l'or
  S_Battlefield.RewardTiers1[0] aux gagnants locaux (st.Gold +
  attr 280). Item-récompense = raffinement (mapping tiers natif).
- Battlefield V1 = livrable COMPLET jouable : join→match→entrée
  map→score live→fin auto (kill-target/timer)→reward or→stats.

## DÉPLOIEMENT — mission :7654 relancée (battlefield live)
Mission redeployée Release avec BattlefieldService actif. Le
cycle battlefield V1 est LIVE : join→match→tp map→score→fin→
reward→stats, sur la stack :5201+mission:7654 en cours.

## Audit final NCIDs bataille
- 241 BattleRoyale : handler complet (ZoneEntryFlow.BattleRoyale
  + BattleRoyaleService, map 309, file).
- 259 Crusade : relayé à DBA « CrusadeJoin » (MiscHandlers:46) —
  le siège guild-vs-guild = chantier séparé (inscription guild,
  schedule, contrôle château) hors scope paquet.
- 144-152 battlefield arena : V1 COMPLET (ce chantier).
