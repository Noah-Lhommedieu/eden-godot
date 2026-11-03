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

    /// <summary>Icône native du marqueur pour un PNJ (tag catégorie
    /// brut, pas la traduction EN — couvre les catégories sans entrée
    /// dans <c>En</c> comme 魔方能量活動).</summary>
    public static string IconOf(int npcId)
    {
        Ensure();
        return _tag != null && _tag.TryGetValue(npcId, out var t)
            ? IconFile(t) : "map005";
    }

    private static Dictionary<int, string>? _tag;

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
                _cat[id] = En.TryGetValue(tag, out var en) ? en : "Other";
            }
        }
        catch { /* catégories facultatives */ }
    }
}
