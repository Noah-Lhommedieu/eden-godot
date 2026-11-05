using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partiel combat — détection passive (port de
/// <c>lapis::CAI::DoSeekEnemy</c>, zone @0x84d5xxx) :
/// à cadence ~2 s par mob (timer CAI+0x88), un monstre dont le profil
/// S_Ai a une chance de détection non nulle (<c>CAiData+0x05</c> =
/// S_Ai col2, roll <c>rand(100) &lt;= chance</c>) fait une recherche
/// circulaire des joueurs dans son rayon
/// (<c>template+0x20</c> = S_Monster col37 × 0.5 u) et engage le
/// premier candidat éligible :
///   · joueur migré, vivant, pas god/invincible-migration ;
///   · gate de niveau : pour les catégories KillType 2/3
///     (template+0x12), un joueur de
///     <c>lv − mobLv &gt;= CheckLevel</c> est ignoré — CheckLevel
///     = 100/S_Param[20] + S_Param[3] = 8 (zone @0x84d6f0) ;
///   · la séquence d'engagement est celle de la capture : facing +
///     271 DisplayMonster (switch de cible CanSee) + 302 mob aggro
///     (0000 sel4 op0) + 302 char engagé (0000 sel4 op1) + 444 lock
///     [mob][0][char], puis la boucle de combat standard.
/// Scanné par le fil unique de <see cref="ZoneLoop"/> avec les mêmes
/// règles que <see cref="TickMobCombatsAsync"/>.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Seuil natif d'immunité par différence de niveau —
    /// `CheckLevel = DAT_087cc680/S_Param[40] + S_Param[41]`
    /// = 100/20 + 3 = <b>8</b> (zone @0x84d6f0). Un monstre des
    /// catégories KillType 2/3 n'aggro pas un joueur qui le dépasse
    /// d'au moins ce nombre de niveaux.</summary>
    private const int PassiveAggroLevelGap = 8;

    /// <summary>Un balayage d'aggro passive — appelé par
    /// <see cref="ZoneLoop"/> à chaque tick (100 ms) ; le timer par
    /// mob (~2 s) garde la cadence native quelle que soit la fréquence
    /// du tick. Le scan est dédupliqué <b>par mob</b> : itérer les mobs
    /// par joueur relançait le roll de chance S_Ai N fois par période
    /// (une par joueur en ligne) au lieu d'une seule — chaque mob ne
    /// doit chercher qu'une fois par timer (CAI+0x88), sa recherche
    /// circulaire couvrant déjà tous les joueurs.</summary>
    public async Task TickPassiveAggroAsync()
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<ZoneLiveWorld.LiveMob>();
        foreach (var st in _states.Values)
        {
            if (!st.Migrated || st.Session == null) continue;
            foreach (var mob in _services.Live.MobsOf(
                st.NodeId, st.InstanceGroup))
            {
                if (!seen.Add(mob) || !MobCanSeek(mob, now)) continue;
                var target = PickSeekTarget(mob, now);
                mob.NextAggroCheckAt =
                    now.AddSeconds(2 + Random.Shared.NextDouble());
                if (target == null) continue;
                await PassiveEngageAsync(target, mob);
            }
        }
    }

    /// <summary>Conditions du tick de recherche natif : vivant, hors
    /// combat, sauvage (pas un pet invoqué — ceux-ci n'ont pas
    /// d'AI de recherche), S_Monster avec un rayon &gt; 0, timer
    /// écoulé, puis le roll de chance S_Ai col2.</summary>
    private bool MobCanSeek(ZoneLiveWorld.LiveMob mob, DateTime now)
    {
        if (!mob.Alive || mob.InCombat || mob.OwnerCharId != 0
            || now < mob.NextAggroCheckAt || mob.EventActionLocked(now))
            return false;
        var def = mob.Def;
        // Les entités « décor » (coffres/cristaux/œufs — IsStructure)
        // n'ont pas de FSM de déplacement chez le natif : jamais
        // d'aggro passive, même si leur profil S_Ai a une chance de
        // détection (les cristaux ai=164 ont seek=100 mais sont fixes).
        if (def == null || def.AggroCells <= 0 || def.AiId <= 0
            || def.IsStructure)
            return false;
        return _services.MonsterAi.RollPassiveSeek(def.AiId);
    }

    /// <summary>Premier candidat du CircularRangeSearch natif :
    /// joueurs de la même scène+instance, vivants, dans le rayon
    /// template+0x20, hors god/invincible-migration, et qui passent le
    /// gate de niveau (KillType 2/3 → écart &lt; 8).</summary>
    private EntryState? PickSeekTarget(ZoneLiveWorld.LiveMob mob,
        DateTime now)
    {
        var def = mob.Def!;
        var radius2 = def.AggroRadius * def.AggroRadius;
        var levelGated = def.KillType is 2 or 3;
        EntryState? found = null;
        foreach (var st in _states.Values)
        {
            if (!st.Migrated || st.Session == null || st.PlayerHp <= 0
                || st.NodeId != mob.NodeId
                || st.InstanceGroup != mob.InstanceKey
                || st.GodMode
                || now < st.MigrationInvincibleUntil)
                continue;
            var dx = st.PosX - mob.X; var dz = st.PosZ - mob.Y;
            if (dx * dx + dz * dz > radius2) continue;
            // CheckLevel natif : un mob KillType 2/3 ignore un joueur
            // qui le surclasse d'au moins 8 niveaux — c'est la règle
            // « nom rouge : il peut m'aggro ou pas selon mon niveau ».
            if (levelGated && st.Level - mob.Level
                    >= PassiveAggroLevelGap)
                continue;
            found = st;
            break;
        }
        return found;
    }

    /// <summary>Engagement passif : même séquence que le premier
    /// contact mêlée (capture natif) sans le 272 d'attaque du joueur —
    /// le mob prend le joueur pour cible, les témoins voient le switch
    /// (271), le joueur passe en stance combat (302 0000 sel4 op1)
    /// puis la boucle de combat prend le relais.</summary>
    private async Task PassiveEngageAsync(EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        var session = st.Session!;
        st.InCombat = true;
        BeginMobCombat(st, mob);
        // 271 — le mob switch de cible, broadcast CanSee natif.
        await BroadcastDisplayMonsterAsync(mob.Handle, st.NodeId,
            mob.InstanceKey, st.CharId, 1);
        // 302 mob : flag d'aggro (0000 sel4 op0 val=actionId).
        await SendMobAggroAsync(session, mob, CancellationToken.None);
        // 302 char : stance combat de la victime (0000 sel4 op1) —
        // c'est ce flag qui met le client en « mode combat ».
        await SendFlagAsync(session, st.CharId, 0x0000, SEL_COMBAT, 1,
            st.NextAction(), CancellationToken.None);
        // 444 lock [i32 mob][u8 0][i32 char] — capture engage.
        await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
        {
            Field1 = (int)mob.Handle, Field2 = 0,
            Field3 = (int)st.CharId,
        }, CancellationToken.None);
        // 326 — le mob part vers le joueur (même flux de marche que
        // la riposte mêlée). dst = le cercle d'arrêt mêlée, pas la
        // position du joueur : engagé à portée le mob rendu marchait
        // jusque dans le modèle du perso.
        var (gx, gy) = MeleeChaseDst(st, mob.X, mob.Y);
        await SendMovePredictionAsync(session, mob.Handle,
            mob.X, mob.Y, gx, gy, CancellationToken.None);
        StartMobCombatLoop(session, st, mob);
    }
}
