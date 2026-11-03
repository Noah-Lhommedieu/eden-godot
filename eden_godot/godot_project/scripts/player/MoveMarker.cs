using Godot;

namespace EdenClient.Player;

/// <summary>
/// Marqueur visuel d'arrivée du clic-sol — anneau plat texturé
/// (<c>effect/texture/mark-ground01.png</c>, l'indicateur de
/// destination du client natif) posé au point cliqué, pulse
/// d'apparition, disparait à l'arrivée ou au timeout.
/// </summary>
public partial class MoveMarker : Node3D
{
    /// <summary>Durée de vie de sécurité (s) — l'anneau disparaît
    /// même si le point n'est jamais atteint.</summary>
    [Export] public float LifeSeconds { get; set; } = 6f;
    /// <summary>Taille de l'anneau (u).</summary>
    [Export] public float RingSize { get; set; } = 1.6f;

    private MeshInstance3D? _ring;
    private float _t;
    private bool _active;
    private float _fade; // 0→1 apparition, puis fondu sortie

    public override void _Ready()
    {
        _ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new PlaneMesh { Size = new Vector2(RingSize, RingSize) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var tex = AssetCatalog.GetTexture("effect/texture/mark-ground01.png");
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 1f, 1f, 0f),
            NoDepthTest = false,
        };
        if (tex != null) mat.AlbedoTexture = tex;
        _ring.MaterialOverride = mat;
        AddChild(_ring);
        Visible = false;
    }

    /// <summary>Affiche l'anneau au point monde (y déjà au sol).</summary>
    public void ShowAt(Vector3 worldPos)
    {
        GlobalPosition = worldPos + new Vector3(0f, 0.07f, 0f);
        _t = 0f;
        _fade = 0f;
        _active = true;
        Visible = true;
    }

    /// <summary>Efface l'anneau (fondu rapide) — masque
    /// <see cref="CanvasItem.Hide"/> intentionnellement : « cacher »
    /// ici = lancer le fondu, pas rendre invisible d'un coup.</summary>
    public new void Hide() => _active = false;

    /// <summary>Vrai si le marqueur est affiché.</summary>
    public bool IsShowing => Visible;

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var dt = (float)delta;
        _t += dt;
        // Apparition : scale 1.35→1 + fondu 0→0.9 sur 0.25 s, puis
        // pulsation légère ; sortie : fondu vers 0.
        var targetFade = _active ? 0.9f : 0f;
        _fade += (targetFade - _fade) * Mathf.Min(1f, dt * 8f);
        var pulse = _active
            ? 1f + 0.06f * Mathf.Sin(_t * 8f)
            : 1f;
        var s = (1.35f - 0.35f * Mathf.Min(1f, _t * 5f)) * pulse;
        Scale = new Vector3(s, 1f, s);
        if (_ring?.MaterialOverride is StandardMaterial3D m)
            m.AlbedoColor = new Color(1f, 1f, 1f, _fade);
        if ((_active && _t > LifeSeconds) || (!_active && _fade < 0.02f))
            Visible = _active = false;
    }
}
