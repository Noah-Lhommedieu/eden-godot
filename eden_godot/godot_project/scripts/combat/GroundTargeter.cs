using Godot;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Visée au sol des sorts de forme Ground (SSpellData+0x18 = 1, NCID 16) — ref-shot « cercle
/// preparation d'un skill cast de zone a choix » : un anneau vert à motif runique, posé à plat
/// sur le terrain, de rayon <see cref="SpellZone.AreaRadius"/>, qui suit le curseur ; clic gauche =
/// lancer au centre du cercle, clic droit / Échap = annuler. Le centre est borné à la portée du sort
/// autour du lanceur (anneau rouge quand borné).
///
/// Rendu : <see cref="Decal"/> projeté vers le bas (épouse le relief, aucun z-fighting). La texture est
/// procédurale (disque léger + anneau de bord + anneau intérieur + 8 rayons), partagée.
/// </summary>
public partial class GroundTargeter : Node
{
    private const float DecalHeight = 40f;     // épaisseur de projection (m) : couvre les dénivelés
    private static ImageTexture? _ringTex;

    private SkillCast? _owner;
    private PlayerController? _player;
    private Camera3D? _cam;
    private Decal? _decal;
    private int _spell;
    private float _radius, _reach;
    private ulong _armedAt;

    /// <summary>Tests/captures : vise le centre de l'écran au lieu du curseur (aucune souris en run scripté).</summary>
    public bool UseScreenCenter { get; set; }

    public bool Active => _spell != 0;
    public int Spell => _spell;

    /// <summary>Dernier centre visé (coords sol Eden) — exposé pour les tests / captures.</summary>
    public (float X, float Y) Center { get; private set; }

    public void Bind(SkillCast owner, PlayerController? player)
    {
        _owner = owner;
        _player = player;
    }

    /// <summary>Entre en mode visée pour le sort <paramref name="spellId"/>.</summary>
    public void Begin(int spellId)
    {
        Cancel();
        _spell = spellId;
        _radius = Math.Max(SpellZone.AreaRadius(SpellCatalog.RadiusUnits(spellId)), 1f);
        _reach = SpellZone.Reach(SpellCatalog.RangeUnits(spellId));
        _armedAt = Time.GetTicksMsec();
        _decal = new Decal
        {
            TextureAlbedo = RingTexture(),
            TextureEmission = RingTexture(),
            EmissionEnergy = 1.2f,
            Size = new Vector3(_radius * 2f, DecalHeight, _radius * 2f),
            UpperFade = 0.1f,
            LowerFade = 0.1f,
            Modulate = new Color(0.35f, 1f, 0.6f, 1f),
        };
        AddChild(_decal);
        GD.Print($"[ground] visée #{spellId} rayon={_radius:0.#} portée={_reach:0.#}");
        Update();
    }

    public void Cancel()
    {
        _spell = 0;
        if (_decal != null && IsInstanceValid(_decal)) _decal.QueueFree();
        _decal = null;
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        if (SkillCast.Instance is { IsCasting: true } || PlayerVitals.IsDead) { Cancel(); return; }
        Update();
    }

    /// <summary>Intercepte les clics avant PlayerController (déplacement/sélection).</summary>
    public override void _Input(InputEvent e)
    {
        if (!Active) return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true } mb) return;
        // Le clic qui a déclenché la touche de raccourci souris ne doit pas valider aussitôt.
        if (Time.GetTicksMsec() - _armedAt < 120) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
        else if (mb.ButtonIndex == MouseButton.Left)
        {
            var gui = GetViewport().GuiGetHoveredControl();
            if (gui != null && gui.IsVisibleInTree() &&
                gui.MouseFilter != Control.MouseFilterEnum.Ignore) return;   // clic sur le HUD
            Confirm();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Valide la visée : lance le sort au centre courant (clic gauche, ou appel de test).</summary>
    public void Confirm()
    {
        var spell = _spell;
        var (x, y) = Center;
        Cancel();
        GD.Print($"[ground] cast #{spell} au point ({x:0.##},{y:0.##})");
        _owner?.CastAt(spell, x, y);
    }

    private void Update()
    {
        if (_decal == null) return;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam == null || _player == null) return;
        var screen = UseScreenCenter
            ? GetViewport().GetVisibleRect().Size * new Vector2(0.5f, 0.62f)
            : GetViewport().GetMousePosition();
        if (!TryPickGround(screen, out var hit))
            return;
        // Borne à la portée du sort autour du lanceur (plan XZ).
        var p = _player.GlobalPosition;
        var (cx, cz, clamped) = SpellZone.ClampToReach(p.X, p.Z, hit.X, hit.Z, _reach);
        var h = _player.GroundHeight?.Invoke(cx, cz) ?? hit.Y;
        _decal.GlobalPosition = new Vector3(cx, h, cz);
        _decal.Modulate = clamped ? new Color(1f, 0.4f, 0.35f) : new Color(0.35f, 1f, 0.6f);
        Center = WorldConv.ToEdenGround(new Vector3(cx, h, cz));
    }

    /// <summary>Raycast caméra → terrain (marche + dichotomie sur GroundHeight, espace Godot) — même
    /// méthode que <c>PlayerController.TryPickGround</c>.</summary>
    private bool TryPickGround(Vector2 screenPos, out Vector3 hit)
    {
        hit = default;
        var cam = _cam;
        var gh = _player?.GroundHeight;
        if (cam == null || gh == null) return false;
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);
        float F(float t)
        {
            var q = origin + dir * t;
            var h = gh(q.X, q.Z);
            return h.HasValue ? q.Y - h.Value : float.NaN;
        }
        var prevT = 0f;
        for (var t = 0.5f; t <= 500f; t += 0.5f)
        {
            var f = F(t);
            if (float.IsNaN(f)) continue;
            if (f <= 0f)
            {
                var lo = prevT; var hi = t;
                for (var i = 0; i < 24; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (F(mid) > 0f) lo = mid; else hi = mid;
                }
                hit = origin + dir * ((lo + hi) * 0.5f);
                if (gh(hit.X, hit.Z) is { } g) hit = hit with { Y = g };
                return true;
            }
            prevT = t;
        }
        return false;
    }

    /// <summary>Texture d'anneau 256² : disque translucide, anneau de bord épais, anneaux intérieurs fins et
    /// 8 rayons (motif runique simplifié du ref-shot). Alpha = intensité, blanc teinté par Modulate.</summary>
    private static ImageTexture RingTexture()
    {
        if (_ringTex != null) return _ringTex;
        const int N = 256;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
            {
                var u = (x + 0.5f) / N * 2f - 1f;
                var v = (y + 0.5f) / N * 2f - 1f;
                var r = MathF.Sqrt(u * u + v * v);
                var a = 0f;
                if (r <= 1f)
                {
                    a = 0.16f;                                                    // disque
                    if (r > 0.90f)
                        a = Math.Max(a, 0.85f * Smooth(0.90f, 0.93f, r) * (1f - Smooth(0.985f, 1f, r)));   // bord
                    if (MathF.Abs(r - 0.62f) < 0.012f) a = Math.Max(a, 0.55f);    // anneau intérieur
                    if (MathF.Abs(r - 0.30f) < 0.010f) a = Math.Max(a, 0.45f);
                    var ang = MathF.Atan2(v, u);
                    var spoke = MathF.Abs(MathF.Cos(ang * 4f));                   // 8 rayons
                    if (spoke > 0.992f && r is > 0.30f and < 0.90f) a = Math.Max(a, 0.40f);
                }
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return _ringTex = ImageTexture.CreateFromImage(img);
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
