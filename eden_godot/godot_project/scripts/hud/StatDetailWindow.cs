using Godot;
using EdenClient.Data;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Panneau DÉDIÉ à une statistique du profil (ouvert au clic sur son nom ou sa valeur) :
/// valeur, diagramme des sources (naturel + bonus pour les 5 stats de base ; jauge vers le plafond
/// pour les autres), rôle, formule générique et « vos valeurs » injectées par le serveur (32002).</summary>
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

    public static void Open(HudRoot hud, string key, string title, string valueText, int total, int? baseValue)
    {
        key = key.TrimEnd('%');
        _key = key; _title = title; _value = valueText; _total = total; _base = baseValue;
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
            AdvancedStatsWindow.Updated += OnUpdated;
            _win.TreeExiting += () => AdvancedStatsWindow.Updated -= OnUpdated;
        }
        _win.Visible = true;
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

    private static void Rebuild()
    {
        if (_col == null) return;
        foreach (var c in _col.GetChildren()) c.QueueFree();
        var ink = new Color(0.96f, 0.95f, 0.92f); var dim = new Color(0.75f, 0.75f, 0.72f);
        var gold = new Color(1f, 0.93f, 0.62f); var green = new Color(0.45f, 0.85f, 0.5f);
        _col.AddChild(L($"{_title}", 18, gold, false));
        _col.AddChild(L(_value, 26, ink, false));

        // --- diagramme des sources ---
        if (_base is { } nat && _total >= nat)
        {
            var bonus = _total - nat;
            var bar = new Bar { CustomMinimumSize = new Vector2(0, 22), Max = Math.Max(1, _total) };
            bar.Seg.Add((nat, new Color(0.35f, 0.55f, 0.9f), "natural"));
            bar.Seg.Add((bonus, green, "bonus"));
            _col.AddChild(bar);
            _col.AddChild(L($"■ {UiTextEn.Translate("Natural (class, level, race)")} : {nat}", 12, new Color(0.55f, 0.7f, 1f)));
            _col.AddChild(L($"■ {UiTextEn.Translate("Bonus (gear, buffs, certs, talents)")} : +{bonus}", 12, green));
            _col.AddChild(L($"{_total}  =  {nat}  +  {bonus}", 13, ink, false));
        }
        else if (Cap.TryGetValue(_key, out var cap) &&
                 float.TryParse(new string(_value.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray()).Replace(',', '.'),
                     System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            var bar = new Bar { CustomMinimumSize = new Vector2(0, 22), Max = cap };
            bar.Seg.Add((v, new Color(0.9f, 0.7f, 0.25f), "value"));
            _col.AddChild(bar);
            _col.AddChild(L($"{UiTextEn.Translate("Cap")} : {cap}%", 12, dim));
        }

        // --- rôle + formule ---
        var fkey = FormulaKey.TryGetValue(_key, out var fk) ? fk : "";
        var doc = fkey.Length > 0 ? AdvancedStatsWindow.DocOf(fkey) : null;
        var generic = ProfileStatInfo.DescOf(_key);
        if (doc is { } d)
        {
            _col.AddChild(L(UiTextEn.Translate("Why"), 13, gold, false));
            _col.AddChild(L(UiTextEn.Translate(d.Role), 12, ink));
            _col.AddChild(L(UiTextEn.Translate("Formula"), 13, gold, false));
            _col.AddChild(L(UiTextEn.Translate(d.Formula), 12, dim));
            if (d.Note.Length > 0) _col.AddChild(L(UiTextEn.Translate(d.Note), 11, dim));
        }
        else if (generic.Length > 0)
        {
            _col.AddChild(L(UiTextEn.Translate("Formula"), 13, gold, false));
            _col.AddChild(L(UiTextEn.Translate(generic), 12, ink));
        }
        // --- vos valeurs (serveur 32002) : schéma des contributions ---
        var last = AdvancedStatsWindow.Last;
        var f = last?.Formulas.FirstOrDefault(x => x.Key == fkey);
        if (f != null)
        {
            _col.AddChild(L(Tr("Your values", "Vos valeurs"), 13, gold, false));
            var terms = ParseTerms(f.Expr);
            var parts = terms.Where(t => t.Kind == 0 && t.Value > 0).ToList();
            var sum = parts.Sum(t => t.Value);
            if (parts.Count > 0 && sum > 0)
            {
                var bar = new Bar { CustomMinimumSize = new Vector2(0, 26), Max = (float)sum };
                foreach (var t in parts) bar.Seg.Add(((float)t.Value, t.Color, t.Label));
                _col.AddChild(bar);
                foreach (var t in parts)
                    _col.AddChild(Row(t.Color, t.Label, t.Detail, $"{t.Value:0.##}", $"{t.Value / sum * 100:0}%"));
            }
            foreach (var t in terms.Where(t => t.Kind != 0))
                _col.AddChild(Row(t.Color, t.Label, t.Detail, t.Kind == 1 ? $"×{t.Value:0.##}" : $"+{t.Value:0.##}", ""));
            _col.AddChild(L($"= {f.Value:0.##}{f.Unit}", 20, ink, false));
            _col.AddChild(L(Tr("Raw formula", "Formule brute") + " : " + f.Expr, 10, dim));
        }
        else if (fkey.Length > 0)
            _col.AddChild(L(UiTextEn.Translate("Loading..."), 11, dim));
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
