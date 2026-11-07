using EdenClient.World;

namespace EdenClient.Data;


/// <summary>Catégorie fonctionnelle d'un PNJ (c_npc.ini col 20 — « 商人 »,
/// « 任務 », « 傳送陣 »…) traduite en libellés de la Region Map
/// (Search Listing de la ref : Merchant, Quest, Warp Portal…).</summary>
public static class NpcCategoryTable
{
    private static Dictionary<int, string>? _cat;

    private static readonly Dictionary<string, string> En = new()
    {
        ["商人"] = "Merchant", ["任務"] = "Quest", ["每日任務"] = "Daily Challenges",
        ["傳送陣"] = "Warp Portal", ["傳送師"] = "Warp Portal",
        ["採集生產"] = "Gather/Craft", ["活動"] = "Event",
        ["地區聲望"] = "Regional Prestige", ["九大聲望"] = "Nine Prestiges",
        ["水晶十字商人"] = "Crystal Cross Merchant",
        ["公會聖物"] = "Guild Town", ["職業覺醒"] = "Class Awakening Quest",
        ["情侶"] = "Couple", ["召喚師"] = "Summoner", ["英雄試煉"] = "Trial",
        ["神殿騎士"] = "Temple Knight", ["奇幻副本"] = "Mystic Dungeon",
        ["領地戰商人"] = "Territory War Merchant",
        ["銀行拍賣場"] = "Auction House", ["競技場商人"] = "Arena Battle Merchant",
        ["毀滅幻境"] = "Mystic Dungeon", ["其它"] = "Other", ["其他"] = "Other",
    };

    private static readonly Godot.Color[] Palette =
    {
        Godot.Color.Color8(20, 130, 40), Godot.Color.Color8(180, 125, 0),
        Godot.Color.Color8(200, 80, 20), Godot.Color.Color8(30, 90, 210),
        Godot.Color.Color8(150, 40, 150), Godot.Color.Color8(0, 135, 135),
        Godot.Color.Color8(70, 70, 70),
    };

    /// <summary>Couleur stable d'une catégorie (Region Map et minimap
    /// partagent la même légende).</summary>
    public static Godot.Color ColorOf(string category)
    {
        var h = 17;
        foreach (var ch in category) h = h * 31 + ch;
        return Palette[(h & 0x7fffffff) % Palette.Length];
    }

    /// <summary>Icône native <c>UI/uiicon/mapNNN</c> du marqueur minimap
    /// pour le tag catégorie brut (c_npc col 20). Correspondances
    /// prouvées par la légende « City Features » + la minimap du ref
    /// (cubes map055 = « Cube Energy », halo map010 = « Revival Point »,
    /// porte map011 = « Portal », coffre map009 = « Auction »,
    /// pioche map028 = « Gather/Craft », cœur map017 = « Couple »,
    /// blob rose map012 = alpaca, parchemin map015 = « Library ») ;
    /// table index→fichier native extraite du désasm (rf_game
    /// FUN_008194b0/FUN_007ed830). Défaut = map005, l'emblème PNJ
    /// générique (« General Merchant ») dominant dans le ref. La
    /// distinction weapon/armor merchant n'est pas portée par la
    /// catégorie — tous les marchands gardent map005.</summary>
    public static string IconFile(string tag) => tag switch
    {
        "傳送陣" or "傳送師" => "map011",
        "銀行拍賣場" => "map009",
        "採集生產" => "map028",
        "情侶" => "map017",
        "圖書館" => "map015",
        "羊駝王" => "map012",
        "魔方能量活動" => "map055",
        _ => "map005",
    };

    /// <summary>Icône native <c>mapNNN</c> par type de service PNJ —
    /// table indexée par type de <c>CZoneMap</c> (client_game
    /// <c>FUN_008194b0</c> @0x8194b0, 41 entrées ; map000 = « aucune »
    /// donc absente ici). Recoupée avec c_npc (1 arme=map006, 2 armure=
    /// map007, 3 général=map005, 18 enchères=map009, 23 gloire=map004)
    /// et la légende « City Features » du ref carte.png.</summary>
    private static readonly Dictionary<int, string> TypeIcon = new()
    {
        [1] = "map006", [2] = "map007", [3] = "map005", [4] = "map008",
        [8] = "map011", [12] = "map028", [13] = "map029", [14] = "map030",
        [15] = "map031", [16] = "map032", [17] = "map033", [18] = "map009",
        [23] = "map004", [26] = "map017", [27] = "map010", [28] = "map023",
        [29] = "map024", [30] = "map021", [31] = "map022", [32] = "map024",
        [36] = "map051", [37] = "map052", [38] = "map026", [39] = "map027",
        [40] = "map055",
    };

    /// <summary>Type de service natif du PNJ : <c>atoi(col10.split('#')[1])</c>
    /// sinon 3e jeton (<c>:Service id TYPE …</c>) du 1er slot d'action
    /// (cols 11-18) — règle de <c>FUN_007efdc0</c> @0x7efdc0 (CUIRadarWnd,
    /// remplissage de la liste d'icônes radar). 0 = aucun.</summary>
    public static int ServiceType(int npcId)
    {
        Ensure();
        return _svc != null && _svc.TryGetValue(npcId, out var t) ? t : 0;
    }

    /// <summary>Icône du type (null si le natif n'en a pas).</summary>
    public static string? IconOfType(int type) =>
        TypeIcon.TryGetValue(type, out var f) ? f : null;

    /// <summary>Icône native d'un PNJ : type de service d'abord, puis
    /// repli sur la catégorie c_npc (portails, banque, couple…), sinon
    /// null — le radar natif ne dessine rien pour un PNJ sans type
    /// (switch par défaut de <c>FUN_007eea00</c> @0x7eea00).</summary>
    public static string? IconOrNull(int npcId)
    {
        Ensure();
        var byType = IconOfType(ServiceType(npcId));
        if (byType != null) return byType;
        if (_tag != null && _tag.TryGetValue(npcId, out var t))
        {
            var f = IconFile(t);
            if (f != "map005") return f;
        }
        return null;
    }

    /// <summary>Icône native du marqueur pour un PNJ (défaut map005
    /// conservé pour l'API historique).</summary>
    public static string IconOf(int npcId) => IconOrNull(npcId) ?? "map005";

    private static Dictionary<int, string>? _tag;
    private static Dictionary<int, int>? _svc;

    /// <summary>Catégorie EN du PNJ, « Other » si inconnue.</summary>
    public static string Of(int npcId)
    {
        Ensure();
        return _cat != null && _cat.TryGetValue(npcId, out var c)
            ? c : "Other";
    }

    private static void Ensure()
    {
        if (_cat != null) return;
        _cat = new Dictionary<int, string>();
        _tag = new Dictionary<int, string>();
        _svc = new Dictionary<int, int>();
        try
        {
            var path = AssetCatalog.ResolveRawAsset("data/db/c_npc.ini");
            if (path == null) return;
            var t = IniTable.ParseFile(path, EdenEncoding.Big5);
            foreach (var row in t.Rows)
            {
                if (row.Length < 21 || !int.TryParse(row[0], out var id))
                    continue;
                var tag = row[20].Trim();
                _tag[id] = tag;
                _svc[id] = ParseServiceType(row);
                _cat[id] = En.TryGetValue(tag, out var en) ? en : "Other";
            }
        }
        catch (Exception e)
        {
            Godot.GD.PrintErr($"[radar] c_npc catégories : {e.Message}");
        }
    }

    private static int ParseServiceType(string[] row)
    {
        var p = row[10].Split('#');
        if (p.Length > 1 && int.TryParse(p[1], out var t) && t > 0) return t;
        for (var col = 11; col <= 18 && col < row.Length; col++)
        {
            var seg = row[col].Split('#');
            if (seg.Length < 2 || !seg[1].StartsWith(":Service")) continue;
            var tok = seg[1].Split(' ');
            if (tok.Length > 2 && int.TryParse(tok[2], out var st) && st > 0)
                return st;
        }
        return 0;
    }
}
