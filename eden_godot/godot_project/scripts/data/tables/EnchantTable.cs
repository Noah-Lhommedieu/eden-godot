namespace EdenClient.Data.Tables;

/// <summary>
/// Définitions des enchants (buffs/debuffs visibles) —
/// <c>data/db/c_enchant.ini</c> (<c>|V.3|61|</c>, ~25k entrées,
/// contenu Reforged) croisées avec <c>data/db/T_Enchant.ini</c>
/// (noms/descriptions anglais).
///
/// Colonnes utiles (mêmes indices que le <c>EnchantParser</c> serveur
/// + EnchantService) :
///   col0 id, col1 code icône (<c>E0302</c>, <c>J515</c>,
///   <c>ERLR1</c>… → <c>UI/skillicon/&lt;code&gt;.png</c>, repli
///   itemicon/uiicon), col5 nom Big5, col7 flags1 (bit0 = « buff » —
///   SEffectData+8 natif, EnchantService @0x805fe00), col8 flags2
///   (bit 0x10 = can't-abandon, 0x100 = no-recast), col56 desc Big5.
///
/// T_Enchant.ini : col1 = nom EN, col3 = description EN (la col2
/// n'est qu'une tagline partielle — préférence à col3 puis col2).
/// </summary>
public static class EnchantTable
{
    /// <summary>Un enchant de c_enchant.ini.</summary>
    public sealed class EnchantDef
    {
        public int Id { get; init; }
        /// <summary>Col1 — code icône → UI/skillicon/ (repli
        /// itemicon/uiicon).</summary>
        public string Code { get; init; } = "";
        /// <summary>Col5 — nom Big5 (repli si T_Enchant absente).</summary>
        public string Name { get; init; } = "";
        /// <summary>Col6 — groupe intégration pet (affichage debug).</summary>
        public int PetGroup { get; init; }
        /// <summary>Col7 bit0 — « buff » (icône positive ; sinon
        /// debuff).</summary>
        public bool IsBuff { get; init; }
        /// <summary>Col8 bit 0x10 — can't-abandon natif (pas de
        /// retrait clic droit).</summary>
        public bool NoAbandon { get; init; }
        /// <summary>Col8 bit 0x100 — refus de recast (info tooltip).</summary>
        public bool NoReapply { get; init; }
        /// <summary>Col59 — plafond de stack (défaut natif 1).</summary>
        public int MaxStack { get; init; } = 1;
        /// <summary>Col56 — description Big5 (repli).</summary>
        public string Description { get; init; } = "";
        /// <summary>Cellules brutes de la ligne (commandes 2134…
        /// entremêlées avec leurs paramètres — cf. <see cref="RideKeyOf"/>).</summary>
        public string[] Raw { get; init; } = Array.Empty<string>();
    }

    /// <summary>Clé de monture (ridelist) portée par un enchant : scan
    /// des cellules, commande 2134 suivie de la rideKey — même règle que
    /// <c>EnchantService.GetRideInfo</c> côté serveur. 0 si aucune.</summary>
    public static int RideKeyOf(int enchantId)
    {
        var raw = Get(enchantId)?.Raw;
        if (raw == null) return 0;
        for (var i = 0; i + 1 < raw.Length; i++)
            if (int.TryParse(raw[i], out var code) && code == 2134 &&
                int.TryParse(raw[i + 1], out var k)) return k;
        return 0;
    }

    /// <summary>Charge c_enchant.ini → id → définition.</summary>
    public static Dictionary<int, EnchantDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, EnchantDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 9 || !int.TryParse(row[0], out var id))
                continue;
            // c_enchant EDENFR = V.2 (60 colonnes) : S_Enchant V.3 (61)
            // sans la colonne groupe elf-pet en index 6 — les champs
            // suivants sont donc décalés de −1.
            var s = row.Length == 60 ? -1 : 0;
            var flag1 = IniTable.GetInt(row, 7 + s);
            var flag2 = IniTable.GetInt(row, 8 + s);
            var maxStack = IniTable.GetInt(row, 59 + s);
            dict[id] = new EnchantDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                Name = IniTable.GetStr(row, 5),
                PetGroup = s == 0 ? IniTable.GetInt(row, 6) : 0,
                IsBuff = (flag1 & 1) != 0,
                NoAbandon = (flag2 & 0x10) != 0,
                NoReapply = (flag2 & 0x100) != 0,
                MaxStack = Math.Max(1, maxStack),
                Description = IniTable.GetStr(row, 56 + s),
                Raw = row,
            };
        }
        return dict;
    }

    // ------------------- accès paresseux global -------------------
    // Mêmes conventions que TextResolver : résolution du chemin via
    // TextResolver.ResolveFile (branché sur AssetCatalog côté Godot),
    // chargement une fois, échec mis en cache. Thread-safety : les
    // tables sont immuables après chargement ; le double-check lock
    // de TextResolver est repris pour rester correct sur les lookups
    // faits depuis le thread réseau (tooltips parsés à la réception).

    private static readonly object _lock = new();
    private static Dictionary<int, EnchantDef>? _defs;
    private static Dictionary<int, TextTable.TextRow>? _text;

    /// <summary>Invalide la table texte — le fichier T_Enchant
    /// localisé (lang/fr) doit être rechargé au switch de langue.</summary>
    public static void Reset() { lock (_lock) _text = null; }

    /// <summary>Définition c_enchant d'un effect_id (null si
    /// inconnu/absent des assets).</summary>
    public static EnchantDef? Get(int enchantId) =>
        EnsureDefs().TryGetValue(enchantId, out var d) ? d : null;

    /// <summary>Nom anglais de l'enchant (T_Enchant col1) — repli sur
    /// le nom Big5 de c_enchant puis « enchant #id ».</summary>
    public static string NameOf(int enchantId)
    {
        if (EnsureText().TryGetValue(enchantId, out var t) &&
            t.Name.Length > 0)
            return t.Name;
        var d = Get(enchantId);
        return d != null && d.Name.Length > 0
            ? d.Name : $"enchant #{enchantId}";
    }

    /// <summary>Description complète de l'enchant (T_Enchant col2 —
    /// « $15$Chaque attaque a 5 % de chances… » pour les procs),
    /// repli col3 (tagline courte) puis null.</summary>
    public static string? DescriptionOf(int enchantId)
    {
        if (!EnsureText().TryGetValue(enchantId, out var t))
            return null;
        var d = TextResolver.Clean(t.Field(2));
        if (d.Length == 0) d = TextResolver.Clean(t.Field(3));
        return d.Length > 0 ? d : null;
    }

    /// <summary>Texte d'un buff ACTIF (T_Enchant champ 3, ex. « MAG +8214 ») ;
    /// repli sur la description. La description (champ 2) est celle du déclencheur
    /// (« Chaque attaque a 5 % de chances… ») — pas ce qu'on veut sur une icône
    /// de buff déjà déclenché.</summary>
    public static string? ActiveTextOf(int enchantId)
    {
        if (EnsureText().TryGetValue(enchantId, out var t))
        {
            var d = TextResolver.Clean(t.Field(3));
            if (d.Length > 0) return d;
        }
        return DescriptionOf(enchantId);
    }

    private static Dictionary<int, EnchantDef> EnsureDefs()
    {
        if (_defs != null) return _defs;
        lock (_lock)
        {
            if (_defs != null) return _defs;
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(
                    "data/db/c_enchant.ini");
                var defs = p != null ? Load(p) : new();
                // c_itemmallenchant.ini : enchants des objets mall
                // (329 clés de monture…) absents de c_enchant — sans eux
                // RideKeyOf rate et les montures tombent en « Item ».
                // Additif : c_enchant reste prioritaire (même règle que
                // l'overlay EnchantService côté serveur).
                var pm = TextResolver.ResolveFile?.Invoke(
                    "data/db/c_itemmallenchant.ini");
                if (pm != null)
                    foreach (var kv in Load(pm)) defs.TryAdd(kv.Key, kv.Value);
                _defs = defs;
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[enchant] c_enchant.ini : {e.Message}");
                _defs = new();
            }
            return _defs;
        }
    }

    private static Dictionary<int, TextTable.TextRow> EnsureText()
    {
        if (_text != null) return _text;
        lock (_lock)
        {
            if (_text != null) return _text;
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(
                    $"data/db/lang/{TextResolver.Language}/t_enchant.ini")
                    ?? TextResolver.ResolveFile?.Invoke(
                        "data/db/T_Enchant.ini");
                _text = p != null ? TextTable.Load(p) : new();
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[enchant] T_Enchant.ini : {e.Message}");
                _text = new();
            }
            return _text;
        }
    }
}
