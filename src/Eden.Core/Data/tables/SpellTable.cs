namespace EdenClient.Data.Tables;

/// <summary>
/// Définitions de sorts client — <c>data/db/c_spell.ini</c> (~60k
/// lignes). Port minimal du <c>SpellCatalog</c> de la référence Godot :
/// mêmes indices de colonnes — seules celles consommées par le chemin
/// de cast Flax sont lues (icône hotbar, type de cible pour la
/// redirection self-cast native).
/// </summary>
public static class SpellTable
{
    /// <summary>Un sort de c_spell.ini.</summary>
    public sealed class SpellDef
    {
        public int Id { get; init; }
        /// <summary>Col1 — code icône → <c>UI/skillicon/&lt;code&gt;.dds</c>.</summary>
        public string IconCode { get; init; } = "";
        /// <summary>Col18 (0-based) — type de cible : 1 = hostile
        /// (même index que le <c>TargetType</c> du SpellParser
        /// serveur ; 2-5 = soutien/soin).</summary>
        public int TargetType { get; init; }
        /// <summary>Col28 — portée du sort (unités table — ×1.5 donne
        /// la portée monde, même formule que le <c>RangeUnits</c> de la
        /// réf. et le SpellParser serveur).</summary>
        public int Range { get; init; }
    }

    private static readonly object _lock = new();
    private static Dictionary<int, SpellDef>? _defs;

    /// <summary>Définition d'un spellmaster (null si inconnu/absent).</summary>
    public static SpellDef? Get(int spellId) =>
        Ensure().TryGetValue(spellId, out var d) ? d : null;

    /// <summary>Type de cible (1 = hostile), 0 si inconnu.</summary>
    public static int TargetType(int spellId) => Get(spellId)?.TargetType ?? 0;

    /// <summary>Portée table du sort (col28), 0 si inconnu.</summary>
    public static int RangeUnits(int spellId) => Get(spellId)?.Range ?? 0;

    /// <summary>Code icône (J001…) ou chaîne vide.</summary>
    public static string IconCode(int spellId) => Get(spellId)?.IconCode ?? "";

    /// <summary>Nom du sort (t_spell.ini FR), repli « #id » — comme
    /// <see cref="EdenClient.Protocol.KnownSpell.SpellName"/>.</summary>
    public static string Name(int spellId) =>
        TextResolver.SpellName(spellId) ?? $"#{spellId}";

    /// <summary>Charge c_spell.ini → id → définition (mêmes colonnes
    /// que le SpellCatalog de référence : 0 id, 1 icône, 18 cible).</summary>
    public static Dictionary<int, SpellDef> Load(string path)
    {
        var defs = new Dictionary<int, SpellDef>();
        foreach (var row in IniTable.ParseFile(path).Rows)
        {
            var id = IniTable.GetInt(row, 0);
            if (id <= 0) continue;
            defs[id] = new SpellDef
            {
                Id = id,
                IconCode = IniTable.GetStr(row, 1),
                TargetType = IniTable.GetInt(row, 18),
                Range = IniTable.GetInt(row, 28),
            };
        }
        return defs;
    }

    private static Dictionary<int, SpellDef> Ensure()
    {
        if (_defs != null) return _defs;
        lock (_lock)
        {
            if (_defs != null) return _defs;
            var defs = new Dictionary<int, SpellDef>();
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(
                    "data/db/c_spell.ini")
                    ?? TextResolver.ResolveFile?.Invoke(
                        "data/db/C_Spell.ini");
                if (p != null) defs = Load(p);
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[spell] c_spell.ini : {e.Message}");
            }
            _defs = defs;
            return defs;
        }
    }
}
