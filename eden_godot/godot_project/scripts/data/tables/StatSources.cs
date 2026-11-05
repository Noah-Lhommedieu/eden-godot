namespace EdenClient.Data.Tables;

/// <summary>Famille d'une provenance de statistique.</summary>
public enum SourceGroup { Equipment, Certificate, Buff, Nerf, State }

/// <summary>Une provenance : ce qui apporte (ou retire) <see cref="Value"/> à
/// une statistique du profil. <see cref="Name"/> est toujours un libellé
/// lisible (jamais un identifiant).</summary>
public sealed record StatSource(SourceGroup Group, string Name, double Value, bool Percent);

/// <summary>
/// Provenances d'une statistique du Profil (clés de <c>ProfileWindow</c> :
/// str agi int wis lck patk matk def acc eva crit mcrit critdmg mcritdmg parry
/// block aspd cspd move res_*). Pur, sans Godot : le mapping commande →
/// statistique est le miroir de <c>LicenseService.ApplyEnchantCommands</c>
/// (serveur), l'équipement lit les colonnes stats de c_item.
/// </summary>
public static class StatSources
{
    private static readonly string[] Core = { "str", "agi", "int", "wis", "lck" };
    // Ordre ÉLÉMENT natif (id AttrResist 1..9 = colonnes c_item 63-71,
    // kinddata+0x00..0x11 — arg i de la commande 2095 vise l'élément
    // 4+i, celle de 2101 l'élément 1+i, cf. LicenseService serveur) :
    // ICE avant LIGHTNING. ⚠️ Ce n'est PAS l'ordre d'affichage du
    // profil (lightning avant ice — ProfileStatInfo.ResistKeys) ni
    // l'ordre wire du 296.
    private static readonly string[] Resist =
        { "res_slash", "res_strike", "res_pierce", "res_fire", "res_ice", "res_lightning", "res_nature", "res_holy", "res_dark" };

    /// <summary>Effets d'une commande d'enchant sur les stats du profil :
    /// (clé, valeur de gain signée, pourcentage ?). Vitesse d'attaque et de cast
    /// natives : négatif = plus rapide, la valeur retournée est inversée.</summary>
    public static List<(string Stat, double Value, bool Percent)> Effects(int cmd, double[] a)
    {
        var r = new List<(string, double, bool)>();
        double V(int i) => i < a.Length ? a[i] : 0;
        void Add(string s, double v, bool pct) { if (v != 0) r.Add((s, v, pct)); }
        switch (cmd)
        {
            case 2085: for (var i = 0; i < 5; i++) Add(Core[i], V(i), true); break;
            case 2086: for (var i = 0; i < 5; i++) Add(Core[i], V(i), false); break;
            case 2083: Add("patk", V(0), true); Add("def", V(1), true); Add("matk", V(2), true); break;
            case 2084: Add("patk", V(0), false); Add("def", V(1), false); Add("matk", V(2), false); break;
            case 2040: Add("crit", V(0), true); Add("mcrit", V(1), true); break;
            case 2048: Add("crit", V(0), false); Add("mcrit", V(1), false); break;
            case 2043: case 2044: Add("critdmg", V(0), true); Add("mcritdmg", V(1), true); break;
            case 2087: Add("acc", V(0), false); Add("eva", V(1), false); break;
            case 2088: Add("acc", V(0), true); Add("eva", V(1), true); break;
            case 2051: Add("move", V(0), true); break;
            case 2056: case 2058: Add("aspd", V(0), false); break;
            case 2068: Add("aspd", V(0), true); break;
            case 2052: Add("aspd", -(V(0) + V(1) + V(2) + V(3) + V(4) + V(5)), true); break;
            case 2069: Add("cspd", V(0), true); break;
            case 2053: Add("cspd", -V(0), true); Add("aspd", V(1) + V(2) + V(3) + V(4), true); break;
            case 2108: case 2117: Add("block", V(0), true); break;
            case 2109: Add("parry", V(0), true); break;
            case 2101: for (var i = 0; i < 3; i++) Add(Resist[i], V(i), false); break;
            case 2095: for (var i = 0; i < 6; i++) Add(Resist[3 + i], V(i), false); break;
        }
        return r;
    }

    /// <summary>Ajoute à <paramref name="acc"/> les contributions à <paramref name="stat"/>
    /// des commandes d'un enchant (× <paramref name="stacks"/>).</summary>
    public static void AddCommands(List<StatSource> acc, string stat, SourceGroup group, string name,
        IEnumerable<(int Id, double[] Args)> commands, int stacks = 1)
    {
        foreach (var (id, args) in commands)
            foreach (var (s, v, pct) in Effects(id, args))
                if (s == stat) acc.Add(new StatSource(group, name, v * Math.Max(1, stacks), pct));
    }

    /// <summary>Contribution d'une pièce d'équipement (colonnes stats de c_item).</summary>
    public static void AddItem(List<StatSource> acc, string stat, string name, ItemTable.ItemDef d)
    {
        void Add(int v) { if (v != 0) acc.Add(new StatSource(SourceGroup.Equipment, name, v, false)); }
        switch (stat)
        {
            case "str": Add(d.BonusStr); break;
            case "agi": Add(d.BonusAgi); break;
            case "int": Add(d.BonusInt); break;
            case "wis": Add(d.BonusWis); break;
            case "lck": Add(d.BonusLck); break;
            case "patk": Add(d.PhysicalAttack); break;
            case "matk": Add(d.MagicAttack); break;
            case "def": Add(d.Defence); break;
            case "acc": Add(d.Hit); break;
            case "eva": Add(d.Dodge); break;
            case "crit": Add(d.Critical); break;
            case "mcrit": Add(d.MagicCritical); break;
            case "critdmg": Add(d.CriticalDamage); break;
            case "mcritdmg": Add(d.MagicCriticalDamage); break;
            case "aspd": Add(d.AtkSpeedBonus); break;
            case "cspd": Add(d.CastSpeed); break;
            case "parry": Add(d.Parry); break;
            case "block": Add(d.Block); break;
            default:
                var i = Array.IndexOf(Resist, stat);
                if (i >= 0 && i < d.Resistances.Length) Add(d.Resistances[i]);
                break;
        }
    }

    /// <summary>Total par famille (valeurs de même unité seulement : le
    /// pourcentage et le plat ne s'additionnent pas).</summary>
    public static string Format(StatSource s) =>
        $"{(s.Value >= 0 ? "+" : "")}{s.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}{(s.Percent ? " %" : "")}";
}
