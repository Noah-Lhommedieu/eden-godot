using Eden.Core;
using Eden.Core.Game.WorldBoss;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone;

/// <summary>
/// Partiel — world boss, boss 70-S et monstres mutants (plan
/// futur-feature/12). Responsabilités :
/// <list type="bullet">
/// <item>annonce d'apparition lue en scène (col19 de reborn_monsters →
///   316 <c>ClientTextOutputIndex</c> à tous les clients, natif
///   <c>BroadCastMessageToAllCharacter(short,…)</c> @0x08177b50) au
///   respawn et à l'activation d'une ligne dormante
///   (<c>Event_RebornMonster</c>) ;</item>
/// <item>suivi du compte à rebours de respawn (<c>LiveMob.RespawnAt</c>) ;</item>
/// <item>kill broadcast optionnel (extension Eden, config) ;</item>
/// <item>avertissement « compte à rebours » des sorts longs de boss
///   (index natif 1647, HYPOTHESIS donc config) ;</item>
/// <item>mutants 60761-66 : <see cref="MutantScheduler"/> (apparition
///   dynamique, annonce monde) + courrier de « Broken Red Crystal
///   Shard » aux frappeurs.</item>
/// </list>
/// Toutes les mutations passent par la boucle de zone.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Planificateur des mutants (null tant que non démarré
    /// ou désactivé par <c>EDEN_MUTANTS=0</c>). Accédé uniquement sur
    /// la boucle de zone.</summary>
    private MutantScheduler? _mutants;

    /// <summary>Options de cadence lues dans <see cref="EdenConfig"/>.</summary>
    internal static MutantScheduler.Options MutantOptionsFromConfig()
    {
        var min = Math.Max(1, EdenConfig.MutantMinIntervalMin);
        var max = Math.Max(min, EdenConfig.MutantMaxIntervalMin);
        return new MutantScheduler.Options(
            TimeSpan.FromMinutes(min), TimeSpan.FromMinutes(max),
            TimeSpan.FromMinutes(Math.Max(1, EdenConfig.MutantLifetimeMin)),
            Math.Max(1, EdenConfig.MutantMaxConcurrent));
    }

    /// <summary>Diffuse un 316 à toutes les sessions authentifiées.</summary>
    private async Task BroadcastTextIndexAsync(
        CNC_CZ_ClientTextOutputIndex? msg)
    {
        if (msg == null) return;
        await _services.Sessions.BroadcastAllAsync(msg);
    }

    /// <summary>Annonce d'apparition du mob si sa ligne de scène en
    /// porte une (col19 &gt; 0) — appelée au respawn et à
    /// l'activation d'une ligne dormante.</summary>
    private async Task AnnounceSpawnAsync(ZoneLiveWorld.LiveMob mob)
    {
        var msg = WorldBossAnnouncements.Spawn(mob.AnnounceTextId);
        if (msg == null) return;
        EdenLog.Write($"[WorldBoss] annonce {mob.AnnounceTextId} " +
            $"mob #{mob.MobId} node={mob.NodeId}");
        await BroadcastTextIndexAsync(msg);
    }

    /// <summary>Hook de mort (appelé depuis la fin de la séquence de
    /// mort, sur la boucle) : échéance de respawn, kill broadcast des
    /// boss fixes, fin de vie d'un mutant (récompenses + replanif).</summary>
    private async Task OnWorldMonsterKilledAsync(EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        if (mob.Kind == BossKind.None) return;
        var killer = st.CharName ?? "?";
        switch (mob.Kind)
        {
            case BossKind.WorldBoss:
                if (EdenConfig.WorldBossKillAnnounce)
                    await BroadcastTextIndexAsync(
                        WorldBossAnnouncements.Kill(mob.MobId, killer));
                break;
            case BossKind.Mutant:
                _mutants?.NotifyKilled(mob.MobId, DateTime.UtcNow);
                await BroadcastTextIndexAsync(
                    WorldBossAnnouncements.MutantKilled(mob.MobId, killer));
                await SendMutantRewardsAsync(mob);
                // Le cadavre quitte le registre après le despawn
                // visuel (5 s) — un mutant ne respawne pas.
                ScheduleOnLoop(TimeSpan.FromSeconds(8), _ =>
                {
                    _services.Live.Remove(mob);
                    return Task.CompletedTask;
                });
                break;
        }
    }

    /// <summary>Courrier « Broken Red Crystal Shard » à chaque
    /// frappeur du mutant (guide : « petits lots par messagerie »).
    /// Persistance via la file ordonnée par perso.</summary>
    private async Task SendMutantRewardsAsync(ZoneLiveWorld.LiveMob mob)
    {
        int[] hitters;
        lock (mob.Attackers) hitters = mob.Attackers.ToArray();
        foreach (var m in WorldBossAnnouncements
                     .MutantRewards(mob.MobId, hitters))
        {
            try
            {
                await _persistence.EnqueueAsync(m.CharId, ct =>
                    _services.AchievementRepo.InsertRewardMailAsync(
                        m.CharId, m.ItemId, MutantMailSender,
                        MutantMailTitle, MutantMailBody, m.Amount));
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[WorldBoss] courrier mutant " +
                    $"char={m.CharId} : {ex.Message}");
            }
        }
    }

    // Textes de courrier : chaînes EN canoniques, traduites à
    // l'affichage par la fenêtre de courrier du client
    // (UiTextEn / ui_text_en_fr.tsv).
    internal const string MutantMailSender = "Mutant Expedition";
    internal const string MutantMailTitle = "Mutant hunt reward";
    internal const string MutantMailBody =
        "Thank you for taking on the mutant monster. " +
        "Here are your Broken Red Crystal Shards.";

    /// <summary>Tick 1 s (slow tick de la boucle) : avance le
    /// planificateur de mutants et applique ses actions.</summary>
    public async Task TickWorldBossAsync()
    {
        await SweepExpiredDynamicMobsAsync();
        if (!EdenConfig.MutantsEnabled) return;
        _mutants ??= new MutantScheduler(MutantOptionsFromConfig(),
            Random.Shared, DateTime.UtcNow);
        foreach (var act in _mutants.Tick(DateTime.UtcNow))
            await ApplyMutantActionAsync(act);
    }

    /// <summary>Applique une action du planificateur : peuple le node
    /// cible si besoin (sinon le spawn masquerait le peuplement
    /// initial), instancie le mutant, diffuse la vue puis l'annonce
    /// monde ; ou retire un mutant expiré.</summary>
    internal async Task ApplyMutantActionAsync(MutantScheduler.Action act)
    {
        if (act.Kind == MutantScheduler.ActionKind.Expire)
        {
            foreach (var m in _services.Live.AllMobs
                .Where(x => x.MobId == act.MutantMobId && x.Alive &&
                            x.ExpireAt != null).ToList())
            {
                _services.Live.Remove(m);
                await BroadcastUnsightAsync(m.NodeId, m.InstanceKey,
                    m.Handle, CancellationToken.None);
                EdenLog.Write($"[WorldBoss] mutant #{m.MobId} expiré");
            }
            return;
        }
        var scene = _services.Scenes.GetScene($"S{act.NodeId:D3}");
        _services.Live.EnsureNodePopulated(act.NodeId, 0, scene);
        var span = MutantLifetime();
        var spawned = _services.Live.SpawnDynamic(act.NodeId, 0,
            act.MutantMobId, act.X, act.Y, 0f, DateTime.UtcNow + span);
        if (spawned == null)
        {
            // Pas de définition S_Monster : on libère le slot.
            _mutants?.NotifyGone(act.MutantMobId, DateTime.UtcNow);
            EdenLog.Write($"[WorldBoss] mutant #{act.MutantMobId} sans " +
                "définition S_Monster — ignoré");
            return;
        }
        EdenLog.Write($"[WorldBoss] mutant #{act.MutantMobId} né " +
            $"node={act.NodeId} ({act.X:F0},{act.Y:F0})");
        await BroadcastSeeMobAsync(act.NodeId, 0, spawned.Value.Entity,
            CancellationToken.None);
        await BroadcastTextIndexAsync(
            WorldBossAnnouncements.MutantSpawn(act.MutantMobId));
    }

    private static TimeSpan MutantLifetime() =>
        TimeSpan.FromMinutes(Math.Max(1, EdenConfig.MutantLifetimeMin));

    /// <summary>Retire les mutants arrivés à échéance même sans tick
    /// du planificateur (sécurité : mutant GM, planificateur coupé).</summary>
    private async Task SweepExpiredDynamicMobsAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var m in _services.Live.AllMobs
            .Where(x => x.ExpireAt is { } e && e <= now && x.Alive)
            .ToList())
        {
            _services.Live.Remove(m);
            _mutants?.NotifyGone(m.MobId, now);
            await BroadcastUnsightAsync(m.NodeId, m.InstanceKey, m.Handle,
                CancellationToken.None);
        }
    }

    /// <summary>Avertissement « compte à rebours » d'un sort long de
    /// boss (config <c>EDEN_WORLDBOSS_CAST_WARN</c>, HYPOTHESIS) :
    /// 316 index natif 1647 aux sessions du node du boss. L'argument
    /// est l'ID du sort : le client en résout le nom dans sa langue
    /// (t_spell FR/EN).</summary>
    private async Task WarnBossCastAsync(ZoneLiveWorld.LiveMob mob,
        Eden.Core.Data.Parsers.SpellParser.SpellDef sdef)
    {
        if (!EdenConfig.WorldBossCastWarn) return;
        var msg = WorldBossAnnouncements.CastCountdown(mob.MobId,
            sdef.Id.ToString(), sdef.CastTimeMs, EdenConfig.WorldBossCastWarnMs);
        if (msg == null) return;
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != mob.NodeId ||
                ost.InstanceGroup != mob.InstanceKey || !ost.Migrated)
                continue;
            try { await s.SendAsync(msg, CancellationToken.None); }
            catch { /* session morte */ }
        }
    }
}
