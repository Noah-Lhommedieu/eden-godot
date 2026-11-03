using System.Collections.Concurrent;
using System.Text;
using EdenClient.Data;
using EdenClient.World;

// Combinaisons demandées à l'exécution par CharVisual / CharModelSpec / tables : on les rejoue ici.
static partial class TextureAudit
{
    static readonly HashSet<string> allFiles = new();             // rel en minuscules, tout le corpus
    static readonly Dictionary<string, NifFile> megaNifs = new();  // lettre -> nif du corps

    static void IndexAllFiles()
    {
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            allFiles.Add(Path.GetRelativePath(root, f).Replace('\\', '/').ToLowerInvariant());
    }

    static void Mark(string cat, string source, string slot, string req, bool ok, string why = "") =>
        pre.Add((new Ref(cat, source, slot, req, true), ok ? St.Ok : St.Missing, why));

    // Lettres de corps et bornes de création : CharModelSpec.BodyLetter + CharCreateBodySpec.BoundsFor
    // (RaceTable.cs). code 0 g, 1 b, 2 m, 4 f, 6 k (Ursun : hair<=7, face<=1), 8 l, 9 c.
    static readonly (int code, string letter, int hairMax, int faceMax)[] Bodies =
    {
        (0, "g", 10, 4), (1, "b", 10, 4), (2, "m", 10, 4), (4, "f", 10, 4), (6, "k", 7, 1), (8, "l", 10, 4), (9, "c", 10, 4),
    };

    static void ScanCharacterCombos()
    {
        IndexAllFiles();
        foreach (var (code, letter, hairMax, faceMax) in Bodies)
        {
            var up = letter.ToUpperInvariant();
            var nifRel = $"char/model/{letter}.nif";
            Mark("corps-joueur", nifRel, "modèle", nifRel, allFiles.Contains(nifRel));
            if (!allFiles.Contains(nifRel)) continue;
            var nif = NifFile.Load(Path.Combine(root, nifRel));
            lock (megaNifs) megaNifs[letter] = nif;
            var names = nif.Shapes.Select(s => s.Name).Concat(nif.Nodes.Select(n => n.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // --- Visage : CharVisual.FaceTexture, tous (style, couleur de peau) de la création + tous les styles ayant des fichiers.
            // Chaîne exacte du runtime : « <L>FACES<ss> » (seulement si skin==0), « <L>FACES<ss><variante> », « <L>FACES<ss> », « <L>FACES<ss>A ».
            var styles = new SortedSet<int>(Enumerable.Range(1, faceMax + 1));
            foreach (var f in allFiles)
                if (f.StartsWith($"char/texture/{letter}faces") && f.Length >= $"char/texture/{letter}faces".Length + 2
                    && int.TryParse(f.AsSpan($"char/texture/{letter}faces".Length, 2), out var st)) styles.Add(st);
            foreach (var style in styles)
            for (var skin = 0; skin <= 9; skin++)
            {
                var ss = style.ToString("D2");
                var variant = (char)('A' + Math.Clamp(skin, 0, 5));
                // Le runtime essaie d'abord « <L>FACES<ss> » (skin 0 seulement) puis la variante : pour l'EXISTENCE l'ordre
                // est sans effet, on met la variante en tête pour qu'un repli signale une vraie dégradation de variante.
                var chain = new List<string>
                {
                    $"char/texture/{up}FACES{ss}{variant}.dds",
                    $"char/texture/{up}FACES{ss}.dds",
                    $"char/texture/{up}FACES{ss}A.dds",
                };
                // Combinaison proposable à la création ? sinon (styles 70+ …) informatif seulement : catégorie distincte.
                var cat = style <= faceMax + 1 ? "visages(création)" : "visages(hors bornes,info)";
                var req = string.Join("|", chain);
                refs.Add(new Ref(cat, $"corps {letter} style {ss} peau {skin}", "face", req, true));
            }

            // --- Cheveux : ApplyLook choisit le mesh #<L>1xx le plus proche de hairStyle ; il faut au moins un mesh cheveux
            // et chacun doit avoir une texture résolue (vérifié par ailleurs dans le scan des NIF) -> on liste les meshes.
            var hairMeshes = names.Where(n => n.Length >= 4 && n[0] == '#' && char.ToUpperInvariant(n[1]) == up[0] && n[2] == '1' && char.IsDigit(n[3])).ToList();
            // Corps sans coiffure par construction (Zumi/Anuran : crâne/capuche animale « #<L>HAIR » ou rien) : ApplyLook n'a
            // alors aucun mesh à choisir (hairMesh == null) et ne demande aucune texture -> rien à vérifier.
            var hasHairShape = hairMeshes.Count > 0 || names.Any(n => n.EndsWith("HAIR", StringComparison.OrdinalIgnoreCase));
            for (var h = 0; h <= hairMax; h++)
                Mark("cheveux(création)", $"corps {letter} coiffure {h}", hairMeshes.Count > 0 ? "mesh #" + up + "1xx" : "sans coiffure par conception",
                    hairMeshes.Count > 0 ? hairMeshes[0] : "(aucun mesh)", true);
            // Chaque shape AFFICHABLE du méga-modèle doit avoir un albedo nommé (NifMaterials.AlbedoMap + SourceName).
            // Affichable = ce que CharVisual.ApplyLook peut rendre visible : *FACE, *HAIR, cheveux #<L>1xx, #<L>000,
            // et toute variante tenue/peau « #<L>… » ou « S<L>… ». Le reste est masqué en permanence (helpers, boîtes).
            foreach (var sh in nif.Shapes)
            {
                var n = sh.Name;
                if (sh.IsHelper || string.IsNullOrEmpty(n)) continue;
                var displayable = n.EndsWith("FACE", StringComparison.OrdinalIgnoreCase) || n.EndsWith("HAIR", StringComparison.OrdinalIgnoreCase) ||
                    (n.Length > 2 && (n[0] == '#' || n[0] == 'S') && char.ToUpperInvariant(n[1]) == up[0]);
                if (!displayable) continue;
                var ok = AlbedoName(sh.Texturing).Length > 0;
                Mark("corps-joueur(shapes)", nifRel, "shape " + sh.Name, "albedo", ok, "shape sans texture d'albedo nommée");
            }
        }
    }
}

static partial class TextureAudit
{
    /// <summary>NifMaterials.AlbedoMap + SourceName : nom de fichier de l'albedo (Base, Detail, Dark, Decal0) ou "".</summary>
    static string AlbedoName(NifTexturingProperty? t)
    {
        var m = t == null ? null : t.Base ?? t.Detail ?? t.Dark ?? (t.Decals.Length > 0 ? t.Decals[0] : null);
        if (m == null) return "";
        var s = m.Source;
        if (s == null) return m.File ?? "";
        if (!string.IsNullOrEmpty(s.FileName)) return s.FileName;
        if (!string.IsNullOrEmpty(m.File)) return m.File!;
        var n = s.Name ?? "";
        return n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
               n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ? n : "";
    }
}
