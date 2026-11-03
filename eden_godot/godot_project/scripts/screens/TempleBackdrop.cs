using Godot;

namespace EdenClient.Screens;

/// <summary>
/// Fond 3D « hall de création » partagé par les écrans de connexion
/// (login, channel, sélection, création) — reproduit le flux natif
/// qui place toute la pré-partie dans la map de création
/// <c>S218_room10b.glb</c> — preuve reverse : <c>FUN_00699a60(...,99)</c>
/// charge la scène ID 99 et <c>c_node.ini</c> mappe 99 → S218.
/// Select_pos=318,613,5 : sonde triangle S218 → sol natif
/// z=165,44 sous ce point.
///
/// Le GLB est en coords natives Z-up — comme <c>MapLoader</c>, il
/// est parenté sous un Node3D tourné de −90°/X
/// (<see cref="World.WorldConv.EdenRootEulerDeg"/>). La caméra et le
/// marqueur perso vivent en espace Godot (Y-up) et convertissent les
/// positions ini via <see cref="World.WorldConv.ToGodot"/>.
///
/// <b>Persistant</b> : une seule instance traverse les écrans
/// (<see cref="Attach"/> re-parente la racine — la scène temple
/// n'est chargée qu'une fois, pas de flash noir entre les écrans).
/// La transition vers <c>game.tscn</c> la détruit avec l'arbre —
/// <see cref="Shared"/> recrée alors l'instance.
/// </summary>
public sealed class TempleBackdrop
{
    // Triangle probe of S218_room10b.glb: walkable surface under [Select]/[Create]
    // is native Z=165.440, while startsetting.ini gives avatar heights
    // 5 / 39-42; the stage is shifted by this measured amount.
    private const float NativeStageFloorZ = 165.440f;

    /// <summary>Cadrages du temple (positions de
    /// <c>data/db/startsetting.ini</c>).</summary>
    public enum Shot
    {
        /// <summary>Vue large du hall (login, channel) — caméra
        /// [Select].</summary>
        Wide,
        /// <summary>Sélection de personnage — même caméra ; le
        /// marqueur porte le perso à Select_pos.</summary>
        Select,
        /// <summary>Création — caméra rapprochée [Create] par race
        /// (Human/Kuma/Mouse/Frog de l'ini).</summary>
        Create,
    }

    private static TempleBackdrop? _shared;

    /// <summary>Racine 3D persistante (Node3D — rend dans le
    /// viewport principal derrière les Controls).</summary>
    public Node3D Root { get; }
    /// <summary>Caméra fixe de l'écran courant.</summary>
    public Camera3D Camera { get; }
    /// <summary>Emplacement du personnage (espace Godot — posé par
    /// <see cref="ApplyShot"/>).</summary>
    public Node3D CharMarker { get; }
    /// <summary>La map de création est-elle chargée (vs env seul) ?</summary>
    public bool TempleLoaded { get; private set; }
    /// <summary>Le GLB du temple existe et peut être demandé. Ne pas
    /// confondre cette disponibilité avec <see cref="TempleLoaded"/> :
    /// le chargement se termine de façon asynchrone.</summary>
    public bool TempleAssetAvailable =>
        AssetCatalog.TryGetGlb("map/model/S218/S218_room10b", out _);

    private Node3D? _world;      // porteur Z-up du GLB
    private bool _glbRequested;
    private TextureRect? _placeholder;   // splash 2D en attente du GLB

    private TempleBackdrop()
    {
        Root = new Node3D { Name = "TempleBackdrop" };

        // Environnement intérieur : pas de ciel (le temple est
        // fermé) — fond sombre + ambiant doux chaud (teinté du
        // FogColor S218.ini 0xFF9846, éclairci).
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.05f, 0.045f, 0.06f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.55f, 0.48f),
            AmbientLightEnergy = 0.85f,
            TonemapMode = Godot.Environment.ToneMapper.Reinhardt,
            TonemapExposure = 1.15f,
        };
        var we = new WorldEnvironment
        {
            Name = "Env",
            Environment = env,
        };
        // SettingsApplier ré-applique fog/bloom/SSAO sur tous les
        // WorldEnvironment de la scène (réglages jeu). Le hall pré-jeu
        // a son propre éclairage natif : ne pas laisser ces toggles
        // recréer le voile gris/blanc vu sur les captures.
        we.SetMeta("pregame_env", true);
        Root.AddChild(we);

        // Soleil doux de face (l'intérieur du hall reste plat sans
        // directionnelle — constat sur les autres maps indoor).
        var key = new DirectionalLight3D
        {
            Name = "Key",
            RotationDegrees = new Vector3(-55f, -20f, 0f),
            LightEnergy = 1.1f,
            LightColor = new Color(1f, 0.92f, 0.78f),
            ShadowEnabled = false,
        };
        Root.AddChild(key);
        var rim = new DirectionalLight3D
        {
            Name = "Rim",
            RotationDegrees = new Vector3(-30f, 150f, 0f),
            LightEnergy = 0.45f,
            LightColor = new Color(0.6f, 0.68f, 0.95f),
            ShadowEnabled = false,
        };
        Root.AddChild(rim);

        Camera = new Camera3D { Name = "TempleCam", Fov = 30f };
        Camera.Current = true;
        Root.AddChild(Camera);
        CharMarker = new Node3D { Name = "CharMarker" };
        Root.AddChild(CharMarker);
    }

    /// <summary>Instance partagée (recréée si la précédente a été
    /// libérée avec l'arbre — changement de scène).</summary>
    public static TempleBackdrop Shared
    {
        get
        {
            if (_shared == null ||
                !GodotObject.IsInstanceValid(_shared.Root))
            {
                _shared = new TempleBackdrop();
                _shared.RequestGlb();
            }
            return _shared;
        }
    }

    /// <summary>Attache (ou rattache) le fond temple sous
    /// <paramref name="screen"/>, tout au fond de la pile, et applique
    /// le cadrage <paramref name="shot"/>.</summary>
    public static TempleBackdrop Attach(Control screen, Shot shot,
        int raceWireId = 1)
    {
        var tb = Shared;
        if (tb.Root.GetParent() != screen)
        {
            tb.Root.GetParent()?.RemoveChild(tb.Root);
            screen.AddChild(tb.Root);
            screen.MoveChild(tb.Root, 0);   // derrière l'UI
        }
        tb.ApplyShot(shot, raceWireId);
        tb.EnsurePlaceholder(screen);
        var probePrefix = OrbitProbePrefix();
        if (probePrefix != null &&
            tb.Root.GetNodeOrNull("__temple_orbit_probe__") == null)
            tb.Root.AddChild(new TempleOrbitProbe(tb, probePrefix));
        return tb;
    }

    private static string? OrbitProbePrefix()
    {
        foreach (var arg in OS.GetCmdlineArgs().Concat(
                     OS.GetCmdlineUserArgs()))
            if (arg.StartsWith("--temple-orbit-test=",
                    StringComparison.Ordinal))
                return arg["--temple-orbit-test=".Length..];
        return null;
    }

    /// <summary>Splash vanilla (loading_0 du client d'origine — pas
    /// le visuel Reforged) plein écran tant que le temple GLB n'est
    /// pas monté (~1,4 s de parse) : plus de vide noir au login. Le
    /// 3D Godot rend sous le canvas → le rect recouvre le fond vide,
    /// puis disparaît à <see cref="AttachMap"/>.</summary>
    private void EnsurePlaceholder(Control screen)
    {
        if (TempleLoaded ||
            (_placeholder != null &&
             GodotObject.IsInstanceValid(_placeholder)))
            return;
        var tex = AssetCatalog.GetTexture(
            "UI/loadingframe/Loading_0_vanilla.png");
        if (tex == null) return;
        _placeholder = new TextureRect
        {
            Name = "TemplePlaceholder",
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode =
                TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        _placeholder.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);
        screen.AddChild(_placeholder);
        screen.MoveChild(_placeholder, 0);   // sous tout le reste
    }

    /// <summary>Vide le marqueur perso (le preview de l'écran
    /// précédent ne doit pas suivre sur l'écran suivant).</summary>
    public void ClearMarker()
    {
        foreach (var c in CharMarker.GetChildren()) c.QueueFree();
    }

    /// <summary>Cadre caméra + marqueur pour <paramref name="shot"/>.
    /// Positions natives <c>startsetting.ini</c> (repli codé en dur
    /// si l'ini est absent — valeurs de la dist).</summary>
    public void ApplyShot(Shot shot, int raceWireId = 1)
    {
        var st = StartSetting.Instance;
        var (pos, _) = shot switch
        {
            Shot.Create => st.CreateShot(raceWireId),
            _ => (st.SelectPos, st.SelectCamera),
        };
        var gp = World.WorldConv.ToGodot(pos.X, pos.Y, pos.Z);
        // S218 : Select_camera ini (327.5,670,7 → Godot 327.5,7,-670)
        // tombe dans le massif sud (pont y≈20-28 à z -650..-720).
        // Cadrage validé par captures : sur le bord sud de l'estrade,
        // ~5 u au-dessus du sol, regard montant vers le haut de la
        // façade (escalier → balcon+lions → arches → coupole ajourée).
        // Le shot sélection reste un plan plus rapproché au niveau du
        // sol ; création = plan buste.
        var gc = shot switch
        {
            Shot.Wide => new Vector3(318f, 10f, -645f),
            // Create : la caméra ini [Create] (~0,5 u devant le
            // perso) cadre un buste pour les modèles natifs — nos
            // modèles convertis sont plus grands → même direction
            // (+z devant), distance validée par capture pour un
            // plan pieds-tête.
            Shot.Create => gp + new Vector3(-0.05f, 1.35f, 4.4f),
            _ => gp + new Vector3(0f, 3.0f, -13.5f),
        };
        // S218 : le sol natif mesuré est z=165,44 ; on décale le monde
        // pour que la hauteur ini Select_pos z=5 tombe dessus.
        if (_world != null)
            _world.Position = new Vector3(0f,
                pos.Z - NativeStageFloorZ, 0f);
        CharMarker.Position = gp;
        // Les modèles char convertis ont leur avant sur -Z Godot après
        // la rotation Z-up→Y-up. LookAt standard oriente -Z vers la
        // caméra ; useModelFront=true montrait leur dos.
        var flat = new Vector3(gc.X, gp.Y, gc.Z);
        if (!gp.IsEqualApprox(flat))
            CharMarker.LookAt(flat, Vector3.Up);
        Camera.Fov = shot switch
        {
            Shot.Select => 40f,
            Shot.Create => 46f,
            _ => 52f,
        };
        // Vise le haut du corps : +0.9 u (sélection, cadrage large) ;
        // création = plan buste → le point ini est déjà à hauteur
        // de torse (+~0.9 au-dessus des pieds).
        // Wide : vise le haut de la façade (portail/arches à ~y 30-45,
        // coupole au-dessus) — composition validée sur la ref channel.
        var target = shot == Shot.Wide
            ? new Vector3(318f, 48f, -565f)
            : gp + new Vector3(0f,
                shot == Shot.Create ? 0.95f : 1.0f, 0f);
        // QA : --temple-cam=x,y,z,tx,ty,tz force position+cible (espace
        // Godot) pour trouver un cadrage sans recompiler.
        foreach (var a in OS.GetCmdlineArgs().Concat(
                     OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--temple-cam=",
                    StringComparison.Ordinal))
            {
                var f = a["--temple-cam=".Length..].Split(',')
                    .Select(s => float.Parse(s,
                        System.Globalization.CultureInfo
                            .InvariantCulture)).ToArray();
                if (f.Length >= 6)
                {
                    gc = new Vector3(f[0], f[1], f[2]);
                    target = new Vector3(f[3], f[4], f[5]);
                }
            }
        Camera.Position = gc;
        Camera.LookAt(target, Vector3.Up);
        GD.Print($"[temple] shot={shot} floorOffsetY=" +
            $"{pos.Z - NativeStageFloorZ:F3} camera={gc} target={target}");
        // Halo chaud au-dessus du marqueur (accentue le piédestal).
        var glow = CharMarker.GetNodeOrNull<OmniLight3D>("Glow");
        if (glow == null)
        {
            glow = new OmniLight3D
            {
                Name = "Glow",
                LightColor = new Color(1f, 0.85f, 0.6f),
                LightEnergy = 1.2f,
                OmniRange = 12f,
                Position = new Vector3(0f, 3f, 0f),
            };
            CharMarker.AddChild(glow);
        }
    }

    /// <summary>Re-cadre le plan [Create] sur l'AABB réel de
    /// l'avatar — le cadrage fixe humain coupe les grandes races
    /// (Ursun remplit tout l'écran, tête hors champ). Garde la
    /// direction native (+z devant, léger −x) et la cible à
    /// mi-corps mesurée.</summary>
    public void FitCreateCamera(Node3D avatar)
    {
        if (!GodotObject.IsInstanceValid(avatar)) return;
        // --temple-cam QA : la caméra forcée gagne.
        foreach (var a in OS.GetCmdlineArgs().Concat(
                     OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--temple-cam=",
                    StringComparison.Ordinal))
                return;
        Aabb box = default;
        var found = false;
        foreach (var mi in avatar
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            if (!mi.Visible) continue;
            var m = mi.GlobalTransform * mi.GetAabb();
            box = found ? box.Merge(m) : m;
            found = true;
        }
        if (!found || box.Size.Y < 0.05f) return;
        var vp = Camera.GetViewport().GetVisibleRect().Size;
        var tan = Mathf.Tan(Mathf.DegToRad(Camera.Fov * 0.5f));
        // Le modèle remplit ~62% de la hauteur (4,4u FOV46 ↔
        // humain ~2,5u, cadrage validé par captures) ; la largeur
        // plafonne à 45% pour les poses bras écartés (Ursun).
        var dist = Mathf.Max(
            box.Size.Y / (0.62f * 2f * tan),
            box.Size.X / (0.45f * 2f * tan * (vp.X / vp.Y)));
        var basePos = CharMarker.GlobalPosition;
        var centerY = box.GetCenter().Y;
        Camera.Position = new Vector3(
            basePos.X - 0.05f, centerY, basePos.Z + dist);
        Camera.LookAt(new Vector3(basePos.X, centerY, basePos.Z),
            Vector3.Up);
    }

    /// <summary>Kick de préchargement du proto GLB (tâche de fond,
    /// cache <see cref="AssetCatalog.GetProto"/>) — appelé au boot
    /// pour que le premier écran n'attende pas le parse 16 Mo.</summary>
    public static void Preload()
    {
        if (AssetCatalog.TryGetGlb("map/model/S218/S218_room10b", out var p))
            _ = Task.Run(() => AssetCatalog.GetProto(p));
    }

    /// <summary>Lance le chargement du temple en tâche de fond
    /// (proto GLB extrait, généralement préchauffé au boot) ; la géométrie est ajoutée une fois prête,
    /// sous un porteur −90°/X (coords natives).</summary>
    private void RequestGlb()
    {
        if (_glbRequested) return;
        _glbRequested = true;
        var world = new Node3D
        {
            Name = "World",
            RotationDegrees = World.WorldConv.EdenRootEulerDeg,
        };
        _world = world;
        Root.AddChild(world);
        Root.MoveChild(world, 0);
        Perf.PerfTrace.Abs("temple : proto demandé");
        Task.Run(() =>
        {
            Node3D? proto = null;
            if (AssetCatalog.TryGetGlb("map/model/S218/S218_room10b",
                    out var path))
                proto = AssetCatalog.GetProto(path);
            Perf.PerfTrace.Abs("temple : proto chargé");
            // Retour thread principal via CallDeferred.
            var dup = proto;   // Node3D détaché — Duplicate thread-main
            Callable.From(() => AttachMap(dup)).CallDeferred();
        });
    }

    private void AttachMap(Node3D? proto)
    {
        if (_world == null || !GodotObject.IsInstanceValid(_world) ||
            TempleLoaded)
            return;
        if (proto == null)
        {
            GD.PrintErr("[temple] S218_room10b.glb introuvable/illisible — " +
                "fond env seul");
            return;
        }
        if (proto.Duplicate() is Node3D map)
        {
            map.Name = "TempleMap";
            _world.AddChild(map);
            HideRoomShells(map);
            // QA : --temple-hide=n1,n2 masque les meshes dont le nom
            // contient un des motifs (recherche du bloqueur de vue).
            foreach (var a in OS.GetCmdlineArgs().Concat(
                         OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--temple-hide=",
                        StringComparison.Ordinal))
                {
                    var pats = a["--temple-hide=".Length..]
                        .Split(',', StringSplitOptions.TrimEntries);
                    var hidden = 0;
                    foreach (var mi in map.FindChildren("*",
                                 "MeshInstance3D", true, false)
                             .Cast<MeshInstance3D>())
                        foreach (var p in pats)
                            if (mi.Name.ToString().Contains(p,
                                    StringComparison.OrdinalIgnoreCase) ||
                                MeshUsesTexture(mi, p))
                            { mi.Visible = false; hidden++; break; }
                    GD.Print($"[temple-hide] {pats.Length} motifs → " +
                        $"{hidden} meshes masqués");
                }
            // Dôme de ciel natif S218 (SkyDay=B401 dans S218.ini) —
            // visible à travers la coupole ajourée, comme Reforged.
            if (World.SkyDome.Create("B401") is { } dome)
                _world.AddChild(dome);
            // Portail violet de l'arche centrale — la capture Reforged
            // le montre brillant (texture d'effet native S2182101).
            // Espace Godot (Root) : _world est le porteur −90°/X.
            var pmat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                EmissionEnergyMultiplier = 2f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                // Capture ancien Eden : la grande arche laisse passer une
                // lumière blanche chaude (pas un voile violet).
                AlbedoColor = new Color(1f, 0.96f, 0.86f),
                Emission = new Color(1f, 0.95f, 0.82f),
            };
            // Halo radial doux (la texture de fumée S2182101 donnait
            // une tache laiteuse peu crédible) : cœur blanc chaud.
            var gr = new Gradient();
            gr.SetColor(0, new Color(1f, 1f, 0.95f, 0.95f));
            gr.SetColor(1, new Color(1f, 0.95f, 0.8f, 0f));
            var ptex = new GradientTexture2D
            {
                Gradient = gr,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 128, Height = 128,
            };
            pmat.AlbedoTexture = ptex;
            pmat.EmissionTexture = ptex;
            var portal = new MeshInstance3D
            {
                Name = "PortalGlow",
                Mesh = new QuadMesh { Size = new Vector2(7f, 9f) },
                Position = new Vector3(318f, 17f, -582f),
                RotationDegrees = new Vector3(0f, 180f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = pmat,
                Visible = false,   // le halo ovale flottait sur l'escalier
            };
            Root.AddChild(portal);
            Root.AddChild(new OmniLight3D
            {
                Name = "PortalLight",
                LightColor = new Color(1f, 0.9f, 0.75f),
                LightEnergy = 1.4f,
                OmniRange = 30f,
                Position = new Vector3(318f, 24f, -570f),
            });
            TempleLoaded = true;
            // Fondu court plutôt qu'un QueueFree sec : le remplacement
            // splash 2D → temple 3D était un saut visible ~1,4 s après
            // l'ouverture du login (P2.1).
            if (_placeholder != null)
            {
                var ph = _placeholder;
                _placeholder = null;
                if (GodotObject.IsInstanceValid(ph))
                {
                    var tw = ph.CreateTween();
                    tw.TweenProperty(ph, "modulate:a", 0f, 0.45f);
                    tw.TweenCallback(Callable.From(ph.QueueFree));
                }
            }
            Perf.PerfTrace.Abs("temple : affiché");
            GD.Print("[temple] S218 monté en fond d'écrans");
        }
    }

    /// <summary>Enveloppes de salles natives <c>*_NDLWL</c> : plans
    /// internes de pièce qui bloquent la caméra Select hors de la salle
    /// (raycast prouvé : DummyRoom08_NDLWL à y=650 couvre toute la vue).
    /// Elles sont masquées, les murs/props intérieurs restent.</summary>
    private static void HideRoomShells(Node root)
    {
        var hidden = 0;
        foreach (var n in root.FindChildren("*", "Node3D", true, false)
                     .Cast<Node3D>())
            if (n.Name.ToString().Contains("_NDLWL",
                    StringComparison.OrdinalIgnoreCase))
            {
                n.Visible = false;
                hidden++;
            }
        // Le proto a déjà passé ToonMaterializer : sur S218, la plupart
        // des murs/props réels s'appellent « Editable Poly » et n'ont
        // pas de texture — ils ont été masqués comme helpers. Le temple
        // n'a pas besoin de colliders : on les réaffiche hors coques
        // NDLWL (les parents cachés les garderont invisibles).
        var restored = 0;
        var sightBlockers = 0;
        foreach (var mi in root.FindChildren("*", "MeshInstance3D",
                         true, false).Cast<MeshInstance3D>())
        {
            if (mi.HasMeta("collision_helper"))
            {
                mi.Visible = true;
                restored++;
            }
            // Plane05/Object349 portent les textures natives du hall
            // (s218051/52 : tapis/escalier) — ce ne sont PAS des
            // bloqueurs. Le gris venait de leur masquage.
        }
        GD.Print($"[temple] room shells masqués={hidden} " +
                 $"meshes restaurés={restored} " +
                 $"bloquants Select={sightBlockers}");
    }

    /// <summary>Le mesh utilise-t-il une texture dont le nom de
    /// ressource contient <paramref name="pat"/> (insensible casse) ?
    /// — permet de masquer par motif de texture.</summary>
    private static bool MeshUsesTexture(MeshInstance3D mi, string pat)
    {
        if (mi.Mesh == null) return false;
        for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = mi.GetSurfaceOverrideMaterial(i)
                    ?? mi.Mesh.SurfaceGetMaterial(i);
            if (m is StandardMaterial3D sm &&
                sm.AlbedoTexture?.ResourceName?
                    .Contains(pat, StringComparison.OrdinalIgnoreCase)
                    == true)
                return true;
        }
        return false;
    }

    private static bool HasAncestorNamed(Node node, string name)
    {
        for (var n = node.GetParent(); n != null; n = n.GetParent())
            if (n.Name == name) return true;
        return false;
    }

    // ----------------------------------------------------------------

    /// <summary>Lecteur minimal de <c>startsetting.ini</c> —
    /// [Select] Select_pos/Select_camera + [Create]
    /// &lt;Race&gt;_pos/_camera/_rotate. Les coordonnées restent en
    /// espace natif Z-up (x,y sol, z hauteur).</summary>
    private sealed class StartSetting
    {
        public static readonly StartSetting Instance = Load();

        public System.Numerics.Vector3 SelectPos =
            new(318f, 613f, 5f);
        public System.Numerics.Vector3 SelectCamera =
            new(327.5f, 670f, 7f);
        private readonly Dictionary<string, System.Numerics.Vector3>
            _create = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>(pos, camera) du shot création pour la race wire
        /// (1 Human, 2 Zumi→Mouse, 3 Anuran→Frog, 4 Ursun→Kuma,
        /// 5 Halfkin→Human par défaut — l'ini n'a pas de section
        /// Halfkin).</summary>
        public (System.Numerics.Vector3 Pos,
                System.Numerics.Vector3 Cam) CreateShot(int race)
        {
            var key = race switch
            {
                2 => "Mouse", 3 => "Frog", 4 => "Kuma",
                _ => "Human",
            };
            if (!_create.TryGetValue(key + "_pos", out var pos))
                pos = new System.Numerics.Vector3(
                    317.69f, 613.5f, 41.93f);
            if (!_create.TryGetValue(key + "_camera", out var cam))
                cam = new System.Numerics.Vector3(
                    317.64f, 613.0f, 42.9f);
            return (pos, cam);
        }

        private static StartSetting Load()
        {
            var st = new StartSetting();
            var path = AssetCatalog.ResolveRawAsset(
                "data/db/startsetting.ini");
            if (path == null) return st;
            try
            {
                string section = "";
                foreach (var raw in Vfs.EdenVfs.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith('['))
                    {
                        section = line.Trim('[', ']');
                        continue;
                    }
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line[..eq].Trim();
                    var v = ParseVec(line[(eq + 1)..]);
                    if (v == null) continue;
                    if (section.Equals("Select",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (key == "Select_pos") st.SelectPos = v.Value;
                        else if (key == "Select_camera")
                            st.SelectCamera = v.Value;
                    }
                    else if (section.Equals("Create",
                                 StringComparison.OrdinalIgnoreCase))
                        st._create[key] = v.Value;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[temple] startsetting.ini : {e.Message}");
            }
            return st;
        }

        private static System.Numerics.Vector3? ParseVec(string s)
        {
            var parts = s.Split(',');
            if (parts.Length < 3) return null;
            var v = new float[3];
            for (var i = 0; i < 3; i++)
            {
                var p = parts[i].Trim().TrimEnd('f', 'F');
                if (!float.TryParse(p,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo
                            .InvariantCulture, out v[i]))
                    return null;
            }
            return new System.Numerics.Vector3(v[0], v[1], v[2]);
        }
    }
}
