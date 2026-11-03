using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Zone.Combat.SpellEffects;

/// <summary>Contexte d'exécution d'une commande d'enchant de résultat
/// sur un monstre victime (voie mob de
/// <c>ApplySpellResultCommandsAsync</c>).</summary>
public sealed record SpellEffectContext(int CasterCharId,
    float CasterX, float CasterZ, ZoneLiveWorld.LiveMob Mob,
    int EnchantId);

/// <summary>Opérations de zone dont les handlers ont besoin —
/// implémentées par <c>ZoneEntryFlow</c>, factices en test.</summary>
public interface ISpellEffectHost
{
    /// <summary>Déplace le lanceur (CNC_CZ_ClientGoto vers lui-même).
    /// </summary>
    Task MoveCasterAsync(float x, float z);
    /// <summary>Déplace le monstre (move-prediction aux témoins).
    /// </summary>
    Task MoveMobAsync(ZoneLiveWorld.LiveMob mob, float x, float y);
    /// <summary>Retire un enchant du registre du mob + 300 aux témoins
    /// (dissipation — <c>CEC_EnchantKill*</c> retire via
    /// <c>ILifeEntity::RemoveReplaceableSpell</c> natif).</summary>
    Task RemoveMobEnchantAsync(ZoneLiveWorld.LiveMob mob, int effectId);
    /// <summary>Pose un enchant sur un mob (registre + 299) — sous-effet
    /// de <c>CEC_Effect2Target</c> (7003), même voie que
    /// <c>EffectToLife</c> natif.</summary>
    Task ApplyEnchantToMobAsync(ZoneLiveWorld.LiveMob mob, int effectId,
        int restSec, int level);
    /// <summary>Pose un enchant sur le lanceur joueur (status + 299).
    /// </summary>
    Task ApplyEnchantToCasterAsync(int effectId, int restSec,
        int level);
    /// <summary>Pose un enchant sur chaque joueur migré du node dans
    /// le rayon — sélection de <c>CEC_Effect2Target</c> modes 3/4/5.
    /// <paramref name="foes"/>=false → côté ami : le lanceur inclus,
    /// ses ennemis exclus (un ennemi à l'instant T ne reçoit jamais
    /// le buff — <c>CCharacter::IsMyEnemy</c> @0x0840e490) ;
    /// =true → côté ennemi : seuls les joueurs hostiles au lanceur
    /// (contexte PvP partagé) sont touchés.</summary>
    /// <returns>Nombre de joueurs touchés.</returns>
    Task<int> ApplyEnchantToPlayersNearAsync(float x, float z,
        float radius, int effectId, int restSec, int level,
        bool foes = false);
    /// <summary>Mobs vivants du node dans le rayon — sélection ennemi
    /// de <c>CEC_Effect2Target</c> modes 4/5.</summary>
    IEnumerable<ZoneLiveWorld.LiveMob> MobsNear(float x, float z,
        float radius);
    /// <summary>Soin/drain instantané des vitals du lanceur joueur
    /// (<c>CEC_HealPlus</c> soigne l'attaquant — entity+0x8c).</summary>
    Task HealCasterAsync(int hpDelta, int mpDelta);
    /// <summary>Effet visuel <c>CNC_CZ_ClientPlayEffect</c> — notifié
    /// au lanceur + can-see (<c>CEC_PlayEffect::Execute</c>
    /// @0x0854b200). `handle` = control id natif (0 → lanceur).</summary>
    Task PlayEffectAsync(float arg0, float arg1, float arg2, int arg3,
        float arg4, uint handle = 0);
    /// <summary>La cible mob recaste un sort vers son attaquant
    /// (<c>CEC_SpellAttack</c> — contre-effet).</summary>
    Task MobCounterCastAsync(ZoneLiveWorld.LiveMob mob, int spellId);
}

/// <summary>Commande d'enchant de résultat exécutable sur un mob.
/// </summary>
public interface ISpellEffectHandler
{
    /// <summary>ids de commande S_Enchant pris en charge.</summary>
    IReadOnlyCollection<int> CommandIds { get; }
    /// <summary>Exécute la commande.</summary>
    Task ExecuteAsync(ISpellEffectHost host, SpellEffectContext ctx,
        EnchantService.UseCommand cmd);
}

/// <summary>Registre + exécuteur générique des commandes d'enchant de
/// résultat : un handler par famille, aucune logique ad hoc dans le
/// flux. Les ids sans handler sont rendus à l'appelant (journalisés
/// `spell-result cmd`), jamais spéculés.</summary>
public sealed class SpellEffectExecutor
{
    private readonly Dictionary<int, ISpellEffectHandler> _byId = new();

    /// <summary>Exécuteur avec les handlers prouvés par désasm.
    /// <paramref name="enchants"/> ajoute le handler de dissipation
    /// (1821/1823/1824) — il a besoin des métadonnées S_Enchant.</summary>
    public static SpellEffectExecutor CreateDefault(
        EnchantService? enchants = null)
    {
        var handlers = new List<ISpellEffectHandler>
        {
            new DistanceGotoHandler(), new AttrHateResetHandler(),
            new Effect2TargetHandler(), new EnchantmentCheckHandler(),
            new AttrHateHotHandler(), new HealPlusHandler(),
            new PlayEffectHandler(), new SpellAttackHandler(),
            new MobRegistryAttrsHandler(), new DistanceSummonHandler(),
        };
        if (enchants != null)
            handlers.Add(new MobEnchantDispelHandler(enchants));
        return new(handlers.ToArray());
    }

    public SpellEffectExecutor(params ISpellEffectHandler[] handlers)
    {
        foreach (var h in handlers)
            foreach (var id in h.CommandIds) _byId[id] = h;
    }

    /// <summary>Vrai si l'id a un handler.</summary>
    public bool Handles(int commandId) => _byId.ContainsKey(commandId);

    /// <summary>Exécute la commande ; faux si aucun handler.</summary>
    public async Task<bool> TryExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        if (!_byId.TryGetValue(cmd.CommandId, out var h)) return false;
        await h.ExecuteAsync(host, ctx, cmd);
        return true;
    }
}

/// <summary>DistanceGoto 1802/1803/1804/1809 —
/// <c>CEC_DistanceGoto::Execute</c> @0x85239xx : dist==0 → charge du
/// lanceur vers la cible ; dist&gt;0 → recul de la cible.</summary>
public sealed class DistanceGotoHandler : ISpellEffectHandler
{
    /// <summary>Marge de contact (≈ portée mêlée).</summary>
    private const double ContactRange = 1.5;
    public IReadOnlyCollection<int> CommandIds { get; } =
        new[] { 1802, 1803, 1804, 1809 };

    public async Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        var dist = cmd.Args[0];
        var dx = ctx.Mob.X - ctx.CasterX;
        var dz = ctx.Mob.Y - ctx.CasterZ;
        var d = Math.Sqrt(dx * dx + dz * dz);
        if (dist <= 0)
        {
            // Charge : approche jusqu'au contact (déjà au contact = rien).
            if (d <= ContactRange) return;
            var stop = d - ContactRange;
            await host.MoveCasterAsync(
                ctx.CasterX + (float)(dx / d * stop),
                ctx.CasterZ + (float)(dz / d * stop));
            return;
        }
        // Recul : la cible s'éloigne du lanceur de `dist`.
        if (d < 0.01) return;
        ctx.Mob.X += (float)(dx / d * dist);
        ctx.Mob.Y += (float)(dz / d * dist);
        await host.MoveMobAsync(ctx.Mob, ctx.Mob.X, ctx.Mob.Y);
    }
}

/// <summary>DistanceSummon 1807 — <c>CEC_DistanceSummon::Execute</c>
/// @0x08522bd0 : l'owner (mob) est téléporté au contact de
/// l'attaquant — point sur la droite mob→lanceur à la somme des
/// bounding radii du lanceur (≈ marge de contact) + PlayEffect
/// broadcast. shortcut: marge fixe 1.5 au lieu des bounding radii
/// natifs, à reprendre si un pull visible colle trop près.</summary>
public sealed class DistanceSummonHandler : ISpellEffectHandler
{
    private const double ContactRange = 1.5;
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 1807 };

    public async Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        if (!ctx.Mob.Alive) return;
        var dx = ctx.Mob.X - ctx.CasterX;
        var dz = ctx.Mob.Y - ctx.CasterZ;
        var d = Math.Sqrt(dx * dx + dz * dz);
        if (d <= ContactRange || d < 0.01) return;
        ctx.Mob.X = ctx.CasterX + (float)(dx / d * ContactRange);
        ctx.Mob.Y = ctx.CasterZ + (float)(dz / d * ContactRange);
        await host.MoveMobAsync(ctx.Mob, ctx.Mob.X, ctx.Mob.Y);
        await host.PlayEffectAsync(0, 0, 0, 0, 11f,
            ctx.Mob.Handle);   // Field6=11f + control id mob (natif)
    }
}

/// <summary>AttrHateReset 1120 — <c>CEC_AttrHateReset::Execute</c>
/// @0x08507570 : sur un monstre vivant, toute entrée de la table de
/// haine passe à 1, puis le lanceur (joueur) devient la cible forcée
/// du monstre (+0xd0) et candidat au combat.</summary>
public sealed class AttrHateResetHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 1120 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        if (ctx.Mob.Alive) ctx.Mob.ResetHate(ctx.CasterCharId);
        return Task.CompletedTask;
    }
}

/// <summary>AttrHateHot 2119/2121 — <c>CEC_AttrHateHot::Execute</c>
/// @0x08507aa0 : ajoute le montant à l'entrée de haine du lanceur dans
/// la table du monstre (`hate[caster] += arg0` — données : +100
/// provocation, -450/-540 apaisement).</summary>
public sealed class AttrHateHotHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } =
        new[] { 2119, 2121 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        if (ctx.Mob.Alive && cmd.Args.Length > 0)
            ctx.Mob.Hate[ctx.CasterCharId] =
                ctx.Mob.Hate.GetValueOrDefault(ctx.CasterCharId)
                + (int)cmd.Args[0];
        return Task.CompletedTask;
    }
}

/// <summary>HealPlus 2047 — <c>CEC_HealPlus::Execute</c> @0x085353xx :
/// le soin vise le LANCEUR (param_1[0]+0x8c = HP/MP de l'attaquant).
/// arg0 = type : 0/1 = HP/MP au taux du spellref (formule de
/// référence, non portée — toute la donnée réelle est `1` seul =
/// soin 0) ; 4 = HP flat arg1 ; 5 = MP flat arg1.</summary>
public sealed class HealPlusHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 2047 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        double A(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        var type = (int)A(0);
        var amount = (int)A(1);
        return type switch
        {
            4 => host.HealCasterAsync(amount, 0),
            5 => host.HealCasterAsync(0, amount),
            _ => Task.CompletedTask,   // types 0-3 : taux spellref
        };
    }
}

/// <summary>PlayEffect 1913 — <c>CEC_PlayEffect::Execute</c>
/// @0x0854b200 : émet <c>CNC_CZ_ClientPlayEffect</c> (opcode 383) au
/// lanceur + can-see. <c>Init</c> @0x0854b3a0 : 5 args directs
/// (3 float, 1 long, 1 long→float).</summary>
public sealed class PlayEffectHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 1913 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        double A(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        return host.PlayEffectAsync((float)A(0), (float)A(1),
            (float)A(2), (int)A(3), (float)A(4));
    }
}

/// <summary>SpellAttack 1999 — <c>CEC_SpellAttack::Execute</c>
/// @0x08553900 : la CIBLE recaste le sort arg0 vers l'attaquant
/// (contre-effet — CastToLife/Location/… dispatché sur le type du
/// sous-sort natif ; voie mob → blast sur le lanceur joueur).</summary>
public sealed class SpellAttackHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 1999 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        var sid = cmd.Args.Length > 0 ? (int)cmd.Args[0] : 0;
        return sid > 0 && ctx.Mob.Alive
            ? host.MobCounterCastAsync(ctx.Mob, sid)
            : Task.CompletedTask;
    }
}

/// <summary>Attributs du registre mob — 2055 (verrous), 3001/3002
/// (HP/tick) : pas des commandes exécutables — ils sont réalisés par
/// l'enchant persistant (<c>FillMobEnchantFields</c>). Enregistrés en
/// no-op pour finir le bruit <c>spell-result cmd</c>. Idem pour les
/// tags de formule natifs (Execute no-op réel) : 1997 LifeFloat
/// (arg1 flag vide dans toute la donnée → les deux blocs sautés),
/// 2033/2034 DamageAddPlus (lus par la formule de dégâts).</summary>
public sealed class MobRegistryAttrsHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } =
        new[] { 2055, 3001, 3002, 1997, 2033, 2034 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
        => Task.CompletedTask;
}

/// <summary>Dispel joueur→mob — EnchantKills 1823 (liste d'ids) et
/// EnchantKillRandom 1821 / EnchantKillRandomForce 1824 (tirage
/// type-filtré). Miroir des <c>CEC_EnchantKill*::Execute</c> natifs
/// (@0x0852dc90/@0x0852ea60/@0x0852eed0) appliqués au
/// <c>CEnchantMgr</c> de la cible : la sélection (chance, filtre
/// type1 buff/type2 debuff, icône, indissipable, plage, ids protégés,
/// Fisher-Yates, troncature) vit dans <see cref="EnchantDispel"/>.
/// </summary>
public sealed class MobEnchantDispelHandler(EnchantService enchants)
    : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } =
        new[] { 1821, 1822, 1823, 1824 };

    public async Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        var mob = ctx.Mob;
        if (!mob.Alive || mob.Enchants.Count == 0) return;
        double A(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        var removed = new List<int>();
        if (cmd.CommandId == 1823)
        {
            foreach (var eid in EnchantDispel.KillList(cmd.Args))
                removed.Add(eid);   // le natif retire même sans icône
        }
        else if (cmd.CommandId == 1822)
        {
            // CEC_EnchantCancel @0x852c470 : retire tout enchant dont
            // le TYPE (S_Enchant c38 → SEffectData+0x112) ∈ [a0,a1],
            // grade (c39 → +0x114) ≤ a2, non indissipable —
            // ILifeEntity::RemoveEnchantByType @0x847b380. La donnée
            // embarque jusqu'à deux triplets (49854 : 163,163,2 |
            // 165,165,2 — Init @0x852c4e0 borne a0≤a1 par swap).
            for (var k = 0; k < 2; k++)
            {
                var mn = (int)A(k * 3); var mx = (int)A(k * 3 + 1);
                var fl = (int)A(k * 3 + 2);
                if (mn <= 0 || mx <= 0) continue;
                if (mx < mn) (mn, mx) = (mx, mn);
                foreach (var e in mob.Enchants.Values)
                {
                    var t = enchants.GetTypeId(e.EffectId);
                    if (t >= mn && t <= mx &&
                        enchants.GetGrade(e.EffectId) <= fl &&
                        !enchants.IsUndispellable(e.EffectId))
                        removed.Add(e.EffectId);
                }
            }
        }
        else
        {
            // Candidats = le registre du mob — même format minimal que
            // la voie joueur (debuff col7 bit1, indissipable col8
            // 0x10, icône cachée dur -9).
            var candidates = mob.Enchants.Values.Select(e =>
                new DispelCandidate(e.EffectId,
                    enchants.IsDebuff(e.EffectId),
                    enchants.IsUndispellable(e.EffectId), e.Hidden));
            foreach (var rule in EnchantDispel.RulesFor(cmd.CommandId,
                cmd.Args))
                removed.AddRange(EnchantDispel.Pick(rule, candidates,
                    () => Random.Shared.Next(100),
                    n => Random.Shared.Next(n)));
        }
        foreach (var eid in removed)
            await host.RemoveMobEnchantAsync(mob, eid);
    }
}

/// <summary>Effect2Target 7003 — <c>CEC_Effect2Target::Execute</c>
/// @0x08526780 : applique l'enchant secondaire (arg3, durée arg4) aux
/// entités choisies par arg0 (1=lanceur, 2=cible courante, 3=groupe
/// autour du lanceur, 4=cercle autour de la cible, 5=cercle autour du
/// lanceur) filtrées par arg1 (0=alliés +0x7c, 1=ennemis +0x80).
/// <c>Init</c> @0x08526300 : rayon = arg2 (s16) × 0,5 (DAT_087cc688),
/// modes bornés 1-5, arg1/arg5 &lt; 2, l'effet secondaire doit exister.
/// Dans notre monde : alliés = joueurs migrés du node, ennemis =
/// mobs du node (le lanceur est un joueur).</summary>
public sealed class Effect2TargetHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } = new[] { 7003 };

    public async Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
    {
        double A(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        var mode = (int)A(0);
        var foes = (int)A(1) == 1;
        var radius = (float)(A(2) * 0.5);
        var eid = (int)A(3);
        var rest = (int)A(4);
        if (eid <= 0) return;
        switch (mode)
        {
            case 1:
                await host.ApplyEnchantToCasterAsync(eid, rest, 1);
                break;
            case 2:
                if (ctx.Mob.Alive)
                    await host.ApplyEnchantToMobAsync(ctx.Mob, eid,
                        rest, 1);
                break;
            case 3:
                // Groupe : le lanceur est à distance 0 — couvert par
                // la passe joueurs proches.
                await host.ApplyEnchantToPlayersNearAsync(ctx.CasterX,
                    ctx.CasterZ, radius, eid, rest, 1);
                break;
            case 4 or 5:
            {
                var cx = mode == 4 ? ctx.Mob.X : ctx.CasterX;
                var cz = mode == 4 ? ctx.Mob.Y : ctx.CasterZ;
                if (foes)
                {
                    foreach (var m in host.MobsNear(cx, cz, radius))
                        if (m.Alive)
                            await host.ApplyEnchantToMobAsync(m, eid,
                                rest, 1);
                    // Côté ennemi du natif (+0x80) : les joueurs
                    // hostiles au lanceur (IsMyEnemy — contexte PvP
                    // partagé) prennent aussi l'effet.
                    await host.ApplyEnchantToPlayersNearAsync(cx, cz,
                        radius, eid, rest, 1, foes: true);
                }
                else
                {
                    await host.ApplyEnchantToPlayersNearAsync(cx, cz,
                        radius, eid, rest, 1);
                }
                break;
            }
            default:
                EdenLog.Write(
                    $"[Zone] spell-result Effect2Target mode={mode} " +
                    "hors 1-5 — ignoré");
                break;
        }
    }
}

/// <summary>Checks d'enchant 3101/3102/3103/2137 —
/// <c>CEC_EnchantCheckByType::Execute</c> @0x0852c790 retourne 1 sans
/// mutation, <c>CEC_LevelCheckPlus::Execute</c> @0x08541250 retourne
/// un booléen (comparaison de niveau) : des conditions consultées par
/// d'autres chemins, pas des actions à l'application. Enregistrés
/// pour ne plus polluer le log `spell-result cmd`.</summary>
public sealed class EnchantmentCheckHandler : ISpellEffectHandler
{
    public IReadOnlyCollection<int> CommandIds { get; } =
        new[] { 3101, 3102, 3103, 2137 };

    public Task ExecuteAsync(ISpellEffectHost host,
        SpellEffectContext ctx, EnchantService.UseCommand cmd)
        => Task.CompletedTask;
}
