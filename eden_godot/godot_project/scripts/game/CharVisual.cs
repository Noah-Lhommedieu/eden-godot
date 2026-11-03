using Godot;

namespace EdenClient.Game;

/// <summary>
/// Visuel de personnage : le GLB corps (<c>char/model/&lt;L&gt;.glb</c>)
/// est un « mega-modèle » contenant le squelette + toutes les
/// variantes d'équipement/visage/cheveux en meshes nommés
/// (<c>#BFACE</c>, <c>#B1xx</c>/#B17x = cheveux, <c>#B2xx+</c> =
/// pièces d'équipement, <c>SBxxx</c> = corps de base — texture
/// BX01AS01A « skin »).
///
/// Règle de visibilité retenue (HYPOTHESIS — la logique native de
/// toggling par item équipé n'est pas désassemblée) :
///   - visible : <c>#BFACE</c>, <c>SB*</c> (corps), et le mesh cheveux
///     <c>#&lt;L&gt;{100+hairStyle}</c> le plus proche ;
///   - caché : toute autre surface mesh.
/// </summary>
public static class CharVisual
{
    /// <summary>Instancie le GLB du corps (lettre b/m/g/f/k/l/c) et
    /// filtre les meshes pour n'afficher que visage + cheveux +
    /// tenue (équipement réel si <paramref name="worn"/> fourni,
    /// tenue de débutant sinon). Retourne null si le GLB est absent.</summary>
    /// <param name="attachAnims">false = ne pas attacher
    /// l'<see cref="EntityAnimator"/> (utilisé par les écrans qui
    /// construisent le corps en tâche de fond : l'animator est un
    /// nœud C# dont la création est interdite hors thread principal
    /// — le rattacher via <see cref="AttachAnimator"/> sur le thread
    /// principal).</param>
    public static Node3D? BuildBody(byte[]? appearance32,
        IReadOnlyDictionary<int, int>? worn = null,
        Func<int, (string Code, string ModelCode)?>? lookup = null,
        bool attachAnims = true, int classId = 0, int style = 0)
    {
        var rel = CharModelSpec.BodyModelPath(appearance32);
        if (!AssetCatalog.TryGetGlb(rel, out var abs))
            return null;
        var tt = EdenClient.Perf.PerfTrace.Mark();
        var node = AssetCatalog.LoadGlbNode(abs);
        EdenClient.Perf.PerfTrace.Report("    body LoadGlbNode", tt);
        if (node == null) return null;
        var app = appearance32 is { Length: >= 29 }
            ? Protocol.AppearanceCodec.Decode(appearance32)
            : default;
        var letter = CharModelSpec.BodyStem(appearance32);
        tt = EdenClient.Perf.PerfTrace.Mark();
        FilterMeshes(node, letter, app.HairStyle,
            OutfitSuffixes(letter, worn, lookup, classId, style));
        EdenClient.Perf.PerfTrace.Report("    body FilterMeshes", tt);
        // Texture visage réelle (style + variante peau — l'artwork
        // natif contient yeux/lèvres, contrairement aux masques
        // rouges des peaux SB*) — remplace le tint destructeur.
        tt = EdenClient.Perf.PerfTrace.Mark();
        ApplyFaceVariant(node, letter, app.BodyCode, app.FaceStyle,
            app.SkinColor, app.EyesColor, app.EyesColor2);
        EdenClient.Perf.PerfTrace.Report("    body FaceVariant", tt);
        // Teintes natives : sélecteur blob + base de section par code
        // corps (C_CharColor.ini — désasm FUN_0073e970/8d0/a60) ;
        // mémorisées en meta pour le re-tint d'ApplyEquipment.
        var skin = TintFor(app.BodyCode, app.SkinColor, skin: true);
        var hair = TintFor(app.BodyCode, app.HairColor, skin: false);
        if (OS.GetCmdlineUserArgs().Contains("--look-log"))
            GD.Print($"[look] body={app.BodyCode} hair={app.HairStyle}/{app.HairColor} skin={app.SkinColor} eyes={app.EyesColor}/{app.EyesColor2} → skin={skin} hair={hair}");
        node.SetMeta(MetaSkin, skin);
        node.SetMeta(MetaHair, hair);
        tt = EdenClient.Perf.PerfTrace.Mark();
        TintMeshes(node, skin, hair,
            OutfitRows(letter, worn, lookup, classId, style));
        EdenClient.Perf.PerfTrace.Report("    body TintMeshes", tt);
        // Anims : anims/char/animation/<L>.glb retargetées sur le
        // squelette du corps (noms de bones identiques — vérifié
        // GLB anim vs modèle). Null si absent → corps statique.
        if (attachAnims)
        {
            tt = EdenClient.Perf.PerfTrace.Mark();
            AttachAnimator(node, CharModelSpec.BodyStem(appearance32));
            EdenClient.Perf.PerfTrace.Report(
                "    body AttachAnimator", tt);
        }
        return node;
    }

    /// <summary>Construit le corps d'un personnage distant depuis son
    /// blob d'apparence 32 octets (réponse WhoIs — NCID 263) : même
    /// pipeline que le joueur local (<see cref="BuildBody"/>) — GLB
    /// corps + filtrage des meshes + animator embarqué. Null si le
    /// GLB corps est absent (placeholder conservé côté appelant).</summary>
    public static Node3D? BuildFromAppearance(byte[]? appearance32) =>
        BuildBody(appearance32);

    /// <summary>Attache un <see cref="EntityAnimator"/> au corps et
    /// précharge les anims joueur (<see cref="AnimNames.CharWanted"/>)
    /// depuis <c>anims/char/animation/&lt;letter&gt;.glb</c>. Retourne
    /// l'animator (null si le GLB d'anims est introuvable).</summary>
    public static EntityAnimator? AttachAnimator(Node3D body,
        string letter)
    {
        var animator = EntityAnimator.Attach(body,
            $"char/animation/{letter}",
            new HashSet<string>(AnimNames.CharWanted),
            modelKey: $"char/model/{letter}");
        return animator;
    }

    /// <summary>Tenue de base affichée : <c>#B202</c> + sa peau
    /// <c>SB202</c>. Chaque <c>#B2nn</c> est une tenue COMPLÈTE
    /// (bbox ~0-1.65 — vérifié) et sa peau <c>SB2nn</c> appariée
    /// (105 paires exactes sur B.glb) ne rend que la peau exposée
    /// par cette tenue (~90-600 v, zones mains/cou/jambes).
    /// « 202 » = première tenue de <c>data/db/4pequip.ini
    /// [TableBoy]</c> (item A01201 — débutant).</summary>
    private static readonly string[] BaseEquipIds = { "202" };

    /// <summary>Mesh cheveux natif pour un index de coiffure :
    /// « #&lt;L&gt;(100+i) » (HYPOTHESIS — la correspondance exacte
    /// hairStyle→mesh n'est pas prouvée ; on prend le mesh #X1yy dont
    /// le suffixe est le plus proche de l'index).</summary>
    private static void FilterMeshes(Node root, string letter,
        int hairStyle, ISet<string>? outfitSuffixes) =>
        ApplyLook(root, letter, hairStyle, outfitSuffixes);

    /// <summary>Passe de visibilité partagée (jeu <b>et</b> sélecteur
    /// — les deux affichent la même tenue) : visage + cheveux +
    /// tenues (<paramref name="outfitSuffixes"/> — suffixes
    /// numériques résolus par <see cref="OutfitSuffixes"/>, repli
    /// <see cref="BaseEquipIds"/> si vide) avec leur peau S appariée
    /// + corps nu <c>#&lt;L&gt;000</c> quand il existe (races sans
    /// squelette partagé). Retourne le nombre de meshes visibles.</summary>
    public static int ApplyLook(Node root, string letter,
        int hairStyle, ISet<string>? outfitSuffixes)
    {
        var wanted = outfitSuffixes is { Count: > 0 } s ? s
            : new HashSet<string>(BaseEquipIds,
                StringComparer.OrdinalIgnoreCase);
        var up = letter.ToUpperInvariant();

        // Passe 1 : repérer le mesh #<L>1xx le plus proche de
        // hairStyle parmi les cheveux du corps.
        string? hairMesh = null;
        var bestDist = int.MaxValue;
        var meshes = root.FindChildren("*", "MeshInstance3D", true, false)
            .Cast<MeshInstance3D>().ToArray();
        foreach (var mi in meshes)
        {
            var n = mi.Name.ToString();
            if (n.Length >= 4 && n[0] == '#' &&
                n[1].ToString().Equals(up,
                    StringComparison.OrdinalIgnoreCase) &&
                n[2] == '1' && char.IsDigit(n[3]))
            {
                var idx = ParseTail(n);
                var d = Math.Abs(idx - hairStyle);
                if (d < bestDist) { bestDist = d; hairMesh = n; }
            }
        }

        // Visible : visage (+ suffixe HAIR du modèle M) + cheveux
        // choisis + tenues/peaux de l'ensemble voulu + corps nu
        // « #<L>000 » éventuel. Tout le reste (autres tenues/peaux,
        // bones/helpers sans texture) est masqué.
        var visible = 0;
        foreach (var mi in meshes)
        {
            var n = mi.Name.ToString();
            if (n.Length == 0) { mi.Visible = false; continue; }
            var isVariant = n.Length > 2 &&
                (n[0] == '#' || n[0] == 'S') &&
                n[1].ToString().Equals(up,
                    StringComparison.OrdinalIgnoreCase);
            var show =
                n.EndsWith("FACE", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("HAIR", StringComparison.OrdinalIgnoreCase) ||
                (hairMesh != null && n == hairMesh) ||
                n == $"#{up}000" ||
                (isVariant && wanted.Contains(n[2..]));
            mi.Visible = show;
            if (show) visible++;
        }
        return visible;
    }

    // ------------------------------------------------------------------
    // Texture visage : l'artwork natif <c>&lt;L&gt;FACES&lt;style&gt;
    /// &lt;variante&gt;</c> contient yeux/lèvres/peau réels (PAS un
    /// masque rouge comme les peaux SB*) — le tint R→teinte appliqué
    /// aux autres surfaces détruisait tout détail non-rouge
    /// (« lunettes sombres sans yeux »). La lettre variante encode la
    /// couleur de peau : A..F ← skinColor 0..5 (HYPOTHESIS — la
    /// correspondance n'est pas désassemblée, mais l'énumération des
    /// fichiers colle : 6 variantes A-F par style).
    // ------------------------------------------------------------------


    // Maillages visage déjà soudés (le proto GLB est partagé : une fois).
    private static readonly HashSet<Mesh> _weldedFaces = new();

    /// <summary>Le visage est exporté en deux moitiés symétriques aux
    /// normales indépendantes : l'éclairage dessine un trait vertical au
    /// milieu du visage. On moyenne les normales des sommets confondus
    /// (même position à 1e-4 près) — la jointure devient invisible.</summary>
    private static void WeldFaceNormals(MeshInstance3D mi)
    {
        if (mi.Mesh is not ArrayMesh am) return;
        lock (_weldedFaces) { if (!_weldedFaces.Add(am)) return; }
        for (var sf = 0; sf < am.GetSurfaceCount(); sf++)
        {
            var arr = am.SurfaceGetArrays(sf);
            if (arr.Count <= (int)Mesh.ArrayType.Normal ||
                arr[(int)Mesh.ArrayType.Vertex].VariantType !=
                    Variant.Type.PackedVector3Array ||
                arr[(int)Mesh.ArrayType.Normal].VariantType !=
                    Variant.Type.PackedVector3Array)
                continue;
            var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (v.Length != n.Length || v.Length == 0) continue;
            var sum = new Dictionary<(int, int, int), Vector3>();
            (int, int, int) Key(Vector3 p) => ((int)MathF.Round(p.X * 1e4f),
                (int)MathF.Round(p.Y * 1e4f), (int)MathF.Round(p.Z * 1e4f));
            for (var i = 0; i < v.Length; i++)
            {
                var k = Key(v[i]);
                sum[k] = sum.TryGetValue(k, out var a) ? a + n[i] : n[i];
            }
            var nn = new Godot.Vector3[n.Length];
            for (var i = 0; i < v.Length; i++)
                nn[i] = sum[Key(v[i])].Normalized();
            arr[(int)Mesh.ArrayType.Normal] = nn;
            // Natif : le matériau est un override d'instance, que la reconstruction efface (surface
            // retirée = overrides perdus) — on le reporte sur la surface du mesh partagé.
            var mat = am.SurfaceGetMaterial(sf) ?? mi.GetSurfaceOverrideMaterial(sf);
            // Reconstruction de la surface (même ordre) : SurfaceRemove
            // décale les indices — on reconstruit donc tout le mesh.
            am.SurfaceRemove(sf);
            am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            // La nouvelle surface passe en dernier : remise à l'index sf.
            var last = am.GetSurfaceCount() - 1;
            if (mat != null) am.SurfaceSetMaterial(last, mat);
            if (last != sf) { /* surface unique pour un visage */ }
        }
    }

    private static Texture2D? FaceTexture(string letter,
        int faceStyle, int skinColor)
    {
        var up = letter.ToUpperInvariant();
        var variant = (char)('A' + Math.Clamp(skinColor, 0, 5));
        var style = (faceStyle + 1).ToString("D2");
        // B3 : la variante A n'est qu'un calque (sourcil/œil gris sur
        // fond transparent) — le visage complet (peau + iris) est la
        // texture sans lettre ; on la préfère pour la carnation de base.
        if (skinColor == 0 &&
            AssetCatalog.GetTexture(
                $"char/texture/{up}FACES{style}.dds") is { } full)
            return full;
        return AssetCatalog.GetTexture(
                $"char/texture/{up}FACES{style}{variant}.dds")
            ?? AssetCatalog.GetTexture(
                $"char/texture/{up}FACES{style}.dds")
            ?? AssetCatalog.GetTexture(
                $"char/texture/{up}FACES{style}A.dds");
    }

    /// <summary>Remplace l'albedo des meshes « *FACE » par la texture
    /// <c>char/texture/&lt;L&gt;FACES&lt;style+1:D2&gt;&lt;variante&gt;</c>
    /// (repli sur la version sans lettre puis variante A).</summary>
    private static void ApplyFaceVariant(Node3D body, string letter,
        int bodyCode, int faceStyle, int skinColor, int eyes1 = 0, int eyes2 = 0)
    {
        // Client natif (rf_game FUN_00ab8000) : %cFACES%02dA = surcalque RGBA (œil ouvert,
        // sourcils, cils), %cFACES%02dB = masque dye (R peau, G iris) ; C/D, E/F = autres
        // états d'expression — la lettre n'est PAS la couleur de peau.
        var up = letter.ToUpperInvariant();
        var style = (faceStyle + 1).ToString("D2");
        var ov = AssetCatalog.GetTexture($"char/texture/{up}FACES{style}A.dds");
        var mk = AssetCatalog.GetTexture($"char/texture/{up}FACES{style}B.dds");
        Texture2D? tex;
        if (ov != null && mk != null)
            tex = FaceComposite(ov, mk,
                TintFor(bodyCode, skinColor, skin: true),
                EyeCol(eyes1), EyeCol(eyes2));
        else
        {
            tex = FaceTexture(letter, faceStyle, skinColor);
            if (tex == null) return;
            tex = (skinColor & 1) == 1
                ? TintedFaceMask(tex, TintFor(bodyCode, skinColor, skin: true))
                : FaceOverSkin(tex, TintFor(bodyCode, skinColor, skin: true));
        }
        foreach (var mi in body
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            var n = mi.Name.ToString();
            // « #<L>HAIR » = capuche-tête animale (Zumi #MHAIR) — dans
            // le GLB elle partage la texture du #XFACE : le natif pose
            // le même atlas FACES dessus (fourrure = variante peau).
            if (mi.Mesh != null &&
                n.EndsWith("FACE", StringComparison.OrdinalIgnoreCase))
                WeldFaceNormals(mi);
            if (mi.Mesh == null ||
                !(n.EndsWith("FACE", StringComparison.OrdinalIgnoreCase) ||
                  n.Equals($"#{letter.ToUpperInvariant()}HAIR",
                      StringComparison.OrdinalIgnoreCase)))
                continue;
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(i) ??
                    mi.Mesh.SurfaceGetMaterial(i);
                switch (mat)
                {
                    case StandardMaterial3D sm:
                        var dup = (StandardMaterial3D)sm.Duplicate();
                        dup.AlbedoTexture = tex;
                        mi.SetSurfaceOverrideMaterial(i, dup);
                        break;
                    case ShaderMaterial shm:
                        var sdup = (ShaderMaterial)shm.Duplicate();
                        sdup.SetShaderParameter("albedo_tex", tex);
                        mi.SetSurfaceOverrideMaterial(i, sdup);
                        break;
                }
            }
        }
    }

    /// <summary>Teintes peau/cheveux/visage : les textures char sont
    /// des masques rouges (canal R = intensité — vérifié
    /// BX01AS01A/BFACES01B/B1H00S01 = rouge pur en DXT1) que le natif
    /// teinte avec la couleur choisie à la création. On re-colore
    /// l'image : out = teinte × r/max (le max normalise le masque).
    /// La teinte vient de <see cref="Data.CharColorTable"/> —
    /// sélecteur + base par code corps (preuve désasm dans le
    /// summary de la table).</summary>
    private static readonly Godot.Color SkinTintFallback =
        new(1.0f, 0.82f, 0.66f);  // carnation claire anime
    private static readonly Godot.Color HairTintFallback =
        new(0.55f, 0.34f, 0.2f);  // brun moyen

    /// <summary>Clés meta du corps : teintes résolues (réutilisées par
    /// <see cref="ApplyEquipment"/> qui n'a pas le blob).</summary>
    private const string MetaSkin = "eden_tint_skin";
    private const string MetaHair = "eden_tint_hair";

    /// <summary>Teinte d'un sélecteur blob via la table native ;
    /// repli sur les teintes fixes historiques si sel &lt; 0.</summary>
    private static Godot.Color TintFor(int bodyCode, int selector,
        bool skin) => selector >= 0
        ? (skin ? Data.CharColorTable.SkinColor(bodyCode, selector)
                : Data.CharColorTable.HairTint(bodyCode, selector))
        : (skin ? SkinTintFallback : HairTintFallback);

    private static readonly Dictionary<(Texture2D, uint), Texture2D>
        _tintCache = new();
    // Verrou dédié : Tinted/Dyed peuvent tourner en tâche de fond
    // (preview async — BuildBody hors thread principal).
    private static readonly object _tintLock = new();

    private static void TintMeshes(Node root, Godot.Color skin,
        Godot.Color hairC,
        IReadOnlyDictionary<string, CharModelSpec.EquipMesh>? rows =
            null)
    {
        var td = EdenClient.Perf.PerfTrace.Mark();
        // Filtre mi.Visible : les deux appelants (BuildBody via
        // ApplyLook, ApplyEquipment via ApplyOutfitVisibility) ont
        // DÉJÀ fixé la visibilité — teinter les ~300 tenues non
        // portées du méga-modèle coûtait des minutes. Une tenue
        // réaffichée plus tard est teintée par le TintMeshes
        // d'ApplyEquipment (cache idempotent).
        var meshes = root
            .FindChildren("*", "MeshInstance3D", true, false)
            .Cast<MeshInstance3D>()
            .Where(mi => mi.Mesh != null && mi.Visible).ToArray();
        // Séquentiel, jamais Parallel.ForEach : Texture2D.GetImage()
        // est un readback RenderingServer résolu seulement quand le
        // thread principal pompe une frame — appelé depuis un worker
        // pendant que le main attend le ForEach = deadlock (mesuré :
        // workers bloqués >300 s sur SB202/#B202/#B105, --char=b).
        var nT = 0; var nD = 0;
        foreach (var mi in meshes)
        {
            var n = mi.Name.ToString();
            // Peau (SB*) → teinte peau réelle ; cheveux (#X1xx) →
            // teinte cheveux ; tenues (#X2xx) → masque dye 3 canaux.
            // Le VISAGE (*FACE) est exclu : son artwork natif porte
            // les yeux/lèvres en vraies couleurs — la texture variante
            // (style+peau) posée par ApplyFaceVariant suffit.
            Godot.Color? tint = null;
            float floor = 0f;
            if (n.Length > 2 && n[0] == 'S' && char.IsLetter(n[1]))
            {
                tint = skin;
                floor = -SkinGain;
            }
            // Corps nu « #<L>000 » : même masque rouge de peau que les
            // S* — sans teinte il sortait en rouge vif sous les tenues.
            else if (n.Length == 5 && n[0] == '#' && char.IsLetter(n[1]) &&
                     n.EndsWith("000"))
            {
                tint = skin;
                floor = -SkinGain;
            }
            else if (n.Length >= 4 && n[0] == '#' && n[2] == '1' &&
                     char.IsDigit(n[3]))
            {
                tint = hairC;
                // Le masque cheveux est une carte d'intensité R —
                // le calque/cap interne échantillonne le fond noir :
                // sans plancher la teinte sort purement noire alors
                // que le natif garde un ombrage sombre teinté.
                // 0.7 : les zones noires de l'atlas (cap interne, dessous
                // des mèches) restent de la couleur des cheveux — à 0.35
                // elles sortaient en gros aplats noirs sur le visage.
                floor = -2f;       // gain ×2 (voir Tinted)
            }
            if (tint != null)
            {
                TintMeshSurfaces(mi, tint.Value, floor);
                nT++;
                continue;
            }
            // Masques dye des tenues : pixels à un seul canal
            // (R,G,B ≈ zones teintables — vérifié B202AS01B = pixels
            // purs par canal). Composite canal→teinte textile avec
            // les couleurs natives de la ligne 4pequip.
            if (n.Length >= 4 && n[0] == '#' && n[2] == '2' &&
                char.IsDigit(n[3]))
            {
                var suffix = n[2..];
                var row = (rows != null && rows.TryGetValue(suffix,
                        out var r) ? r : null)
                    ?? CharModelSpec.RowForSuffix(
                        n[1].ToString().ToLowerInvariant(), suffix);
                DyeMeshSurfaces(mi, row);
                nD++;
            }
            else if (EdenClient.Perf.PerfTrace.Enabled)
                GD.Print($"[dye] hors-pattern {n}");
        }
        EdenClient.Perf.PerfTrace.Report(
            $"    body TintMeshes nT={nT} nD={nD} " +
            $"dyeComposites={_dyeComposites}", td);
    }

    /// <summary>Re-teinte les meshes visibles après un changement de
    /// visibilité hors <see cref="ApplyEquipment"/> (CharDressing
    /// montre la vraie tenue portée après le teintage de BuildBody —
    /// les tenues cachées à cet instant n'ont pas été teintées).
    /// Teintes relues dans les metas posées par BuildBody (repli
    /// fixes si le corps n'y a pas transité). Idempotent : le cache
    /// rend les surfaces déjà recolorées quasi gratuites.</summary>
    public static void RetintVisible(Node3D body,
        IReadOnlyDictionary<string, CharModelSpec.EquipMesh>? rows =
            null) =>
        TintMeshes(body,
            body.GetMeta(MetaSkin, Variant.From(SkinTintFallback))
                .AsColor(),
            body.GetMeta(MetaHair, Variant.From(HairTintFallback))
                .AsColor(), rows);

    private static void TintMeshSurfaces(MeshInstance3D mi,
        Godot.Color tint, float floor = 0f)
    {
        int nOk = 0, nNull = 0;
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
        {
            var mat = mi.GetSurfaceOverrideMaterial(i) ??
                mi.Mesh.SurfaceGetMaterial(i);
            switch (mat)
            {
                case StandardMaterial3D sm:
                    if (sm.AlbedoTexture == null ||
                        IsTintProduct(sm.AlbedoTexture))
                    { nNull++; break; }
                    nOk++;
                    var dup = (StandardMaterial3D)sm.Duplicate();
                    dup.AlbedoTexture =
                        Tinted(sm.AlbedoTexture, tint, floor);
                    mi.SetSurfaceOverrideMaterial(i, dup);
                    break;
                case ShaderMaterial shm:
                    if (shm.GetShaderParameter("albedo_tex").Obj
                        is Texture2D t && !IsTintProduct(t))
                    {
                        nOk++;
                        var sdup = (ShaderMaterial)shm.Duplicate();
                        sdup.SetShaderParameter("albedo_tex",
                            Tinted(t, tint, floor));
                        mi.SetSurfaceOverrideMaterial(i, sdup);
                    }
                    else nNull++;
                    break;
                default:
                    nNull++;
                    break;
            }
        }
        if (EdenClient.Perf.PerfTrace.Enabled)
            GD.Print($"[tint] {mi.Name} ok={nOk} nullTex={nNull}");
    }

    /// <summary>Teintes de repli des canaux dye quand la ligne
    /// 4pequip est introuvable (tenue hors table).</summary>
    private static readonly Godot.Color DyeR =
        new(0.62f, 0.22f, 0.2f);   // canal R → cramoisi
    private static readonly Godot.Color DyeG =
        new(0.22f, 0.4f, 0.62f);   // canal G → indigo
    private static readonly Godot.Color DyeB =
        new(0.78f, 0.74f, 0.62f);  // canal B → lin

    /// <summary>Canal dye décodé d'un short du spec 4pequip —
    /// <see cref="Dyed"/>.</summary>
    private readonly record struct DyeChannel(Godot.Color C1,
        Godot.Color C2, float Blend, bool Has);

    /// <summary>Décode le spec dye 48 bits de la ligne 4pequip en
    /// 4 canaux (R,G,B + un 4e inutilisé) — ordre little-endian
    /// (preuve FUN_00ab6b30 client_game) :
    ///   - bit15=0 → couleur RGB555 directe
    ///     (<c>& 0x7FFF</c> indexe la palette RGB555 précalculée
    ///     <c>DAT_010c7a80</c> remplie par FUN_00d10c70) ;
    ///   - bit15=1 → deux indices 6 bits dans la palette DYE_*
    ///     1-64 de C_CharColor (<c>DAT_010c0f60</c>) + blend 3 bits
    ///     (1-8 — dégradé entre les deux couleurs).
    ///   - 0 → canal sans couleur (défaut objet natif — ignoré).</summary>
    private static DyeChannel[] DecodeDye(ulong spec)
    {
        var ch = new DyeChannel[4];
        for (var i = 0; i < 4; i++)
        {
            var v = (ushort)((spec >> (16 * i)) & 0xFFFF);
            if (v == 0) continue;
            if ((v & 0x8000) == 0)
            {
                ch[i] = new DyeChannel(
                    new Godot.Color((v & 31) / 31f,
                        ((v >> 5) & 31) / 31f,
                        ((v >> 10) & 31) / 31f),
                    default, 0f, true);
            }
            else
            {
                var c1 = Data.CharColorTable.Dye(v & 0x3F);
                var c2 = Data.CharColorTable.Dye((v >> 6) & 0x3F);
                ch[i] = new DyeChannel(
                    new Godot.Color(c1.R, c1.G, c1.B),
                    new Godot.Color(c2.R, c2.G, c2.B),
                    ((v >> 12) & 7) + 1f, true);
            }
        }
        return ch;
    }

    /// <summary>Applique le composite dye sur toutes les surfaces du
    /// mesh : albedo = masque canaux <c>&lt;skin&gt;B</c> teinté par
    /// les couleurs natives du spec de <paramref name="row"/>
    /// (repli cramoisi/indigo/lin si spec=0). L'artwork
    /// <c>&lt;skin&gt;A</c> (détails peints — sangles, métal) reste
    /// visible là où le masque est noir.</summary>
    private static void DyeMeshSurfaces(MeshInstance3D mi,
        CharModelSpec.EquipMesh? row)
    {
        var spec = row?.Dye ?? 0;
        Texture2D? mask = row is { Skin.Length: > 0 } r
            ? AssetCatalog.GetTexture($"char/texture/{r.Skin}B.dds")
            : null;
        var art = row is { Skin.Length: > 0 } r2
            ? AssetCatalog.GetTexture($"char/texture/{r2.Skin}A.dds")
            : null;
        int nOk = 0, nNull = 0, nSkip = 0;
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
        {
            var mat = mi.GetSurfaceOverrideMaterial(i) ??
                mi.Mesh.SurfaceGetMaterial(i);
            Texture2D? tex = mat switch
            {
                StandardMaterial3D sm => sm.AlbedoTexture,
                ShaderMaterial shm => shm.GetShaderParameter(
                    "albedo_tex").Obj as Texture2D,
                _ => null,
            };
            if (tex == null)
            {
                if (mask == null) { nNull++; continue; }
                // Surface sans albedo (ex. #B248 — armure overlay) :
                // le composite du masque devient l'albedo, sinon la
                // surface reste au matériau de base ≈ noir.
                tex = mask;
            }
            if (IsTintProduct(tex)) { nSkip++; continue; }
            nOk++;
            var dyed = Dyed(mask ?? tex, spec, art);
            switch (mat)
            {
                case StandardMaterial3D sm:
                    var dup = (StandardMaterial3D)sm.Duplicate();
                    dup.AlbedoTexture = dyed;
                    mi.SetSurfaceOverrideMaterial(i, dup);
                    break;
                case ShaderMaterial shm:
                    var sdup = (ShaderMaterial)shm.Duplicate();
                    sdup.SetShaderParameter("albedo_tex", dyed);
                    mi.SetSurfaceOverrideMaterial(i, sdup);
                    break;
                case null:
                    mi.SetSurfaceOverrideMaterial(i,
                        new StandardMaterial3D { AlbedoTexture = dyed });
                    break;
            }
        }
        if (EdenClient.Perf.PerfTrace.Enabled)
            GD.Print($"[dye] {mi.Name} row={(row == null ? "-" : row.Mesh)} " +
                $"skin={row?.Skin ?? "-"} spec={spec:X} " +
                $"mask={(mask != null)} art={(art != null)} " +
                $"ok={nOk} nullTex={nNull} skip={nSkip}");
    }

    /// <summary>Composite dye : out = r/max·DyeR + g/max·DyeG +
    /// b/max·DyeB (normalisé par le max global — même principe que
    /// <see cref="Tinted"/>). Cache par texture.</summary>
    /// <summary>Textures produites par Tinted/Dyed — le re-tint
    /// d'ApplyEquipment doit sauter les surfaces déjà recolorées
    /// (sinon la teinte s'applique en double sur un masque déjà
    /// coloré → couleurs corrompues).</summary>
    private static readonly HashSet<ulong> _tintProducts = new();
    public static int _dyeComposites;   // compteur diag composites réels

    private static bool IsTintProduct(Texture2D tex)
    {
        lock (_tintLock)
            return _tintProducts.Contains(tex.GetInstanceId());
    }

    private static void RegisterTint(Texture2D key, uint k2,
        Texture2D tex)
    {
        lock (_tintLock)
        {
            _tintCache[(key, k2)] = tex;
            _tintProducts.Add(tex.GetInstanceId());
        }
    }

    // Boucles octet-directes sur le buffer RGBA8 — le GetPixel/
    // SetPixel pixel-par-pixel prenait ~26 s sur les ~100 textures
    // de peau du méga-modèle (profil sélecteur perso).
    private static Texture2D Dyed(Texture2D src, ulong spec,
        Texture2D? art)
    {
        var key = spec;
        lock (_tintLock)
            if (_tintCache.TryGetValue((src, (uint)key ^ (uint)(key >> 32)),
                    out var cached))
                return cached;
        // spec 0 = « pas de teinte » natif (variantes couleur fixe
        // comme b2053s01black) : tous les canaux ignorés → l'artwork
        // s'affiche tel quel ; le repli cramoisi/indigo ne sert qu'aux
        // tenues hors table 4pequip (row null → art null ici).
        _dyeComposites++;
        var ch = DecodeDye(spec);
        if (spec == 0 && art != null)
        {
            // Spec 0 = teinte native neutre : A + B (canaux en blanc).
            var white = new DyeChannel(new Godot.Color(1f, 1f, 1f), default, 0f, true);
            ch[0] = ch[1] = ch[2] = white;
        }
        else if (!ch[0].Has && !ch[1].Has && !ch[2].Has)
        {
            ch[0] = new DyeChannel(DyeR, default, 0f, true);
            ch[1] = new DyeChannel(DyeG, default, 0f, true);
            ch[2] = new DyeChannel(DyeB, default, 0f, true);
        }
        var img = src.GetImage();
        if (img == null) return src;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        if (img.HasMipmaps()) img.ClearMipmaps();
        var data = img.GetData();
        // Artwork A (détails peints) — visible là où le masque est
        // noir ; tailles différentes → échantillonnage UV.
        byte[]? artData = null;
        int aw = 0, ah = 0;
        if (art?.GetImage() is { } ai)
        {
            ai = (Image)ai.Duplicate();
            if (ai.IsCompressed()) ai.Decompress();
            ai.Convert(Image.Format.Rgba8);
            if (ai.HasMipmaps()) ai.ClearMipmaps();
            if (ai.GetWidth() > 0)
            {
                artData = ai.GetData();
                aw = ai.GetWidth();
                ah = ai.GetHeight();
            }
        }
        var w = img.GetWidth();
        var h = img.GetHeight();
        for (var i = 0; i < data.Length; i += 4)
        {
            // Composition ADDITIVE (régression sur G202AS01C : C ≈ A + Σ Bᵢ·dyeᵢ,
            // erreur 0,0135) : B porte l'ombrage par canal, A (détails peints :
            // coutures, rivets, bordures) s'ajoute PARTOUT — l'ancienne version
            // l'atténuait par (1 − masque) et normalisait B → aplats.
            var kr = data[i] / 255f;
            var kg = data[i + 1] / 255f;
            var kb = data[i + 2] / 255f;
            float cRr = 0, cRg = 0, cRb = 0, cGr = 0, cGg = 0,
                cGb = 0, cBr = 0, cBg = 0, cBb = 0;
            ChannelColor(ch[0], kr, out cRr, out cRg, out cRb);
            // Ordre R,G,B → couleurs 1,2,3 : confirmé sur 12 tenues ayant une texture C officielle
            // (erreur moyenne 0,103 contre ≥ 0,105 pour toute autre permutation).
            ChannelColor(ch[1], kg, out cGr, out cGg, out cGb);
            ChannelColor(ch[2], kb, out cBr, out cBg, out cBb);
            float ar = 0, ag = 0, ab = 0;
            if (artData != null)
            {
                var x = ((i / 4) % w) * aw / w;
                var y = ((i / 4) / w) * ah / h;
                var j = (y * aw + x) * 4;
                var aa = artData[j + 3] / 255f;      // A = surcalque RGBA
                ar = artData[j] * aa;
                ag = artData[j + 1] * aa;
                ab = artData[j + 2] * aa;
            }
            // Îlots UV sans masque NI artwork (noir pur, ex. épaulières) : teinte du
            // canal 0 assombrie plutôt qu'un trou noir (C est noir là aussi).
            if (kr + kg + kb <= 0.02f && ar + ag + ab < 6f && ch[0].Has)
            {
                ar = 255f * ch[0].C1.R * 0.55f;
                ag = 255f * ch[0].C1.G * 0.55f;
                ab = 255f * ch[0].C1.B * 0.55f;
            }
            data[i] = (byte)Math.Clamp((int)(ar + cRr + cGr + cBr),
                0, 255);
            data[i + 1] = (byte)Math.Clamp((int)(ag + cRg + cGg + cBg),
                0, 255);
            data[i + 2] = (byte)Math.Clamp((int)(ab + cRb + cGb + cBb),
                0, 255);
        }
        var outImg = Image.CreateFromData(w, h, false,
            Image.Format.Rgba8, data);
        if (outImg.GetWidth() >= 64)
            outImg.GenerateMipmaps();
        // Pas de recompression S3TC : l'encodeur CPU coûtait ~0,3 s
        // par texture (×~100 = 38 s au build du corps) — ces sorties
        // restent RGBA8, relues au pixel par les re-tints.
        var tex = ImageTexture.CreateFromImage(outImg);
        if (tex == null) return src;
        RegisterTint(src, (uint)key ^ (uint)(key >> 32), tex);
        return tex;
    }

    /// <summary>Contribution d'un canal dye : intensité masque
    /// <paramref name="k"/> × couleur (dégradé c1→c2 si
    /// <paramref name="ch"/>.Blend &gt; 0 — la pente suit le champ
    /// blend du spec).</summary>
    private static void ChannelColor(DyeChannel ch, float k,
        out float r, out float g, out float b)
    {
        r = g = b = 0f;
        if (!ch.Has || k <= 0f) return;
        var c = ch.C1;
        if (ch.Blend > 0f)
        {
            var t = MathF.Pow(Math.Clamp(k, 0f, 1f), ch.Blend);
            c = new Godot.Color(
                ch.C1.R + (ch.C2.R - ch.C1.R) * t,
                ch.C1.G + (ch.C2.G - ch.C1.G) * t,
                ch.C1.B + (ch.C2.B - ch.C1.B) * t);
        }
        // Gain ×2 (modulate 2x Gamebryo) : sans lui les armures sortent trop sombres.
        r = 510f * c.R * k;
        g = 510f * c.G * k;
        b = 510f * c.B * k;
    }

    /// <summary>Texture teintée (cache par texture+teinte+plancher) :
    /// masque rouge → out = teinte × (floor + (1-floor)·r/rMax).
    /// Le plancher préserve l'ombrage sombre teinté où le masque
    /// est noir (cap interne des cheveux).</summary>
    private static Texture2D Tinted(Texture2D src, Godot.Color tint,
        float floor = 0f)
    {
        var key = tint.ToRgba32() ^ (uint)(int)(floor * 1024f);
        lock (_tintLock)
            if (_tintCache.TryGetValue((src, key), out var cached))
                return cached;
        var img = src.GetImage();
        if (img == null) return src;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        if (img.HasMipmaps()) img.ClearMipmaps();
        var data = img.GetData();
        var max = 1;
        for (var i = 0; i < data.Length; i += 4)
            if (data[i] > max) max = data[i];
        var inv = 1f / max;
        var span = 1f - floor;
        for (var i = 0; i < data.Length; i += 4)
        {
            // floor < 0 : mode « gain direct » (|floor| = gain, sans normalisation ni plancher) —
            // le masque R plafonne à ~0,48 (cheveux) : conçu pour un modulate ×2 natif.
            var k = floor < 0f ? -floor * (data[i] / 255f) : floor + span * (data[i] * inv);
            data[i] = (byte)Math.Clamp((int)(255f * tint.R * k),
                0, 255);
            data[i + 1] = (byte)Math.Clamp((int)(255f * tint.G * k),
                0, 255);
            data[i + 2] = (byte)Math.Clamp((int)(255f * tint.B * k),
                0, 255);
        }
        var outImg = Image.CreateFromData(img.GetWidth(),
            img.GetHeight(), false, Image.Format.Rgba8, data);
        if (outImg.GetWidth() >= 64)
            outImg.GenerateMipmaps();
        // Idem Dyed : sortie RGBA8 — la recompression S3TC CPU
        // coûtait des dizaines de secondes sur le corps complet.
        var tex = ImageTexture.CreateFromImage(outImg);
        if (tex == null) return src;
        RegisterTint(src, key, tex);
        return tex;
    }

    /// <summary>Teinte un atlas visage « masque rouge » (variantes
    /// impaires B/D/F de <c>&lt;L&gt;FACES</c>) : seuls les pixels à
    /// dominance rouge (fourrure/peau, r = intensité) sont recolorés
    /// par <paramref name="tint"/> — yeux/dents en vraies couleurs
    /// (verts, blancs) et l'alpha sont préservés.</summary>

    /// <summary>Calque visage à fond transparent (races ≠ B : yeux/sourcils
    /// sur alpha 0) composé sur la couleur peau → texture OPAQUE. Les
    /// pixels de jointure entre les deux moitiés du visage (alpha 0) ne
    /// laissent plus voir le fond : plus de trait vertical au centre.</summary>
    private static Godot.Color EyeCol(int sel)
    {
        var c = Data.CharColorTable.Eyes(sel);
        return new Godot.Color(c.R, c.G, c.B);
    }

    /// <summary>Gain « modulate ×2 » Gamebryo sur le masque peau (corps) et sur le terme
    /// kr·peau du visage ; réglables (--skin-gain / --face-gain du SkinViewer) pour la mesure.
    /// FaceSkinGain = 1,35 — calibré sur la ref-shot chevalier (visage 234,188,130,
    /// G/R 0,81) : le masque visage (kr moyen ~0,71, p90 0,81 vs corps 0,58/0,84)
    /// sature dès ~1,52 (kr·0,81·gain ≥ 1) ; 1,35 donne (234,189,142) mesuré.</summary>
    public static float SkinGain = 2f;
    public static float FaceSkinGain = 1.35f;

    private static Texture2D FaceComposite(Texture2D ov, Texture2D mk,
        Godot.Color skin, Godot.Color eye1, Godot.Color eye2)
    {
        var key = skin.ToRgba32() ^ (eye1.ToRgba32() * 31u) ^ (eye2.ToRgba32() * 131u) ^ ((uint)(FaceSkinGain * 4096f) * 2654435761u);
        lock (_tintLock)
            if (_tintCache.TryGetValue((mk, key), out var cached)) return cached;
        var mi = mk.GetImage(); var oi = ov.GetImage();
        if (mi == null || oi == null) return mk;
        mi = (Image)mi.Duplicate(); oi = (Image)oi.Duplicate();
        if (mi.IsCompressed()) mi.Decompress();
        if (oi.IsCompressed()) oi.Decompress();
        mi.Convert(Image.Format.Rgba8); oi.Convert(Image.Format.Rgba8);
        if (mi.HasMipmaps()) mi.ClearMipmaps();
        if (oi.GetWidth() != mi.GetWidth() || oi.GetHeight() != mi.GetHeight())
            oi.Resize(mi.GetWidth(), mi.GetHeight(), Image.Interpolation.Bilinear);
        var m = mi.GetData(); var o = oi.GetData();
        for (var i = 0; i < m.Length; i += 4)
        {
            // Σ Bᵢ·cᵢ + A.rgb·A.α (même modèle que les armures, erreur 0,009 mesurée).
            float kr = m[i] / 255f, kg = m[i + 1] / 255f, kb = m[i + 2] / 255f;
            var a = o[i + 3] / 255f;
            var r = kr * skin.R * FaceSkinGain + kg * eye1.R + kb * eye2.R + o[i] / 255f * a;
            var g = kr * skin.G * FaceSkinGain + kg * eye1.G + kb * eye2.G + o[i + 1] / 255f * a;
            var b = kr * skin.B * FaceSkinGain + kg * eye1.B + kb * eye2.B + o[i + 2] / 255f * a;
            m[i] = (byte)Math.Clamp((int)(r * 255f), 0, 255);
            m[i + 1] = (byte)Math.Clamp((int)(g * 255f), 0, 255);
            m[i + 2] = (byte)Math.Clamp((int)(b * 255f), 0, 255);
            m[i + 3] = 255;
        }
        var outImg = Image.CreateFromData(mi.GetWidth(), mi.GetHeight(), false, Image.Format.Rgba8, m);
        if (outImg.GetWidth() >= 64) outImg.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(outImg);
        if (tex == null) return mk;
        RegisterTint(mk, key, tex);
        return tex;
    }

    private static Texture2D FaceOverSkin(Texture2D src, Godot.Color skin)
    {
        var key = skin.ToRgba32() ^ 0x40000000u;
        lock (_tintLock)
            if (_tintCache.TryGetValue((src, key), out var cached))
                return cached;
        var img = src.GetImage();
        if (img == null) return src;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        if (img.HasMipmaps()) img.ClearMipmaps();
        var data = img.GetData();
        var hasAlpha = false;
        for (var i = 3; i < data.Length; i += 4)
            if (data[i] < 250) { hasAlpha = true; break; }
        if (!hasAlpha) return src;
        for (var i = 0; i < data.Length; i += 4)
        {
            var a = data[i + 3] / 255f;
            data[i] = (byte)(255f * (skin.R * (1f - a) + data[i] / 255f * a));
            data[i + 1] = (byte)(255f * (skin.G * (1f - a) + data[i + 1] / 255f * a));
            data[i + 2] = (byte)(255f * (skin.B * (1f - a) + data[i + 2] / 255f * a));
            data[i + 3] = 255;
        }
        var outImg = Image.CreateFromData(img.GetWidth(), img.GetHeight(),
            false, Image.Format.Rgba8, data);
        outImg.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(outImg);
        if (tex == null) return src;
        RegisterTint(src, key, tex);
        return tex;
    }

    private static Texture2D TintedFaceMask(Texture2D src,
        Godot.Color tint)
    {
        // Marqueur haut pour ne pas collisionner avec les entrées
        // « tint intégral » du même cache.
        var key = tint.ToRgba32() ^ 0x80000000u;
        lock (_tintLock)
            if (_tintCache.TryGetValue((src, key), out var cached))
                return cached;
        var img = src.GetImage();
        if (img == null) return src;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        if (img.HasMipmaps()) img.ClearMipmaps();
        var data = img.GetData();
        var max = 1;
        for (var i = 0; i < data.Length; i += 4)
            if (IsMaskPx(data, i) && data[i] > max) max = data[i];
        var inv = 1f / max;
        for (var i = 0; i < data.Length; i += 4)
        {
            if (!IsMaskPx(data, i)) continue;
            var k = data[i] * inv;
            data[i] = (byte)Math.Clamp((int)(255f * tint.R * k),
                0, 255);
            data[i + 1] = (byte)Math.Clamp((int)(255f * tint.G * k),
                0, 255);
            data[i + 2] = (byte)Math.Clamp((int)(255f * tint.B * k),
                0, 255);
        }
        var outImg = Image.CreateFromData(img.GetWidth(),
            img.GetHeight(), false, Image.Format.Rgba8, data);
        if (outImg.GetWidth() >= 64)
            outImg.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(outImg);
        if (tex == null) return src;
        RegisterTint(src, key, tex);
        return tex;
    }

    /// <summary>Pixel « fourrure » du masque : rouge dominant
    /// (r &gt; 2×g et r &gt; 2×b) et non négligeable — le reste
    /// (œil vert, dent claire, fond) est de l'artwork coloré.</summary>
    private static bool IsMaskPx(byte[] d, int i) =>
        d[i] > 8 && d[i] > d[i + 1] * 2 && d[i] > d[i + 2] * 2;

    /// <summary>Dernier entier du nom (« #B117 » → 17, « #B2109 » →
    /// 109… grossier : on prend le numéro après le 3e caractère).</summary>
    private static int ParseTail(string name)
    {
        var i = 3;
        var v = 0;
        var any = false;
        while (i < name.Length && char.IsDigit(name[i]))
        {
            v = v * 10 + (name[i] - '0');
            i++;
            any = true;
        }
        return any ? v : -1;
    }

    // ------------------------------------------------------------------
    // Équipement porté → visuel (lot S2)
    //
    // Convention 4pequip.ini (CharModelSpec.LoadEquipMap) :
    //   - mesh « #B230 » → mesh du mega-GLB corps + peau « SB230 »
    //     (le suffixe numérique = la tenue ; le skin apparié rend la
    //     peau exposée par cette pièce) ;
    //   - mesh « B1246 »/« X652A » → GLB séparé char/model/<nom>.glb
    //     attaché à l'os (groupe 2 = tête, 1/4 = dos → Spine1) ;
    //   - « NULL » → item sans visuel (casque invisible).
    // Armes : code W<modèle><variante> → item/model/<W+3ch>.glb
    // attaché aux bones prop natifs (Bip01 Prop1 = main droite,
    // Prop2 = main gauche — désasm bones du squelette B.glb).
    // ------------------------------------------------------------------

    /// <summary>Lettre du corps déduite des noms de meshes
    /// (« #B202 » → « b », « #GFACE » → « g »). Repli « g ».</summary>
    public static string BodyLetterOf(Node body)
    {
        foreach (var mi in body
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            var n = mi.Name.ToString();
            if (n.Length > 1 && n[0] == '#' && char.IsLetter(n[1]))
                return char.ToLowerInvariant(n[1]).ToString();
        }
        return "g";
    }

    /// <summary>Résolution partagée « items portés → meshes » :
    /// <paramref name="outfits"/> = suffixes numériques des meshes
    /// tenue du mega-GLB (entrées « #… » de 4pequip — ex. « 230 ») et
    /// <paramref name="attach"/> = (bone, chemin GLB) pour les meshes
    /// externes (casque/cape) et les armes. Utilisé par
    /// <see cref="ApplyEquipment"/> (jeu) et
    /// <see cref="OutfitSuffixes"/> (sélecteur — même tenue).</summary>
    private static (HashSet<string> Outfits,
            List<(string Bone, string Rel, CharModelSpec.EquipMesh? Row)> Attach,
            Dictionary<string, CharModelSpec.EquipMesh> Rows)
        ResolveEquippedMeshes(string letter,
            IReadOnlyDictionary<int, int> worn,
            Func<int, (string Code, string ModelCode)?> lookup)
    {
        var map = CharModelSpec.LoadEquipMap(letter);
        var outfits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attach = new List<(string Bone, string Rel, CharModelSpec.EquipMesh? Row)>();
        var rows = new Dictionary<string, CharModelSpec.EquipMesh>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (loc, itemId) in worn)
        {
            var info = lookup(itemId);
            if (info == null) continue;
            // Table 4pequip : tenue corps / casque / cape — clé =
            // code item ou modelCode (les deux cohabitent).
            if (map.TryGetValue(info.Value.Code, out var row) ||
                map.TryGetValue(info.Value.ModelCode, out row))
            {
                if (row.Mesh.StartsWith('#'))
                {
                    // « #B230 » → « 230 » : les ensembles tenue sont
                    // des suffixes numériques seuls (comparés à
                    // n[2..] par ApplyOutfitVisibility/ApplyLook et
                    // à BaseEquipIds) — garder la lettre rendait la
                    // bascule inopérante (tenue jamais affichée,
                    // repli #B202 permanent, local comme distant).
                    var suffix = row.Mesh[2..];
                    outfits.Add(suffix);
                    rows[suffix] = row;
                }
                else if (!string.Equals(row.Mesh, "NULL",
                             StringComparison.OrdinalIgnoreCase))
                    attach.Add((AttachmentBone(row.Group),
                        $"char/model/{row.Mesh}", row));
                // « NULL » = porté mais invisible (rien à attacher).
            }
            // Arme : code W… → item/model/<stem>.glb sur bone prop.
            var stem = CharModelSpec.WeaponModelStem(info.Value.ModelCode)
                ?? CharModelSpec.WeaponModelStem(info.Value.Code);
            if (stem != null)
            {
                // loc 7/9 = main droite (Prop1) ; loc 8 bouclier et
                // loc 10 distance = main gauche (Prop2 — HYPOTHESIS
                // pour l'arc, la main native n'est pas désassemblée).
                var bone = loc switch
                {
                    7 or 9 => "Bip01 Prop1",
                    8 or 10 => "Bip01 Prop2",
                    _ => "",
                };
                if (bone.Length > 0)
                {
                    // « #W40106 » : variante de skin (texture propre).
                    var skin = CharModelSpec.WeaponSkinCode(
                        info.Value.ModelCode) ??
                        CharModelSpec.WeaponSkinCode(info.Value.Code);
                    attach.Add((bone, skin != null
                        ? $"item/model/{stem}#{skin}"
                        : $"item/model/{stem}", null));
                }
            }
        }
        return (outfits, attach, rows);
    }

    /// <summary>Suffixes tenue à afficher pour les items
    /// <paramref name="worn"/> (loc→itemId — Preview de la liste
    /// persos ou <c>PlayerEquipment.Worn</c> en zone) — ensemble
    /// <b>vide</b> si aucune armure 4pequip résolue (le rappel reste
    /// alors la tenue débutant <see cref="BaseEquipIds"/>).</summary>
    public static HashSet<string> OutfitSuffixes(string letter,
        IReadOnlyDictionary<int, int>? worn,
        Func<int, (string Code, string ModelCode)?>? lookup,
        int classId = 0, int style = 0)
    {
        var res = worn == null || lookup == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : ResolveEquippedMeshes(letter, worn, lookup).Outfits;
        // Aucune armure de corps portée → tenue de base DE LA CLASSE (et non plus toujours « 202 »,
        // la tenue Guerrier).
        if (res.Count == 0 && classId > 0 &&
            CharModelSpec.ClassOutfitRow(letter, classId, style) is { } r)
            res.Add(r.Mesh[2..]);
        if (OS.GetCmdlineUserArgs().Contains("--look-log"))
            GD.Print($"[outfit] letter={letter} class={classId} style={style} → {string.Join(',', res)} (closet={CharModelSpec.ClassCloset(classId).Count})");
        return res;
    }

    /// <summary>Lignes 4pequip résolues des tenues portées : suffixe
    /// mesh (« 230 ») → <see cref="CharModelSpec.EquipMesh"/>
    /// (skin + spec dye). Retourné avec les suffixes pour que
    /// <see cref="TintMeshes"/> applique les teintes natives.</summary>
    public static IReadOnlyDictionary<string, CharModelSpec.EquipMesh>
        OutfitRows(string letter,
            IReadOnlyDictionary<int, int>? worn,
            Func<int, (string Code, string ModelCode)?>? lookup,
            int classId = 0, int style = 0)
    {
        var rows = worn == null || lookup == null
            ? new Dictionary<string, CharModelSpec.EquipMesh>()
            : new Dictionary<string, CharModelSpec.EquipMesh>(
                ResolveEquippedMeshes(letter, worn, lookup).Rows);
        if (rows.Count == 0 && classId > 0 &&
            CharModelSpec.ClassOutfitRow(letter, classId, style) is { } r)
            rows[r.Mesh[2..]] = r;
        return rows;
    }

    /// <summary>Classe active et style (garde-robe) du joueur LOCAL — pour sa tenue de base.</summary>
    public static (int Class, int Style) LocalClass()
    {
        var cls = Protocol.PlayerVitals.ActiveClass > 0 ? Protocol.PlayerVitals.ActiveClass
            : Screens.SessionCarry.Character?.ClassId ?? 0;
        var style = 0;
        foreach (var e in Hud.ClassChangeDialog.Classes)
            if (e.ClassId == cls) { style = e.Style; break; }
        return (cls, style);
    }

    /// <summary>Tenue de débutant (repli quand rien d'équipé) —
    /// exposée pour <c>CharDressing</c> : les deux passes doivent
    /// choisir la même tenue.</summary>
    public static IReadOnlyList<string> StarterOutfits => BaseEquipIds;

    /// <summary>Applique l'état porté (<paramref name="worn"/> loc →
    /// itemId) au corps : bascule les meshes tenue (#&lt;L&gt;2xx /
    /// S&lt;L&gt;2xx) vers les tenues des items portés (repli tenue de
    /// base si aucune armure 4pequip), attache les GLB casque/cape/
    /// armes aux bones. <paramref name="lookup"/> : itemId →
    /// (Code, ModelCode) c_item.</summary>
    public static void ApplyEquipment(Node3D body, string letter,
        IReadOnlyDictionary<int, int> worn,
        Func<int, (string Code, string ModelCode)?> lookup,
        int classId = 0, int style = 0)
    {
        ClearEquipAttachments(body);
        var (outfits, attach, rowsIn) =
            ResolveEquippedMeshes(letter, worn, lookup);
        var rows = new Dictionary<string, CharModelSpec.EquipMesh>(rowsIn);
        if (outfits.Count == 0 && classId > 0 &&
            CharModelSpec.ClassOutfitRow(letter, classId, style) is { } cr)
        {
            outfits.Add(cr.Mesh[2..]);
            rows[cr.Mesh[2..]] = cr;
            // KB-17 : coiffe de la tenue de classe — sauf casque porté.
            if (!attach.Any(a => a.Bone == "Bip01 Head") &&
                CharModelSpec.ClassHeadRow(letter, classId, style) is { } hr)
                attach.Add((AttachmentBone(hr.Group),
                    $"char/model/{hr.Mesh}", hr));
        }
        ApplyOutfitVisibility(body, letter, outfits);
        foreach (var (bone, rel, row) in attach)
        {
            var node = AttachGlbToBone(body, bone, rel);
            // Casque/cape : leur texture « <skin>B » est un masque dye
            // 3 canaux (comme les tenues #X2xx) — sans teinture elle
            // s'affichait brute (rouge/vert/bleu).
            if (node != null && row != null)
                foreach (var mi in node.FindChildren("*", "MeshInstance3D",
                             true, false).Cast<MeshInstance3D>())
                    DyeMeshSurfaces(mi, row);
        }
        // Les peaux SB* nouvellement visibles prennent la teinte peau
        // (Tinted est en cache — idempotent). Teintes résolues à la
        // construction (meta posées par BuildBody) — repli fixes si
        // le corps n'a pas transité par BuildBody.
        TintMeshes(body,
            body.GetMeta(MetaSkin, Variant.From(SkinTintFallback))
                .AsColor(),
            body.GetMeta(MetaHair, Variant.From(HairTintFallback))
                .AsColor(), rows);
    }

    /// <summary>Diagnostic <c>--mesh-dump</c> : meshes visibles, matériau,
    /// albedo (taille, produit de teinture ou texture brute).</summary>
    public static void DumpVisible(Node root, string tag)
    {
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true,
                     false).Cast<MeshInstance3D>())
        {
            if (!n.IsVisibleInTree() || n.Mesh == null) continue;
            for (var i = 0; i < n.Mesh.GetSurfaceCount(); i++)
            {
                var m = n.GetActiveMaterial(i);
                var tex = m switch
                {
                    StandardMaterial3D sm => sm.AlbedoTexture,
                    ShaderMaterial shm => shm.GetShaderParameter(
                        "albedo_tex").Obj as Texture2D,
                    _ => null,
                };
                var avg = "";
                if (tex != null && n.Name.ToString().Length >= 4 && n.Name.ToString()[2] == '1')
                {
                    var im = tex.GetImage();
                    if (im != null)
                    {
                        im = (Image)im.Duplicate();
                        if (im.IsCompressed()) im.Decompress();
                        im.Convert(Image.Format.Rgba8);
                        double r = 0, g = 0, b = 0; long c = 0;
                        var d = im.GetData();
                        for (var q = 0; q < d.Length; q += 64) if (d[q] + d[q + 1] + d[q + 2] > 30) { r += d[q]; g += d[q + 1]; b += d[q + 2]; c++; }
                        if (c > 0) avg = $" avgRGB=({r / c:F0},{g / c:F0},{b / c:F0})";
                    }
                }
                GD.Print($"[mesh-dump] {tag} {n.Name}#{i}{avg} " +
                    $"mat={m?.GetType().Name ?? "null"} " +
                    $"tex={(tex == null ? "none" : $"{tex.GetWidth()}x{tex.GetHeight()} " +
                        (IsTintProduct(tex) ? "TEINTE" : "BRUTE"))} " +
                    $"src={(m?.HasMeta("src_tex") == true ? m.GetMeta("src_tex") : "-")} " +
                    (m is StandardMaterial3D st
                        ? $"color={st.AlbedoColor} vcol={st.VertexColorUseAsAlbedo} " +
                          $"emis={st.EmissionEnabled}:{st.Emission} shade={st.ShadingMode} " +
                          $"tr={st.Transparency} blend={st.BlendMode}"
                        : ""));
            }
        }
    }

    /// <summary>H10 diag : liste les meshes visibles dont une surface
    /// n'a ni texture ni couleur claire (rendu noir).</summary>
    public static void AuditBlack(Node root, string tag)
    {
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true,
                     false).Cast<MeshInstance3D>())
        {
            if (!n.IsVisibleInTree() || n.Mesh == null) continue;
            for (var i = 0; i < n.Mesh.GetSurfaceCount(); i++)
            {
                var m = n.GetActiveMaterial(i) as BaseMaterial3D;
                var tex = m?.AlbedoTexture;
                var c = m?.AlbedoColor ?? Godot.Colors.White;
                var darkTex = false;
                if (tex != null)
                {
                    try
                    {
                        var img = tex.GetImage();
                        if (img != null)
                        {
                            if (img.IsCompressed()) img.Decompress();
                            img.Resize(1, 1, Image.Interpolation.Bilinear);
                            var px = img.GetPixel(0, 0);
                            darkTex = px.Luminance < 0.06f && px.A > 0.6f;
                        }
                    }
                    catch { /* diag */ }
                }
                if (m is BaseMaterial3D && (darkTex ||
                    (tex == null && c.Luminance < 0.15f)))
                    GD.Print($"[black-audit] {tag} mesh={n.Name} " +
                        $"surf={i} mat={(m == null ? "null/shader" : m.ResourceName)} " +
                        $"tex={(tex == null ? "none" : darkTex ? "DARK" : "ok")} path={tex?.ResourcePath} c={c} parent={n.GetParent()?.Name}");
            }
        }
    }

    /// <summary>Os d'attache pour un groupe 4pequip : 2 = tête,
    /// 1/4 = dos (capes → Spine1), autres → tête par défaut.</summary>
    private static string AttachmentBone(int group) =>
        group switch
        {
            1 or 4 => "Bip01 Spine1",
            _ => "Bip01 Head",
        };

    /// <summary>Bascule les meshes tenue : visibles = les suffixes de
    /// <paramref name="outfits"/> (ex. « B230 » + peau « B230 » via
    /// préfixe #/S) ou la tenue de base si l'ensemble est vide ; les
    /// autres meshes #&lt;L&gt;2xx+/S&lt;L&gt;2xx+ sont masqués.
    /// Cheveux (#&lt;L&gt;1xx), visage et meshes sans suffixe
    /// numérique ne sont pas touchés.</summary>
    private static void ApplyOutfitVisibility(Node3D body,
        string letter, HashSet<string> outfits)
    {
        var wanted = outfits.Count > 0
            ? outfits
            : new HashSet<string>(BaseEquipIds,
                StringComparer.OrdinalIgnoreCase);
        var up = letter.ToUpperInvariant();
        foreach (var mi in body
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            var n = mi.Name.ToString();
            if (n.Length < 4) continue;
            // « #B230 » → suffixe « 230 » ; « SB230 » → « 230 ».
            if ((n[0] == '#' || n[0] == 'S') &&
                n[1].ToString().Equals(up, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = n[2..];
                if (!int.TryParse(suffix, out var num) || num < 200)
                    continue;                     // cheveux/visage/…
                mi.Visible = wanted.Contains(suffix);
            }
        }
    }

    /// <summary>Retire les attachements d'équipement posés par
    /// <see cref="AttachGlbToBone"/> (nœuds « __equip_* »).</summary>
    private static void ClearEquipAttachments(Node3D body)
    {
        foreach (var n in body.FindChildren("__equip_*", "", true, false))
            n.QueueFree();
    }

    /// <summary>Attache le GLB <paramref name="rel"/> à l'os
    /// <paramref name="boneName"/> : BoneAttachment3D sous le
    /// Skeleton3D du corps (les bones Prop1/Prop2/Head existent dans
    /// le skin) ; repli sur le Node3D du même nom si l'os n'est pas
    /// un bone du skin. No-op si le GLB est absent.</summary>
    private static Node3D? AttachGlbToBone(Node3D body, string boneName,
        string rel)
    {
        string? weaponSkin = null;
        if (rel.IndexOf('#') is var hash and > 0)
        {
            weaponSkin = rel[(hash + 1)..];
            rel = rel[..hash];
        }
        if (!AssetCatalog.TryGetGlb(rel, out var abs))
            return null;
        var node = AssetCatalog.LoadGlbNode(abs);
        if (node == null) return null;
        if (weaponSkin != null)
            CharModelSpec.ApplyWeaponSkin(node, weaponSkin);
        node.Name = $"__equip_glb_{boneName}";
        var skel = body
            .FindChildren("*", "Skeleton3D", true, false)
            .Cast<Skeleton3D>().FirstOrDefault();
        if (skel != null && skel.FindBone(boneName) >= 0)
        {
            var attach = skel
                .FindChildren($"__equip_ba_{boneName}",
                    "BoneAttachment3D", false, false)
                .Cast<BoneAttachment3D>().FirstOrDefault();
            if (attach == null)
            {
                attach = new BoneAttachment3D
                {
                    Name = $"__equip_ba_{boneName}",
                    BoneName = boneName,
                };
                skel.AddChild(attach);
            }
            attach.AddChild(node);
            return node;
        }
        // Repli : parent direct au nœud portant le nom de l'os
        // (corps dont le squelette n'a pas les bones prop).
        var host = body.FindChildren(boneName, "", true, false)
            .FirstOrDefault() as Node3D;
        (host ?? body).AddChild(node);
        return node;
    }

    // ------------------------------------------------------------------
    // Monture (ride) — visuel partagé par le joueur local et les
    // entités distantes.
    // ------------------------------------------------------------------

    /// <summary>rideKey → stem du modèle : code ridelist
    /// « r »+key:D6 (ex. 12010 → « r012010 » → ridelist col2
    /// « r012.kfm » → <c>ride/model/R012.glb</c>) ; repli
    /// « r{key/1000:D3} » quand le code n'est pas dans la table —
    /// même convention que MissionBridge.RideStem (les 3 derniers
    /// chiffres sont la variante, npclist/monsterlist idem).</summary>
    public static string? RideStem(int rideKey)
    {
        if (rideKey <= 0) return null;
        return Hud.RideList.Stem($"r{rideKey:D6}")
            ?? $"r{rideKey / 1000:D3}";
    }

    /// <summary>Construit le visuel d'une monture : GLB
    /// <c>ride/model/&lt;stem&gt;.glb</c> tourné −90°/X (convention
    /// Z-up des GLB convertis, identique à
    /// <see cref="World.RemoteEntity.SetVisual"/>) + animator
    /// <c>ride/animation/&lt;stem&gt;.glb</c> quand il existe
    /// (stand01/move01 — auto-locomotion). Null si le modèle est
    /// absent.</summary>
    public static Node3D? BuildRideVisual(int rideKey)
    {
        var stem = RideStem(rideKey);
        if (stem == null ||
            !AssetCatalog.TryGetGlb($"ride/model/{stem}", out var abs))
            return null;
        var node = AssetCatalog.LoadGlbNode(abs);
        if (node == null) return null;
        // −90°/X (Z-up) + 180°/Y : les GLB convertis font face +Z alors
        // que l'entité avance vers −Z (comme le corps perso) — sans ce
        // 180° la monture regarde la caméra (capture r-2, A6.2).
        node.RotationDegrees = new Vector3(-90f, 180f, 0f);
        ApplyRideTexVariant(rideKey, node);
        EntityAnimator.Attach(node, $"ride/animation/{stem}",
                modelKey: abs)
            ?.PlayLoop("stand01");
        return node;
    }

    /// <summary>Texture corps de la variante ridelist (col4) :
    /// « r253020 » → <c>ride/texture/r25302.dds</c> sur les surfaces
    /// dont le matériau natif référence la variante « 01 » du même
    /// stem (<c>src_tex</c> = r25301). Les accessoires (r25351…)
    /// gardent leur propre texture — meta « src_tex » posée par
    /// <see cref="Native.NifMaterial.For"/>.</summary>
    private static void ApplyRideTexVariant(int rideKey, Node3D mount)
    {
        var texName = Hud.RideList.Tex($"r{rideKey:D6}");
        if (string.IsNullOrEmpty(texName) || texName.Length < 3 ||
            !char.IsAsciiDigit(texName[^1]) || !char.IsAsciiDigit(texName[^2]))
            return;
        var baseStem = texName[..^2] + "01";
        if (string.Equals(texName, baseStem,
                StringComparison.OrdinalIgnoreCase))
            return;                                     // variante « 01 » = déjà la base
        var tex = AssetCatalog.GetTexture(
            $"ride/texture/{texName}.dds");
        if (tex == null)
        {
            GD.PushWarning($"[mount] texture variante absente : " +
                           $"ride/texture/{texName}.dds");
            return;
        }
        foreach (var mi in mount
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if ((mi.GetSurfaceOverrideMaterial(s) ??
                     mi.GetActiveMaterial(s)) is not
                        StandardMaterial3D mat ||
                    !mat.HasMeta("src_tex") ||
                    !string.Equals(mat.GetMeta("src_tex").AsString(),
                        baseStem, StringComparison.OrdinalIgnoreCase))
                    continue;
                var dup = (StandardMaterial3D)mat.Duplicate();
                dup.AlbedoTexture = tex;
                dup.SetMeta("src_tex", texName.ToLowerInvariant());
                mi.SetSurfaceOverrideMaterial(s, dup);
            }
        }
    }

    /// <summary>Animator de la monture construite par
    /// <see cref="BuildRideVisual"/> (null si anims absentes).</summary>
    public static EntityAnimator? MountAnimator(Node3D? mount) => mount?
        .FindChildren("*", "", true, false)
        .OfType<EntityAnimator>().FirstOrDefault();

    /// <summary>Joue l'idle « stand01 » via l'EntityAnimator attaché
    /// par <see cref="AttachAnimator"/> ; sinon retombe sur la première
    /// anim embarquée (ancienne logique, corps sans anims retargetées).
    /// </summary>
    public static void PlayIdle(Node3D body)
    {
        foreach (var a in body
                     .FindChildren("*", "EntityAnimator", true, false)
                     .Cast<EntityAnimator>())
        {
            a.PlayLoop(a.IdleAnim);
            return;
        }
        foreach (var p in body.FindChildren("*", "AnimationPlayer", true, false)
                     .Cast<AnimationPlayer>())
        {
            var names = p.GetAnimationList();
            if (names.Length == 0) continue;
            // Préfère une anim contenant « stand »/« idle », sinon la 1re.
            var pick = names.FirstOrDefault(n =>
                n.Contains("stand", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("idle", StringComparison.OrdinalIgnoreCase))
                ?? names[0];
            p.Play(pick);
            return;
        }
    }
}
