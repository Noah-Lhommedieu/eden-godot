using Godot;
using EdenClient.World;

namespace EdenClient.Debugging;

/// <summary>Scène de test visuel terrain/environnement —
/// charge une map via <see cref="MapLoader"/> et place une caméra
/// orbitale au spawn. Args : --map=S009 --cam=orbit|top|fixed.</summary>
public partial class VisTest : Node3D
{
    private static readonly System.Globalization.CultureInfo CI =
        System.Globalization.CultureInfo.InvariantCulture;

    private MapLoader? _map;
    private Camera3D? _cam;
    private Vector3 _pivot;
    private float _yaw = 0.6f;
    // Positive elevation puts the orbital camera above the terrain.
    // Negative pitch from below made every ground texture appear absent.
    private float _pitch = 0.45f;
    private float _dist = 18f;
    private bool _orbitDrag;
    private bool _auto;
    private bool _hideObjects;
    private bool _hideTerrain;
    private bool _hideWater;
    private bool _hideGrass;
    private bool _noFog;
    private bool _noSky;
    private readonly List<string> _mobs = new();
    private readonly List<string> _chars = new();
    // --weapons=W134,W234 : planche d'armes (--wbody=code corps).
    private readonly List<string> _weapons = new();
    private int _wbody = 1;
    private int _rideKey;
    private string? _sbProbe;
    private bool _pickTest;
    private bool _appTest;
    private readonly List<string> _treasures = new();
    private readonly List<RemoteEntity> _spawned = new();
    private int _shotFrames = -1;
    private bool _shotParsed;
    private int _frames;
    private string _shotPath = "/tmp/vistest.png";
    // --char-cache[=lettre] : proto Prewarm en worker puis
    // GetOrBuild sur le main (preuve P1.3 <100 ms à proto chaud).
    private string? _charCache;
    private Task? _charCacheTask;
    private byte[]? _charCacheApp;
    // --loading-test[=frac] : monte l'overlay LoadingScreen à la
    // fraction voulue (preuve P1.4 : mascottes animées, câlin à 1.0).
    private float? _loadingTest;
    // --blob : ajoute l'ombre blob (Decal, chemin live EntityRegistry)
    // sous les entités spawnées — diagnostic « toiles noires » M609.
    private bool _blob;

    public override void _Ready()
    {
        EdenClient.Data.TextResolver.ResolveFile ??= rel =>
            AssetCatalog.ResolveRawAsset(rel) ?? DataPaths.ResolveFile(rel);
        var mapCode = "S009";
        float? px = null, py = null, pz = null;
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--map=")) mapCode = a[6..];
            else if (a == "--blob") _blob = true;
            else if (a == "--auto") _auto = true;
            else if (a.StartsWith("--yaw="))
                _yaw = float.Parse(a[6..], CI);
            else if (a.StartsWith("--pitch="))
                _pitch = float.Parse(a[8..], CI);
            else if (a.StartsWith("--dist="))
                _dist = float.Parse(a[7..], CI);
            else if (a.StartsWith("--amb="))
                MapLoader.AmbientEnergy = float.Parse(a[6..], CI);
            else if (a.StartsWith("--sun="))
                MapLoader.SunEnergy = float.Parse(a[6..], CI);
            else if (a.StartsWith("--pivz="))
                pz = float.Parse(a[7..], CI);
            else if (a.StartsWith("--pivot="))
            {
                var p = a[8..].Split(',');
                if (p.Length >= 2)
                {
                    px = float.Parse(p[0], CI);
                    py = float.Parse(p[1], CI);
                }
            }
            else if (a.StartsWith("--mob=")) _mobs.Add(a[6..]);
            else if (a.StartsWith("--char=")) _chars.Add(a[7..]);
            else if (a.StartsWith("--ride=") &&
                     int.TryParse(a[7..], out var rk)) _rideKey = rk;
            else if (a == "--chars") _chars.Add("b");
            else if (a.StartsWith("--weapons="))
                _weapons.AddRange(a[10..].Split(','));
            else if (a.StartsWith("--wbody="))
                _wbody = int.Parse(a[8..]);
            else if (a.StartsWith("--sb=")) _sbProbe = a[5..];
            else if (a == "--pick-test") _pickTest = true;
            else if (a == "--appearance-test") _appTest = true;
            else if (a.StartsWith("--treasure="))
                _treasures.AddRange(a[11..].Split(','));
            else if (a == "--hide-objects") _hideObjects = true;
            else if (a == "--hide-terrain") _hideTerrain = true;
            else if (a == "--no-fog") _noFog = true;
            else if (a == "--no-sky") _noSky = true;
            else if (a == "--hide-water") _hideWater = true;
            else if (a == "--hide-grass") _hideGrass = true;
            else if (a.StartsWith("--char-cache"))
                _charCache = a.Contains('=')
                    ? a[(a.IndexOf('=') + 1)..] : "b";
            else if (a.StartsWith("--loading-test"))
                _loadingTest = a.Contains('=')
                    ? float.Parse(a[(a.IndexOf('=') + 1)..], CI)
                    : 0.55f;
        }
        _map = new MapLoader();
        var mapRoot = _map.LoadMap(mapCode);
        AddChild(mapRoot);
        if (_hideObjects &&
            mapRoot.FindChild("Objects", true, false) is Node3D objs)
        {
            objs.Visible = false;
            GD.Print("[vistest] objects hidden");
        }
        if (_hideTerrain &&
            mapRoot.FindChild("Terrain", true, false) is Node3D terr)
        {
            terr.Visible = false;
            GD.Print("[vistest] terrain hidden");
        }
        if (_hideWater &&
            mapRoot.FindChild("Terrain", true, false) is Node3D terr2)
        {
            var hidden = 0;
            foreach (var mi in terr2.FindChildren("*_water",
                         "MeshInstance3D", true, false)
                         .Cast<MeshInstance3D>())
            {
                mi.Visible = false;
                hidden++;
            }
            GD.Print($"[vistest] water hidden={hidden}");
        }
        if (_hideGrass &&
            mapRoot.FindChild("Grass", true, false) is Node3D grass)
        {
            grass.Visible = false;
            GD.Print("[vistest] grass hidden");
        }
        if (_noFog &&
            mapRoot.FindChild("FogCeiling", true, false) is { } fog)
            fog.QueueFree(); // il se réactive tout seul — on le supprime
        if (_noFog &&
            mapRoot.FindChild("Environment", false, false)
                is WorldEnvironment { Environment: { } mapEnv })
            mapEnv.FogEnabled = false;
        if (_noSky)
            foreach (var n in mapRoot.FindChildren("Sky_*", "Node3D",
                         true, false).Cast<Node3D>())
                n.Visible = false;
        var sp = _map.GetSpawnPoint();
        var ex = px ?? sp.X;
        var ey = py ?? sp.Y;
        var z = pz ??
            (_map.TryGetEdenHeight(ex, ey, out var h) ? h : sp.Z);
        _pivot = WorldConv.ToGodot(ex, ey, z) +
            new Vector3(0f, 1.6f, 0f); // look-at torse, pas pieds —
                                       // sinon la caméra plonge sous
                                       // le terrain en pitch négatif
        _cam = new Camera3D { Current = true, Far = 6000f };
        AddChild(_cam);
        UpdateCam();
        GD.Print($"[vistest] map={mapCode} pivot={_pivot} ground={z}");
        // Diagnostic surfaces géantes résiduelles : liste les meshes
        // encore visibles hors Terrain/Objects quand les filtres
        // --hide-* sont actifs (les overlays d'entités masquaient le
        // terrain dans les captures précédentes).
        if (_hideTerrain || _hideObjects || _hideWater || _hideGrass)
        {
            foreach (var mi in mapRoot.FindChildren("*",
                         "MeshInstance3D", true, false)
                         .Cast<MeshInstance3D>())
            {
                if (!mi.Visible || mi.Mesh == null) continue;
                var aabb = mi.GetAabb();
                if (aabb.Size.LengthSquared() < 100f) continue;
                var chain = new List<string>();
                for (Node? n = mi; n != mapRoot && n != null;
                     n = n.GetParent())
                    chain.Insert(0, n.Name.ToString());
                GD.Print($"[vistest] BIG {string.Join('/', chain)} " +
                         $"size={aabb.Size}");
            }
        }
        SpawnRequested(ex, ey, z);
        if (_appTest) AppearanceTest();
        if (_charCache != null)
        {
            var code = _charCache.Length > 0 ? _charCache[0] : 'b';
            var bodyCode = code switch
            {
                'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6,
                'l' => 8, 'c' => 9, _ => 0,
            };
            _charCacheApp = Protocol.AppearanceCodec.Encode(
                new Protocol.AppearanceCodec.Appearance(
                    BodyCode: bodyCode, HairStyle: 5, FaceStyle: 0,
                    HairColor: 0, SkinColor: 0, EyesColor: 0,
                    EyesColor2: 0));
            _charCacheTask = Task.Run(() =>
                Game.CharBodyCache.Prewarm(_charCacheApp));
            GD.Print($"[vistest] char-cache prewarm lancé ({code})");
        }
        if (_loadingTest.HasValue)
        {
            // add_child sur la racine impossible pendant _Ready —
            // repousser le montage à la première frame.
            var frac = _loadingTest.Value;
            CallDeferred(nameof(MountLoadingTest), frac);
        }
    }

    private void MountLoadingTest(float frac)
    {
        Screens.LoadingScreen.EnsureOverlay(GetTree(), 1);
        Screens.LoadingScreen.Report(frac, "chargement test…");
    }

    /// <summary>--appearance-test : matrice apparence — aligne des
    /// entités couvrant les 7 corps × variantes visage/peau/cheveux
    /// × costumes 4pequip (worn simulé : codes réels de la table,
    /// itemIds fictifs — <paramref name="..."/> lookup local) et
    /// dump <c>[appmat]</c> ce que CharVisual a résolu pour chaque
    /// combinaison : meshes visibles (cheveux/tenue/peau), suffixes
    /// tenue résolus vs présents dans le GLB, texture visage
    /// trouvée/absente, attachements bone. Les écarts « voulu mais
    /// absent » sont des limites de données, pas des bugs.</summary>
    private void AppearanceTest()
    {
        // Cadrage frontal adapté à la rangée (deux lignes de 6).
        if (_weapons.Count == 0)
        {
            _dist = 11f;
            _pitch = 0.18f;
        }
        UpdateCam();

        var itemById =
            new Dictionary<int, (string Code, string ModelCode)>();
        var nextId = 900000;
        (string Code, string ModelCode)? Lookup(int id) =>
            itemById.TryGetValue(id, out var c) ? c : null;
        Dictionary<int, int>? Worn(params (int Loc, string Code)[] it)
        {
            if (it.Length == 0) return null;
            var w = new Dictionary<int, int>();
            foreach (var (loc, code) in it)
            {
                itemById[nextId] = (code, code);
                w[loc] = nextId++;
            }
            return w;
        }

        // (tag, bodyCode, hair, face, hairColor, skin, eyes, worn).
        // Codes 4pequip : A07201→#_208 tenue, A02103→casque tête
        // B1102/G1102, A06203→#_207, W13407→arme main droite.
        var combos = new (string Tag, int Body, int Hair, int Face,
            int Hc, int Sc, int Ec,
            (int Loc, string Code)[] It)[]
        {
            ("b-base",  1, 2, 1, 3, 1, 0, System.Array.Empty<(int, string)>()),
            ("b-alt",   1, 8, 3, 9, 4, 5, System.Array.Empty<(int, string)>()),
            ("b-equip", 1, 5, 0, 0, 0, 0,
                new[] { (2, "A07201"), (0, "A02103"), (7, "W13407") }),
            ("g-base",  0, 4, 2, 5, 2, 1, System.Array.Empty<(int, string)>()),
            ("g-alt",   0, 9, 4, 11, 5, 6, System.Array.Empty<(int, string)>()),
            ("g-equip", 0, 3, 1, 7, 2, 0,
                new[] { (2, "A06203"), (0, "A07101"), (7, "W13407") }),
            ("m-base",  2, 3, 1, 4, 3, 0, System.Array.Empty<(int, string)>()),
            ("f-base",  4, 2, 0, 2, 4, 0, System.Array.Empty<(int, string)>()),
            ("k-base",  6, 1, 0, 1, 5, 0, System.Array.Empty<(int, string)>()),
            ("k-noface",6, 1, 11, 1, 5, 0, System.Array.Empty<(int, string)>()),
            ("l-base",  8, 6, 2, 6, 1, 3, System.Array.Empty<(int, string)>()),
            ("c-base",  9, 5, 3, 7, 2, 4, System.Array.Empty<(int, string)>()),
        };

        EdenClient.Data.TextResolver.ResolveFile ??= rel =>
            AssetCatalog.ResolveRawAsset(rel) ?? DataPaths.ResolveFile(rel);
        // --skin-sheet=SEED [--skin-body=N] : 12 apparences ALÉATOIRES du même corps (coiffure,
        // visage, couleurs cheveux/peau/yeux) pour vérifier que chaque choix est fonctionnel.
        var sheet = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--skin-sheet="));
        if (sheet != null)
        {
            var rng = new Random(int.Parse(sheet[13..]));
            var bodyArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--skin-body="));
            var bc = bodyArg != null ? int.Parse(bodyArg[12..]) : 1;
            var letterS = Game.CharModelSpec.BodyStem(Protocol.AppearanceCodec.Encode(
                new Protocol.AppearanceCodec.Appearance(bc, 0, 0, 0, 0, 0, 0)));
            var nFace = Enumerable.Range(1, 30).Count(i => AssetCatalog.GetTexture(
                $"char/texture/{letterS.ToUpperInvariant()}FACES{i:D2}B.dds") != null);
            combos = Enumerable.Range(0, 6).Select(i => ($"{letterS}{i}", bc,
                rng.Next(0, 10), rng.Next(0, Math.Max(1, nFace)),
                rng.Next(0, 24), rng.Next(0, 12), rng.Next(0, 24),
                System.Array.Empty<(int, string)>())).ToArray();
            _dist = 6.2f; _pitch = 0.05f; UpdateCam();
            GD.Print($"[skin-sheet] corps={bc}({letterS}) visages={nFace}");
        }
        if (_weapons.Count > 0)
            combos = _weapons.Select(w => ($"{w}", _wbody, 5, 0, 0, 0, 0,
                new[] { (7, w + "01") })).ToArray();
        var missFace = 0;
        var slot = 0;
        foreach (var c in combos)
        {
            var worn = Worn(c.It);
            var app = Protocol.AppearanceCodec.Encode(
                new Protocol.AppearanceCodec.Appearance(
                    BodyCode: c.Body, HairStyle: c.Hair,
                    FaceStyle: c.Face, HairColor: c.Hc,
                    SkinColor: c.Sc, EyesColor: c.Ec, EyesColor2: c.Ec ^ 7));
            var letter = Game.CharModelSpec.BodyStem(app);
            var wanted = Game.CharVisual.OutfitSuffixes(letter, worn,
                Lookup);
            // --skin-classes[=base] : la i-ème figurine porte la tenue de base de la classe base+i
            // (--skin-style=N : index de garde-robe).
            var skc = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--skin-classes"));
            var clsIdx = skc == null ? 0 : (skc.Contains('=') ? int.Parse(skc[(skc.IndexOf('=') + 1)..]) : 1) + slot;
            var sty = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--skin-style="));
            var body = Game.CharVisual.BuildBody(app, worn, Lookup, classId: clsIdx,
                style: sty != null ? int.Parse(sty[13..]) : 0);
            if (body != null && worn != null)
                Game.CharVisual.ApplyEquipment(body, letter, worn,
                    Lookup);
            DumpLook(c, letter, body, wanted,
                worn == null ? "-" :
                    string.Join('+', c.It.Select(t => t.Code)),
                ref missFace);

            var e = new RemoteEntity
            {
                Handle = 0x88C80000u + (uint)slot,
                Kind = 0,
            };
            e.Interp = new Player.NetworkInterpolator { Name = "Interp" };
            e.AddChild(e.Interp);
            e.SetVisual(body, isPlayer: true);
            e.LabelProminent = true;
            e.SetLabel(c.Tag);
            var col = slot % 6;
            var row = slot / 6;
            e.Position = _pivot + new Vector3(
                col * 1.5f - 3.75f, 0f, -1.2f - row * 2.2f);
            SnapGround(e);
            AddChild(e);
            _spawned.Add(e);
            if (body != null) Game.CharVisual.DumpVisible(body, c.Tag);
            slot++;
        }
        GD.Print($"[appmat] résumé combos={combos.Length} " +
            $"face_tex_absente={missFace}");
    }

    /// <summary>Dump de ce que BuildBody/ApplyEquipment ont résolu :
    /// meshes visibles par catégorie, suffixes tenue voulus absents
    /// du GLB, texture visage effective (repli comme
    /// ApplyFaceVariant).</summary>
    private void DumpLook(
        (string Tag, int Body, int Hair, int Face, int Hc, int Sc,
            int Ec, (int Loc, string Code)[] It) c,
        string letter, Node3D? body, ISet<string> wanted,
        string wornStr, ref int missFace)
    {
        var up = letter.ToUpperInvariant();
        var variant = (char)('A' + Math.Clamp(c.Sc, 0, 5));
        var style = (c.Face + 1).ToString("D2");
        string? faceTex = null;
        foreach (var stem in new[]
        {
            $"char/texture/{up}FACES{style}{variant}",
            $"char/texture/{up}FACES{style}",
            $"char/texture/{up}FACES{style}A",
        })
            if (AssetCatalog.ResolveAssetFile(
                    $"textures/{stem}.png") != null)
            { faceTex = Path.GetFileName(stem); break; }
        if (faceTex == null)
        {
            missFace++;
            faceTex = $"ABSENT({up}FACES{style}{variant})";
        }

        if (body == null)
        {
            GD.PrintErr($"[appmat] {c.Tag} body={letter} " +
                $"GLB_CORPS_ABSENT");
            return;
        }
        var hair = new List<string>();
        var outfits = new List<string>();
        var skins = new List<string>();
        var presentSuffix = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var face = 0;
        var attach = 0;
        foreach (var mi in body
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            var n = mi.Name.ToString();
            if (n.Length > 3 && (n[0] == '#' || n[0] == 'S') &&
                n[1].ToString().Equals(up,
                    StringComparison.OrdinalIgnoreCase))
                presentSuffix.Add(n[2..]);
            if (!mi.Visible) continue;
            if (n.EndsWith("FACE", StringComparison.OrdinalIgnoreCase))
                face++;
            else if (n.EndsWith("HAIR", StringComparison.OrdinalIgnoreCase))
                hair.Add(n);          // convention M : « #MHAIR » unique
            else if (n.Length >= 4 && n[0] == '#' && n[2] == '1' &&
                     char.IsDigit(n[3]))
                hair.Add(n);
            else if (n.Length >= 4 && n[0] == '#' && char.IsDigit(n[2]))
                outfits.Add(n);
            else if (n.Length >= 4 && n[0] == 'S' &&
                     char.IsDigit(n[2]))
                skins.Add(n);
        }
        foreach (var n in body.FindChildren("__equip_*", "",
                     true, false))
            attach++;
        var missing = wanted.Where(s => !presentSuffix.Contains(s))
            .ToList();
        GD.Print(
            $"[appmat] {c.Tag} body={letter}({c.Body}) " +
            $"hair={c.Hair} face={c.Face} hc={c.Hc} sc={c.Sc} " +
            $"ec={c.Ec} worn={wornStr} → " +
            $"hair_mesh=[{string.Join(',', hair)}] " +
            $"tenues=[{string.Join(',', outfits)}] " +
            $"peaux={skins.Count} face={face} attach={attach} " +
            $"face_tex={faceTex} " +
            $"voulu_absent=[{string.Join(',', missing)}]");
    }

    /// <summary>Spawn audit entités : <c>--mob=&lt;stem&gt;</c> (mob
    /// GLB via RemoteEntity + anims + label, même chemin que
    /// <see cref="EntityRegistry.HandleSeeMob"/>) et
    /// <c>--char=&lt;lettre&gt;</c> (corps perso via
    /// <see cref="CharVisual.BuildBody"/>). Alignés autour du pivot.</summary>
    private void SpawnRequested(float ex, float ey, float ez)
    {
        var slot = 0;
        foreach (var stem in _mobs)
        {
            var e = new RemoteEntity
            {
                Handle = 0xFFFFA000u + (uint)slot,
                EntityId = 25,
                Kind = 12,
            };
            e.Interp = new Player.NetworkInterpolator { Name = "Interp" };
            e.AddChild(e.Interp);
            Node3D? glb = null;
            if (AssetCatalog.TryGetGlb($"monster/model/{stem}", out var p))
                glb = AssetCatalog.LoadGlbNode(p);
            e.SetVisual(glb, isPlayer: false, animStem: stem);
            e.Hp = 80; e.MaxHp = 100;
            e.SetLabel($"Lv25 {stem.ToUpperInvariant()}");
            e.Position = _pivot + new Vector3(slot * 2.4f - 1.2f, 0f, -1.5f);
            SnapGround(e);
            if (_blob)
            {
                var shadow = World.BlobShadow.Make(0.7f);
                shadow.Position = new Vector3(0f, 0.6f, 0f);
                e.AddChild(shadow);
                // Comportement live : RelayoutOverhead exclut les
                // meshes de l'entité des projections du blob.
                World.BlobShadow.ExemptVisuals(e);
            }
            AddChild(e);
            _spawned.Add(e);
            GD.Print($"[vistest] mob {stem} → {(glb != null ? "glb" : "placeholder")}");
            slot++;
        }
        foreach (var fam in _treasures)
        {
            // --treasure=<famille[,famille…]> : reproduit le chemin live
            // HandleSeeTreasure (entité kind 0 + blob à y=0,6 +
            // SetTreasureVisual + label) — diagnostic « panneau noir ».
            var family = int.TryParse(fam, out var fn)
                ? $"G{Math.Clamp(fn, 1, 10):00000}"
                : EdenClient.Data.TreasureRules.Family(fam);
            var e = new RemoteEntity
            {
                Handle = 0x88E60000u + (uint)slot,
                Kind = 0,
            };
            e.Interp = new Player.NetworkInterpolator { Name = "Interp" };
            e.AddChild(e.Interp);
            var shadow = World.BlobShadow.Make(0.7f);
            shadow.Position = new Vector3(0f, 0.6f, 0f);
            e.AddChild(shadow);
            Node3D? glb = null;
            if (AssetCatalog.TryGetGlb($"item/model/{family}", out var gp))
                glb = AssetCatalog.LoadGlbNode(gp);
            e.SetTreasureVisual(glb, new Color(0.9f, 0.8f, 0.3f), family);
            e.SetLabel($"treasure#{family}");
            e.Position = _pivot + new Vector3(slot * 2.4f - 1.2f, 0f, -1.5f);
            SnapGround(e);
            AddChild(e);
            _spawned.Add(e);
            GD.Print($"[vistest] treasure {family} → {(glb != null ? "nif" : "capsule")}");
            slot++;
        }
        foreach (var letter in _chars)
        {
            var e = new RemoteEntity
            {
                Handle = 0x88C80000u + (uint)slot,
                Kind = 0,
            };
            e.Interp = new Player.NetworkInterpolator { Name = "Interp" };
            e.AddChild(e.Interp);
            var code = letter.Length > 0 ? letter[0] : 'b';
            var bodyCode = code switch
            {
                'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6,
                'l' => 8, 'c' => 9, _ => 0,
            };
            var app = Protocol.AppearanceCodec.Encode(
                new Protocol.AppearanceCodec.Appearance(
                    BodyCode: bodyCode, HairStyle: 5, FaceStyle: 0,
                    HairColor: 0, SkinColor: 0, EyesColor: 0,
                    EyesColor2: 0));
            var body = Game.CharVisual.BuildBody(app);
            if (body != null && _sbProbe != null)
            {
                // --sb=<id> : probe — n'affiche que le mesh SB<id>
                // (audit visuel de la convention SB↔#B).
                foreach (var mi in body
                             .FindChildren("*", "MeshInstance3D", true, false)
                             .Cast<MeshInstance3D>())
                {
                    var n = mi.Name.ToString();
                    mi.Visible = n == $"SB{_sbProbe}" ||
                        n.EndsWith("FACE",
                            StringComparison.OrdinalIgnoreCase);
                }
            }
            e.SetVisual(body, isPlayer: true);
            if (_rideKey > 0) e.SetRide(_rideKey);
            e.LabelProminent = true;
            e.SetLabel($"Lv1 Char_{code}");
            e.Position = _pivot + new Vector3(slot * 2.4f - 1.2f, 0f, -1.5f);
            SnapGround(e);
            AddChild(e);
            _spawned.Add(e);
            GD.Print($"[vistest] char {code} → {(body != null ? "glb" : "placeholder")}");
            slot++;
        }
    }

    /// <summary>Snappe l'entité sur la hauteur du terrain au point
    /// (godot x,z → eden x,y).</summary>
    private void SnapGround(Node3D e)
    {
        var ed = WorldConv.ToEden(e.Position);
        if (_map != null && _map.TryGetEdenHeight(ed.X, ed.Y, out var h))
            e.Position = new Vector3(e.Position.X,
                WorldConv.ToGodot(ed.X, ed.Y, h).Y, e.Position.Z);
    }

    /// <summary>--pick-test : raycast caméra→torse sur chaque entité
    /// spawnée ; vérifie que PickArea est réellement intersectée.</summary>
    private void PickTest()
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null || _cam == null) return;
        var ok = 0;
        foreach (var e in _spawned)
        {
            var target = e.GlobalPosition + new Vector3(0f, 1.1f, 0f);
            var q = PhysicsRayQueryParameters3D.Create(
                _cam.GlobalPosition, target, RemoteEntity.PickLayerMask);
            q.CollideWithAreas = true;
            q.CollideWithBodies = false;
            var hit = space.IntersectRay(q);
            var found = hit.Count > 0 &&
                hit["collider"].AsGodotObject() is Area3D area &&
                area.GetMeta("pick_entity").AsGodotObject()
                    is RemoteEntity picked && picked == e;
            if (found) ok++;
            GD.Print($"[pick-test] h=0x{e.Handle:x8} kind={e.Kind} " +
                $"capsule={(found ? "HIT" : "MISS")}");
        }
        GD.Print($"[pick-test] {ok}/{_spawned.Count} entités cliquables");
    }

    private void UpdateCam()
    {
        if (_cam == null) return;
        var dir = new Vector3(
            Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            -Mathf.Sin(_pitch),
            Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        _cam.GlobalPosition = _pivot - dir * _dist;
        _cam.LookAt(_pivot, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb:
                if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
                { _dist = Mathf.Max(3f, _dist * 0.85f); UpdateCam(); }
                else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                { _dist = Mathf.Min(400f, _dist * 1.18f); UpdateCam(); }
                else if (mb.ButtonIndex == MouseButton.Right)
                    _orbitDrag = mb.Pressed;
                break;
            case InputEventMouseMotion mm when _orbitDrag:
                _yaw -= mm.Relative.X * 0.008f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.008f,
                    -0.6f, 1.2f);
                UpdateCam();
                break;
        }
    }

    public override void _Process(double delta)
    {
        // --shot=<path> : capture headless-friendly après ~180 frames
        // (la scène et les GLB sont chargés) puis quit — fiable quand
        // plusieurs fenêtres sont à l'écran (pas de capture active).
        if (!_shotParsed)
        {
            _shotParsed = true;
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--shot"))
                {
                    _shotFrames = 180;
                    if (a.Contains('='))
                        _shotPath = a[(a.IndexOf('=') + 1)..];
                }
        }
        if (_pickTest && _frames++ == 100)
            PickTest();
        if (_charCacheTask is { IsCompleted: true })
        {
            _charCacheTask = null;
            var t = EdenClient.Perf.PerfTrace.Mark();
            var dup = Game.CharBodyCache.GetOrBuild(_charCacheApp);
            EdenClient.Perf.PerfTrace.Report(
                "  char-cache GetOrBuild (P1.3)", t);
            if (dup != null)
            {
                var e = new RemoteEntity
                {
                    Handle = 0x88C8F000u,
                    Kind = 0,
                };
                e.Interp = new Player.NetworkInterpolator
                    { Name = "Interp" };
                e.AddChild(e.Interp);
                e.SetVisual(dup, isPlayer: true);
                e.SetLabel("Lv1 CacheDup");
                e.Position = _pivot + new Vector3(0f, 0f, -1.5f);
                SnapGround(e);
                AddChild(e);
                _spawned.Add(e);
            }
        }
        // --char-cache : laisser le prewarm (~4 s) finir avant le
        // shot — la preuve P1.3 est le temps du GetOrBuild.
        if (_shotFrames == 1 && _charCacheTask != null)
            _shotFrames = 2;
        if (_shotFrames > 0 && --_shotFrames == 0)
        {
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture()?.GetImage();
            img?.SavePng(_shotPath);
            GD.Print($"[vistest] shot {_shotPath}");
            GetTree().Quit();
        }
        if (_auto)
        {
            _yaw += (float)delta * 0.05f;
            UpdateCam();
        }
        if (Input.IsKeyPressed(Key.Q)) GetTree().Quit();
        if (Input.IsKeyPressed(Key.W))
        { _pivot -= _cam!.GlobalBasis.Z * (float)delta * 30f; UpdateCam(); }
        if (Input.IsKeyPressed(Key.S))
        { _pivot += _cam!.GlobalBasis.Z * (float)delta * 30f; UpdateCam(); }
    }
}
