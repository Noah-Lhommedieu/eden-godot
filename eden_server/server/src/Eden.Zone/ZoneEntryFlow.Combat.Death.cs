using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Party;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Partiel combat — boucle des combats de mobs :
/// engagement/fin de combat, tick du registre sur la boucle de zone,
/// sorts de mob, mort du mob (broadcast, drops délégués à
/// Combat.Rewards), événements de groupe, respawn/corpse, mort et
/// rez du joueur.</summary>
/// <summary>Partiel combat — mort et réapparition : kill
/// de mob (broadcast à 2 phases natif), événements de groupe,
/// despawn du corps, respawn schedulé, mort du joueur et
/// téléportation au point de rez.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Mort du mob — séquences capture zsniff native :
    ///   mêlée (17:23:01.853) : `280 attr0=0` + `280 attr13` →
    ///     `272 [char][mob][dmg][type 1][-1]` (coup fatal, hpLeft=-1) →
    ///     `305 [mob][char][0]` → `280 mob attr0x21 (0x2bc,0)` →
    ///     `300 [mob][0xcb35][N][0]` + `300 [mob][0xcb36][N+1][0]`
    ///   sort (17:23:02.481) : `280 attr0=0` + `280 attr13` →
    ///     `281 [spell][mob][char][dmg][1]` → `305` →
    ///     `300 [mob][0xcb36][N][0]` UN SEUL (pas de 0xcb35).
    ///   Wire 300 (capture `2c01 <mob> 36cb 06 00*8`) :
    ///     [u32 handle][u16 attr][u8 act][i64 0] = 15B corps — le i64
    ///     final est OBLIGATOIRE : un corps court désynchronise le
    ///     flux RC4 (le client lit le ncid suivant sur les octets en
    ///     trop → "freeze" sans déconnexion : plus rien n'apparaît).
    /// N = compteur d'actions du mob (croît à chaque action).</summary>
    private async Task KillMobAsync(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob mob, CancellationToken token,
        int spellId = 0, int dmg = 0)
    {
        // Déjà mort (MarkDead d'un autre chemin — sort, proc, elf) :
        // ne pas re-scheduler une seconde séquence (double 305/loot/
        // exp constaté en live).
        if (!mob.Alive) return;
        mob.MarkDead();
        mob.DeadAt = DateTime.UtcNow;
        if (spellId == 0)
        {
            // Mêlée : le natif laisse ~300 ms entre le swing (272
            // type=1 kfId, déjà émis) et le twin fatal type=5/-1 + la
            // séquence de mort — délai du CEventDelayDamage (capture
            // 17:23:17.292→17.593). Schedulé sur la boucle : un
            // Task.Delay ici suspendrait le fil de jeu 300 ms.
            ScheduleOnLoop(TimeSpan.FromMilliseconds(300),
                _ => KillMobBroadcastAsync(session, st, mob, 0, dmg));
            return;
        }
        await KillMobBroadcastAsync(session, st, mob, spellId, dmg);
    }
    /// <summary>Séquence de mort émise — appelée immédiatement pour
    /// les sorts, schedulée +300 ms pour la mêlée
    /// (<see cref="KillMobAsync"/>).</summary>
    private async Task KillMobBroadcastAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int spellId, int dmg)
    {
        // Broadcast schedulé devenu obsolète : un sort/aoe a émis la
        // mort entre la planification (+300 ms) et l'exécution.
        if (mob.DeathEmitted) return;
        using var killScope = EdenMetrics.BeginDbOperation("mob_kill");
        var token = CancellationToken.None;
        // Attrs à zéro AVANT le coup fatal (ordre de la capture).
        await SendAttrAsync(session, mob.Handle, 0, 0, 0, token);
        await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0, token);
        if (spellId != 0)
            // Mort par sort : hit final 281 (dmg réel, flag hit=1).
            await SendSpellHitAsync(session, spellId, mob.Handle,
                st.CharId, dmg, SpellHitFlags.Hit, token);
        else
            // 272 coup fatal : type 5, hpLeft = -1
            // (capture 17:23:11.776 `…30 000000 05000000 ffffffff`).
            await SendMeleeAsync(session, st.CharId, mob.Handle,
                dmg, 5, -1, token);
        await FinishMobDeathAsync(session, st, mob, spellId != 0, token);
    }
    /// <summary>Queue de la mort du mob — capture :
    ///   `305 [mob][char][u8 0]` →
    ///   mêlée : `280 attr0x21 (0x2bc)` + `300 0xcb35` + `300 0xcb36`
    ///   sort  : `300 0xcb36` seul (17:23:02.482)
    /// puis sortie de combat `302 char 0000 sel=0 (nouvel act)` +
    /// `444 [0][0][0]` si plus rien n'aggro le joueur.</summary>
    private async Task FinishMobDeathAsync(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob mob, bool bySpell, CancellationToken token)
    {
        // Invariant « la mort n'est émise qu'une fois » — premier
        // arrivé (sort immédiat vs broadcast mêlée schedulé +300 ms)
        // gagne, les suivants (procs multi-stroke, second coup en vol)
        // repartent sans rejouer 305/loot/exp.
        if (mob.DeathEmitted) return;
        mob.DeathEmitted = true;
        // Timers armés EN TÊTE : une exception plus bas (session morte,
        // loot, procs) ne doit jamais laisser un cadavre éternel.
        ScheduleCorpseDespawn(mob);
        ScheduleRespawn(mob);
        // World boss / mutant : annonce de mort, récompenses courrier,
        // fin de vie du mutant (plan 12) — sur la boucle, hors chemin
        // critique de la séquence de mort.
        if (mob.Kind != Eden.Core.Game.WorldBoss.BossKind.None)
            ScheduleOnLoop(TimeSpan.Zero,
                _ => OnWorldMonsterKilledAsync(st, mob));
        // Procs d'équipement « kill » — OnKillEnchant natif (events
        // S_Enchant 6/7 : buffs self du porteur).
        await RunItemProcsAsync(session, st, mob, ProcHitEvent.Kill,
            0, token);
        // Pet elfe tué : l'elf repasse inactif + panneau notifié
        // (status 0 / hp 1 — la réanimation est le rôle d'ElfRevive).
        if (mob.ElfId != 0)
            ScheduleOnLoop(TimeSpan.Zero,
                _ => NotifyElfPetDiedAsync(mob));
        // 305 ClientDie [u32 victime][u32 tueur][u8 flags] — ordre
        // natif du ctor CCharacter::Die (piVar[1]=uid victime,
        // piVar[2]=handle tueur). Inversé, le client affichait le
        // joueur mort à chaque kill (bug live).
        await session.SendAsync(new CNC_CZ_ClientDie
        {
            Field1 = mob.Handle,        // u32 : victime
            Field2 = st.CharId,         // u32 : tueur
            // u8 : flags — bit 8 = mob brisé (CMonster::Die lit mob+0x7c) ;
            // 0 pour un kill ordinaire (capture).
            Field3 = DieFlags(mob),
        }, token);
        if (!bySpell)
        {
            await SendAttrAsync(session, mob.Handle, 0x21, 0x2bc, 0, token);
            await SendEnchantRemoveAsync(session, mob, 0xcb35, token);
        }
        await SendEnchantRemoveAsync(session, mob, 0xcb36, token);
        // Registre d'enchants du mob : 300 par enchant actif puis
        // clear — la capture montre les retraits avant le 305 et le
        // CEnchantMgr natif meurt avec l'entité (jamais persisté).
        await PurgeMobEnchantsAsync(session, st, mob, token);
        EdenLog.Write(
            $"[Combat] mob #{mob.MobId} (0x{mob.Handle:x8}) tué char={st.CharId}");
        // CEvaluate::CharacterKillMonster natif (@0x8083f40) : le kill
        // crédite l'instance d'évaluation du tueur (compteurs + End si
        // boss de la def S_RewardScore). Sur la boucle, avant le loot.
        await OnEvaluateMobKilledAsync(session, st, mob, token);
        if (_mission.Connected)
            _ = _mission.SendMobDieAsync(st.CharId, mob.MobId,
                mob.X, mob.Y);
        // Drops : table normale S_DropItem[mobId] + tables BP de classe
        // (ClassDropIds → S_ClassDropItem) + or S_Monster — inserts DB
        // puis notification 274. Fire-and-forget : la latence DB ne doit
        // pas retarder la suite de la séquence de mort.
        ScheduleOnLoop(TimeSpan.Zero,
            _ => DistributeLootAsync(session, st, mob));
        // Exp du kill — natif CMonster::KilledBy → CCharacter::GainExp :
        // 600 ClientUpdateAttrNewExp `[i32 char][i64 exp]` ; level-up →
        // 284 ClientLevelUp + refresh vitals. Fire-and-forget comme le
        // loot — la latence DB ne doit pas retarder la mort.
        ScheduleOnLoop(TimeSpan.Zero,
            _ => GrantKillExpAsync(session, st, mob));
        if (st.InCombat)
        {
            // Ne quitte la stance combat que si AUCUN autre mob n'aggro
            // encore le joueur — sinon l'exit 302 prématuré suivi des
            // coups de l'autre mob laissait le state controller client
            // désynchronisé (anim "course", impossibilité d'agir).
            var still = _services.Live.MobsOf(st.NodeId, st.InstanceGroup)
                .Any(m => m.Alive && m.InCombat &&
                    m.TargetCharId == (int)st.CharId);
            if (!still)
            {
                st.InCombat = false;
                st.AutoAttacking = false;
                st.ActionChain = false;
                await SendCombatExitAsync(session, st.CharId,
                    st.NextAction(), token);
                await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget(), token);
            }
        }
        // Groupe reborn anéanti → le drapeau natif reborn+0x6a retombe
        // et les événements conditionnés sur ce spawnId se débloquent
        // (boss mort → coffre, NPC de sortie, prochaine vague —
        // Event_If_RebornMonster/If_Monster des scripts [event]).
        // Fire-and-forget : la chaîne d'événements ne doit pas retarder
        // la séquence de mort.
        if (mob.SpawnId > 0 &&
            _services.Live.GroupMobs(mob.NodeId, mob.InstanceKey, mob.SpawnId)
                .All(m => !m.Alive))
        {
            RebornGroups[EvKey(mob.NodeId, mob.SpawnId)] = false;
            ScheduleOnLoop(TimeSpan.Zero,
                ct => RunGroupDeathEventsAsync(session, st, mob, ct));
        }
        // Clef d'event de la ligne reborn (col8 → CRebornMonster+0x34) :
        // OneDie natif appelle ExecuteCommand(serverId, reborn, chid)
        // à CHAQUE mort — cristaux 據點 : bleu détruit → event 3
        // (spawn rouge), rouge détruit → event 4 (retour bleu).
        if (mob.SpawnId > 0 && mob.DeathEventId > 0)
            ScheduleOnLoop(TimeSpan.Zero,
                ct => RunNodeSceneEventAsync(mob.NodeId, mob.InstanceKey,
                    mob.DeathEventId, session, ct));
    }
    /// <summary>Évalue les événements de scène dont une branche teste
    /// le groupe `mob.SpawnId` (If_RebornMonster/If_Monster) — natif :
    /// le changement d'état d'un reborn réveille les CEventCmd qui le
    /// conditionnent (respawns en chaîne, coffres, NPC de sortie).</summary>
    private async Task RunGroupDeathEventsAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, CancellationToken token)
    {
        var scene = _services.Scenes.GetScene($"S{mob.NodeId:D3}");
        if (scene == null) return;
        foreach (var (evId, ev) in scene.Events)
        {
            if (!ev.Branches.Any(b => b.Conds.Any(c =>
                    c.Name is "Event_If_RebornMonster" or "Event_If_Monster"
                        or "Event_If_RebornMonsterSum"
                    && c.Args.Length > 0 && c.Args[0] == mob.SpawnId)))
                continue;
            try
            {
                await RunSceneEventAsync(session, st, scene, evId, token);
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Zone-Event] node={mob.NodeId} " +
                    $"ev={evId} (spawn {mob.SpawnId}) : {ex.Message}");
            }
        }
    }
    /// <summary>Despawn du cadavre — retrait du mob de la vue quelques
    /// secondes après le 305 via 269 ClientLifeEradicate `[i32 handle]`
    /// (Serialize @0x81e63f0 : un seul i32 ; même paquet que le retrait
    /// hors-vue de UpdateSightAsync). Broadcast à toutes les sessions du
    /// node et purge de st.Sent — sinon le handle restait "déjà vu" et
    /// le respawn n'était jamais ré-émis en vue.
    /// HYPOTHESIS: délai 5 s — le timer cadavre exact n'est pas
    /// capturé ; le paquet lui-même est le mécanisme natif de retrait.</summary>
    private void ScheduleCorpseDespawn(ZoneLiveWorld.LiveMob mob)
    {
        ScheduleOnLoop(TimeSpan.FromSeconds(5), async ct =>
        {
            var (op, payload) = PreparePacket(new CNC_CZ_ClientLifeEradicate
            { Value = (int)mob.Handle });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    ost.NodeId != mob.NodeId ||
                    ost.InstanceGroup != mob.InstanceKey)
                    continue;
                // 269 inconditionnel : UpdateSightAsync peut avoir
                // purgé Sent entre-temps ; le Remove client est un
                // no-op sur un handle inconnu de toute façon.
                ost.Sent.TryRemove(mob.Handle, out _);
                ost.EntityPos.TryRemove(mob.Handle, out _);
                await SendPreparedToAsync(s, op, payload,
                    CancellationToken.None);
            }
        });
    }
    /// <summary>Mort du joueur — miroir de `CCharacter::Die(int,bool)`
    /// (désasm @0x83dxxxx) : attrs à 0 → 305 ClientDie
    /// `[u32 victime][u32 tueur][u8 flags]` → attr 0x21 →
    /// 531 ClientReviveInfo `[i32 area]` (options de réapparition).
    /// Sans cette séquence le client reste en état "mort sans savoir" :
    /// monde figé, aucune entité n'apparaît — le softlock constaté.</summary>
    private async Task PlayerDeathAsync(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob killer, CancellationToken token)
    {
        // Procs « porteur tué » (BeKillToSelf 8 / BeKillToOpponent 9 —
        // OnBeKillEnchant natif) : l'enchant mourant peut déclencher un
        // dernier effet (ex. buff posthume, riposte sur le tueur).
        await RunItemProcsAsync(session, st, killer, ProcHitEvent.BeKill,
            0, token);
        await PlayerDeathAsync(session, st, killer.Handle, token);
    }

    /// <summary>Variante handle brut — kills joueur→joueur (territory
    /// war : le tueur est un char, pas un mob).</summary>
    private async Task PlayerDeathAsync(NetSession session, EntryState st,
        uint killerHandle, CancellationToken token)
    {
        // Cast en vol à la mort : CastCts.Cancel + 287 + release du
        // flag (CheckCastBreak natif) — sans ça le ResolveCastAsync
        // schedulé résolvait le sort post-mortem et la barre client
        // restait ouverte indéfiniment.
        await BreakCastAsync(session, st, token);
        st.Dead = true;
        st.PlayerHp = 0;
        st.InCombat = false;
        st.AutoAttacking = false;
        st.ActionChain = false;
        st.CastingSpell = 0;
        // Attrs à zéro (attr0 broadcast + attr0x11 self), comme KillMob.
        await SendAttrAsync(session, st.CharId, 0, 0, 0, token);
        await SendVitalHpAsync(session, st.CharId, 0,
            st.PlayerMaxHp, token);
        // 305 ClientDie [u32 victime][u32 tueur][u8 flags] — broadcast.
        // Désasm CCharacter::Die : piVar[1]=char+0x34 (uid = la
        // victime), piVar[2]=param_1 (le tueur). Inversé, le client
        // marque le MOB mort → clic droit muet après revive.
        await session.SendAsync(new CNC_CZ_ClientDie
        {
            Field1 = st.CharId,         // u32 : victime (le joueur)
            Field2 = killerHandle,      // u32 : tueur
            Field3 = 0,                 // u8  : flags
        }, token);
        // 280 attr0x21 — attr post-mort (0x2bc dans la capture mob).
        await SendAttrAsync(session, st.CharId, 0x21, 0x2bc, 0, token);
        // 531 ClientReviveInfo [i32 area] — zone de réapparition liée au
        // perso (player_characters.revive_area_id). Le client affiche le
        // dialogue de résurrection à la réception de ce paquet.
        await session.SendAsync(new CNC_CZ_ClientReviveInfo
        {
            Value = st.ReviveAreaId,      // i32 : revive_area_id
        }, token);
        // 302 sortie de combat — repasse en état idle côté client.
        await SendCombatExitAsync(session, st.CharId, st.NextAction(), token);
        // CEvaluate::CharacterDie natif (@0x8081f70) : une mort de
        // membre de trial = +1 au compteur d'équipe (cappé DSL).
        await OnEvaluatePlayerDiedAsync(st, token);
        // Duel en cours → CCharacter::Die natif appelle
        // CDuelFactory::DuelFinish(char) : résultats 454 aux deux +
        // broadcast 4 aux témoins.
        await FinishDuelAsync(session, st);
        EdenLog.Write($"[Combat] MORT char=#{st.CharId} tué par " +
            $"0x{killerHandle:x8} node={st.NodeId}");
    }
    /// <summary>Revive (NCID 20 client, body `[i32 type]`) →
    /// `CNE_CZ_ZoneServerRevive::Execute` natif (@0x82807xx) :
    /// `this+0x14 == 5` → résurrection par objet SUR PLACE (Phoenix,
    /// type interne 5/7) ; autre → type 1 = `TransportCharacterToArea
    /// (char, revive_area_id)` = téléport à l'aire de réapparition liée.
    /// `CCharacter::Revive(char, _, 5, 10, 10, Para348, _)` restaure
    /// HP/MP à ~10 % du max (param_3/param_4 — jamais un heal complet,
    /// min 1) et applique `DecExpForDead` (exp −5 % du niveau si
    /// level &gt; 20 sur map normale). Wire : 306 ClientRevive
    /// `[u32 charUid][i32 ?][u8 ?]` = 9B (désasm Serialize @0x81e9a00 :
    /// `<<u32`, `PutBinary 4B`, `<<s8`) puis attrs + refresh position
    /// (313/267) — le client redessine tout.</summary>
    private async Task HandleReviveAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || !st.Migrated) return;
        // i32 ReviveType — 0 point de sauvegarde, 1 sur place (avec
        // pénalité d'exp), 5 item de revive sur place (sans pénalité).
        var reviveType = payload.Length >= 4
            ? BitConverter.ToInt32(payload, 0) : 0;
        // Serveur autoritaire : un vivant ne se « réanime » pas (sinon
        // pénalité d'exp / téléport gratuit), un type inconnu est refusé.
        if (!st.Dead || !ReviveRules.IsKnownType(reviveType))
        {
            EdenLog.Write($"[Combat] REVIVE refusé char=#{st.CharId} " +
                $"dead={st.Dead} type={reviveType}");
            return;
        }
        ItemEntity? leaf = null;
        if (ReviveRules.UsesLeaf(reviveType))
        {
            leaf = await FindReviveItemAsync(st, reviveType);
            if (leaf == null)
            {
                // Aucun item de revive en sac : refus natif 0x5ef, on
                // reste mort (la fenêtre de mort reste ouverte).
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x5ef, Field2 = "" }, token);
                EdenLog.Write($"[Combat] REVIVE item refusé " +
                    $"char=#{st.CharId} : pas d'item de revive");
                return;
            }
        }
        st.Dead = false;
        // Restauration native : 10 % du max (CPlayerFactory::Revive
        // (char,5,10,10,Para348,type,true) → CCharacter::Revive
        // param_3=10 hp%, param_4=10 mp% + bonus proto ~0, cap 100).
        st.PlayerHp = Math.Max(1, (int)(st.PlayerMaxHp * 10L / 100));
        st.PlayerMp = Math.Max(1, st.PlayerMaxMp * 10 / 100);
        st.InCombat = false;
        st.AutoAttacking = false;
        st.ActionChain = false;
        st.CastingSpell = 0;
        st.TargetHandle = 0;
        // Invincibilité de réapparition — même constante native que la
        // migration : S_Parameter 349 en 1/10 s (EffectToLife
        // PK_MIGRATION_EFFECT armé par CCharacter::Revive).
        st.MigrationInvincibleUntil = DateTime.UtcNow.AddSeconds(
            _services.GameData.Parameters.Get(349, 100f) / 10f);
        // Malus de mort — spec joueur : −10 % de l'exp du niveau
        // courant, perso ET classe, plancher 0 (jamais de level perdu
        // ni d'exp négative : à 0 % → reste à 0 %). Même moule que le
        // DecExpForDead natif (@0x83f4310 : loss = param ×
        // expRequisNiveau / 100) avec param = 10 — le natif passe 5
        // via Revive(…,Para348,…) ; la valeur demandée en jeu est 10.
        var expLoss = ReviveRules.AppliesExpLoss(reviveType)
            && !IsDungeonNode(st.NodeId);
        if (expLoss && st.Level > 0)
        {
            var lvlRow = _services.GameData.Levels
                .FirstOrDefault(l => l.Level == st.Level);
            if (lvlRow?.ExpNeeded > 0)
            {
                var loss = ReviveRules.ExpLoss(st.Experience,
                    lvlRow.ExpNeeded);
                if (loss > 0)
                {
                    st.Experience -= loss;
                    // 316 TextIndex 0xba "%lld" — "Vous avez perdu X
                    // exp" natif (CCharacter::Revive erreur 0xba).
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0xba, Field2 = loss.ToString() },
                        token);
                    // 600 : la barre d'exp client suit la perte.
                    await session.SendAsync(
                        new CNC_CZ_ClientUpdateAttrNewExp
                        {
                            Field1 = (int)st.CharId,
                            Field2 = st.Experience,
                        }, token);
                }
            }
        }
        // Exp de CLASSE — la pénalité s'applique aussi à la barre de
        // la classe active : même règle (10 % du palier courant,
        // plancher 0, jamais de class_level perdu).
        if (expLoss && st.ClassId > 0)
        {
            var cp = await _services.Classes
                .GetClassProgressAsync((int)st.CharId, st.ClassId);
            if (cp is { ClassExp: > 0 })
            {
                var clv = Math.Max(1, cp.ClassLevel);
                var cpNeed = _services.GameData.Levels
                    .FirstOrDefault(l => l.Level == clv + 1)
                    ?.ClassExpNeeded ?? 0L;
                if (cpNeed > 0)
                {
                    var closs = ReviveRules.ExpLoss(cp.ClassExp, cpNeed);
                    if (closs > 0)
                        await _services.Classes.SetClassProgressAsync(
                            (int)st.CharId, st.ClassId, clv,
                            Math.Max(0, cp.ClassExp - closs));
                }
            }
        }
        // Téléport natif : TransportCharacterToArea(revive_area_id) —
        // composite (node<<16)|eventAreaId, résolu via la table des
        // event-areas (Ins+0x244). La valeur DB peut aussi être une
        // clé [revive_area] col1 : ReviveMap[eaComp]=reviveComp → on
        // cherche dans les DEUX sens (données héritées).
        var migrated = ReviveRules.Teleports(reviveType) &&
            st.ReviveAreaId > 0 &&
            await TeleportToReviveAreaAsync(session, st, token);
        // Désaggro : les mobs qui ciblaient le mort repartent à l'état
        // passif (natif : la mort purge la cible — sinon le respawn est
        // réattaqué instantanément et le client reste en état combat).
        foreach (var m in _services.Live.MobsOf(st.NodeId, st.InstanceGroup))
            if (m.TargetCharId == (int)st.CharId)
                m.CancelCombat();
        // 306 [u32 charUid][i32 flags][u8 param_1] — 9B corps (désasm
        // ctor Revive : piVar[2]=char+0x3a (flags d'état, bit0=mort→0),
        // u8 = param_1 = TRUE chez les appelants natifs
        // (CNE_ZoneServerRevive → Revive(char,true,0,100,100,0,false)).
        // Envoyé à 0, le client gardait l'état "mort" → blocage du
        // clic droit/attaque après revive (bug live confirmé).
        await session.SendAsync(new CNC_CZ_ClientRevive
        { Field1 = st.CharId, Field3 = 1 }, token);
        await SendVitalHpAsync(session, st.CharId, st.PlayerHp, st.PlayerMaxHp, token);
        await SendAttrAsync(session, st.CharId, 0,
            st.PlayerHp, st.PlayerHp, token);
        await SendVitalMpAsync(session, st.CharId, st.PlayerMp, st.PlayerMaxMp, token);
        // Refresh position : 313 spawn + 267 déplacement à la position
        // (déjà mise à jour par le téléport si revive_area résolu) —
        // le client replace le perso.
        // Même node uniquement : une migration a déjà envoyé son 313
        // (node + position + hauteur) ; un second 313 portant le cap en
        // guise de hauteur écrasait l'ancre d'arrivée du client (carte
        // et entités décalées sous le sol).
        if (!migrated)
            await session.SendAsync(new CNC_CZ_ClientEnterNodeNodeData
            {
                Field1 = st.NodeId, Field2 = 1, Field3 = st.PosX,
                Field4 = st.PosZ,
                Field5 = _services.Scenes.GetScene($"S{st.NodeId:D3}")
                    ?.FloorZ(st.PosX, st.PosZ) ?? 0f,
            }, token);
        // Item de revive : consommé seulement une fois la revive faite.
        if (leaf != null)
            await ConsumeReviveItemAsync(session, st, leaf, token);
        await SaveIfLatestAsync(st, NextSaveMark(st.CharId));
        EdenLog.Write($"[Combat] REVIVE char=#{st.CharId} " +
            $"hp={st.PlayerHp}/{st.PlayerMaxHp} node={st.NodeId} " +
            $"type={reviveType}");
    }
    /// <summary>Premier item de revive du sac (Leaf verte/violette…).</summary>
    private async Task<ItemEntity?> FindReviveItemAsync(EntryState st,
        int reviveType)
    {
        var ids = ReviveRules.LeafIdsFor(reviveType);
        if (ids == null) return null;
        var items = await _services.InventoryRepo
            .GetLiveItemsAsync((int)st.CharId);
        foreach (var it in items)
        {
            if (it.Quantity > 0 && ids.Contains(it.ItemId)) return it;
        }
        return null;
    }

    /// <summary>Consomme 1 exemplaire (même queue que l'usage normal :
    /// 291/303/275).</summary>
    private async Task ConsumeReviveItemAsync(NetSession session,
        EntryState st, ItemEntity leaf, CancellationToken token)
    {
        var def = _services.GameData.Items.FirstOrDefault(
            d => d.Id == leaf.ItemId);
        if (def == null) return;
        await Handlers.ItemActionHandlers.ConsumeUsedItemAsync(session,
            _services, (int)st.CharId, leaf, def, 0, (short)leaf.BagId,
            (short)leaf.Slot, token);
    }

    /// <summary>`TransportCharacterToArea(char, revive_area_id, -1,
    /// true)` natif : résout le composite `(node<<16)|eventAreaId`
    /// dans la table des event-areas — le low16 est directement
    /// l'event-area de réapparition (BORN/GATEWAY du node — ex. S002
    /// area 11 « 湖畔森林 » bindée en traversant l'aire REVIVE 5 par
    /// la map [aireTraversée→clé] de LoadReviveArea @0x83504b0).
    /// Même node → reposition + 267/313 ; autre node → migration.</summary>
    private async Task<bool> TeleportToReviveAreaAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var stored = st.ReviveAreaId;
        var node = (short)(stored >> 16);
        var areaId = stored & 0xffff;
        SceneParser.EventArea? area = null;
        var scene = _services.Scenes.GetScene($"S{node:D3}");
        if (scene != null)
            // Le low16 stocké = l'event-area de spawn directement
            // (LoadReviveArea : traverser l'aire REVIVE col3 pose
            // revive_area_id = node<<16|col1 où col1 est l'event-area
            // BORN/GATEWAY de réapparition — ex. S002 area 11).
            area = scene.EventAreas.FirstOrDefault(a => a.Id == areaId);
        if (area == null)
        {
            // Aire invalide ("Player's ReviveAreaID[%d] is invalid") —
            // fallback : renaître sur place.
            EdenLog.Write($"[Combat] revive_area_id={stored} " +
                "invalide — revive sur place");
            return false;
        }
        st.Facing = area.Facing;
        var crossNode = node != st.NodeId;
        if (crossNode)
        {
            await MigrateToNodeAsync(session, st, node,
                area.X, area.Y, area.Z, token);
        }
        else
        {
            st.PosX = area.X;
            st.PosZ = area.Y;
            st.PosY = area.Z;
        }
        EdenLog.Write($"[Combat] revive teleport char=#{st.CharId} " +
            $"→ node={node} area={area.Id} ({area.X:F1},{area.Y:F1}) " +
            $"type={area.Type}");
        return crossNode;
    }
    /// <summary>Respawn d'un mob après son délai — renvoie SeeMob aux
    /// sessions présentes sur le même node.</summary>
    private void ScheduleRespawn(ZoneLiveWorld.LiveMob mob)
    {
        // Pets invoqués : mort définitive — le respawn n'existe que par
        // re-cast de l'enchant (BornPet natif ne respawne pas).
        if (mob.OwnerCharId != 0) return;
        // Mobs invoqués par item de quête : mort définitive (le natif les
        // crée via BornMonster, hors table reborn_monsters).
        if (mob.NoRespawn) return;
        // Échéance exposée pour le compte à rebours des world boss.
        mob.RespawnAt = DateTime.UtcNow.AddSeconds(mob.RespawnSeconds);
        ScheduleOnLoop(TimeSpan.FromSeconds(mob.RespawnSeconds), async _ =>
        {
            // Retiré du registre entre-temps (DespawnGroup d'event —
            // cristal remplacé par son successeur) : pas de fantôme.
            if (_services.Live.GetMob(mob.Handle) == null) return;
            _services.Live.Respawn(mob);
            // Annonce d'apparition encodée en scène (col19), ex. 70-S.
            await AnnounceSpawnAsync(mob);
            var (seeOp, seePayload) = PreparePacket(
                BuildSeeMob(mob.Handle, mob.MobId, true,
                    mob.X, mob.Y, mob.Z, mob.Facing, mob.Hp,
                    maxPatience: mob.MaxPatience));
            foreach (var s in _services.Sessions.All)
            {
                if (_states.TryGetValue(s.SessionId, out var ost) &&
                    ost.NodeId == mob.NodeId &&
                    ost.InstanceGroup == mob.InstanceKey)
                {
                    ost.Sent[mob.Handle] = 0;
                    ost.EntityPos[mob.Handle] = (mob.X, mob.Y);
                    await SendPreparedToAsync(s, seeOp, seePayload,
                        CancellationToken.None);
                }
            }
        });
    }
}
