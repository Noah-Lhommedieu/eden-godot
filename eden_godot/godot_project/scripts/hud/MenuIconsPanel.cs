using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Rangée d'icônes menu bas-droite — instancie le layout natif
/// <c>FunctionMenu.layout</c> (bandeau <c>main:FunctionMenuBG</c> +
/// boutons Charact/bag/Mission/Commu/skill/City/ItemMall/Sys/Team/
/// Work, ~10 icônes comme sur la capture).
/// </summary>
public partial class MenuIconsPanel : Control
{
    /// <summary>Nom de bouton natif → action logique remontée à
    /// <see cref="MenuAction"/> (« bag » ouvre l'inventaire…).</summary>
    private static readonly (string Btn, string Action)[] Map =
    {
        ("CharactBtn", "character"),
        ("bagBtn", "bag"),
        ("MissionBtn", "mission"),
        ("CommuBtn", "community"),
        ("skillBtn", "skill"),
        ("CityBtn", "city"),
        ("ItemMallBtn", "itemmall"),
        ("SysBtn", "system"),
        ("TeamBtn", "team"),
        ("WorkBtn", "work"),
        // PetBtn n'existe que dans la variante « 5pPet » du menu
        // (affichée nativement dès qu'un familier est géré) —
        // silencieusement ignorée sur FunctionMenu.layout classique.
        ("PetBtn", "pet"),
    };

    /// <summary>Ordre gauche→droite de la rangée (ref) : perso, sac,
    /// quêtes, classe, communauté, ville, item mall, familier, système.</summary>
    private static readonly string[] RowOrder =
    {
        "CharactBtn", "bagBtn", "MissionBtn", "skillBtn", "CommuBtn",
        "CityBtn", "ItemMallBtn", "PetBtn", "SysBtn",
    };

    /// <summary>Action logique d'un bouton du menu.</summary>
    public event Action<string>? MenuAction;

    public void Build(HudRoot hud)
    {
        // Variante « 5pPet » en priorité : même menu + PetBtn
        // (icône familier) — le natif la substitue quand un pet
        // est invoqué ; on la charge d'office pour exposer la
        // fenêtre Familier (P).
        var root = hud.Build("FunctionMenu5pPet.layout")
                   ?? hud.Build("FunctionMenu.layout");
        if (root == null)
        {
            BuildFallback();
            return;
        }
        AddChild(root);
        // A4.2 : HudLayout doit enregistrer MenuBackgrand (le strip),
        // pas la racine 550×385 (bande de drag fantôme au-dessus).
        root.SetMeta(HudLayout.DescendMeta, true);
        // La bande d'icônes MenuBackgrand est sérialisée au bord
        // gauche de la racine 550×385 — épinglée explicitement en
        // bas-droite de l'écran (142×74, marge 4 px).
        // Ref juste le jeu.png : UNE rangée de 9 icônes 32×40, pas
        // 35 px, bord droit à 6 px, bas à 12 px (la grille native 2×5
        // est celle du client d'origine pré-Reforged).
        var strip = TopLeftVitals.Find<Control>(root, "MenuBackgrand");
        if (strip != null)
        {
            var w = (RowOrder.Length - 1) * 35 + 32;
            HudRoot.Pin(strip, 1, 1, 1, 1, -6 - w, -52, -6, -12);
            if (strip is TextureRect tr) tr.Texture = null;
            for (var i = 0; i < RowOrder.Length; i++)
            {
                var b = TopLeftVitals.Find<Control>(strip, RowOrder[i]);
                if (b != null)
                    HudRoot.Pin(b, 0, 0, 0, 0, i * 35, 0, i * 35 + 32, 40);
            }
            var team = TopLeftVitals.Find<Control>(strip, "TeamBtn");
            if (team != null) team.Visible = false;
        }
        // Bulles « beginners_guide » du layout natif — popups tutoriel
        // pilotées par le code natif, jamais affichées à froid. Les
        // labels *Text (tooltips de boutons aux ancres x négatives)
        // sont masqués : ils flottaient au milieu du bord bas.
        // Badges « nouveauté » CN (set:CNDejaVu auto1) : présents dans
        // le layout mais jamais pilotés par le code natif — le client
        // vanilla ne les affiche pas (ref juste le jeu.png).
        foreach (var name in new[] { "Fram", "MissionFram", "skillFram",
                                     "NoobBorderTip", "NoobBorderSkill",
                                     "WorkBtn" /* placeholder Nofoto */,
                                     "CharactText", "bagText",
                                     "MissionText", "CommuText",
                                     "skillText", "CityText",
                                     "ItemMallText", "SysText",
                                     "TeamText", "WorkText",
                                     "PetText" })
        {
            var n = TopLeftVitals.Find<Control>(root, name);
            if (n != null) n.Visible = false;
        }
        foreach (var (btn, action) in Map)
        {
            var b = TopLeftVitals.Find<BaseButton>(root, btn);
            if (b == null) continue;
            var a = action;
            b.Pressed += () => MenuAction?.Invoke(a);
        }
    }

    /// <summary>Repli : boutons texte alignés bas-droite.</summary>
    private void BuildFallback()
    {
        var box = new HBoxContainer();
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        box.Position = new Vector2(-330, -40);
        foreach (var (_, action) in Map)
        {
            var b = new Button { Text = action };
            b.AddThemeFontSizeOverride("font_size", 9);
            var a = action;
            b.Pressed += () => MenuAction?.Invoke(a);
            box.AddChild(b);
        }
        AddChild(box);
    }

    /// <summary>Sonde <c>--menu-test</c> : émet le vrai signal Pressed
    /// de chaque icône, vérifie qu'une fenêtre s'ouvre (diff des
    /// contrôles nouvellement visibles), la referme, capture finale
    /// dans <c>godot-shots/u7_menu.png</c> (preuve U7).</summary>
    public sealed partial class MenuProbe : Node
    {
        private readonly MenuIconsPanel _m;
        private readonly HudRoot _hud;
        private readonly string _shotsDir;
        private BaseButton?[] _btns = Array.Empty<BaseButton?>();
        private HashSet<ulong> _before = new();
        private List<string> _opened = new();
        private int _frame, _step, _i, _wait;

        public MenuProbe(MenuIconsPanel m, HudRoot hud)
        {
            _m = m; _hud = hud; Name = "__menu_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            if (Screens.LoadingScreen.Active != null) return;
            _frame++;
            switch (_step)
            {
                case 0 when _frame > 120:
                    _btns = RowOrder.Select(n => TopLeftVitals
                        .Find<BaseButton>(_m, n)).ToArray();
                    GD.Print($"[menu-test] {_btns.Count(b => b != null)}" +
                        $" icônes trouvées");
                    _step = 1;
                    break;
                case 1:
                    if (_i >= _btns.Length) { _step = 3; break; }
                    if (_btns[_i] == null) { _i++; break; }
                    _before = VisibleIds();
                    _btns[_i]!.EmitSignal(
                        BaseButton.SignalName.Pressed);
                    _wait = 0; _step = 2;
                    break;
                case 2 when ++_wait >= 15:
                    var fresh = VisibleIds().Where(
                        id => !_before.Contains(id)).ToList();
                    _opened.Clear();
                    foreach (var id in fresh)
                        if (GodotObject.InstanceFromId(id)
                                is Control c && !_m.IsAncestorOf(c))
                            _opened.Add(c.Name.ToString());
                    GD.Print($"[menu-test] {RowOrder[_i]} → " +
                        (_opened.Count > 0
                            ? string.Join(",", _opened.Distinct())
                            : "RIEN"));
                    // Refermeture par le même bouton (toggle natif).
                    _btns[_i]!.EmitSignal(
                        BaseButton.SignalName.Pressed);
                    _i++; _step = 1;
                    break;
                case 3 when ++_wait >= 30:
                    GetViewport().GetTexture()?.GetImage()?.SavePng(
                        $"{_shotsDir}/u7_menu.png");
                    GD.Print("[menu-test] terminé");
                    _step = 4;
                    break;
            }
        }

        private HashSet<ulong> VisibleIds()
        {
            var s = new HashSet<ulong>();
            foreach (var c in GetTree().Root
                         .FindChildren("*", "Control", true, false)
                         .Cast<Control>())
                if (c.IsVisibleInTree()) s.Add(c.GetInstanceId());
            return s;
        }
    }
}
