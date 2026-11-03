using Godot;
using EdenClient.Combat;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.Hud;

/// <summary>
/// Tooltip de sort du panneau Classes (ref ClassePanel.png /
/// « skill hover info » / ClassePanel-ClassNews2.png) : nom, niveau,
/// « Cost: N MP » (base col32 × MpCoef du palier), « Cast: X s »,
/// « Cooldown: N s », description (T_Enchant de l'enchant lié col39,
/// placeholders <c>#B$n$v#</c> résolus au niveau courant via
/// <see cref="SpellRefTable"/>), « Next level: … » (même description
/// au palier suivant) et coût de montée en rouge (« requires N Class
/// Points! » / or).
/// </summary>
public static partial class ItemTooltip
{
    /// <summary>Construit le tooltip d'un sort. <paramref name="pay"/> =
    /// monnaie de montée (9 = CP, 1 = or), <paramref name="nextCost"/> =
    /// coût du niveau suivant (−1 = inconnu/maxi).</summary>
    public static Control BuildSkillTip(int spellId, int level,
        int pay, bool learned, int nextCost, bool preview = false)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Bg,
            BorderColor = Edge,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        // Ref : nom du sort en bleu clair ; fiche « aperçu » (onglet
        // Class News) sans ligne de niveau ni « Next level ».
        box.AddChild(Label(SpellCatalog.Name(spellId), SkillName));
        if (!preview)
            box.AddChild(Label(learned
                ? UiTextEn.Translate("Lv.%s1").Replace("%s1", level.ToString())
                : UiTextEn.Translate("Not learned"), Dim));

        var desc = SkillDescription(spellId, Math.Max(1, level));
        if (desc == null)
        {
            // Pas de gabarit : lignes minimales coût/cast/cooldown.
            var mp = SpellCatalog.MpCostBase(spellId)
                * SpellRefTable.Coef(Math.Max(1, level), 1) / 100;
            if (mp > 0)
                box.AddChild(Row(UiTextEn.Translate("Cost"),
                    UiTextEn.Translate("%s1 MP")
                        .Replace("%s1", mp.ToString()), Dim, White));
            var cast = SpellCatalog.CastDs(spellId);
            if (cast > 0)
                box.AddChild(Row(UiTextEn.Translate("Cast"),
                    $"{cast / 10f:0.#}s", Dim, White));
            var cd = SpellCatalog.CooldownDs(spellId);
            if (cd > 0)
                box.AddChild(Row(UiTextEn.Translate("Cooldown"),
                    $"{cd / 10f:0.#}s", Dim, White));
        }
        if (desc != null)
        {
            box.AddChild(Spacer(3));
            foreach (var (t, c) in desc)
                box.AddChild(t.Length == 0 ? Spacer(6)
                    : Label(t, c, wrap: true, width: TextWidth));
        }

        // Exigence de niveau de classe — affichée seulement tant
        // qu'elle n'est pas remplie (ref native : ligne rouge pour
        // les sorts verrouillés).
        var req = SpellCatalog.RequiredLevel(spellId);
        if (req > 0 && level < req)
            box.AddChild(Label(UiTextEn.Translate(
                "Must reach class level %d")
                    .Replace("%d", req.ToString()), Warn));

        if (learned && !preview)
        {
            var next = SkillDescription(spellId, level + 1);
            if (next != null && level < SpellRefTable.MaxLevel)
            {
                box.AddChild(Spacer(3));
                // Ref ClassePanel : l'en-tête « Next level » est rouge.
                box.AddChild(Label(UiTextEn.Translate("Next level:"), Warn));
                foreach (var (t, c) in next)
                    box.AddChild(t.Length == 0 ? Spacer(6)
                        : Label(t, c, wrap: true, width: TextWidth));
            }
            if (nextCost >= 0)
            {
                box.AddChild(Spacer(3));
                var gold = nextCost / 100;
                var cost = pay == 9
                    ? UiTextEn.Translate("%s1 Class Points")
                        .Replace("%s1", nextCost.ToString())
                    : gold > 0
                        ? UiTextEn.Translate("%s1 Gold %s2 Silver")
                            .Replace("%s1", $"{gold:#,0}")
                            .Replace("%s2", (nextCost % 100).ToString())
                        : UiTextEn.Translate("%s1 Silver")
                            .Replace("%s1", (nextCost % 100).ToString());
                box.AddChild(Label(UiTextEn.Translate(pay == 9
                        ? "Learning the skill's next level requires %s1!"
                        : "Learning the skill's next level costs %s1")
                    .Replace("%s1", cost), Warn));
            }
        }
        if (ClientConfig.ShowIdsEnabled)
            box.AddChild(Label($"#{spellId}", Dim));
        return panel;
    }

    private static readonly Color Flavor = new(0.96f, 0.9f, 0.5f);
    private static readonly Color SkillName = new(0.62f, 0.82f, 1f);

    /// <summary>Tooltip texte simple (titre blanc optionnel + corps
    /// multi-lignes) — boutons « ? », talents innés.</summary>
    public static Control BuildTextTip(string? title, string? body)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Bg,
            BorderColor = Edge,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        if (!string.IsNullOrEmpty(title))
            box.AddChild(Label(title, White));
        if (!string.IsNullOrEmpty(body))
            foreach (var l in body.Replace("\r", "").Split('\n'))
            {
                var t = TextResolver.Clean(l).Trim();
                if (t.Length > 0)
                    box.AddChild(Label(t, Dim, wrap: true, width: TextWidth));
            }
        return panel;
    }

    /// <summary>Tooltip de talent (ref ClassTalent&Certificat) : nom,
    /// niveau max, description d'effet, prérequis rouges quand non
    /// remplis, ligne de coût rouge.</summary>
    public static Control BuildTalentTip(TalentTable.Slot slot,
        int points, int pending, int prePoints, int classLevel)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Bg,
            BorderColor = Edge,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        box.AddChild(Label(EnchantTable.NameOf(slot.SpellId), White));
        box.AddChild(Label(UiTextEn.Translate("Max Level %s1")
            .Replace("%s1", slot.MaxPoints.ToString()), SkillName));
        if (EnchantTable.DescriptionOf(slot.SpellId) is { } td)
            foreach (var l in td.Replace("\r", "").Split('\n'))
            {
                var t = TextResolver.Clean(l).Trim();
                // Le texte enchant répète souvent « Max Level N » —
                // déjà rendu par la ligne traduite au-dessus.
                if (t.Length == 0 || t.StartsWith("Max Level")) continue;
                // Ref : effet chiffré en vert, saveur en jaune.
                box.AddChild(Label(t, t.Any(char.IsDigit) ? Good : Flavor,
                    wrap: true, width: TextWidth));
            }
        if (slot.PreSpell != 0 && prePoints < slot.PrePoints)
            box.AddChild(Label(UiTextEn.Translate("Requires %s1 Lv %s2")
                .Replace("%s1", EnchantTable.NameOf(slot.PreSpell))
                .Replace("%s2", slot.PrePoints.ToString()), Warn));
        if (slot.RequiredLevel > 0 && classLevel < slot.RequiredLevel)
            box.AddChild(Label(UiTextEn.Translate("Class level %s1")
                .Replace("%s1", slot.RequiredLevel.ToString()), Warn));
        if (points + pending < slot.MaxPoints)
            box.AddChild(Label(UiTextEn.Translate(
                "Learning the next Knowledge level will cost you 1 Knowledge Points!"),
                Warn, wrap: true, width: TextWidth));
        return panel;
    }

    /// <summary>Tooltip de sort au survol de <paramref name="c"/>.</summary>
    public static void BindSkill(Control c, int spellId, int level,
        int pay, bool learned, int nextCost) =>
        BindCustom(c, () => BuildSkillTip(
            spellId, level, pay, learned, nextCost));

    /// <summary>Tooltip d'une classe dans la liste du panneau Classes
    /// (ref HoverToolTipClasse / shot_0048_000033) : nom, description,
    /// « Class Requirement: … » en rouge quand la classe est
    /// verrouillée.</summary>
    public static Control BuildClassTip(int classId, bool unlocked)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Bg,
            BorderColor = Edge,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        var info = TextResolver.ClassInfo(classId);
        box.AddChild(Label(TextResolver.ClassName(classId)
            ?? $"Class {classId}", White));
        // « Empty »/« Vide » = placeholder de colonne vide dans T_Class,
        // pas un texte à afficher.
        if (info is { ShortDescription.Length: > 0 } &&
            info.ShortDescription is not ("Empty" or "Vide"))
            box.AddChild(Label(info.ShortDescription, Dim,
                wrap: true, width: TextWidth));
        if (!unlocked)
        {
            var req = info is { RequirementText.Length: > 0 }
                ? info.RequirementText
                : UiTextEn.Translate("Class Requirement:\n" +
                    "Reach the required class level to unlock this class.");
            box.AddChild(Spacer(3));
            foreach (var l0 in req.Replace("\r", "").Split('\n'))
            {
                var l = TextResolver.Clean(l0.Trim());
                if (l.Length == 0) continue;
                box.AddChild(Label(l, Warn, wrap: true, width: TextWidth));
            }
        }
        if (ClientConfig.ShowIdsEnabled)
            box.AddChild(Label($"#{classId}", Dim));
        return panel;
    }

    /// <summary>Lignes (texte, couleur) de la description du sort au
    /// palier <paramref name="level"/> — gabarit brut de T_Spell col2
    /// (multi-lignes : « Cost: #27# MP », « Cooldown: #24# seconds »,
    /// effet <c>$15$…</c>, « Required weapon: #18# »), placeholders
    /// résolus depuis C_Spell/<see cref="SpellRefTable"/> ; repli sur
    /// la description de l'enchant lié (T_Enchant).</summary>
    private static List<(string Text, Color Tint)>? SkillDescription(
        int spellId, int level)
    {
        var res = ParseDescription(
            TextResolver.SpellDescriptionRaw(spellId), spellId, level);
        var encId = SpellCatalog.ResultEnchantId(spellId);
        if ((res == null || res.Count == 0) && encId > 0)
            // Gabarit T_Spell vide (« $13$#13# ») → texte de
            // l'enchant lié (t_enchant — la vraie description
            // d'effet, souvent localisée FR).
            res = ParseDescription(
                EnchantTable.DescriptionOf(encId), spellId, level);
        return res != null && res.Count > 0 ? res : null;
    }

    /// <summary>Découpe une description multi-lignes en (texte,
    /// couleur) : codes <c>$N$</c> de tête de ligne → couleur,
    /// gabarits résolus via <see cref="ResolveSpellTemplate"/>,
    /// lignes vides/espacements conservés.</summary>
    private static List<(string Text, Color Tint)>? ParseDescription(
        string? raw, int spellId, int level)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var res = new List<(string, Color)>();
        var cur = White;
        foreach (var l0 in raw.Replace("\r", "").Split('\n'))
        {
            var l = l0.Trim();
            if (l.Length == 0) { res.Add(("", cur)); continue; }
            if (l[0] == '$')
            {
                var j = 1;
                while (j < l.Length && char.IsDigit(l[j])) j++;
                if (j > 1 && j < l.Length && l[j] == '$' &&
                    int.TryParse(l.AsSpan(1, j - 1), out var code))
                {
                    cur = CodeColor(code);
                    l = l[(j + 1)..];
                }
            }
            l = ResolveSpellTemplate(spellId, level, l);
            l = TextResolver.Clean(l).Trim();
            // Ligne devenue vide ou réduite à un libellé sans valeur
            // (arme #18# indécidée, marqueur #13#) → masquée.
            if (l.Length == 0 || l.EndsWith(':') || l == "#13#")
                continue;
            res.Add((l, cur));
        }
        while (res.Count > 0 && res[^1].Item1.Length == 0)
            res.RemoveAt(res.Count - 1);
        return res.Count > 0 ? res : null;
    }

    /// <summary>Résout les gabarits <c>#N#</c>/<c>#B$n$v$#</c> d'une
    /// ligne de description au palier <paramref name="level"/> :
    /// #23 cast s, #24 cooldown s, #25 portée, #26 rayon, #27 coût MP,
    /// #29 P-ATK, #30 soin, #B mise à l'échelle enchant. #13 (marqueur
    /// d'en-tête) et #18 (liste d'armes — encodage non décodé) sont
    /// vidés ; les autres codes inconnus sont conservés.</summary>
    private static string ResolveSpellTemplate(int spellId, int level,
        string line)
    {
        var mp = SpellCatalog.MpCostBase(spellId)
            * SpellRefTable.Coef(level, 1) / 100;
        // Canal du palier : physique col2, magique col3 (serveur
        // SpellRefCoef) — la ligne « ATQ MAG +#29# » prend le canal magique.
        var magic = line.Contains("MAG", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("#29#");
        var patk = SpellCatalog.DamagePower(spellId)
            * SpellRefTable.Coef(level, magic ? 3 : 2) / 100;
        var heal = SpellCatalog.HealPower(spellId)
            * SpellRefTable.Coef(level, 4) / 100;
        line = line
            .Replace("#13#", "")
            // shortcut: #18# = liste d'armes requises, encodage c_spell
            // col13 non décodé — vidé (ligne masquée si seule), à
            // reprendre quand la table sera identifiée.
            .Replace("#18#", "")
            .Replace("#23#", $"{SpellCatalog.CastDs(spellId) / 10f:0.#}")
            .Replace("#24#", $"{SpellCatalog.CooldownDs(spellId) / 10f:0.#}")
            .Replace("#25#", $"{SpellCatalog.RangeUnits(spellId)}")
            .Replace("#26#", $"{SpellCatalog.RadiusUnits(spellId) * 0.5f:0.#}")
            .Replace("#27#", $"{mp}")
            .Replace("#29#", $"{patk}")
            .Replace("#30#", $"{heal}");
        return SpellRefTable.ResolveScaledValues(line, level);
    }

    /// <summary>Tooltip d'un certificat (page License Knowledge) :
    /// nom T_Item, bonus de l'enchant lié (c_item col23 → T_Enchant,
    /// en vert) et texte de saveur T_Item.</summary>
    public static Control BuildCertTip(int itemId)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Bg,
            BorderColor = Edge,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        var d = InventoryWindow.LookupDef(itemId);
        box.AddChild(Label(
            TextResolver.ItemName(itemId) ?? $"#{itemId}", White));
        if (d?.UseEnchantId > 0 &&
            EnchantTable.DescriptionOf(d.UseEnchantId) is { } effect)
            box.AddChild(Label(effect, Good, wrap: true, width: 220));
        foreach (var (text, tint) in DescriptionLines(itemId, d))
            box.AddChild(Label(text, tint, wrap: true, width: 220));
        if (ClientConfig.ShowIdsEnabled)
            box.AddChild(Label($"#{itemId}", Dim));
        return panel;
    }
}
