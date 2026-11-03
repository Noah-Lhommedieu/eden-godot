using EdenClient.Protocol;

namespace EdenClient.Game;

/// <summary>
/// Mapping apparence → modèle corps du personnage.
///
/// Preuve : <c>rf_game FUN_0073ddc0</c> @0x0073ddc0 — le client natif
/// switch sur un octet « code corps » et retourne le KFM de base :
///   1 → b.kfm   2 → m.kfm   4 → f.kfm   6 → k.kfm
///   8 → l.kfm   9 → c.kfm   défaut → g.kfm
/// (miroir serveur : <c>MotionParser.KfmForGender</c>, nibble
/// char+0x4aa = app[28]&gt;&gt;4 du blob d'apparence — voir
/// <see cref="AppearanceCodec"/>).
///
/// Le KFM x.kfm pointe <c>char/model/X.nif</c> + <c>char/animation/
/// X.kf</c> — chez nous : <c>assets/models/char/model/&lt;L&gt;.glb</c>
/// (B/C/F/G/K/L/M.glb convertis, résolution insensible à la casse).
///
/// Sémantique race↔code : HYPOTHESIS — le code n'est PAS le race_id
/// direct ; c'est un index corps dérivé race+genre choisi par l'UI de
/// création native. Valeurs observées à couvrir : tous les codes du
/// switch. Sans donnée fiable, on retombe sur « g » (modèle humain
/// féminin normal de référence) pour les codes inconnus — le natif
/// fait pareil (default → g.kfm).
/// </summary>
public static class CharModelSpec
{
    /// <summary>Lettre du modèle corps pour un code apparence
    /// (app[28]&gt;&gt;4). Switch natif exact, défaut « g ».</summary>
    public static string BodyLetter(int bodyCode) => bodyCode switch
    {
        0 => "g",
        1 => "b",
        2 => "m",
        4 => "f",
        6 => "k",
        8 => "l",
        9 => "c",
        _ => "g",
    };

    /// <summary>Stem GLB du corps pour un blob d'apparence 32 octets
    /// (null/vide → modèle par défaut « g »).</summary>
    public static string BodyStem(byte[]? appearance32) =>
        appearance32 is { Length: >= 29 }
            ? BodyLetter(appearance32[28] >> 4)
            : "g";

    /// <summary>Chemin relatif asset du modèle corps
    /// (« char/model/b » pour la lettre b).</summary>
    public static string BodyModelPath(byte[]? appearance32) =>
        $"char/model/{BodyStem(appearance32)}";

    // -------------------- équipement → modèles (4pequip.ini) --------

    /// <summary>Une entrée de <c>data/db/4pequip.ini</c> : le mesh
    /// d'équipement pour un code d'item — <paramref name="Mesh"/> de
    /// la forme « #B230 » (mesh du mega-GLB corps) ou « B1246 » /
    /// « X652A » (GLB séparé <c>char/model/&lt;mesh&gt;.glb</c>),
    /// « NULL » = invisible. <paramref name="Group"/> : 0 = tenue
    /// corps (mesh # interne), 2 = tête, 1/4 = dos/cape (GLB
    /// séparés). <paramref name="Dye"/> : spec de teinture 48 bits
    /// (col. hex — 3 shorts 16 bits, un par canal R/G/B du masque
    /// *ASnnB : bit15=0 → couleur RGB555 directe, bit15=1 → deux
    /// indices 6 bits palette DYE_* + blend — preuve désasm
    /// FUN_00ab6b30/FUN_00d10c70 client_game).</summary>
    public sealed record EquipMesh(string Mesh, string Skin, int Group,
        ulong Dye = 0);

    private static readonly Dictionary<string,
            Dictionary<string, EquipMesh>> _pequipCache = new();
    // Verrou dédié : LoadEquipMap peut tourner en tâche de fond
    // (preview async du sélecteur — CharVisual.BuildBody hors thread
    // principal) alors que le jeu principal la lit aussi.
    private static readonly object _pequipLock = new();

    /// <summary>Section 4pequip pour une lettre de corps :
    /// b→TableBoy, g→TableGirl, m→TableMouse, f→TableFrog,
    /// k→TableKuma, l→TableLoli, c→TableShota (en-têtes du ini).</summary>
    public static string EquipTableName(string bodyLetter) =>
        bodyLetter.ToLowerInvariant() switch
        {
            "b" => "TableBoy", "g" => "TableGirl",
            "m" => "TableMouse", "f" => "TableFrog",
            "k" => "TableKuma", "l" => "TableLoli",
            "c" => "TableShota",
            _ => "TableGirl",
        };

    /// <summary>Stem du GLB arme pour un code/modèle d'item
    /// (<c>W13202</c> → <c>W132</c> — « W » + 3 chiffres de modèle,
    /// 2 derniers = variante ; vérifié sur item/model/*.glb).
    /// Null si le code n'est pas un modèle d'arme.</summary>
    public static string? WeaponModelStem(string? modelCode) =>
        modelCode is { Length: >= 4 } m &&
        (m[0] is 'W' or 'w') && char.IsDigit(m[1])
            ? m[..4].ToUpperInvariant() : null;

    /// <summary>Code de skin d'une arme (<c>W40106</c> → texture
    /// <c>item/texture/W40106</c>) : les 2 derniers chiffres du modèle
    /// sont la VARIANTE (texture propre) ; le GLB W401 n'embarque que
    /// W40101 — sans ceci toutes les variantes d'une arme avaient le
    /// même skin. Null si le code n'a pas de variante.</summary>
    public static string? WeaponSkinCode(string? modelCode) =>
        WeaponModelStem(modelCode) != null && modelCode!.Length >= 6
            ? modelCode[..6].ToUpperInvariant() : null;
#if GODOT

    /// <summary>Pose la texture de variante <paramref name="skin"/> sur
    /// les surfaces du GLB arme dont la texture d'origine est celle du
    /// modèle (même base, méta « src_tex »). Matériaux dupliqués
    /// (les protos sont partagés).</summary>
    public static void ApplyWeaponSkin(Godot.Node node, string skin)
    {
        var tex = AssetCatalog.GetTexture($"item/texture/{skin}.png");
        if (tex == null) return;
        var baseKey = skin[..^2].ToLowerInvariant();
        foreach (var mi in node.FindChildren("*", "MeshInstance3D", true,
                     false).Cast<Godot.MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ??
                          mi.Mesh.SurfaceGetMaterial(s);
                if (mat == null || !mat.HasMeta("src_tex") ||
                    mat.GetMeta("src_tex").AsString() is not
                        { Length: > 2 } t || t[..^2] != baseKey)
                    continue;
                if (mat is Godot.ShaderMaterial sm)
                {
                    var d = (Godot.ShaderMaterial)sm.Duplicate();
                    d.SetShaderParameter("albedo_tex", tex);
                    mi.SetSurfaceOverrideMaterial(s, d);
                }
                else if (mat is Godot.StandardMaterial3D bm)
                {
                    var d = (Godot.StandardMaterial3D)bm.Duplicate();
                    d.AlbedoTexture = tex;
                    mi.SetSurfaceOverrideMaterial(s, d);
                }
            }
        }
    }
#endif

    /// <summary>Charge la table 4pequip du corps
    /// <paramref name="bodyLetter"/> → code item (col2 c_item) OU
    /// modelCode (col3) → <see cref="EquipMesh"/>. Lignes
    /// <c>DBn=&lt;code&gt;,&lt;grp&gt;,&lt;mesh&gt;,&lt;skin&gt;,…</c>
    /// de <c>[TableX]</c>. Paresseux + cache par lettre ; vide si le
    /// ini est absent.</summary>
    public static IReadOnlyDictionary<string, EquipMesh>
        LoadEquipMap(string bodyLetter)
    {
        var letter = bodyLetter.ToLowerInvariant();
        lock (_pequipLock)
            if (_pequipCache.TryGetValue(letter, out var cached))
                return cached;
        var map = new Dictionary<string, EquipMesh>(
            StringComparer.OrdinalIgnoreCase);
        // Résolution raw_assets partagée (TextResolver.ResolveFile
        // est branché sur AssetCatalog en jeu — même pattern que
        // ItemTableCache ; StoreTable.ResolveDefault couvre dev/tests).
        var path = (Data.TextResolver.ResolveFile
            ?? Data.Tables.StoreTable.ResolveDefault)(
                "data/db/4pequip.ini");
        if (path != null)
        {
            var section = "";
            var wanted = EquipTableName(letter);
            try
            {
                foreach (var raw in Vfs.EdenVfs.ReadLines(path,
                             Data.EdenEncoding.Big5))
                {
                    var line = raw.Trim();
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        section = line[1..^1];
                        continue;
                    }
                    if (!string.Equals(section, wanted,
                            StringComparison.OrdinalIgnoreCase) ||
                        !line.StartsWith("DB", StringComparison.Ordinal))
                        continue;
                    var eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    var parts = line[(eq + 1)..].Trim().Split(',');
                    if (parts.Length < 4) continue;
                    var code = parts[0].Trim();
                    if (!int.TryParse(parts[1].Trim(), out var grp))
                        continue;
                    var dye = 0ul;
                    if (parts.Length > 5)
                        ulong.TryParse(parts[5].Trim(),
                            System.Globalization.NumberStyles.HexNumber,
                            null, out dye);
                    map[code] = new EquipMesh(parts[2].Trim(),
                        parts[3].Trim(), grp, dye);
                }
            }
            catch (Exception e)
            {
                Data.TextResolver.Log?.Invoke(
                    $"[equip-map] 4pequip.ini : {e.Message}");
            }
        }
        lock (_pequipLock)
            _pequipCache[letter] = map;
        return map;
    }

    /// <summary>Ligne 4pequip d'un <b>suffixe de mesh tenue</b>
    /// (« #B230 » → « 230 ») — première ligne qui le référence ;
    /// sert de repli pour les tenues affichées sans item résolu
    /// (tenue de débutant du sélecteur). Null si aucune.</summary>
    public static EquipMesh? RowForSuffix(string bodyLetter, string suffix)
    {
        var wanted = "#" + bodyLetter.ToUpperInvariant() + suffix;
        foreach (var e in LoadEquipMap(bodyLetter))
            if (string.Equals(e.Value.Mesh, wanted,
                    StringComparison.OrdinalIgnoreCase))
                return e.Value;
        return null;
    }

    // ---- tenues de classe (c_class.ini : « niveau itemHomme itemFemme 0 », jusqu'à 9) ----
    private static Dictionary<int, List<(int Level, string Male, string Female)>>? _closet;
    private static readonly object _closetLock = new();

    /// <summary>Tenues débloquables d'une classe, par palier de niveau (c_class cols 55+).</summary>
    public static IReadOnlyList<(int Level, string Male, string Female)> ClassCloset(int classId)
    {
        lock (_closetLock)
        {
            if (_closet == null)
            {
                var path = (Data.TextResolver.ResolveFile ?? Data.Tables.StoreTable.ResolveDefault)("data/db/c_class.ini");
                if (path == null) return System.Array.Empty<(int, string, string)>();
                _closet = new();
                foreach (var cols in Data.IniTable.ParseFile(path).Rows)
                {
                    var id = Data.IniTable.GetInt(cols, 0);
                    if (id <= 0) continue;
                    var list = new List<(int, string, string)>();
                    foreach (var c in cols)
                    {
                        var w = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (w.Length == 4 && int.TryParse(w[0], out var lv) &&
                            w[1].Length >= 5 && char.IsLetter(w[1][0]) && char.IsDigit(w[1][1]) &&
                            w[3] == "0")
                            list.Add((lv, w[1], w[2]));
                    }
                    _closet[id] = list;
                }
            }
            return _closet.TryGetValue(classId, out var l) ? l : System.Array.Empty<(int, string, string)>();
        }
    }

    /// <summary>Tenue de base de la classe <paramref name="classId"/> (style = index de garde-robe
    /// 0..8) : ligne 4pequip du corps dont le code item est celui de la classe. Null → repli.</summary>
    public static EquipMesh? ClassOutfitRow(string bodyLetter, int classId, int style)
    {
        var list = ClassCloset(classId);
        if (list.Count == 0) return null;
        var e = list[Math.Clamp(style, 0, list.Count - 1)];
        var map = LoadEquipMap(bodyLetter);
        if (map.TryGetValue(e.Female, out var r) && r.Mesh.StartsWith('#')) return r;
        if (map.TryGetValue(e.Male, out r) && r.Mesh.StartsWith('#')) return r;
        return null;
    }
}
