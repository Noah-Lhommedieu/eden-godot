using System.Collections.Concurrent;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Moteur d'événements de scène — exécution complète des scripts
/// <c>[event]</c> (branches {conditions → actions}).
///
/// Preuve native : <c>CSceneEventCmds::ExecuteCommand</c>
/// (mission/decompiled.c @0x80c3620) — l'événement est une LISTE de
/// paires CEventCmd ; les conditions Event_If_* d'une paire doivent
/// toutes passer pour que ses actions jouent, sinon on teste la paire
/// suivante. Chez nous la zone exécute directement (le natif passe par
/// mission via la file <c>CheckTrigger</c> — même résultat observable).
///
/// Verbes implémentés (désasm <c>CSceneCond*/CSceneAction*</c>) :
///   conditions : Event_If_Level (op 1=≥ 2=≤ 3== 4=≠), If_EventState,
///                If_Variable, If_Monster (compte vivants du groupe),
///                If_RebornMonster (op 5 = groupe né, 6 = absent).
///   actions    : Event_Transport (arg0=node dest, désasm
///                CSceneActionTransport — « NodeEventID=node&lt;&lt;16|sel »),
///                Event_ScreenMessage (316), Event_RebornMonster
///                (arg1 5=spawn/6=despawn, arg2=8 clear immédiat),
///                Event_KillMonster, Event_BornNpc, Event_KillNpc,
///                Event_MonsterAction (495 + 268 MonsterSay),
///                Event_GiveBuff/Event_DeleteBuff (enchant joueur),
///                Event_TriggerEvent (récursion), Event_Variable,
///                Event_RanDomVariable, Event_EventState,
///                Event_SetAchievement(NPC) → journalisé (pas de
///                système d'achievements côté zone).
/// Plan 02 : conditions If_ScreenMessage (toujours vrai), If_BuffCheck,
/// If_BuffCheck6P, If_ItemCheck (<see cref="SceneEventRules"/>) ; actions
/// PlaySound (494), PlayEffect (383), ChannelMsg (316 monde) et
/// MonsterAction à verrou d'IA — corps « niveau node » dans
/// <c>ZoneEntryFlow.Events.Scene.cs</c>. Restent non portés (aucune
/// occurrence dans les 637 scènes) : Event_DeleteEffect/FogChange/
/// Timestamp, If_Timestamp.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>États <c>EventState</c> par (node,id) — les scripts de
    /// scène avancent une machine d'états partagée par tout le node
    /// (donjons, événements publics : « 幹部buff消除 » etc.).</summary>
    private static readonly ConcurrentDictionary<long, int> EventStates = new();

    /// <summary>Variables <c>Variable</c> par (node,id) — aléas
    /// d'embranchement (Event_RanDomVariable + If_Variable).</summary>
    private static readonly ConcurrentDictionary<long, int> EventVars = new();

    /// <summary>Groupes reborn « nés » par (node,spawnId) — cond
    /// <c>Event_If_RebornMonster id 5|6</c> teste ce drapeau
    /// (reborn+0x6a bit0 natif).</summary>
    private static readonly ConcurrentDictionary<long, bool> RebornGroups =
        new();

    private static long EvKey(int node, int id) => (long)node << 32 | (uint)id;

    /// <summary>Détection de franchissement d'event-areas au mouvement —
    /// équivalent de la grille spatiale de <c>CCharacter::MoveSelf</c>
    /// natif (@0x844aae0 : chaque IGameObj de la cellule reçoit le
    /// déplacement, vtable+0x20 = test d'aire). Ne tire qu'à l'ENTRÉE
    /// (st.InsideAreas) :
    ///   • REVIVE → revive_area_id = node&lt;&lt;16|ReviveLinks[ea]
    ///     (LoadReviveArea : map[eventTraversée]=clé — persistance +
    ///     531 ClientReviveInfo comme SetReviveAreaID) ;
    ///   • NORMAL → last_normal_area_id = node&lt;&lt;16|NormalLinks[ea]
    ///     (LoadNormalArea — sauvegarde de repli « logout in cave ») ;
    ///   • GATEWAY/BORN : rien — le client pilote les portails (NCID
    ///     15 après validation TriggerAreaWnd), BORN ne sert qu'au spawn.
    /// </summary>
    private async Task CheckAreaEntryAsync(NetSession session, EntryState st,
        CancellationToken token)
    {
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        if (scene == null) return;
        var now = new HashSet<int>();
        foreach (var a in scene.EventAreas)
        {
            var dx = st.PosX - a.X; var dz = st.PosZ - a.Y;
            if (dx * dx + dz * dz <= a.Radius * a.Radius)
                now.Add(a.Id);
        }
        // Sortie d'aire → retrait (le prochain passage redéclenchera).
        st.InsideAreas.RemoveWhere(id => !now.Contains(id));
        foreach (var a in scene.EventAreas)
        {
            if (!now.Contains(a.Id) || !st.InsideAreas.Add(a.Id))
                continue;
            switch (a.Type)
            {
                case "REVIVE":
                    if (scene.ReviveLinks.TryGetValue(a.Id, out var rk))
                    {
                        var comp = st.NodeId << 16 | rk;
                        if (comp != st.ReviveAreaId)
                        {
                            st.ReviveAreaId = comp;
                            _writeBehind.MarkReviveAreaId(
                                (int)st.CharId, comp);
                            // 531 [i32 area] — ClientReviveInfo natif.
                            await session.SendAsync(new CNC_CZ_ClientReviveInfo
                            {
                                Value = comp,       // i32 : revive_area_id
                            }, token);
                            EdenLog.Write($"[Zone-Event] char={st.CharId} " +
                                $"aire REVIVE {a.Id} → revive={comp:x}");
                        }
                    }
                    break;
                case "NORMAL":
                    if (scene.NormalLinks.TryGetValue(a.Id, out var nk))
                    {
                        var comp = st.NodeId << 16 | nk;
                        if (comp != st.LastNormalAreaId)
                        {
                            st.LastNormalAreaId = comp;
                            _writeBehind.MarkLastNormalAreaId(
                                (int)st.CharId, comp);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>Compare natif des conditions : op 1=≥, 2=≤, 3==, 4=≠
    /// (CSceneCondLevel::Execute @0x80c33b0 — même table pour Variable,
    /// EventState, MonsterSum).</summary>
    private static bool CondOp(int lhs, int op, int rhs)
        => SceneEventRules.Compare(lhs, op, rhs);

    /// <summary>Exécute l'événement `eventId` de la scène `scene` pour
    /// le personnage `st` — équivalent de TriggerArea natif. Retourne
    /// true si un Event_Transport a migré le joueur.</summary>
    private async Task<bool> RunSceneEventAsync(NetSession session,
        EntryState st, SceneParser.Scene scene, int eventId,
        CancellationToken token, int depth = 0)
    {
        if (depth > 8 || !scene.Events.TryGetValue(eventId, out var ev))
            return false;
        var transported = false;
        foreach (var br in ev.Branches)
        {
            var ok = true;
            foreach (var c in br.Conds)
            {
                if (!await EvalSceneCondAsync(st, c))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            foreach (var a in br.Actions)
            {
                if (await RunSceneActionAsync(session, st, scene, a,
                        token, depth))
                {
                    transported = true;
                    // Après un transport le joueur quitte le node —
                    // les branches suivantes n'ont plus de sens.
                    return true;
                }
            }
        }
        return transported;
    }

    /// <summary>Évalue une condition Event_If_* — false bloque la
    /// branche (natif : une condition fausse saute à la paire suivante).</summary>
    private async Task<bool> EvalSceneCondAsync(EntryState st,
        SceneParser.SceneEvent.Cmd c)
    {
        int A(int i) => i < c.Args.Length ? c.Args[i] : 0;
        // Conditions qui exigent l'état joueur / la DB (plan 02).
        switch (c.Name)
        {
            // If_ScreenMessage — natif @0x080db5f0 : *result = 1,
            // TOUJOURS vrai (les args « 4 id 0 » sont ignorés).
            case "Event_If_ScreenMessage":
                return SceneEventRules.ScreenMessageCond();
            // If_BuffCheck buffId mode — 11 = porté, 12 = absent
            // (mission @0x080dd810, énumère les enchants de l'exécutant).
            case "Event_If_BuffCheck":
                return SceneEventRules.BuffPresence(
                    st.Statuses.ContainsKey(A(0)), A(1));
            // If_BuffCheck6P buffId portée op val — nom de script de
            // CSceneCondBuffCheckByStack (@0x080dd2a0) : compare le
            // nombre de piles ; portée 13 = soi, 14 = équipe locale.
            case "Event_If_BuffCheck6P":
                return EvalBuffStackCond(st, A(0), A(1), A(2), A(3));
            // If_ItemCheck itemId op n — inventaire de l'exécutant
            // (mission @0x080ddbe0 : GetInventoryItemNum, -1 → faux).
            case "Event_If_ItemCheck":
                return SceneEventRules.Compare(
                    await CountInventoryItemAsync(st, A(0)), A(1), A(2));
        }
        return EvalSceneCondSync(st, c);
    }

    /// <summary>BuffCheck6P : un perso (portée 13) ou chaque membre
    /// local de l'équipe (portée 14 — faux sans équipe, comme le
    /// natif <c>GetForceTeam == null</c>).</summary>
    private bool EvalBuffStackCond(EntryState st, int buffId, int scope,
        int op, int value)
    {
        int StackOf(EntryState e) =>
            e.Statuses.TryGetValue(buffId, out var b) ? b.StackCount : 0;
        if (scope == SceneEventRules.ScopeSelf)
            return SceneEventRules.BuffStack(StackOf(st), op, value);
        if (scope != SceneEventRules.ScopeTeam) return false;
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null) return false;
        foreach (var m in LocalTeamMembers(st, team))
            if (!SceneEventRules.BuffStack(StackOf(m), op, value))
                return false;
        return true;
    }

    /// <summary>Nombre d'exemplaires d'un item hors équipement.</summary>
    private async Task<int> CountInventoryItemAsync(EntryState st,
        int itemId)
    {
        if (itemId <= 0) return 0;
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        return items.Where(i => i.ItemId == itemId && !i.IsEquipped)
            .Sum(i => Math.Max(1, i.Quantity));
    }

    /// <summary>Conditions pures (état de node / monstres).</summary>
    private bool EvalSceneCondSync(EntryState st,
        SceneParser.SceneEvent.Cmd c)
    {
        int A(int i) => i < c.Args.Length ? c.Args[i] : 0;
        return c.Name switch
        {
            // If_Level op val — niveau du perso (CSceneCondLevel).
            "Event_If_Level" => CondOp(st.Level, A(0), A(1)),
            // If_EventState id op val — état du node.
            "Event_If_EventState" =>
                CondOp(EventStates.GetValueOrDefault(EvKey(st.NodeId, A(0))),
                    A(1), A(2)),
            // If_Variable id op val — variable du node.
            "Event_If_Variable" =>
                CondOp(EventVars.GetValueOrDefault(EvKey(st.NodeId, A(0))),
                    A(1), A(2)),
            // If_Monster spawnId op val — vivants du groupe.
            "Event_If_Monster" or "Event_If_RebornMonsterSum" =>
                CondOp(_services.Live.GroupMobs(st.NodeId, st.InstanceGroup, A(0))
                        .Count(m => m.Alive), A(1), A(2)),
            // If_RebornMonster spawnId 5|6 — groupe né / absent
            // (CSceneCondRebornMonsterState : op5 = flag posé).
            // Drapeau explicite sinon état vivant du groupe.
            "Event_If_RebornMonster" =>
                (RebornGroups.TryGetValue(EvKey(st.NodeId, A(0)),
                    out var born) ? born
                    : _services.Live.GroupMobs(st.NodeId, st.InstanceGroup, A(0))
                        .Any(m => m.Alive)) == (A(1) == 5),
            _ => false,
        };
    }

    /// <summary>Exécute une action Event_* ; retourne true uniquement
    /// quand un transport a eu lieu (le perso a quitté le node).</summary>
    private async Task<bool> RunSceneActionAsync(NetSession session,
        EntryState st, SceneParser.Scene scene, SceneParser.SceneEvent.Cmd a,
        CancellationToken token, int depth)
    {
        int A(int i) => i < a.Args.Length ? a.Args[i] : 0;
        switch (a.Name)
        {
            // Transport node sel — désasm : NodeEventID = node<<16|sel ;
            // l'arrivée = aire gateway pairée (même LinkKey) ou position.
            case "Event_Transport":
            {
                var target = A(0);
                if (target <= 0) return false;
                var dest = _services.Scenes.GetScene($"S{target:D3}");
                if (dest == null) return false;
                var minLv = _services.GameData.Nodes
                    .FirstOrDefault(n => n.Id == target)?.MinLevel ?? 0;
                if (minLv > 0 && st.Level < minLv)
                {
                    await SendTextIndexAsync(session, 5259, token);
                    return false;
                }
                float dx = 100, dy = 100, dz = 0;
                if (dest.Positions.Count > 0)
                    (dx, dy, dz) = dest.Positions[0];
                else if (dest.EventAreas.FirstOrDefault(
                    e => e.Type == "GATEWAY") is { } ga)
                    (dx, dy, dz) = (ga.X, ga.Y, ga.Z);
                await MigrateToNodeAsync(session, st, target, dx, dy, dz,
                    token);
                return true;
            }
            // ScreenMessage idx — message système 316 (texte T_TextIndex).
            case "Event_ScreenMessage":
                await SendTextIndexAsync(session, (short)A(0), token);
                return false;
            // RebornMonster spawnId mode delay — 5=faire naître le
            // groupe, 6=le retirer ; delay==8 → clear immédiat du handle
            // (CSceneActionRebornMonsterState @0x80c3aa0 : vtable+0x10 =
            // vivre, +0xc = mourir, arg3==8 → ClearMonsterByHandle).
            case "Event_RebornMonster":
            {
                var spawnId = A(0);
                var mode = A(1);
                if (mode == 6)
                {
                    foreach (var mob in _services.Live
                        .DespawnGroup(st.NodeId, st.InstanceGroup, spawnId))
                        await BroadcastUnsightAsync(st.NodeId, st.InstanceGroup, mob.Handle,
                            token);
                    RebornGroups[EvKey(st.NodeId, spawnId)] = false;
                }
                else
                {
                    var spawn = scene.Monsters
                        .FirstOrDefault(m => m.AreaId == spawnId);
                    if (spawn != null &&
                        _services.Live.GroupMobs(st.NodeId, st.InstanceGroup, spawnId)
                            .All(m => !m.Alive))
                    {
                        var announced = false;
                        foreach (var e in _services.Live
                            .SpawnGroup(st.NodeId, st.InstanceGroup, spawn))
                        {
                            await BroadcastSeeMobAsync(st.NodeId, st.InstanceGroup, e, token);
                            // Annonce encodée en scène (col19) : « Le Pouvoir
                            // de Destruction a infecté… » (70-S), invasions…
                            // Une seule fois par groupe né, pas par mob.
                            if (!announced &&
                                _services.Live.GetMob(e.Handle) is { } born)
                            {
                                announced = true;
                                await AnnounceSpawnAsync(born);
                            }
                        }
                        RebornGroups[EvKey(st.NodeId, spawnId)] = true;
                    }
                    else if (spawn != null)
                        RebornGroups[EvKey(st.NodeId, spawnId)] = true;
                }
                return false;
            }
            // KillMonster spawnId — retire les vivants du groupe.
            case "Event_KillMonster":
                foreach (var mob in _services.Live
                    .DespawnGroup(st.NodeId, st.InstanceGroup, A(0)))
                    await BroadcastUnsightAsync(st.NodeId, st.InstanceGroup, mob.Handle,
                        token);
                RebornGroups[EvKey(st.NodeId, A(0))] = false;
                return false;
            // BornNpc npcId group — NPC aux positions col7==group
            // (le NPC né est un NPC ordinaire : dialogable via son
            // template, pas de verbe « transform » natif).
            case "Event_BornNpc":
                await BornSceneNpcAsync(st.NodeId, st.InstanceGroup, scene,
                    A(0), A(1), st.PosX, st.PosZ, token);
                return false;
            // KillNpc npcId — retire le(s) NPC de ce template.
            case "Event_KillNpc":
                await KillSceneNpcAsync(st.NodeId, st.InstanceGroup, A(0),
                    token);
                return false;
            // MonsterAction groupId kfId dlgId time — CAI::SetEventAction
            // : IA du groupe figée « time » s (motion connue prioritaire),
            // 495 motion + 268 bulle (MonsterSay). Le « Trigger_Id » natif
            // est le joueur déclencheur, PAS un event chaîné.
            case "Event_MonsterAction":
                await SceneMonsterActionAsync(st.NodeId, st.InstanceGroup,
                    A(0), A(1), A(2), A(3), token);
                return false;
            // PlaySound fichier positionId 0 — 494 (ClientPlaySound).
            case "Event_PlaySound":
                await ScenePlaySoundAsync(st.NodeId, st.InstanceGroup,
                    scene, a.Words.Length > 0 ? a.Words[0] : "", A(1),
                    st.PosX, st.PosZ, token);
                return false;
            // PlayEffect effectId fileNo positionId — 383 au node.
            case "Event_PlayEffect":
                await ScenePlayEffectAsync(st.NodeId, st.InstanceGroup,
                    scene, A(0), A(1), A(2), st.PosX, st.PosZ, token);
                return false;
            // ChannelMsg textId — annonce monde (T_TextIndex).
            case "Event_ChannelMsg":
                await SceneChannelMsgAsync(st, A(0), token);
                return false;
            // GiveBuff ? enchantId durée — enchant jeté sur le joueur
            // qui déclenche (292 EffectReport comme les buffs objet).
            case "Event_GiveBuff":
                await ApplyEventBuffAsync(session, st, A(1), A(2), token);
                return false;
            case "Event_DeleteBuff":
                await RemoveEventBuffAsync(session, st, A(1), token);
                return false;
            // TriggerEvent eventId — chaîne vers un autre événement.
            case "Event_TriggerEvent":
                return await RunSceneEventAsync(session, st, scene, A(0),
                    token, depth + 1);
            // Variable id val — assignation.
            case "Event_Variable":
                EventVars[EvKey(st.NodeId, A(0))] = A(1);
                return false;
            // RanDomVariable id lo hi — tirage inclusif natif.
            case "Event_RanDomVariable":
                EventVars[EvKey(st.NodeId, A(0))] =
                    Random.Shared.Next(A(1), A(2) + 1);
                return false;
            // EventState id val — état de node persistant.
            case "Event_EventState":
                EventStates[EvKey(st.NodeId, A(0))] = A(1);
                return false;
            // SetAchievement id flag — le script source porte
            // (id, flag) ; la mission réécrit le verbe en
            // « id def.Point flag » (ParsingEventAchievement, mission
            // @0x08060510 : def+0x1c = col8 S_Achievement). Exécution
            // zone : TC_SetAchievement @0x083d51d0 — flag!=0 ET team
            // → grant à tous les membres locaux, sinon l'exécutant.
            case "Event_SetAchievement":
                await GrantSceneAchievementAsync(session, st, A(0),
                    A(1), token);
                return false;
            // Variante NPC : token2 ignoré par le natif (id décoratif),
            // le flag d'équipe est le token 3.
            case "Event_SetAchievementNPC":
                await GrantSceneAchievementAsync(session, st, A(0),
                    A(2), token);
                return false;
            default:
                // Verbes non encore portés (Event_EventTimeDelay…)
                // — journalisés, jamais bloquants.
                EdenLog.Write($"[Zone-Event] node={st.NodeId} verbe " +
                    $"non géré : {a.Name} {string.Join(' ', a.Args)}");
                return false;
        }
    }

    /// <summary>Diffuse le 298 (ClientSeeMob) d'une entité fraîche à
    /// toutes les sessions du node qui l'ont en vue (spawn d'event).</summary>
    private async Task BroadcastSeeMobAsync(int nodeId, int inst,
        ZoneLiveWorld.SeenEntity e, CancellationToken token)
    {
        var live = e.IsMonster ? _services.Live.GetMob(e.Handle) : null;
        // float3 = dir, float4 = GetMovement (roam idle / battle pet —
        // même convention que UpdateSightAsync).
        var speed = live?.Def != null
            ? (live.IsElfPet ? live.Def.BattleSpeed : live.Def.RoamSpeed)
            : 0f;
        var body = BuildSeeMob(e.Handle, e.EntityId, e.IsMonster,
            e.X, e.Y, e.Z, e.Facing, live?.Hp ?? 0, speed,
            live is { IsElfPet: true } ? (short)-2537 : null,
            live?.PetName,
            live is { IsElfPet: true } ? live.OwnerCharId : 0,
            live?.MaxPatience ?? 100);
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated) continue;
            // À l'inverse du scan périodique, on force la vue : le mob
            // d'event doit apparaître même hors scan courant.
            ost.Sent[e.Handle] = 0;
            ost.EntityPos[e.Handle] = (e.X, e.Y);
            try { await s.SendAsync(body, token); }
            catch { ost.Sent.TryRemove(e.Handle, out _); }
        }
    }

    /// <summary>Diffuse le 269 (LifeEradicate) d'un handle à tout le
    /// node — disparition d'un mob/NPC d'event.</summary>
    private async Task BroadcastUnsightAsync(int nodeId, int inst,
        uint handle, CancellationToken token)
    {
        var body = BitConverter.GetBytes(handle);
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst) continue;
            ost.Sent.TryRemove(handle, out _);
            ost.EntityPos.TryRemove(handle, out _);
            ost.HandleToEntity.TryRemove(handle, out _);
            try { await s.SendPacketAsync(269, body, token); }
            catch { }
        }
    }

    /// <summary>268 ClientSay d'un mob/NPC (MonsterSay natif) :
    /// `[u32 handle][str texte][u32 0]` — la bulle au-dessus du modèle.</summary>
    private async Task BroadcastMonsterSayAsync(int nodeId, int inst,
        uint handle,
        string text, CancellationToken token, int dialogueId = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Blob « DLG1 »+id (extension Godot-first) : le client résout
        // l'id dans sa langue ; sans id = say natif (blob vide).
        var blob = SceneEventRules.EncodeDialogueBlob(dialogueId);
        var (op, payload) = PreparePacket(new CNC_CZ_ClientSay
        {
            Field1 = handle,                        // u32 : handle
            Field2 = text,                          // str : texte
            Field3 = (uint)blob.Length,             // u32 : taille blob
            Blob = blob,                            // blob : DLG1+id
        });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Event_GiveBuff — pose l'enchant sur le joueur
    /// déclencheur via le statut réel (AddPlayerEnchantAsync → 299 +
    /// player_enchants + tick/expiration) — l'ancien 299 brut
    /// (rest=0, sans ActiveStatus) affichait une icône impossible à
    /// retirer (bug live « statuts collés »).</summary>
    private async Task ApplyEventBuffAsync(NetSession session, EntryState st,
        int enchantId, int durationSec, CancellationToken token)
    {
        if (enchantId <= 0 ||
            _services.GameData.Enchants.All(e => e.Id != enchantId))
            return;
        await AddPlayerEnchantAsync(session, st, (int)st.CharId,
            enchantId, durationSec > 0 ? durationSec : -1, 1, token);
    }

    /// <summary>Event_DeleteBuff — retire l'enchant du joueur
    /// (RemoveEnchantByID natif → 300 + purge DB — le 300 brut sans
    /// statut laissait l'effet actif).</summary>
    private async Task RemoveEventBuffAsync(NetSession session,
        EntryState st, int enchantId, CancellationToken token)
    {
        if (enchantId <= 0) return;
        await RemovePlayerEnchantAsync(session, st, enchantId, token);
    }
}
