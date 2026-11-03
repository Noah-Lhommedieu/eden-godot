# Plan temporaire — Visibilité joueur↔joueur (265 ClientSeeChar)

Date : 2026-11-05. Lot : annonce de présence joueur↔joueur — le
serveur émettait déjà le mouvement (326) mais jamais la présentation
(265) : les autres joueurs étaient invisibles.

## Preuve native

- `CNC_CZ_ClientSeeChar::Serialize` zone @0x0819ec20 — ordre des
  champs vérifié ligne par ligne (u32 charId, 4×f32 pos/dir/move,
  2×u8 atkSpd, `PutBinary(4)` flags char+0x3a, 2×u16 niveaux,
  2×i64 HP/MaxHP, 2×i32 MaxHP/MaxMP, 5×u8 révisions, s16 cast,
  str étal, s16 team territoire, 3×f32 flottement, s16 état,
  s16 +0xd8c, i32 +0xdb0).
- Ctor @0x0819e8e0 — paquet caché dans `char+0x7d8`, construit par
  `MoveSelf`/`Goto`, broadcast via `CZoneServer::BroadCastToCanSee`.
- Gate natif : `(byte)char+0x3b & 4` = vrai personnage (posé à bit2
  de notre byte1).
- Handle = `char+0x34` = charId (log natif "Character ID = [%d]").
- `.g.cs` généré aplati : PutBinary absent + i64 tronqués → codec
  dédié `Codecs/ClientSeeCharCodec.cs` (pas de golden : dumps solo).

## Implémentation

- [x] `ClientSeeCharCodec` — builder fidèle + `LifeFlags` (bitset
      char+0x3a : mort, monté, elf, is-char, invincible, slayer).
- [x] `ZoneEntryFlow.SeeChar.cs` — snapshot EntryState→natif
      (BaseMovement, atkSpd×10/15, révisions modèle, titre d'étal,
      0xffff team territoire, flottement à 0).
- [x] Passe joueurs dans `UpdateSightAsync` — entrée en vue →
      265+326 ; sortie (>SightGone) → 269 ; purge orphelins couvre
      déco/migration/changement d'instance.
- [x] `AnnounceSelfToWatchersAsync` dans UpdateSight (throttle 350 ms
      partagé) — le mobile pousse son 265 aux watchers immobiles.
- [x] Gates CanSee : 326 joueur, 290 PlayerAction et 326 mob ne
      partent qu'aux sessions ayant le handle en vue (`ost.Sent`).
- [x] 6 tests `ClientSeeCharCodecTests` — layout figé (85 B chaîne
      vide, offsets, bitset, chaîne variable).

## Validation live (2026-11-05, headless ×2 sessions)

Fait via `tools/eden_session.py` (ticket T_ forgé + rejeu d'entrée) :

- [x] Node partagé S002 : A↔B reçoivent mutuellement 265 (87B wire,
      corps décodé champ par champ OK) + 326 ; mouvements de B
      relayés à A (gate CanSee).
- [x] Donjon S035 (type 5) : `resolve groupe -200000002` vs
      `-200000003`, **zéro 265 croisé** → isolation réelle.
- [x] `destruction groupe` à la déconnexion ; 269 émis aux watchers
      quand un perso part/déco.
- [x] **Bug trouvé et corrigé** : `DailyAwardRow` (`short` vs col
      `integer`) avortait `HandleTicketAsync` avant
      `ResolveInstanceGroup` → l'instancing était inerte en live.
      Commit fa58e6b.
- [ ] Client graphique (les deux builds) : confirmation visuelle
      finale — reste à la charge du testeur réel client.

## Audit des broadcasts génériques (2026-11-05, suite)

- [x] `BroadcastToNodeAsync` (Status.cs) — gate CanSee ajouté avec
      param `handle` : entité ciblée du paquet (charId par défaut,
      `ElfPetHandle` pour 470, `ex.Handle` pour 438/383). Tous les
      appelants diffusent des données d'une entité → seuls les
      watchers qui la voient reçoivent.
- [x] `ScheduleCorpseDespawn` — `InstanceGroup` ajouté + 269 seulement
      si le watcher avait le handle en `Sent` (purge conditionnée).
- [x] `BroadcastMonsterActionAsync`/`BroadcastDisplayMonsterAsync` —
      instance déjà filtrée ; gate `Sent` ajouté sur DisplayMonster.
- [x] Sortie de FSM mob (Combat.Mob:602) — `InstanceGroup` ajouté.
- [x] SpellBreak (Spells:618) — gate `Sent` sur le caster + indent.
- [x] Anims 290/PlayerAction (Strengthen ×2, Cube) — gate `Sent`.
- [x] Vérifiés OK : Chat say (node+inst), tous les broadcasts
      Events/Treasure/Migration/Evaluate (EvalId), MobWander
      groupé (node,inst), respawn mob, elf despawn.
- [x] Handlers/ legacy : toutes les registrations avec broadcast
      générique auditées — interceptées par les `case` du flow
      (code mort) SAUF annonces de recrutement (191/236) qui sont
      volontairement serveur-entier.
- [x] 963/963 tests verts, build 0/0.

## Validation live — instance d'équipe en donjon (2026-11-05, suite)

- [x] Deux sessions solo dans S035 (type 5) : groupes `-200000002`
      /`-200000003`, zéro visibilité croisée.
- [x] Invitation en zone (NCID 29) + acceptation (30) →
      `team=1`, les DEUX persos basculent `groupe 1`, les deux
      instances solo détruites, **265 reçu des deux côtés**.
- [x] **Bug wire trouvé** : `TeamInviteAnswer` natif n'a pas d'id
      d'invitation — `+0x14`=charId inviteur, `+0x12`=type de réponse
      (Execute @0x82918f0). Le notice envoyé était vide → un vrai
      client ne pouvait jamais accepter. Corrigé : lookup par paire
      (inviteur, cible) + notice remplie (inviteur/nom).
- [x] **Bug archi trouvé** : `OnTeamMembershipChanged` mutait
      `EntryState` depuis le thread lecteur — reposté sur la boucle,
      nouvel instance peuplée (`EnsureNodePopulated`) + rescan sight
      forcé (un perso immobile ne rescanne jamais sinon).
- [x] `eden_session.py --send NCID:HEX --send-delay N` : paquets
      arbitraires post-burst pour tests multi-sessions.
- [x] 963/963 tests verts.
