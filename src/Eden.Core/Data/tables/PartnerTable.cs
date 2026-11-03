
namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_Partner.ini</c> (|V.2|30|) — définitions des
/// familiers (elf pets) côté client : icône, modèle, nom, stats de
/// base. La clé (col 0) est l'<b>id de l'item œuf</b> — même clé que
/// <c>ElfInfoRecord.TemplateId</c> du 412/413 et que S_Partner.ini
/// serveur (<c>PartnerParser</c> : indices prouvés par le désasm
/// CPartnerData — icône col1, modèle col2 « M279010 »→279010, nom
/// col3, GrowType col5, grow cols 7-10, battle cols 13-17,
/// integrate cols 18-22, MaxHp col29).
///
/// Le nom anglais vient de <c>t_partner.ini</c> col1 via
/// <see cref="TextResolver.PartnerName"/> ; le nom Big5 de col3
/// sert de repli.
/// </summary>
public static class PartnerTable
{
    /// <summary>Un familier depuis C_Partner.ini.</summary>
    public sealed class PartnerDef
    {
        /// <summary>Id de l'item œuf (col 0 — = templateId du wire).</summary>
        public int ItemId { get; init; }
        /// <summary>Code icône I##### (col 1) →
        /// <c>UI/itemicon/&lt;code&gt;.png</c>.</summary>
        public string IconCode { get; init; } = "";
        /// <summary>Code modèle brut (col 2 — « M144010 »).</summary>
        public string ModelCode { get; init; } = "";
        /// <summary>Stem du modèle GLB (letter + 3 chiffres —
        /// « M144010 » → « M144 » ; convention monsterlist :
        /// code = stem + variante 3 chiffres ; les GLB elf existent
        /// sous <c>monster/model/M144.glb</c>…).</summary>
        public string ModelStem =>
            ModelCode.Length > 4 ? ModelCode[..^3] : ModelCode;
        /// <summary>Nom Big5 (col 3).</summary>
        public string Name { get; init; } = "";
        /// <summary>Type de croissance (col 5).</summary>
        public int GrowType { get; init; }
        /// <summary>Indices de croissance de base (cols 7-10).</summary>
        public int[] Grow { get; } = new int[4];
        /// <summary>Sorts de combat natifs (cols 13-17).</summary>
        public int[] BattleSpells { get; } = new int[5];
        /// <summary>Sorts intégrés natifs (cols 18-22).</summary>
        public int[] IntegrateSpells { get; } = new int[5];
        /// <summary>HP max de base (col 29).</summary>
        public int MaxHp { get; init; }
    }

    private static Dictionary<int, PartnerDef>? _defs;

    /// <summary>templateId (= itemId œuf) → définition ; null si
    /// absent.</summary>
    public static PartnerDef? Get(int itemId)
    {
        EnsureLoaded();
        return _defs != null && _defs.TryGetValue(itemId, out var d)
            ? d : null;
    }

    /// <summary>Résout le stem GLB d'un familier par templateId
    /// (« M144 » → <c>monster/model/M144.glb</c>) — le même stem
    /// que la convention mob de la scène/c_monster.</summary>
    public static string ModelStemFor(int templateId) =>
        Get(templateId)?.ModelStem ?? "";


    /// <summary>Nom affiché : t_partner.ini EN en priorité, repli
    /// Big5 col3.</summary>
    public static string NameOf(int templateId)
    {
        var en = TextResolver.PartnerName(templateId);
        if (en != null) return en;
        var d = Get(templateId);
        return d != null && d.Name.Length > 0
            ? d.Name : $"Elf #{templateId}";
    }

    /// <summary>Dernier token « \n » d'une cellule multi-noms
    /// (convention lapis::StrToken du parser natif).</summary>
    private static string LastToken(string s)
    {
        var i = s.LastIndexOf('\n');
        return i >= 0 ? s[(i + 1)..] : s;
    }

    private static void EnsureLoaded()
    {
        if (_defs != null) return;
        _defs = new Dictionary<int, PartnerDef>();
        // Résolution via le hook TextResolver (AssetCatalog côté jeu,
        // raw_assets/ ou $EDEN_RAW_ASSETS sous NetTests — même
        // convention que les autres tables data/).
        var resolve = TextResolver.ResolveFile;
        var path = resolve?.Invoke("data/db/C_Partner.ini")
            ?? resolve?.Invoke("data/db/c_partner.ini");
        if (path == null) return;
        try
        {
            var t = IniTable.ParseFile(path);
            foreach (var row in t.Rows)
            {
                if (row.Length < 30 ||
                    !int.TryParse(row[0], out var id)) continue;
                var d = new PartnerDef
                {
                    ItemId = id,
                    IconCode = IniTable.GetStr(row, 1),
                    ModelCode = IniTable.GetStr(row, 2),
                    // Le natif découpe col3 par '\n' et garde le
                    // dernier token (lapis::StrToken — PartnerParser
                    // serveur, même convention).
                    Name = LastToken(IniTable.GetStr(row, 3)),
                    GrowType = IniTable.GetInt(row, 5),
                    MaxHp = IniTable.GetInt(row, 29),
                };
                for (var i = 0; i < 4; i++)
                    d.Grow[i] = IniTable.GetInt(row, 7 + i);
                for (var i = 0; i < 5; i++)
                {
                    d.BattleSpells[i] = IniTable.GetInt(row, 13 + i);
                    d.IntegrateSpells[i] = IniTable.GetInt(row, 18 + i);
                }
                _defs[id] = d;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
    }
}
