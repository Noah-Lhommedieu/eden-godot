using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
namespace Eden.Zone;

/// <summary>Wander idle des mobs — partial de ZoneEntryFlow.
/// Natif : un monstre vivant hors combat se déplace périodiquement
/// vers un point aléatoire proche de son spawn (comportement visible
/// dans toutes les sessions de jeu — "chaque mob bouge après un temps
/// aléatoire dans un endroit aléatoire mais proche"). Émis via le 326
/// ClientMovePrediction — même paquet que la boucle de combat
/// (SendMovePredictionAsync), seul paquet de déplacement dont le
/// format est prouvé par capture + désasm.</summary>
public sealed partial class ZoneEntryFlow
{
    private int _wanderStarted;

    /// <summary>Lance le wander central (une fois) — appelée quand le
    /// premier perso migre. Un step schedulé sur la boucle de zone
    /// pour tous les nodes : pas de tâche par mob ni de while+Delay.</summary>
    private void EnsureMobWanderLoop()
    {
        if (Interlocked.CompareExchange(ref _wanderStarted, 1, 0) == 0)
            ScheduleOnLoop(TimeSpan.FromMilliseconds(300),
                _ => MobWanderStepAsync());
    }

    /// <summary>Tick 300 ms : pour chaque node où un perso a migré,
    /// les pets invoqués suivent leur maître (suivi fluide — un tick
    /// à 1,5 s les faisait sauter par bonds) et les mobs vivants, hors
    /// combat et non stun dont le délai est échu repartent vers un
    /// point aléatoire autour de leur home (cadence inchangée — le
    /// wander est horodaté par <c>NextWanderAt</c>, pas par la boucle).
    /// Rayon et intervalle pilotés par <c>GameBalance.MobWanderRadius</c> /
    /// <c>MobWanderMinMs</c>/<c>MaxMs</c> (balance.ini / EDEN_BAL_*).
    /// La position logique est mise à jour immédiatement — le client
    /// anime la marche lui-même via le 326 (cur→dst).</summary>
    private async Task MobWanderStepAsync()
    {
        var rng = Random.Shared;
        {
            try
            {
                var now = DateTime.UtcNow;
                // Cellules (node, instance) — chaque groupe d'équipe
                // voit sa propre population de mobs (CNodeGroup natif).
                var cells = _states.Values
                    .Where(st => st.Migrated && st.CharId != 0)
                    .Select(st => (st.NodeId, st.InstanceGroup))
                    .Distinct().ToList();
                foreach (var (nodeId, inst) in cells)
                {
                    var watchers = _services.Sessions.All.Where(s =>
                        _states.TryGetValue(s.SessionId, out var ost) &&
                        ost.Migrated && ost.NodeId == nodeId &&
                        ost.InstanceGroup == inst).ToList();
                    if (watchers.Count == 0) continue;
                    // Timers d'invasion (reborn col9/col11) : armement
                    // des événements de naissance — le cycle 據點 vit
                    // ici (56038 → event 1 à +30 s, 56039 → event 2 à
                    // +420 s), indépendamment des joueurs présents.
                    var cellScene = _services.Scenes
                        .GetScene($"S{nodeId:D3}");
                    if (cellScene != null)
                        ArmBornTimers(nodeId, inst, cellScene);
                    // Pets invoqués : suivi du maître + attaque de sa
                    // cible (BornPet natif — jamais de wander).
                    await TickPetsAsync(nodeId, inst, watchers, now);
                    foreach (var mob in _services.Live.MobsOf(nodeId, inst))
                    {
                        if (!mob.Alive || mob.InCombat || mob.OwnerCharId != 0 ||
                            mob.StunnedUntil > now || mob.NextWanderAt > now ||
                            mob.EventActionLocked(now) ||
                            mob.MoveLocked)
                            continue;
                        // Siège en cours (élite arrivée au cristal) :
                        // le mob frappe la structure jusqu'à sa mort —
                        // pas de wander pendant l'assaut.
                        if (mob.SiegeMobHandle != 0)
                        {
                            await TickMobSiegeAsync(nodeId, inst, mob,
                                now);
                            continue;
                        }
                        // Mobs-objets (coffres, autels, cristaux,
                        // entités de quête — S_Monster atk≤1 / sans
                        // portée / ai=164) : jamais de déplacement
                        // (spec live + natif : pas d'état FSM de
                        // mouvement pour eux).
                        if (_services.Live.MonsterDefs.TryGetValue(
                                mob.MobId, out var wdef)
                            && wdef.IsStructure)
                            continue;
                        // Route de patrouille : réservée aux world
                        // bosses (règle live — les chemins tracés
                        // [mob_patrol_mode] ne sont pas pour les mobs
                        // ordinaires ; appliquée à tous, un col2 de
                        // reborn qui matchait une route par
                        // coïncidence envoyait des hordes traverser
                        // la map en file). Les mobs normaux gardent
                        // l'errance ±rayon du home (roam mode 0
                        // natif, SetNewGoalNormal).
                        if (mob.Kind != Eden.Core.Game.WorldBoss
                                .BossKind.None || mob.EventSpawned)
                            ResolveMobPatrol(nodeId, mob);
                        float dstX, dstY;
                        int nextDelayMs;
                        bool marchEnd = false;
                        if (mob.Patrol is { } route)
                        {
                            // Waypoint suivant — séquentiel en boucle
                            // (SetNewGoalPatrol natif). Un waypoint
                            // dans un mur invisible est sauté (le mob
                            // ne doit pas traverser les mêmes murs
                            // que les joueurs). Exception élites
                            // d'invasion (EventSpawned) : la route est
                            // une marche unique — dernier waypoint
                            // atteint → ancrage + siège du cristal.
                            var lastIdx = route.Waypoints.Count - 1;
                            marchEnd = mob.EventSpawned &&
                                mob.PatrolIndex >= lastIdx;
                            var wp = route.Waypoints[mob.EventSpawned
                                ? Math.Min(mob.PatrolIndex, lastIdx)
                                : mob.PatrolIndex % route.Waypoints.Count];
                            dstX = wp.X; dstY = wp.Z;
                            if (!marchEnd)
                                mob.PatrolIndex = mob.EventSpawned
                                    ? mob.PatrolIndex + 1
                                    : (mob.PatrolIndex + 1) %
                                        route.Waypoints.Count;
                            // Prochain waypoint ≈ temps de marche à
                            // ~5 u/s (les waypoints de scène peuvent
                            // être distants — cap 30 s pour ne pas
                            // « téléporter » trop vite).
                            var ddx = dstX - mob.X; var ddy = dstY - mob.Y;
                            var dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                            nextDelayMs = (int)Math.Clamp(
                                dist / 5.0 * 1000, 300, 30000);
                        }
                        else
                        {
                            // Tout boss de monde (fixe, 70-S, mutant)
                            // SANS route prédéfinie : fixe (règle live
                            // — un boss ne doit jamais dériver au
                            // hasard ; seuls ceux porteurs d'un chemin
                            // tracé [mob_patrol_mode] bougent). Kind
                            // couvre aussi les mutants — avant, un
                            // mutant sans PatrolId errait comme un mob
                            // normal. Re-tester dans ~10 s au cas où.
                            if (mob.Kind != Eden.Core.Game.WorldBoss
                                    .BossKind.None)
                            {
                                mob.NextWanderAt = now +
                                    TimeSpan.FromSeconds(10);
                                continue;
                            }
                            // Destination ±R du home (même ordre que
                            // l'éparpillement natif ±10u au spawn) —
                            // jamais hors du node ni de la zone de
                            // spawn.
                            var radius = Eden.Core.Game.GameBalance
                                .MobWanderRadius;
                            dstX = mob.HomeX + (float)(
                                rng.NextDouble() * radius * 2 - radius);
                            dstY = mob.HomeY + (float)(
                                rng.NextDouble() * radius * 2 - radius);
                            var min = Eden.Core.Game.GameBalance
                                .MobWanderMinMs;
                            var max = Eden.Core.Game.GameBalance
                                .MobWanderMaxMs;
                            nextDelayMs = min + rng.Next(
                                Math.Max(1, max - min + 1));
                        }
                        // Mur invisible : même grille NodeMap que le
                        // mouvement joueur (natif IsFakeBlock /
                        // CheckLineOfMove) — un mob, boss compris, ne
                        // traverse pas les murs qui bloquent les
                        // joueurs. Destination bloquée → re-tirage au
                        // prochain tick.
                        var mapCode = MapCodeOfNode(nodeId);
                        if (mapCode != null &&
                            _services.Scenes.IsNodeBlocked(
                                mapCode, dstX, dstY))
                        {
                            mob.NextWanderAt = now +
                                TimeSpan.FromMilliseconds(nextDelayMs);
                            continue;
                        }
                        var (mvOp, mvPayload) = PreparePacket(
                            BuildMovePrediction(mob.Handle, mob.X,
                                mob.Y, dstX, dstY));
                        foreach (var s in watchers)
                        {
                            if (!_states.TryGetValue(s.SessionId,
                                    out var ost) ||
                                !ost.Sent.ContainsKey(mob.Handle))
                                continue;
                            // session morte — ignorée (avalée par
                            // SendPreparedToAsync).
                            await SendPreparedToAsync(s, mvOp,
                                mvPayload, CancellationToken.None);
                        }
                        mob.X = dstX;
                        mob.Y = dstY;
                        mob.NextWanderAt = now +
                            TimeSpan.FromMilliseconds(nextDelayMs);
                        if (marchEnd)
                        {
                            // Fin de marche d'invasion : l'élite s'ancre
                            // au dernier waypoint et assiège la
                            // structure vivante la plus proche (cristal
                            // 據點) ; sans cible elle erre sur place.
                            mob.Patrol = null;
                            mob.HomeX = mob.X;
                            mob.HomeY = mob.Y;
                            mob.SiegeMobHandle = FindSiegeTarget(
                                nodeId, inst, mob.X, mob.Y);
                            mob.NextSiegeAt = now +
                                TimeSpan.FromSeconds(2);
                            EdenLog.Write($"[Wander] élite #{mob.MobId} " +
                                $"(0x{mob.Handle:x8}) fin de route " +
                                $"{mob.PatrolId} ({mob.X:F0},{mob.Y:F0}) " +
                                $"→ siège=0x{mob.SiegeMobHandle:x8}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Wander] erreur tick : {ex.Message}");
            }
        }
        // 300 ms — fréquence du suivi de pets (le wander reste
        // cadencé par NextWanderAt, typiquement plusieurs secondes).
        // Le step se reschedule tant que la boucle de zone vit.
        ScheduleOnLoop(TimeSpan.FromMilliseconds(300),
            _ => MobWanderStepAsync());
    }

    /// <summary>Code de map (S001…) d'un node — table des nodes
    /// <c>S_Node</c> ; null si inconnu (pas de grille → pas de test
    /// de mur).</summary>
    private string? MapCodeOfNode(int nodeId) =>
        _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == nodeId)?.MapCode;

    /// <summary>Résolution paresseuse de la route de patrouille d'un
    /// mob : <c>PatrolId</c> (col2 de [reborn_monsters]) →
    /// <c>Scene.Patrols</c> [mob_patrol_mode]. Miroir de
    /// <c>CAI::Init</c> natif (zone @0x84d26f0 : id de patrouille
    /// valide → roam mode 1 <c>SetNewGoalPatrol</c>, sinon mode 0
    /// errance aléatoire). Une fois tranchée, la décision est
    /// mémorisée sur le mob (<c>PatrolResolved</c>).</summary>
    private void ResolveMobPatrol(int nodeId, ZoneLiveWorld.LiveMob mob)
    {
        if (mob.PatrolResolved) return;
        mob.PatrolResolved = true;
        if (mob.PatrolId == 0) return;
        var mapCode = MapCodeOfNode(nodeId);
        var scene = mapCode != null
            ? _services.Scenes.GetScene(mapCode) : null;
        if (scene == null ||
            !scene.Patrols.TryGetValue(mob.PatrolId, out var route) ||
            route.Waypoints.Count == 0)
            return;
        // Waypoint de départ = le plus proche du spawn — le natif
        // (CAI::Init/SetNewGoalPatrol) engage le mob sur SA position,
        // pas sur waypoint[0] ; un index 0 forcé faisait converger
        // toute la population de la route en file indienne. Et si la
        // route entière est loin du home (col2 = id de groupe qui
        // matche une route par coïncidence), ce n'est pas une
        // patrouille pour ce mob : pas de route, errance normale.
        var best = 0; var bestD = double.MaxValue;
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            var dx = wp.X - mob.X; var dy = wp.Z - mob.Y;
            var dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        if (bestD > 40.0 * 40.0) return;
        mob.Patrol = route;
        mob.PatrolIndex = best;
    }

    /// <summary>Structure vivante la plus proche (≤30 u) — cible de
    /// siège de l'élite en bout de route : le cristal 據點 bleu est
    /// un S_Monster « structure » (atk≤1, immobile). 30 u couvre le
    /// dernier waypoint posé à quelques pas du cristal.</summary>
    private uint FindSiegeTarget(int nodeId, int inst, float x, float y)
    {
        uint best = 0; var bestD = 30.0 * 30.0;
        foreach (var m in _services.Live.MobsOf(nodeId, inst))
        {
            if (!m.Alive || m.Def is not { IsStructure: true }) continue;
            var dx = m.X - x; var dy = m.Y - y;
            var dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = m.Handle; }
        }
        return best;
    }

    /// <summary>Tick de siège — l'élite frappe la structure toutes
    /// les ~2 s (dégâts = AttackPower, sans paquet de combat : la
    /// cristallisation des dégâts natifs n'est pas capturée ; le 305
    /// de mort seul signale la chute du cristal).</summary>
    private async Task TickMobSiegeAsync(int nodeId, int inst,
        ZoneLiveWorld.LiveMob mob, DateTime now)
    {
        var target = _services.Live.GetMob(mob.SiegeMobHandle);
        if (target == null || !target.Alive)
        {
            mob.SiegeMobHandle = 0;
            return;
        }
        if (now < mob.NextSiegeAt) return;
        mob.NextSiegeAt = now + TimeSpan.FromSeconds(2);
        target.Hp -= Math.Max(1, mob.AttackPower);
        if (target.Hp <= 0)
            await KillSceneMobAsync(nodeId, inst, target, mob.Handle);
    }

    /// <summary>Mort d'un mob de scène sans tueur joueur (siège
    /// d'invasion, expiration) : 305 broadcast au node (tueur =
    /// handle de l'assaillant), cadavre retiré, drapeau reborn
    /// retombé, clef d'event col8 évaluée — miroir allégé de
    /// FinishMobDeathAsync sans loot/exp (pas de charId bénéficiaire).
    /// Pas de ScheduleRespawn : le cycle des cristaux/timers est
    /// porté par les events eux-mêmes.</summary>
    private async Task KillSceneMobAsync(int nodeId, int inst,
        ZoneLiveWorld.LiveMob mob, uint killerHandle)
    {
        if (mob.DeathEmitted) return;
        mob.DeathEmitted = true;
        mob.Alive = false;
        mob.Hp = 0;
        mob.NoRespawn = true;
        ScheduleCorpseDespawn(mob);
        var (op, payload) = PreparePacket(new CNC_CZ_ClientDie
        {
            Field1 = mob.Handle,          // u32 : victime (le cristal)
            Field2 = killerHandle,        // u32 : tueur (l'élite)
            Field3 = DieFlags(mob),       // u8 : flags de mort
        });
        foreach (var s in NodeSessions(nodeId, inst))
        {
            try
            {
                await SendPreparedToAsync(s, op, payload,
                    CancellationToken.None);
            }
            catch { /* session morte — ignorée */ }
        }
        if (mob.SpawnId > 0)
            RebornGroups[EvKey(nodeId, mob.SpawnId)] = false;
        EdenLog.Write($"[Zone-Event] node={nodeId} structure " +
            $"#{mob.MobId} (0x{mob.Handle:x8}) détruite par " +
            $"0x{killerHandle:x8} → event {mob.DeathEventId}");
        if (mob.DeathEventId > 0)
            await RunNodeSceneEventAsync(nodeId, inst, mob.DeathEventId,
                null, CancellationToken.None);
    }
}
