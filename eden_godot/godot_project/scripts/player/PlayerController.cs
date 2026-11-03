using Godot;
using EdenClient.Combat;
using EdenClient.Game;
using EdenClient.GameInput;
using EdenClient.Net;
using EdenClient.Screens;
using EdenClient.World;

namespace EdenClient.Player;

/// <summary>
/// Contrôleur joueur — contrôles fidèles au client EE
/// (docs/GAMEPLAY-NOTES.md) :
///
///   - <b>clic gauche + drag</b> : orbite caméra seule ;
///   - <b>clic droit + drag</b> : orbite caméra ET le modèle tourne
///     vers l'avant horizontal caméra (steering, TurnSelf au relâchement) ;
///   - <b>clic gauche sol</b> : marche vers le point + <see cref="MoveMarker"/> ;
///     clic gauche entité : sélection (TargetUpdate 124) ;
///   - <b>clic droit NPC</b> : approche ≤ <see cref="TalkRange"/> →
///     TalkToNpc (NCID 10) ;
///   - <b>clic droit mob</b> : <see cref="AutoAttack"/> (approche +
///     boucle NCID 5) ;
///   - WASD : déplacement direct relatif caméra (annule clic-sol ET
///     auto-attack) ; molette : zoom ;
///   - <b>espace</b> (<c>eden_jump</c>) : saut + double saut purement
///     local (cosmétique — aucune sync NCID 581, non prouvée natif).
///
/// Distinction clic/drag : seuil <see cref="DragThresholdPx"/> —
/// au-delà c'est un drag d'orbite, en dessous un clic au relâchement.
///
/// Picking entité : projection écran (<see cref="PickCore"/>) des
/// entités de <see cref="Registry"/> (« NetEntities », découvert
/// automatiquement — GameScene l'ajoute après le joueur).
///
/// API réseau : <see cref="SessionCarry.Session"/> — mode
/// <c>--live</c> direct non couvert (session privée de GameScene).
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    /// <summary>Multiplicateur de vitesse issu du SERVEUR (296 Speeds[0],
    /// monture/buffs inclus) — aucun calcul local, voir
    /// <see cref="EdenClient.Game.ServerMoveSpeed"/>.</summary>
    public static float MoveSpeedMul =>
        EdenClient.Game.ServerMoveSpeed.Multiplier(
            EdenClient.Protocol.PlayerVitals.TotalStats?.Speeds);

    /// <summary>Vitesse de déplacement (unités/s).</summary>
    [Export] public float MoveSpeed { get; set; } = 6.0f;
    /// <summary>Vitesse de course (shift) — 0 = désactivée.</summary>
    [Export] public float RunSpeed { get; set; } = 10.0f;
    /// <summary>Distance d'arrêt du point&amp;click.</summary>
    [Export] public float ArriveDistance { get; set; } = 0.3f;
    /// <summary>Amplitude caméra min/max (SpringArm).</summary>
    [Export] public float ZoomMin { get; set; } = 3f;
    /// <summary>Amplitude caméra max.</summary>
    [Export] public float ZoomMax { get; set; } = 18f;
    /// <summary>Sensibilité orbitale (rad/px).</summary>
    [Export] public float OrbitSensitivity { get; set; } = 0.008f;
    /// <summary>Seuil px distinguant clic et drag.</summary>
    [Export] public float DragThresholdPx { get; set; } = 6f;
    /// <summary>Portée d'interaction NPC (u).</summary>
    [Export] public float TalkRange { get; set; } = 3.0f;
    /// <summary>Rayon de picking entité (px).</summary>
    [Export] public float PickRadiusPx { get; set; } = 36f;
    /// <summary>Impulsion verticale du saut (u/s). Avec
    /// <see cref="JumpGravity"/> = 19 u/s² : hauteur de saut ≈ v²/2g
    /// ≈ 1,3 u (le perso fait ~1,7 u) — feel MMO arcade, ni flottant
    /// ni anémique. Échelle monde : GroundHeight est en unités monde
    /// et le capsule collider fait 1,7 u.</summary>
    [Export] public float JumpSpeed { get; set; } = 7.0f;
    /// <summary>Gravité appliquée en vol (u/s²).</summary>
    [Export] public float JumpGravity { get; set; } = 19f;
    /// <summary>Impulsions par envol (2 = double saut — miroir du
    /// « jump_feature_max » Reforged ; réinitialisé à
    /// l'atterrissage).</summary>
    [Export] public int MaxJumps { get; set; } = 2;

    /// <summary>Hauteur du sol (godotX, godotZ) → y, null hors carte.
    /// Injectée par la scène (TerrainSampler via MapLoader).</summary>
    public Func<float, float, float?>? GroundHeight;

    /// <summary>Murs invisibles natifs (.smp) : (x, z Godot) → case
    /// bloquée. Mouvement refusé vers une case bloquée (glissement par
    /// axe) sauf si la position courante l'est déjà (sortie libre).</summary>
    public Func<float, float, bool>? Blocked;

    /// <summary>Registre des entités réseau — assigné par découverte
    /// automatique du sibling « NetEntities » ou par l'appelant.</summary>
    public EntityRegistry? Registry { get; set; }

    /// <summary>Animator du visuel courant (posé par SetVisual quand
    /// le corps embarque un <see cref="EntityAnimator"/>).</summary>
    public EntityAnimator? Animator { get; private set; }

    /// <summary>Animator de la monture courante (null = à pied) :
    /// le corps passe en pose/anims cavalier (ride01-08).</summary>
    public EntityAnimator? MountAnimator
    {
        get => _mountAnimator;
        set
        {
            _mountAnimator = value;
            if (Animator != null) Animator.RideMount = value;
        }
    }
    private EntityAnimator? _mountAnimator;

    /// <summary>Émis quand un déplacement/annulation doit couper les
    /// actions en cours (auto-attack, approche NPC).</summary>
    public event Action? Cancelled;

    /// <summary>Entité sous le curseur (hover — curseur contextuel).</summary>
    public RemoteEntity? HoveredEntity { get; private set; }

    private Node3D? _model;
    private Node3D? _camYaw;
    private Node3D? _camPitch;
    private SpringArm3D? _spring;
    private Camera3D? _camera;
    private MoveMarker? _marker;
    private AutoAttack? _autoAttack;
    private Vector3? _moveTarget;
    private float _yaw;
    // Positive elevation places the third-person camera above the
    // pivot. The old -0.5 rad put it below the player's feet; the
    // terrain then filled the view from its underside.
    private float _pitch = 0.45f;
    private float _camDbgT = OS.GetCmdlineArgs()
        .Concat(OS.GetCmdlineUserArgs()).Contains("--camdbg") ? 1f : -1f;

    // --- état souris ---
    private bool _lDown, _rDown, _mDown;
    private bool _lDrag, _rDrag;
    private Vector2 _pressPos;
    // Grâce UI (s) — survit aux frames où GuiGetHoveredControl() rend
    // null pendant la reconstruction d'un panel (curseur + clics).
    private float _uiGrace;

    // --- interaction NPC en cours ---
    private RemoteEntity? _talkTarget;
    private readonly CancellationTokenSource _cts = new();
    private float _steerYawBefore = float.NaN;

    /// <summary>Auto-attaque (null jusqu'à _Ready).</summary>
    public AutoAttack? AutoAttacker => _autoAttack;

    public override void _Ready()
    {
        EnsureInputActions();
        _model = new Node3D { Name = "Model" };
        var capsule = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        };
        _model.AddChild(capsule);
        AddChild(_model);

        // Collider joueur : capsule (les murs viennent des helpers
        // « collision » des GLB — CollisionBuilder). Le corps reste
        // kinematic : le déplacement passe par MoveAndCollide
        // (_PhysicsProcess) et Y est snappé sur le terrain.
        AddChild(new CollisionShape3D
        {
            Name = "BodyShape",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.7f },
            Position = new Vector3(0f, 0.85f, 0f),
        });

        // Ombre « blob » au sol (Decal — visible en jeu, suit le
        // relief via la projection ; ~0,6 u au-dessus des pieds).
        var shadow = World.BlobShadow.Make(0.8f);
        shadow.Position = new Vector3(0f, 0.6f, 0f);
        AddChild(shadow);

        _camYaw = new Node3D { Name = "CamYaw" };
        _camPitch = new Node3D { Name = "CamPitch" };
        _spring = new SpringArm3D
        {
            Name = "SpringArm",
            // Réf TailleMob.png : le perso natif occupe ~12,5 % de la
            // hauteur écran au zoom par défaut → ~11 u à FOV 75° ;
            // 6,5 u le rendait ~1,9× trop gros (monde perçu géant).
            SpringLength = 11f,
            Margin = 0.3f,
            // Walls (1) + floors (2) des helpers GLB : l'arm rétracte
            // la caméra au lieu de traverser la géométrie.
            CollisionMask = World.WalkableGround.WallLayer |
                            World.WalkableGround.FloorLayer,
        };
        // La capsule du joueur est en layer 1 (= WallLayer) : sans
        // exclusion le cast de l'arm la frappe → caméra collée au crâne.
        _spring.AddExcludedObject(GetRid());
        _camera = new Camera3D { Name = "Camera3D", Current = true };
        _spring.AddChild(_camera);
        _camPitch.AddChild(_spring);
        _camYaw.AddChild(_camPitch);
        _camYaw.Position = new Vector3(0f, 1.6f, 0f);
        AddChild(_camYaw);
        _camPitch.Rotation = new Vector3(_pitch, 0f, 0f);

        _marker = new MoveMarker { Name = "MoveMarker" };
        // Le marqueur vit en monde (pas enfant du joueur).
        AddSibling(_marker);
        _marker.Visible = false;

        _autoAttack = new AutoAttack { Name = "AutoAttack" };
        AddChild(_autoAttack);
    }

    /// <summary>Position monde Godot de la caméra (debug).</summary>
    public Camera3D? Camera => _camera;

    /// <summary>Fixe la cible point&amp;click (coords Godot au sol).</summary>
    public void SetMoveTarget(Vector3 target) => _moveTarget = target;

    private readonly Queue<Vector3> _path = new();
    private Vector3? _pathHead;

    /// <summary>Planificateur de chemin (A* sur grille .smp dilatée),
    /// posé par GameScene : (départ, arrivée) Godot → waypoints sans le
    /// départ ; null = pas de grille / pas de chemin.</summary>
    public Func<Vector3, Vector3, List<Vector3>?>? Planner;

    /// <summary>Blocage en marche auto (route A*) : (position, cap
    /// unitaire Godot). Posé par AutoRoute → liste noire + replan.</summary>
    public Action<Vector3, Vector3>? Stuck;
    /// <summary>Grille .smp courante (pour la liste noire de route).</summary>
    public Func<NodeMapFile?>? NodeMapForRoute;
    private float _stuckT;
    private Vector3 _lastPhysPos;

    private bool AutoPathActive =>
        _pathHead.HasValue && _moveTarget.HasValue &&
        _moveTarget == _pathHead;

    /// <summary>Vitesse réelle &lt;&lt; attendue pendant 0,6 s en
    /// marche auto → événement <see cref="Stuck"/>.</summary>
    private void DetectStuck(float dt, Vector2 moveDir, float speed)
    {
        var pos = GlobalPosition;
        var moved = new Vector2(pos.X - _lastPhysPos.X,
            pos.Z - _lastPhysPos.Z).Length();
        _lastPhysPos = pos;
        if (moveDir == Vector2.Zero || !AutoPathActive || _keyMove)
        {
            _stuckT = 0f;
            return;
        }
        var expected = speed * MoveSpeedMul * dt;
        if (expected > 1e-4f && moved < expected * 0.25f)
            _stuckT += dt;
        else
            _stuckT = 0f;
        if (_stuckT < 0.6f) return;
        _stuckT = 0f;
        var dir = new Vector3(moveDir.X, 0f, moveDir.Y);
        GD.Print($"[route] bloqué en ({pos.X:F1},{-pos.Z:F1}) " +
                 $"cap=({dir.X:F2},{-dir.Z:F2}) : {DescribeBlocker(dir)}");
        Stuck?.Invoke(pos, dir);
    }

    /// <summary>Diag : collider qui bloque le cap (nom du nœud + de ses
    /// parents, layer, forme) — identifie les murs invisibles.</summary>
    private string DescribeBlocker(Vector3 dir)
    {
        var res = new PhysicsTestMotionResult3D();
        var prm = new PhysicsTestMotionParameters3D
        { From = GlobalTransform, Motion = dir * 0.4f };
        if (!PhysicsServer3D.BodyTestMotion(GetRid(), prm, res))
            return "aucun collider physique (mur grille/hors-sol)";
        var id = res.GetColliderId();
        var n = GodotObject.InstanceFromId(id) as Node;
        var path = "";
        for (var k = 0; k < 4 && n != null; k++, n = n.GetParent())
            path += n.Name + "<";
        var layer = (GodotObject.InstanceFromId(id) as CollisionObject3D)
            ?.CollisionLayer ?? 0;
        var free = "";
        for (var k = 0; k < 8; k++)
        {
            var a = k * Mathf.Pi / 4f;
            var pr = new PhysicsTestMotionParameters3D
            {
                From = GlobalTransform,
                Motion = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.4f,
            };
            free += PhysicsServer3D.BodyTestMotion(GetRid(), pr,
                new PhysicsTestMotionResult3D()) ? "x" : "o";
        }
        return $"y={GlobalPosition.Y:F1} dirs(E,SE,S,SW,W,NW,N,NE)={free} " +
               $"n=({res.GetCollisionNormal().X:F2},{res.GetCollisionNormal().Y:F2},{res.GetCollisionNormal().Z:F2}) " +
               $"depth={res.GetCollisionDepth():F2} {path} layer={layer} pt=({res.GetCollisionPoint().X:F1}," +
               $"{-res.GetCollisionPoint().Z:F1},h={res.GetCollisionPoint().Y:F1})";
    }

    /// <summary>Marche le long de <paramref name="pts"/> : cible = pts[0],
    /// la suivante prise à l'arrivée. File invalidée dès que la cible
    /// courante change (clic, clavier, mur…).</summary>
    public void SetMovePath(IReadOnlyList<Vector3> pts)
    {
        _path.Clear();
        if (pts.Count == 0) return;
        for (var i = 1; i < pts.Count; i++) _path.Enqueue(pts[i]);
        _moveTarget = pts[0];
        _pathHead = pts[0];
    }

    /// <summary>Efface la cible point&amp;click (arrêt net).</summary>
    public void ClearMoveTarget()
    {
        _moveTarget = null;
        _marker?.Hide();
    }

    /// <summary>Cible point&amp;click courante (null si arrêté/WASD).</summary>
    public Vector3? MoveTarget => _moveTarget;

    /// <summary>Cap du visuel (yaw du modèle — radians autour de Y,
    /// convention « −Z = avant »).</summary>
    public float ModelYaw => _model?.Rotation.Y ?? 0f;

    /// <summary>Visuel courant posé par <see cref="SetVisual"/>
    /// (capsule incluse) — sert au portrait HUD (duplicata).</summary>
    public Node3D? Visual => _model?.GetChildCount() > 0
        ? _model.GetChildOrNull<Node3D>(0) : null;

    // ---- barre HP au-dessus de la tête (soi) ----------
    // Pseudo propre : derrière la préférence « My Name » (défaut off) ;
    // la barre dépend de Préférences > Cible.
    private Hud.HpBar3D? _npBar;
    private Label3D? _npName;
    private string _ownName = "";

    /// <summary>Pose la barre HP du joueur local. Le nom n'est
    /// volontairement pas affiché au-dessus de soi.</summary>
    public void SetNameplate(string name)
    {
        if (name.Length > 0)
        {
            _ownName = name;
            if (_npName != null) _npName.Text = name;
        }
        if (_npBar != null) return;
        _npBar = new Hud.HpBar3D(Hud.HudPrefs.BarRole.Self,
            new Vector2(1.1f, 0.12f))
        {
            FixedFill = new Color(0.85f, 0.15f, 0.2f),
            Position = new Vector3(0f, 1.92f, 0f),
        };
        AddChild(_npBar);
        _npName = new Label3D
        {
            Name = "OwnName",
            Text = _ownName,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0f, 2.1f, 0f),
            FontSize = 40,
            PixelSize = 0.006f,
            OutlineSize = 10,
            Modulate = new Color(0.55f, 0.95f, 1f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
            Visible = Hud.HudPrefs.ShowOwnName,
        };
        AddChild(_npName);
        Hud.HudPrefs.Changed += () =>
        {
            if (_npName != null && IsInstanceValid(_npName))
                _npName.Visible = Hud.HudPrefs.ShowOwnName;
        };
    }

    /// <summary>Bulle de dialogue 3D (268 ClientSay) au-dessus de soi
    /// (ref-shots/shot_0082 : bulle « bonjour » sur le perso).</summary>
    public void ShowBubble(string text) =>
        Scene.SpeechBubble3D.Show(this, text, 2.1f + 0.5f);

    /// <summary>Ratio HP pour la barre au-dessus de soi.</summary>
    public void SetNameplateHp(long hp, long maxHp)
    {
        // Source de vérité = HudPrefs.SelfHp/SelfMaxHp (poussés par
        // HudRoot.SetVitals, lus dans _Process) — pas de double écriture.
    }

    /// <summary>Cap caméra horizontal (yaw du pivot — radians).</summary>
    public float CamYaw => _yaw;

    /// <summary>Force le cap caméra (degrés Godot) — --tele=…,yaw vise
    /// avant un --walk-test : la marche suit _yaw, pas la rotation du
    /// joueur. Le modèle est aligné pour cohérence.</summary>
    public void SetCamYawDeg(float deg)
    {
        _yaw = Mathf.DegToRad(deg);
        ApplyCamAngles();
        if (_model != null)
            _model.Rotation = new Vector3(0f, _yaw, 0f);
    }

    /// <summary>Oriente le visuel vers un point monde
    /// (lerp <paramref name="factor"/> ∈ ]0,1]).</summary>
    public void FaceTowards(Vector3 worldPos, float factor)
    {
        if (_model == null) return;
        var d = worldPos - GlobalPosition;
        if (Mathf.Abs(d.X) + Mathf.Abs(d.Z) < 1e-4f) return;
        var targetYaw = Mathf.Atan2(-d.X, -d.Z);
        var cur = _model.Rotation.Y;
        var dd = Mathf.Wrap(targetYaw - cur, -Mathf.Pi, Mathf.Pi);
        _model.Rotation = new Vector3(0f, cur + dd * factor, 0f);
    }

    /// <summary>Remplace le visuel capsule par un modèle (GLB corps
    /// du perso). Recherche un <see cref="EntityAnimator"/> embarqué
    /// (attaché par <see cref="CharVisual.AttachAnimator"/>).</summary>
    public void SetVisual(Node3D? visual)
    {
        if (_model == null) return;
        // RemoveChild avant le QueueFree différé : un ancien visuel
        // encore dans l'arbre conserve son nom jusqu'à la fin de
        // frame et AddChild auto-renommerait le nouveau
        // (« @Node3D@NNNN » — même bug que RemoteEntity.SetVisual).
        foreach (var c in _model.GetChildren())
        {
            _model.RemoveChild(c);
            c.QueueFree();
        }
        Animator = null;
        _sitting = false;   // nouvel animator : l'assise tombe
        if (visual != null)
        {
            // GLB corps en coords natives Z-up → Y-up Godot + 180°/Y :
            // le modèle converti fait face +Z Godot (H9 — le perso
            // avançait dos à la direction de marche) ; le yaw du
            // visuel reste la convention « −Z = avant » (même
            // convention que RemoteEntity.SetVisual).
            visual.RotationDegrees = new Vector3(-90f, 180f, 0f);
            _model.AddChild(visual);
            if (_weaponsHidden) ApplyWeaponVisibility();
            // Type C# filtré en LINQ : FindChildren(typeName) ne
            // résout que les classes natives (bug documenté
            // DragHelper.EnsureTitleBar) — l'animator embarqué du
            // corps perso n'était sinon jamais retrouvé.
            foreach (var a in visual
                         .FindChildren("*", "", true, false)
                         .OfType<EntityAnimator>())
            {
                Animator = a;
                a.RideMount = MountAnimator;
                break;
            }
        }
        else
        {
            _model.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
                Position = new Vector3(0f, 0.9f, 0f),
            });
        }
        if (OS.GetCmdlineArgs().Contains("--meshdbg"))
            foreach (var mi in _model
                         .FindChildren("*", "MeshInstance3D", true, false)
                         .Cast<MeshInstance3D>())
            {
                if (!mi.Visible) continue;
                var mat = mi.GetSurfaceOverrideMaterial(0)
                    ?? mi.Mesh?.SurfaceGetMaterial(0);
                var tex = mat is StandardMaterial3D sm
                    ? sm.AlbedoTexture : null;
                GD.Print($"[meshdbg] vis {mi.Name} mat=" +
                    $"{mat?.GetType().Name} tex=" +
                    $"{tex?.ResourcePath ?? tex?.GetInstanceId().ToString()} " +
                    $"aabb={(mi.Mesh != null ? mi.GetAabb().Size.ToString() : "?")}");
            }
    }

    // ------------------------------------------------------------------
    // Input — machine clic/drag par bouton
    // ------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                HandleMouseMotion(mm);
                break;
        }
    }

    /// <summary>H2 : un Control du HUD est survolé → la molette ne
    /// doit pas zoomer la caméra. La grâce couvre les frames où un
    /// panel reconstruit laisse GuiGetHoveredControl() à null — sinon
    /// les clics et le curseur traversent vers le monde derrière.</summary>
    private bool OverHud() => _uiGrace > 0f;

    private bool PointerOverUi()
    {
        var gui = GetViewport().GuiGetHoveredControl();
        return gui != null && gui.IsVisibleInTree() &&
            gui.MouseFilter != Control.MouseFilterEnum.Ignore;
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp when mb.Pressed:
                if (!OverHud()) Zoom(-1f);
                break;
            case MouseButton.WheelDown when mb.Pressed:
                if (!OverHud()) Zoom(1f);
                break;
            case MouseButton.Left:
                if (mb.Pressed)
                {
                    // Appui sur du HUD (contrôle passant, grab strip
                    // armé) → ni drag caméra ni clic-monde.
                    if (OverHud() || Hud.HudLayout.GrabArmed) break;
                    // Clic monde : relâcher le focus clavier (chat/bouton),
                    // sinon WASD/hotkeys restent bloqués.
                    GetViewport().GuiReleaseFocus();
                    _lDown = true; _lDrag = false;
                    _pressPos = mb.Position;
                }
                else
                {
                    if (!_lDown) break;
                    _lDown = false;
                    if (!_lDrag && !OverHud()) OnLeftClick(mb.Position);
                    _lDrag = false;
                    UpdateMouseMode();
                }
                break;
            case MouseButton.Right:
                if (mb.Pressed)
                {
                    if (OverHud() || Hud.HudLayout.GrabArmed) break;
                    GetViewport().GuiReleaseFocus();
                    _rDown = true; _rDrag = false;
                    _pressPos = mb.Position;
                }
                else
                {
                    if (!_rDown) break;
                    _rDown = false;
                    if (_rDrag) EndSteer();
                    else if (!OverHud()) OnRightClick(mb.Position);
                    _rDrag = false;
                    UpdateMouseMode();
                }
                break;
            case MouseButton.Middle:
                if (mb.Pressed && (OverHud() || Hud.HudLayout.GrabArmed))
                    break;
                _mDown = mb.Pressed;
                UpdateMouseMode();
                break;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        // Hygiène : release consommé par le GUI → états coincés.
        if (_lDown && !mm.ButtonMask.HasFlag(MouseButtonMask.Left))
        { _lDown = false; _lDrag = false; }
        if (_rDown && !mm.ButtonMask.HasFlag(MouseButtonMask.Right))
        { _rDown = false; _rDrag = false; }
        // Bascule clic → drag au-delà du seuil.
        if (_lDown && !_lDrag && (mm.Position - _pressPos).Length()
                >= DragThresholdPx)
            _lDrag = true;
        if (_rDown && !_rDrag && (mm.Position - _pressPos).Length()
                >= DragThresholdPx)
        {
            _rDrag = true;
            _steerYawBefore = ModelYaw;
        }
        if (_lDrag || _rDrag || _mDown)
        {
            UpdateMouseMode();
            var mouse = Hud.HudPrefs.MouseSpeed;   // pref « Mouse »
            _yaw -= mm.Relative.X * OrbitSensitivity * mouse;
            _pitch = Mathf.Clamp(
                _pitch - mm.Relative.Y * OrbitSensitivity * mouse *
                    (Hud.HudPrefs.InvertCamera ? -1f : 1f),
                -0.6f, 1.2f);
            ApplyCamAngles();
            // Steering : le corps suit l'avant horizontal caméra.
            if (_rDrag && _model != null)
                _model.Rotation = new Vector3(0f, _yaw, 0f);
        }
    }

    private void UpdateMouseMode()
    {
        // Le curseur reste VISIBLE pendant les drags caméra — ni
        // capture (disparition) ni téléportation au relâchement.
        // Les deltas de mm.Relative continuent de porter la rotation.
        if (Input.MouseMode != Input.MouseModeEnum.Visible)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    // ------------------------------------------------------------------
    // Clics résolus

    /// <summary>Clic gauche : entité sous le curseur → sélection ;
    /// sinon sol → marche + marqueur.</summary>
    /// <summary>H5 : cible posée localement (cercle + cadre) en plus de
    /// l'envoi serveur — le 444 de retour ne fait que confirmer.</summary>
    public static event Action<uint>? LocalTargeted;

    private void SelectTarget(RemoteEntity ent)
    {
        Send(new Protocol.ZoneTargetUpdate { Value = (int)ent.Handle });
        LocalTargeted?.Invoke(ent.Handle);
    }

    private void OnLeftClick(Vector2 screenPos)
    {
        var ent = PickEntity(screenPos);
        if (ent != null)
        {
            // Sélection de la cible côté serveur (124 → 444).
            SelectTarget(ent);
            GD.Print($"[input] select h=0x{ent.Handle:x8} " +
                $"«{ent.DisplayName}»");
            return;
        }
        // Pref « Left-click to route » : off → le sol ne déplace plus.
        if (Hud.HudPrefs.LeftClickRoute &&
            TryPickGround(screenPos, out var hit))
        {
            Cancelled?.Invoke();
            _talkTarget = null;
            _moveTarget = hit;
            _marker?.ShowAt(hit);
        }
    }

    /// <summary>Clic droit : NPC → approche + TalkToNpc ; mob →
    /// auto-attack ; joueur → sélection ; sol → rien (le clic-sol
    /// droit n'existe pas dans EE).</summary>
    private void OnRightClick(Vector2 screenPos)
    {
        var ent = PickEntity(screenPos);
        if (ent == null)
        {
            // Clic droit dans le vide (sans drag) : désélection (124 Value=0
            // → écho 444 qui vide le targetframe).
            Send(new Protocol.ZoneTargetUpdate { Value = 0 });
            LocalTargeted?.Invoke(0);
            return;
        }
        Cancelled?.Invoke();
        _talkTarget = null;
        if (ent.Kind == 8)
        {
            // NPC : approche à portée puis talk.
            SelectTarget(ent);
            _talkTarget = ent;
            GD.Print($"[input] talk→npc h=0x{ent.Handle:x8} " +
                $"«{ent.DisplayName}»");
        }
        else if (ent.Kind == 12)
        {
            _autoAttack?.Engage(ent);
        }
        else
        {
            SelectTarget(ent);
        }
    }

    // ------------------------------------------------------------------
    // Actions clavier réglables (Settings > Hotkeys) — appelées par
    // SettingsApplier.Dispatch.

    private bool _sitting;
    private bool _weaponsHidden;
    /// <summary>Armes rangées (Toggle WPN) — réappliqué par le pilote de
    /// prefs car les équipements s'attachent après SetVisual.</summary>
    public bool WeaponsHidden => _weaponsHidden;
    private float _lastLoggedMul = -1f;
    private string _idleBeforeSit = "stand01";

    /// <summary>Hotkeys « Party Member 1..10 » : cible le membre
    /// <paramref name="index"/> (0-based, ordre de slot du roster) s'il
    /// est visible dans la zone. true = cible envoyée.</summary>
    public bool TargetPartyMember(int index)
    {
        var members = Protocol.PartyState.Members.Values
            .OrderBy(m => m.Slot).ToList();
        if (index < 0 || index >= members.Count) return false;
        var h = members[index].CharId;
        var self = (uint)(SessionCarry.Character?.Id ?? 0);
        if (h == self || (Registry != null &&
            Registry.Entities.TryGetValue(h, out var e) &&
            GodotObject.IsInstanceValid(e) && !e.Dying))
        {
            Send(new Protocol.ZoneTargetUpdate { Value = (int)h });
            LocalTargeted?.Invoke(h);
            return true;
        }
        return false;
    }

    /// <summary>Hotkey « Sit / Stand » : pose assise locale (anim
    /// sit01 tenue comme idle ; le serveur n'a pas de message
    /// d'assise → visuel local seulement, auto-relevé au mouvement).</summary>
    public bool ToggleSit()
    {
        if (Animator == null || _airborne) return false;
        if (_sitting) { StandUp(); return true; }
        if (!Animator.Has("sit01"))
            return false;
        _sitting = true;
        _idleBeforeSit = Animator.IdleAnim;
        Animator.IdleAnim = "sit01";
        _autoAttack?.Cancel();
        return true;
    }

    private void StandUp()
    {
        if (!_sitting) return;
        _sitting = false;
        if (Animator != null) Animator.IdleAnim = _idleBeforeSit;
    }

    /// <summary>Hotkey « Toggle WPN » : range/sort les armes en main
    /// (BoneAttachment Prop1/Prop2 du corps).</summary>
    public bool ToggleWeapons()
    {
        _weaponsHidden = !_weaponsHidden;
        ApplyWeaponVisibility();
        return true;
    }

    /// <summary>Applique l'état « arme rangée » aux attaches d'armes du
    /// corps courant.</summary>
    public void ApplyWeaponVisibility()
    {
        var vis = Visual;
        if (vis == null) return;
        foreach (var n in vis.FindChildren("__equip_ba_Prop*", "", true,
                     false))
            if (n is Node3D n3) n3.Visible = !_weaponsHidden;
    }

    /// <summary>Hotkey « Attack » : engage le mob ciblé, sinon le mob
    /// vivant le plus proche à ≤ 12 u (hors familiers).</summary>
    public bool AttackTargetOrNearest()
    {
        var reg = Registry;
        if (reg == null || _autoAttack == null) return false;
        RemoteEntity? best = null;
        var tgt = Hud.HudExtensions.FindHud()?.Target?.TargetHandle ?? 0;
        if (tgt != 0 && reg.Entities.TryGetValue(tgt, out var t) &&
            GodotObject.IsInstanceValid(t) && t.Kind == 12 && !t.Dying)
            best = t;
        else
        {
            var bd = 12f * 12f;
            foreach (var (_, e) in reg.Entities)
            {
                if (!GodotObject.IsInstanceValid(e) || e.Kind != 12 ||
                    e.Dying || e.IsElfPet) continue;
                var d = (e.GlobalPosition - GlobalPosition)
                    .LengthSquared();
                if (d < bd) { bd = d; best = e; }
            }
        }
        if (best == null) return false;
        StandUp();
        Cancelled?.Invoke();
        _talkTarget = null;
        _autoAttack.Engage(best);
        return true;
    }

    /// <summary>Pref « Camera Follow » : recale doucement le yaw de
    /// la caméra vers le cap de marche quand on avance (pas en
    /// marche arrière, pas pendant un drag caméra).</summary>
    private void FollowCamera(Vector2 moveDir, float dt)
    {
        if (!Hud.HudPrefs.CameraFollow || _lDrag || _rDrag || _mDown)
            return;
        var dir = moveDir.Normalized();
        var fwd = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
        if (dir.Dot(fwd) < 0.2f) return;
        var heading = Mathf.Atan2(-dir.X, -dir.Y);
        _yaw = Mathf.LerpAngle(_yaw, heading,
            1f - Mathf.Exp(-2.5f * dt));
        ApplyCamAngles();
    }

    /// <summary>Fin du steering droit : envoie TurnSelf (123) si le
    /// cap a changé (le serveur le rebroadcast en 441).</summary>
    private void EndSteer()
    {
        if (float.IsNaN(_steerYawBefore) ||
            Mathf.Abs(Mathf.Wrap(ModelYaw - _steerYawBefore,
                -Mathf.Pi, Mathf.Pi)) < 0.02f)
            return;
        _steerYawBefore = float.NaN;
        // dir Eden : même formule que GameScene.MaybeSendMove.
        var dir = EdenClient.Game.EdenDir.FromYaw(ModelYaw);
        Send(new Protocol.ZoneTurnSelf { Value = dir });
    }

    // ------------------------------------------------------------------
    // Picking

    /// <summary>Entité sous le curseur : projection écran de chaque
    /// RemoteEntity (+1.1 u ≈ torse) dans un rayon <see cref="PickRadiusPx"/>.
    /// Préférence mob/NPC si ex æquo (le pick le plus proche gagne).</summary>
    private RemoteEntity? PickEntity(Vector2 screenPos)
    {
        var cam = _camera;
        var reg = Registry;
        if (cam == null || reg == null) return null;
        // Raycast physique sur les capsules de picking (H11 — rayon
        // ~1 u, tolérant) : la hitbox n'est plus le mesh précis.
        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            var origin = cam.ProjectRayOrigin(screenPos);
            var dir = cam.ProjectRayNormal(screenPos);
            var q = PhysicsRayQueryParameters3D.Create(origin,
                origin + dir * 500f, RemoteEntity.PickLayerMask);
            // PickArea is an Area3D, not a physics body. The query
            // default excludes areas, so the previous "wide capsule"
            // was silently ignored and picking fell back to a tiny
            // projected screen radius.
            q.CollideWithAreas = true;
            q.CollideWithBodies = false;
            var hit = space.IntersectRay(q);
            if (hit.Count > 0 &&
                hit["collider"].AsGodotObject() is Area3D area &&
                area.GetMeta("pick_entity").AsGodotObject()
                    is RemoteEntity picked && !picked.Dying)
                return picked;
        }
        var pts = new List<(float X, float Y)>();
        var ents = new List<RemoteEntity>();
        foreach (var (_, e) in reg.Entities)
        {
            if (!GodotObject.IsInstanceValid(e) || !e.IsInsideTree())
                continue;
            if (e.Dying) continue;
            var sp = cam.UnprojectPosition(
                e.GlobalPosition + new Vector3(0f, 1.1f, 0f));
            ents.Add(e);
            pts.Add((sp.X, sp.Y));
        }
        var idx = PickCore.NearestInRadius(pts,
            screenPos.X, screenPos.Y, PickRadiusPx);
        return idx >= 0 ? ents[idx] : null;
    }

    private void Zoom(float delta)
    {
        if (_spring == null) return;
        _spring.SpringLength = Mathf.Clamp(
            _spring.SpringLength + delta, ZoomMin, ZoomMax);
    }

    private float _dbgYaw;
    private bool _dbgYawOn;

    /// <summary>Caméra de debug (RideDebug) : yaw/pitch en degrés,
    /// distance et hauteur du pivot, hors bornes de zoom.</summary>
    public void DebugCam(float yawDeg, float pitchDeg, float dist, float h)
    {
        // Yaw relatif au cap du modèle : 0 = derrière, 90 = de côté.
        _dbgYaw = Mathf.DegToRad(yawDeg);
        _dbgYawOn = true;
        _yaw = ModelYaw + _dbgYaw;
        _pitch = Mathf.DegToRad(pitchDeg);
        if (_npBar != null) _npBar.Visible = false;
        if (_npName != null) _npName.Visible = false;
        if (_spring != null)
        {
            _spring.SpringLength = dist;
            _spring.CollisionMask = 0;
        }
        if (_camYaw != null) _camYaw.Position = new Vector3(0f, h, 0f);
        ApplyCamAngles();
    }

    private void ApplyCamAngles()
    {
        if (_camYaw != null)
            _camYaw.Rotation = new Vector3(0f, _yaw, 0f);
        if (_camPitch != null)
            _camPitch.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    /// <summary>Raycast caméra→terrain par marche + dichotomie sur
    /// <see cref="GroundHeight"/> (espace Godot).</summary>
    private bool TryPickGround(Vector2 screenPos, out Vector3 hit)
    {
        hit = default;
        var cam = _camera;
        if (cam == null || GroundHeight == null) return false;
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);

        // f(t) = y(t) − hauteur(x(t), z(t)).
        float F(float t)
        {
            var p = origin + dir * t;
            var h = GroundHeight(p.X, p.Z);
            return h.HasValue ? p.Y - h.Value : float.NaN;
        }

        var prevT = 0f;
        for (var t = 0.5f; t <= 500f; t += 0.5f)
        {
            var f = F(t);
            if (float.IsNaN(f)) continue;
            if (f <= 0f)
            {
                var lo = prevT;
                var hi = t;
                for (var i = 0; i < 24; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (F(mid) > 0f) lo = mid; else hi = mid;
                }
                hit = origin + dir * ((lo + hi) * 0.5f);
                var gh = GroundHeight(hit.X, hit.Z);
                if (gh.HasValue) hit = hit with { Y = gh.Value };
                return true;
            }
            prevT = t;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Process

    /// <summary>Applique les murs invisibles .smp au déplacement :
    /// destination bloquée → essai par axe (glissement), sinon rien.
    /// Position courante déjà bloquée → libre (jamais coincé).</summary>
    private ulong _nodeMapLogMs;
    private Vector3 ClipByNodeMap(Vector3 motion)
    {
        if (Blocked == null || motion.LengthSquared() <= 0f)
            return motion;
        var p = GlobalPosition;
        if (Blocked(p.X, p.Z)) return motion;
        if (!Blocked(p.X + motion.X, p.Z + motion.Z)) return motion;
        if (Time.GetTicksMsec() - _nodeMapLogMs > 1000)
        {
            _nodeMapLogMs = Time.GetTicksMsec();
            GD.Print($"[nodemap] mur invisible en ({p.X:F1},{-p.Z:F1})");
        }
        if (!Blocked(p.X + motion.X, p.Z))
            return new Vector3(motion.X, 0f, 0f);
        if (!Blocked(p.X, p.Z + motion.Z))
            return new Vector3(0f, 0f, motion.Z);
        // Point&click contre un mur : abandon de la cible (en route
        // auto : replanification avec la case en liste noire).
        var wasAuto = AutoPathActive;
        _moveTarget = null;
        _marker?.Hide();
        if (wasAuto)
        {
            _path.Clear();
            _pathHead = null;
            var h = motion.Normalized();
            Stuck?.Invoke(p, h);
        }
        return Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        // A1.1 : un mort ne bouge pas (WASD/clic-sol ignorés).
        var noMove = EdenClient.Combat.PlayerStateLocks.NoMove
            || EdenClient.Protocol.PlayerVitals.IsDead;
        var moveDir = noMove ? Vector2.Zero : InputMoveDir();
        _keyMove = moveDir != Vector2.Zero;
        if (noMove) _moveTarget = null;

        // Le clavier annule clic-sol / approche NPC / auto-attack.
        if (moveDir != Vector2.Zero &&
            (_moveTarget.HasValue || _talkTarget != null))
        {
            _moveTarget = null;
            _talkTarget = null;
            _marker?.Hide();
            Cancelled?.Invoke();
        }

        // Point&click si pas de saisie clavier.
        if (moveDir == Vector2.Zero && _moveTarget.HasValue)
        {
            var to = _moveTarget.Value - GlobalPosition;
            var flat = new Vector2(to.X, to.Z);
            if (flat.Length() < ArriveDistance)
            {
                // Waypoint suivant si la file appartient à cette cible.
                if (_path.Count > 0 && _pathHead == _moveTarget)
                {
                    _moveTarget = _pathHead = _path.Dequeue();
                }
                else
                {
                    _moveTarget = null;
                    _path.Clear();
                    _marker?.Hide();
                }
            }
            else
            {
                moveDir = flat.Normalized();
            }
        }

        // Se relever dès qu'on bouge (Sit/Stand) ; pref « Camera
        // Follow » : la caméra se recale vers le cap de marche.
        if (moveDir != Vector2.Zero)
        {
            if (_sitting) StandUp();
            FollowCamera(moveDir, dt);
        }

        var speed = Input.IsActionPressed("move_run")
            ? RunSpeed : MoveSpeed;
        var srvMul = MoveSpeedMul;
        if (Mathf.Abs(srvMul - _lastLoggedMul) > 1e-3f)
        {
            _lastLoggedMul = srvMul;
            GD.Print($"[speed] mul serveur={srvMul:0.###} (296 Speeds[0]=" +
                $"{EdenClient.Protocol.PlayerVitals.TotalStats?.Speeds?[0]:0.###})");
        }
        var vel = new Vector3(moveDir.X, 0f, moveDir.Y) * speed * srvMul;

        // Déplacement horizontal via la physique : MoveAndCollide
        // stoppe sur les StaticBody3D des helpers GLB (murs/clôtures)
        // et on glisse le long de la normale pour ne pas coller.
        var motion = new Vector3(vel.X, 0f, vel.Z) * dt;
        motion = ClipByNodeMap(motion);
        if (motion.LengthSquared() > 0f)
        {
            var dst = GlobalPosition + motion;
            var curG = GroundHeight?.Invoke(
                GlobalPosition.X, GlobalPosition.Z);
            var dstG = GroundHeight?.Invoke(dst.X, dst.Z);
            // Hors-carte : la destination n'a pas de terrain alors
            // que la position courante en a → mouvement refusé (le
            // natif borne au navmesh ; ici on borne à la grille fsm).
            // Terrain inconnu partout (maps modèle sans fsm) : libre.
            if (dstG.HasValue || !curG.HasValue)
            {
                if (MoveAndCollide(motion) is { } col)
                {
                    var rem = col.GetRemainder();
                    var n = col.GetNormal();
                    var slide = rem - n * rem.Dot(n);
                    slide.Y = 0f;
                    if (slide.LengthSquared() > 1e-8f)
                    {
                        var sdst = GlobalPosition + slide;
                        var sdstG = GroundHeight?.Invoke(
                            sdst.X, sdst.Z);
                        if (sdstG.HasValue || !curG.HasValue)
                            MoveAndCollide(slide);
                    }
                }
            }
        }

        DetectStuck(dt, moveDir, speed);

        var pos = GlobalPosition;
        // Saut local : en l'air pos.Y est intégrée par la vitesse
        // verticale au lieu d'être snappée sur le terrain ; à
        // l'atterrissage (y ≤ sol) le snap reprend. Sans terrain sous
        // les pieds (hors grille fsm), le plancher de repli est la
        // hauteur de décollage — jamais de chute infinie.
        UpdateJump(dt, ref pos);
        GlobalPosition = pos;
        vel.Y = _airborne ? _vy : 0f;
        Velocity = vel; // exposé pour debug/animation
        // H10 : l'input direct bascule l'anim marche sans attendre la
        // fenêtre de mesure de vitesse (latence de démarrage visible).
        if (Animator != null)
            Animator.InputMoving = moveDir != Vector2.Zero;

        // Rotation du visuel vers la direction de déplacement
        // (le steering droit garde la priorité tant que RMB est drag).
        // Natif (docs/ANIMS-DIRECTIONNELLES.md) : A/D/S ne retournent
        // pas le perso — il garde son cap (= caméra) et joue l'anim
        // directionnelle (101 avant, 102 gauche, 103 droite, 104
        // arrière) ; seules les diagonales pivotent de ±45° (monté :
        // A/D purs ±90°, records monture).
        var mounted = MountAnimator != null;
        MoveRecord = _keyMove ? CurrentMoveRecord() : DirectionalLocomotion.Fwd;
        var moveAnim = DirectionalLocomotion.AnimFor(MoveRecord, mounted);
        if (_keyMove && _recHeld < 0.6f && (_recHeld += dt) >= 0.6f)
            GD.Print($"[dirmove] held rec={MoveRecord} " +
                $"cur={Animator?.Current} mount={MountAnimator?.Current}" +
                $" modelYaw={Mathf.RadToDeg(ModelYaw):F0}");
        if (MoveRecord != _lastLoggedRecord)
        {
            _recHeld = 0f;
            _lastLoggedRecord = MoveRecord;
            GD.Print($"[dirmove] rec={MoveRecord} anim={moveAnim} " +
                $"camYaw={Mathf.RadToDeg(_yaw):F0} " +
                $"modelYaw={Mathf.RadToDeg(ModelYaw):F0} " +
                $"cur={Animator?.Current}");
        }
        if (Animator != null) Animator.MoveAnim = moveAnim;
        if (MountAnimator != null) MountAnimator.MoveAnim = moveAnim;
        if (moveDir != Vector2.Zero && _model != null && !_rDrag)
        {
            var targetYaw = _keyMove
                ? _yaw + DirectionalLocomotion.FaceOffset(MoveRecord, mounted)
                : Mathf.Atan2(-moveDir.X, -moveDir.Y);
            var cur = _model.Rotation.Y;
            var d = Mathf.Wrap(targetYaw - cur, -Mathf.Pi, Mathf.Pi);
            _model.Rotation = new Vector3(0f,
                cur + d * Mathf.Min(1f, dt * 12f), 0f);
        }

        // Approche NPC : à portée → TalkToNpc (NCID 10) une fois.
        if (_talkTarget != null)
        {
            if (!GodotObject.IsInstanceValid(_talkTarget) ||
                _talkTarget.IsQueuedForDeletion())
            {
                _talkTarget = null;
            }
            else
            {
                var to = _talkTarget.GlobalPosition - GlobalPosition;
                var dist = new Vector2(to.X, to.Z).Length();
                if (dist <= TalkRange)
                {
                    var npc = _talkTarget;
                    _talkTarget = null;
                    _moveTarget = null;
                    _marker?.Hide();
                    FaceTowards(npc.GlobalPosition, 1f);
                    Send(new Protocol.ZoneTalkToNpc
                    {
                        NpcId = (int)npc.Handle,
                    });
                    GD.Print($"[input] TalkToNpc h=0x{npc.Handle:x8}");
                }
                else if (_rechase >= RechaseSeconds)
                {
                    _rechase = 0;
                    _moveTarget = _talkTarget.GlobalPosition;
                }
            }
        }
        _rechase += dt;
    }

    private float _rechase;
    private const float RechaseSeconds = 0.3f;

    // ------------------------------------------------------------------
    // Saut local (eden_jump — l'action InputMap est posée par
    // InputBindings.Apply depuis les bindings sauvegardés, pas par
    // EnsureInputActions ; on ne fait que vérifier sa présence).

    private float _vy;          // vitesse verticale courante (u/s)
    private bool _airborne;     // en vol → snap Y terrain suspendu
    private int _jumpsUsed;     // impulsions consommées dans ce vol
    private float _jumpFloorY;  // plancher de repli hors-terrain
    private bool _jumpDiagLogged;
    private bool _argsDumped;
    /// <summary>Log « [jump] … » derrière EDEN_JUMP_DIAG=1 (même
    /// convention que EDEN_ANIM_DIAG d'EntityAnimator).</summary>
    private static readonly bool JumpDiag =
        System.Environment.GetEnvironmentVariable("EDEN_JUMP_DIAG")
            == "1";

    /// <summary>État vertical du saut : à la frappe
    /// <c>eden_jump</c> → impulsion <see cref="JumpSpeed"/> (jusqu'à
    /// <see cref="MaxJumps"/> par envol — la 2e, en l'air, est le
    /// double saut), puis gravité <see cref="JumpGravity"/> ;
    /// atterrissage dès que y ≤ sol (vy=0, <see cref="_jumpsUsed"/>
    /// réinitialisé, retour au snap). Ignoré pendant un one-shot
    /// d'action (<see cref="EntityAnimator.OneShotPlaying"/>) autre
    /// que jump01 — sinon la 2e impulsion serait bloquée par le saut
    /// lui-même. À l'atterrissage l'auto-locomotion reprend seule
    /// (le one-shot est terminé). L'input n'est testé que si
    /// l'action existe dans l'InputMap — sinon IsActionJustPressed
    /// loguerait une erreur à chaque frame.</summary>
    private void UpdateJump(float dt, ref Vector3 pos)
    {
        if (!_jumpDiagLogged)
        {
            _jumpDiagLogged = true;
            if (JumpDiag)
                GD.Print("[jump] eden_jump InputMap=" +
                    $"{InputMap.HasAction("eden_jump")} " +
                    $"(speed={JumpSpeed} g={JumpGravity} " +
                    $"max={MaxJumps})");
        }
        var groundY = GroundHeight?.Invoke(pos.X, pos.Z);
        var busy = Animator?.OneShotPlaying == true &&
            Animator.Current is not ("jump01" or "ride08");
        if (!busy && InputMap.HasAction("eden_jump") &&
            Input.IsActionJustPressed("eden_jump") &&
            (!_airborne || _jumpsUsed < MaxJumps))
        {
            // Le plancher de repli hors-terrain mémorise la hauteur
            // du DÉCOLLAGE : la 2e impulsion en l'air ne doit pas le
            // remonter (sinon atterrissage flottant au-dessus du sol).
            if (!_airborne) _jumpFloorY = pos.Y;
            _airborne = true;
            _jumpsUsed++;
            _vy = JumpSpeed;
            StandUp();
            Animator?.PlayOnce("jump01");
            if (JumpDiag)
                GD.Print($"[jump] impulsion n={_jumpsUsed} " +
                    $"y={pos.Y:F2}");
        }
        if (_airborne)
        {
            _vy -= JumpGravity * dt;
            pos.Y += _vy * dt;
            // Sol : terrain sous les pieds s'il existe (atterrissage
            // sur une pente/terrasse inclus), sinon hauteur de
            // décollage — hors grille fsm la chute serait infinie.
            var floor = groundY ?? _jumpFloorY;
            if (pos.Y <= floor)
            {
                pos.Y = floor;
                _airborne = false;
                _vy = 0f;
                _jumpsUsed = 0;
                if (JumpDiag)
                    GD.Print($"[jump] atterrissage y={pos.Y:F2}");
            }
        }
        else if (groundY is { } gy)
        {
            pos.Y = gy;
        }
        // En vol stationnaire l'animator tient la pose de saut ;
        // au sol l'auto-locomotion stand01/move01 reprend.
        if (Animator != null) Animator.Airborne = _airborne;
    }

    // --- pilotes de validation smoke -------------------------------
    private double _demoT = -1;
    private int _demoStep;
    private double _cursorT;
    private int _cursorStep;
    private bool _cursorTest;
    private bool _cursorArgChecked;
    private bool _cursorDone;
    private string _cursorUiTarget = "";
    private const string ShotsDir =
        "/home/ster2lune/Bureau/reverseden/portage/godot-shots";

    /// <summary>Rejoue le scénario clic-sol → clic-droit mob en
    /// appelant les mêmes handlers que la souris (validation
    /// automatisée — le chemin souris réel est identique en amont).</summary>
    private void DemoInput(double delta)
    {
        if (_demoT < 0) return;
        _demoT += delta;
        var vp = GetViewport();
        if (vp == null) return;
        var center = vp.GetVisibleRect().Size / 2f;
        switch (_demoStep)
        {
            // Saut + double saut injectés en tout premier (aucune
            // entité requise) : InputEventKey Space → action
            // eden_jump → même chemin que le clavier réel. Les
            // impulsions/atterrissages sont logués par UpdateJump
            // (EDEN_JUMP_DIAG) et la hauteur par [demo] ici.
            case 0 when _demoT > 1.0:
                _demoStep = 10;
                InjectKey(Key.Space, true);      // 1re impulsion
                break;
            case 10 when _demoT > 1.15:
                _demoStep = 11;
                InjectKey(Key.Space, false);
                break;
            case 11 when _airborne || _demoT > 2.5:
                // En vol (idéal : <1 s après l'impulsion 1) → la
                // frappe déclenche le double saut n=2 ; repli temps
                // si le 1er appui n'a pas décollé (one-shot busy…).
                _demoStep = 12;
                GD.Print($"[demo] jump#2 y={GlobalPosition.Y:F2} " +
                         $"air={_airborne}");
                InjectKey(Key.Space, true);      // double saut
                break;
            case 12 when _demoT > 2.0:
                _demoStep = 13;
                InjectKey(Key.Space, false);
                Shot("00-jump-air");             // capture en vol
                GD.Print($"[demo] jump air={_airborne} " +
                         $"y={GlobalPosition.Y:F2}");
                break;
            case 13 when _demoT > 3.2 && Registry == null:
                // Solo : pas de registre d'entités, seul le schéma souris
                // est testable.
                _demoStep = 4;
                MouseSchemeTest(center);
                GD.Print("[demo] fin");
                GetTree().Quit();
                break;
            case 13 when _demoT > 3.2 && Registry != null:
                _demoStep = 14;
                // Retombé — prouvé par le log [jump] atterrissage.
                GD.Print($"[demo] landed y={GlobalPosition.Y:F2} " +
                         "→ réplique joueur distant (265+263)");
                DemoRemoteEquip();
                break;
            case 14 when _demoT > 3.8:
                _demoStep = 15;
                DemoRemoteMove();      // 326 dst≠cur → InputMoving
                break;
            case 15 when _demoT > 4.6:
                _demoStep = 1;
                // L'animator du factice doit jouer move01 (flag
                // InputMoving posé par le 326, sans attendre la
                // mesure de vitesse) — preuve [registry]/[demo].
                var fe = Registry != null &&
                    Registry.Entities.TryGetValue(FakeCharId,
                        out var _fe)
                        ? _fe : null;
                GD.Print($"[demo] remote anim=" +
                         $"{(fe?.Animator == null ? "null" :
                            fe.Animator.Current)} (attendu move01)");
                // Preuve visuelle équipement : suffixes de meshes
                // tenue (#B2xx) réellement visibles sur le corps
                // factice + attachements __equip_* (casque/cape/arme
                // aux bones Prop1/Prop2/Head) — #B202 seul = repli
                // « pas d'équipement » d'ApplyEquipment.
                var body = fe?.GetNodeOrNull<Node3D>("Visual");
                if (body == null && fe != null)
                    GD.Print("[demo] remote enfants=" +
                        string.Join(",", fe.GetChildren()
                            .Select(c => c.Name.ToString())));
                if (body != null)
                {
                    var allMesh = body
                        .FindChildren("*", "MeshInstance3D", true,
                            false)
                        .Cast<MeshInstance3D>().ToArray();
                    var vis = allMesh
                        .Where(mi => mi.Visible &&
                            mi.Name.ToString() is { Length: > 3 } n &&
                            n[0] == '#' &&
                            int.TryParse(n[2..], out var s) &&
                            s >= 200)
                        .Select(mi => mi.Name.ToString())
                        .ToArray();
                    var eq = body
                        .FindChildren("__equip_*", "", true, false)
                        .Count;
                    GD.Print($"[demo] remote tenue={string.Join(
                        ",", vis)} attach={eq} " +
                        $"worn={fe!.WornItems.Count}");
                    GD.Print($"[demo] remote visibles=" +
                        string.Join(",", allMesh
                            .Where(m => m.Visible)
                            .Select(m => m.Name.ToString())
                            .Take(12)));
                    // Résolution c_item par slot — explique pourquoi
                    // la tenue tombe (ou pas) sur une rangée 4pequip.
                    foreach (var (loc, itemId) in fe.WornItems
                                 .OrderBy(kv => kv.Key))
                    {
                        var def = Hud.InventoryWindow
                            .LookupDef(itemId);
                        GD.Print($"[demo]   worn loc{loc} " +
                            $"id={itemId} → " +
                            $"{(def == null ? "null" :
                                $"{def.Code}/{def.ModelCode}")}");
                    }
                }
                Shot("04-remote-equip");
                // Clic gauche au sol (un peu à droite du centre).
                OnLeftClick(center + new Vector2(220f, 60f));
                GD.Print("[demo] clic sol → marker+move");
                break;
            case 1 when _demoT > 5.5:
                _demoStep = 2;
                Shot("01-move-marker");
                // Clic droit : mob si la zone en contient, sinon NPC
                // (S002 est une ville sans mob — le chemin réel de
                // PickArea est identique, TalkToNpc remplace l'attaque).
                var target = NearestEntity(12) ?? NearestEntity(8);
                if (target != null)
                {
                    var sp = _camera!.UnprojectPosition(
                        target.GlobalPosition + new Vector3(0f, 1.1f, 0f));
                    OnRightClick(sp);
                    GD.Print($"[demo] clic droit kind={target.Kind} " +
                             $"h=0x{target.Handle:x8}");
                }
                else GD.Print("[demo] aucune entité visible");
                break;
            case 2 when _demoT > 7.5:
                _demoStep = 3;
                Shot("02-approach");
                break;
            case 3 when _demoT > 13:
                _demoStep = 4;
                Shot("03-combat");
                MouseSchemeTest(center);
                GD.Print("[demo] fin");
                GetTree().Quit();
                break;
        }
    }

    private static InputEventMouseButton TestBtn(MouseButton b,
        bool pressed, Vector2 pos) => new()
    {
        ButtonIndex = b, Pressed = pressed, Position = pos,
        ButtonMask = pressed ? (b == MouseButton.Left
            ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
    };

    private static InputEventMouseMotion TestMove(MouseButtonMask m,
        Vector2 pos, Vector2 rel) => new()
    {
        Position = pos, Relative = rel, ButtonMask = m,
    };

    /// <summary>--input-test : schéma souris voulu. Glisser GAUCHE =
    /// caméra seule (cap du perso inchangé) ; glisser DROIT = caméra +
    /// perso orienté sur la caméra ; clic droit simple sur PNJ = talk.
    /// Passe par les vrais handlers (HandleMouseButton/Motion).</summary>
    private void MouseSchemeTest(Vector2 c)
    {
        _moveTarget = null;
        _uiGrace = 0f;
        var ok = true;
        void Check(string name, bool pass)
        {
            ok &= pass;
            GD.Print($"[input-test] {(pass ? "PASS" : "FAIL")} {name}");
        }
        // 1) Glisser gauche.
        var m0 = ModelYaw; var c0 = _yaw;
        HandleMouseButton(TestBtn(MouseButton.Left, true, c));
        HandleMouseMotion(TestMove(MouseButtonMask.Left,
            c + new Vector2(60f, 0f), new Vector2(60f, 0f)));
        HandleMouseMotion(TestMove(MouseButtonMask.Left,
            c + new Vector2(100f, 0f), new Vector2(40f, 0f)));
        var dragL = _lDrag;
        var moveL = _moveTarget;
        HandleMouseButton(TestBtn(MouseButton.Left, false,
            c + new Vector2(100f, 0f)));
        GD.Print($"[input-test] glisser gauche: camYaw {Mathf.RadToDeg(c0):F1}" +
            $"->{Mathf.RadToDeg(_yaw):F1} modelYaw {Mathf.RadToDeg(m0):F1}" +
            $"->{Mathf.RadToDeg(ModelYaw):F1}");
        Check("gauche: drag détecté", dragL);
        Check("gauche: caméra tourne", Mathf.Abs(_yaw - c0) > 0.1f);
        Check("gauche: yaw perso inchangé",
            Mathf.Abs(Mathf.Wrap(ModelYaw - m0, -Mathf.Pi, Mathf.Pi))
                < 0.001f);
        Check("gauche: relâché sans clic-déplacement (pas de _moveTarget)",
            moveL == null && _moveTarget == null);
        // 2) Glisser droit.
        c0 = _yaw; m0 = ModelYaw;
        HandleMouseButton(TestBtn(MouseButton.Right, true, c));
        HandleMouseMotion(TestMove(MouseButtonMask.Right,
            c + new Vector2(60f, 0f), new Vector2(60f, 0f)));
        HandleMouseMotion(TestMove(MouseButtonMask.Right,
            c + new Vector2(100f, 0f), new Vector2(40f, 0f)));
        var dragR = _rDrag;
        var yawR = ModelYaw;
        var camR = _yaw;
        HandleMouseButton(TestBtn(MouseButton.Right, false,
            c + new Vector2(100f, 0f)));
        GD.Print($"[input-test] glisser droit: camYaw {Mathf.RadToDeg(c0):F1}" +
            $"->{Mathf.RadToDeg(camR):F1} modelYaw {Mathf.RadToDeg(m0):F1}" +
            $"->{Mathf.RadToDeg(yawR):F1}");
        Check("droit: drag détecté", dragR);
        Check("droit: caméra tourne", Mathf.Abs(camR - c0) > 0.1f);
        Check("droit: yaw perso suit la caméra",
            Mathf.Abs(Mathf.Wrap(yawR - camR, -Mathf.Pi, Mathf.Pi))
                < 0.001f);
        // 3) Clic droit simple sur un PNJ : conversation (approche+talk).
        var npc = Registry?.Entities.Values.FirstOrDefault(e =>
            e.Kind == 8 && GodotObject.IsInstanceValid(e));
        if (npc == null || _camera == null)
            GD.Print("[input-test] SKIP clic droit PNJ : aucun PNJ en zone");
        else
        {
            var sp = _camera.UnprojectPosition(
                npc.GlobalPosition + new Vector3(0f, 1.1f, 0f));
            _talkTarget = null;
            HandleMouseButton(TestBtn(MouseButton.Right, true, sp));
            HandleMouseButton(TestBtn(MouseButton.Right, false, sp));
            Check($"droit simple PNJ h=0x{npc.Handle:x8}: talk armé",
                _talkTarget == npc && !_rDrag);
        }
        GD.Print($"[input-test] {(ok ? "OK" : "ECHEC")}");
    }

    private bool _walkArgChecked;
    // _walkT = MinValue : inactif ; <0 : délai avant départ ; ≥0 : actif.
    private float _walkT = float.MinValue, _walkSecs = 5f;
    private Vector3 _walkStart;

    private void WalkProbe(double delta)
    {
        var prev = _walkT; _walkT += (float)delta;
        if (prev < 2f && _walkT >= 2f)
        {
            _walkStart = GlobalPosition;
            InjectKey(Key.W, true);
            GD.Print($"[walk-test] départ {GlobalPosition}");
        }
        else if (prev >= 2f && prev < _walkSecs + 2f &&
                 _walkT >= _walkSecs + 2f)
        {
            InjectKey(Key.W, false);
            GD.Print($"[walk-test] arrêt {GlobalPosition} — " +
                     $"déplacé {(GlobalPosition - _walkStart).Length():F1} u " +
                     $"en {_walkSecs:F0} s, y={GlobalPosition.Y:F2}");
            _walkT = float.MinValue;
        }
        else if (_walkT >= 2f &&
                 MathF.Floor(prev * 2f) != MathF.Floor(_walkT * 2f))
            GD.Print($"[walk-test] t={_walkT - 2f:F1} " +
                     $"pos={GlobalPosition}");
    }

    /// <summary>Injecte un événement touche dans la boucle d'input
    /// (<see cref="Input.ParseInputEvent"/> — même pipeline que le
    /// clavier réel : l'InputMap mappe la touche à son action, dont
    /// <c>eden_jump</c>).</summary>
    private static void InjectKey(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            Pressed = pressed,
        });

    /// <summary>charId factice du joueur distant rejoué par
    /// <see cref="DemoRemoteEquip"/> (hors plage des charIds réels).</summary>
    private const int FakeCharId = 0x7fff0001;
    /// <summary>Position Eden à ~3 u devant le joueur self — cible de
    /// la réplique distante.</summary>
    private (float X, float Y) DemoRemotePos(float dy)
    {
        var p = GlobalPosition;
        return WorldConv.ToEdenGround(p + new Vector3(0f, 0f, dy));
    }

    /// <summary>Rejoue localement la séquence spawn+fiche d'un joueur
    /// distant équipé : 265 (SeeChar) puis 263 (DisplayChar) avec la
    /// queue équipement encodée au format exact du codec serveur
    /// (<c>ClientDisplayCharCodec.Build</c> — u8 mask vide, u32 mask
    /// set0, records [u16 id][corps EquipView], u32 set1 vide). Les
    /// itemIds sont des ids c_item réels résolus par 4pequip
    /// (608 → #B230, 31900/32034 → armes W102/W101). Le log
    /// « [registry] player … équip=N » prouve le parse, et le corps
    /// factice affiche la tenue via <c>CharVisual.ApplyEquipment</c>.
    /// </summary>
    private void DemoRemoteEquip()
    {
        if (Registry == null) return;
        var (ex, ey) = DemoRemotePos(3f);
        Registry.HandleSeeChar(new Protocol.ZoneSeeChar
        {
            CharId = FakeCharId, X = ex, Y = ey,
            Hp = 10, MaxHp = 10, Level = 42,
        });
        // Set curé : 608 = costume marin K20105 → rangée TableBoy
        // group0 « #B230 » (bascule le mesh tenue hors du repli
        // #B202) ; 31900/32034 = épées W10201/W10101 → GLB
        // item/model/W102|W101 attachés aux bones Prop1/Prop2.
        // (PlayerEquipment.Worn du self ne porte que des armures
        // starter A20065… sans rangée TableBoy — preuve neutre.)
        var worn = new Dictionary<int, int>
        {
            { 2, 608 }, { 7, 31900 }, { 8, 32034 },
        };
        var w = new BinWriter();
        w.WriteByte(0);                  // u8 mask — aucun champ cond.
        var eqMask = 0u;
        foreach (var (loc, _) in worn)
            if (loc is >= 0 and < 11) eqMask |= 1u << loc;
        w.WriteInt((int)eqMask);         // u32 — mask équipement set0
        foreach (var (loc, id) in worn.OrderBy(kv => kv.Key))
        {
            if (loc < 0 || loc >= 11) continue;
            w.WriteShort((short)id);     // u16 — itemId du slot
            w.WriteByte(0);              // u8 flags (corps EquipView)
            w.WriteShort(0);             // str maker vide
            w.WriteBytes(new byte[3]);   // u24 mask = 0 (aucun cond.)
            w.WriteBytes(new byte[34]);  // tail fixe 34B
        }
        w.WriteInt(0);                   // u32 — mask set1 vide
        var app = new byte[32];
        app[28] = 0x10;                  // bodyCode 1 → corps « b »
        Registry.HandleDisplayChar(new Combat.ZoneClientDisplayChar
        {
            CharId = FakeCharId, CharName = "EquipDummy", Level = 42,
            Appearance = app, Rest = w.ToArray(),
        });
    }

    /// <summary>Envoie au factice un 326 avec destination ≠ position
    /// courante — le flag « moving » posé par
    /// <see cref="EntityRegistry.HandleMovePrediction"/> doit mettre
    /// son animator en move01 dès la trame (preuve phase5 §4).</summary>
    private void DemoRemoteMove()
    {
        if (Registry == null) return;
        var (ex, ey) = DemoRemotePos(3f);
        Registry.HandleMovePrediction(new Protocol.ZoneMovePrediction
        {
            Handle = FakeCharId,
            CurX = (ushort)(ex * 16f), CurY = (ushort)(ey * 16f),
            Flag = 7, Facing = 0,
            DstX = (ushort)((ex + 4f) * 16f), DstY = (ushort)(ey * 16f),
        });
        GD.Print($"[demo] remote 326 dst→cur+4u envoyé");
    }

    /// <summary>Pilote --cursor-test : NPC → Talk, mob → Attack, puis
    /// un point du HUD → Arrow. La preuve vient du pointeur réel
    /// (WarpMouse + hover PickArea), pas de Set() appelé directement.</summary>
    private void CursorTest(double delta)
    {
        if (!_cursorTest) return;
        _cursorT += delta;
        var vp = GetViewport();
        if (vp == null || _camera == null) return;
        var t0 = _cursorT;
        switch (_cursorStep)
        {
            case 0 when t0 > 1.5 && ProjectedEntity(8, vp,
                out var npc, out var npcPos):
                Input.WarpMouse(npcPos);
                _cursorStep = 1;
                _cursorT = 0;
                break;
            case 0 when t0 > 8:
                GD.Print("[cursor-test] aucun NPC projeté à l'écran");
                _cursorStep = 2;
                _cursorT = 0;
                break;
            case 1 when t0 > 0.5:
            {
                var p = PickEntity(vp.GetMousePosition());
                GD.Print($"[cursor-test] npc hover " +
                    $"mouse={vp.GetMousePosition()} picked={p?.Kind} " +
                    $"h=0x{p?.Handle:x8} → {EdenCursors.Current}");
                _cursorStep = 2;
                _cursorT = 0;
                break;
            }
            case 2 when ProjectedEntity(12, vp,
                out var mob, out var mobPos):
                Input.WarpMouse(mobPos);
                _cursorStep = 3;
                _cursorT = 0;
                break;
            case 2 when t0 > 240:
                GD.Print("[cursor-test] aucun mob projeté à l'écran");
                _cursorStep = 4;
                _cursorT = 0;
                break;
            case 3 when t0 > 0.5:
            {
                var p = PickEntity(vp.GetMousePosition());
                GD.Print($"[cursor-test] mob hover " +
                    $"mouse={vp.GetMousePosition()} picked={p?.Kind} " +
                    $"h=0x{p?.Handle:x8} → {EdenCursors.Current}");
                _cursorStep = 4;
                _cursorT = 0;
                break;
            }
            case 4:
            {
                var rect = vp.GetVisibleRect();
                var b = GetTree().Root
                    .FindChildren("*", "BaseButton", true, false)
                    .Cast<BaseButton>()
                    .Where(c => c.IsVisibleInTree())
                    .FirstOrDefault(c =>
                        c.GetGlobalRect().Size is { X: > 2f, Y: > 2f } &&
                        rect.HasPoint(c.GetGlobalRect().GetCenter()));
                var p = b?.GetGlobalRect().GetCenter()
                        ?? rect.Size - new Vector2(40f, 40f);
                _cursorUiTarget = b?.GetPath().ToString() ?? "<fallback>";
                Input.WarpMouse(p);
                // WarpMouse déplace le pointeur OS mais ne réinjecte pas
                // toujours un événement motion dans le GUI Godot.
                Input.ParseInputEvent(new InputEventMouseMotion
                { Position = p });
                _cursorStep = 5;
                _cursorT = 0;
                break;
            }
            case 5 when t0 > 0.5:
                GD.Print($"[cursor-test] ui hover " +
                         $"target={_cursorUiTarget} " +
                         $"hovered={vp.GuiGetHoveredControl()?.GetPath()} " +
                         $"→ {EdenCursors.Current}");
                _cursorTest = false;
                _cursorDone = true;
                break;
        }
    }

    /// <summary>Entité du type demandé dont le torse se projette dans
    /// le viewport (sinon le warp pointer tombe hors écran).</summary>
    private bool ProjectedEntity(byte kind, Viewport vp,
        out RemoteEntity? entity, out Vector2 screenPos)
    {
        entity = null;
        screenPos = default;
        if (Registry == null || _camera == null) return false;
        var rect = vp.GetVisibleRect();
        var center = rect.GetCenter();
        var best = float.MaxValue;
        foreach (var (_, e) in Registry.Entities)
        {
            if (e.Kind != kind || !GodotObject.IsInstanceValid(e) ||
                !e.IsInsideTree())
                continue;
            var world = e.GlobalPosition + new Vector3(0f, 1.1f, 0f);
            if (_camera.IsPositionBehind(world)) continue;
            var p = _camera.UnprojectPosition(world);
            if (!rect.HasPoint(p)) continue;
            var d = p.DistanceSquaredTo(center);
            if (d >= best) continue;
            best = d;
            entity = e;
            screenPos = p;
        }
        return entity != null;
    }

    private RemoteEntity? NearestEntity(byte kind)
    {
        RemoteEntity? best = null;
        var bestD = float.MaxValue;
        if (Registry == null) return null;
        foreach (var (_, e) in Registry.Entities)
        {
            if (e.Kind != kind || !GodotObject.IsInstanceValid(e))
                continue;
            var d = (e.GlobalPosition - GlobalPosition).LengthSquared();
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    private void Shot(string name)
    {
        try
        {
            Directory.CreateDirectory(ShotsDir);
            RenderingServer.ForceDraw();
            var img = GetViewport()?.GetTexture()?.GetImage();
            img?.SavePng($"{ShotsDir}/{name}.png");
            GD.Print($"[demo] shot {name}");
        }
        catch (Exception ex) { GD.PrintErr($"[demo] shot: {ex}"); }
    }

    private long _lastBarHp = -1, _lastBarMax = -1;

    public override void _Process(double delta)
    {
        if (_dbgYawOn)
        {
            _yaw = ModelYaw + _dbgYaw;
            ApplyCamAngles();
        }
        // Barre de PV au-dessus de soi : créée à la demande, nourrie
        // par HudPrefs.SelfHp (HudRoot.SetVitals).
        if (_npBar == null) SetNameplate("");
        if (Hud.HudPrefs.SelfHp != _lastBarHp ||
            Hud.HudPrefs.SelfMaxHp != _lastBarMax)
        {
            _lastBarHp = Hud.HudPrefs.SelfHp;
            _lastBarMax = Hud.HudPrefs.SelfMaxHp;
            _npBar?.SetHp(_lastBarHp, _lastBarMax);
        }
        // --input-test : pilote les handlers de clic pour la smoke.
        if (_demoT < 0)
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--input-test")) { _demoT = 0; break; }
        if (JumpDiag && !_argsDumped)
        {
            _argsDumped = true;
            GD.Print("[jump] args=[" +
                string.Join("][", OS.GetCmdlineArgs()) + "] user=[" +
                string.Join("][", OS.GetCmdlineUserArgs()) + "]");
        }
        DemoInput(delta);

        // --cursor-test : warp le pointeur réel sur une entité et logue
        // le mode appliqué (la boucle de hover juste dessous choisit
        // Talk/Attack depuis PickArea).
        if (!_cursorArgChecked)
        {
            _cursorArgChecked = true;
            var args = OS.GetCmdlineArgs()
                .Concat(OS.GetCmdlineUserArgs()).ToArray();
            _cursorTest = args.Contains("--cursor-test") ||
                args.Contains("--cursor-test=mob") ||
                args.Contains("--cursor-test=ui");
            _cursorStep = args.Contains("--cursor-test=mob") ? 2
                : args.Contains("--cursor-test=ui") ? 4 : 0;
            if (_cursorTest)
                GD.Print("[cursor-test] actif — attente NetEntities");
        }
        if (_cursorTest && !_cursorDone) CursorTest(delta);

        // --walk-test[=secs] : maintient W (vrai pipeline input) après
        // le spawn et logue la position chaque 0,5 s — preuve de
        // déplacement et de blocage mur/sol (fuite hors salle = échec).
        if (!_walkArgChecked)
        {
            _walkArgChecked = true;
            var wa = OS.GetCmdlineArgs()
                .Concat(OS.GetCmdlineUserArgs())
                .FirstOrDefault(a => a.StartsWith("--walk-test"));
            if (wa != null)
            {
                // --walk-test=secs[,delay] : delay repousse le départ
                // (_walkT négatif) — utile pour marcher après une
                // migration //transport_node + //tele.
                var wv = wa.Contains('=') ? wa.Split('=')[1].Split(',')
                    : System.Array.Empty<string>();
                _walkSecs = wv.Length > 0 &&
                    float.TryParse(wv[0], out var ws) ? ws : 5f;
                _walkT = wv.Length > 1 &&
                    float.TryParse(wv[1], out var wd) ? -wd : 0f;
            }
        }
        if (_walkT != float.MinValue) WalkProbe(delta);

        // Découverte tardive du registre « NetEntities » (ajouté après
        // le joueur par GameScene en mode live). On retente tant qu'il
        // est absent : le premier _Process peut précéder AddChild().
        if (Registry == null)
        {
            Registry = GetParent()?
                .GetNodeOrNull<EntityRegistry>("NetEntities");
            if (_autoAttack != null) _autoAttack.Registry = Registry;
        }
        if (_camera != null && _camDbgT > 0f)
        {
            _camDbgT -= (float)delta;
            if (_camDbgT <= 0f && _spring != null && _camYaw != null)
            {
                _camDbgT = 2f;
                GD.Print($"[camdbg] springLen={_camera.Position.Length():F2} " +
                    $"want={_spring?.SpringLength} camPos={_camera.GlobalPosition}");
                var from = _camYaw.GlobalPosition;
                var to = from + _camPitch!.GlobalBasis * new Vector3(0f, 0f, 6.5f);
                var q = PhysicsRayQueryParameters3D.Create(from, to,
                    World.WalkableGround.WallLayer |
                    World.WalkableGround.FloorLayer);
                var hit = GetWorld3D().DirectSpaceState
                    .IntersectRay(q);
                if (hit.Count > 0)
                {
                    var col = hit["collider"].AsGodotObject();
                    GD.Print($"[camdbg] ray hit " +
                        $"{(col is Node n ? n.Name : col)} " +
                        $"@ {hit["position"]}");
                }
                else GD.Print("[camdbg] ray clear — pas de bloqueur");
            }
        }
        // La caméra ne rentre jamais sous le terrain : le SpringArm
        // n'a pas de collision physique — clamp Y min = sol + 0.3
        // (TerrainSampler via GroundHeight).
        if (_camera != null && GroundHeight != null)
        {
            var cp = _camera.GlobalPosition;
            if (GroundHeight(cp.X, cp.Z) is { } gy &&
                cp.Y < gy + 0.3f)
            {
                cp.Y = gy + 0.3f;
                _camera.GlobalPosition = cp;
            }
        }
        // The GUI owns its cursor while a real interactive Control is
        // hovered. World cursor states come from the native atlas:
        // monster→sword, NPC→speech bubble, treasure/object→hand.
        if (PointerOverUi() || GetViewport().GuiIsDragging())
            _uiGrace = 0.12f;
        else if (_uiGrace > 0f)
            _uiGrace -= (float)delta;
        if (_camera != null && Registry != null && !_rDrag && !_lDrag)
        {
            var vp = GetViewport();
            if (vp != null)
            {
                var overUi = _uiGrace > 0f;
                if (overUi)
                {
                    HoveredEntity = null;
                    EdenCursors.Set(EdenCursors.Mode.Arrow);
                }
                else
                {
                    HoveredEntity = PickEntity(vp.GetMousePosition());
                    EdenCursors.Set(HoveredEntity?.Kind switch
                    {
                        12 => EdenCursors.Mode.Attack,
                        8 => EdenCursors.Mode.Talk,
                        0 when HoveredEntity.EntityId != 0 =>
                            EdenCursors.Mode.Interact,
                        _ => EdenCursors.Mode.Arrow,
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Envoi session (flux écrans — SessionCarry)

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s == null) return;
        var task = s.SendZoneAsync(msg, _cts.Token);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[input] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _cts.Dispose();
        EdenCursors.Set(EdenCursors.Mode.Arrow);
        base._ExitTree();
    }

    private bool _keyMove;
    private int _lastLoggedRecord = -1;
    private float _recHeld;

    /// <summary>Record de direction natif courant (= MoveType du
    /// MoveSelf / Flag du 326 vu par les autres clients) : 7 avant,
    /// 3 gauche, 5 droite, 1 arrière, diagonales 0/2/6/8.</summary>
    public int MoveRecord { get; private set; } = DirectionalLocomotion.Fwd;

    private int CurrentMoveRecord()
    {
        var v = Input.GetVector("move_left", "move_right",
            "move_forward", "move_back");
        return DirectionalLocomotion.Record(v.X, -v.Y);
    }

    /// <summary>Direction de déplacement WASD dans le plan XZ, relative
    /// au yaw caméra.</summary>
    private Vector2 InputMoveDir()
    {
        var v = Input.GetVector("move_left", "move_right",
            "move_forward", "move_back");
        if (v == Vector2.Zero) return v;
        var sin = Mathf.Sin(_yaw);
        var cos = Mathf.Cos(_yaw);
        var dir = new Vector2(
            v.X * cos + v.Y * sin,
            -v.X * sin + v.Y * cos);
        return dir.LengthSquared() > 1e-6f
            ? dir.Normalized() : Vector2.Zero;
    }

    /// <summary>Actions d'input par défaut — enregistrées au runtime
    /// (project.godot n'a pas d'[input] map ; elles sont recréées
    /// si absentes).</summary>
    public static void EnsureInputActions()
    {
        void Bind(string name, params InputEvent[] events)
        {
            if (!InputMap.HasAction(name))
                InputMap.AddAction(name);
            if (InputMap.ActionGetEvents(name).Count != 0) return;
            foreach (var ev in events) InputMap.ActionAddEvent(name, ev);
        }
        InputEventKey K(Key k) => new() { PhysicalKeycode = k };
        Bind("move_forward", K(Key.W), K(Key.Up));
        Bind("move_back", K(Key.S), K(Key.Down));
        Bind("move_left", K(Key.A), K(Key.Left));
        Bind("move_right", K(Key.D), K(Key.Right));
        Bind("move_run", K(Key.Shift));
    }
}
