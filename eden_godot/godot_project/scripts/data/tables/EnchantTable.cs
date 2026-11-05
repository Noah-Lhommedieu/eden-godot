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
    public static int RideKeyOf(int enchantId) =>
        RideKeyOfRaw(Get(enchantId)?.Raw);

    /// <summary><see cref="RideKeyOf"/> sur les cellules brutes d'une ligne.</summary>
    public static int RideKeyOfRaw(string[]? raw)
    {
        if (raw == null) return 0;
        for (var i = 0; i + 1 < raw.Length; i++)
            if (int.TryParse(raw[i], out var code) && code == 2134 &&
                int.TryParse(raw[i + 1], out var k)) return k;
        return 0;
    }

    /// <summary>Commandes persistantes d'un enchant : 4 blocs « id + 6 vars »
    /// (cols 9+7k), hors blocs déclenchés (col40 : procs, pas un effet
    /// continu) ; la commande 160 enveloppe une commande imbriquée. Même
    /// lecture que <c>EnchantService.GetPassiveCommands</c> côté serveur.
    /// </summary>
    public static List<(int Id, double[] Args)> CommandsOf(EnchantDef def)
    {
        var raw = def.Raw;
        var trig = new HashSet<int>();
        if (raw.Length > 40)
            foreach (var t in raw[40].Split(';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(t, out var n)) trig.Add(n);
        var list = new List<(int, double[])>(4);
        for (var k = 0; k < 4; k++)
        {
            var at = 9 + k * 7;
            if (trig.Contains(k + 1) || at >= raw.Length ||
                !int.TryParse(raw[at], out var cmd) || cmd <= 0) continue;
            var args = new double[6];
            for (var i = 0; i < 6 && at + 1 + i < raw.Length; i++)
                double.TryParse(raw[at + 1 + i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out args[i]);
            if (cmd == 160 && args[0] > 0) list.Add(((int)args[0], args.Skip(1).ToArray()));
            else list.Add((cmd, args));
        }
        return list;
    }

    /// <summary>Catégorie d'affichage d'un status (buff / nerf / état / aucun) ;
    /// enchant inconnu → <see cref="StatusKind.None"/> (rien à afficher).</summary>
    public static StatusKind KindOf(int enchantId) =>
        Get(enchantId) is { } d
            ? StatusKinds.Classify(enchantId, CommandsOf(d), d.IsBuff)
            : StatusKind.None;

    /// <summary>Clic droit sur l'icône de la barre de buffs : retire le
    /// buff. Toute monture se retire (même « can't abandon »), sinon seuls
    /// les buffs positifs sans drapeau NoAbandon ; définition inconnue =
    /// retirable (le serveur tranche).</summary>
    public static bool CanCancelByRightClick(EnchantDef? def) =>
        RideKeyOfRaw(def?.Raw) > 0 ||
        !(def is { IsBuff: false } or { NoAbandon: true });

    /// <summary>Col3 « BUFF特效 » — clé d'effet visuel tenu sur
    /// l'entité tant que l'enchant est actif (flammes du DoT, étoiles
    /// du stun, ZZZ du sommeil — résolue via
    /// <c>SpellFxTable.Resolve</c>). null si la ligne n'en porte pas.</summary>
    public static string? VisualKeyOf(int enchantId)
    {
        var raw = Get(enchantId)?.Raw;
        if (raw == null || raw.Length <= 3) return null;
        var k = raw[3].Trim();
        return k.Length > 1 && k != "0" &&
            !k.Equals("null", StringComparison.OrdinalIgnoreCase) ? k : null;
    }

    /// <summary>Bonus de vitesse NATIF (commande 2051, en %) d'un enchant
    /// de monture ; 0 si l'enchant n'est pas une monture ou sans 2051.
    /// </summary>
    public static int RideNativeSpeedPct(int enchantId)
    {
        var raw = Get(enchantId)?.Raw;
        if (raw == null || RideKeyOf(enchantId) == 0) return 0;
        for (var i = 0; i + 1 < raw.Length; i++)
            if (int.TryParse(raw[i], out var code) && code == 2051 &&
                int.TryParse(raw[i + 1], out var pct)) return pct;
        return 0;
    }

    /// <summary>Texte d'enchant avec le palier de vitesse effectif pour
    /// une monture (35/55/70/90 %, <see cref="EdenClient.Game.MountCombatRules"/>) ;
    /// autres enchants : inchangé.</summary>
    private static string? WithRideTier(int enchantId, string? text)
    {
        if (text == null) return null;
        var pct = RideNativeSpeedPct(enchantId);
        return pct > 0 ? EdenClient.Game.MountCombatRules.TierText(text, pct) : text;
    }

    /// <summary>KB-01 : c_enchant EDENFR (<c>|V.2|60|</c>) n'a pas la
    /// colonne « groupe elf-pet » (index 6) du schéma S_Enchant
    /// <c>|V.3|61|</c>. Les lignes FR font 61 cellules (60 + « | »
    /// terminal vide, ou 56 si la fin est tronquée) : on réinsère une
    /// cellule vide en 6 pour tout lire à l'index S_ (cmds 9+7k, flags
    /// 7/8, description 56, stack 59). Les lignes déjà au schéma S_
    /// (62/57 cellules) et la ligne anomalie 46131 (55) restent telles
    /// quelles.</summary>
    public static string[] NormalizeRow(string[] row, int headerCols)
    {
        // c_itemmallenchant |V.2|60| : 62 cellules, même schéma FR (blocs de
        // commandes à 8+7k) — sans normalisation 55869 `2047|1` se lisait
        // en commande 1 (miroir de EnchantParser.NormalizeRow serveur).
        var fr = row.Length == 61 || (headerCols == 60 && row.Length is 56 or 62);
        if (!fr) return row;
        var r = new string[row.Length + 1];
        Array.Copy(row, 0, r, 0, 6);
        r[6] = string.Empty;
        Array.Copy(row, 6, r, 7, row.Length - 6);
        return r;
    }

    /// <summary>Charge c_enchant.ini → id → définition.</summary>
    public static Dictionary<int, EnchantDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, EnchantDef>(table.Rows.Count);
        foreach (var row0 in table.Rows)
        {
            var row = NormalizeRow(row0, table.ColumnCount);
            if (row.Length < 9 || !int.TryParse(row[0], out var id))
                continue;
            var flag1 = IniTable.GetInt(row, 7);
            var flag2 = IniTable.GetInt(row, 8);
            var maxStack = IniTable.GetInt(row, 59);
            dict[id] = new EnchantDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                Name = IniTable.GetStr(row, 5),
                PetGroup = IniTable.GetInt(row, 6),
                IsBuff = (flag1 & 1) != 0,
                NoAbandon = (flag2 & 0x10) != 0,
                NoReapply = (flag2 & 0x100) != 0,
                MaxStack = Math.Max(1, maxStack),
                Description = IniTable.GetStr(row, 56),
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
    /// le nom Big5 de c_enchant puis un libellé générique (jamais d'id).</summary>
    public static string NameOf(int enchantId)
    {
        if (EnsureText().TryGetValue(enchantId, out var t) &&
            t.Name.Length > 0)
            return t.Name;
        // Jamais d'identifiant à l'écran : repli sur le nom Big5 puis un
        // libellé générique.
        var d = Get(enchantId);
        return d != null && d.Name.Length > 0
            ? d.Name : TextResolver.Language == "fr" ? "Effet inconnu" : "Unknown effect";
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
        return d.Length > 0 ? WithRideTier(enchantId, d) : null;
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
            if (d.Length > 0) return WithRideTier(enchantId, d);
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
