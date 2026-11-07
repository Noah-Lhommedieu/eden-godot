using Godot;
using EdenClient.Combat;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Tooltip d'item unique du client (inventaire, équipement, coffres,
/// banque, échange, hotbar, boutiques, courrier…). Gabarit natif
/// (ref-shots <c>item hover *</c>, catalogue §5) : en-tête icône + nom
/// coloré par qualité, puis groupes séparés par un filet —
/// description (codes <c>$N$</c>, <c>&amp;id&amp;</c> = item produit
/// incrusté), bloc type/flags/durabilité/stats de base, bloc stats +
/// proc + cooldown, bloc niveau requis + fortification. Les règles
/// pures (flags, libellés, formats) vivent dans
/// <see cref="ItemTipFormat"/> (testées par NetTests).
/// Deux entrées : <see cref="Build"/> (Control prêt à poser) et
/// <see cref="Bind"/> (survol d'un Control quelconque, placement
/// clampé à l'écran, un seul tooltip à la fois).
/// </summary>
public static partial class ItemTooltip
{
    private const int TextWidth = 250;
    private const int FontSize = 12;

    private static readonly Color Bg = new(0.04f, 0.04f, 0.04f, 0.9f);
    private static readonly Color Edge = new(0.5f, 0.5f, 0.5f, 0.9f);
    private static readonly Color White = new(1f, 1f, 1f);
    private static readonly Color Dim = new(0.78f, 0.78f, 0.78f);
    private static readonly Color Warn = new(1f, 0.42f, 0.42f);
    private static readonly Color Good = new(0.55f, 0.9f, 0.55f);
    private static readonly Color Gold = new(1f, 0.9f, 0.45f);
    private static readonly Color LightBlue = new(0.55f, 0.78f, 1f);
    private static readonly Color Cyan = new(0.35f, 0.92f, 0.95f);

    /// <summary>Couleur du nom selon la qualité (col 19) — palette
    /// native « Grey, Green, Orange, Blue, Gold, Purple »
    /// (textindex 1152-1157), ancrée par les ref-shots : 3 = orange,
    /// 5 = doré (cf. <see cref="ItemTipFormat.QualityTier"/>).</summary>
    public static Color QualityColor(int q) => ItemTipFormat.QualityTier(q) switch
    {
        0 => White,
        1 => new Color(0.72f, 0.72f, 0.72f),
        2 => new Color(0.45f, 0.92f, 0.45f),
        3 => new Color(1f, 0.62f, 0.2f),
        4 => new Color(0.35f, 0.68f, 1f),
        5 => new Color(1f, 0.9f, 0.2f),
        _ => new Color(0.78f, 0.52f, 1f),
    };

    /// <summary>Couleur d'une ligne de description à partir du code
    /// natif <c>$N$</c> : 7 = flavour jaune (ref Light Beam Gun),
    /// 12 = consigne rouge, 13 = liste de matériaux bleu clair (ref
    /// Blueprint), 15 = bonus vert (T_Enchant), 35 = titre doré,
    /// 5 = titre bleu (notes Sky Tower).</summary>
    private static Color CodeColor(int code) => code switch
    {
        5 => LightBlue,
        7 => Flavor,
        12 => Warn,
        13 => LightBlue,
        15 => Good,
        35 => Gold,
        _ => White,
    };

    /// <summary>Libellé de catégorie (table native kind → texte,
    /// <see cref="ItemTipFormat.KindLabel"/>) ; repli générique.</summary>
    public static string TypeLabel(ItemTable.ItemDef? d)
    {
        if (d == null) return UiTextEn.Translate("Item");
        var k = ItemTipFormat.KindLabel(d.Kind);
        if (k.Length > 0) return UiTextEn.Translate(k);
        if (d.EquipLoc > 0) return UiTextEn.Translate("Equipment");
        return UiTextEn.Translate("Item");
    }

    /// <summary>Lignes (texte, couleur) de la description brute
    /// <c>$N$…</c> ; une ligne sans code garde la couleur de la
    /// précédente jusqu'à la prochaine ligne vide (continuation d'un
    /// paragraphe), qui remet le blanc — ref Blueprint : « Finished
    /// Item: » blanc après une liste $13$.</summary>
    public static List<(string Text, Color Tint)> DescriptionLines(int id,
        ItemTable.ItemDef? def)
    {
        var res = new List<(string, Color)>();
        var raw = TextResolver.ItemDescriptionRaw(id)
            ?? def?.Description ?? "";
        var cur = White;
        foreach (var l0 in raw.Replace("\r", "").Split('\n'))
        {
            var l = l0.Trim();
            if (l.Length == 0) { res.Add(("", cur)); cur = White; continue; }
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
            res.Add((TextResolver.Clean(l), cur));
        }
        while (res.Count > 0 && res[^1].Item1.Length == 0)
            res.RemoveAt(res.Count - 1);
        return res;
    }

    // ------------------------- construction ------------------------

    /// <summary>Tooltip pour un item de définition connue ou non.</summary>
    public static Control Build(InventorySlot item, ItemTable.ItemDef? def)
    {
        def ??= InventoryWindow.LookupDef(item.ItemId);
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

        var name = TextResolver.ItemName(item.ItemId)
            ?? (def is { Name.Length: > 0 } ? def.Name
                : $"item #{item.ItemId}");
        // Ref « Heritage Spear Lv60+12 » : le +N est collé au nom.
        if (item.Strengthen > 0) name += $"+{item.Strengthen}";
        box.AddChild(Header(name, def, 36, TextWidth - 44));
        box.AddChild(Spacer(2));

        if (def is { Kind: 60 } && PartnerTable.Get(item.ItemId) is { } pet)
            AppendPetBody(box, item, def, pet);
        else
            AppendBody(box, item, def, 0);

        if (ClientConfig.ShowIdsEnabled)
            box.AddChild(Label($"#{item.ItemId}", Dim));
        return panel;
    }

    private static Control Header(string name, ItemTable.ItemDef? def,
        int icon, int nameWidth)
    {
        var head = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        head.AddThemeConstantOverride("separation", 8);
        var frame = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(icon, icon),
        };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.2f, 0.3f, 0.9f),
            BorderColor = new Color(0.25f, 0.6f, 1f),
            BorderWidthBottom = 2, BorderWidthTop = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
        });
        Texture2D? tex = def is { Code.Length: > 0 }
            ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png") : null;
        frame.AddChild(new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        head.AddChild(frame);
        var nameLbl = Label(name, QualityColor(def?.Quality ?? 0),
            wrap: true, width: nameWidth);
        nameLbl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(nameLbl);
        return head;
    }

    // ------------------------- composition -------------------------

    private static VBoxContainer NewGroup()
    {
        var g = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        g.AddThemeConstantOverride("separation", 2);
        return g;
    }

    private static Control Rule() => new ColorRect
    {
        Color = new Color(0.6f, 0.6f, 0.6f, 0.7f),
        CustomMinimumSize = new Vector2(TextWidth, 1),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Ajoute les groupes non vides ; un filet précède chaque
    /// groupe sauf la description quand elle ouvre le tooltip (ref :
    /// filet sous l'en-tête seulement si pas de description).</summary>
    private static void AddGroups(VBoxContainer box, bool descFirst,
        params VBoxContainer[] groups)
    {
        var first = true;
        foreach (var g in groups)
        {
            if (g.GetChildCount() == 0) { g.QueueFree(); continue; }
            if (!(first && descFirst))
            {
                box.AddChild(Spacer(2));
                box.AddChild(Rule());
                box.AddChild(Spacer(2));
            }
            box.AddChild(g);
            first = false;
            descFirst = false;
        }
    }

    /// <summary>Corps commun : description → bloc type/flags →
    /// stats → niveau requis/fortification. <paramref name="depth"/>
    /// borne l'incrustation d'items produits (blueprints).</summary>
    private static void AppendBody(VBoxContainer box, InventorySlot it,
        ItemTable.ItemDef? d, int depth)
    {
        var desc = NewGroup();
        var a = NewGroup();
        var b = NewGroup();
        var c = NewGroup();
        FillDescription(desc, it, d, depth);
        FillBlockA(a, it, d);
        FillStats(b, it, d);
        FillRequirements(c, it, d);
        AddGroups(box, desc.GetChildCount() > 0, desc, a, b, c);
    }

    private static void FillDescription(VBoxContainer g, InventorySlot it,
        ItemTable.ItemDef? d, int depth)
    {
        var lines = DescriptionLines(it.ItemId, d);
        foreach (var (t, c) in lines)
        {
            if (t.Length == 0) { g.AddChild(Spacer(8)); continue; }
            if (ItemTipFormat.TryItemRef(t, out var pid))
            {
                if (depth < 1) AppendEmbedded(g, pid, depth + 1);
                continue;
            }
            g.AddChild(Label(t, c, wrap: true, width: TextWidth));
        }
    }

    /// <summary>Item produit d'un blueprint/formule (« Finished Item:
    /// &amp;id&amp; ») : icône + nom puis corps complet (ref Blueprint
    /// et « Hover Item Plans »).</summary>
    private static void AppendEmbedded(VBoxContainer g, int pid, int depth)
    {
        var pdef = InventoryWindow.LookupDef(pid);
        var pname = TextResolver.ItemName(pid)
            ?? (pdef is { Name.Length: > 0 } ? pdef.Name : $"item #{pid}");
        g.AddChild(Header(pname, pdef, 28, TextWidth - 36));
        var sub = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        sub.AddThemeConstantOverride("separation", 2);
        AppendBody(sub, new InventorySlot(0, 0, pid, 0, 0, 0, 1), pdef,
            depth);
        g.AddChild(sub);
    }

    /// <summary>Bloc type + emplacement, vitesse d'attaque, flags,
    /// durabilité, stats de base, sockets/étoiles.</summary>
    private static void FillBlockA(VBoxContainer g, InventorySlot it,
        ItemTable.ItemDef? d)
    {
        if (d == null) return;
        var kl = ItemTipFormat.KindLabel(d.Kind);
        var loc = ItemTipFormat.LocLabel(d.EquipLoc, d.Kind);
        if (kl.Length > 0 || loc.Length > 0)
            g.AddChild(Pair(kl.Length > 0 ? UiTextEn.Translate(kl) : "",
                loc.Length > 0 ? UiTextEn.Translate(loc) : "", 14,
                White, White));
        var weapon = ItemTipFormat.IsWeaponKind(d.Kind);
        if (weapon && d.AttackSpeed > 0)
            g.AddChild(Pair(UiTextEn.Translate("ATK SPD"),
                ItemTipFormat.AtkSpeed(d.AttackSpeed) + " "
                + UiTextEn.Translate("Seconds"), 8, White, White));
        var flags = ItemTipFormat.FlagKeys(d.Flags, it.Flags);
        if (flags.Count > 0)
            g.AddChild(Label(string.Join(" ",
                flags.Select(UiTextEn.Translate)), White, wrap: true,
                width: TextWidth));
        var dura = ItemTipFormat.DurabilityLine(d.MaxDurability, d.Kind,
            d.EquipLoc, it.Durability, it.MaxDurability);
        if (dura is { } du)
            g.AddChild(Pair(UiTextEn.Translate("Durability") + ":",
                $"{du.Cur} / {du.Max}", 8, White, du.Cur <= 0 ? Warn : White));
        if (ItemTipFormat.IsGearKind(d.Kind))
        {
            // Ref « Basic P-ATK 24571 (120%) » : le % est la qualité
            // d'instance (clone+0x2c), absente de InventorySlot → omis.
            if (weapon && d.PhysicalAttack > 0)
                g.AddChild(Pair(UiTextEn.Translate("Basic P-ATK"),
                    d.PhysicalAttack.ToString(), 6, White, White));
            if (weapon && d.MagicAttack > 0)
                g.AddChild(Pair(UiTextEn.Translate("Basic M-ATK"),
                    d.MagicAttack.ToString(), 6, White, White));
            if (!weapon && d.Defence > 0)
                g.AddChild(Pair(UiTextEn.Translate("Basic DEF"),
                    d.Defence.ToString(), 6, White, White));
        }
        if (d.MaxSocket > 0)
        {
            var row = new HBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(TextWidth, 0),
            };
            row.AddThemeConstantOverride("separation", 2);
            var l = Label(UiTextEn.Translate("Sockets"), Dim);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(l);
            var sheet = AssetCatalog.GetTexture(
                "uidata/imagesets/ToolTips.png");
            for (var i = 0; i < d.MaxSocket && sheet != null; i++)
            {
                // Châsse remplie (Meta.Embedded) → icône gemme pleine,
                // vide → EmbedNone — les ids 2/18 de la grille 12×12.
                var filled = i < it.Meta.Embedded.Length &&
                    it.Meta.Embedded[i] > 0;
                row.AddChild(new TextureRect
                {
                    Texture = new AtlasTexture
                    {
                        Atlas = sheet,
                        Region = new Rect2(filled ? 18 : 2, 50, 12, 12),
                        FilterClip = true,
                    },
                    CustomMinimumSize = new Vector2(12, 12),
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            g.AddChild(row);
        }
        if (it.Strengthen > 0)
            g.AddChild(IconRow(UiTextEn.Translate("Enhance"), "StarJewelry",
                Math.Min(it.Strengthen, 15)));
    }

    /// <summary>Bloc stats (ordre de la ref : P-ATK, M-ATK, DEF, HP, MP,
    /// STR…LCK, ACC, EVA, CRIT, M-CRIT, vitesses, soins, Parry, Block,
    /// 9 résistances natives), proc d'enchant et cooldown. Vert sur les
    /// pièces d'équipement, blanc ailleurs (ref Trophy Enchant) ; un
    /// malus passe en rouge.</summary>
    private static void FillStats(VBoxContainer g, InventorySlot it,
        ItemTable.ItemDef? d)
    {
        if (d == null) return;
        var gear = ItemTipFormat.IsGearKind(d.Kind) || d.Kind == 81;
        var weapon = ItemTipFormat.IsWeaponKind(d.Kind);
        var fort = it.Strengthen;
        var pAtk = d.PhysicalAttack;
        var mAtk = d.MagicAttack;
        var def = d.Defence;
        if (weapon)
        {
            pAtk += ItemTipFormat.Fortify(d.PhysicalAttack, fort);
            mAtk += ItemTipFormat.Fortify(d.MagicAttack, fort);
        }
        else if (gear)
            def += ItemTipFormat.Fortify(d.Defence, fort);

        void S(string key, int v) => StatLine(g, UiTextEn.Translate(key), v,
            gear);
        S("P-ATK", pAtk); S("M-ATK", mAtk); S("DEF", def);
        S("HP", d.BonusHp); S("MP", d.BonusMp);
        S("STR", d.BonusStr); S("AGI", d.BonusAgi); S("INT", d.BonusInt);
        S("WIS", d.BonusWis); S("LCK", d.BonusLck);
        S("ACC", d.Hit); S("EVA", d.Dodge);
        S("CRIT Rate", d.Critical); S("CRIT DMG", d.CriticalDamage);
        S("M-CRIT Rate", d.MagicCritical);
        S("M-CRIT DMG", d.MagicCriticalDamage);
        S("ATK SPD", d.AtkSpeedBonus); S("Cast SPD", d.CastSpeed);
        S("G-Healing", d.GHealing); S("P-Healing", d.PHealing);
        S("Parry", d.Parry); S("Block", d.Block);
        for (var i = 0; i < d.Resistances.Length; i++)
            S(ItemTipFormat.ResistName(i), d.Resistances[i]);

        // Proc / effet d'usage (T_Enchant) : paragraphes verts.
        if (d.UseEnchantId > 0 &&
            EnchantTable.DescriptionOf(d.UseEnchantId) is { } fx)
        {
            if (g.GetChildCount() > 0) g.AddChild(Spacer(4));
            foreach (var l in fx.Replace("\r", "").Split('\n'))
            {
                var t = TextResolver.Clean(l).Trim();
                if (t.Length > 0)
                    g.AddChild(Label(t, Good, wrap: true, width: TextWidth));
                else
                    g.AddChild(Spacer(6));
            }
        }
        // Livre de sorts pet (UseType 6) : ligne rouge « remplace le
        // Nᵉ slot de prière/combat » (ref « pray book for pet »).
        if (d.UseType == 6 && d.UseEnchantId > 0 &&
            EnchantTable.Get(d.UseEnchantId) is { } bookFx &&
            EdenClient.Game.PetClientRules.TryParseBook(bookFx.Raw,
                out var bookPray, out var bookSlot))
        {
            if (g.GetChildCount() > 0) g.AddChild(Spacer(4));
            g.AddChild(Label(PetBookNote(bookPray, bookSlot), Warn,
                wrap: true, width: TextWidth));
        }
        if (d.CooldownDs > 0)
        {
            if (g.GetChildCount() > 0) g.AddChild(Spacer(4));
            g.AddChild(Label(UiTextEn.Translate("Cooldown") + ": "
                + ItemTipFormat.Cooldown(d.CooldownDs), White));
        }
    }

    private static void StatLine(VBoxContainer g, string label, int v,
        bool green)
    {
        if (v == 0) return;
        g.AddChild(Pair(label, ItemTipFormat.Signed(v), 8, White,
            v < 0 ? Warn : green ? Good : White));
    }

    /// <summary>Niveau requis, niveau max, ligne cyan de fortification
    /// + bonus verts (ref Heritage Spear : « Fortification Level: +12 »
    /// puis « P-ATK+2406 | M-ATK+1203 »).</summary>
    private static void FillRequirements(VBoxContainer g, InventorySlot it,
        ItemTable.ItemDef? d)
    {
        if (d == null) return;
        if (d.RequiredLevel > 0)
            g.AddChild(Pair(UiTextEn.Translate("Level Requirement"),
                d.RequiredLevel.ToString(), 14, White, White));
        if (d.MaxLevel > 0)
            g.AddChild(Label(UiTextEn.Translate("Max Lv %s1")
                .Replace("%s1", d.MaxLevel.ToString()), Warn));
        if (it.Strengthen <= 0 || !ItemTipFormat.IsGearKind(d.Kind)) return;
        if (g.GetChildCount() > 0) g.AddChild(Spacer(4));
        g.AddChild(Label(UiTextEn.Translate("Fortification Level: +%s1")
            .Replace("%s1", it.Strengthen.ToString()), Cyan));
        var parts = new List<string>();
        if (ItemTipFormat.IsWeaponKind(d.Kind))
        {
            var p = ItemTipFormat.Fortify(d.PhysicalAttack, it.Strengthen);
            var m = ItemTipFormat.Fortify(d.MagicAttack, it.Strengthen);
            if (p > 0) parts.Add($"{UiTextEn.Translate("P-ATK")}+{p}");
            if (m > 0) parts.Add($"{UiTextEn.Translate("M-ATK")}+{m}");
        }
        else
        {
            var df = ItemTipFormat.Fortify(d.Defence, it.Strengthen);
            if (df > 0) parts.Add($"{UiTextEn.Translate("DEF")}+{df}");
        }
        if (parts.Count > 0)
            g.AddChild(Label(string.Join(" | ", parts), Good, wrap: true,
                width: TextWidth));
    }

    // ------------------------- familier ----------------------------

    /// <summary>Layout œuf de familier (ref « item hover pet
    /// (legendary) ») : niveau, type + flags, sorts de combat puis
    /// aptitudes de prière. Les notes par lettre (ATK B, ACC B+…) ne
    /// sont pas dérivables de C_Partner/GrowType (non prouvé) → omises.
    /// </summary>
    private static void AppendPetBody(VBoxContainer box, InventorySlot it,
        ItemTable.ItemDef def, PartnerTable.PartnerDef pet)
    {
        var lvl = NewGroup();
        lvl.AddChild(Label(ItemTipFormat.PetLevel(
            UiTextEn.Translate("Pet Level: Lv%s1 %s2%"), 1, 0), White));
        var a = NewGroup();
        FillBlockA(a, it, def);
        var grades = NewGroup();
        FillPetGrades(grades, pet.Grow);
        var battle = NewGroup();
        FillPetSkills(battle, "Battle", pet.BattleSpells);
        var pray = NewGroup();
        FillPetSkills(pray, "Pray", pet.IntegrateSpells);
        AddGroups(box, true, lvl, a, grades, battle, pray);
    }

    /// <summary>Note rouge d'un livre de sorts pet (« Note: This book
    /// will replace the pet's third pray slot. »).</summary>
    public static string PetBookNote(bool pray, int slot) =>
        UiTextEn.Translate("Note: This book will replace the pet's {0} {1} slot.")
            .Replace("{0}", UiTextEn.Translate(
                EdenClient.Game.PetClientRules.OrdinalKey(slot)))
            .Replace("{1}", UiTextEn.Translate(pray ? "pray" : "battle"));

    /// <summary>Grades lettres ATK/ACC/CRIT/SPD d'après les index de
    /// croissance (HYPOTHESIS d'échelle — voir PetClientRules). Rien si
    /// l'œuf n'a pas de croissance fixe (tirage à l'invocation).</summary>
    private static void FillPetGrades(VBoxContainer g, int[] grow)
    {
        if (grow.All(v => v <= 0)) return;
        var keys = new[] { "ATK", "ACC", "CRIT", "SPD" };
        for (var i = 0; i < 4 && i < grow.Length; i++)
            g.AddChild(Label(UiTextEn.Translate(keys[i]) + " " +
                EdenClient.Game.PetClientRules.GradeLetter(grow[i]),
                White));
    }

    private static void FillPetSkills(VBoxContainer g, string title,
        int[] spells)
    {
        var any = false;
        foreach (var sid in spells)
        {
            if (sid <= 0) continue;
            if (!any)
            {
                g.AddChild(Label(UiTextEn.Translate(title), White));
                any = true;
            }
            g.AddChild(Label(SpellCatalog.Name(sid) + "-", SkillName));
            if (SkillDescription(sid, 1) is { } desc)
                foreach (var (t, c) in desc)
                    g.AddChild(t.Length == 0 ? Spacer(4)
                        : Label(t, c, wrap: true, width: TextWidth));
        }
    }

    /// <summary>Tooltip pour un simple id (boutique, courrier, lien).</summary>
    public static Control BuildForId(int itemId, int amount = 1,
        ItemTable.ItemDef? def = null) =>
        Build(new InventorySlot(0, 0, itemId, 0, 0, 0, amount), def);

    // ------------------------- briques -----------------------------

    private static Label Label(string text, Color tint, bool wrap = false,
        int width = 0)
    {
        var l = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize);
        l.AddThemeColorOverride("font_color", tint);
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(width, 0);
        }
        return l;
    }

    private static Control Spacer(int h) => new Control
    {
        CustomMinimumSize = new Vector2(0, h),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Control Row(string left, string right, Color lc, Color rc)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(TextWidth, 0),
        };
        var a = Label(left, lc);
        a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(a);
        row.AddChild(Label(right, rc));
        return row;
    }

    /// <summary>Libellé + valeur côte à côte (gabarit natif : texte
    /// aligné à gauche, valeur à <paramref name="gap"/> px).</summary>
    private static Control Pair(string left, string right, int gap,
        Color lc, Color rc)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", gap);
        if (left.Length > 0) row.AddChild(Label(left, lc));
        if (right.Length > 0) row.AddChild(Label(right, rc));
        return row;
    }

    /// <summary>Rangée d'icônes de l'imageset natif ToolTips (12×12).</summary>
    private static Control IconRow(string label, string image, int count)
    {
        var x = image switch
        {
            "EmbedNone" => 2, "EmbedJewelry" => 18,
            "StarNone" => 34, _ => 50,
        };
        var sheet = AssetCatalog.GetTexture("uidata/imagesets/ToolTips.png");
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(TextWidth, 0),
        };
        row.AddThemeConstantOverride("separation", 2);
        var l = Label(label, Dim);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(l);
        for (var i = 0; i < count; i++)
        {
            if (sheet == null) break;
            row.AddChild(new TextureRect
            {
                Texture = new AtlasTexture
                {
                    Atlas = sheet,
                    Region = new Rect2(x, 50, 12, 12),
                    FilterClip = true,
                },
                CustomMinimumSize = new Vector2(12, 12),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        return row;
    }

    // ------------------------- survol ------------------------------

    private const string MetaKey = "item_tooltip_provider";

    /// <summary>Affiche le tooltip d'item au survol de
    /// <paramref name="c"/> ; <paramref name="get"/> renvoie l'item
    /// courant (null = pas de tooltip). Idempotent. Le
    /// <c>TooltipText</c> natif du Control est vidé.</summary>
    public static void Bind(Control c, Func<InventorySlot?> get)
    {
        var first = !c.HasMeta(MetaKey);
        c.SetMeta(MetaKey, Variant.CreateFrom(0));
        _providers[c.GetInstanceId()] = get;
        _builders.Remove(c.GetInstanceId());
        if (!first) return;
        c.MouseEntered += () => HostOf(c)?.Enter(c);
        c.MouseExited += () => HostOf(c)?.Leave(c);
    }

    /// <summary>Variante id seul (0 = rien).</summary>
    public static void BindId(Control c, Func<int> id, int amount = 1) =>
        Bind(c, () => id() is var i && i > 0
            ? new InventorySlot(0, 0, i, 0, 0, 0, amount) : null);

    /// <summary>Tooltip libre (sorts, classes…) : <paramref name="build"/>
    /// renvoie le Control affiché au survol (null = rien). Partage le
    /// même host que les tooltips d'items.</summary>
    public static void BindCustom(Control c, Func<Control?> build)
    {
        var first = !c.HasMeta(MetaKey);
        c.SetMeta(MetaKey, Variant.CreateFrom(0));
        _builders[c.GetInstanceId()] = build;
        _providers.Remove(c.GetInstanceId());
        if (!first) return;
        c.MouseEntered += () => HostOf(c)?.Enter(c);
        c.MouseExited += () => HostOf(c)?.Leave(c);
    }

    private static readonly Dictionary<ulong, Func<InventorySlot?>>
        _providers = new();
    private static readonly Dictionary<ulong, Func<Control?>>
        _builders = new();

    private static Host? _host;

    private static Host? HostOf(Control c)
    {
        if (_host != null && GodotObject.IsInstanceValid(_host))
            return _host;
        if (!c.IsInsideTree()) return null;
        _host = new Host { Layer = 200, Name = "ItemTooltipHost" };
        c.GetTree().Root.AddChild(_host);
        return _host;
    }

    private sealed partial class Host : CanvasLayer
    {
        private Control? _owner;
        private Control? _tip;
        private InventorySlot? _shown;
        private double _wait;
        private bool _empty;   // builder libre a renvoyé null au survol

        public void Enter(Control c) { _owner = c; _wait = 0; _empty = false; }
        public void Leave(Control c)
        {
            if (_owner == c) Clear();
        }

        private void Clear()
        {
            _owner = null; _shown = null;
            _tip?.QueueFree();
            _tip = null;
        }

        public override void _Process(double delta)
        {
            if (_owner == null) return;
            if (!GodotObject.IsInstanceValid(_owner) ||
                !_owner.IsVisibleInTree()) { Clear(); return; }
            var id = _owner.GetInstanceId();
            if (_builders.TryGetValue(id, out var b))
            {
                // Tooltip libre : contenu figé à l'entrée du survol.
                if (_tip == null && !_empty)
                {
                    _tip = b();
                    if (_tip != null) AddChild(_tip);
                    else _empty = true;
                }
            }
            else
            {
                var it = _providers.TryGetValue(id,
                    out var g) ? g() : null;
                if (it == null || it.ItemId == 0)
                {
                    _tip?.QueueFree(); _tip = null; _shown = null;
                    return;
                }
                if (_tip == null || _shown != it)
                {
                    if (_tip == null && (_wait -= delta) > 0) return;
                    _tip?.QueueFree();
                    _tip = Build(it, null);
                    _shown = it;
                    AddChild(_tip);
                }
            }
            if (_tip == null) return;
            var vp = GetViewport().GetVisibleRect().Size;
            var sz = _tip.GetCombinedMinimumSize();
            _tip.Size = sz;
            var mp = GetViewport().GetMousePosition();
            var pos = mp + new Vector2(16, 18);
            if (pos.X + sz.X > vp.X) pos.X = mp.X - sz.X - 8;
            if (pos.Y + sz.Y > vp.Y) pos.Y = mp.Y - sz.Y - 8;
            _tip.Position = new Vector2(
                Math.Clamp(pos.X, 0, Math.Max(0, vp.X - sz.X)),
                Math.Clamp(pos.Y, 0, Math.Max(0, vp.Y - sz.Y)));
        }
    }
}
