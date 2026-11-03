using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Système Break Point (« Barre de Rupture ») — branchement
/// zone des règles pures <see cref="BreakPointRules"/>. Preuves natives
/// (binaire zone, <c>re/ghidra/zone</c>) :
/// <list type="bullet">
/// <item><c>CNode::BornMonster</c> @0x084b02b0 : <c>mob+0x78</c> (patience)
/// = <c>S_Monster</c> col 13 (défaut 100), <c>mob+0x7c</c> = 0.</item>
/// <item><c>CCharacter::AttackResult</c> @0x08416030 (mêlée, exige
/// <c>mob+0x7d</c>) et <c>ISpell::HitMagicSkill</c> @0x084ef710 (sorts) :
/// si l'attribut de l'attaquant est une faiblesse (masque col 11),
/// <c>patience −= dégâts</c> ; puis broadcast <c>ClientUpdateAttr</c>
/// attr 0x13 = patience.</item>
/// <item>Rupture (<c>patience &lt; 1</c>, one-shot <c>+0x7c</c>) :
/// <c>CAI::SetEventAction(4|3, 0x3f7)</c> = motion de stagger + verrou
/// d'IA, puis <c>mob+0x3a &amp;= 0x7f</c> (retire l'invincibilité posée par
/// SetEventAction — le mob reste vulnérable pendant le stagger).</item>
/// <item>Mort : <c>CMonster::Die</c> @0x08498xxx pose le bit 8 du flag du
/// <c>ClientDie</c> si <c>mob+0x7c</c>, et le drop alimente le 2ᵉ pool de
/// <c>S_DropItem</c> (« break ») quand <c>patience &lt; 1</c>
/// (<c>CMonster::GiveItem</c> @0x08496590 : <c>GenerateDrop(1, …)</c>).</item>
/// </list>
/// Les faiblesses ne sont PLUS dérivées des résistances (hypothèse du
/// plan 13 abandonnée) : le masque S_Monster est la source, 1 558 mobs en
/// portent un (bosses de donjon, élites, coffres de trésor).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Attribut de drain de l'attaquant joueur (4-arg natif
    /// @0x083f94e0) : sort → <c>SpellDef.AttackElement</c>, mêlée →
    /// élément de l'arme équipée (sans arme : Strike).</summary>
    private int BpAttackerAttr(EntryState st, SpellParser.SpellDef? spell)
    {
        var snap = PlayerSnapshot(st);
        var hasWeapon = snap.WeaponKind != 0 || snap.WeaponPAtk > 0
                        || snap.WeaponElement != 0;
        return BreakPointRules.AttackerAttr(0, spell?.AttackElement ?? 0,
            spell != null, snap.WeaponElement, hasWeapon);
    }

    /// <summary>Draine la patience du mob après un coup de
    /// <paramref name="dmg"/> du joueur (<paramref name="spell"/> = sort,
    /// null = mêlée). Retourne true à l'instant où la jauge passe sous 1
    /// (rupture : stagger + broadcast). N'émet rien si le coup ne
    /// drainait pas.</summary>
    private async Task<bool> RegisterBpDamageAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        SpellParser.SpellDef? spell, int dmg)
    {
        var def = mob.Def;
        if (def is not { BpWeakMask: not 0 } || dmg <= 0 || !mob.Alive)
            return false;
        var attr = BpAttackerAttr(st, spell);
        var tier = (def.BpGate & 0xFF) == BreakPointRules.GateAll ? 0
            : BreakPointRules.SectorTier(mob.Facing, st.Facing);
        var res = BreakPointRules.Drain(def, mob.Patience, attr, dmg,
            requireBar: spell == null, tier);
        if (!res.Drained) return false;
        mob.Patience = res.Patience;
        // attr 0x13 = patience courante (i32) — broadcast CanSee natif.
        await BroadcastToMobSeersAsync(mob, new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)mob.Handle,   // i32 : handle du mob
            Field2 = 0x13,              // s16 : attr patience
            Field3 = mob.Patience,      // i32 : patience courante
            Field4 = 0,                 // i64 : 0 (natif piVar14[4..5])
        }, CancellationToken.None);
        if (!res.Broke || mob.BpBroken) return false;
        mob.BpBroken = true;
        EdenLog.Write($"[BP] mob #{mob.MobId} brisé par char={st.CharId} " +
            $"attr={attr} ({BreakPointRules.ElementNames[attr - 1]})");
        if (mob.Hp > 0) await StaggerBrokenMobAsync(st, mob, melee: spell == null);
        return true;
    }

    /// <summary>Stagger de rupture : <c>SetEventAction(4|3, 0x3f7)</c> —
    /// verrou d'IA (durée de la motion 0x3f7 si connue, sinon 4 s mêlée /
    /// 3 s sort), arrêt de marche (326 immobile), 495 de la motion, et
    /// annulation d'un cast en cours (la patience vide interrompt le
    /// wind-up : l'IA est figée par l'action d'événement).</summary>
    private async Task StaggerBrokenMobAsync(EntryState st,
        ZoneLiveWorld.LiveMob mob, bool melee)
    {
        var sec = SceneEventRules.ActionLockSeconds(
            BreakPointRules.FallbackStaggerSeconds(melee),
            MotionEndDs(mob, 0x3f7));
        mob.EventActionUntil = DateTime.UtcNow.AddSeconds(sec);
        mob.CastingUntil = default;
        mob.CastSerial++;
        await BroadcastMobStopAsync(mob.NodeId, mob.InstanceKey, mob,
            CancellationToken.None);
        await BroadcastMonsterActionAsync(mob.Handle, mob.NodeId,
            mob.InstanceKey, 0x3f7);
    }

    /// <summary>Rupture de cast d'un mob frappé (<c>GetHurt</c> fin
    /// @0x0849ad40) : à chaque coup, 2 % de chance (puis contre la
    /// résistance d'interruption <c>data+0xc6</c> = 0) d'annuler le
    /// wind-up en cours → <c>SpellBreak</c> → <c>ISpell::NotifyBreak</c>
    /// @0x084ee5d0 : broadcast <c>CNC_CZ_ClientSpellBreak</c> (287)
    /// [i32 caster][s16 sort][u8 reason 0]. Sans effet si
    /// <see cref="ZoneLiveWorld.LiveMob.CantBreak"/> (CEC_CantBreak).
    /// Retourne true si le cast a été brisé.</summary>
    private async Task<bool> TryInterruptMobCastAsync(
        ZoneLiveWorld.LiveMob mob, Random? rng = null)
    {
        if (!mob.Alive || mob.CantBreak ||
            mob.CastingUntil <= DateTime.UtcNow ||
            mob.CastingSpellId == 0 ||
            !BreakPointRules.RollSpellBreak(rng ?? Random.Shared))
            return false;
        var spell = mob.CastingSpellId;
        mob.CastingUntil = default;
        mob.CastingSpellId = 0;
        mob.CastSerial++;
        await BroadcastToMobSeersAsync(mob, new CNC_CZ_ClientSpellBreak
        {
            Field1 = (int)mob.Handle,   // i32 : lanceur (le mob)
            Field2 = (short)spell,      // s16 : sort interrompu
            Field3 = 0,                 // u8  : reason
        }, CancellationToken.None);
        EdenLog.Write($"[BP] cast {spell} du mob #{mob.MobId} interrompu");
        return true;
    }

    /// <summary>Retour de leash : <c>CAIFSMReturn::Update</c> @0x084dd1b0
    /// remet <c>mob+0x78</c> au max et rediffuse l'attr 0x13.</summary>
    private async Task ResetMobPatienceAsync(ZoneLiveWorld.LiveMob mob)
    {
        if (mob.Patience == mob.MaxPatience && !mob.BpBroken) return;
        mob.ResetPatience();
        await BroadcastToMobSeersAsync(mob, new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)mob.Handle, Field2 = 0x13,
            Field3 = mob.Patience, Field4 = 0,
        }, CancellationToken.None);
    }

    /// <summary>Flag de mort natif : bit 8 du flag <c>ClientDie</c> si le
    /// mob a été brisé (<c>mob+0x7c</c>, CMonster::Die @0x08498xxx).</summary>
    internal static byte DieFlags(ZoneLiveWorld.LiveMob mob) =>
        mob.BpBroken ? (byte)8 : (byte)0;

    /// <summary>Le 2ᵉ pool « break » de <c>S_DropItem</c> est tiré quand la
    /// patience est sous 1 à la mort (<c>GiveItem</c> @0x08496590 :
    /// <c>if (mob+0x78 &lt; 1) GenerateDrop(1, …)</c>).</summary>
    internal static bool BreakPoolDrops(ZoneLiveWorld.LiveMob mob) =>
        mob.Patience < 1;
}
