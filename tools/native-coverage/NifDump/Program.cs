// Dump des shapes d'un .nif : nom, helper, texture, géométrie.
// Usage : dotnet run -c Release --project tools/native-coverage/NifDump -- <fichier.nif> [--untextured]
using EdenClient.World;

var path = args.Length > 0 ? args[0] : throw new ArgumentException("usage: NifDump <fichier.nif> [--untextured]");
var onlyUntextured = args.Contains("--untextured");
var nif = NifFile.Load(path);
if (args.Contains("--nodes"))
{
    foreach (var kv in nif.AvByBlock.OrderBy(k => k.Key))
        Console.WriteLine($"{kv.Key,-5} {kv.Value.Type,-24} \"{kv.Value.Name}\" " +
            $"hidden={kv.Value.Hidden} parent={kv.Value.ParentBlock} " +
            $"extra=[{string.Join(",", kv.Value.Extra.Select(x => $"{x.Name}={x.StringValue}"))}]");
    return;
}
// --aabb <substr> : AABB locale (vertices) avec centre + min/max par
// axe (les transforms de nœuds des maps NIF sont identité — local ≈
// monde Eden Z-up). Sert à localiser les sols en hauteur.
var wantAabb = args.Contains("--aabb");
var aabbFilter = args.SkipWhile(a => a != "--aabb").Skip(1)
    .FirstOrDefault();
Console.WriteLine($"{nif.Shapes.Count} shapes — {path}");
foreach (var s in nif.Shapes)
{
    if (onlyUntextured && s.TextureFile != null) continue;
    if (aabbFilter != null && aabbFilter.Length > 0 &&
        !s.Name.Contains(aabbFilter, StringComparison.OrdinalIgnoreCase))
        continue;
    var min = new System.Numerics.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var max = new System.Numerics.Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var v in s.Vertices)
    {
        min = System.Numerics.Vector3.Min(min, v);
        max = System.Numerics.Vector3.Max(max, v);
    }
    var sz = max - min;
    var extra = s.Node == null ? "" : string.Join(",", s.Node.Extra.Select(x =>
        $"{x.Name}={x.StringValue}"));
    var bounds = wantAabb
        ? $" min=({min.X:F0},{min.Y:F0},{min.Z:F0}) " +
          $"max=({max.X:F0},{max.Y:F0},{max.Z:F0})"
        : "";
    var slots = "";
    if (args.Contains("--slots") && s.Texturing is { } tp)
        slots = $" slots[apply={tp.ApplyMode}]=" + string.Join(";", new (string, NifTexMap?)[]
            { ("base", tp.Base), ("dark", tp.Dark), ("detail", tp.Detail), ("glow", tp.Glow), ("bump", tp.Bump), ("normal", tp.Normal) }
            .Where(x => x.Item2 != null).Select(x => $"{x.Item1}={x.Item2!.File ?? x.Item2.Source?.FileName}"))
            + (tp.Decals.Length > 0 ? ";decals=" + string.Join(",", tp.Decals.Select(d => d.File ?? d.Source?.FileName)) : "")
            + $" vcol={(s.VertexColor == null ? "absent" : "src" + s.VertexColor.SourceMode)} alpha={(s.Alpha == null ? "-" : $"0x{s.Alpha.Flags:X}")}";
    var mat = "";
    if (args.Contains("--mat"))
        mat = s.Material == null ? " mat=-" :
            $" mat[amb=({s.Material.Ambient.X:F2},{s.Material.Ambient.Y:F2},{s.Material.Ambient.Z:F2}) diff=({s.Material.Diffuse.X:F2},{s.Material.Diffuse.Y:F2},{s.Material.Diffuse.Z:F2}) emis=({s.Material.Emissive.X:F2},{s.Material.Emissive.Y:F2},{s.Material.Emissive.Z:F2}) a={s.Material.Alpha:F2}]" +
            $" vcol={(s.VertexColor == null ? "absent" : "src" + s.VertexColor.SourceMode)} alphaProp={(s.Alpha == null ? "-" : $"0x{s.Alpha.Flags:X} blend={s.Alpha.Blend} test={s.Alpha.Test} thr={s.Alpha.Threshold}")}";
    Console.WriteLine($"{(s.IsHelper ? "H" : " ")} {(s.TextureFile != null ? "T" : "-")} " +
        $"{(s.Node?.Hidden == true ? "N" : " ")}{(s.Node?.CollisionBlock >= 0 ? "C" : " ")} " +
        $"{s.Name,-32} tris={s.Indices.Length / 3,-6} aabb=({sz.X:F1},{sz.Y:F1},{sz.Z:F1}) " +
        $"tex={s.TextureFile ?? "-"}{bounds}{slots}{mat} {extra}");
}
