using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// A5.3 : descriptions hover des stats de la fenêtre Profile. Clés
/// anglaises (littéraux) traduites via <see cref="UiTextEn.Translate"/>
/// (table <c>ui_text_en_fr.tsv</c>). Les textes sont mono-ligne
/// (Canon() écrase les retours à la ligne).
/// </summary>
public static class ProfileStatInfo
{
    private static readonly System.Collections.Generic.Dictionary<string, string> Desc = new()
    {
        ["str"] = "STR: +5 P-ATK, +1 Block per point.",
        ["agi"] = "AGI: +1 EVA, +0.8 Parry, +0.5 ATK SPD, +1 Cast SPD per point.",
        ["int"] = "INT: +5 M-ATK per point.",
        ["wis"] = "WIS: raises G-Healing and P-Healing (ratio not published).",
        ["lck"] = "LCK: +0.8 CRIT Rate, +1 M-CRIT Rate, +1 ACC per point.",
        ["acc"] = "ACC: hit chance against the target's EVA (cap 300%).",
        ["move"] = "Move SPD: movement speed (cap 200%).",
        ["patk"] = "P-ATK: physical attack power.",
        ["aspd"] = "ATK SPD: physical attack speed.",
        ["pheal"] = "P-Healing: healing power of physical skills.",
        ["crit"] = "CRIT Rate: chance of a critical physical hit.",
        ["critdmg"] = "CRIT DMG: critical damage multiplier (x1.5 base, cap 300%).",
        ["matk"] = "M-ATK: magical attack power.",
        ["cspd"] = "Cast SPD: spell casting speed.",
        ["gheal"] = "G-Healing: healing power of magical skills.",
        ["mcrit"] = "M-CRIT Rate: chance of a critical magical hit.",
        ["mcritdmg"] = "M-CRIT DMG: critical magic damage multiplier.",
        ["def"] = "DEF: base damage reduction (cap 70%), also reduces critical damage (cap 70%).",
        ["eva"] = "EVA: chance to dodge, against the attacker's ACC (cap 100%).",
        ["parry"] = "Parry: halves damage taken, two-handed weapons only (cap 70%).",
        ["block"] = "Block: negates damage, shield only (cap 50%).",
        ["res_slash"] = "Slash resistance: +1% damage reduction per point (cap 50%).",
        ["res_strike"] = "Strike resistance: +1% damage reduction per point (cap 50%).",
        ["res_pierce"] = "Pierce resistance: +1% damage reduction per point (cap 50%).",
        ["res_fire"] = "Fire resistance: +1% damage reduction per point (cap 70%).",
        ["res_lightning"] = "Lightning resistance: +1% damage reduction per point (cap 70%).",
        ["res_ice"] = "Ice resistance: +1% damage reduction per point (cap 70%).",
        ["res_nature"] = "Nature resistance: +1% damage reduction per point (cap 70%).",
        ["res_holy"] = "Light resistance: +1% damage reduction per point (cap 70%).",
        ["res_dark"] = "Dark resistance: +1% damage reduction per point (cap 70%).",
    };

    /// <summary>Clés des résistances dans l'ORDRE D'AFFICHAGE natif
    /// (Anti icons / Number1-9 : slash, strike, pierce, fire,
    /// lightning, ice, nature, holy, dark). C'est aussi l'ordre de
    /// <c>PlayerStats.Resists</c> — réordonné du wire à la réception.
    /// ⚠️ Ne pas confondre avec l'ordre élément (c_item/AttrResist :
    /// ice avant lightning — <c>StatSources</c>).</summary>
    private static readonly string[] ResistKeys =
    {
        "res_slash", "res_strike", "res_pierce", "res_fire",
        "res_lightning", "res_ice", "res_nature", "res_holy", "res_dark",
    };

    public static string ResistKey(int index) =>
        index >= 0 && index < ResistKeys.Length ? ResistKeys[index] : "";

    /// <summary>Position d'affichage (0-8) d'une clé res_* ; -1 si la
    /// clé n'est pas une résistance.</summary>
    public static int ResistIndex(string key) =>
        System.Array.IndexOf(ResistKeys, key);

    /// <summary>Nom natif de la résistance affichée en position i —
    /// t_textindex 1202-1210 suit l'ordre ÉLÉMENT (ice avant
    /// lightning) : l'indice display i vise l'élément
    /// {0,1,2,3,5,4,6,7,8}[i].</summary>
    public static string ResistTitle(int index)
    {
        if (index < 0 || index >= ResistElem.Length) return "";
        return TextResolver.TextIndex(1202 + ResistElem[index])
            ?? ResistKey(index);
    }

    // position d'affichage → index élément natif (0-8, ordre
    // c_item/textindex) : seuls les slots 4/5 (lightning/ice)
    // s'échangent.
    private static readonly int[] ResistElem = { 0, 1, 2, 3, 5, 4, 6, 7, 8 };

    /// <summary>Pose le tooltip traduit sur le label (et le rend
    /// survolable).</summary>
    public static void Hint(Control c, string key)
    {
        if (!Desc.TryGetValue(key, out var en)) return;
        c.TooltipText = UiTextEn.Translate(en);
        c.MouseFilter = Control.MouseFilterEnum.Pass;
    }

    /// <summary>Stats de base (clé → index NCID 492
    /// <c>ZoneBaseAttrRefresh.Stats</c> : Str, Int, Agi, Wis, Lck) — ce
    /// sont les 5 stats dont le hover affiche le split « X + Y ».</summary>
    public static readonly System.Collections.Generic.IReadOnlyDictionary<string, int>
        CoreIndex = new System.Collections.Generic.Dictionary<string, int>
    {
        ["str"] = 0, ["int"] = 1, ["agi"] = 2, ["wis"] = 3, ["lck"] = 4,
    };

    /// <summary>A5.3 : tooltip « total = naturel + bonus » (naturel = base
    /// 492, bonus = total 296 − base : gear, buffs, certs, talents)
    /// suivi de la description de la stat.</summary>
    public static void SplitHint(Control c, string key, int total,
        int? baseValue)
    {
        if (!Desc.TryGetValue(key, out var en)) return;
        var head = StatSplit.Split(total, baseValue) is null
            ? ""
            : StatSplit.Format(UiTextEn.Translate("{0} = {1} + {2}"),
                total, baseValue) + "\n" +
              UiTextEn.Translate("Natural (class, level, race) + bonus " +
                  "(gear, buffs, certs, talents)") + "\n";
        c.TooltipText = head + UiTextEn.Translate(en);
        c.MouseFilter = Control.MouseFilterEnum.Pass;
    }

    public static string DescOf(string key)
    {
        if (!Desc.TryGetValue(key.TrimEnd('%'), out var en)) return "";
        var i = en.IndexOf(':');
        return i >= 0 ? en[(i + 1)..].Trim() : en;
    }

    /// <summary>Clic sur une stat : mini-panneau de détail (valeur, naturel + bonus, formule, plafonds).</summary>
    public static void ShowDetail(Control anchor, string key, string valueText, int total, int? baseValue)
    {
        if (!Desc.TryGetValue(key.TrimEnd('%'), out var en)) return;
        var tree = anchor.GetTree();
        if (tree == null) return;
        foreach (var old in tree.Root.FindChildren("StatDetailPopup", "", true, false)) old.QueueFree();
        var pop = new PopupPanel { Name = "StatDetailPopup" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        pop.AddChild(box);
        Label L(string t, int size, Color? col = null, bool wrap = true)
        {
            var l = new Label { Text = t, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
                CustomMinimumSize = new Vector2(wrap ? 300 : 0, 0) };
            l.AddThemeFontSizeOverride("font_size", size);
            if (col != null) l.AddThemeColorOverride("font_color", col.Value);
            return l;
        }
        var title = UiTextEn.Translate(en).Split(':')[0];
        box.AddChild(L($"{title}  =  {valueText}", 14, Colors.White));
        if (baseValue.HasValue && StatSplit.Split(total, baseValue) is not null)
        {
            box.AddChild(L(StatSplit.Format(UiTextEn.Translate("{0} = {1} + {2}"), total, baseValue),
                12, Color.Color8(120, 220, 130)));
            box.AddChild(L(UiTextEn.Translate("Natural (class, level, race) + bonus (gear, buffs, certs, talents)"),
                11, Color.Color8(200, 200, 205)));
        }
        var formula = UiTextEn.Translate(en);
        var idx = formula.IndexOf(':');
        box.AddChild(L(idx >= 0 ? formula[(idx + 1)..].Trim() : formula, 12, Color.Color8(235, 235, 240)));
        tree.Root.AddChild(pop);
        pop.Popup(new Rect2I((Vector2I)(anchor.GetGlobalRect().Position + new Vector2(anchor.Size.X + 150, 24)), Vector2I.Zero));
    }
}
