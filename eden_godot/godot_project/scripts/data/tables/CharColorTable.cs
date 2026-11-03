namespace EdenClient.Data;

/// <summary>
/// Table des couleurs natives : <c>data/db/C_CharColor.ini</c>
/// (<c>idx|NOM|r|g|b|</c>, 241 lignes). <b>La colonne idx est la clé</b>
/// — la table est sectionnée (trous) :
///   1-64    <c>DYE_*</c>        — teintures équipement (palette 64
///             entrées <c>DAT_010c0f60</c> du client, remplie par
///             <c>GetData(1..64)</c> dans FUN_006e6620 client_game) ;
///   101-143 <c>HAIR_*</c>       — cheveux (générique) ;
///   201-220 <c>SKIN_TONE_*</c>  — peau humain/halfkin ;
///   301-340 <c>EYES_*</c>       — yeux ;
///   401-420 <c>SKIN_TONE_ZUMI_*</c>, 501-520 <c>SKIN_TONE_ANURAN_*</c>,
///   601-622 <c>SKIN_TONE_URSUN_*</c>, 701-712 <c>HAIR_COLOR_URSUN_*</c>.
///
/// Convention d'indexation — <b>preuve désasm client_game</b>
/// (identique rf_game, mêmes adresses) : le sélecteur 6 bits du blob
/// d'apparence s'<b>additionne à une base par code corps</b> :
///   skin : <c>FUN_0073e970</c> @0x0073e970 — corps (param_1+0x168)
///     2→0x191 (401), 4→0x1f5 (501), 6→0x259 (601), sinon 0xc9 (201) ;
///     <c>GetData(base + sel)</c> → couleur dye slot 2 ;
///   hair : <c>FUN_0073e8d0</c> — <c>sel + (corps==6 ? 0x2bd : 0x65)</c>
///     → 701 (Ursun) ou 101, dye slot 1 ;
///   eyes : <c>FUN_0073ea60</c>/<c>FUN_0073eaf0</c> — <c>sel + 0x12d</c>
///     (301), dye slot 3 (deux sous-couleurs).
/// Le code corps natif : {0:g, 1:b, 2:m, 4:f, 6:k, 8:l, 9:c}
/// (<see cref="EdenClient.CharModelSpec.BodyLetter"/>).
/// </summary>
public static class CharColorTable
{
    /// <summary>Triplet RGB normalisé 0-1.</summary>
    public readonly record struct Rgb(float R, float G, float B);

    /// <summary>Base de la section peau pour un code corps —
    /// <c>FUN_0073e970</c> : 2→401, 4→501, 6→601, sinon 201.</summary>
    public static int SkinBase(int bodyCode) => bodyCode switch
    {
        2 => 401,    // Zumi
        4 => 501,    // Anuran
        6 => 601,    // Ursun
        _ => 201,    // Human (g/b) + Halfkin (l/c)
    };

    /// <summary>Base de la section cheveux — <c>FUN_0073e8d0</c> :
    /// 701 pour l'Ursun (corps 6), 101 pour tous les autres.</summary>
    public static int HairBase(int bodyCode) => bodyCode == 6 ? 701 : 101;

    /// <summary>Base de la section yeux — <c>FUN_0073ea60</c> : 301
    /// quel que soit le corps.</summary>
    public const int EyesBase = 301;

    /// <summary>Couleur de peau : <c>GetById(SkinBase+sel)</c> —
    /// repli sur la première teinte de la section si l'id est absent
    /// (sel hors bornes, ex. skin=10 Ursun spécial).</summary>
    public static Rgb Skin(int bodyCode, int selector) =>
        GetById(SkinBase(bodyCode) + selector, SkinBase(bodyCode));

    /// <summary>Couleur de cheveux : <c>GetById(HairBase+sel)</c>.</summary>
    public static Rgb Hair(int bodyCode, int selector) =>
        GetById(HairBase(bodyCode) + selector, HairBase(bodyCode));

    /// <summary>Couleur d'yeux : <c>GetById(301+sel)</c>.</summary>
    public static Rgb Eyes(int selector) =>
        GetById(EyesBase + selector, EyesBase);

    /// <summary>Teinture équipement : le sélecteur 6 bits indexe la
    /// palette 64 = lignes id 1..64 (<c>DAT_010c0f60[i] =
    /// GetData(i+1)</c>, FUN_006e6620).</summary>
    public static Rgb Dye(int selector) => GetById(selector + 1, 1);

    /// <summary>Couleur d'un <b>id de ligne</b> natif. Si l'id est
    /// absent de la table, retourne la couleur de
    /// <paramref name="fallbackId"/> (si &gt; 0) sinon la carnation
    /// claire historique.</summary>
    public static Rgb GetById(int id, int fallbackId = 0)
    {
        EnsureLoaded();
        var map = _byId ?? _empty;
        if (map.TryGetValue(id, out var c)) return c;
        if (fallbackId > 0 && map.TryGetValue(fallbackId, out var fb))
            return fb;
        return new Rgb(1.0f, 0.82f, 0.66f);
    }

#if GODOT
    /// <summary>Versions Godot (hors NetTests).</summary>
    public static Godot.Color SkinColor(int bodyCode, int selector) =>
        ToColor(Skin(bodyCode, selector));
    public static Godot.Color HairTint(int bodyCode, int selector) =>
        ToColor(Hair(bodyCode, selector));
    public static Godot.Color EyesTint(int selector) =>
        ToColor(Eyes(selector));
    public static Godot.Color GetColor(int id) => ToColor(GetById(id));

    private static Godot.Color ToColor(Rgb c) => new(c.R, c.G, c.B);
#endif

    private static Dictionary<int, Rgb>? _byId;
    private static readonly Dictionary<int, Rgb> _empty = new();
    private static readonly object _lock = new();

    /// <summary>Résolution du fichier table, robuste à la course du
    /// boot : <see cref="TextResolver.ResolveFile"/> n'est posé que
    /// par CharSelectScreen/GameScene — PreGameWarmup (worker) peut
    /// atteindre TintFor avant. Repli : le nom minuscule réel de
    /// fr-full, puis la résolution insensible à la casse d'AssetCatalog
    /// (Godot seul — DataPaths/EdenVfs purs filesystem, sûrs en worker).
    /// </summary>
    private static string? ResolveTablePath()
    {
        var resolve = TextResolver.ResolveFile ??
            Tables.StoreTable.ResolveDefault;
        var path = resolve("data/db/C_CharColor.ini")
            ?? resolve("data/db/c_charcolor.ini");
#if GODOT
        path ??= AssetCatalog.ResolveRawAsset("data/db/C_CharColor.ini")
            ?? DataPaths.ResolveFile("data/db/C_CharColor.ini");
#endif
        return path;
    }

    private static void EnsureLoaded()
    {
        if (_byId != null) return;
        lock (_lock)
        {
            if (_byId != null) return;
            var map = new Dictionary<int, Rgb>();
            if (ResolveTablePath() is { } path)
            {
                var table = IniTable.ParseFile(path,
                    System.Text.Encoding.UTF8);
                foreach (var row in table.Rows)
                {
                    if (row.Length < 5 ||
                        !int.TryParse(row[0], out var id)) continue;
                    map[id] = new Rgb(
                        IniTable.GetInt(row, 2) / 255f,
                        IniTable.GetInt(row, 3) / 255f,
                        IniTable.GetInt(row, 4) / 255f);
                }
            }
            // Table vide = fichier pas encore résoluble : ne pas figer, réessayer.
            if (map.Count > 0) _byId = map;
        }
    }
}
