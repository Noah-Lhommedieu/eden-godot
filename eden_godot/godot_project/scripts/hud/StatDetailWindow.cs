using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Panneau DÉDIÉ à une statistique du profil (ouvert au clic sur son nom ou sa valeur).
/// Quatre blocs séparés : valeur + schéma (naturel/bonus pour les 5 stats de base, jauge vers le
/// plafond pour les taux, répartition des termes du calcul serveur 32002), Explication (rôle,
/// formule — texte statique), Provenances (équipement, certificats, buffs, nerfs, états ACTUELS
/// avec leur valeur — jamais d'identifiant) et Calcul (valeurs injectées par le serveur).
/// Se rafraîchit à chaque changement de stats, de statuts, d'équipement ou de certificats.</summary>
public static partial class StatDetailWindow
{
    // clé profil → clé de formule serveur (AdvancedStatsWindow.Docs)
    private static readonly Dictionary<string, string> FormulaKey = new()
    {
        ["patk"] = "P-ATK", ["matk"] = "M-ATK", ["acc"] = "Hit Rate", ["eva"] = "Dodge Rate",
        ["crit"] = "Critical Rate", ["mcrit"] = "Magic Critical Rate", ["critdmg"] = "Critical Damage",
        ["mcritdmg"] = "Magic Critical Damage", ["parry"] = "Parry Rate", ["block"] = "Block Rate",
        ["def"] = "Damage Reduction", ["aspd"] = "Attack Speed", ["cspd"] = "Cast Speed", ["move"] = "Move Speed",
    };
    // plafonds natifs (%), pour la jauge
    private static readonly Dictionary<string, float> Cap = new()
    {
        ["acc"] = 300, ["eva"] = 100, ["critdmg"] = 300, ["mcritdmg"] = 300, ["parry"] = 70, ["block"] = 50,
        ["def"] = 70, ["move"] = 200,
    };

    private static Panel? _win;
    private static VBoxContainer? _col;
    private static string _key = "";
    private static string _value = "";
    private static int _total;
    private static int? _base;
    private static string _title = "";
    private static Func<string>? _valueGetter;
    private static Action? _liveHook;

    public static void Open(HudRoot hud, string key, string title, string valueText, int total, int? baseValue,
        Func<string>? valueGetter = null)
    {
        key = key.TrimEnd('%');
        _key = key; _title = title; _value = valueText; _total = total; _base = baseValue;
        _valueGetter = valueGetter;
        if (_win == null || !GodotObject.IsInstanceValid(_win))
        {
            _win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Statistics"), 640, 40, 470, 650);
            hud.AddChild(_win);
            DragHelper.Persist(_win, "StatDetail");
            var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            HudRoot.Pin(sc, 0, 0, 1, 1, 8, 30, -8, -8);
            _win.AddChild(sc);
            _col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _col.AddThemeConstantOverride("separation", 6);
            sc.AddChild(_col);
            // Tout ce qui peut changer une provenance ou une valeur relance le calcul.
            _liveHook = OnLiveChange;
            AdvancedStatsWindow.Updated += OnUpdated;
            PlayerVitals.StatsChanged += _liveHook;
            EntityBuffs.Changed += _liveHook;
            PlayerEquipment.Changed += _liveHook;
            PlayerProfileState.Changed += OnProfileState;
            _win.TreeExiting += () =>
            {
                AdvancedStatsWindow.Updated -= OnUpdated;
                if (_liveHook != null)
                {
                    PlayerVitals.StatsChanged -= _liveHook;
                    EntityBuffs.Changed -= _liveHook;
                    PlayerEquipment.Changed -= _liveHook;
                }
                PlayerProfileState.Changed -= OnProfileState;
            };
        }
        _win.Visible = true;
        Rebuild();
        AdvancedStatsWindow.Request?.Invoke();
    }

    private static void OnProfileState(string _) => OnLiveChange();

    /// <summary>Valeurs du profil relues, provenances recalculées, formule serveur redemandée.</summary>
    private static void OnLiveChange()
    {
        if (_win == null || !GodotObject.IsInstanceValid(_win)) return;
        if (_valueGetter != null) _value = _valueGetter();
        if (ProfileStatInfo.CoreIndex.TryGetValue(_key, out var idx) && PlayerVitals.TotalStats is { } s)
        {
            _total = new[] { s.Str, s.Int, s.Agi, s.Wis, s.Lck }[idx];
            _base = PlayerVitals.BaseStats is { } bs && idx < bs.Stats.Length ? bs.Stats[idx] : null;
        }
        else if (ProfileStatInfo.ResistIndex(_key) is var ri && ri >= 0
                 && PlayerVitals.TotalStats is { } st
                 && ri < st.Resists.Length)
            _total = st.Resists[ri];
        Rebuild();
        AdvancedStatsWindow.Request?.Invoke();
    }

    private static void OnUpdated()
    {
        if (_win != null && GodotObject.IsInstanceValid(_win)) Rebuild();
    }

    private static Label L(string t, int size, Color col, bool wrap = true)
    {
        var l = new Label { Text = t, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(wrap ? 410 : 0, 0) };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    /// <summary>Barre empilée : segments (valeur, couleur, légende) ; un trait marque le plafond éventuel.</summary>
    private sealed partial class Bar : Control
    {
        public List<(float V, Color C, string Name)> Seg = new();
        public float Max = 1;
        public override void _Draw()
        {
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.12f, 0.12f, 0.14f));
            var x = 0f;
            foreach (var (v, c, _) in Seg)
            {
                var w = Size.X * Mathf.Clamp(v / Max, 0, 1);
                DrawRect(new Rect2(x, 1, w, Size.Y - 2), c);
                x += w;
            }
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.6f, 0.6f, 0.6f), false, 1);
        }
    }

    private static readonly Color Ink = new(0.96f, 0.95f, 0.92f);
    private static readonly Color Dim = new(0.75f, 0.75f, 0.72f);
    private static readonly Color Gold = new(1f, 0.93f, 0.62f);
    private static readonly Color Green = new(0.45f, 0.85f, 0.5f);

    private static void Rebuild()
    {
        if (_col == null) return;
        foreach (var c in _col.GetChildren()) c.QueueFree();
        var last = AdvancedStatsWindow.Last;
        var fkey = FormulaKey.TryGetValue(_key, out var fk) ? fk : "";
        var f = last?.Formulas.FirstOrDefault(x => x.Key == fkey);

        // ===== En-tête + schéma =====
        _col.AddChild(L($"{_title}", 18, Gold, false));
        _col.AddChild(L(_value, 26, Ink, false));
        var src = Gather(_key);
        BuildSchema(f, src);

        // ===== Explication (statique : rien de personnel ici) =====
        var doc = fkey.Length > 0 ? AdvancedStatsWindow.DocOf(fkey) : null;
        var generic = ProfileStatInfo.DescOf(_key);
        if (doc is { } d || generic.Length > 0)
        {
            Section(Tr("Explanation", "Explication"));
            if (doc is { } dd)
            {
                _col.AddChild(L(UiTextEn.Translate(dd.Role), 12, Ink));
                _col.AddChild(L(Tr("Formula", "Formule") + " : " + UiTextEn.Translate(dd.Formula), 12, Dim));
                if (dd.Note.Length > 0) _col.AddChild(L(UiTextEn.Translate(dd.Note), 11, Dim));
            }
            else _col.AddChild(L(UiTextEn.Translate(generic), 12, Ink));
        }

        // ===== Provenances (état courant du joueur) =====
        BuildSources(src);

        // ===== Calcul serveur (valeurs injectées, 32002) =====
        if (f != null)
        {
            Section(Tr("Calculation", "Calcul"));
            var terms = ParseTerms(f.Expr);
            foreach (var t in terms)
                _col.AddChild(Row(t.Color, t.Label, t.Detail,
                    t.Kind == 1 ? $"×{t.Value:0.##}" : t.Kind == 2 ? $"+{t.Value:0.##}" : $"{t.Value:0.##}", ""));
            _col.AddChild(L($"= {f.Value:0.##}{f.Unit}", 20, Ink, false));
            _col.AddChild(L(f.Expr, 10, Dim));
        }
        else if (fkey.Length > 0)
            _col.AddChild(L(UiTextEn.Translate("Loading..."), 11, Dim));
    }

    private static void Section(string name)
    {
        _col!.AddChild(new HSeparator());
        _col.AddChild(L(name, 14, Gold, false));
    }

    /// <summary>Schéma : naturel + bonus (stats de base), sinon jauge vers le plafond (valeur du
    /// calcul serveur, en %), sinon répartition additive des termes du calcul, sinon
    /// répartition des provenances en valeur plate (hors session, sans 32002).</summary>
    private static void BuildSchema(ZoneAdvStatsInfo.Formula? f,
        List<StatSource> src)
    {
        // Résistances : AttrResist natif = équipement + effets actifs
        // (kinddata), borné ±50 phys / ±70 élémentaire — aucun
        // « naturel » n'est transmis (le 296 ne porte que le total).
        // La part non couverte par les sources visibles (gemmes
        // serties, enchantements d'objet, bonus de set, plafond) est
        // marquée « non ventilée » plutôt que présentée comme base.
        if (_key.StartsWith("res_", System.StringComparison.Ordinal)
            && PlayerVitals.TotalStats != null)
        {
            var eq = src.Where(s => !s.Percent &&
                s.Group == SourceGroup.Equipment).Sum(s => s.Value);
            var fx = src.Where(s => !s.Percent &&
                s.Group != SourceGroup.Equipment).Sum(s => s.Value);
            var rest = _total - eq - fx;
            string Sgn(double v) =>
                $"{(v >= 0 ? "+" : "")}{v:0.##}";
            var grey = new Color(0.5f, 0.5f, 0.52f);
            var bar = new Bar
            {
                CustomMinimumSize = new Vector2(0, 26),
                Max = (float)Math.Max(1, Math.Abs(eq) + Math.Abs(fx)
                    + Math.Abs(rest)),
            };
            if (eq != 0)
                bar.Seg.Add(((float)Math.Abs(eq),
                    GroupColor(SourceGroup.Equipment), "equip"));
            if (fx != 0)
                bar.Seg.Add(((float)Math.Abs(fx),
                    GroupColor(SourceGroup.Buff), "effects"));
            if (rest != 0)
                bar.Seg.Add(((float)Math.Abs(rest), grey, "unattributed"));
            _col!.AddChild(bar);
            _col.AddChild(L($"■ {UiTextEn.Translate("Natural (class, level, race)")} : 0 — " +
                Tr("no transmitted base for resistances",
                   "aucune base transmise pour les résistances"), 12,
                new Color(0.55f, 0.7f, 1f)));
            _col.AddChild(L($"■ {Tr("Equipment", "Équipement")} : {Sgn(eq)}",
                12, GroupColor(SourceGroup.Equipment)));
            _col.AddChild(L($"■ {Tr("Active effects (buffs, certificates, states)", "Effets actifs (buffs, certificats, états)")} : {Sgn(fx)}",
                12, GroupColor(SourceGroup.Buff)));
            if (rest != 0)
                _col.AddChild(L($"■ {Tr("Unattributed (socketed gems, item enchants/fortify, native cap)", "Non ventilé (gemmes serties, enchants/fortification d'objet, plafond natif)")} : {Sgn(rest)}",
                    12, grey));
            _col.AddChild(L($"{_total}  =  {Sgn(eq)} + {Sgn(fx)} + {Sgn(rest)}",
                13, Ink, false));
            return;
        }
        if (_base is { } nat && _total >= nat)
        {
            var bonus = _total - nat;
            var bar = new Bar { CustomMinimumSize = new Vector2(0, 22), Max = Math.Max(1, _total) };
            bar.Seg.Add((nat, new Color(0.35f, 0.55f, 0.9f), "natural"));
            bar.Seg.Add((bonus, Green, "bonus"));
            _col!.AddChild(bar);
            _col.AddChild(L($"■ {UiTextEn.Translate("Natural (class, level, race)")} : {nat}", 12, new Color(0.55f, 0.7f, 1f)));
            _col.AddChild(L($"■ {Tr("Bonus", "Bonus")} : +{bonus}", 12, Green));
            _col.AddChild(L($"{_total}  =  {nat}  +  {bonus}", 13, Ink, false));
            return;
        }
        if (f != null && Cap.TryGetValue(_key, out var cap))
        {
            var bar = new Bar { CustomMinimumSize = new Vector2(0, 22), Max = cap };
            bar.Seg.Add((f.Value, new Color(0.9f, 0.7f, 0.25f), "value"));
            _col!.AddChild(bar);
            _col.AddChild(L($"{f.Value:0.##}{f.Unit}  /  {UiTextEn.Translate("Cap")} {cap}%", 12, Dim));
            return;
        }
        if (f != null && TermBarKeys.Contains(f.Key))
        {
            var parts = ParseTerms(f.Expr).Where(t => t.Kind == 0 && t.Value > 0).ToList();
            var sum = parts.Sum(t => t.Value);
            if (parts.Count > 0 && sum > 0)
            {
                var bar = new Bar { CustomMinimumSize = new Vector2(0, 26), Max = (float)sum };
                foreach (var t in parts) bar.Seg.Add(((float)t.Value, t.Color, t.Label));
                _col!.AddChild(bar);
                foreach (var t in parts)
                    _col.AddChild(Row(t.Color, t.Label, t.Detail, $"{t.Value:0.##}", $"{t.Value / sum * 100:0}%"));
            }
            return;
        }
        // Hors session (pas de 32002, pas de stats) : répartition des
        // contributions plates des provenances actives — le détail
        // nommé reste sous « Provenances ».
        var flats = src.Where(s => !s.Percent && s.Value != 0).ToList();
        var tot = flats.Where(s => s.Value > 0).Sum(s => s.Value);
        if (flats.Count > 0 && tot > 0)
        {
            var bar = new Bar { CustomMinimumSize = new Vector2(0, 26),
                Max = (float)flats.Sum(s => Math.Abs(s.Value)) };
            foreach (var s in flats)
                bar.Seg.Add(((float)Math.Abs(s.Value), GroupColor(s.Group), s.Name));
            _col!.AddChild(bar);
            _col.AddChild(L(Tr("Share of current sources",
                "Part des sources actives"), 11, Dim));
        }
    }

    /// <summary>Formules dont les termes additifs forment une répartition lisible.</summary>
    private static readonly HashSet<string> TermBarKeys = new()
    {
        "P-ATK", "M-ATK", "Hit Rate", "Dodge Rate", "Critical Rate", "Magic Critical Rate",
        "Critical Damage", "Magic Critical Damage", "Parry Rate", "Block Rate", "Attack Speed", "Cast Speed",
    };

    private static readonly (SourceGroup G, string En, string Fr, Color C)[] GroupInfo =
    {
        (SourceGroup.Equipment, "Equipment", "Équipement", new Color(0.7f, 0.55f, 0.95f)),
        (SourceGroup.Certificate, "Certificates", "Certificats", new Color(0.95f, 0.65f, 0.3f)),
        (SourceGroup.Buff, "Buffs", "Buffs", new Color(0.4f, 0.85f, 0.85f)),
        (SourceGroup.Nerf, "Nerfs", "Nerfs", new Color(0.9f, 0.3f, 0.3f)),
        (SourceGroup.State, "States", "États", new Color(0.35f, 0.65f, 1f)),
    };

    /// <summary>Couleur d'une famille de provenance (GroupInfo).</summary>
    private static Color GroupColor(SourceGroup g) =>
        GroupInfo.FirstOrDefault(x => x.G == g).C;

    /// <summary>Provenances groupées : ce qui, MAINTENANT, apporte ou retire de cette statistique.
    /// Les valeurs sont celles des sources (plat ou %), pas le résultat du calcul.</summary>
    private static void BuildSources(List<StatSource> src)
    {
        Section(Tr("Sources", "Provenances"));
        if (src.Count == 0)
        {
            _col!.AddChild(L(Tr("No active source.", "Aucune source active."), 12, Dim));
            return;
        }
        foreach (var (g, en, fr, col) in GroupInfo)
        {
            var rows = src.Where(s => s.Group == g).ToList();
            if (rows.Count == 0) continue;
            _col!.AddChild(L(Tr(en, fr), 12, col, false));
            foreach (var s in rows)
            {
                var r = Row(col, s.Name, "", StatSources.Format(s), "");
                _col.AddChild(r);
            }
        }
    }

    /// <summary>Rassemble les provenances de <paramref name="stat"/> : pièces portées, certificats
    /// posés (loc &gt; 0) et statuts actifs du joueur.</summary>
    internal static List<StatSource> Gather(string stat)
    {
        var acc = new List<StatSource>();
        foreach (var (_, itemId) in PlayerEquipment.Worn)
            if (ItemTable.Lookup(itemId) is { } d)
                StatSources.AddItem(acc, stat, TextResolver.ItemName(itemId) ?? d.Name, d);
        foreach (var (id, loc) in PlayerProfileState.Licenses)
        {
            if (loc == 0) continue;
            var item = id & 0xffff;
            if (ItemTable.Lookup(item) is { UseEnchantId: > 0 } d && EnchantTable.Get(d.UseEnchantId) is { } e)
                StatSources.AddCommands(acc, stat, SourceGroup.Certificate,
                    TextResolver.ItemName(item) ?? d.Name, EnchantTable.CommandsOf(e));
        }
        foreach (var b in EntityBuffs.Snapshot(BuffBar.SelfHandle))
        {
            if (EnchantTable.Get(b.EffectId) is not { } e) continue;
            var group = EnchantTable.KindOf(b.EffectId) switch
            {
                StatusKind.Buff => SourceGroup.Buff,
                StatusKind.Nerf => SourceGroup.Nerf,
                StatusKind.State => SourceGroup.State,
                _ => (SourceGroup?)null,
            };
            if (group is { } g)
                StatSources.AddCommands(acc, stat, g, EnchantTable.NameOf(b.EffectId),
                    EnchantTable.CommandsOf(e), b.StackCount);
        }
        GD.Print($"[statdetail] {stat} : worn={PlayerEquipment.Worn.Count} lic={PlayerProfileState.Licenses.Count}" +
            $"(actifs {PlayerProfileState.Licenses.Count(l => l.Loc > 0)}) buffs={EntityBuffs.Snapshot(BuffBar.SelfHandle).Count} -> {acc.Count} sources");
        return acc;
    }

    private static bool Fr => TextResolver.Language == "fr";
    private static string Tr(string en, string fr) => Fr ? fr : en;

    private static Control Row(Color c, string name, string detail, string value, string share)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        var sq = new ColorRect { Color = c, CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        h.AddChild(sq);
        var n = L(name + (detail.Length > 0 ? $"  ({detail})" : ""), 12, new Color(0.93f, 0.93f, 0.9f));
        n.CustomMinimumSize = new Vector2(150, 0);
        h.AddChild(n);
        var v = L(value, 12, new Color(1f, 0.93f, 0.62f), false);
        v.HorizontalAlignment = HorizontalAlignment.Right;
        v.CustomMinimumSize = new Vector2(60, 0);
        h.AddChild(v);
        if (share.Length > 0)
        {
            var sh = L(share, 11, new Color(0.7f, 0.7f, 0.68f), false);
            sh.CustomMinimumSize = new Vector2(34, 0);
            h.AddChild(sh);
        }
        return h;
    }

    // Kind 0 = contribution additive, 1 = multiplicateur (%), 2 = bonus fixe
    private sealed record Term(string Label, string Detail, double Value, Color Color, int Kind);

    private static List<Term> ParseTerms(string e)
    {
        var res = new List<Term>();
        // « 150 + equip … » : base native en tête (dégâts critiques).
        var lead = System.Text.RegularExpressions.Regex.Match(e, @"^\s*(\d+(?:\.\d+)?)\s*\+");
        if (lead.Success)
            res.Add(new(Tr("Base", "Base"), Tr("native value", "valeur native"),
                double.Parse(lead.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                new Color(0.55f, 0.7f, 1f), 0));
        var rx = System.Text.RegularExpressions.Regex.Matches(e,
            @"\b(STR|AGI|INT|LCK|equip|weapon|buff|main|off|ride/buff|DEF)\s+(-?\d+(?:\.\d+)?)(?:\s*x\s*(\d+(?:\.\d+)?))?");
        foreach (System.Text.RegularExpressions.Match m in rx)
        {
            var key = m.Groups[1].Value;
            var val = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var fac = m.Groups[3].Success
                ? double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 1;
            switch (key)
            {
                case "STR": res.Add(new(Tr("Strength", "Force"), $"{val:0.##} × {fac:0.##}", val * fac, new Color(0.9f, 0.45f, 0.35f), 0)); break;
                case "AGI": res.Add(new(Tr("Agility", "Agilité"), $"{val:0.##} × {fac:0.##}", val * fac, new Color(0.45f, 0.85f, 0.5f), 0)); break;
                case "INT": res.Add(new(Tr("Intelligence", "Intelligence"), $"{val:0.##} × {fac:0.##}", val * fac, new Color(0.45f, 0.6f, 1f), 0)); break;
                case "LCK": res.Add(new(Tr("Luck", "Chance"), $"{val:0.##} × {fac:0.##}", val * fac, new Color(0.95f, 0.85f, 0.35f), 0)); break;
                case "equip": res.Add(new(Tr("Equipment", "Équipement"), Tr("armor & accessories", "armures et accessoires"), val, new Color(0.7f, 0.55f, 0.95f), 0)); break;
                case "weapon": res.Add(new(Tr("Weapon", "Arme"), Tr("weapon stat", "stat de l'arme"), val, new Color(0.95f, 0.65f, 0.3f), 0)); break;
                case "buff": res.Add(new(Tr("Buffs", "Buffs"), Tr("buffs, talents, certificates", "buffs, talents, certificats"), val, new Color(0.4f, 0.85f, 0.85f), 0)); break;
                case "ride/buff": res.Add(new(Tr("Mount & buffs", "Monture et buffs"), $"{val:0.##}%", val, new Color(0.4f, 0.85f, 0.85f), 0)); break;
                case "DEF": res.Add(new(Tr("Defence", "Défense"), $"{val:0.##}", val, new Color(0.6f, 0.75f, 0.9f), 0)); break;
                default: break;
            }
        }
        var pct = System.Text.RegularExpressions.Regex.Match(e, @"\((?:100|1)\s*\+\s*(-?\d+(?:\.\d+)?)%\)");
        if (pct.Success)
        {
            var pv = double.Parse(pct.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            res.Add(new(Tr("% multiplier", "Multiplicateur %"), Tr("bonus from % effects", "bonus des effets en %"), 1 + (e.Contains("(100 +") ? pv / 100.0 : pv / 100.0), new Color(0.9f, 0.6f, 0.8f), 1));
        }
        var flat = System.Text.RegularExpressions.Regex.Match(e, @"\+\s*(-?\d+(?:\.\d+)?)\s*$");
        if (flat.Success)
            res.Add(new(Tr("Flat bonus", "Bonus fixe"), "", double.Parse(flat.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), new Color(0.75f, 0.75f, 0.75f), 2));
        return res;
    }
}
