using Godot;
using EdenClient.Data.Tables;

namespace EdenClient.World;

/// <summary>
/// Assemble la scène 3D d'une map Eden (« S009 ») :
///
///   Map_S009 (Node3D, espace Godot Y-up)
///   ├─ World          (Node3D rot −90°/X — contenu en coords fsm Z-up)
///   │   ├─ Terrain    (MeshInstance3D par tuile — TerrainBuilder)
///   │   └─ Objects    (GLB convertis ou placeholders BoxMesh)
///   ├─ Environment    (DirectionalLight3D + WorldEnvironment/fog —
///   │                  depuis map/model/Sxxx/Sxxx.ini)
///   └─ Entities       (placeholders NPC depuis data/scene/sxxx.ini —
///                      positions converties via WorldConv)
///
/// Résolution des fichiers : <see cref="AssetCatalog.RawAssetsRoot"/>
/// pour .fsm/.ini, <see cref="AssetCatalog"/> pour textures/GLB.
/// </summary>
public partial class MapLoader : Node3D
{
    /// <summary>Gain appliqué à l'énergie ambiante (LitColor* ini) —
    /// le natif ne laisse pas l'ambiant laver la scène autant qu'un
    /// Godot à 1.0 ; ajusté visuellement sur les captures natives.</summary>
    public static float AmbientEnergy = 0.55f;
    /// <summary>Gain sur l'énergie du soleil (LitDifColor* ini).</summary>
    public static float SunEnergy = 1.4f;

    /// <summary>Phase du cycle jour/nuit chargée (clés
    /// Sky*/LitColor*/Fog* de Sxxx.ini). <c>--phase=Day|Sunrise|Sunset|
    /// Night</c> override ; défaut Day. Lue par BuildEnvironment et
    /// propagée via la meta <c>env_phase</c> de la racine map
    /// (FogCeiling, WeatherEffects).</summary>
    public static MapEnvIni.Phase EnvPhase { get; set; } =
        MapEnvIni.Phase.Day;

    /// <summary>Force l'activation météo (<c>--weather</c>) — les ini
    /// portent toutes <c>WeatherSwitch=0</c> (off natif) ; l'override
    /// sert aux captures de vérif.</summary>
    public static bool WeatherForce = false;

    /// <summary>Lit <c>--phase=</c> et <c>--weather</c> dans les args
    /// (utilisables depuis GameScene ET VisTest — parse autonome,
    /// idempotent).</summary>
    private static void ParseEnvArgs()
    {
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--phase=", StringComparison.Ordinal) &&
                Enum.TryParse(a[8..], true, out MapEnvIni.Phase p))
                EnvPhase = p;
            else if (a == "--weather" || a == "--weather=on")
                WeatherForce = true;
            else if (a == "--weather=off")
                WeatherForce = false;
        }
    }

    /// <summary>Progression du chargement (fraction, libellé) — émis
    /// depuis le thread de construction (<see cref="LoadMap"/> peut
    /// tourner en tâche de fond ; l'abonné doit reposter sur le
    /// thread principal, ex. <c>Screens.LoadingScreen.Report</c>
    /// qui est thread-safe).</summary>
    public event Action<float, string>? LoadProgress;

    /// <summary>Map chargée (null avant <see cref="LoadMap"/>).</summary>
    public FsmFile? Fsm { get; private set; }

    /// <summary>Murs invisibles natifs (map/sknodemap/Sxxx.smp) ;
    /// null si absent ou EDEN_NO_NODEMAP=1.</summary>
    public NodeMapFile? NodeMap { get; private set; }

    /// <summary>Case (x, z Godot) bloquée par un mur invisible.</summary>
    public bool IsNodeBlocked(float gx, float gz) =>
        NodeMap != null && NodeMap.IsBlocked(gx, -gz);
    /// <summary>Données de scène (spawns/NPC/events), null si absente.</summary>
    public SceneTable.Scene? Scene { get; private set; }
    /// <summary>Racine « World » tournée (coords natives).</summary>
    public Node3D? WorldRoot { get; private set; }
    /// <summary>Code de la map chargée.</summary>
    public string MapCode { get; private set; } = string.Empty;

    /// <summary>Construit la scène 3D en mode solo (crée les holders
    /// de PNJ dans <c>Entities</c>). En mode live, <c>spawnSceneNpcs=false</c>
    /// évite les PNJ en double (P0.3) : seul <see cref="EntityRegistry"/>
    /// instancie les entités kind 8 envoyées par le serveur, après avoir
    /// préchargé les GLB/anims de <see cref="Scene"/>.</summary>
    public bool SpawnSceneNpcs { get; set; } = true;

    /// <summary>Charge la map et retourne sa racine (non encore
    /// parentée — l'appelant l'ajoute à l'arbre).</summary>
    public Node3D LoadMap(string mapCode)
    {
        MapCode = mapCode;
        // Instrumentation perf (lot G1) — traces [perf] quand --perf.
        var t0 = Perf.PerfTrace.Mark();
        // Le .fsm a pu être pré-parsé par l'écran de chargement en
        // tâche de fond (SessionCarry.PreparedFsm) — on le réutilise
        // au lieu de relire 15-20 Mo.
        // Géométrie : [globe_data] col0 de data/scene/{mapCode}.ini
        // est autoritaire (« S055.fsm » dans s137.ini : la scène du
        // node déclare sa géométrie partagée — ses portails/PNJ restent
        // les siens). À défaut, le modèle du même nom que la map.
        var sceneIni = AssetCatalog.ResolveRawAsset(
            $"data/scene/{mapCode}.ini");
        var declared = sceneIni == null ? null
            : SceneTable.GlobeModelName(sceneIni);
        var geoCode = declared is { Length: > 0 } dd
            ? Path.GetFileNameWithoutExtension(dd) : mapCode;
        if (!geoCode.Equals(mapCode, StringComparison.OrdinalIgnoreCase))
            GD.Print($"[MapLoader] {mapCode} : géométrie déclarée " +
                     $"{declared} (scène propre conservée)");

        var prepared = EdenClient.Screens.SessionCarry.PreparedFsm;
        EdenClient.Screens.SessionCarry.PreparedFsm = null;
        if (prepared != null)
            Fsm = prepared;
        else if (declared == null || declared.EndsWith(
                     ".fsm", StringComparison.OrdinalIgnoreCase))
        {
            var fsmPath = AssetCatalog.ResolveRawAsset(
                $"map/model/{geoCode}/{geoCode}.fsm");
            if (fsmPath != null)
                Fsm = FsmFile.Load(fsmPath);
        }
        Perf.PerfTrace.Report("  fsm", t0);
        NodeMap = null;
        if (System.Environment.GetEnvironmentVariable(
                "EDEN_NO_NODEMAP") != "1")
        {
            // map/sknodemap/fix/ : .smp US corrigés (tools/fix_nodemaps.py)
            // pour les maps dont le .smp EDENFR bloque tout. La grille
            // suit la géométrie (geoCode), pas le code de la scène.
            var smp = DataPaths.ResolveFile(
                    $"map/sknodemap/fix/{geoCode}.smp")
                ?? AssetCatalog.ResolveRawAsset(
                    $"map/sknodemap/{geoCode}.smp");
            if (smp != null) NodeMap = NodeMapFile.Load(smp);
        }

        var root = new Node3D { Name = $"Map_{mapCode}" };
        WorldRoot = new Node3D
        {
            Name = "World",
            RotationDegrees = WorldConv.EdenRootEulerDeg,
        };
        root.AddChild(WorldRoot);

        if (Fsm != null)
        {
            LoadProgress?.Invoke(0.55f, "Terrain…");
            t0 = Perf.PerfTrace.Mark();
            WorldRoot.AddChild(TerrainBuilder.Build(Fsm, geoCode));
            Perf.PerfTrace.Report("  terrain+grass", t0);
            t0 = Perf.PerfTrace.Mark();
            BuildObjects(WorldRoot);
        }
        else
        {
            // Maps sans .fsm = un seul modèle NIF (S018, S035, S095…) —
            // le GLB converti est models/map/model/Sxxx/Sxxx.glb.
            // Modèle : [globe_data] col0 de data/scene/Sxxx.ini quand la
            // map a une scène (ex. « S401.nif » — autoritaire : les
            // event_area sont écrites pour ce modèle) ; sinon
            // local.ini [Scene] ModelName (S995 → S402 : map sans
            // scène propre qui réutilise le modèle d'une autre) ;
            // sinon le modèle du même nom que la map.
            var modelStem = geoCode;
            if (declared == null)
            {
                var localIni = AssetCatalog.ResolveRawAsset(
                    $"map/model/{mapCode}/local.ini");
                if (localIni != null)
                {
                    var mn = MapEnvIni.Load(localIni)
                        .Find("Scene")?.Get("ModelName")?.Trim();
                    if (!string.IsNullOrEmpty(mn) &&
                        !mn.Equals(mapCode,
                            StringComparison.OrdinalIgnoreCase))
                        modelStem = mn;
                }
            }
            if (AssetCatalog.TryGetGlb(
                    $"map/model/{modelStem}/{modelStem}", out var mapGlb) &&
                AssetCatalog.LoadGlbNode(mapGlb) is { } model)
            {
                model.Name = "MapModel";
                WorldRoot.AddChild(model);
                // Même plafond FogMax que le chemin .fsm (sinon le fog
                // builtin monte à 100 % — grottes S101/S110 trop
                // sombres).
                if (FogCeiling.Create() is { } fog)
                    WorldRoot.AddChild(fog);
                // Comme pour chaque objet d'une map .fsm : les helpers « true wall »/sol du NIF deviennent des
                // colliders (sans quoi le joueur traverse murs et sols de ces ~80 maps).
                var nColl = CollisionBuilder.Apply(model);
                var (nBodies, _) = nColl > 0 ? ColliderConsolidator.Consolidate(model) : (0, 0);
                GD.Print($"[MapLoader] {mapCode} : collisions {nColl} helpers → {nBodies} corps");
                GD.Print($"[MapLoader] {mapCode} : modèle unique " +
                         $"(pas de .fsm)");
            }
            else
            {
                GD.PrintErr($"[MapLoader] {mapCode} : ni .fsm ni " +
                            $"GLB map/model/{modelStem}/{modelStem}.glb");
            }
        }
        // Parallélisation désactivée (P0.1) : l'ancien chargeur glTF /
        // GenerateScene + Texture2D.GetImage() en parallèle multi-threads
        // pendant que le SceneTree tourne provoquent un deadlock natif
        // Godot (ObjectDB / RenderingServer command queue). Tout LoadMap
        // tourne déjà sur UN worker dédié hors du thread principal.
        // Environnement (fog/ciel/lumière) = ini de la géométrie ;
        // entités/portails/spawn = scène propre du node.
        BuildEnvironment(root, geoCode);
        BuildEntities(root, mapCode);
        Perf.PerfTrace.Report("  objects+env+entities", t0);
        return root;
    }

    /// <summary>Hauteur terrain en coords Eden (x, y) → z, false si
    /// hors carte. Wrapper <see cref="TerrainSampler"/>.</summary>
    public bool TryGetEdenHeight(float x, float y, out float z)
    {
        z = 0f;
        return Fsm != null &&
               TerrainSampler.TryGetHeight(Fsm, x, y, out z);
    }

    /// <summary>Sol sous (x, z) pour une entité distante — frame
    /// physique obligatoire (appelé depuis le _PhysicsProcess du
    /// registre). <paramref name="terrainY"/> = NaN sur une map sans
    /// heightfield (modèle unique : rayon depuis refY, sinon la
    /// surface la plus basse) ; sinon plancher minimum — un sol OBJ
    /// (pont/dalle, <see cref="WalkableGround.FloorLayer"/>) au-dessus
    /// du terrain l'emporte, jamais en dessous. null si aucun
    /// collider.</summary>
    public float? SampleEntityGround(float gx, float gz, float refY,
        float terrainY)
    {
        if (WorldRoot == null || !WorldRoot.IsInsideTree()) return null;
        var hasTerrain = !float.IsNaN(terrainY);
        // hasTerrain → SampleAt retourne toujours vrai (terrain en
        // plancher minimum, sol OBJ au-dessus gagnant).
        if (_walk.PeekNearestY(WorldRoot, gx, gz, refY, hasTerrain,
                terrainY, out var g))
            return g;
        return _walk.PeekNearest(WorldRoot, gx, gz, refY, out g)
            ? g : null;
    }

    /// <summary>Sol marchable sous la position Godot : heightfield
    /// terrain + raycast sur la layer
    /// <see cref="WalkableGround.FloorLayer"/> (dalles/ponts helpers —
    /// pont S002). Le raycast n'est tenté QUE pendant les frames
    /// physique (<see cref="Engine.IsInPhysicsFrame"/>) : c'est le
    /// contexte du joueur local — son délégué
    /// <c>GroundHeight</c> est branché ici par la scène — et ça coûte
    /// un rayon/frame ; les appelants « idle » (snap d'entités
    /// distantes, clic-sol, spawn) gardent le heightfield pur —
    /// identique à avant — et ne cassent pas le suivi « sticky » du
    /// marcheur. Hors arbre (chargement) : terrain pur.</summary>
    public bool TryGetGodotHeight(Vector3 godotPos, out float y)
    {
        var (ex, ey) = WorldConv.ToEdenGround(godotPos);
        var hasTerrain = TryGetEdenHeight(ex, ey, out y);
        if (Engine.IsInPhysicsFrame() && WorldRoot != null &&
            WorldRoot.IsInsideTree())
        {
            if (_walk.Sample(WorldRoot, godotPos.X, godotPos.Z,
                    hasTerrain, y, out y))
                return true;
            // Modèle unique sans heightfield : la référence est sous
            // ou sur le sol réel (spawn local.ini en 2D) — rayon
            // montant de secours.
            if (!hasTerrain)
                return _walk.SampleUp(WorldRoot, godotPos.X,
                    godotPos.Z, out y);
            return false;
        }
        return hasTerrain;
    }

    /// <summary>Variante « y-aware » de
    /// <see cref="TryGetGodotHeight"/> : <paramref name="godotPos"/>
    /// .Y sert de référence du rayon (position courante du corps —
    /// dessus/dessous d'un pont résolu sans heuristique). SEAM pour
    /// un futur délégué joueur transportant y : le signature actuelle
    /// <c>GroundHeight(x,z)</c> ne la porte pas — elle passe par le
    /// suivi interne de <see cref="WalkableGround"/>.</summary>
    public bool TryGetWalkableHeight(Vector3 godotPos, out float y)
    {
        var (ex, ey) = WorldConv.ToEdenGround(godotPos);
        var hasTerrain = TryGetEdenHeight(ex, ey, out y);
        // Pas de repli SampleUp ici (colonne entière) : cette requête
        // sert le marcheur — un sol loin au-dessus de la référence est
        // un plafond, pas une destination (S198 : snap +195 u sur le
        // toit d'une plateforme). Le spawn passe par
        // TryGetGodotHeight → SampleUp, inchangé.
        var ok = _walk.SampleAt(WorldRoot, godotPos.X, godotPos.Z,
            godotPos.Y, hasTerrain, y, out y);
        if (System.Environment.GetEnvironmentVariable(
                "EDEN_COLLISION_DIAG") == "1")
        {
            GD.Print($"[walky] ({godotPos.X:F0},{godotPos.Y:F0}," +
                     $"{godotPos.Z:F0}) terrain={hasTerrain} ok={ok} " +
                     $"y={y:F1}");
            if (!ok && WorldRoot?.GetWorld3D()?.DirectSpaceState
                    is { } sp)
            {
                var q = PhysicsRayQueryParameters3D.Create(
                    new Vector3(godotPos.X, 4000f, godotPos.Z),
                    new Vector3(godotPos.X, -4000f, godotPos.Z),
                    0xFFFFFFFF);
                q.HitFromInside = true;
                var hit = sp.IntersectRay(q);
                if (hit.Count > 0)
                {
                    var body = hit["collider"].AsGodotObject() as StaticBody3D;
                    GD.Print($"[walky-probe] hit " +
                        $"y={hit["position"].AsVector3().Y:F1} " +
                        $"l={body?.CollisionLayer} " +
                        $"{hit["collider"].AsGodotObject()}");
                }
            }
        }
        return ok;
    }

    /// <summary>Suivi « sol marchable » du marcheur local (layer
    /// floor) — <see cref="WalkableGround"/>.</summary>
    private readonly WalkableGround _walk = new();

    /// <summary>Verrou des attaches directes à la racine map —
    /// objects/env/entities sont construits en tâches parallèles
    /// (sous-arbres détachés disjoints), mais plusieurs AddChild
    /// concurrents sur le MÊME parent « root » ne sont pas sûrs.</summary>
    private readonly object _rootLock = new();

    /// <summary>Point de spawn joueur en coords Eden : première aire
    /// REVIVE → première GATEWAY → [position] indexée par born_area →
    /// première position → centre de la map.</summary>
    public System.Numerics.Vector3 GetSpawnPoint()
    {
        var s = Scene;
        if (s != null)
        {
            var revive = s.EventAreas.FirstOrDefault(a =>
                a.Type.Equals("REVIVE", StringComparison.OrdinalIgnoreCase));
            if (revive != null)
                return new System.Numerics.Vector3(
                    revive.X, revive.Y, revive.Z);
            var gateway = s.EventAreas.FirstOrDefault(a =>
                a.Type.Equals("GATEWAY",
                    StringComparison.OrdinalIgnoreCase));
            if (gateway != null)
                return new System.Numerics.Vector3(
                    gateway.X, gateway.Y, gateway.Z);
            if (s.BornPositionIndex > 0 &&
                s.BornPositionIndex <= s.Positions.Count)
            {
                var p = s.Positions[s.BornPositionIndex - 1];
                return new System.Numerics.Vector3(p.X, p.Y, p.Z);
            }
            if (s.Positions.Count > 0)
            {
                var p = s.Positions[0];
                return new System.Numerics.Vector3(p.X, p.Y, p.Z);
            }
            // Maps à modèle unique sans spawn déclaré (donjons
            // portail S406/S620 : born/revive/gateway/position
            // vides) : le premier [event_area] est le portail
            // d'entrée — le serveur donne la vraie position en jeu
            // en ligne ; en solo c'est la porte logique.
            if (s.EventAreas.Count > 0)
            {
                var a = s.EventAreas[0];
                return new System.Numerics.Vector3(a.X, a.Y, a.Z);
            }
        }
        // Maps à modèle unique (donjons — pas de data/scene ni de .fsm) :
        // local.ini de l'éditeur porte [Player] pos=x,y ; z = snap au
        // sol au runtime, comme les positions de scène.
        var localPath = AssetCatalog.ResolveRawAsset(
            $"map/model/{MapCode}/local.ini");
        if (localPath != null)
        {
            var pos = MapEnvIni.Load(localPath)
                .Find("Player")?.Get("pos");
            if (pos != null)
            {
                var xy = pos.Split(',');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (xy.Length >= 2 &&
                    float.TryParse(xy[0].Trim(), ci, out var px) &&
                    float.TryParse(xy[1].Trim(), ci, out var py))
                    return new System.Numerics.Vector3(px, py, 0f);
            }
        }
        var f = Fsm;
        if (f != null && TryGetEdenHeight(f.Header.WorldSizeX * 0.5f,
                f.Header.WorldSizeY * 0.5f, out var z))
            return new System.Numerics.Vector3(f.Header.WorldSizeX * 0.5f,
                f.Header.WorldSizeY * 0.5f, z);
        // Dernier repli (S994 : aucune source de spawn authentique) :
        // case libre du .smp la plus proche du centre — donnée native
        // de marchabilité, puis snap au sol par le scan de collision.
        if (NodeMap is { } nm)
        {
            var cx0 = nm.Width / 2f;
            var cy0 = nm.Height / 2f;
            var best = double.MaxValue;
            var bx = 0f;
            var by = 0f;
            for (var cy = 0; cy < nm.Rows; cy++)
                for (var cx = 0; cx < nm.Cols; cx++)
                {
                    if (nm.IsCellBlocked(cx, cy)) continue;
                    var x = (cx + 0.5f) * 0.5f;
                    var y = (cy + 0.5f) * 0.5f;
                    var d = (x - cx0) * (x - cx0) + (y - cy0) * (y - cy0);
                    if (d < best) { best = d; bx = x; by = y; }
                }
            if (best < double.MaxValue)
                return new System.Numerics.Vector3(bx, by, 0f);
        }
        return System.Numerics.Vector3.Zero;
    }

    // ------------------------------------------------------------------

    /// <summary>Chunk OBJ : GLB converti si disponible, sinon
    /// placeholder boxe avec metadata (nom du modèle).</summary>
    private void BuildObjects(Node3D world)
    {
        var objs = new Node3D { Name = "Objects" };
        var fsm = Fsm!;
        // Lot G1 : précharge en parallèle les protos GLB distincts —
        // ~2,6 s de parse séquentiel sur S001 (3 769 objets, ~600
        // modèles uniques). Le Duplicate reste sur le thread principal.
        var protos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in fsm.SceneObjects)
            if (AssetCatalog.TryGetGlb(
                    $"map/model/object/{obj.Name}", out var gp))
                protos.Add(gp);
        AssetCatalog.PreloadGlbProtos(protos, maxDegree: 1);

        // Instanciation séquentielle : Node.Duplicate en parallèle
        // s'est avéré NON sûr (blocage mesuré lot G1 — probable
        // contention interne ObjectDB) ; seul le parse des protos est
        // parallélisé (ci-dessus), le Duplicate reste main-thread.
        var loaded = 0;
        var placeholders = 0;
        var colliders = 0;
        // Batching des props statiques (lot G2) : les objets éligibles
        // partent en MultiMesh (modèle × cellule 64 u) — voir
        // PropBatcher pour les critères (pas de collider ni alpha
        // blend). --props-batch=off désactive.
        var batcher = PropBatcher.Enabled ? new PropBatcher() : null;
        var total = Math.Max(fsm.SceneObjects.Count, 1);
        var step = 0;
        // Diag : EDEN_OBJ_DUMP=<fichier> écrit « modèle x y z » (coords
        // Eden) de chaque objet — sert à localiser un prop fautif.
        if (System.Environment.GetEnvironmentVariable("EDEN_OBJ_DUMP")
            is { Length: > 0 } dumpPath)
            File.WriteAllLines(dumpPath, fsm.SceneObjects.Select(o =>
                FormattableString.Invariant(
                    $"{o.Name} {o.Position.X:F1} {o.Position.Y:F1} {o.Position.Z:F1}")));
        foreach (var obj in fsm.SceneObjects)
        {
            // Progression réelle vers l'écran de chargement (la boucle
            // tourne en tâche de fond — Report est thread-safe).
            if ((step++ & 0x7F) == 0)
                LoadProgress?.Invoke(0.60f + 0.25f * step / total,
                    $"Objets… {step}/{total}");
            // « (null) » est le nom littéral des enregistrements vides
            // de l'éditeur (10 dans le corpus) — rien à rendre.
            if (string.IsNullOrEmpty(obj.Name) || obj.Name == "(null)")
                continue;
            if (batcher != null &&
                batcher.TryCollect(obj.Name, ObjTransform(obj)))
                continue;
            var rel = $"map/model/object/{obj.Name}";
            Node3D node;
            if (AssetCatalog.TryGetGlb(rel, out var glbPath) &&
                AssetCatalog.LoadGlbNode(glbPath) is { } glb)
            {
                node = glb;
                node.Name = obj.Name;
                loaded++;
            }
            else if (TryTexVariant(obj.Name, out node))
            {
                loaded++;
            }
            else
            {
                var mi = new MeshInstance3D
                {
                    Name = $"Obj_{obj.Name}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(1.5f, 1.5f, 1.5f),
                    },
                };
                mi.SetMeta("model_name", obj.Name);
                mi.SetMeta("placeholder", true);
                node = mi;
                placeholders++;
                GD.Print($"[MapLoader] placeholder : {obj.Name} " +
                    $"({obj.Position.X:F1},{obj.Position.Y:F1}," +
                    $"{obj.Position.Z:F1})");
            }
            node.Transform = ObjTransform(obj);
            // Perf : les petits props (rayon < ~1,5 u post-scale) ne
            // projettent pas d'ombre — la passe shadow du soleil
            // 4-splits redessine chaque casters sur ~160 u, c'était le
            // premier poste de draw calls sur S001 (>4 000).
            DisableSmallShadows(node, obj.Scale);
            // Collision : les meshes « helper » masqués du GLB
            // (true wall/blo/Box — posés dans les NIF natifs pour
            // délimiter le joueur) deviennent des StaticBody3D —
            // murs sur layer 1, sols marchables sur layer floor
            // (CollisionBuilder/WalkableGround).
            if (CollisionBuilder.ShouldCollide(obj.Name) &&
                CollisionBuilder.Apply(node) > 0)
                colliders++;
            objs.AddChild(node);
        }
        batcher?.Flush(objs);
        if (_texVariantCount > 0)
            GD.Print($"[MapLoader] {_texVariantCount} objets en " +
                     "variante texture (_NN)");
        GD.Print($"[MapLoader] OBJ : {loaded} GLB, " +
                 $"{placeholders} placeholders, {colliders} objets " +
                 "avec collision" +
                 (batcher == null ? "" :
                  $", {batcher.Batched} props batchés en " +
                  $"{batcher.MultiMeshes} multimeshes + " +
                  $"{batcher.ColliderBodies} bodies " +
                  $"({batcher.Models} modèles)" +
                  batcher.RejectSummary()));
        lock (_rootLock)
        {
            world.AddChild(objs);
            // ~4 k StaticBody3D (wall+floor par prop) → 2 bodies
            // partagés par layer : −9,7 k→~5,8 k nœuds collision,
            // −6 ms/frame mesuré (--strip-colliders / bisect perf).
            var (freed, merged) = ColliderConsolidator.Consolidate(objs);
            if (freed > 0)
                GD.Print($"[MapLoader] colliders consolidés : " +
                         $"{freed} bodies + {merged} shapes → " +
                         "2 bodies wall/floor partagés");
        }
    }

    private int _texVariantCount;

    /// <summary>Code « stem_NN » (suffixe 2 chiffres) = modèle
    /// <c>stem.nif</c> + texture variante <c>stemNN.dds</c>
    /// (convention RF Online ; UC409_02 → uc409 + UC40902.dds — aucun
    /// fichier *_NN.nif à deux chiffres n'existe dans le corpus).
    /// </summary>
    private bool TryTexVariant(string name, out Node3D node)
    {
        node = null!;
        var us = name.LastIndexOf('_');
        if (us <= 0 || name.Length - us - 1 != 2 ||
            !char.IsAsciiDigit(name[us + 1]) ||
            !char.IsAsciiDigit(name[us + 2]))
            return false;
        var stem = name[..us];
        var tex = AssetCatalog.GetTexture(
            $"map/texture/{stem}{name[(us + 1)..]}.dds");
        if (tex == null ||
            !AssetCatalog.TryGetGlb($"map/model/object/{stem}",
                out var gp) ||
            AssetCatalog.LoadGlbNode(gp) is not { } glb)
            return false;
        // Le proto est partagé/caché : dupliquer les matériaux de
        // l'instance pour y poser la texture variante.
        Retexture(glb, tex);
        glb.Name = name;
        node = glb;
        _texVariantCount++;
        return true;
    }

    private static void Retexture(Node3D node, Texture2D tex)
    {
        if (node is MeshInstance3D root) RetextureMi(root, tex);
        foreach (var mi in node
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
            RetextureMi(mi, tex);
    }

    private static void RetextureMi(MeshInstance3D mi, Texture2D tex)
    {
        var mesh = mi.Mesh;
        if (mesh == null) return;
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            switch (mi.GetSurfaceOverrideMaterial(s) ??
                    mi.GetActiveMaterial(s))
            {
                case ShaderMaterial sh when
                    sh.GetShaderParameter("albedo_tex").As<Texture2D>()
                        != null:
                    var sd = (ShaderMaterial)sh.Duplicate();
                    sd.SetShaderParameter("albedo_tex", tex);
                    mi.SetSurfaceOverrideMaterial(s, sd);
                    break;
                case StandardMaterial3D sm when sm.AlbedoTexture != null:
                    var md = (StandardMaterial3D)sm.Duplicate();
                    md.AlbedoTexture = tex;
                    mi.SetSurfaceOverrideMaterial(s, md);
                    break;
            }
        }
    }

    /// <summary>Désactive la projection d'ombre sur les meshes d'un
    /// objet dont le rayon englobant post-scale est < 1,5 u (pots,
    /// cailloux, débris — leur ombre est imperceptible et chaque
    /// caster coûte un draw dans chacun des 4 splits).</summary>
    private static void DisableSmallShadows(Node3D node, float scale)
    {
        var stack = new Stack<Node>();
        stack.Push(node);
        var any = false;
        var big = false;
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null &&
                !mi.HasMeta("collision_helper") && mi.Visible)
            {
                var r = mi.Mesh.GetAabb().Size.Length() * 0.5f *
                        scale;
                if (r >= 1.5f) { big = true; break; }
                any = true;
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        if (!any || big) return;
        // Tout l'objet est petit → ombres off sur ses meshes.
        foreach (var mi in node
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
            mi.CastShadow =
                GeometryInstance3D.ShadowCastingSetting.Off;
    }

    /// <summary>Transform natif (pos + mat3 + scale) → Transform3D
    /// dans les coords Z-up du parent « World ».</summary>
    private static Transform3D ObjTransform(SceneObject obj)
    {
        var m = obj.Rotation;
        var basis = new Basis(
            new Vector3(m.M11, m.M21, m.M31),
            new Vector3(m.M12, m.M22, m.M32),
            new Vector3(m.M13, m.M23, m.M33));
        basis = basis.Scaled(new Vector3(obj.Scale, obj.Scale, obj.Scale));
        return new Transform3D(basis, new Vector3(
            obj.Position.X, obj.Position.Y, obj.Position.Z));
    }

    /// <summary>Lumière + brouillard + ciel depuis
    /// map/model/Sxxx/Sxxx.ini (phase Day du cycle — le cycle complet
    /// est un lot futur).
    ///
    /// Décodage validé sur les phases : les couleurs ini sont des
    /// entiers 0xRRGGBB (preuve : LitColorNight S009 = 0x17285B =
    /// bleu nuit, incohérent en BGR).
    ///
    /// Brouillard natif ≈ rampe linéaire atteignant <c>FogMax</c> à
    /// <c>FogSightRang</c> (le max natif est plafonné à ~8-22 % — les
    /// plans éloignés des captures natives restent nets). Le fog
    /// linéaire de Godot plafonne à 100 % : on l'étire avec
    /// <c>FogDepthEnd = SightRange / FogMax</c> — rampe exacte jusqu'à
    /// SightRange, légèrement plus dense au-delà (approximation
    /// documentée).
    ///
    /// Soleil : direction lue sur le nœud « sun » du dôme de ciel natif
    /// (<see cref="SkyDome"/>) — les angles absents du .ini.
    /// </summary>
    private void BuildEnvironment(Node3D root, string mapCode)
    {
        ParseEnvArgs();
        var envPath = AssetCatalog.ResolveRawAsset(
            $"map/model/{mapCode}/{mapCode}.ini");
        MapEnvIni.PhaseEnv? phase = null;
        MapEnvIni? env = null;
        if (envPath != null)
        {
            env = MapEnvIni.Load(envPath);
            phase = env.GetPhase(EnvPhase);
        }
        // Phase résolue exposée aux nœuds d'environnement enfants
        // (FogCeiling recharge l'ini pour la phase demandée).
        root.SetMeta("env_phase", (int)EnvPhase);

        // ---- Ciel : dôme natif sous « World » (suivi caméra).
        var sunPos = new Vector3(0.35f, 0.8f, -0.45f);
        if (phase?.Sky is { Length: > 0 } skyCode &&
            SkyDome.Create(skyCode) is { } dome)
        {
            WorldRoot!.AddChild(dome);
            if (dome.SunDirectionEden.LengthSquared() > 0.01f)
            {
                var s = dome.SunDirectionEden;
                sunPos = WorldConv.ToGodot(s.X, s.Y, s.Z).Normalized();
            }
        }

        var sun = new DirectionalLight3D { Name = "Sun" };
        if (phase is { } p)
        {
            var (r, g, b) = p.LitDifColor;
            if (r == 0 && g == 0 && b == 0)
                (r, g, b) = p.LitColor;
            if (r != 0 || g != 0 || b != 0)
                sun.LightColor = new Godot.Color(r / 255f, g / 255f,
                    b / 255f);
        }
        sun.LightEnergy = SunEnergy;
        // La lumière voyage le long de -Z : viser -sunPos oriente le
        // faisceau depuis la position du soleil vers le monde.
        sun.LookAtFromPosition(Vector3.Zero, -sunPos);
        sun.ShadowEnabled = true;
        // 2 splits / 100 u : chaque caster coûte une passe ombre par
        // split — ~3 000 draw calls sur S001 avec 4 splits à 160 u ;
        // le natif ne projette qu'à courte portée (blob shadows pour
        // les entités). Le blend splits garde la transition douce.
        sun.DirectionalShadowMode =
            DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowMaxDistance = 100f;
        sun.DirectionalShadowBlendSplits = true;
        // Pancake 20 u (défaut) : les casters plus hauts que l'extension
        // vers le soleil sont écrasés sur le plan proche de la cascade →
        // leur ombre change de forme/place selon la caméra (la cascade
        // suit le frustum). Large pancake = ombres stables (hypothèse,
        // à confirmer en jeu : comparer //goto avec caméra tournée).
        sun.DirectionalShadowPancakeSize = 200f;
        // Diag : --no-shadows mesure le coût de la passe d'ombres.
        if (OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
                .Contains("--no-shadows"))
            sun.ShadowEnabled = false;
        lock (_rootLock) root.AddChild(sun);

        var we = new WorldEnvironment { Name = "Environment" };
        var e = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = BuildSkyFallback(phase),
            },
            FogEnabled = true,
            FogDepthBegin = 0f,
        };
        if (phase is { } fp)
        {
            var (fr, fg, fb) = fp.FogColor;
            if (fr != 0 || fg != 0 || fb != 0)
                e.FogLightColor = new Godot.Color(fr / 255f, fg / 255f,
                    fb / 255f);
            if (fp.FogSightRange > 0f)
                e.FogDepthEnd = fp.FogSightRange /
                    Math.Max(fp.FogOpacityCap, 0.05f);
            else
                e.FogDepthEnd = 500f;
            var (ar, ag, ab) = fp.LitColor;
            if (ar != 0 || ag != 0 || ab != 0)
            {
                e.AmbientLightSource =
                    Godot.Environment.AmbientSource.Color;
                e.AmbientLightColor = new Godot.Color(ar / 255f,
                    ag / 255f, ab / 255f);
                e.AmbientLightEnergy = AmbientEnergy;
            }
        }
        else
        {
            // Pas de .ini de map : fog doux + ciel neutre — jamais
            // de monde noir pour une map sans données.
            e.FogDepthBegin = 60f;
            e.FogDepthEnd = 600f;
        }
        // Plancher d'ambiance : LitColor absent/nul ou phase manquante
        // → ambiant minimal garanti (≥ 0,4) pour que le monde reste
        // lisible quoi qu'il arrive.
        var amb = e.AmbientLightColor;
        var ambLum = 0.2126f * amb.R + 0.7152f * amb.G + 0.0722f * amb.B;
        if (e.AmbientLightSource != Godot.Environment.AmbientSource.Color ||
            ambLum < 0.05f)
        {
            e.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            e.AmbientLightColor = new Godot.Color(0.45f, 0.47f, 0.55f);
        }
        e.AmbientLightEnergy = Math.Max(e.AmbientLightEnergy, 0.4f);
        we.Environment = e;
        lock (_rootLock) root.AddChild(we);

        // Météo ini ([Weather]/[WeatherParticle]) — inactive quand
        // WeatherSwitch=0 (toutes les maps actuelles) sauf --weather.
        if (env != null &&
            WeatherEffects.Build(env, EnvPhase, WeatherForce)
                is { } weather)
            lock (_rootLock) WorldRoot!.AddChild(weather);
    }

    /// <summary>Ciel procédural de secours teinté du fog ini — ne
    /// sert que pour les directions non couvertes par le dôme natif
    /// (et si le GLB sky est absent).</summary>
    private static ProceduralSkyMaterial BuildSkyFallback(
        MapEnvIni.PhaseEnv? phase)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyEnergyMultiplier = 0.9f,
            GroundEnergyMultiplier = 0.9f,
        };
        if (phase is { } p)
        {
            var (r, g, b) = p.FogColor;
            if (r != 0 || g != 0 || b != 0)
            {
                var fog = new Godot.Color(r / 255f, g / 255f,
                    b / 255f);
                sky.SkyHorizonColor = fog;
                sky.GroundHorizonColor = fog;
                sky.SkyTopColor = fog * 0.75f;
                sky.GroundBottomColor = fog * 0.6f;
            }
        }
        return sky;
    }

    /// <summary>Entités NPC depuis data/scene/sxxx.ini : le GLB du
    /// modèle (« model » col → <c>monster/model/&lt;code&gt;.glb</c>,
    /// rotation −90°/X — coords natives Z-up), repli capsule verte.
    /// Label du nom au-dessus (vert-jaune + outline — convention
    /// native).</summary>
    private void BuildEntities(Node3D root, string mapCode)
    {
        var iniPath = AssetCatalog.ResolveRawAsset(
            $"data/scene/{mapCode}.ini");
        var t = EdenClient.Perf.PerfTrace.Mark();
        if (iniPath != null)
            Scene = SceneTable.Load(iniPath);
        EdenClient.Perf.PerfTrace.Report("    scene ini", t);

        var entities = new Node3D { Name = "Entities" };
        lock (_rootLock) root.AddChild(entities);
        if (Scene == null) return;

        // Lot G1 / P0.4 : précharge en tâche de fond les GLB de
        // modèles, les textures de variantes (npclist col9) et les
        // fichiers d'anims des NPC distincts pendant le chargement.
        var npcProtos = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var animStems = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var skinTexs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var npc in Scene.Npcs)
        {
            var stem = npc.ModelCode;
            if (NpcModelTable.Resolve(npc.NpcId) is { } nd)
            {
                if (nd.Stem.Length > 0) stem = nd.Stem;
                if (nd.Texture.Length > 0) skinTexs.Add(nd.Texture);
            }
            if (stem.Length == 0) continue;
            if (AssetCatalog.TryGetGlb(
                    $"monster/model/{stem}", out var gp))
                npcProtos.Add(gp);
            animStems.Add($"monster/animation/{stem}");
        }
        t = EdenClient.Perf.PerfTrace.Mark();
        AssetCatalog.PreloadGlbProtos(npcProtos, maxDegree: 1);
        // Trésors au sol : les 10 familles coffre G00001-G00010 — un
        // drop froid coûtait ~13 ms de parse dans HandleSeeTreasure
        // (pic à chaque premier loot d'une session de grind).
        var treasureProtos = new List<string>(10);
        for (var i = 1; i <= 10; i++)
            if (AssetCatalog.TryGetGlb($"item/model/G{i:00000}",
                    out var tp))
                treasureProtos.Add(tp);
        AssetCatalog.PreloadGlbProtos(treasureProtos, maxDegree: 1);
        foreach (var st in skinTexs)
            AssetCatalog.GetTexture($"monster/texture/{st}");
        EdenClient.Perf.PerfTrace.Report(
            $"    npc protos ({npcProtos.Count}) + skins ({skinTexs.Count})", t);
        t = EdenClient.Perf.PerfTrace.Mark();
        Game.EntityAnimator.PreloadAnimFiles(animStems);
        EdenClient.Perf.PerfTrace.Report(
            $"    npc anims ({animStems.Count})", t);

        if (!SpawnSceneNpcs)
        {
            GD.Print($"[MapLoader] {Scene.Npcs.Count} NPC en table " +
                     $"(live : délégués à EntityRegistry), " +
                     $"{Scene.EventAreas.Count} event areas");
            return;
        }

        // Streaming post-entrée : les holders (position/métas/label)
        // sont créés ici — léger — mais le corps GLB (Duplicate +
        // skin + animator ≈ 20 ms × 263) part dans
        // <see cref="_pendingNpcBodies"/> que le jeu vide à ~4/frame
        // après « EN JEU », triés du plus proche du spawn au plus
        // lointain (le joueur voit les NPC proches d'abord).
        var n = 0;
        var spawn = GetSpawnPoint();
        var pending = new List<(Node3D Holder, SceneTable.NpcSpawn Npc,
            float Dist2)>();
        foreach (var npc in Scene.Npcs)
        {
            if ((n & 0x1F) == 0)
                LoadProgress?.Invoke(0.86f +
                    0.09f * n / Math.Max(Scene.Npcs.Count, 1),
                    $"Personnages… {n}/{Math.Max(Scene.Npcs.Count, 1)}");
            var holder = new Node3D
            {
                Name = $"Npc_{npc.NpcId}",
            };
            var (ex, ey) = (npc.X, npc.Y);
            var ez = TryGetEdenHeight(ex, ey, out var h) ? h : 0f;
            holder.Position = WorldConv.ToGodot(ex, ey, ez);
            holder.SetMeta("npc_id", npc.NpcId);
            holder.SetMeta("model", npc.ModelCode);
            if (npc.Facing != 0f)
                holder.Rotation = new Vector3(0f,
                    EntityRegistry.EdenFacingToYaw(npc.Facing), 0f);
            var en = EdenClient.Data.TextResolver.NpcName(npc.NpcId);
            var labelText = en;
            if (en != null &&
                EdenClient.Data.TextResolver.NpcTitle(npc.NpcId)
                    is { } npcTitle)
                labelText = $"<{npcTitle}>\n{en}";
            labelText ??= npc.Name;
            if (labelText.Length > 0)
            {
                var label = new Label3D
                {
                    Text = labelText,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Position = new Vector3(0f, 2.1f, 0f),
                    FontSize = 28,
                    PixelSize = 0.009f,
                    Modulate = new Godot.Color(0.75f, 1f, 0.45f),
                    OutlineSize = 6,
                    OutlineModulate = new Godot.Color(0f, 0f, 0f, 0.85f),
                    VisibilityRangeEnd = 65f,
                };
                holder.AddChild(label);
            }
            entities.AddChild(holder);
            var dx = npc.X - spawn.X;
            var dy = npc.Y - spawn.Y;
            pending.Add((holder, npc, dx * dx + dy * dy));
            n++;
        }
        pending.Sort((a, b) => a.Dist2.CompareTo(b.Dist2));
        // P0.4 : construit les corps des PNJ proches du spawn (< 75 m)
        // pendant le chargement ; les lointains restent différés.
        foreach (var item in pending)
        {
            if (item.Dist2 <= 75f * 75f)
                AttachNpcBody(item.Holder, item.Npc);
            else
                _pendingNpcBodies.Add(item);
        }
        GD.Print($"[MapLoader] {n} NPC ({n - _pendingNpcBodies.Count} proches préconstruits, " +
                 $"{_pendingNpcBodies.Count} différés), " +
                 $"{Scene.EventAreas.Count} event areas");
    }

    /// <summary>Corps NPC en attente (holder, spawn, distance² au
    /// point d'apparition) — vidés par
    /// <see cref="DrainNpcBodies"/> depuis le jeu (thread
    /// principal).</summary>
    private readonly List<(Node3D Holder,
        SceneTable.NpcSpawn Npc, float Dist2)> _pendingNpcBodies = new();
    private readonly Dictionary<(string, string), Node3D?>
        _skinnedProtos = new();
    private int _npcsBuilt;

    /// <summary>Construit les corps de NPC proches du joueur (< 80 m)
    /// dans un budget borné par <paramref name="max"/> et
    /// <paramref name="maxMs"/> ms (P0.4).</summary>
    public int DrainNpcBodies(int max, double maxMs = 2.5,
        Vector3? playerPos = null)
    {
        if (_pendingNpcBodies.Count == 0) return 0;
        var built = 0;
        var t0 = Time.GetTicksUsec();
        var budgetUs = (ulong)(maxMs * 1000.0);
        var ppos = playerPos ?? Vector3.Zero;
        for (var i = _pendingNpcBodies.Count - 1; i >= 0 && built < max; i--)
        {
            var (holder, npc, _) = _pendingNpcBodies[i];
            if (!GodotObject.IsInstanceValid(holder))
            {
                _pendingNpcBodies.RemoveAt(i);
                continue;
            }
            if (playerPos.HasValue &&
                (holder.Position - ppos).LengthSquared() > 80f * 80f)
                continue;
            _pendingNpcBodies.RemoveAt(i);
            AttachNpcBody(holder, npc);
            built++;
            if (Time.GetTicksUsec() - t0 >= budgetUs) break;
        }
        return _pendingNpcBodies.Count;
    }

    /// <summary>Construit et attache le corps du NPC
    /// (GLB/skin/échelle/animator ou capsule de repli) — le
    /// <paramref name="holder"/> porte déjà position/métas/label.</summary>
    private void AttachNpcBody(Node3D holder, SceneTable.NpcSpawn npc)
    {
        Node3D body;
        // Modèle : npclist (npcId → stem + texture variante, H4)
        // en priorité ; le « motion » de la scène en repli.
        var stem = npc.ModelCode;
        var skin = "";
        var npcScale = 0f;
        if (NpcModelTable.Resolve(npc.NpcId) is { } nd)
        {
            stem = nd.Stem;
            skin = nd.Texture;
            npcScale = nd.Scale;
        }
        if (stem.Length > 0 &&
            AssetCatalog.TryGetGlb($"monster/model/{stem}",
                out var glbPath) &&
            SkinnedProto(glbPath, skin, _skinnedProtos)
                is { } glb)
        {
            body = glb;
            // Coords natives Z-up → Y-up + 180°/Y (modèles « face
            // +Z », même convention que RemoteEntity — H9).
            body.RotationDegrees = new Vector3(-90f, 180f, 0f);
            if (npcScale > 0f && npcScale != 1f)
                body.Scale = Vector3.One * npcScale;
            // Anims si dispo — joue l'idle (évite la T-pose).
            if (Game.EntityAnimator.Attach(glb,
                    $"monster/animation/{stem}",
                    modelKey: glbPath) is { } an)
            {
                an.SetProcess(false); // PNJ de scène statique : l'AnimationPlayer tourne seul
                an.PlayLoop(an.IdleAnim);
            }
            foreach (var mi in body
                         .FindChildren("*", "MeshInstance3D", true, false)
                         .Cast<MeshInstance3D>())
                if (mi.VisibilityRangeEnd <= 0f)
                {
                    mi.VisibilityRangeEnd = 75f;
                    mi.VisibilityRangeEndMargin = 5f;
                    mi.VisibilityRangeFadeMode =
                        GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
                }
        }
        else
        {
            var capsule = new CapsuleMesh
            {
                Radius = 0.35f, Height = 1.7f,
            };
            capsule.Material = new StandardMaterial3D
            {
                AlbedoColor = new Godot.Color(0.3f, 0.8f, 0.4f),
            };
            body = new MeshInstance3D
            {
                Mesh = capsule,
                Position = new Vector3(0f, 0.85f, 0f),
            };
        }
        holder.AddChild(body);
        holder.AddChild(BlobShadow.Make(0.7f));
        // Le blob ne se projette pas sur le modèle (couche 2).
        BlobShadow.ExemptVisuals(holder);
    }

    /// <summary>Skin variante NPC (npclist col9) — délégué à
    /// <see cref="RemoteEntity.ApplySkinTexture"/>.</summary>
    private static void ApplyNpcSkin(Node3D glb, string texName) =>
        RemoteEntity.ApplySkinTexture(glb, texName);

    /// <summary>Instance du modèle <paramref name="glbPath"/> avec la
    /// skin <paramref name="skin"/> appliquée — mutualisée par couple
    /// (glb,skin) dans <paramref name="cache"/> : le premier NPC paie
    /// LoadGlbNode+skin, les suivants un simple Duplicate.</summary>
    private static Node3D? SkinnedProto(string glbPath, string skin,
        Dictionary<(string, string), Node3D?> cache)
    {
        if (skin.Length == 0)
            return AssetCatalog.LoadGlbNode(glbPath);
        if (!cache.TryGetValue((glbPath, skin), out var proto))
        {
            proto = AssetCatalog.LoadGlbNode(glbPath);
            if (proto != null) ApplyNpcSkin(proto, skin);
            cache[(glbPath, skin)] = proto;
        }
        return proto == null
            ? null
            : AssetCatalog.DuplicateSanitized(proto);
    }
}
