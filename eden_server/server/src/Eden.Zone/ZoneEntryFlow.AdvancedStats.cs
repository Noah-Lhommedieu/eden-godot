using System.Globalization;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Zone.Combat;

namespace Eden.Zone;

/// <summary>
/// Extension Eden (hors protocole natif) pour la fenêtre « Statistiques
/// avancées » du client : formules dérivées avec valeurs injectées et
/// sources de multi-hit (MultiStroke) avec provenance.
///
/// Le multi-hit n'existe pas sur le fil natif (296/492/493) : c'est un
/// roll serveur (`CEC_MultiStroke::Execute`), donc le serveur agrège les
/// mêmes sources que <c>RunItemProcsAsync</c> (<c>ProcSources</c> :
/// équipement puis statuts actifs — talents, certificats, buffs) et
/// renvoie les valeurs EFFECTIVES (× stacks × tunables
/// <c>MultiStroke*Mult</c> de server.config.txt).
///
/// Wire (opcodes privés hors plage native 0-~870) :
///   C→S <see cref="OpAdvStatsQuery"/> : corps vide.
///   S→C <see cref="OpAdvStatsInfo"/> :
///     [u8 version=1][u8 maxHits][f32 chanceMult][f32 damageMult]
///     [s16 nFormulas] n×([str key][str expr][f32 value][str unit])
///     [s16 nSources]  n×([s16 enchantId][u8 origin][s16 stacks]
///                        [f32 gate%][f32 c1][f32 f1][f32 c2][f32 f2])
///   origin : 0 = équipement (useEnchant), 1 = statut actif (talent,
///   certificat, buff…), 2 = proc déclenché (gate% = chance du proc).
///   c1/c2 = chance % du 1er/2e coup extra, f1/f2 = dégât % du coup.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>C→S — demande des stats avancées (corps vide).</summary>
    public const ushort OpAdvStatsQuery = 32001;
    /// <summary>S→C — réponse stats avancées.</summary>
    public const ushort OpAdvStatsInfo = 32002;

    private static string F(double v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);

    private async Task HandleAdvancedStatsQueryAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        try
        {
            if (!st.WeaponResolved) await ResolveWeaponAsync(st);
            var body = BuildAdvancedStatsBody(st);
            await session.SendPacketAsync(OpAdvStatsInfo, body, token);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[AdvStats] char={st.CharId} : {ex.Message}");
        }
    }

    private byte[] BuildAdvancedStatsBody(EntryState st)
    {
        var cfg = Eden.Core.Game.ServerConfig.Current;
        var c = PlayerSnapshot(st);
        var p = CombatParams();
        var rows = BuildFormulaRows(c, p);

        var src = new List<(int Id, byte Origin, int Stacks, float Gate,
            double C1, double F1, double C2, double F2)>();
        var equip = st.ProcEnchantIds;
        foreach (var (id, stacks) in ProcSources(st))
        {
            var origin = (byte)(equip != null && equip.Contains(id) ? 0 : 1);
            // Passifs en bloc (blocs non déclenchés) : un roll par
            // commande, gate 100 %.
            foreach (var cmd in _services.Enchants.GetAttackCommands(id))
                src.Add((id, origin, stacks, 100f, cmd.Args[0],
                    cmd.Args[1], cmd.Args[2], cmd.Args[3]));
            // MultiStroke derrière un proc déclenché sur attaque.
            var proc = _services.Enchants.GetProc(id);
            if (proc != null && proc.IsAttackEvent)
                foreach (var cmd in proc.Commands)
                    if (cmd.CommandId is 1031 or 2031 or 2032 &&
                        cmd.Args.Length >= 4)
                        src.Add((id, 2, stacks, proc.Chance, cmd.Args[0],
                            cmd.Args[1], cmd.Args[2], cmd.Args[3]));
        }
        return EncodeAdvancedStats(
            (byte)Math.Clamp(cfg.MultiStrokeMaxHits, 0, 2),
            (float)cfg.MultiStrokeChanceMult,
            (float)cfg.MultiStrokeDamageMult,
            cfg.MultiStrokeChanceMult, cfg.MultiStrokeDamageMult,
            rows, src);
    }

    /// <summary>Sérialise le corps du 32002 (testé en golden :
    /// <c>AdvancedStatsWireTests</c>). <paramref name="chanceMult"/>/
    /// <paramref name="damageMult"/> (double) servent aux valeurs
    /// effectives des sources — mêmes produits que
    /// RunMultiStrokeAsync.</summary>
    internal static byte[] EncodeAdvancedStats(byte maxHits,
        float chanceMultF, float damageMultF,
        double chanceMult, double damageMult,
        List<(string Key, string Expr, float Value, string Unit)> rows,
        List<(int Id, byte Origin, int Stacks, float Gate,
            double C1, double F1, double C2, double F2)> src)
    {
        var o = new OutBinStream();
        o.WriteByte(1);
        o.WriteByte(maxHits);
        o.WriteFloat(chanceMultF);
        o.WriteFloat(damageMultF);
        o.WriteShort((short)rows.Count);
        foreach (var (key, expr, value, unit) in rows)
        {
            o.WriteString(key); o.WriteString(expr);
            o.WriteFloat(value); o.WriteString(unit);
        }
        o.WriteShort((short)src.Count);
        foreach (var s in src)
        {
            o.WriteShort((short)s.Id);
            o.WriteByte(s.Origin);
            o.WriteShort((short)Math.Min(s.Stacks, short.MaxValue));
            o.WriteFloat(s.Gate);
            o.WriteFloat((float)Math.Round(
                s.C1 * s.Stacks * chanceMult));
            o.WriteFloat((float)(s.F1 * s.Stacks * damageMult));
            o.WriteFloat((float)Math.Round(
                s.C2 * s.Stacks * chanceMult));
            o.WriteFloat((float)(s.F2 * s.Stacks * damageMult));
        }
        return o.ToArray();
    }

    /// <summary>Lignes « formule » : clé (EN, traduite côté client),
    /// expression avec valeurs injectées, résultat, unité.</summary>
    private static List<(string Key, string Expr, float Value, string Unit)>
        BuildFormulaRows(CombatantSnapshot c, NativeCombatFormulas.Params p)
    {
        static int Term(int total, int pct) => Math.Clamp(
            (int)Math.Round(total * pct * 0.01,
                MidpointRounding.AwayFromZero), 0, 9999);
        var lck = Term(c.LckTotal, c.LckPct);
        var agi = Term(c.AgiTotal, c.AgiPct);
        var str = Term(c.StrTotal, c.StrPct);
        var intT = Term(c.IntTotal, c.IntPct);
        var den = $"(Lv {c.Level} x 0.4 + 1)";
        var wpnHit = c.DualWield
            ? c.WeaponHit + c.OffWeaponHit * p.OffhandRatio : c.WeaponHit;
        var rows = new List<(string, string, float, string)>();
        void Add(string k, string e, double v, string u) =>
            rows.Add((k, e, (float)v, u));

        var wpn = c.DualWield
            ? c.OffWeaponPAtk * p.OffhandRatio + c.WeaponPAtk : c.WeaponPAtk;
        Add("P-ATK",
            $"(100 + {F(c.BonusPAtkRate)}%) x (STR {str} x {F(p.StrToPAtk)} + " +
            $"equip {c.EquipPAtk} + weapon {F(wpn)}) / 100 + {F(c.BonusPAtk)}",
            NativeCombatFormulas.MeleeDamage(c, p), "");
        var mwpn = c.DualWield
            ? c.OffWeaponMAtk * p.OffhandRatio + c.WeaponMAtk : c.WeaponMAtk;
        Add("M-ATK",
            $"(100 + {F(c.BonusMAtkRate)}%) x (INT {intT} x {F(p.IntToMAtk)} + " +
            $"equip {c.EquipMAtk} + weapon {F(mwpn)}) / 100 + {F(c.BonusMAtk)}",
            NativeCombatFormulas.MagicDamage(c, p), "");
        Add("Hit Rate",
            $"((equip {c.EquipHit} + weapon {F(wpnHit)} + buff " +
            $"{F(c.BonusHitRating)} + LCK {lck} x {F(p.LckToHit)}) x " +
            $"(1 + {F(c.BonusHitRatePercent)}%)) / {den} + {F(p.BaseHitLv)}",
            NativeCombatFormulas.PhysicalHitRate(c, p) / 100.0, "%");
        Add("Dodge Rate",
            $"((equip {c.EquipDodge} + buff {F(c.BonusDodgeRating)} + " +
            $"AGI {agi} x {F(p.AgiToDodge)}) x (1 + " +
            $"{F(c.BonusDodgeRatePercent)}%)) / {den} + {F(p.BaseDodgeLv)}" +
            $" + {F(c.BonusDodgePercent)}",
            NativeCombatFormulas.PhysicalDodgeRate(c, p) / 100.0, "%");
        Add("Critical Rate",
            $"((equip {c.EquipCrit} + buff {F(c.BonusCritRating)} + " +
            $"LCK {lck} x {F(p.LckToCrit)}) x (1 + " +
            $"{F(c.BonusCritRatePercent)}%)) / {den} + {F(p.BaseCritLv)}",
            NativeCombatFormulas.PhysicalCriticalRate(c, p) / 100.0, "%");
        Add("Magic Critical Rate",
            $"((equip {c.EquipMagicCrit} + buff {F(c.BonusMagicCritRating)}" +
            $" + LCK {lck} x {F(p.LckToMCrit)}) x (1 + " +
            $"{F(c.BonusMagicCritRatePercent)}%)) / {den} + " +
            $"{F(p.BaseMCritLv)}",
            NativeCombatFormulas.MagicCriticalRate(c, p) / 100.0, "%");
        Add("Critical Damage",
            $"{F(p.BaseCritDmg)} + equip {c.EquipCritDamage} + buff " +
            $"{F(c.BonusCritDamage)}",
            NativeCombatFormulas.PhysicalCriticalDamage(c, p), "%");
        Add("Magic Critical Damage",
            $"{F(p.BaseMCritDmg)} + equip {c.EquipMagicCritDamage} + buff " +
            $"{F(c.BonusMagicCritDamage)}",
            NativeCombatFormulas.MagicCriticalDamage(c, p), "%");
        Add("Parry Rate",
            $"(equip {c.EquipParry} + buff {F(c.BonusParry)} + AGI {agi} x " +
            $"{F(p.AgiToParry)}) / {den}",
            NativeCombatFormulas.ParryRate(c, p) / 100.0, "%");
        Add("Block Rate",
            $"(equip {c.EquipBlockRate} + buff {F(c.BonusBlockRate)} + " +
            $"STR {str} x {F(p.StrToBlock)}) / {den}",
            NativeCombatFormulas.BlockRate(c, p) / 100.0, "%");
        Add("Attack Speed",
            $"((equip {c.EquipAttackSpeed} + buff {F(c.BonusAttackSpeed)} " +
            $"+ AGI {agi} x {F(p.AgiToAtkSpeed)}) x (1 + " +
            $"{F(c.BonusAttackSpeedPercent)}%)) / {den}",
            NativeCombatFormulas.AttackSpeedLevelRate(c, p) * 100.0, "%");
        Add("Cast Speed",
            $"((equip {c.EquipCastSpeed} + buff {F(c.BonusCastSpeed)} + " +
            $"AGI {agi} x {F(p.AgiToCastSpeed)}) x (1 + " +
            $"{F(c.BonusCastSpeedPercent)}%)) / {den}",
            NativeCombatFormulas.CastSpeedLevelRate(c, p) * 100.0, "%");
        var pdef = NativeCombatFormulas.PhysicalDefence(c);
        Add("Damage Reduction",
            $"clamp(DEF {pdef} x {F(p.DefParam)} / (Lv {c.Level}^1.6 + 30)," +
            " 0, 70)",
            NativeCombatFormulas.DefenceReduce(c, p), "%");
        Add("Critical Damage Reduction",
            $"clamp(DEF {pdef} / (Lv {c.Level}^1.6 + 30), 0, 70)",
            NativeCombatFormulas.CriDmgReduce(c), "%");
        Add("Weapon Variance",
            $"main {c.WeaponVariance}" +
            (c.DualWield ? $" / off {c.OffWeaponVariance}" : "") +
            " : damage x (100 +/- variance)%",
            c.WeaponVariance, "%");
        Add("Move Speed",
            $"{F(NativeCombatFormulas.BaseMovement)} x min(1 + (ride/buff " +
            $"{F(c.MoveSpeedBonusPct + c.BonusMoveSpeedPct)}% + AGI {agi} " +
            $"x {F(Eden.Core.Game.GameBalance.AgiToMoveSpeed)}%), cap " +
            $"{F(Eden.Core.Game.GameBalance.MoveSpeedCapPct)}%)",
            NativeCombatFormulas.MovementRate(c), "%");
        return rows;
    }
}
