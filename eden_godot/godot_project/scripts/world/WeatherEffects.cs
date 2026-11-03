using Godot;
using EdenClient.Data.Tables;

namespace EdenClient.World;

/// <summary>
/// Effets météo déclarés dans <c>map/model/Sxxx/Sxxx.ini</c> —
/// sections <c>[Weather]</c> (<c>WeatherSwitch</c>) et
/// <c>[WeatherParticle]</c> (une section par type).
///
/// Données constatées (5 maps : S143, S406, s1102, s504, s863) :
///   - <c>WeatherSwitch=0</c> partout → météo OFF en données natives ;
///     l'émetteur n'est construit que si le switch est à 1, ou via
///     <c>--weather</c> (<see cref="MapLoader.WeatherForce"/>).
///   - <c>ParticleType=0</c> → pluie (Rain.dds — streak tombant) ;
///     <c>ParticleType=1</c> → nappes de brume (Fog.dds — billboards
///     dérivants). Aucune autre valeur observée.
///   - <c>Particle{Base,Range,Velocity,Texture}&lt;Phase&gt;</c> par
///     phase ; Velocity = 6 floats (vx#vy#vz + ~taille/alpha —
///     HYPOTHESIS : seules les 3 premières composantes, direction de
///     chute/dérive en coords Eden Z-up, sont exploitées).
///
/// Les textures ini (Rain.dds/Fog.dds) sont absentes des assets
/// extraits → repli : Rainjungle.dds (seule texture météo présente,
/// <c>map/model/weather/</c>) pour la pluie, dégradé radial procédural
/// pour la brume.
///
/// Rendu : <see cref="GpuParticles3D"/> sous la racine « World »
/// (coords natives), boîte d'émission recentrée sur la caméra chaque
/// frame (<see cref="WeatherEmitter"/>).
/// </summary>
public static class WeatherEffects
{
    /// <summary>Construit le nœud météo de la map, ou null si la
    /// météo est désactivée en données (switch=0, pas de sections)
    /// sans forçage.</summary>
    public static Node3D? Build(MapEnvIni env, MapEnvIni.Phase phase,
        bool force)
    {
        var sw = env.Find("Weather");
        var enabled = sw != null &&
                      MapEnvIni.Int(sw, "WeatherSwitch") != 0;
        if (!enabled && !force) return null;

        var root = new Node3D { Name = "Weather" };
        var i = 0;
        foreach (var sec in env.FindAll("WeatherParticle"))
        {
            if (BuildEmitter(sec, phase, i++) is { } em)
                root.AddChild(em);
        }
        // Debug : --weather sur une map sans sections → pluie
        // synthétique (vérif visuelle uniquement, pas de données ini).
        if (force && root.GetChildCount() == 0)
            root.AddChild(DebugRain());
        return root.GetChildCount() > 0 ? root : null;
    }

    // ------------------------------------------------------------------

    private static readonly string[] PhaseSuffix =
        { "Sunrise", "Day", "Sunset", "Night" };

    /// <summary>Un émetteur par section [WeatherParticle].</summary>
    private static GpuParticles3D? BuildEmitter(MapEnvIni.Section sec,
        MapEnvIni.Phase phase, int index)
    {
        var suf = PhaseSuffix[(int)phase];
        var type = MapEnvIni.Int(sec, "ParticleType");
        var range = Vector(sec, "ParticleRange" + suf);
        var vel = Vector(sec, "ParticleVelocity" + suf);
        var texName = sec.Get("ParticleTexture" + suf) ?? string.Empty;
        var rangeV = Vec3(range, new Vector3(30f, 30f, 30f));
        var velV = Vec3(vel, new Vector3(0f, 0f, -20f));

        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial
                .EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(
                MathF.Abs(rangeV.X) * 0.5f,
                MathF.Abs(rangeV.Y) * 0.5f,
                MathF.Abs(rangeV.Z) * 0.5f),
            // Chute/dérive : direction normalisée + vitesse constante.
            Direction = velV.Normalized(),
            InitialVelocityMin = velV.Length(),
            InitialVelocityMax = velV.Length() * 1.15f,
            Gravity = Vector3.Zero,
            Spread = type == 1 ? 40f : 3f,
        };
        var emitter = new WeatherEmitter
        {
            Name = $"Wx_{index}_{type}",
            Amount = type == 1 ? 40 : 700,
            Lifetime = type == 1 ? 14f : 2.2f,
            ProcessMaterial = process,
            DrawPass1 = MakeDrawPass(type, texName),
            // Réserve de marge : la boîte suit la caméra.
            VisibilityAabb = new Aabb(
                new Vector3(-200f, -200f, -200f),
                new Vector3(400f, 400f, 400f)),
        };
        return emitter;
    }

    /// <summary>Quad billboard du draw pass — texture ini si
    /// résolvable, sinon repli (Rainjungle pour la pluie, dégradé
    /// radial procédural pour la brume). DrawPass attend un Mesh :
    /// le matériau est posé sur la PrimitiveMesh.</summary>
    private static Mesh MakeDrawPass(int type, string texName)
    {
        var tex = texName.Length > 0
            ? AssetCatalog.GetTexture($"map/texture/{texName}")
            : null;
        tex ??= type == 0
            ? AssetCatalog.GetTexture(
                "map/model/weather/Rainjungle.dds")
            : SoftDisc();
        var size = type == 0 ? new Vector2(0.06f, 0.9f)
                             : new Vector2(14f, 14f);
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = type == 0
                ? new Color(0.75f, 0.8f, 0.9f, 0.55f)
                : new Color(0.85f, 0.85f, 0.9f, 0.12f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new QuadMesh { Size = size, Material = mat };
    }

    /// <summary>Pluie synthétique de vérification (--weather sur une
    /// map sans [WeatherParticle] — aucune donnée ini n'existe pour
    /// elle ; paramètres copiés du ParticleType=0 de S143).</summary>
    private static GpuParticles3D DebugRain()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial
                .EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(15f, 15f, 30f),
            Direction = new Vector3(0f, 0.45f, -0.89f).Normalized(),
            InitialVelocityMin = 22f,
            InitialVelocityMax = 26f,
            Gravity = Vector3.Zero,
            Spread = 3f,
        };
        return new WeatherEmitter
        {
            Name = "Wx_debug_rain",
            Amount = 700,
            Lifetime = 2.2f,
            ProcessMaterial = process,
            DrawPass1 = MakeDrawPass(0, "Rain.dds"),
            VisibilityAabb = new Aabb(
                new Vector3(-200f, -200f, -200f),
                new Vector3(400f, 400f, 400f)),
        };
    }

    private static float[] Vector(MapEnvIni.Section sec, string key) =>
        MapEnvIni.Vector(sec, key);

    private static Vector3 Vec3(float[] v, Vector3 fallback) =>
        v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : fallback;

    /// <summary>Texture radiale douce 64×64 (brume — Fog.dds absente
    /// des assets).</summary>
    private static ImageTexture? _softDisc;
    private static ImageTexture SoftDisc()
    {
        if (_softDisc != null) return _softDisc;
        const int n = 64;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var dx = (x - n / 2f) / (n / 2f);
            var dy = (y - n / 2f) / (n / 2f);
            var d = MathF.Sqrt(dx * dx + dy * dy);
            var a = Math.Clamp(1f - d, 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        _softDisc = ImageTexture.CreateFromImage(img);
        return _softDisc;
    }
}

/// <summary>Émetteur particules recentré sur la caméra à chaque
/// frame (coords natives Eden sous « World » — la boîte couvre
/// toujours le champ de vue).</summary>
public partial class WeatherEmitter : GpuParticles3D
{
    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var g = cam.GlobalPosition;
        // godot → eden : (gx, −gz, gy) ; +12 en z = au-dessus de l'œil.
        Position = new Vector3(g.X, -g.Z, g.Y + 12f);
    }
}
