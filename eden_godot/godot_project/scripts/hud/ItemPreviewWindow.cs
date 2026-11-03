using System.Text.RegularExpressions;
using Godot;
using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Protocol;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// A6.7 — mini panel de prévisualisation d'item (simulateur visuel).
/// Un <see cref="SubViewport"/> 3D isolé porte le mannequin du joueur
/// (<see cref="CharVisual.BuildBody"/>, même corps que le jeu) ; les items
/// testés passent par <see cref="CharVisual.ApplyEquipment"/> (table
/// 4pequip via <see cref="CharModelSpec.LoadEquipMap"/>, skin + dye natifs)
/// et la monture par <see cref="CharVisual.BuildRideVisual"/> +
/// <see cref="RideFit"/> (même assemblage que MissionBridge.SetRideModel).
/// Aucun pipeline d'habillage dupliqué.
///
/// Entrées : CTRL+clic sur un item de l'inventaire
/// (<see cref="Preview(int)"/>), saisie d'un id d'item, saisie d'une clé de
/// monture (rideKey ridelist, ex. 12010), ou clic dans le catalogue de
/// droite (teintures, star stones, luna rocks, météores, montures —
/// noms EN de t_item, cf. knowledge/wiki-tables/misc/Useful_Info.md).
/// Un item monture (UseEnchantId → enchant 2134|rideKey, même règle que
/// le serveur) selle le mannequin. Corps construit en tâche de fond
/// (comme CharacterPreview) : l'ouverture ne gèle pas. Souris : glisser = rotation,
/// molette = zoom. Base = équipement porté (<see cref="PlayerEquipment.Worn"/>) ;
/// « Strip » = tenue de base ; « Reset » = retour au porté.
/// </summary>
public partial class ItemPreviewWindow : Control
{
    /// <summary>Singleton (posé dans _Ready) pour <see cref="Preview"/>.</summary>
    public static ItemPreviewWindow? Instance { get; private set; }

    private static readonly Regex RideCode = new(@"^r(\d{6})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int WinW = 440, WinH = 560;
    private Panel? _win;
    private SubViewport? _vp;
    private Node3D? _pivot;      // yaw utilisateur
    private Node3D? _model;      // = nœud « Model » du joueur en jeu
    private Camera3D? _cam;
    private Node3D? _body;
    private Node3D? _mountHolder;
    private Label? _status, _loading;
    private int _bodyGen;
    private readonly List<int> _queued = new();
    private string _letter = "b";
    private byte[]? _appearance;
    // Face au joueur : le modèle regarde −Z, la caméra est en +Z.
    private float _yaw = Mathf.Pi + 0.45f;
    private float _dist = 3.1f;
    private float _frameY = 0.95f;
    private bool _drag, _panDrag, _built;
    private float _panY;
    private int _rideKey;
    // loc → itemId ; items testés dans l'ordre d'ajout.
    private readonly Dictionary<int, int> _worn = new();
    private readonly List<int> _tested = new();

    public override void _Ready()
    {
        Instance = this;
        var hud = HudExtensions.FindHud();
        if (hud == null) return;
        _win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Item preview"),
            240, 60, WinW, WinH);
        _win.Name = "ItemPreviewPanel";
        AddChild(_win);
        HudWindows.Register(_win);
        DragHelper.Persist(_win, "itempreview");

        _vp = new SubViewport
        {
            OwnWorld3D = true,
            Size = new Vector2I(WinW - 20, WinH - 98),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        var cont = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = UiTextEn.Translate("Drag to rotate, right-drag to move up/down, wheel to zoom"),
        };
        HudRoot.Pin(cont, 0, 0, 0, 0, 10, 34, WinW - 10, WinH - 64);
        cont.AddChild(_vp);
        cont.GuiInput += OnViewportInput;
        _win.AddChild(cont);
        BuildScene();

        _loading = new Label
        {
            Text = UiTextEn.Translate("Loading…"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        HudRoot.Pin(_loading, 0, 0, 0, 0, 10, 34, WinW - 10, WinH - 64);
        _win.AddChild(_loading);

        _status = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        EeSkin.Shadowed(_status, 10, Color.Color8(232, 232, 228));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.ClipText = false;
        HudRoot.Pin(_status, 0, 0, 0, 0, 12, WinH - 58, WinW - 150, WinH - 10);
        _win.AddChild(_status);
        AddButton(UiTextEn.Translate("Strip"), WinW - 146, WinH - 50,
            WinW - 80, WinH - 22, Strip);
        AddButton(UiTextEn.Translate("Reset"), WinW - 74, WinH - 50,
            WinW - 10, WinH - 22, Reset);

        _win.Visible = false;
        _win.VisibilityChanged += () =>
        {
            if (_win.Visible) EnsureBody();
        };
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        base._ExitTree();
    }

    private void AddButton(string text, int x0, int y0, int x1, int y1,
        Action act)
    {
        var b = EeSkin.Grey(new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
        }, 11);
        HudRoot.Pin(b, 0, 0, 0, 0, x0, y0, x1, y1);
        b.Pressed += act;
        _win!.AddChild(b);
    }

    // ------------------------------------------------------------------
    // API publique
    // ------------------------------------------------------------------

    /// <summary>Ouvre le panel et applique <paramref name="itemId"/> au
    /// mannequin (CTRL+clic inventaire/archive). Monture si l'item porte
    /// un code ridelist ou un enchant de ride, sinon pièce d'équipement.</summary>
    public static void Preview(int itemId)
    {
        var w = Instance;
        if (w?._win == null) return;
        HudWindows.Open(w._win);
        w.EnsureBody();
        w.TryItem(itemId);
    }

    /// <summary>Item pour lequel un aperçu a un sens (équipement, costume,
    /// monture).</summary>
    public static bool Previewable(int itemId)
    {
        var d = InventoryWindow.LookupDef(itemId);
        if (d == null) return false;
        if (d.EquipLoc > 0 || d.Kind is >= 61 and <= 77) return true;
        return d.UseEnchantId > 0 &&
            Data.Tables.EnchantTable.RideKeyOf(d.UseEnchantId) > 0;
    }

    /// <summary>Menu clic droit : action principale (utiliser/équiper/
    /// retirer…) + « Aperçu sur moi » (mannequin habillé de l'item).</summary>
    public static void ShowMenu(Control anchor, int itemId,
        string primaryLabel, Action primary)
    {
        var menu = new PopupMenu();
        menu.AddItem(primaryLabel, 0);
        menu.AddItem(UiTextEn.Translate("Preview on me"), 1);
        menu.IdPressed += id =>
        {
            if (id == 0) primary();
            else Preview(itemId);
            menu.QueueFree();
        };
        menu.PopupHide += () =>
            Callable.From(() => { if (GodotObject.IsInstanceValid(menu)) menu.QueueFree(); })
                .CallDeferred();
        anchor.AddChild(menu);
        menu.Position = (Vector2I)anchor.GetGlobalMousePosition();
        menu.Popup();
    }

    /// <summary>Bouton dédié (menu système) : ouvre/ferme le panel.</summary>
    public static void Toggle()
    {
        if (Instance?._win != null) HudWindows.Toggle(Instance._win);
    }

    // ------------------------------------------------------------------
    // Scène 3D
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Color.Color8(52, 50, 58),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.7f,
        };
        _vp!.AddChild(new WorldEnvironment { Environment = env });
        _vp.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-35f, 150f, 0f),
            LightEnergy = 1.0f,
        });
        _pivot = new Node3D { Name = "Pivot" };
        _vp.AddChild(_pivot);
        // Même hiérarchie qu'en jeu : Player/Model/<visuel>.
        _model = new Node3D { Name = "Model" };
        _pivot.AddChild(_model);
        _cam = new Camera3D { Fov = 40f, Current = true };
        _vp.AddChild(_cam);
        UpdateCamera();
    }

    /// <summary>Cadre le contenu (mannequin seul ou + monture) : hauteur
    /// de visée au centre de la boîte englobante, recul selon sa plus
    /// grande dimension — le personnage remplit le panneau.</summary>
    private void Frame()
    {
        if (_model == null || !_model.IsInsideTree()) return;
        // À pied : cadrage fixe sur la silhouette complète (l'AABB des
        // meshes skinnés est peu fiable) ; monture : AABB de l'ensemble.
        if (_mountHolder == null)
        {
            _frameY = 0.85f;
            _dist = 4.2f;
            return;
        }
        Aabb? box = null;
        foreach (var n in _model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D { Mesh: not null, Visible: true } mi)
                continue;
            var b = mi.GlobalTransform * mi.GetAabb();
            box = box == null ? b : box.Value.Merge(b);
        }
        if (box is not { } bb) return;
        _frameY = bb.GetCenter().Y;
        var ext = Mathf.Max(bb.Size.Y, Mathf.Max(bb.Size.X, bb.Size.Z));
        _dist = Mathf.Clamp(ext * 1.15f / (2f * Mathf.Tan(Mathf.DegToRad(20f))),
            1.6f, 40f);
    }

    private void UpdateCamera()
    {
        if (_cam == null || _pivot == null) return;
        _pivot.Rotation = new Vector3(0f, _yaw, 0f);
        var h = _frameY + _panY;
        _cam.Position = new Vector3(0f, h + 0.15f, _dist);
        _cam.LookAt(new Vector3(0f, h, 0f), Vector3.Up);
    }

    private void OnViewportInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _drag = mb.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rb:
                _panDrag = rb.Pressed;
                break;
            case InputEventMouseMotion pm when _panDrag:
                _panY = Ui.PreviewPanMath.Apply(_panY, pm.Relative.Y);
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.WheelUp }:
                _dist = Mathf.Max(1.2f, _dist - 0.3f);
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.WheelDown }:
                _dist = Mathf.Min(40f, _dist + 0.3f);
                UpdateCamera();
                break;
            case InputEventMouseMotion mm when _drag:
                _yaw += mm.Relative.X * 0.012f;
                UpdateCamera();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Mannequin
    // ------------------------------------------------------------------

    private void EnsureBody()
    {
        if (_built || _model == null) return;
        _built = true;
        _appearance = SessionCarry.Character?.Appearance;
        _letter = CharModelSpec.BodyStem(_appearance);
        if (_loading != null) _loading.Visible = true;
        var gen = ++_bodyGen;
        var app = _appearance;
        // Hors thread principal (comme CharacterPreview) : pas de gel à
        // l'ouverture ; attache + habillage reprennent sur le main.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            Node3D? body = null;
            try
            {
                body = CharVisual.BuildBody(app);
                if (body != null)
                {
                    // Convention du jeu (PlayerController.SetVisual).
                    body.RotationDegrees = new Vector3(-90f, 180f, 0f);
                    CharVisual.PlayIdle(body);
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[itempreview] corps : {e.Message}");
            }
            Callable.From(() => AttachBody(gen, body)).CallDeferred();
        });
    }

    private void AttachBody(int gen, Node3D? body)
    {
        if (_loading != null) _loading.Visible = false;
        if (gen != _bodyGen || _model == null ||
            !GodotObject.IsInstanceValid(_model))
        {
            body?.QueueFree();
            return;
        }
        if (body == null)
        {
            GD.PrintErr("[itempreview] corps introuvable");
            _built = false;
            return;
        }
        _body = body;
        _body.SetMeta("ride_rest", _body.Transform);
        _model.AddChild(body);
        Reset();
        var q = _queued.ToList();
        _queued.Clear();
        foreach (var id in q) TryItem(id);
    }

    private static (string Code, string ModelCode)? Lookup(int itemId)
    {
        var d = InventoryWindow.LookupDef(itemId);
        return d == null ? null : (d.Code, d.ModelCode);
    }

    private void Reset()
    {
        _worn.Clear();
        foreach (var (loc, id) in PlayerEquipment.VisualWorn(
                     i => InventoryWindow.LookupDef(i)?.Kind ?? -1))
            _worn[loc] = id;
        _tested.Clear();
        _panY = 0f;
        SetMount(0);
        ApplyWorn();
        SetStatus("");
    }

    private void Strip()
    {
        _worn.Clear();
        _tested.Clear();
        ApplyWorn();
        SetStatus("");
    }

    private void ApplyWorn()
    {
        if (_body == null) return;
        CharVisual.ApplyEquipment(_body, _letter, _worn, Lookup);
        // Les attachements précédents sont libérés en fin de frame :
        // recadrage après.
        Callable.From(() => { Frame(); UpdateCamera(); }).CallDeferred();
    }

    /// <summary>EquipType S_Item → loc du conteneur d'équipement (mêmes
    /// règles que le serveur : <c>NativeEquipmentSlots</c>).</summary>
    private static int LocFor(int equipType) => equipType switch
    {
        7 or 19 or 9 => 7,
        8 => 8,
        35 => 10,
        10 => 11,
        11 or 20 => 12,
        12 => 14,
        _ => equipType,
    };

    /// <summary>Conflits de mains natifs (DoWeaponUnEquip) : 2H / arc
    /// retirent les autres armes, une main libre retire l'arc, une
    /// off-hand ne cohabite pas avec une 2H.</summary>
    private void ClearWeaponConflicts(int loc, int equipType)
    {
        bool Is2H(int l) => _worn.TryGetValue(l, out var id) &&
            InventoryWindow.LookupDef(id)?.EquipLoc == 9;
        if (equipType == 9 && loc == 7) { _worn.Remove(8); _worn.Remove(10); }
        else if (loc == 10) { _worn.Remove(7); _worn.Remove(8); }
        else if (loc == 8) { if (Is2H(7)) _worn.Remove(7); _worn.Remove(10); }
        else if (loc == 7) _worn.Remove(10);
    }

    private void TryItem(int itemId)
    {
        if (itemId <= 0) return;
        if (_body == null)
        {
            _queued.Remove(itemId);
            _queued.Add(itemId);
            SetStatus(UiTextEn.Translate("Loading…"));
            return;
        }
        var d = InventoryWindow.LookupDef(itemId);
        if (d == null) return;
        var name = TextResolver.ItemName(itemId)?.Split('\n')[0].Trim()
            ?? d.Name ?? ("#" + itemId);
        // Monture : code/modèle ridelist « r012010 », sinon enchant
        // d'usage portant 2134|rideKey (lien item → monture).
        var rk = 0;
        foreach (var c in new[] { d.Code, d.ModelCode })
        {
            var m = RideCode.Match(c ?? "");
            if (m.Success) { rk = int.Parse(m.Groups[1].Value); break; }
        }
        if (rk == 0 && d.UseEnchantId > 0)
            rk = Data.Tables.EnchantTable.RideKeyOf(d.UseEnchantId);
        if (rk > 0)
        {
            SetMount(rk);
            SetStatus(name);
            return;
        }
        if (d.EquipLoc <= 0)
        {
            SetStatus(name + " — " + UiTextEn.Translate(
                "No visual preview for this item."));
            return;
        }
        var loc = LocFor(d.EquipLoc);
        ClearWeaponConflicts(loc, d.EquipLoc);
        _worn[loc] = itemId;
        _tested.Remove(itemId);
        _tested.Add(itemId);
        ApplyWorn();
        SetStatus(name);
    }

    private void SetStatus(string t)
    {
        if (_status != null) _status.Text = t;
    }

    /// <summary>Selle le mannequin (0 = à pied) — mêmes étapes que
    /// <c>MissionBridge.SetRideModel</c> : monture enfant de « Model »,
    /// corps hissé puis suivi du siège L17 ; à pied, pose de repos
    /// restaurée (le siège réécrit tout le Transform).</summary>
    private void SetMount(int rideKey)
    {
        if (_mountHolder != null)
        {
            if (GodotObject.IsInstanceValid(_mountHolder))
            {
                _mountHolder.GetParent()?.RemoveChild(_mountHolder);
                _mountHolder.QueueFree();
            }
            _mountHolder = null;
        }
        _rideKey = 0;
        var riderAnim = _body?.FindChildren("*", "", true, false)
            .OfType<EntityAnimator>().FirstOrDefault();
        if (riderAnim != null) riderAnim.RideMount = null;
        if (_body != null && _body.HasMeta("ride_rest"))
            _body.Transform = _body.GetMeta("ride_rest").AsTransform3D();
        if (rideKey > 0 && _model != null && _body != null)
        {
            var mount = CharVisual.BuildRideVisual(rideKey);
            if (mount == null)
                GD.PrintErr($"[itempreview] pas de GLB pour rideKey={rideKey}");
            else
            {
                _mountHolder = new Node3D { Name = "_mount" };
                _mountHolder.AddChild(mount);
                _model.AddChild(_mountHolder);
                var fit = RideFit.Apply(rideKey, _mountHolder, mount);
                _body.Position = new Vector3(0f, fit.Lift, fit.BackZ);
                RideFit.Follow(_mountHolder, mount, _body);
                _rideKey = rideKey;
                // Mannequin immobile : la monture reste en « stand01 »
                // (la mesure de déplacement ferait marcher monture ET
                // cavalier quand le siège suit L17) et le cavalier copie
                // l'anim de sa monture → assis (ride01), comme en jeu.
                var mountAnim = CharVisual.MountAnimator(mount);
                if (mountAnim != null)
                {
                    mountAnim.AutoLocomotion = false;
                    mountAnim.PlayLoop("stand01");
                }
                if (riderAnim != null) riderAnim.RideMount = mountAnim;
            }
        }
        Callable.From(() => { Frame(); UpdateCamera(); }).CallDeferred();
    }
}
