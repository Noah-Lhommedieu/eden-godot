using Eden.Core;
using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Game.Services;
using Eden.Zone.Combat;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  ZoneEntryFlow — séquence d'entrée en map pilotée par les captures de la
//  zone native (captures/zsniff_cs_entry_success.bin, run test3 18:26).
//
//  Flux prouvé côté fil :
//    C>Z NCID 0  [ticket 22B]          → T_ ServerUseTicket → S>C NCID 473 (hello)
//    C>Z NCID 57 (1er Echo)            → push 302/313/404 + ZW NCID 5 (ResetTime)
//    C>Z NCID 26/24/53/110/113/195/243/256/133/25 → burst EnterNode*
//    C>Z requêtes batch (62,50,152,162,166,164,165,181,213,558,223,258…)
//    C>Z NCID 4  (MigrationDone)       → dump attributs + spawns (sight 298×N)
//    C>Z NCID 1  (MoveSelf)            → NCID 264 echo position
//
//  Les payloads sont replayés depuis la capture et patchés :
//    - charId de référence 200000004 (LE 04c2eb0b) → charId réel
//    - nom "test3" (u16 len + bytes) → nom réel dans NCID 310
// -----------------------------------------------------------------------------
public sealed partial class ZoneEntryFlow
{
    /// <summary>charId du run de référence (test3) présent dans les payloads.</summary>
    public const uint RefCharId = 200000004;
    /// <summary>Node de la capture d'entrée (EntryReplayData) : les
    /// 298/313 rejoués n'ont de sens que sur ce node (S002 — le NCID
    /// 313 de la capture porte node=2 en dur).</summary>
    private const int ReplayNodeId = 2;

    private readonly TicketServerClient _ticket;
    private readonly ZoneWorldLink _world;
    private readonly ZoneMissionLink _mission;
    private readonly CharacterRepository _characters;
    private readonly ZoneServices _services;
    /// <summary>Exécuteur générique des commandes d'enchant de
    /// résultat (voie mob) — handlers sous Combat/SpellEffects. Lazily
    /// construit : le handler de dispel a besoin de
    /// <c>_services.Enchants</c> (indisponible en init de champ).</summary>
    private Eden.Zone.Combat.SpellEffects.SpellEffectExecutor?
        _spellEffects;
    private Eden.Zone.Combat.SpellEffects.SpellEffectExecutor
        SpellEffects => _spellEffects ??= Eden.Zone.Combat.SpellEffects
            .SpellEffectExecutor.CreateDefault(_services.Enchants);
    private readonly string _dataDir;
    private bool _nodeAnnounced;
    /// <summary>File de persistance ordonnée par charId — remplace les
    /// <c>_ = repo.XxxAsync()</c> fire-and-forget (ordre non garanti,
    /// exceptions perdues). Les écritures d'un même perso s'exécutent
    /// dans l'ordre d'enqueue ; les exceptions sont loguées
    /// <c>[Persistence]</c>.</summary>
    private readonly Eden.Core.Database.PersistenceQueue _persistence = new();
    /// <summary>Write-behind des champs fréquents (coords à chaque
    /// MoveSelf, revive/last-normal-area) — dirty flag, flush
    /// périodique et flush forcé à la déconnexion.</summary>
    private readonly CharacterWriteBehind _writeBehind;


    private readonly ConcurrentDictionary<Guid, EntryState> _states = new();

    /// <summary>Trace [Zone-In] par paquet — EDEN_ZONE_TRACE=1.</summary>
    private static readonly bool TraceInbound =
        EdenConfig.ZoneTrace;

    public ZoneEntryFlow(TicketServerClient ticket, ZoneWorldLink world,
        ZoneMissionLink mission, CharacterRepository characters,
        ZoneServices services, string dataDir)
    {
        _ticket = ticket;
        _world = world;
        _mission = mission;
        _characters = characters;
        _services = services;
        _dataDir = dataDir;
        _writeBehind = new CharacterWriteBehind(characters, _persistence);
        // Commandes GM implémentées par le switch local de
        // ZoneEntryFlow.Admin.cs (elles ont besoin d'EntryState ou du
        // pipeline de boucle) — déclarées au registre pour que
        // ListCommands()/help/audit reflètent l'implémenté.
        foreach (var name in new[]
                 {
                     "gain_exp", "gain_class_point", "self_use_effect",
                     "remove_enchant", "clone_monster",
                     "clone_monster_locate", "transport_node", "goto",
                     "restore", "get_license", "set_class_level",
                     "set_money", "level", "clevel", "add_class",
                     "change_class", "kill", "heal", "invincible",
                     "gain_lifelong_gold", "gain_prestige_exp",
                     "set_prestige_level", "update_closet",
                     "get_player_locate",
                 })
            _services.Gm.MarkImplemented(name);

        // Callback effets consommables : le handler ItemUse (stateless)
        // y délègue l'application HP/MP/téléport sur l'EntryState —
        // routé sur la boucle (mutation EntryState).
        _services.ItemUseApply = (s, charId, enh, buffSec, tok) =>
            RunOnLoopAsync(ct => ApplyUseEnchantAsync(s, charId, enh,
                buffSec, ct), tok);
        // Partage de quête (C→S 87 op 6/7) — l'état d'équipe/sessions
        // vit sur la boucle.
        _services.MissionShareOp = (s, charId, op, mid, arg, tok) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var sst)
                ? HandleMissionShareOpAsync(s, sst, op, mid, arg, ct)
                : Task.FromResult(false), tok);
        // Pré-contrôle CanExecute des commandes d'usage (quest items
        // positionnels : SummonInEventArea node/position) — le handler
        // ItemUse le consulte AVANT de consommer l'item.
        _services.ItemUseCanExecute = (s, charId, enh, tok) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var cst)
                ? CanUseEnchantAsync(s, cst, enh, ct)
                : Task.FromResult(true), tok);
        // Ouverture de sac chanceux : le handler ItemUse (stateless)
        // délègue — l'état char+0xc70.. vit dans l'EntryState (boucle).
        _services.LuckyBagUse = (s, charId, bagId, tok) =>
            RunOnLoopAsync(async ct =>
            {
                if (_states.TryGetValue(s.SessionId, out var lst))
                    return await StartLuckyBagAsync(s, lst, bagId, ct);
                return false;
            }, tok);
        // Sac de fortune instantané (OpenFortuneBag natif — plages
        // [49001,50001)∪[51001,53001)). srcBound = bind de l'item
        // sac consommé (item+0x24 bit5 natif, hérité par le lot).
        _services.FortuneBagUse = (s, charId, bagId, srcBound, tok) =>
            RunOnLoopAsync(async ct =>
            {
                if (_states.TryGetValue(s.SessionId, out var lst))
                    return await OpenFortuneBagAsync(s, lst, bagId,
                        srcBound, ct);
                return false;
            }, tok);
        // Invalidation du cache d'anims d'arme : les handlers
        // inventaire (stateless) signalent un changement d'équipement ;
        // le natif rebuild les pools CCharKfm à chaque équipement.
        _services.InvalidateWeaponAnim = sessionId =>
        {
            _ = RunOnLoopAsync(_ =>
            {
                if (_states.TryGetValue(sessionId, out var wst))
                {
                    wst.WeaponResolved = false;
                    // Le corps 311/équipement du bundle d'entrée est
                    // périmé — les patches retombent sur les requêtes
                    // live (EntryDirty, Phase 2).
                    wst.EntryDirty = true;
                }
                return Task.CompletedTask;
            });
        };
        // Débordement d'inventaire → trésor au sol (CTreasure natif) :
        // les handlers sans EntryState déposent via ce callback.
        _services.TreasureDrop = (s, itemId, count) =>
            RunOnLoopAsync(_ => TreasureDropCallbackAsync(s, itemId,
                count));
        // UpdateFastDressFromInfo natif : tout déplacement d'item
        // resynchronise la position source des records fast_dress
        // (ChangeFastDressInfo → UPDATE fast_dress).
        _services.FastDressItemMoved = (charId, serial, con, loc, idx) =>
        {
            var fst = _states.Values.FirstOrDefault(
                e => e.CharId == charId);
            return fst == null
                ? Task.CompletedTask
                : SyncFastDressSourceAsync(fst, serial, con, loc, idx);
        };
        // Verrous d'état (enchant 2055) — lecture synchrone de
        // l'EntryState live : les handlers stateless (ItemUse)
        // refusent l'action quand le compteur natif est > 0.
        _services.StateLocksOf = charId =>
        {
            var lst = _states.Values.FirstOrDefault(
                e => e.CharId == charId);
            // StateLocks est publié sur la boucle (int atomique) —
            // pas d'itération de Statuses hors-boucle.
            return lst?.StateLocks ?? 0;
        };
        // player_spells modifié (learn/upgrade) → invalide le cache
        // de niveaux de sorts de l'EntryState live.
        _services.SpellLevelsDirty = charId =>
            _ = RunOnLoopAsync(_ =>
            {
                var lst = _states.Values.FirstOrDefault(
                    e => e.CharId == charId);
                if (lst != null)
                {
                    lst.SpellLevels = null;
                    lst.EntryDirty = true; // sorts du bundle périmés
                }
                return Task.CompletedTask;
            });
        // 302 du mot d'état (bit ride) : les handlers items stateless
        // (ItemActionHandlers montée/descente, CheckRide du move item)
        // délèguent — seq char+0x280 + CanSee vivent sur la boucle.
        _services.RideFlagNotify = (s, charId, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? NotifyFlagChangeAsync(s, est, ct)
                : Task.CompletedTask, token);
        // Montée/descente complètes — mêmes délégations boucle :
        // le statut de ride (st.Statuses) vit sur l'EntryState.
        _services.RideApply = (s, charId, serial, enh, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? ApplyRideEnchantAsync(s, est, serial, enh, ct)
                : Task.CompletedTask, token);
        _services.RideRemove = (s, charId, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? DismountAsync(s, est, charId, ct)
                : Task.CompletedTask, token);
        // Usage d'un item œuf kind 60 → AddElf natif (le handler
        // ItemUse stateless délègue : l'elf vit dans l'EntryState).
        _services.ElfUseItem = (s, charId, egg, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? AddElfFromItemAsync(s, est, egg, ct)
                : Task.FromResult(false), token);
        // Positions d'équipe (392) — NotifyTeamMemberPos natif : les
        // handlers Team (stateless) délèguent — les EntryState des
        // coéquipiers vivent sur la boucle.
        _services.TeamMemberPositions = (s, charId, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? SendTeamMemberPositionsAsync(s, est, ct)
                : Task.CompletedTask, token);
        // Livre de sorts pet (UseType 6 → UseItemToElfControl natif) :
        // le handler ItemUse délègue — LearnSpell/LearnSpellFromGroup
        // mutent les vecteurs de l'ElfSlot + émettent le 588.
        _services.ElfBookApply = (s, charId, enh, elfLoc, token) =>
            RunOnLoopAsync(ct => _states.TryGetValue(s.SessionId,
                    out var est)
                ? ApplyElfSpellBookAsync(s, est, enh, elfLoc, ct)
                : Task.FromResult(false), token);
        // Appartenance à une équipe → clé d'instance (CNodeGroup
        // natif) : join/leave/kick/disband recalculent la copie de
        // donjon du perso et détruisent le groupe devenu vide.
        _services.Teams.MembershipChanged = (cid, leftTeam) =>
            _ = RunOnLoopAsync(_ =>
            {
                OnTeamMembershipChanged(cid, leftTeam);
                return Task.CompletedTask;
            });
        // NPCs de hall de famille — injection/retrait dans la vue
        // d'un perso pour les commandes GM place_npc/replace_npc
        // (les handlers GM tournent déjà sur la boucle — mutation
        // directe de l'EntryState passé en Context.State).
        _services.HallNpcInject = (stObj, tid, x, y, z, f) =>
            stObj is EntryState he
                ? InjectHallNpc(he, tid, x, y, z, f) : 0u;
        _services.HallNpcRemove = (stObj, x, y, r) =>
            stObj is EntryState re
                ? RemoveHallNpcFromView(re, x, y, r) : 0u;
        _mission.PacketReceived += OnMissionPacket;
        _mission.Reconnected += () => _ = AnnounceNodesAsync();
    }

    

    /// <summary>Exécute <paramref name="work"/> sur le fil de la
    /// boucle de zone — c'est LE point de routage des mutations
    /// d'<see cref="EntryState"/> : sessions, callbacks de services
    /// et liens internes postent leur logique ici au lieu de muter
    /// depuis leur thread. Sans boucle branchée (tests) : exécution
    /// en ligne, comportement inchangé.</summary>
    private Task RunOnLoopAsync(Func<CancellationToken, Task> work,
        CancellationToken token = default)
        => _services.Loop is { } loop
            ? loop.PostOrRunAsync(work, token)
            : work(token);

    /// <summary>Variante à valeur de retour de
    /// <see cref="RunOnLoopAsync"/>.</summary>
    private async Task<T> RunOnLoopAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken token = default)
    {
        if (_services.Loop is { } loop && !loop.IsOnLoopThread)
        {
            var tcs = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await loop.PostAsync(async ct =>
            {
                try { tcs.SetResult(await work(ct)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, token);
            return await tcs.Task;
        }
        return await work(token);
    }

    /// <summary>Planifie un événement différé sur la boucle de zone —
    /// les mutations de jeu s'exécutent alors sur le fil unique. Sans
    /// boucle (tests, flow non branché), repli Task.Delay : même
    /// sémantique que l'ancien fire-and-forget.</summary>
    private void ScheduleOnLoop(TimeSpan delay,
        Func<CancellationToken, Task> work,
        CancellationToken token = default)
    {
        if (_services.Loop is { } loop)
            loop.Schedule(delay, work, token);
        else
            _ = FallbackDelayAsync(delay, work, token);
    }

    private static async Task FallbackDelayAsync(TimeSpan delay,
        Func<CancellationToken, Task> work, CancellationToken token)
    {
        try { await Task.Delay(delay, token); await work(token); }
        catch { /* annulé ou session morte */ }
    }


    /// <summary>Retire l'état + notifie le monde (logout) — à appeler à la déco.
    /// Persiste TOUT l'état (SaveFullStateAsync : node/pos/hp/mp/exp/level/
    /// class_id) — avant, seules les coords partaient et un kill du
    /// process perdait niveau, PV et map (constaté live). Le flush
    /// write-behind précède la sauvegarde complète sur la même file
    /// ordonnée : un état sale ne peut pas écraser le save final.</summary>
    public async Task OnDisconnectAsync(NetSession session)
    {
        if (_states.TryRemove(session.SessionId, out var st) && st.CharId != 0)
        {
            st.LoopCts?.Cancel();
            st.CastCts?.Cancel();               // cast temporisé en vol
            st.PendingLottery?.Cts?.Cancel();   // cast autel en vol
            st.PendingCube?.Cts?.Cancel();      // cast cube Horadric en vol
            st.PendingMagicBox?.Cts?.Cancel();  // cast boîte magique en vol
            st.PendingLifeSkill?.Cts?.Cancel(); // cast métier en vol
            st.PendingCombine?.Cts?.Cancel();   // cast station combine
            foreach (var c in st.WorkCts) c?.Cancel(); // timers works
            // Nettoyage + despawn des pets sur le fil de la boucle —
            // mutations d'EntryState/registry, jamais sur le thread
            // de déconnexion.
            await RunOnLoopAsync(async _ =>
            {
                CleanupStallOnDisconnect(st);     // étal ouvert / vue
                // Trade zombie : un trade jamais annulé à la déco
                // restait en base (active_trades) et captait les ops
                // du perso au login suivant — GetActiveTradeByPlayer
                // LIMIT 1 sans ordre pouvait renvoyer le vieux trade
                // (constat live : partner_confirmed/gold du trade
                // courant écrivaient dans le zombie → échange muet).
                // CancelTradeAsync rend les items réservés aux deux.
                try { await _services.Trade.CancelTradeAsync(
                    (int)st.CharId, 0); }
                catch { /* best-effort */ }
                // Enchants de session (procs flag 0x1000 natif) — le
                // CEnchantMgr natif meurt avec le perso ; sans purge,
                // le stage « buffs » compterait encore leurs stats
                // pour un perso hors-ligne puis à la reconnexion.
                _services.SessionEnchants.TryRemove((int)st.CharId,
                    out var _sessionMap);
                // État ride — le CEnchant natif survit en DB
                // (player_enchants) mais l'état volatile char+0x7f4
                // meurt avec le perso hors-ligne ; la restauration
                // au prochain login le re-peuple via le hook 2134.
                _services.RideStates.TryRemove((int)st.CharId,
                    out var _ride);
                // CColosseumFactory::CharacterLeave natif : file et
                // match en cours retirés à la déconnexion.
                _services.Colosseum.CharacterLeave((int)st.CharId);
                // Idem CVersusInstanceFactory : file + instance
                // versus purgées (session morte — pas de 601 reset).
                _services.Versus.CharacterLeave((int)st.CharId);
                // Idem CBattleRoyaleFactory : file + BR.
                _services.BattleRoyale.CharacterLeave((int)st.CharId);
                // CEvaluate::CharacterLeave natif : sortie de trial à
                // la déconnexion — auto-claim des gains par mail si la
                // loterie était ouverte (status 2). Les packets de
                // reset vers le client mort sont ignorés en interne.
                try
                {
                    await CharacterLeaveEvaluateAsync(session, st,
                        CancellationToken.None);
                }
                catch { /* session déjà morte */ }
                // Duel en cours → DuelFinish(char) natif sur quit :
                // l'adversaire gagne par forfait (résultats 454).
                try { await FinishDuelAsync(session, st); }
                catch { }
                // Territory war : CharacterLeave natif retire le perso
                // du camp (les compteurs de kills survivent — ils
                // alimentent les émissions de fin).
                if (st.TerritoryId != 0)
                {
                    _services.Territory.Leave((int)st.CharId);
                    ClearTerritory(st);
                }
                // RecruitInstanceLogout natif : la file de recrutement
                // d'instance est purgée à la déconnexion.
                RecruitOnDisconnect(st);
                // RollitemManager::UserDisconnect natif : le perso
                // passe « pass » sur ses rolls en cours ; ceux
                // devenus complets se résolvent.
                foreach (var rid in _services.RollTreasure
                             .UserDisconnect((int)st.CharId))
                    await ResolveTreasureRollAsync(rid,
                        CancellationToken.None);
                // CNodeGroup natif : les instances du groupe sont
                // détruites quand plus aucun membre ne s'y trouve —
                // st est déjà retiré de _states, le scan ne compte
                // donc que les autres occupants (équipe qui reste
                // dans le donjon = conservation).
                if (st.InstanceGroup != 0 &&
                    !_states.Values.Any(o =>
                        o.InstanceGroup == st.InstanceGroup))
                    DestroyGroupInstances(st.InstanceGroup);
                // Pets invoqués : mort avec le maître hors-ligne (le
                // statut est purgé des DB à la déco — pas de pet
                // orphelin au spawn).
                await DespawnPetsAsync(st, -1, -1,
                    CancellationToken.None);
            });
            _ = _world.SendLogoutAsync(st.CharId);
            // caveLogout : CheckLogoutInCave natif — réécrit node/pos
            // persistés vers la revive_area si déco dans un nœud cave
            // {2,5,7,12,14}.
            await PersistStateAsync(st, caveLogout: true);
        }
    }

    /// <summary>Traite un paquet brut. true = consommé (ne pas dispatcher).
    /// Le traitement est routé sur la boucle de zone — les handlers
    /// mutent <see cref="EntryState"/> exclusivement sur le fil
    /// unique (modèle acteur). Phase 2 : tant que le chargement
    /// d'entrée (<see cref="EntryState.EntryLoad"/>) est en vol, la
    /// passerelle attend sa fin — install comprise — avant de poster
    /// le paquet sur la boucle ; l'ordre par session est préservé
    /// puisque la lecture réseau est séquentielle, et aucun handler
    /// ne voit un état à moitié chargé.</summary>
    public async Task<bool> TryHandleAsync(NetSession session,
        ushort opcode, byte[] payload, CancellationToken token)
    {
        var st = _states.GetOrAdd(session.SessionId, _ => new EntryState());
        if (st.EntryLoad is { } load && !load.IsCompleted)
        {
            try { await load.WaitAsync(token); }
            catch (OperationCanceledException) { throw; }
            catch { /* pipeline déjà loggé — la commande décide */ }
        }
        return await RunOnLoopAsync(ct => TryHandleOnLoopAsync(session,
            st, opcode, payload, ct), token);
    }

    /// <summary>Dispatch NCID → handler — s'exécute sur le fil de la
    /// boucle (<see cref="RunOnLoopAsync"/>), jamais sur le thread de
    /// lecture de session.</summary>
    private async Task<bool> TryHandleOnLoopAsync(NetSession session,
        EntryState st, ushort opcode, byte[] payload,
        CancellationToken token)
    {
        // Trace live des requêtes client — gated par EDEN_ZONE_TRACE=1 :
        // Console.WriteLine = lock global + alloc hex à CHAQUE paquet,
        // tous les threads de session se serialisent dessus sous charge.
        // Hors canaux verbeux (1=move, 57=echo, 123=turn).
        if (TraceInbound && opcode is not (1 or 57 or 123))
            EdenLog.Write($"[Zone-In] NCID={opcode} len={payload.Length} " +
                Convert.ToHexString(payload.AsSpan(0,
                    Math.Min(payload.Length, 32))));
        if (NetDebug.Enabled)
            EdenLog.Write($"[PKT-IN] NCID={opcode} {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 48)))}");
        switch (opcode)
        {
            case 0: // ReceiveTicket — la commande ne fait QUE poster
                    // le pipeline hors-boucle : RPC ticket + ~30-50
                    // opérations SQL ne bloquent plus les autres
                    // commandes (Phase 2). L'install atomique est
                    // repostée sur la boucle en fin de chargement ; la
                    // passerelle (EntryLoad) retient les paquets
                    // suivants jusqu'à elle.
                st.EntryLoad ??= StartEntryLoad(session, st, payload);
                return true;

            case 2: // WhoIs [i32 charId][u8 flag] → 263+301+326
                await HandleWhoIsAsync(session, st, payload, token);
                return true;

            case 57: // Echo — le natif pousse le burst d'entrée sur le 1er echo
                using (EdenMetrics.BeginDbOperation("zone_entry_burst"))
                    await HandleFirstEchoAsync(session, st, token);
                // A4.4 : pong RTT (custom Reborn, corps vide).
                await session.SendAsync(
                    new Handlers.CNC_CZ_ClientEchoPong(), token);
                return true;

            case 1:
                // MoveSelf — wire capture : [u16 n][n×(f32 x,f32 z)]
                // [f32 curX][f32 curZ][f32 tgtX][f32 tgtZ][f32 dir]
                // [s16 flag]. Réponse native : 264 seul (écho curX/curZ).
                await HandleMoveSelfAsync(session, st, payload, token);
                return true;

            case 3: // TextCommand [u16 len][utf8] — commandes admin/GM
                    // (gate privilege>0 — désasm Execute @0x8295120)
                await HandleTextCommandAsync(session, st, payload, token);
                return true;

            case 4: // MigrationDone → dump état + spawns dans la vue
                await HandleMigrationDoneAsync(session, st, token);
                return true;

            case OpAdvStatsQuery: // Extension Eden : stats avancées
                                  // (ZoneEntryFlow.AdvancedStats.cs)
                await HandleAdvancedStatsQueryAsync(session, st, token);
                return true;

            case 85: // EnchantCancel [s16 effectId] — clic droit sur
                     // l'icône de statut : CancelPlayerEnchant natif
                     // @0x805fe00 (gardes buff/abandon/ride) —
                     // ZoneEntryFlow.Status.cs.
                await HandleEnchantCancelAsync(session, st,
                    payload.Length >= 2
                        ? BitConverter.ToInt16(payload, 0) : (short)0,
                    token);
                return true;

            // ---- Familiers de combat (CKFElfManager natif — voir
            // ZoneEntryFlow.Elf.cs ; interceptés avant le dispatcher
            // car ils requièrent l'EntryState) ----
            case 110: // EnterNodeElfInfo → 412 tableau d'elfs complet
                await HandleEnterNodeElfInfoAsync(session, st, token);
                return true;

            case 109: // ElfCallElf [s16 loc][s16][s16][u8]
                await HandleElfCallElfAsync(session, st, payload, token);
                return true;

            case 108: // ElfReturn [s16 loc][u8][s16][s16]
                await HandleElfReturnAsync(session, st, payload, token);
                return true;

            case 107: // ElfWork [s16 loc][s16 mode] — ToWork natif :
                      // 0 = ToIdle, 1 = ToMix (intégration),
                      // 2 = CallElf ; loteries 7/8/9 → dispatcher.
                      // Gate de carte native (S_Node col42 →
                      // template+0x10 bits 0x20/0x40) appliquée à tous
                      // les modes avant ToWork — 8350/8351/8352.
                {
                    var wmode = payload.Length >= 4
                        ? BitConverter.ToInt16(payload, 2) : (short)-1;
                    if (wmode < 0)
                        return true;
                    if (!await CheckElfWorkMapGateAsync(session, st,
                            wmode, token))
                        return true;
                    if (wmode is 0 or 2)
                    {
                        await HandleElfWorkAsync(session, st, payload,
                            token);
                        return true;
                    }
                    if (wmode == 1 && payload.Length >= 2)
                    {
                        await HandleElfToMixAsync(session, st,
                            BitConverter.ToInt16(payload, 0), token);
                        return true;
                    }
                    return false;
                }

            case 187: // PetModeChange [s16 mode]
                await HandlePetModeChangeAsync(session, st, payload,
                    token);
                return true;

            case 188: // SendPetCmd [s16 cmd][i32][s16][i32]
                await HandleSendPetCmdAsync(session, st, payload,
                    token);
                return true;

            case 227: // ElfMix [s16 elf1][s16 elf2][s16 sac][s16 slot]
                      // — fusion d'elfs (CPetUpgradeOp natif, cast 4 s,
                      // 574/575/568). Requiert EntryState → ici.
                await HandleElfMixAsync(session, st, payload, token);
                return true;

            case 228: // ElfChangeName [s16 loc][string] — parchemin
                      // de renommage 40056/40156 requis (natif).
                await HandleElfChangeNameAsync(session, st, payload,
                    token);
                return true;

            case 15: // CheckNodeExit [i32 portalKey] — portail / transition de node
                await HandleNodeExitAsync(session, st, payload, token);
                return true;

            case 5: // MeleeAttack [u32 mobHandle][u8 type] — type 2 = stop
                await HandleMeleeAsync(session, st, payload, token);
                return true;

            case 9: // SpellCastToLife [u16 spellId][u32 mobHandle]
                if (!SpellAllowedForClass(st, payload))
                {
                    await RefuseSpellCastAsync(session, 9, payload, token);
                    return true;
                }
                await HandleSpellHitAsync(session, st, payload, token);
                return true;

            case 35: // ZoneServerTreasurePickup [u32 treasureId] —
                // ramassage d'un trésor au sol (CTreasure natif).
                await HandleTreasurePickupAsync(session, st, payload,
                    token);
                return true;

            case 193: // ZoneServerRollTreasure [u32 rollId][s16 choice]
                // — réponse need/greed/pass d'un roll de loot
                // (RollitemManager::CheckRollItem natif).
                await HandleRollTreasureAsync(session, st, payload,
                    token);
                return true;

            case 16: // SpellCastToLocation [u16 spell][i32 target][f32 x][f32 z]
                if (!SpellAllowedForClass(st, payload))
                {
                    await RefuseSpellCastAsync(session, 16, payload, token);
                    return true;
                }
                await HandleCastLocationAsync(session, st, payload, token);
                return true;

            case 17: // SpellCastToNowhere [u16 spell][i32]
                if (!SpellAllowedForClass(st, payload))
                {
                    await RefuseSpellCastAsync(session, 17, payload, token);
                    return true;
                }
                await HandleCastNowhereAsync(session, st, payload, token,
                    SpellCastRouting.NcidToNowhere);
                return true;

            case 59: // SpellCastToNearby — même wire que 17 mais gate
                     // forme 4 (CastToNearby @0x081536d0) ; le 410 de
                     // refus porte le ncid 59, pas 17.
                if (!SpellAllowedForClass(st, payload))
                {
                    await RefuseSpellCastAsync(session, 59, payload, token);
                    return true;
                }
                await HandleCastNowhereAsync(session, st, payload, token,
                    SpellCastRouting.NcidToNearby);
                return true;

            case 241: // BattleRoyaleJoinRequest [i32 join][i32 leave]
                // — gates Execute @0x0823da10 puis file locale
                // (BattleRoyaleService, JoinWait natif, groupe 30).
                await OnBattleRoyaleJoinAsync(session, st,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 4) : 0,
                    token);
                return true;

            case 252: // VersusInstanceJoinRequest [i32 v1][i32 v2] —
                // Execute natif @0x82a0670 : v2≠0 + in-versus
                // (scene_type==0x11) → LeaveVersusInstance ;
                // v1==0 → sortie de file ; v1≠0 → validations
                // (niveau, état, type de scène, enchant, sched 602)
                // puis file locale (VersusService, JoinWait natif).
                await OnVersusJoinRequestAsync(session, st,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 4) : 0,
                    token);
                return true;

            // ---- Salles personnelles (registre CZoneServer+0x564) ----
            case 207: // EnterRoom [i32 roomId][s16 hotel_type] —
                // EnterRoomCheck natif @0x814eaf0.
                return await HandleEnterRoomAsync(session, st,
                    payload, token);
            case 210: // RoomGetData [] — sert le CPlayerRoom
                // (+conteneurs vides) en 541 depuis le registre.
                return await HandleRoomGetDataAsync(session, st,
                    token);
            // ---- Hall de famille (CPlayerFactory::FamilyInstance*) ----
            case 173: // FamilyInstanceCreate [s16 type][u8 vip] —
                // gates zone natifs puis relais ZW texte vers le monde.
                return await HandleFamilyInstanceCreateAsync(session, st,
                    payload, token);
            case 132: // FamilyInstanceGetData [] — 487 bâtiments du
                // hall visité (char+0xaac natif).
                return await HandleFamilyInstanceGetDataAsync(session, st,
                    token);
            case 176: // FamilyTownEnter [i32 familyId] — visite/sortie
                // du hall (VisitFamilyInstance + char+0xa98 natif).
                return await HandleFamilyTownEnterAsync(session, st,
                    payload, token);
            case 174: // FamilyTownQuery [s16 min][s16 max][i32 mask]
                // [str filtre] — recherche de ville, réponses 502
                // chunkées ×10 (FamilyTownQuery @0x8110830).
                return await HandleFamilyTownQueryAsync(session, st,
                    payload, token);
            case 175: // FamilyTownMemberQuery [i32 famId] — membres
                // de la ville (TC_FamilyTownMemberQuery mission
                // @0x820a670) → 503.
                return await HandleFamilyTownMemberQueryAsync(session,
                    st, payload, token);
            case 130: // FamilyContainerPutItem
                // [s16 mode][s16 cont][s16 loc][s16 bag][s16 slot] —
                // dépose dans le hall (op 0x3b requise, @0x8255c00).
                return await HandleFamilyContainerPutAsync(session, st,
                    payload, token);
            case 131: // FamilyContainerGetItem
                // [s16 cont][s16 loc][s16 bag][s16 slot] — retrait du
                // hall vers l'inventaire (@0x82541c0).
                return await HandleFamilyContainerGetAsync(session, st,
                    payload, token);
            case 135: // DuelRequest [i32 cible] — validations
                // DuelRequest @0x080f2bf0 → 451 à la cible.
                await HandleDuelRequestAsync(session, st, payload, token);
                return true;
            case 136: // DuelRequestAnswer [s16 accept][i32 demandeur]
                // — AddToDuel (453 + countdown 5 s) ou DuelReject (452).
                await HandleDuelAnswerAsync(session, st, payload, token);
                return true;
            case 161: // FamilyStatue
                // [i32 target][i32 pos][str name][i32 rot][f32 frame] —
                // statue de membre du hall (@0x8257f10).
                return await HandleFamilyStatueAsync(session, st,
                    payload, token);
            case 189: // TerritoryQuery [] — 515 données territoires
                // (Execute zone @0x82944a0).
                return await HandleTerritoryQueryAsync(session, st,
                    token);
            case 190: // TerritoryJoin [i32 terrId][s16 type] —
                // join/leave guerre de territoire (@0x8249970).
                return await HandleTerritoryJoinAsync(session, st,
                    payload, token);
            case 225: // FamilyBattleAskInfo [s16 type] — info/join
                // élimination de familles (@0x8253740).
                return await HandleFamilyBattleAskInfoAsync(session,
                    st, payload, token);

            // ---- Colosseum (arène PvP — ZoneEntryFlow.Colosseum.cs) ----
            case 196: // GladiatorTeamCreate [str][u8] — no-op natif
            case 197: // GladiatorTeamQuery [] — idem @0x825dd10
                return HandleGladiatorDead();
            case 198: // ColosseumJoinAnswer [s16 accept]
                return await HandleColosseumJoinAnswerAsync(session,
                    st, payload, token);
            case 199: // ColosseumJoinRequest [] — gates + file
                return await HandleColosseumJoinRequestAsync(session,
                    st, token);
            case 200: // ColosseumTeamQuery [] — 529 équipes du match
                return await HandleColosseumTeamQueryAsync(session,
                    st, token);
            case 201: // ColosseumLeaveJoin [] — sortie de file
                return await HandleColosseumLeaveJoinAsync(session,
                    st, token);
            case 219: // ColosseumModeQuery [] — 587 calendrier + 552
                return await HandleColosseumModeQueryAsync(session,
                    st, token);

            case 194: // RankInfoQuery [s16 type][s16 ver] —
                // boucle de réponse du natif (521 depuis le cache
                // alimenté par les pushes ZMis 763).
                await HandleRankInfoQueryAsync(session, payload,
                    token);
                return true;
            case 231: // PVPRankLookEquip [s16 type][str name] → 571
                await HandlePvpRankLookEquipAsync(session, payload,
                    token);
                return true;

            // ---- Tenues rapides (fast dress) ----
            case 243: // EnterNodeFastDress [] → 593 records live
                await HandleFastDressEnterAsync(session, st, token);
                return true;
            case 244: // ApplyFastDressData [i32 page][i32 mask]
                await HandleFastDressApplyAsync(session, st,
                    payload.Length >= 4
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 4) : 0,
                    token);
                return true;
            case 245: // SaveFastDressData [i32 page] → 594 mode 0
                await HandleFastDressSaveAsync(session, st,
                    payload.Length >= 4
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    token);
                return true;
            case 246: // RecordFastDressInfo 5×i32
                      // [con][slot][idx][clé][page]
                await HandleFastDressRecordAsync(session, st,
                    payload.Length >= 4
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    payload.Length >= 8
                        ? BitConverter.ToInt32(payload, 4) : 0,
                    payload.Length >= 12
                        ? BitConverter.ToInt32(payload, 8) : 0,
                    payload.Length >= 16
                        ? BitConverter.ToInt32(payload, 12) : 0,
                    payload.Length >= 20
                        ? BitConverter.ToInt32(payload, 16) : 0,
                    token);
                return true;
            case 247: // UpdateFastDressPage [i32 page] → 594 mode 3
                await HandleFastDressPageUpdateAsync(session, st,
                    payload.Length >= 4
                        ? BitConverter.ToInt32(payload, 0) : 0,
                    token);
                return true;
            case 248: // RemoveFastDressInfo [s16 all][i32 page][i32 clé]
                await HandleFastDressRemoveAsync(session, st,
                    payload.Length >= 2
                        ? BitConverter.ToInt16(payload, 0) : (short)0,
                    payload.Length >= 6
                        ? BitConverter.ToInt32(payload, 2) : 0,
                    payload.Length >= 10
                        ? BitConverter.ToInt32(payload, 6) : 0,
                    token);
                return true;

            // ---- Chemin d'éveil (destiny road) ----
            case 203: // DestinyDice [s16 op][s16 p2][s16 p3] —
                // DestinyRoadAction natif @0x0810d610.
                await HandleDestinyDiceAsync(session, st,
                    payload.Length >= 2
                        ? BitConverter.ToInt16(payload, 0) : (short)0,
                    payload.Length >= 4
                        ? BitConverter.ToInt16(payload, 2) : (short)0,
                    payload.Length >= 6
                        ? BitConverter.ToInt16(payload, 4) : (short)0,
                    token);
                return true;
            case 204: // QueryDestinyRoad → 534 si route active
                // (Execute natif @0x08275db0).
                await HandleQueryDestinyRoadAsync(session, st, token);
                return true;

            // ---- Sac chanceux (chance lucky bag) ----
            case 221: // ChanceLuckyBagGetReward [] — récupère le lot
                // en attente (ChanceLuckyBagGetItem natif @0x080bf220).
                await HandleLuckyBagGetRewardAsync(session, st, token);
                return true;
            case 222: // ChanceLuckyBagReTry [] — relance le dé
                // (ChanceLuckyBagNext natif @0x08097180).
                await HandleLuckyBagRetryAsync(session, st, token);
                return true;
            case 223: // QueryFortuneBagCounter [] — 557 avec la map
                // char+0x574 entière (Execute natif @0x08277f00).
                await HandleFortuneCounterQueryAsync(session, st,
                    token);
                return true;

            case 558: // QueryRecommendActivities [u8 flag][u16 id] —
                // flag=0 : liste 559 (Execute @zone : sert le cache
                // char+0xc8c) ; flag!=0 : RecommendActivitiesCheckFinished
                // @0x840be30 (réclamation + GiveReward @0x810ba80).
                await OnQueryRecommendActivitiesAsync(session, st,
                    payload.Length > 0 ? payload[0] : (byte)0,
                    payload.Length >= 3
                        ? BitConverter.ToUInt16(payload, 1) : (ushort)0,
                    token);
                return true;

            case 13: // SpellBreak — annulation manuelle du cast.
                // Désasm ZoneServerSpellBreak::Execute @0x8289d70 →
                // CSpellFactory::SpellBreak(caster, caster, 0) → break
                // de l'ISpell +0x94 → NotifyBreak → 287 ClientSpellBreak
                // `[i32 caster][s16 spell][u8 0]` au client + CanSee.
                await BreakCastAsync(session, st, token);
                return true;

            case 123: // TurnSelf [f32 dir] → 441 TurnChar broadcast self
                await HandleTurnSelfAsync(session, st, payload, token);
                return true;

            case 88: // Transport [u8 nodeId][i32 param] — array de transport
                await HandleTransportAsync(session, st, payload, token);
                return true;

            // 22/23 BuyItems/SellItems : NE PAS intercepter — le typed
            // dispatcher route vers ShopHandlers (ShopService.BuyItemAsync/
            // SellItemAsync avec validation or/slot/inventaire réels).
            case 22:
            case 23:
                return false;

            case 34: // ChannelMsg [u8 ch][str][str msg][s16][s16][u32][blob]
                await HandleChannelMsgAsync(session, st, payload, token);
                return true;

            case 63: // LeaveShop [u16]
                HandleLeaveShop(session, st);
                return true;

            case 160: // MissionGoto [s16 mid][s16 type][s16 npc][s16 flag]
                await HandleMissionGotoAsync(session, st, payload, token);
                return true;

            case 50: // QueryMissionStates → 348 ClientMissionStates
                     // construit depuis la DB — le natif le bâtit
                     // LOCALEMENT depuis les maps du personnage
                     // (CNE_CZ_ZoneServerQueryMissionStates::Execute
                     // @0x827a860 : copie char+0x828/0x840/…), pas de
                     // round-trip mission. Intercepté AVANT le replay
                     // sinon le client rejoue les quêtes de test3
                     // (quête 1009) au lieu des siennes (1010).
                await HandleQueryMissionStatesAsync(session, st, token);
                return true;

            case 165: // QueryClosets [] → 488 ClientPlayerClosets
                await HandleQueryClosetsAsync(session, st, token);
                return true;

            case 166: // QueryClassList [u8] → 489 PlayerClassList
                await HandleQueryClassListAsync(session, st, token);
                return true;

            case 167: // ClosetSet [u8 idx] → couleurs + 283 + 489
                await HandleClosetSetAsync(session, st, payload,
                    token);
                return true;

            case 168: // ChangeClass [u8 type][u16 classId][u8]
                await HandleChangeClassAsync(session, st, payload, token);
                return true;

            case 181: // QueryLicense [] → 509 licences possédées
                await HandleQueryLicenseAsync(session, st, token);
                return true;

            case 182: // PlayerLicense [u16 n]{s16 lic,u8 slot}[s16 class]
                await HandlePlayerLicenseAsync(session, st, payload, token);
                return true;

            case 183: // QueryLicenseSet [u16 n]{u16 lic}+{u16 flag}
                await HandleQueryLicenseSetAsync(session, st, payload,
                    token);
                return true;

            case 70: // ItemMallReceivableEnter — pose l'opération
                     // CItemMallReceivableOp native (IsA = 0x2f) ;
                     // Cancel() natif = succès immédiat (désasm Execute
                     // @0x82… : CreateOperation si op courante != 0x2f).
                st.ShopOpType = ItemMallReceivableOpType;
                return true;

            case 170: // PlayerWork [u32 id][s16×4 locs] → ToWork natif
                await HandlePlayerWorkAsync(session, st, payload, token);
                return true;

            // ---- Étal joueur (CStall natif — ZoneEntryFlow.Vending) ----
            case 71: // AskVendor — Execute natif = no-op effectif
                await HandleVendorAskAsync(session, st, payload);
                return true;

            case 72: // AskVendorContainer [i32 vendeur] → vue d'étal
                await HandleAskVendorContainerAsync(session, st, payload,
                    token);
                return true;

            case 74: // VendorClose [u8 mode][u8] → fermeture vendeur
                await HandleVendorCloseAsync(session, st, payload, token);
                return true;

            case 75: // VendorCloseView [i32 vendeur] → reset op 0x2d
                await HandleVendorCloseViewAsync(session, st, payload);
                return true;

            case 76: // VendorBuy [i32 vendeur][u16 n][n×(s16,u8)]
                await HandleVendorBuyAsync(session, st, payload, token);
                return true;

            case 77: // VendorSellItem [i32 région][str][u8 n][n×10o]
                await HandleVendorSellItemAsync(session, st, payload,
                    token);
                return true;

            case 78: // VendorCloseVending — méthode usine = no-op natif
            case 80: // VendorUseItem — méthode usine = no-op natif
                return true;

            case 171: // PlayerWorkResultOK [s16 workNum] → claim
                await HandleWorkResultOKAsync(session, st, payload, token);
                return true;

            case 172: // PlayerWorkCheckData [] → 498 par terminé + 497
                await HandleWorkCheckDataAsync(session, st, token);
                return true;

            case 148: // SetAAPoint [s16 effectId][s16 classId]
                await HandleSetAaPointAsync(session, st, payload, token);
                return true;

            // ---- Collection de cartes (ZoneEntryFlow.Collection) ----
            case 150: // SaveCollection [u8 flag][(container,loc) |
                      // vec containers + vec locs] → 275 + 472 type 1
                await HandleSaveCollectionAsync(session, st, payload,
                    token);
                return true;

            case 151: // GetCollection [s16 item_id] → 472 type 2 + 274
                await HandleGetCollectionAsync(session, st, payload,
                    token);
                return true;

            case 152: // QueryCollection [] → 472 type 0 liste complète
                await HandleQueryCollectionAsync(session, st, token);
                return true;

            case 164: // UpdateBotData [u8][u8] — flags bot client
                await HandleUpdateBotDataAsync(session, st, payload,
                    token);
                return true;

            case 162: // QueryBattlefieldData [] → 482 stats bf_*
                await HandleQueryBattlefieldDataAsync(session, st,
                    token);
                return true;

            case 218: // NewPlayerGuide [s16 op][u16 id] — tutoriel
                await HandleNewPlayerGuideAsync(session, st, payload,
                    token);
                return true;

            case 258: // DailyAwardOP [s16 op][i32 param] → 615
                await HandleDailyAwardOpAsync(session, st, payload,
                    token);
                return true;

            case 149: // SetAARate [s16 rate] — no-op natif
                await HandleSetAaRateAsync(session, st, payload, token);
                return true;

            case 242: // PlayerCCDataSave — flux natif complet
                      // (Execute @0x8272bc0) : gates → enchant 46605 →
                      // carte → SwitchClassEquip → 592 → ModelModify.
                await HandlePlayerCCDataSaveAsync(session, st, payload,
                    token);
                return true;

            case 14: // PlayerAction [u8 action][i32][i32][i32] —
                     // CNE_CZ_ZoneServerPlayerAction::Execute désassemblé
                     // (0x826fbd0). Le SILENCE d'avant figeait le client :
                     // sans écho serveur il n'anime pas et coupe la
                     // session (log 18:4x : NCID=14 act=4 → déconnexion
                     // immédiate).
                await HandlePlayerActionEntryAsync(session, st, payload,
                    token);
                return true;

            case 119: // StrengthenItem → CItemFactory::StrengthenItem
                await HandleStrengthenItemAsync(session, st, payload, token);
                return true;

            case 120: // StrengthenClose → ResetOperation + cancel
                await HandleStrengthenCloseAsync(session, st, payload, token);
                return true;

            case 67: // InlayItem → op CInlayShopOp (0x23) +
                     // 371 ClientInlayItemResult (stub natif).
                await HandleInlayItemAsync(session, st, payload, token);
                return true;

            case 257: // ExecuteMagicBox → op 0x58 (boîte magique)
                await HandleExecuteMagicBoxAsync(session, st, payload, token);
                return true;

            case 232: // VerifyCubeCosts → op 0x52 (cube Horadric)
                await HandleVerifyCubeCostsAsync(session, st, payload, token);
                return true;

            case 213: // QueryLifeSkill → 543 ClientPlayerLifeSkill
                await HandleQueryLifeSkillAsync(session, st, token);
                return true;

            case 214: // LifeSkill [u8 op][u16 id][s16 sac][s16 slot]
                await HandleLifeSkillAsync(session, st, payload, token);
                return true;

            case 95: // MailOpenBox [s16 op] — 150/151 = fenêtres
                     // lottery natif (CInGameLotteryOp/CBaseLotteryOp).
                     // Les autres ops (7 mail, 22 métier, 152…)
                     // repartent au dispatcher legacy.
                return await TryHandleLotteryOpenAsync(
                    session, st, payload, token);

            case 129: // GoLottery — CNE_CZ_ZoneServerGoLottery
                      // [s16 autel][u16 item][s16 req][s16 claim]
                      // [s16 slot][u32 bag] — dispatch natif
                      // Go/GoInGame/GoDiamond + Receive(WeekCount)Item.
                await HandleGoLotteryAsync(session, st, payload, token);
                return true;

            case 184: // CheckUpdateLottery [u16][u16 version][s16 autel]
                      // → 511 si la version client diffère.
                await HandleCheckUpdateLotteryAsync(
                    session, st, payload, token);
                return true;

            case 224: // UpdateLotteryBonus [s16 autel][u16 item]
                      // [s16 counts] — anti-dérive du bonus exact.
                await HandleUpdateLotteryBonusAsync(
                    session, st, payload, token);
                return true;

            case 253: // PayEventSelect [u32 event][s16 choose] —
                      // TriggerPayEvent natif complet (coûts → récomp.).
                await HandlePayEventSelectAsync(
                    session, st, payload, token);
                return true;

            case 79: // TeamOperation [s16 op][s16 p1][s16 p2] — les
                     // ops evaluate (4 p1=3 check, 5 réponse) ont
                     // besoin de l'EntryState ; le reste repart au
                     // dispatcher legacy (loot/roll/raid).
                return await TryHandleEvaluateTeamOpAsync(
                    session, st, payload, token);

            case 255: // EvaluateLotteryOpen [i32 value] — loterie du
                      // gladiator trial (CEvaluate::LotteryOpen).
                await HandleEvaluateLotteryAsync(session, st, payload,
                    token);
                return true;
            case 220: // RecruitInstance — tableau de recrutement
                      // d'instance (wire variable, codec dédié).
                await HandleRecruitInstanceAsync(session, st, payload,
                    token);
                return true;
            case 163: // NodeGroupReset — payload vide, reset des
                      // instances de l'équipe.
                await HandleNodeGroupResetAsync(session, st, token);
                return true;

            case 93: // UpdateAppellation [u16 titre] —
                     // SetPresentAppellation natif @0x080f2400 : vérif
                     // possession, seq++, 397 joueur + 396 voyants,
                     // persist player_characters.appellation.
                await HandleSetAppellationAsync(session, st, payload,
                    token);
                return true;

            case 94: // QueryAppellation [u32 charId cible] —
                     // répond 396 + 280 attr 0x2c (zone @0x082748c0).
                await HandleQueryAppellationAsync(session, st, payload,
                    token);
                return true;

            case 20: // Revive — respawn joueur au born_area
            case 44: // Resurrect — variante du client Reforged ; même
                     // séquence native. Routé ici car le handler
                     // PlayerHandlers testait e.IsDead (toujours faux :
                     // le combat écrit st.PlayerHp, pas e.CurrentHp)
                     // → silence total = revive jamais traité.
                await HandleReviveAsync(session, st, payload, token);
                return true;

            case 124: // TargetUpdate [i32 handle] → 444 lock report
                await HandleTargetUpdateAsync(session, st, payload, token);
                return true;

            case 18: // EnchantUpdate [i32 handle] → 301 enchants visibles
                await HandleEnchantUpdateAsync(session, st, payload, token);
                return true;

            case 10: // TalkToNPC [i32 sightHandle]
                await HandleTalkToNpcAsync(session, st, payload, token);
                return true;

            case 11: // ReplyDialog [s16 optionIndex]
                await HandleReplyDialogAsync(session, st, payload, token);
                return true;
        }

        // Requêtes d'entrée avec réponses capturées — replay patché.
        if (st.CharId != 0 && EntryReplayData.Map.TryGetValue(opcode, out var resps))
        {
            foreach (var hx in resps)
                await SendPatchedAsync(session, st, hx, token);
            return true;
        }

        // Commande texte client (/xxx) — interceptée pour les tests live.
        if (opcode == 3)
        {
            await HandleTextCommandAsync(session, st, payload, token);
            return true;
        }

        // Requêtes natives silencieuses (aucune réponse dans la
        // capture). 186 SetMissionTrace a un handler réel
        // (TeamHandlers — persistance via AgentDBA) : il passe par
        // le dispatcher comme tout C→S traité.
        if (opcode is 104 or 156 or 172 or 204)
            return true;

        return false;
    }

    /// <summary>NCID 57 Echo — le natif pousse le burst d'entrée
    /// (replay 57 : 302/313/404 + ResetTime monde) sur le PREMIER echo
    /// post-ticket ; les suivants sont ignorés.</summary>
    private async Task HandleFirstEchoAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0 || st.BurstSent) return;
        st.BurstSent = true;
        foreach (var hx in EntryReplayData.Map[57])
            await SendPatchedAsync(session, st, hx, token);
        if (_world.Connected)
            await _world.SendResetTimeAsync(st.CharId, token);
        EdenLog.Write(
            $"[Zone-Entry] burst envoyé + ResetTime char=#{st.CharId}");
    }


    /// <summary>NCID 123 TurnSelf [f32 dir] → 441 TurnChar
    /// `[u32 char][f32 dir]` — natif : stocke la direction en
    /// char+0x58 puis <b>BroadCastToCanSee</b> (Execute @0x829a390) —
    /// la grille inclut l'acteur lui-même (distance 0) donc l'écho
    /// revient à l'émetteur ET aux observateurs. Sans le broadcast les
    /// témoins ne voient jamais la rotation du skin (monture en
    /// strafe : corps figé pendant que la tête anime).</summary>
    private async Task HandleTurnSelfAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0) return;
        st.Facing = BitConverter.ToSingle(payload, 0);
        var turn = new CNC_CZ_ClientTurnChar
        {
            Field1 = (int)st.CharId,
            Field2 = st.Facing,
        };
        await session.SendAsync(turn, token);
        await BroadcastToNodeAsync(session, st, turn, token);
    }

    /// <summary>NCID 63 LeaveShop — ferme le contexte boutique
    /// (OpenShopId de l'EntryState + CurrentStoreId de la session
    /// utilisé par le dispatcher des achats PNJ).</summary>
    private static void HandleLeaveShop(NetSession session, EntryState st)
    {
        st.OpenShopId = -1;
        session.CurrentStoreId = 0;
    }

    /// <summary>NCID 14 PlayerAction [u8 action][i32][i32][i32] —
    /// les codes d'ouverture d'opération (13 fortify, 14 combine,
    /// 16 métier, 101 cube) partent à TryPlayerActionOpAsync ; les
    /// actions de pose (4 sit / 5 stand / 8 spawn) émettent le
    /// 290 ClientPlayerAction à l'acteur + observateurs.</summary>
    private async Task HandlePlayerActionEntryAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 13 || st.CharId == 0) return;
        var act = payload[0];
        // Codes d'ouverture d'opération natifs
        // (SimpleAction @0x8115a10) : ils créent l'op et envoient
        // 432, jamais de broadcast.
        if (await TryPlayerActionOpAsync(session, st, act,
                BitConverter.ToInt32(payload, 1),
                BitConverter.ToInt32(payload, 5), token))
            return;
        var actionTarget = BitConverter.ToInt32(payload, 9);
        if (act == 4) st.Sitting = true;
        else if (act == 5) st.Sitting = false;
        await SendPlayerActionAsync(session, st, act, actionTarget, token);
    }

    /// <summary>NCID 124 TargetUpdate [i32 handle] → 444 =
    /// [i32 handle][u8][i32 who] (capture : bc01 &lt;mob&gt; 00
    /// 02c2eb0b = le mob cible le perso — field3 = charId cible
    /// courante du mob, pas l'émetteur).</summary>
    private async Task HandleTargetUpdateAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0) return;
        var th = BitConverter.ToUInt32(payload, 0);
        st.TargetHandle = th;
        var mob = _services.Live.GetMob(th);
        await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
        {
            Field1 = (int)th,                       // u32 : handle cible
            Field2 = 0,                             // u8 : 0 capturé
            Field3 = mob?.TargetCharId ?? 0,        // i32 : cible du mob
        }, token);
    }

    // Ticket 22B : [u32 charId][u32 clientIp][u32 zoneIp][u16 port][u32 t1][u32 t2]
    




    
    private static byte[] Concat(params byte[][] parts)
    {
        var b = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }
}
