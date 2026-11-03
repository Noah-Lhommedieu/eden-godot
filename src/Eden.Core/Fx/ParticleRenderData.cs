using System.Numerics;

namespace EdenClient.Fx;

/// <summary>Vue caméra pour les billboards (en espace monde).</summary>
public readonly record struct BillboardView(Vector3 Right, Vector3 Up, Vector3 Forward, Vector3 Position);

/// <summary>Tampons neutres pour un MultiMesh : 12 floats par instance
/// (3 lignes × [bx by bz o], disposition Transform3D de Godot) et 4 floats RGBA.
/// Réutilisable d'une image à l'autre (aucune allocation une fois dimensionné).</summary>
public sealed class ParticleRenderData
{
    public float[] Transforms { get; private set; } = [];
    public float[] Colors { get; private set; } = [];
    public int InstanceCount { get; private set; }

    int[] _order = [];
    float[] _depth = [];

    /// <summary>Remplit les tampons. <paramref name="sortBackToFront"/> trie du
    /// plus lointain au plus proche (mélange alpha) ; faux = ordre de simulation.
    /// Un système LocalSpace est amené en monde via sim.EmitterTransform.</summary>
    public void Fill(ParticleSim sim, in BillboardView view, bool sortBackToFront)
    {
        var ps = sim.Particles;
        int n = ps.Length;
        if (Transforms.Length < n * 12)
        {
            Transforms = new float[n * 12];
            Colors = new float[n * 4];
            _order = new int[n];
            _depth = new float[n];
        }
        InstanceCount = n;
        bool local = sim.Def.LocalSpace;
        var m = sim.EmitterTransform;

        for (int i = 0; i < n; i++)
        {
            _order[i] = i;
            Vector3 pos = local ? Vector3.Transform(ps[i].Position, m) : ps[i].Position;
            _depth[i] = Vector3.Dot(pos - view.Position, view.Forward);
        }
        if (sortBackToFront) Array.Sort(_depth, _order, 0, n, Comparer<float>.Create((a, b) => b.CompareTo(a)));

        // Après tri, _depth est permuté : on recalcule la position à la volée.
        for (int k = 0; k < n; k++)
        {
            ref readonly var p = ref ps[_order[k]];
            Vector3 pos = local ? Vector3.Transform(p.Position, m) : p.Position;
            float cs = MathF.Cos(p.Rotation), sn = MathF.Sin(p.Rotation);
            Vector3 x = (view.Right * cs + view.Up * sn) * p.Size;
            Vector3 y = (view.Up * cs - view.Right * sn) * p.Size;
            Vector3 z = view.Forward * p.Size;
            int o = k * 12;
            Transforms[o + 0] = x.X; Transforms[o + 1] = y.X; Transforms[o + 2] = z.X; Transforms[o + 3] = pos.X;
            Transforms[o + 4] = x.Y; Transforms[o + 5] = y.Y; Transforms[o + 6] = z.Y; Transforms[o + 7] = pos.Y;
            Transforms[o + 8] = x.Z; Transforms[o + 9] = y.Z; Transforms[o + 10] = z.Z; Transforms[o + 11] = pos.Z;
            int c = k * 4;
            Colors[c] = p.Color.X; Colors[c + 1] = p.Color.Y; Colors[c + 2] = p.Color.Z; Colors[c + 3] = p.Color.W;
        }
    }
}
