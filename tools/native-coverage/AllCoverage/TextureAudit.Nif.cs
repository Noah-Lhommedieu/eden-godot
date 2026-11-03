using System.Collections.Concurrent;
using EdenClient.World;

static partial class TextureAudit
{
    static string NifCategory(string rel)
    {
        var r = rel.ToLowerInvariant();
        if (r.StartsWith("char/model/"))
        {
            var n = Path.GetFileNameWithoutExtension(r);
            return n.Length == 1 ? "char-corps(mega)" : n.StartsWith("x") ? "char-equip-X" : "char-equip-lettre+num";
        }
        if (r.StartsWith("map/model/object/")) return "map-objets";
        if (r.StartsWith("map/model/sky/")) return "map-ciel";
        if (r.StartsWith("map/")) return "map-autres";
        var top = r[..r.IndexOf('/')];
        return top switch { "monster" => "monstres", "ride" => "montures", "item" => "items", "effect" => "effets", "house" => "maisons", "decorate" => "decor", _ => top };
    }

    public static int NifsRead, NifsUnreadable;

    static void ScanNifs()
    {
        var nifs = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories).ToArray();
        Parallel.ForEach(nifs, f =>
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            var cat = NifCategory(rel);
            NifFile n;
            try { n = NifFile.Load(f); if (n.Errors.Count > 0) throw new InvalidDataException(n.Errors[0]); Interlocked.Increment(ref NifsRead); }
            catch { Interlocked.Increment(ref NifsUnreadable); return; }       // les .nif illisibles sont déjà signalés par --all (map/map.nif = exécutable)
            var claimed = new HashSet<int>();
            void Slot(NifTexMap? m, string slot)
            {
                if (m == null) return;
                claimed.Add(m.SourceBlock);
                if (m.Source is not { External: true }) return;      // texture interne (pixels embarqués) : rien à résoudre
                Add(cat, rel, slot, m.Source.FileName);
            }
            foreach (var tp in n.Properties.OfType<NifTexturingProperty>())
            {
                Slot(tp.Base, "base"); Slot(tp.Dark, "dark"); Slot(tp.Detail, "detail"); Slot(tp.Gloss, "gloss");
                Slot(tp.Glow, "glow"); Slot(tp.Bump, "bump"); Slot(tp.Normal, "normal"); Slot(tp.Parallax, "parallax");
                for (var i = 0; i < tp.Decals.Length; i++) Slot(tp.Decals[i], "decal" + i);
                for (var i = 0; i < tp.ShaderMaps.Length; i++) Slot(tp.ShaderMaps[i], "shader" + i);
            }
            foreach (var c in n.Controllers)
                foreach (var (sb, i) in c.FlipSourceBlocks.Select((b, i) => (b, i)))
                {
                    claimed.Add(sb);
                    if (!n.SourceTextures.TryGetValue(sb, out var s) || !s.External || string.IsNullOrEmpty(s.FileName)) continue;
                    Add(cat, rel, "flip", s.FileName);
                }
            // Toute autre NiSourceTexture externe (particules, effets, shaders) : on la vérifie aussi.
            foreach (var (blk, s) in n.SourceTextures)
                if (!claimed.Contains(blk) && s.External && !string.IsNullOrEmpty(s.FileName))
                    Add(cat, rel, "autre-source", s.FileName);
        });
    }

    /// <summary>Toutes les .ifl du corpus (eau, montures, armes) : chaque frame doit se résoudre.</summary>
    static void ScanIfls()
    {
        foreach (var rel in iflByName.Value.Values)
            try
            {
                foreach (var fr in IflFile.Parse(File.ReadAllText(Path.Combine(root, rel))).Frames.Distinct())
                    refs.Add(new Ref("ifl(frames)", rel, "frame", fr));
            }
            catch (Exception e) { pre.Add((new Ref("ifl(frames)", rel, "lecture", e.Message, true), St.Missing, "ifl illisible")); }
    }

    /// <summary>Ajoute une référence ; une .ifl est dépliée en ses frames (chacune vérifiée) + le fichier .ifl lui-même.</summary>
    static void Add(string cat, string source, string slot, string requested)
    {
        if (requested.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase))
        {
            var iflRel = FindIfl(requested);
            if (iflRel == null) { refs.Add(new Ref(cat, source, slot + "/ifl", requested)); return; }
            try
            {
                foreach (var fr in IflFile.Parse(File.ReadAllText(Path.Combine(root, iflRel))).Frames.Distinct())
                    refs.Add(new Ref(cat, source + "→" + Path.GetFileName(iflRel), slot + "/ifl-frame", fr));
            }
            catch (Exception e) { refs.Add(new Ref(cat, source, slot + "/ifl", requested + " (" + e.Message + ")")); }
            return;
        }
        refs.Add(new Ref(cat, source, slot, requested));
    }

    static readonly Lazy<Dictionary<string, string>> iflByName = new(() =>
        Directory.EnumerateFiles(root, "*.ifl", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileName(f).ToLowerInvariant()).ToDictionary(g => g.Key, g => Path.GetRelativePath(root, g.First()).Replace('\\', '/')));
    static string? FindIfl(string name) => iflByName.Value.GetValueOrDefault(Path.GetFileName(name.Replace('\\', '/')).ToLowerInvariant());
}
