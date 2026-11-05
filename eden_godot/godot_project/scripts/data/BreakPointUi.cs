namespace EdenClient.Data;

/// <summary>
/// Logique pure de l'affichage Break Point (« Barre de Rupture ») du
/// cadre de cible natif <c>TargetInfoWnd.layout</c> :
/// <list type="bullet">
/// <item>9 icônes de faiblesse <c>Weak0..Weak8</c> — slot i = attribut
/// i+1 (Slash, Strike, Pierce, Fire, Ice, Lightning, Nature, Light, Dark),
/// image <c>set:main image:Anti*</c> ; seules les faiblesses du mob (bit
/// du masque S_Monster) sont visibles. Natif : boucle <c>param_2 &lt; 9</c>
/// de <c>FUN_007db510</c> (TargetWnd1.cpp) ;</item>
/// <item>barre violette <c>BPBackground/Bar</c> = patience courante /
/// patience max (attr 0x13 du 280, max = champ +0x4c du 298) ;</item>
/// <item>étoiles <c>MonsterStartBorder/star1..5</c> = type de monstre
/// (ref-shots/targetframe (Elite mob).png : élite type 2 = 2 étoiles).</item>
/// </list>
/// Le masque, la patience et le type viennent du <c>c_monster.ini</c>
/// client (même fichier que le S_Monster serveur).
/// </summary>
public static class BreakPointUi
{
    /// <summary>Noms EN des 9 attributs (clés du TSV de traduction).</summary>
    public static readonly string[] ElementNames =
    {
        "Slash", "Strike", "Pierce", "Fire", "Ice",
        "Lightning", "Nature", "Light", "Dark",
    };

    /// <summary>Images de l'imageset <c>main</c> par attribut (le natif
    /// écrit « AntiLightining » avec la faute).</summary>
    public static readonly string[] IconImages =
    {
        "AntiChop", "AntiStroke", "AntiStuck", "AntiFire", "AntiIce",
        "AntiLightining", "AntiNature", "AntiHoly", "AntiDark",
    };

    /// <summary>Patience max cap natif (<c>MaxPatience</c> @0x083f3d30).</summary>
    public const int PatienceCap = 9_999_999;

    /// <summary>Patience max d'une ligne c_monster/S_Monster (idx 12) :
    /// décimal tronqué, 0 + masque 0 → 100, cap 9 999 999.</summary>
    public static int ParsePatience(string[] row)
    {
        var mask = IniTable.GetInt(row, 10);
        long pat = 0;
        if (row.Length > 12 && double.TryParse(row[12],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
            && d > 0)
            pat = (long)d;
        if (mask == 0 && pat == 0) pat = 100;
        return (int)Math.Min(pat, PatienceCap);
    }

    /// <summary>Le slot d'icône <paramref name="slot"/> (0..8) est-il une
    /// faiblesse du mob ?</summary>
    public static bool IsWeak(int mask, int slot) =>
        slot is >= 0 and < 9 && (mask & (1 << slot)) != 0;

    /// <summary>Slots (0..8) à afficher pour un masque.</summary>
    public static List<int> WeakSlots(int mask)
    {
        var l = new List<int>();
        for (var i = 0; i < 9; i++)
            if (IsWeak(mask, i)) l.Add(i);
        return l;
    }

    /// <summary>Étoiles de rang = type S_Monster plafonné à 5
    /// (<c>star1..star5</c>) : ref-shots TailleMob (mob commun, 1
    /// étoile) et « targetframe (Elite mob) » (2 étoiles). 0 = type
    /// inconnu.</summary>
    public static int Stars(int monsterType) =>
        monsterType >= 1 ? Math.Min(5, monsterType) : 0;

    /// <summary>Médaillon du cadre de cible (image de l'imageset
    /// <c>main</c>) — switch natif @0x7dd57e sur la famille du mob :
    /// 1/8-12 Human, 2 Monster, 3-4 UnDead, 5-6 King ; autre = cadre
    /// générique <c>faceFrame</c>. Kind 8 = NPC.</summary>
    public static string TargetBadge(byte kind, int family)
    {
        if (kind == 8) return "TargetNPC";
        if (kind == 0) return "TargetHuman";
        return family switch
        {
            1 or (>= 8 and <= 12) => "TargetHuman",
            2 => "TargetMonster",
            3 or 4 => "TargetUnDead",
            5 or 6 => "TargetKing",
            _ => "faceFrame",
        };
    }

    /// <summary>Libellé du rang (infobulle des étoiles) — HYPOTHESIS pour
    /// les noms 3-5 (seul 1 normal / 2 élite sont prouvés par les refs).</summary>
    public static string RankName(int monsterType) => monsterType switch
    {
        1 => "Normal monster",
        2 => "Elite monster",
        3 => "Boss",
        4 => "World boss",
        >= 5 => "Dungeon boss",
        _ => "",
    };

    /// <summary>La barre BP s'affiche pour les mobs cassables seulement
    /// (« uniquement mobs rares et boss de donjon »).</summary>
    public static bool ShowBar(int weakMask, int maxPatience) =>
        weakMask != 0 && maxPatience > 0;

    /// <summary>Fraction de remplissage 0..1 (patience négative = 0).</summary>
    public static float Fraction(int cur, int max) =>
        max <= 0 ? 0f : Math.Clamp(cur / (float)max, 0f, 1f);

    /// <summary>Texte « cur/max » de la barre (patience ≥ 0 affichée).</summary>
    public static string BarText(int cur, int max) =>
        $"{Math.Max(0, cur)}/{Math.Max(0, max)}";

    /// <summary>Infobulle d'une icône de faiblesse : l'élément seul
    /// (« Feu »), sans « Faiblesse : » (définitions.md). Dérivé de la
    /// traduction existante « Weak to X » pour garder les termes FR du
    /// catalogue (Entaille, Frappe, Percée, Feu, Glace, Foudre, Nature,
    /// Lumière, Ténèbres).</summary>
    public static string WeakTip(int slot)
    {
        if (slot is < 0 or >= 9) return "";
        var t = UiTextEn.Translate($"Weak to {ElementNames[slot]}");
        var i = t.IndexOf(':');
        return i >= 0 ? t[(i + 1)..].Trim() : UiTextEn.Translate(ElementNames[slot]);
    }

    /// <summary>Nom d'élément FR seul (même source que <see cref="WeakTip"/>).</summary>
    public static string ElementLabel(int slot) => WeakTip(slot);

    /// <summary>Découpe un label registre de mob en (titre, niveau, nom) :
    /// « &lt;Titre&gt;\nLvN Nom », « LvN Nom » ou « Nom ».</summary>
    public static (string Title, int Level, string Name) SplitLabel(string label)
    {
        var lines = label.Split('\n', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return ("", 0, label.Trim());
        var title = "";
        var last = lines[^1];
        if (lines.Length > 1) title = string.Join(" ", lines[..^1]);
        var level = 0;
        if (last.StartsWith("Lv") && last.Length > 2)
        {
            var sp = last.IndexOf(' ');
            var num = sp > 2 ? last[2..sp] : last[2..];
            if (int.TryParse(num, out var lv))
            {
                level = lv;
                last = sp > 2 ? last[(sp + 1)..].Trim() : "";
            }
        }
        else if (title.StartsWith("Lv") && lines.Length >= 2)
        {
            // ancien format collé « LvN <Titre>\nNom »
            var sp = title.IndexOf(' ');
            if (sp > 2 && int.TryParse(title[2..sp], out var lv2))
            {
                level = lv2;
                title = title[(sp + 1)..].Trim();
            }
        }
        return (title, level, last);
    }

    /// <summary>Label monde d'un mob : titre sur sa propre ligne, niveau
    /// accolé au nom (« &lt;Legion Elite&gt;\nLv81 Archangel of Sin ») —
    /// plus de « Lv » collé au titre. <paramref name="rawName"/> = nom
    /// t_monster (« &lt;Titre&gt;\nNom » ou « Nom »).</summary>
    public static string FormatMobLabel(int level, string rawName)
    {
        var lines = rawName.Split('\n', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return level > 0 ? $"Lv{level}" : "";
        var name = lines[^1];
        var title = lines.Length > 1 ? string.Join(" ", lines[..^1]) : "";
        var body = level > 0 ? $"Lv{level} {name}" : name;
        return title.Length > 0 ? $"{title}\n{body}" : body;
    }

    /// <summary>Échelle visuelle d'un mob (HYPOTHESIS — aucune colonne
    /// S_Monster de taille prouvée, <c>CMonsterTemplate::GetDBSize</c>
    /// n'a pas de source lisible) : les rares/boss sont plus gros
    /// (wiki : « plus gros »), les mobs communs varient légèrement par
    /// handle (ref-shots/TailleMob.png : tailles variées). Déterministe
    /// pour un handle donné.</summary>
    public static float MobScale(int monsterType, uint handle)
    {
        var h = (handle * 2654435761u) >> 8;          // hash multiplicatif
        var r = (h & 0xFFFF) / 65535f;                // 0..1
        return monsterType switch
        {
            >= 5 => 1.35f,
            4 => 1.3f,
            3 => 1.25f + 0.05f * r,                   // boss
            2 => 1.12f + 0.06f * r,                   // élite / rare
            _ => 0.92f + 0.16f * r,                   // commun 0.92–1.08
        };
    }
}
