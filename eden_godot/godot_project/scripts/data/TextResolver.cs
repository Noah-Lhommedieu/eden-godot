using EdenClient.Data.Tables;

namespace EdenClient.Data;

/// <summary>
/// Résolution centralisée des textes localisés EN depuis les tables
/// <c>data/db/T_*.ini</c> du client Reforged — remplace les noms Big5
/// (chinois) embarqués dans les tables C_* et dans les paquets
/// serveur (SeeMob.DisplayName, scene ini…).
///
/// Tables vérifiées en ASCII anglais (décodage Big5 inoffensif —
/// ASCII ⊆ Big5) :
///   - t_item.ini    : col1 nom, col2 description
///   - t_monster.ini : col1 nom (« Rose Knight »)
///   - t_npc.ini     : col1 « &lt;Titre&gt;\nNom » (CRLF embarqué),
///                     col2 fonction (« Merchant », « Quest »…)
///   - t_mission.ini : col1 titre, col2 zone, col3 description
///   - t_node.ini    : col1 nom de zone (id = n° de Sxxx)
///   - T_Class.ini   : col1 nom de classe (via <see cref="ClassTextTable"/>)
///   - T_Spell.ini   : col1 nom de sort
///   - t_itemmall.ini: col1 nom / col2 desc des items mall
///
/// Chaque table est chargée paresseusement (une fois) et mise en
/// cache ; un id absent retourne <see langword="null"/> — l'appelant
/// garde alors le repli Big5/numérique existant.
/// </summary>
public static class TextResolver
{
    private static readonly object _lock = new();
    private static Dictionary<int, TextTable.TextRow>? _item;
    private static Dictionary<int, TextTable.TextRow>? _itemMall;
    private static Dictionary<int, TextTable.TextRow>? _monster;
    private static Dictionary<int, TextTable.TextRow>? _npc;
    private static Dictionary<int, TextTable.TextRow>? _npcExtra;
    private static Dictionary<int, TextTable.TextRow>? _mission;
    private static Dictionary<int, TextTable.TextRow>? _node;
    private static Dictionary<int, TextTable.TextRow>? _spell;
    private static Dictionary<int, TextTable.TextRow>? _partner;
    private static Dictionary<int, TextTable.TextRow>? _dialogue;
    private static Dictionary<int, TextTable.TextRow>? _textIndex;
    private static Dictionary<int, ClassTextTable.ClassText>? _class;

    // ------------------------- items ------------------------------

    /// <summary>Nom anglais d'un item (t_item.ini col1), null si
    /// absent — repli c_item.ini col7 (Big5) côté appelant.</summary>
    public static string? ItemName(int itemId)
    {
        // t_itemmall.ini peut écraser des ids mall (doublons « Testing
        // ID » de t_item — convention serveur : mall prioritaire).
        var name = Lookup(ref _itemMall, "t_itemmall.ini", itemId, 1);
        if (name != null) return name;
        return Lookup(ref _item, "t_item.ini", itemId, 1);
    }

    /// <summary>Description anglaise (t_item.ini col2), nettoyée des
    /// codes <c>$N$</c>.</summary>
    public static string? ItemDescription(int itemId)
    {
        var d = Lookup(ref _itemMall, "t_itemmall.ini", itemId, 2)
                ?? Lookup(ref _item, "t_item.ini", itemId, 2);
        return d;
    }

    /// <summary>Description anglaise AVEC les codes couleur
    /// <c>$N$</c> de ligne (tooltip d'item natif).</summary>
    public static string? ItemDescriptionRaw(int itemId)
    {
        var raw = Ensure(ref _itemMall, "t_itemmall.ini", TextTable.Load);
        if (raw != null && raw.TryGetValue(itemId, out var r)
            && r.Field(2).Trim().Length > 0)
            return r.Field(2);
        var it = Ensure(ref _item, "t_item.ini", TextTable.Load);
        return it != null && it.TryGetValue(itemId, out var r2)
            && r2.Field(2).Trim().Length > 0 ? r2.Field(2) : null;
    }

    /// <summary>Texte brut d'une colonne t_item (tooltip riche).</summary>
    public static string? ItemField(int itemId, int col) =>
        Lookup(ref _item, "t_item.ini", itemId, col);

    // ------------------------- entités ----------------------------

    /// <summary>Nom anglais d'un monstre (t_monster.ini col1) — brut :
    /// les élites/mini-boss sont « &lt;Titre&gt;\nNom ».</summary>
    public static string? MonsterName(int monsterId) =>
        Lookup(ref _monster, "t_monster.ini", monsterId, 1);

    /// <summary>NOM seul (dernière ligne) — pour les libellés mono-ligne
    /// (objectifs de quête, cadre de cible).</summary>
    public static string? MonsterNameShort(int monsterId) =>
        MonsterName(monsterId) is { } n ? NameOnly(n) : null;

    /// <summary>« &lt;Titre&gt;\nNom » → « Nom ».</summary>
    public static string NameOnly(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : raw.Trim();
    }

    /// <summary>Nom anglais d'un NPC (t_npc.ini col1). Le champ contient
    /// « &lt;Titre&gt;\nNom » : on renvoie le NOM seul (dernière ligne) —
    /// le titre est exposé via <see cref="NpcTitle"/>.</summary>
    public static string? NpcName(int npcId)
    {
        var raw = Lookup(ref _npc, "t_npc.ini", npcId, 1)
            ?? Lookup(ref _npcExtra, "t_npc_extra.ini", npcId, 1);
        if (raw == null) return null;
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        // « <General Merchant>\nCarrack » → « Carrack » ; pas de titre →
        // le nom est la seule ligne.
        return lines.Length > 0 ? lines[^1] : raw;
    }

    /// <summary>Titre du NPC (première ligne de col1 quand il y en a
    /// deux : « Village Head\r\nAndrew » → « Village Head » ; les
    /// éventuels chevrons « &lt;…&gt; » sont retirés). Null si absent.</summary>
    public static string? NpcTitle(int npcId)
    {
        var raw = Lookup(ref _npc, "t_npc.ini", npcId, 1)
            ?? Lookup(ref _npcExtra, "t_npc_extra.ini", npcId, 1);
        if (raw == null) return null;
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return null;
        var first = lines[0];
        return first.StartsWith('<') && first.EndsWith('>')
            ? first[1..^1] : first;
    }

    /// <summary>Fonction du NPC (col2 : « Merchant », « Quest »…) —
    /// utile pour les icônes au-dessus des PNJ.</summary>
    public static string? NpcFunction(int npcId) =>
        Lookup(ref _npc, "t_npc.ini", npcId, 2);

    /// <summary>Nom anglais d'un sort (T_Spell.ini col1).</summary>
    public static string? SpellName(int spellId) =>
        Lookup(ref _spell, "T_Spell.ini", spellId, 1);

    /// <summary>Nom anglais d'un familier (t_partner.ini col1 — clé =
    /// id item œuf = templateId du wire 412).</summary>
    public static string? PartnerName(int partnerId) =>
        Lookup(ref _partner, "t_partner.ini", partnerId, 1);

    /// <summary>Description d'un sort (T_Spell.ini col2), null si absente.</summary>
    public static string? SpellDescription(int spellId) =>
        Lookup(ref _spell, "T_Spell.ini", spellId, 2);

    /// <summary>Description brute d'un sort AVEC les codes <c>$N$</c>
    /// et les gabarits <c>#N#</c> (coût, cooldown, armes, valeurs
    /// mises à l'échelle) — le tooltip de sort les résout.</summary>
    public static string? SpellDescriptionRaw(int spellId) =>
        LookupRaw(ref _spell, "T_Spell.ini", spellId, 2);

    /// <summary>Message système par index (t_textindex.ini col1 — NCID
    /// 316 ClientTextOutputIndex ; « %s1 » = paramètre du paquet).
    /// Null si l'index est inconnu.</summary>
    public static string? TextIndex(int index, string arg = "")
    {
        var t = Lookup(ref _textIndex, "t_textindex.ini", index, 1);
        return t?.Replace("%s1", arg);
    }

    // ------------------------- dialogues ---------------------------

    /// <summary>Texte de dialogue EN (t_dialogue.ini col1 — ids
    /// ctx/displayId du NCID 285 ClientDisplayDialogView). Le texte
    /// peut être une liste « id1,id2,… » multi-parties côté natif ;
    /// on renvoie le champ brut nettoyé.</summary>
    public static string? Dialogue(int dialogueId) =>
        Lookup(ref _dialogue, "t_dialogue.ini", dialogueId, 1);

    // ------------------------- quêtes -----------------------------

    /// <summary>Titre de mission EN (t_mission.ini col1, ex.
    /// « L01. Awakening I »).</summary>
    public static string? MissionTitle(int missionId) =>
        Lookup(ref _mission, "t_mission.ini", missionId, 1);

    /// <summary>Zone de la mission (t_mission.ini col2).</summary>
    public static string? MissionZone(int missionId) =>
        Lookup(ref _mission, "t_mission.ini", missionId, 2);

    /// <summary>Description/objectif (t_mission.ini col3).</summary>
    public static string? MissionDescription(int missionId) =>
        Lookup(ref _mission, "t_mission.ini", missionId, 3);

    // ------------------------- monde ------------------------------

    /// <summary>Nom de zone anglais (t_node.ini col1 ; id = numéro du
    /// code « Sxxx », ex. 2 → « Limestone Mtn. »).</summary>
    public static string? NodeName(int nodeId) =>
        Lookup(ref _node, "t_node.ini", nodeId, 1);

    /// <summary>Description d'une zone (t_node.ini col2).</summary>
    public static string? NodeDescription(int nodeId) =>
        Lookup(ref _node, "t_node.ini", nodeId, 2);

    /// <summary>Description de zone brute ($N$ conservés) — écran de
    /// chargement (lang/fr prioritaire, repli EN).</summary>
    public static string? NodeDescriptionRaw(int nodeId) =>
        LookupRaw(ref _node, "t_node.ini", nodeId, 2);

    /// <summary>Sous-titre de zone = dernière colonne de t_node.ini
    /// (« Aven — The Island Sanctuary »), null si absent.</summary>
    public static string? NodeSubtitle(int nodeId)
    {
        var table = Ensure(ref _node, "t_node.ini", TextTable.Load);
        if (table == null || !table.TryGetValue(nodeId, out var row) ||
            row.Cells.Length <= 12)
            return null;
        var v = Clean(row.Cells[row.Cells.Length - 1]);
        return v.Length > 0 ? v : null;
    }

    /// <summary>Textes EN complets d'une classe (T_Class.ini : description,
    /// trait, prérequis) ; les ids composites (105 = Thief humain)
    /// retombent sur la classe de base.</summary>
    public static ClassTextTable.ClassText? ClassInfo(int classId)
    {
        var table = Ensure(ref _class, "T_Class.ini", ClassTextTable.Load);
        if (table == null) return null;
        return table.TryGetValue(classId, out var t) ? t
            : table.TryGetValue(classId % 100, out var b) ? b : null;
    }

    /// <summary>Nom de classe anglais (T_Class.ini col1). Les ids
    /// composites race×100 (101.., 201.., 301.., 401..) sont ramenés à
    /// l'id de base par %100 — même convention que
    /// <c>ProfileWindow</c>/création perso.</summary>
    public static string? ClassName(int classId)
    {
        var table = Ensure(ref _class, "T_Class.ini", ClassTextTable.Load);
        var baseId = classId > 100 ? classId % 100 : classId;
        if (table != null && table.TryGetValue(baseId, out var ct) &&
            ct.Name.Length > 0)
            return ct.Name;
        return null;
    }

    // ------------------------- moteur -----------------------------

    /// <summary>Charge + lookup d'une table de texte ; retourne le
    /// champ nettoyé (codes <c>$N$</c> retirés) ou null.</summary>
    private static string? Lookup(
        ref Dictionary<int, TextTable.TextRow>? cache,
        string file, int id, int col)
    {
        var table = Ensure(ref cache, file, TextTable.Load);
        if (table == null ||
            !table.TryGetValue(id, out var row))
            return null;
        var v = Clean(row.Field(col));
        return v.Length > 0 ? v : null;
    }

    /// <summary>Lookup sans nettoyage <see cref="Clean"/> — conserve
    /// les codes <c>$N$</c>/<c>#N#</c> du gabarit.</summary>
    private static string? LookupRaw(
        ref Dictionary<int, TextTable.TextRow>? cache,
        string file, int id, int col)
    {
        var table = Ensure(ref cache, file, TextTable.Load);
        if (table == null ||
            !table.TryGetValue(id, out var row))
            return null;
        var v = row.Field(col);
        return v.Trim().Length > 0 ? v : null;
    }

    /// <summary>Résolution fichier data/db — branchée par la couche
    /// Godot sur <c>AssetCatalog.ResolveRawAsset</c>
    /// (EntityRegistry fait pareil pour <c>MotionTable</c>). En dehors
    /// du moteur (tests NetTests sans GodotSharp), le défaut cherche
    /// <c>raw_assets/</c> en remontant depuis le répertoire courant ou
    /// <c>$EDEN_RAW_ASSETS</c>.</summary>
    public static Func<string, string?>? ResolveFile;

    /// <summary>Log optionnel des erreurs de chargement (GD.PrintErr
    /// côté jeu ; stderr/tests sinon).</summary>
    public static Action<string>? Log;

    /// <summary>Langue des tables texte : <c>en</c> (défaut, tables
    /// Reforged) ou <c>fr</c> — les T_* localisés vivent sous
    /// <c>data/db/lang/fr/</c> avec repli sur <c>data/db/</c> pour les
    /// ids/fichiers absents.</summary>
    public static string Language => NativeOnly ? "fr" : _language;
    private static string _language = "en";

    /// <summary>EDENFR pur (PLAN §8.1) : la base EST le français —
    /// aucun overlay <c>lang/&lt;l&gt;</c> ni repli sur les tables
    /// anglaises ; langue forcée « fr ». Posé par le client Flax au
    /// démarrage ; les tests de la voie historique le laissent à false.</summary>
    public static bool NativeOnly
    {
        get => _nativeOnly;
        set
        {
            if (_nativeOnly == value) return;
            _nativeOnly = value;
            ResetCaches();
            UiTextEn.Reset();
        }
    }
    private static bool _nativeOnly;

    /// <summary>Émis après <see cref="SetLanguage"/> quand la langue a
    /// réellement changé — les panneaux visibles se rechargent.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Bascule la langue à chaud : invalide tous les caches
    /// de tables texte + le dictionnaire UI, puis notifie.</summary>
    public static void SetLanguage(string lang)
    {
        lang = lang == "fr" ? "fr" : "en";
        if (lang == _language) return;
        _language = lang;
        ResetCaches();
        UiTextEn.Reset();
        try { LanguageChanged?.Invoke(); }
        catch (Exception ex) { Log?.Invoke($"[loc] {ex.Message}"); }
    }

    /// <summary>Invalide tous les caches paresseux — nécessaire au
    /// changement de langue.</summary>
    public static void ResetCaches()
    {
        lock (_lock)
        {
            _item = null; _itemMall = null; _monster = null;
            _npc = null; _npcExtra = null; _mission = null;
            _node = null; _spell = null; _partner = null;
            _dialogue = null; _textIndex = null; _class = null;
            Tables.EnchantTable.Reset();
        }
    }

    private static string? DefaultResolve(string relPath)
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env))
        {
            var p = Path.Combine(env,
                relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return p;
        }
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "raw_assets",
                relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    /// <summary>Chargement paresseux thread-safe d'une table ; les
    /// échecs sont mis en cache (dictionnaire vide) pour ne pas
    /// réessayer à chaque lookup.</summary>
    private static Dictionary<int, T>? Ensure<T>(
        ref Dictionary<int, T>? cache, string file,
        Func<string, Dictionary<int, T>> loader)
    {
        if (cache != null) return cache;
        lock (_lock)
        {
            if (cache != null) return cache;
            var resolve = ResolveFile ?? DefaultResolve;
            string? overlay = _nativeOnly ? null
                : resolve($"data/db/lang/{Language}/{file}");
            string? basePath = resolve($"data/db/{file}");
            try
            {
                if (overlay != null && basePath != null && overlay != basePath)
                {
                    // Fusion par id : la langue écrase la base, les ids
                    // absents de l'overlay retombent sur la base.
                    cache = loader(basePath);
                    foreach (var kv in loader(overlay))
                        cache[kv.Key] = kv.Value;
                }
                else
                {
                    cache = (overlay ?? basePath) != null
                        ? loader((overlay ?? basePath)!)
                        : new Dictionary<int, T>();
                }
                // Langue ≠ EN : les ids absents retombent sur la table
                // anglaise (ex. contenu US 3xxxx jamais traduit — sans
                // ça le joueur FR voit le nom Big5 brut).
                if (Language != "en" && !_nativeOnly)
                {
                    var en = resolve($"data/db/lang/en/{file}");
                    if (en != null)
                        foreach (var kv in loader(en))
                            cache.TryAdd(kv.Key, kv.Value);
                }
            }
            catch (Exception e)
            {
                Log?.Invoke($"[text] {file} : {e.Message}");
                cache = new Dictionary<int, T>();
            }
            return cache;
        }
    }

    /// <summary>Nettoie un champ T_* : retire les codes couleur/format
    /// <c>$N$</c> (ex. « $7$"A thick layer…" » → « "A thick layer…" »)
    /// et rogne les blancs. Les CRLF embarqués sont conservés.</summary>
    public static string Clean(string text)
    {
        if (text.Length == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '$')
            {
                // Code $N$ (N = 1+ chiffres) — sauté.
                var j = i + 1;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                if (j < text.Length && j > i + 1 && text[j] == '$')
                {
                    i = j;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
