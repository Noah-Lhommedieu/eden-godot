using EdenClient.World;
using Godot;

namespace EdenClient.Native;

/// <summary>Horloge et pilotes d'un effet (créé par <see cref="NativeFx.Attach"/>) : pause quand la
/// racine est invisible, mise en veille au-delà de la distance de culling.</summary>
public sealed partial class NativeFxHost : Node
{
    public NativeFxOptions Options { get; set; } = new();
    public Node3D Root { get; set; } = null!;
    public NifFile Nif { get; set; } = null!;
    public NativeFxReport Report { get; } = new();
    /// <summary>Temps d'effet (s), n'avance pas pendant la pause.</summary>
    public float Time { get; private set; }
    public bool Culled { get; private set; }
    public bool Paused { get; set; }
    /// <summary>Particules vivantes, tous systèmes confondus (dernière mise à jour).</summary>
    public int ParticleCount { get; private set; }

    internal readonly Dictionary<int, Node3D> Nodes = new();
    /// <summary>Matériaux animables par bloc de propriété NIF (Material / Texturing / Alpha).</summary>
    internal readonly Dictionary<int, List<MatState>> MatsByProp = new();
    internal readonly List<MatState> AllMats = new();
    readonly List<IFxDriver> _drivers = new();
    readonly List<NiParticleRuntime> _particles = new();
    float _cullTimer;

    internal void Add(IFxDriver d) => _drivers.Add(d);

    internal void RegisterMat(int propBlock, MatState st)
    {
        if (propBlock < 0) return;
        if (!MatsByProp.TryGetValue(propBlock, out var l)) MatsByProp[propBlock] = l = new();
        if (!l.Contains(st)) l.Add(st);
    }

    public Node3D? NodeOf(int block) => Nodes.GetValueOrDefault(block);

    internal void Build()
    {
        IndexNodes(Root);
        BuildGeomMaterials();
        foreach (var ps in Nif.ParticleSystems)
        {
            var rt = NiParticleRuntime.Create(this, ps);
            if (rt == null) continue;
            _particles.Add(rt); _drivers.Add(rt); Report.ParticleSystems++;
        }
        NiControllerRuntime.Build(this);
        if (Options.Prewarm > 0)
            for (float t = 0; t < Options.Prewarm; t += 1f / 30f) Step(1f / 30f);
    }

    void IndexNodes(Node n)
    {
        if (n is Node3D n3 && n.HasMeta("nif_block")) Nodes[(int)n.GetMeta("nif_block")] = n3;
        foreach (var c in n.GetChildren()) if (c != this) IndexNodes(c);
    }

    void BuildGeomMaterials()
    {
        HashSet<int>? animated = null;
        if (Options.OnlyAnimatedMaterials)
            animated = Nif.Controllers.Where(c => c.Type is "NiTextureTransformController" or "NiAlphaController"
                    or "NiMaterialColorController" or "NiFlipController").Select(c => c.TargetBlock).ToHashSet();
        foreach (var sh in Nif.Shapes)
        {
            if (NodeOf(sh.BlockIndex) is not MeshInstance3D mi) continue;
            Report.Geoms++;
            if (!Options.OwnMaterials) continue;
            // Géométrie additive (plans de flamme/éclair/aile, fond noir) : le rendu toon du modèle la mélange en alpha
            // et laisse le fond noir de la texture en rectangle ; il faut le blend additif NIF même sans contrôleur.
            bool additive = NativeFx.BlendMode(sh.Alpha) == 3;
            if (animated != null && !additive && !(animated.Contains(sh.Material?.Block ?? -2) || animated.Contains(sh.Texturing?.Block ?? -2)
                                      || animated.Contains(sh.Alpha?.Block ?? -2))) continue;
            var (mat, st) = NativeFx.MakeMaterial(sh.Material, sh.Texturing, sh.Alpha, sh.ZBuffer, sh.TwoSided,
                Options, false);
            mi.MaterialOverride = mat;
            AllMats.Add(st);
            RegisterMat(sh.Material?.Block ?? -1, st);
            RegisterMat(sh.Texturing?.Block ?? -1, st);
            RegisterMat(sh.Alpha?.Block ?? -1, st);
        }
    }

    /// <summary>Avance manuelle (tests, mode headless) : ne tient pas compte de la visibilité.</summary>
    public void Step(float dt)
    {
        Time += dt;
        ParticleCount = 0;
        foreach (var d in _drivers) d.Update(Time, dt);
        foreach (var m in AllMats) m.Apply();
        foreach (var p in _particles) ParticleCount += p.Count;
    }

    public void Restart()
    {
        Time = 0;
        foreach (var p in _particles) p.Reset();
    }

    public override void _Process(double delta)
    {
        if (Paused) return;
        if (Options.PauseWhenHidden && !Root.IsVisibleInTree()) return;
        _cullTimer -= (float)delta;
        if (_cullTimer <= 0)
        {
            _cullTimer = 0.25f;
            var cam = GetViewport()?.GetCamera3D();
            bool cull = cam != null && Options.CullDistance > 0 &&
                        cam.GlobalPosition.DistanceTo(Root.GlobalPosition) > Options.CullDistance;
            if (cull != Culled)
            {
                Culled = cull;
                foreach (var d in _drivers) d.SetVisible(!cull);
            }
        }
        if (Culled) return;
        Step((float)delta * Options.TimeScale);
    }
}
