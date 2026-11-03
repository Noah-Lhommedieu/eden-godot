using Eden.Core.Logging;
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
                        // Mobs-objets (coffres, autels, cristaux,
                        // entités de quête — S_Monster atk≤1 / sans
                        // portée / ai=164) : jamais de déplacement
                        // (spec live + natif : pas d'état FSM de
                        // mouvement pour eux).
                        if (_services.Live.MonsterDefs.TryGetValue(
                                mob.MobId, out var wdef)
                            && wdef.IsStructure)
                            continue;
                        // Destination ±R du home (même ordre que
                        // l'éparpillement natif ±10u au spawn) — jamais
                        // hors du node ni de la zone de spawn.
                        var radius = Eden.Core.Game.GameBalance
                            .MobWanderRadius;
                        var dstX = mob.HomeX + (float)(
                            rng.NextDouble() * radius * 2 - radius);
                        var dstY = mob.HomeY + (float)(
                            rng.NextDouble() * radius * 2 - radius);
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
                        var min = Eden.Core.Game.GameBalance
                            .MobWanderMinMs;
                        var max = Eden.Core.Game.GameBalance
                            .MobWanderMaxMs;
                        mob.NextWanderAt = now + TimeSpan.FromMilliseconds(
                            min + rng.Next(Math.Max(1, max - min + 1)));
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
}
