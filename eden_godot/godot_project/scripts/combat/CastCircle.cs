using Godot;

namespace EdenClient.Combat;

/// <summary>
/// Cercle magique d'incantation posé aux pieds du lanceur pendant un
/// cast temporisé — le glyphe doré tournant du client natif (ref-shot
/// « TimedCastSkill » : anneau runique lumineux qui tourne sous le
/// perso le temps que la barre d'incantation se vide).
///
/// Aucun modèle <c>effect/model/*circle*</c> dédié n'existe dans
/// fr-full (audit §8 : le glyphe est généré côté client) — le cercle
/// est donc construit procéduralement : deux anneaux concentriques
/// contre-rotatifs, stries radiales « runes » et disque central doux,
/// matériau additif or, fondu d'entrée/sortie.
///
/// Usage : <see cref="Begin"/> pose le cercle en enfant du lanceur
/// (il suit ses déplacements) ; <see cref="End"/> le fait disparaître
/// en fondu. Une durée &gt; 0 borne sa vie (filet de sécurité si un
/// paquet de fin est perdu).
/// </summary>
public partial class CastCircle : Node3D
{
    // Palette « or magique » EDENFR (teinte du glyphe des ref-shots).
    private static readonly Color Gold =
        new(1.0f, 0.82f, 0.28f, 0.85f);
    private static readonly Color GoldSoft =
        new(1.0f, 0.86f, 0.34f, 0.16f);

    [Export] public float Radius { get; set; } = 0.92f;
    /// <summary>Vitesse de rotation de l'anneau externe (rad/s).</summary>
    [Export] public float SpinSpeed { get; set; } = 1.35f;

    private Node3D? _ring;      // anneau externe + runes (sens horaire)
    private Node3D? _ring2;     // anneau interne (sens inverse)
    private MeshInstance3D? _disc;
    private float _elapsed;
    private float _fadeIn = 0.22f;
    private float _duration;    // 0 = jusqu'à End()
    private bool _ending;
    private float _endT;
    private StandardMaterial3D? _mat;
    private StandardMaterial3D? _matSoft;

    /// <summary>Pose le cercle sous <paramref name="host"/> (pied du
    /// lanceur). <paramref name="duration"/> &gt; 0 = auto-<see cref="End"/>
    /// passé ce délai — filet contre un 287/410/302 manquant.</summary>
    public static CastCircle? Begin(Node3D host, float duration = 0f)
    {
        if (!GodotObject.IsInstanceValid(host)) return null;
        var c = new CastCircle { Name = "CastCircle" };
        host.AddChild(c);
        c.Position = new Vector3(0f, 0.045f, 0f);
        c._duration = duration;
        c.Build();
        return c;
    }

    /// <summary>Fondu de sortie puis libération du nœud (idempotent).</summary>
    public void End()
    {
        if (_ending) return;
        _ending = true;
        _endT = 0f;
    }

    private StandardMaterial3D Mat(Color c)
    {
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
            DisableReceiveShadows = true,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = c,
        };
        return m;
    }

    private void Build()
    {
        _mat = Mat(Gold);
        _matSoft = Mat(GoldSoft);
        _ring = new Node3D { Name = "RingOuter" };
        _ring2 = new Node3D { Name = "RingInner" };
        AddChild(_ring);
        AddChild(_ring2);

        // Anneau externe (double lisère) + 16 stries radiales.
        _ring.AddChild(Mesh(RingMesh(Radius * 0.90f, Radius, 64, Gold),
            _mat));
        _ring.AddChild(Mesh(RingMesh(Radius * 0.60f, Radius * 0.63f,
            64, Gold), _mat));
        _ring.AddChild(Mesh(Runes(Radius * 0.645f, Radius * 0.885f,
            16, Gold), _mat));
        // Anneau interne contre-rotatif + pointes « glyphe ».
        _ring2.AddChild(Mesh(RingMesh(Radius * 0.34f, Radius * 0.42f,
            48, Gold), _mat));
        _ring2.AddChild(Mesh(Runes(Radius * 0.44f, Radius * 0.58f,
            8, Gold), _mat));
        // Disque central doux (halo au sol).
        _disc = Mesh(Disc(Radius * 0.90f, 48, GoldSoft), _matSoft);
        AddChild(_disc);
    }

    private MeshInstance3D Mesh(Mesh m, StandardMaterial3D mat)
    {
        var mi = new MeshInstance3D
        {
            Mesh = m,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        return mi;
    }

    /// <summary>Couronne plate (strip de quads Y=0) teintée par sommet.</summary>
    private static ImmediateMesh RingMesh(float inner, float outer,
        int segments, Color color)
    {
        var im = new ImmediateMesh();
        im.SurfaceBegin(Godot.Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= segments; i++)
        {
            var a = i * Mathf.Tau / segments;
            var (ca, sa) = (Mathf.Cos(a), Mathf.Sin(a));
            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(new Vector3(ca * inner, 0f, sa * inner));
            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(new Vector3(ca * outer, 0f, sa * outer));
        }
        im.SurfaceEnd();
        return im;
    }

    /// <summary>Stries radiales régulières (« runes » du glyphe) —
    /// petits trapèzes rayonnants qui tournent avec l'anneau.</summary>
    private static ImmediateMesh Runes(float inner, float outer,
        int count, Color color)
    {
        var im = new ImmediateMesh();
        im.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles);
        var half = Mathf.Tau / count / 12f;     // largeur angulaire ~15°
        for (var i = 0; i < count; i++)
        {
            var a = i * Mathf.Tau / count;
            foreach (var (t0, t1) in new[]
                     { (a - half, a + half) })
            {
                var p00 = new Vector3(Mathf.Cos(t0) * inner, 0f,
                    Mathf.Sin(t0) * inner);
                var p01 = new Vector3(Mathf.Cos(t0) * outer, 0f,
                    Mathf.Sin(t0) * outer);
                var p10 = new Vector3(Mathf.Cos(t1) * inner, 0f,
                    Mathf.Sin(t1) * inner);
                var p11 = new Vector3(Mathf.Cos(t1) * outer, 0f,
                    Mathf.Sin(t1) * outer);
                im.SurfaceSetColor(color);
                im.SurfaceAddVertex(p00);
                im.SurfaceAddVertex(p01);
                im.SurfaceAddVertex(p10);
                im.SurfaceAddVertex(p10);
                im.SurfaceAddVertex(p01);
                im.SurfaceAddVertex(p11);
            }
        }
        im.SurfaceEnd();
        return im;
    }

    /// <summary>Disque plein très atténué (halo sous l'anneau).</summary>
    private static ImmediateMesh Disc(float radius, int segments,
        Color color)
    {
        var im = new ImmediateMesh();
        im.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < segments; i++)
        {
            var a0 = i * Mathf.Tau / segments;
            var a1 = (i + 1) * Mathf.Tau / segments;
            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(Vector3.Zero);
            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(new Vector3(Mathf.Cos(a0) * radius, 0f,
                Mathf.Sin(a0) * radius));
            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(new Vector3(Mathf.Cos(a1) * radius, 0f,
                Mathf.Sin(a1) * radius));
        }
        im.SurfaceEnd();
        return im;
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _elapsed += d;
        if (_ring != null) _ring.RotateY(SpinSpeed * d);
        if (_ring2 != null) _ring2.RotateY(-SpinSpeed * 0.7f * d);
        var fade = Mathf.Clamp(_elapsed / _fadeIn, 0f, 1f);
        if (_ending)
        {
            _endT += d;
            fade *= Mathf.Clamp(1f - _endT / 0.22f, 0f, 1f);
            if (_endT >= 0.22f)
            {
                QueueFree();
                return;
            }
        }
        else if (_duration > 0f && _elapsed > _duration)
            End();
        if (_mat != null)
            _mat.AlbedoColor = new Color(Gold, Gold.A * fade);
        if (_matSoft != null)
            _matSoft.AlbedoColor =
                new Color(GoldSoft, GoldSoft.A * fade);
        // Légère pulsation du halo (le glyphe « respire » comme dans
        // le client natif).
        var pulse = 1f + 0.045f * Mathf.Sin(_elapsed * 5.2f);
        Scale = new Vector3(pulse, 1f, pulse);
    }
}
