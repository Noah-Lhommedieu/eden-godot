using Godot;

namespace EdenClient;

/// <summary>
/// Résolution d'assets SANS passer par l'import Godot —
/// <c>assets/.gdignore</c> est actif tant que le pipeline nif2gltf
/// tourne, donc <c>res://assets/**</c> n'est pas importable :
/// les textures sont chargées par <see cref="Image.LoadFromFile"/> et
/// les GLB par <see cref="GltfDocument"/> sur des chemins fichier
/// absolus.
///
/// Deux racines (résolution détaillée dans <see cref="DataPaths"/>) :
///   - <see cref="AssetsRoot"/> : contenu converti
///     (defaut <c>godot_project/assets/</c> en dev,
///     <c>gamedata/assets/</c> en package ; override env
///     <c>EDEN_ASSETS_ROOT</c> ou <see cref="Configure"/>).
///     Textures : <c>assets/textures/map/texture/mount_04.png</c>
///     (miroir de <c>map/texture/mount_04.dds</c>, .dds→.png).
///     Modèles :  <c>assets/models/map/model/object/UE013.glb</c>.
///   - <see cref="RawAssetsRoot"/> : client Reforged dépacké en clair
///     (defaut <c>raw_assets/</c> symlink à la racine du portage en
///     dev, <c>gamedata/</c> en package ; override env
///     <c>EDEN_RAW_ASSETS</c> ou <c>EDEN_DATA_DIR</c>). Sert aux
///     fichiers .fsm, .ini, .area non convertis.
/// </summary>
public static class AssetCatalog
{
    private static string? _assetsRoot;
    private static string? _rawAssetsRoot;
    private static readonly Dictionary<string, ImageTexture?> _texCache =
        new(StringComparer.OrdinalIgnoreCase);
    // _glbCache/_dirCache : accès sous verrou — les protos sont
    // préchargés en parallèle (PreloadGlbProtos, lot G1) depuis des
    // tâches de fond pendant le chargement de map.
    private static readonly Dictionary<string, Node3D?> _glbCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, string>>
        _dirCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageTexture? _fallbackWhite;
    private static bool? _headless;
    private static bool IsHeadless => _headless ??=
        DisplayServer.GetName() == "headless" ||
        OS.GetCmdlineArgs().Contains("--headless");
    // 0 = non tenté, 1 = extension enregistrée, -1 = mips désactivés.

    /// <summary>Racine des assets convertis (chemin absolu).</summary>
    public static string AssetsRoot =>
        _assetsRoot ??= ResolveAssetsRoot();

    /// <summary>Racine des assets bruts Reforged (chemin absolu, ou
    /// chaine vide si introuvable).</summary>
    public static string RawAssetsRoot =>
        _rawAssetsRoot ??= ResolveRawAssetsRoot();

    /// <summary>Configure explicitement les racines (tests / headless).</summary>
    public static void Configure(string? assetsRoot, string? rawAssetsRoot)
    {
        _assetsRoot = assetsRoot;
        _rawAssetsRoot = rawAssetsRoot;
    }

    /// <summary>
    /// Texture par chemin relatif asset (« map/texture/mount_04.dds »
    /// ou « mount_04.dds »). Traduit .dds/.ifl → .png, cherche sous
    /// <c>assets/textures/</c> puis à la racine d'assets. Cache les
    /// résultats (y compris les échecs).
    /// </summary>
    public static ImageTexture? GetTexture(string relPath)
    {
        lock (_texCache)
        {
            if (_texCache.TryGetValue(relPath, out var cached))
                return cached;
        }
        var tex = LoadTexture(relPath);
        lock (_texCache) _texCache[relPath] = tex;
        return tex;
    }

    /// <summary>Variante jamais null : placeholder blanc si absente.</summary>
    public static ImageTexture GetTextureOrWhite(string relPath) =>
        GetTexture(relPath) ?? White;

    /// <summary>Texture blanche 1×1 partagée (slot inutilisé/manquant).</summary>
    public static ImageTexture White => _fallbackWhite ??= MakeSolid(
        new Godot.Color(1f, 1f, 1f, 1f));

    /// <summary>Texture unie 1×1.</summary>
    public static ImageTexture MakeSolid(Godot.Color c)
    {
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(c);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>GLB par chemin relatif asset
    /// (« map/model/object/UE013 ») → chemin absolu ou URI
    /// « epk://… » (pack), false si absent.
    /// Essaie aussi le suffixe « _1 » (variantes UB124_1.._5).</summary>
    public static bool TryGetGlb(string relPathNoExt, out string absPath)
    {
        foreach (var candidate in new[]
                 { relPathNoExt, relPathNoExt + "_1" })
        {
            var p = Path.Combine(AssetsRoot, "models",
                candidate + ".glb");
            var hit = ResolveFile(p);
            if (hit != null)
            {
                absPath = hit;
                return true;
            }
        }
        // Natif : pas de .glb (pack natif / phase G) — chemin virtuel, BuildProto
        // le traduit en .nif d'origine (TryBuildNative n'en lit que le nom).
        if ((Native.NativeMode.Mesh || Native.NativeMode.Skin) && Native.NifMaterials.Available
            && !relPathNoExt.StartsWith("effect/", StringComparison.OrdinalIgnoreCase))
            foreach (var candidate in new[] { relPathNoExt, relPathNoExt + "_1" })
                if (ResolveRawAsset(candidate + ".nif") != null)
                {
                    absPath = Path.Combine(AssetsRoot, "models", candidate + ".glb");
                    return true;
                }
        absPath = string.Empty;
        return false;
    }

    /// <summary>Charge un .glb en Node3D via GltfDocument (aucun import
    /// requis — fonctionne sous .gdignore). Le document parsé est mis en
    /// cache et chaque appel retourne un <see cref="Node.Duplicate"/> —
    /// une map instancie un même modèle des centaines de fois.
    ///
    /// Le proto subit un passage
    /// <see cref="World.ToonMaterializer.Apply"/> (matériaux → look
    /// anime + masquage des meshes de collision helpers) — sauf pour
    /// <c>map/model/sky/</c> dont <see cref="World.SkyDome"/> gère les
    /// matériaux.</summary>
    public static Node3D? LoadGlbNode(string absPath)
    {
        var proto = GetProto(absPath);
        return proto == null ? null : DuplicateSanitized(proto);
    }

    /// <summary>Duplicate() + re-normalisation des bone rests —
    /// Duplicate() ne copie pas les rests internes du Skeleton3D
    /// (état hors propriétés), la sanité du proto est perdue à
    /// l'instanciation : sans ça interpolate_via_rest respamme
    /// « end quaternion ».</summary>
    public static Node3D? DuplicateSanitized(Node proto)
    {
        var node = proto.Duplicate() as Node3D;
        if (node != null) SanitizeRestPoses(node);
        if (node != null && System.Environment.GetEnvironmentVariable("EDEN_NOFX") != "1" && proto.HasMeta("nif_fx") && GodotObject.IsInstanceValid(node)
            && OS.GetThreadCallerId() == OS.GetMainThreadId())
            node.AddChild(new Native.NativeFxAttach { Name = "NativeFxAttach", NifPath = proto.GetMeta("nif_fx").AsString() });
        return node;
    }

    /// <summary>Proto GLB en cache (partagé, jamais parenté) —
    /// thread-safe.</summary>
    public static Node3D? GetProto(string absPath)
    {
        lock (_glbCache)
        {
            if (_glbCache.TryGetValue(absPath, out var cached))
                return cached;
        }
        var proto = BuildProto(absPath);
        lock (_glbCache)
        {
            // Deux tâches ont pu parser le même fichier — la première
            // version gagne, la seconde est jetée (QueueFree différé
            // impossible hors arbre : on laisse le GC/queue_free natif).
            if (_glbCache.TryGetValue(absPath, out var existing))
                return existing;
            _glbCache[absPath] = proto;
        }
        return proto;
    }

    /// <summary>
    /// Précharge en parallèle les protos GLB listés — parse +
    /// <see cref="GltfDocument.GenerateScene"/> + matériaux/culling en
    /// tâches de fond, puis retour immédiat avec le cache chaud :
    /// les <see cref="LoadGlbNode"/> suivants ne font plus que des
    /// <see cref="Node.Duplicate"/>.
    ///
    /// Sécurité thread : le parse ne touche ni SceneTree ni l'arbre
    /// (nodes détachés — même garantie que le ResourceLoader threaded
    /// de Godot) ; RenderingServer est thread-safe ; les caches sont
    /// sous verrou. Appelé depuis le thread principal (chargement map)
    /// — bloque jusqu'à la fin des tâches.
    /// </summary>
    /// <returns>Nombre de protos effectivement parsés.</returns>
    public static int PreloadGlbProtos(IEnumerable<string> absPaths,
        int maxDegree = 0)
    {
        var todo = new List<string>();
        lock (_glbCache)
        {
            foreach (var p in absPaths)
                if (p.Length > 0 && !_glbCache.ContainsKey(p))
                    todo.Add(p);
        }
        if (todo.Count == 0) return 0;
        var distinct = todo.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (maxDegree <= 0)
            maxDegree = 1; // Séquentiel sur worker : GltfDocument/GenerateScene concurrents bloquent l'ObjectDB Godot (P0.1).
        var errors = new System.Collections.Concurrent
            .ConcurrentQueue<(string Path, string Error)>();
        if (maxDegree == 1)
        {
            foreach (var path in distinct)
            {
                try { _ = GetProto(path); }
                catch (Exception ex)
                {
                    errors.Enqueue((path, ex.Message));
                }
            }
        }
        else
        {
            System.Threading.Tasks.Parallel.ForEach(distinct,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                path =>
                {
                    try { _ = GetProto(path); }
                    catch (Exception ex)
                    {
                        errors.Enqueue((path, ex.Message));
                    }
                });
        }
        foreach (var (p, err) in errors)
        {
            GD.PrintErr($"[assets] préchargement {p} : {err}");
            lock (_glbCache) _glbCache[p] = null;
        }
        return distinct.Length;
    }

    /// <summary>Diag « top postes VRAM » : octets décodés par source
    /// (stem de texture ou chemin GLB), cumulés au chargement.
    /// Accédé depuis tâches de fond → verrou.</summary>
    private static readonly System.Collections.Concurrent
        .ConcurrentDictionary<string, long> _texBytes = new();

    /// <summary>Point d'entrée public du tally pour les textures
    /// créées hors du catalogue (atlases CEGUI — diag « top postes »).
    /// </summary>
    public static void TallyTexBytesPublic(string key, Image img) =>
        TallyTexBytes(key, img);

    private static void TallyTexBytes(string key, Image img)
    {
        var bpp = img.IsCompressed() ? 1 : 4;
        var bytes = (long)img.GetWidth() * img.GetHeight() * bpp;
        if (img.HasMipmaps()) bytes = bytes * 4 / 3;
        _texBytes.AddOrUpdate(key, bytes, (_, v) => v + bytes);
    }

    /// <summary>Top <paramref name="n"/> sources de texture par octets
    /// décodés cumulés (diag --perf « top postes RAM ») — groupé par
    /// dossier parent pour faire ressortir la masse en longue traîne.</summary>
    public static string TopTexPostes(int n)
    {
        var tot = _texBytes.Values.Sum();
        var dirs = _texBytes.GroupBy(kv =>
            {
                var k = kv.Key;
                var i = k.LastIndexOf('/');
                return i > 0 ? k[..i] : k;
            })
            .Select(g => (g.Key, V: g.Sum(x => x.Value)))
            .OrderByDescending(x => x.V).Take(n);
        return $"total={tot / 1048576}MB " +
               string.Join(" ", dirs.Select(x =>
                   $"{x.Key}={x.V / 1048576}MB"));
    }

    /// <summary>Diag « top postes » : octets des albedos de chaque proto natif (aucune
    /// recompression à l'exécution : les DDS BC sont transmis tels quels au GPU).</summary>
    private static void TallyEmbeddedTextures(Node proto, string absPath)
    {
        var key = "nif:" + Path.GetFileName(absPath);
        var tallied = new HashSet<Texture2D>();
        foreach (var mi in proto
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ??
                    mi.Mesh.SurfaceGetMaterial(s);
                var tex = mat switch
                {
                    StandardMaterial3D sm => sm.AlbedoTexture,
                    ShaderMaterial shm => shm
                        .GetShaderParameter("albedo_tex").Obj
                        as Texture2D,
                    _ => null,
                };
                if (tex == null || !tallied.Add(tex)) continue;
                if (tex.GetImage() is { } src)
                    TallyTexBytes(key, src);
            }
        }
    }

    /// <summary>Parse le GLB + applique matériaux toon et culling —
    /// pur (aucun accès à l'arbre), sûr en tâche de fond.</summary>
    private static Node3D? BuildProto(string absPath) => TryBuildNative(absPath);

    /// <summary>Proto depuis le .nif EDENFR d'origine (B4, EDEN_NATIVE=mesh,skin) :
    /// .nif trouvé sous la racine de données, arbre natif (NifScene) puis même
    /// post-traitement que le GLB. null → repli GLB. Statiques sous « mesh »,
    /// modèles skinnés sous « skin ».</summary>
    private static int _nativeProtos;

    private static Node3D? TryBuildNative(string glbAbs)
    {
        var norm = glbAbs.Replace('\\', '/');
        var models = Path.Combine(AssetsRoot, "models").Replace('\\', '/') + "/";
        if (!norm.StartsWith(models, StringComparison.OrdinalIgnoreCase) ||
            !norm.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            return null;
        var rel = norm[models.Length..^4];
        // effect/ : animations de matériau lues dans les extras GLB (SpellFx) — natif via NativeFx, pas encore branché.
        if (rel.StartsWith("effect/", StringComparison.OrdinalIgnoreCase) || !Native.NifMaterials.Available)
            return null;
        var nifAbs = ResolveRawAsset(rel + ".nif");
        return nifAbs == null ? null : BuildNativeProto(nifAbs, glbAbs, false);
    }

    /// <summary>Arbre natif + post-traitement identique au GLB.
    /// <paramref name="force"/> : ignore le choix mesh/skin (banc de parité).</summary>
    public static Node3D? BuildNativeProto(string nifAbs, string glbAbs, bool force)
    {
        Node3D? proto;
        try
        {
            var nif = World.NifFile.Load(nifAbs);
            var skinned = nif.Shapes.Any(s => s.Skin != null);
            if (!force && (skinned ? !Native.NativeMode.Skin : !Native.NativeMode.Mesh))
                return null;
            proto = Native.NifScene.Build(nif, Path.GetFileNameWithoutExtension(nifAbs),
                ModelTexPrefix(glbAbs));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[native] {nifAbs} : {ex.Message} — repli GLB");
            return null;
        }
        if (proto == null) return null;
        PostProcessProto(proto, glbAbs);
        // Les nœuds C# ne se créent pas hors thread principal : on marque le proto, DuplicateSanitized pose l'animateur.
        if (!nifAbs.Replace('\\', '/').Contains("/effect/", StringComparison.OrdinalIgnoreCase)
            && GetNifCached(nifAbs) is { } fxNif && Native.NativeFxAttach.Needed(fxNif))
            proto.SetMeta("nif_fx", nifAbs);
        var n = System.Threading.Interlocked.Increment(ref _nativeProtos);
        if (n % 25 == 1) GD.Print($"[native] protos natifs construits : {n} (dernier {Path.GetFileName(nifAbs)})");
        return proto;
    }

    /// <summary>Hook NativeFx : arbre NifScene brut (sans toon ni cache, un arbre neuf par appel) pour
    /// « effect/model/X » ; l'appelant enchaîne <c>NativeFx.Attach(root, nif)</c>.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, World.NifFile> _fxNifCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>.nif parsé et mis en cache (lecture seule), par chemin résolu.</summary>
    public static World.NifFile? GetNifCached(string path)
    {
        if (_fxNifCache.TryGetValue(path, out var n)) return n;
        try { _fxNifCache[path] = n = World.NifFile.Load(path); return n; }
        catch (Exception) { return null; }
    }

    public static Node3D? LoadNativeScene(string relNoExt, out World.NifFile? nif)
    {
        nif = null;
        if (!Native.NifMaterials.Available) return null;
        var p = ResolveRawAsset(relNoExt + ".nif");
        if (p == null) return null;
        // Parse du .nif mis en cache (lecture seule) : chaque sort relançait disque + décodage sur le thread principal.
        var swLat = System.Diagnostics.Stopwatch.StartNew();
        var cold = !_fxNifCache.TryGetValue(p, out nif);
        if (cold) _fxNifCache[p] = nif = World.NifFile.Load(p);
        var tParse = swLat.Elapsed.TotalMilliseconds;
        var i = relNoExt.IndexOf("/model/", StringComparison.OrdinalIgnoreCase);
        var scene = Native.NifScene.Build(nif!, Path.GetFileName(relNoExt), i < 0 ? "" : relNoExt[..i]);
        if (System.Environment.GetEnvironmentVariable("EDEN_LAT_TRACE") == "1")
            GD.Print($"[lat] fx {relNoExt} {(cold ? "froid" : "chaud")} parse={tParse:F1} ms build={swLat.Elapsed.TotalMilliseconds - tParse:F1} ms");
        return scene;
    }

    /// <summary>« map/model/object/X.glb » → « map » (arbre de textures préféré).</summary>
    private static string ModelTexPrefix(string glbAbs)
    {
        var norm = glbAbs.Replace('\\', '/');
        var i = norm.IndexOf("/models/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        var rel = norm[(i + 8)..];
        var j = rel.IndexOf("/model/", StringComparison.OrdinalIgnoreCase);
        return j < 0 ? "" : rel[..j];
    }

    /// <summary>Matériaux toon, compression, culling, sanity — commun GLB/natif.</summary>
    private static void PostProcessProto(Node3D proto, string absPath)
    {
        if (!absPath.Replace('\\', '/').Contains("/map/model/sky/"))
        {
            var norm = absPath.Replace('\\', '/');
            World.ToonMaterializer.Apply(proto,
                decorScope: norm.Contains("/map/model/") ||
                            norm.Contains("/decorate/model/") ||
                            norm.Contains("/house/model/"),
                modelName: Path.GetFileNameWithoutExtension(absPath),
                monsterScope: norm.Contains("/monster/model/"));
        }
        TallyEmbeddedTextures(proto, absPath);
        Perf.PerfCulling.ApplyGlbProto(proto, absPath);
        SanitizeRotationKeys(proto);
    }

    /// <summary>Compteur diag : quats de rest corrigés par proto
    /// (loggé une fois par proto — 0 partout = la source du spam
    /// « end quaternion » est ailleurs que l'import GLB).</summary>
    private static int _restFixed, _boneRestFixed;

    /// <summary>Normalise la rest pose de chaque Node3D du proto —
    /// l'import GLB laisse parfois des quaternions dénormalisés sur
    /// les os ; <c>AnimationMixer::interpolate_via_rest</c> les lit
    /// au blend et spamme « The end quaternion must be normalized »
    /// à chaque frame même si les clés de track sont propres
    /// (constaté live S009/S016 — ~11 000 erreurs). La normalisation
    /// du Quaternion conserve l'échelle du nœud.</summary>
    private static void SanitizeRestPoses(Node proto)
    {
        foreach (var n in proto
                     .FindChildren("*", "Node3D", true, false)
                     .Cast<Node3D>())
        {
            var q = n.Quaternion;
            if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) ||
                !float.IsFinite(q.Z) || !float.IsFinite(q.W) ||
                q.LengthSquared() < 1e-6f)
            {
                n.Quaternion = Quaternion.Identity;
                System.Threading.Interlocked.Increment(
                    ref _restFixed);
                continue;
            }
            var nq = q.Normalized();
            if (nq != q)
            {
                n.Quaternion = nq;
                System.Threading.Interlocked.Increment(
                    ref _restFixed);
            }
        }
        // Les bone rests d'un Skeleton3D ne sont PAS des Node3D
        // enfants — interpolate_via_rest les lit au blend et spamme
        // si le quaternion de rest est dénormalisé (source réelle du
        // spam S009/S016).
        foreach (var sk in proto
                     .FindChildren("*", "Skeleton3D", true, false)
                     .Cast<Skeleton3D>())
        {
            for (var b = 0; b < sk.GetBoneCount(); b++)
            {
                var rest = sk.GetBoneRest(b);
                var q = rest.Basis.GetRotationQuaternion();
                Quaternion nq;
                if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) ||
                    !float.IsFinite(q.Z) || !float.IsFinite(q.W) ||
                    q.LengthSquared() < 1e-6f)
                {
                    nq = Quaternion.Identity;
                }
                else
                {
                    nq = q.Normalized();
                    if (nq == q) continue;
                }
                // Reconstruit la base : rotation normalisée × échelle
                // d'origine (conservée — bone non-uniform scale rare
                // mais légal).
                var scale = rest.Basis.Scale;
                var basis = new Basis(nq).Scaled(scale);
                sk.SetBoneRest(b, new Transform3D(basis,
                    rest.Origin));
                System.Threading.Interlocked.Increment(
                    ref _boneRestFixed);
            }
        }
        // Compteurs cumulés `_restFixed`/`_boneRestFixed` — silence par
        // proto (~600 lignes de log par map sinon) ; les totaux restent
        // disponibles pour le diagnostic.
    }

    /// <summary>Normalise toutes les clés des tracks Rotation3D des
    /// animations embarquées du GLB — la conversion .kf/.nif laisse
    /// parfois des quaternions dénormalisés ou nuls ; au blend
    /// (<c>AnimationMixer</c>) Godot spamme « The end quaternion must
    /// be normalized » à chaque frame (constaté live S001).
    /// Idempotent, fait une fois par proto en cache.</summary>
    private static void SanitizeRotationKeys(Node proto)
    {
        SanitizeRestPoses(proto);
        foreach (var player in proto
                     .FindChildren("*", "AnimationPlayer", true, false)
                     .Cast<AnimationPlayer>())
        {
            foreach (var libName in player.GetAnimationLibraryList())
            {
                var lib = player.GetAnimationLibrary(libName);
                foreach (var animName in lib.GetAnimationList())
                    SanitizeAnimation(lib.GetAnimation(animName));
            }
        }
    }

    /// <summary>Normalise les clés quaternion d'une Animation —
    /// tracks Rotation3D et Value (quats Variant). Les clés
    /// dégénérées/NaN sont remplacées par la dernière clé valide —
    /// une clé sale fait spammer « The end quaternion must be
    /// normalized » dans interpolate_via_rest à chaque blend
    /// (audit live : aussi présent dans les anims retargetées
    /// .kf→.glb — 42 clés (-0,-0,-0,-0) mesurées).</summary>
    public static void SanitizeAnimation(Animation? anim)
    {
        if (anim == null) return;
        for (var t = 0; t < anim.GetTrackCount(); t++)
        {
            if (anim.TrackGetType(t) is not
                (Animation.TrackType.Rotation3D or
                 Animation.TrackType.Value)) continue;
            var last = Quaternion.Identity;
            var keys = anim.TrackGetKeyCount(t);
            for (var k = 0; k < keys; k++)
            {
                if (anim.TrackGetKeyValue(t, k).VariantType !=
                    Variant.Type.Quaternion) continue;
                var q = (Quaternion)anim.TrackGetKeyValue(t, k);
                if (!float.IsFinite(q.X) ||
                    !float.IsFinite(q.Y) ||
                    !float.IsFinite(q.Z) ||
                    !float.IsFinite(q.W) ||
                    q.LengthSquared() < 1e-6f)
                {
                    anim.TrackSetKeyValue(t, k, last);
                    continue;
                }
                var nq = q.Normalized();
                if (nq != q)
                    anim.TrackSetKeyValue(t, k, nq);
                last = nq;
            }
        }
    }

    /// <summary>Compte des caches (sonde perf — scripts/perf) :
    /// textures ImageTexture, protos GLB, listings de dossiers.</summary>
    public static (int Textures, int GlbProtos, int DirListings)
        CacheStats()
    {
        lock (_texCache) lock (_glbCache) lock (_dirCache)
            return (_texCache.Count, _glbCache.Count, _dirCache.Count);
    }

    /// <summary>Vide les caches à la sortie de la scène de jeu —
    /// les protos GLB détachés doivent être <c>Free()</c>d (sinon
    /// ObjectDB les compte fuités à l'arrêt), les RefCounted
    /// (textures) suffisent à déréférencer.</summary>
    public static void ReleaseCaches()
    {
        lock (_glbCache)
        {
            foreach (var n in _glbCache.Values)
                if (GodotObject.IsInstanceValid(n)) n!.Free();
            _glbCache.Clear();
        }
        lock (_texCache) _texCache.Clear();
        lock (_dirCache) _dirCache.Clear();
        _fallbackWhite = null;
    }

    /// <summary>Génération de mipmaps à la volée — activée par
    /// défaut (kill le shimmer/aliasing à distance sur terrain,
    /// props et personnages). <c>EDEN_MIPMAPS=0</c> désactive
    /// (mesure A/B, repli d'urgence si VRAM tendue).</summary>
    public static bool MipmapsEnabled
    {
        get
        {
            if (IsHeadless) return false;
            var v = System.Environment.GetEnvironmentVariable(
                "EDEN_MIPMAPS");
            return v is not ("0" or "false" or "off");
        }
    }

    /// <summary>Fichier sous <see cref="RawAssetsRoot"/> (fsm, ini,
    /// area…) — résolution insensible à la casse.</summary>
    public static string? ResolveRawAsset(string relPath)
    {
        var root = RawAssetsRoot;
        if (root.Length == 0) return null;
        return ResolveFile(Path.Combine(root,
            relPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Fichier sous <see cref="AssetsRoot"/>.</summary>
    public static string? ResolveAssetFile(string relPath) =>
        ResolveFile(Path.Combine(AssetsRoot,
            relPath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Résolution insensible à la casse, segment par segment
    /// (les données natives mélangent les casses : S009.fsm vs s009.ini).
    /// Cache les listings de répertoires. En dernier recours, traduit
    /// vers le VFS pack — retourne alors une URI « epk://… » (null
    /// sans pack monté : le flux loose est inchangé).</summary>
    public static string? ResolveFile(string absPath)
    {
        if (Vfs.EdenVfs.IsEpk(absPath))
            return Vfs.EdenVfs.Resolve(absPath);
        // Mode pack-seul (release ou EDEN_ALLOW_LOOSE=0) : jamais de
        // loose — sinon un fichier lâche masquerait les trous du pack.
        if (!Vfs.EdenVfs.LooseAllowed)
            return Vfs.EdenVfs.PackResolve(absPath);
        if (File.Exists(absPath) || Directory.Exists(absPath))
            return absPath;
        return Vfs.EdenVfs.CaseResolve(absPath)
            ?? Vfs.EdenVfs.PackResolve(absPath);
    }

    // ------------------------------------------------------------------

    private static ImageTexture? LoadTexture(string relPath)
    {
        // Normalise : retire l'extension source (.dds/.ifl/.tga → .png).
        var rel = relPath.Replace('\\', '/');
        var dot = rel.LastIndexOf('.');
        var stem = dot > 0 ? rel[..dot] : rel;
        var foundExisting = false;
        // Natif (EDEN_NATIVE=tex) : .dds EDENFR d'origine, blocs BC transmis tels quels.
        if (Native.NativeMode.Tex && !rel.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase))
        {
            var native = LoadNativeDds(rel, stem);
            if (native != null) return native;
        }
        // .ifl : première frame (les animations passent par WaterAnimator / NativeFx).
        if (rel.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase)
            && ResolveRawAsset(rel) is { } iflPath
            && Vfs.EdenVfs.ReadAllText(iflPath) is { } iflText
            && World.IflFile.Parse(iflText).Frames is { Count: > 0 } frames)
        {
            var f0 = frames[0].Replace('\\', '/');
            var fstem = f0.Contains('.') ? f0[..f0.LastIndexOf('.')] : f0;
            foundExisting = true;
            if (LoadNativeDds(f0, fstem) is { } t0) return t0;
        }
        // PNG brut d'origine (imagesets uidata — ToolTips.png…) :
        // pas un DDS → lecture image directe avant l'échec.
        if (rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && ResolveRawAsset(rel) is { } pngPath
            && Vfs.EdenVfs.LoadImage(pngPath) is { } png)
        {
            return ImageTexture.CreateFromImage(png);
        }
        // Échecs cachés dans _texCache → un seul log par chemin.
        GD.PrintErr(foundExisting
            ? $"[assets] texture illisible : {relPath}"
            : $"[assets] texture absente : {relPath}");
        return null;
    }

    /// <summary>Texture depuis le .dds d'origine. Cherche rel, puis stem + « .dds »
    /// sous la racine de données EDENFR et sous <c>map/texture</c>, <c>ui</c>…</summary>
    private static ImageTexture? LoadNativeDds(string rel, string stem)
    {
        string? p = null;
        foreach (var cand in new[] { stem + ".dds", rel })
            if ((p = ResolveRawAsset(cand)) != null) break;
        if (p == null) return null;
        var bytes = Vfs.EdenVfs.ReadAllBytes(p);
        if (bytes == null) return null;
        var img = Native.DdsTexture.Load(bytes, out var err);
        if (img == null)
        {
            GD.PrintErr($"[assets] dds natif illisible : {rel} ({err})");
            return null;
        }
        TallyTexBytes(stem, img);
        if (!img.HasMipmaps() && !img.IsCompressed() && MipmapsEnabled &&
            img.GetWidth() > 1 && img.GetHeight() > 1)
            img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static string ResolveAssetsRoot()
    {
        var env = System.Environment.GetEnvironmentVariable(
            "EDEN_ASSETS_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return Path.GetFullPath(env);
        // Racines de données (DataPaths) : <racine>/assets — couvre
        // $EDEN_DATA_DIR/assets et <exe>/gamedata/assets (package).
        foreach (var root in DataPaths.CandidateDataRoots())
        {
            var p = Path.Combine(root, "assets");
            if (Directory.Exists(p)) return p;
        }
        // res:// = godot_project/ ; assets/ est .gdignore'd mais existe
        // physiquement — GlobalizePath donne le chemin absolu.
        var viaRes = ProjectSettings.GlobalizePath("res://assets");
        if (Directory.Exists(viaRes)) return viaRes;
        // Hors Godot (tests) : remonter depuis le CWD vers un
        // godot_project/assets.
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 6; i++)
        {
            var p = Path.Combine(dir, "godot_project", "assets");
            if (Directory.Exists(p)) return Path.GetFullPath(p);
            var a = Path.Combine(dir, "assets");
            if (Directory.Exists(a)) return Path.GetFullPath(a);
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return viaRes;
    }

    private static string ResolveRawAssetsRoot()
    {
        var env = System.Environment.GetEnvironmentVariable(
            "EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return Path.GetFullPath(env);
        // Première racine candidate contenant map/, data/ ou uidata/
        // (EDEN_DATA_DIR → <exe>/gamedata → raw_assets dev →
        // res://gamedata).
        return DataPaths.FindDataRoot() ?? string.Empty;
    }
}
