using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using EdenClient.Vfs;

namespace EdenClient.World;

/// <summary>
/// Lecteur minimal Gamebryo/NetImmerse 20.x (<c>.nif</c>) — sous-ensemble
/// M4 : header + table des blocs + string table + corps des blocs
/// connus (NiNode, NiTriShape/Strips + Data, NiTexturingProperty,
/// NiSourceTexture, NiMaterialProperty, NiAlphaProperty,
/// NiStencilProperty, NiVertexColorProperty, NiZBufferProperty).
/// Référence de structure : <c>tools/nif2gltf/nif_reader.py</c> +
/// nif.xml (niftools). Blocs inconnus sautés par la taille déclarée
/// (20.2.0.5+) — jamais de resync raté.
///
/// Tolérances corpus FR (<c>docs/flax/NIF-FR-VARIANT.md</c>) : les nifs
/// de <c>corpus/fr-full</c> peuvent avoir des sections géométrie à zéro
/// (NumTriangles=0 → shape sans triangles, pas d'exception ; blocs qui
/// sur-consomment → tronqués à la taille déclarée + warning).
///
/// Coordoonnées : NIF = Z-up droit, matrices stockées « row-vector »
/// (lignes = axes — preuve convert.py <c>nitransform_to_mat4</c> :
/// la matrice colonne voulue est la transposée de la stockée).
/// Transform local Gamebryo : <c>w = v*S*R + T</c> → lignes de
/// <see cref="Matrix4x4"/> = axes, <see cref="Vector3.Transform"/>
/// fait le produit v·M attendu.
/// </summary>
public sealed class NifFile
{
    private NifFile() { }

    /// <summary>Ligne d'en-tête (« Gamebryo File Format, Version 20.2.0.8 »).</summary>
    public string HeaderString { get; private set; } = "";
    /// <summary>Version encodée (0x14020008 / 0x14030100).</summary>
    public uint Version { get; private set; }
    /// <summary>Shapes résolues (transform nif appliqué aux vertices).</summary>
    public List<NifShape> Shapes { get; } = new();
    /// <summary>Avertissements de parse (jamais d'exception sur data).</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>Total triangles exposés (0 sur nif stripé).</summary>
    public int TriangleCount { get; private set; }
    /// <summary>Nombre de blocs déclarés dans le header.</summary>
    public int BlockCount { get; private set; }
    /// <summary>Hiérarchie NiNode (ordre des blocs) — squelette/bind
    /// pour les animations <see cref="KfFile"/>.</summary>
    public List<NifNode> Nodes { get; } = new();
    /// <summary>Index de bloc → nœud (NiNode uniquement).</summary>
    public Dictionary<int, NifNode> NodeByBlock { get; } = new();

    /// <summary>Erreurs de parse (bloc qui lève ou ne consomme pas sa
    /// taille déclarée) — sous-ensemble de <see cref="Warnings"/>.</summary>
    public List<string> Errors { get; } = new();
    /// <summary>Types de blocs inconnus du lecteur (sautés par la taille
    /// déclarée, sans décodage).</summary>
    public List<string> UnknownBlockTypes { get; } = new();
    /// <summary>Types connus mais volontairement non décodés (PhysX,
    /// pixels embarqués, partitions de skin).</summary>
    public List<string> SkippedBlockTypes { get; } = new();
    /// <summary>Nombre de blocs par type (tous types confondus).</summary>
    public Dictionary<string, int> BlockTypeCounts { get; } = new();
    /// <summary>Racines de la scène (pied de fichier).</summary>
    public List<int> RootBlocks { get; } = new();
    public List<NifAv> RootObjects { get; } = new();

    /// <summary>Tous les objets de scène (nœuds, shapes, lumières,
    /// particules, portails, lignes, caméras) par index de bloc.</summary>
    public Dictionary<int, NifAv> AvByBlock { get; } = new();
    /// <summary>Propriétés (matériau, texturing, alpha, z-buffer…).</summary>
    public List<NifProperty> Properties { get; } = new();
    public Dictionary<int, NifProperty> PropertyByBlock { get; } = new();
    /// <summary>NiSourceTexture par index de bloc.</summary>
    public Dictionary<int, NifSourceTexture> SourceTextures { get; } = new();
    /// <summary>Extra data (string, entier, booléen, flottant, text keys).</summary>
    public List<NifExtraData> ExtraData { get; } = new();
    public Dictionary<int, NifExtraData> ExtraByBlock { get; } = new();
    /// <summary>Contrôleurs (matériau, UV, alpha, flip, vis, transform,
    /// morph, particules) — données seules, pas de runtime.</summary>
    public List<NifController> Controllers { get; } = new();
    public Dictionary<int, NifController> ControllerByBlock { get; } = new();
    public List<NifInterpolator> Interpolators { get; } = new();
    public Dictionary<int, NifInterpolator> InterpolatorByBlock { get; } = new();
    /// <summary>NiControllerSequence embarquées (boucle, aller-retour,
    /// une fois + text keys).</summary>
    public List<NifControllerSequence> Sequences { get; } = new();
    public List<NifParticleSystem> ParticleSystems { get; } = new();
    public List<NifLines> Lines { get; } = new();
    public List<NifLight> Lights { get; } = new();
    public List<NifPortal> Portals { get; } = new();
    public List<NifCamera> Cameras { get; } = new();
    public List<NifNode> Rooms { get; } = new();
    public List<NifNode> RoomGroups { get; } = new();

    /// <summary>Charge un .nif via <see cref="EdenVfs"/> (loose/pack).</summary>
    public static NifFile Load(string path) => Parse(
        EdenVfs.ReadAllBytes(path)
        ?? throw new FileNotFoundException(path));

    /// <summary>Parse un buffer .nif — ne lève que sur magic absent
    /// ou troncature du header ; le reste dégrade en warnings.</summary>
    public static NifFile Parse(byte[] data)
    {
        var f = new NifFile();
        var nl = Array.IndexOf<byte>(data, (byte)'\n');
        if (nl < 0)
            throw new InvalidDataException("Pas de ligne d'en-tête NIF");
        f.HeaderString = Encoding.Latin1.GetString(data, 0, nl);
        if (!f.HeaderString.Contains("File Format"))
            throw new InvalidDataException(
                "Pas un NIF : " + f.HeaderString);
        var cur = new Cur(data, nl + 1, data.Length);
        f.Version = cur.U32();
        var endian = cur.U8();
        if (endian != 1)
            throw new InvalidDataException("NIF big-endian non supporté");
        cur.U32(); // userVersion
        var numBlocks = (int)cur.U32();

        // Table des types (SizedString) — la variante 20.3.1.2 (hash
        // DJB1, pas de noms) n'est pas utilisée par Eden.
        var ntypes = (int)cur.U16();
        var types = new string[ntypes];
        for (var i = 0; i < ntypes; i++)
        {
            var len = (int)cur.U32();
            if (len < 0 || len > 256)
                throw new InvalidDataException(
                    $"NIF : type len {len} invalide (variante hash ?)");
            types[i] = Encoding.Latin1.GetString(cur.Take(len));
        }
        var bidx = new int[numBlocks];
        for (var i = 0; i < numBlocks; i++)
            bidx[i] = cur.U16() & 0x7FFF;   // bit 15 = drapeau (PhysX), pas un index
        var hasSizes = f.Version >= 0x14020005;
        var sizes = new int[numBlocks];
        for (var i = 0; i < numBlocks; i++)
            sizes[i] = hasSizes ? (int)cur.U32() : -1;

        // String table (20.1.0.1+) puis groupes (5.0.0.6+).
        var strings = Array.Empty<string>();
        if (f.Version >= 0x14010001)
        {
            var nstr = (int)cur.U32();
            cur.U32(); // maxLen
            strings = new string[nstr];
            for (var i = 0; i < nstr; i++)
            {
                var len = (int)cur.U32();
                if (len < 0 || len > (1 << 22))
                    throw new InvalidDataException("NIF string trop longue");
                strings[i] = Encoding.Latin1.GetString(cur.Take(len));
            }
        }
        if (f.Version >= 0x05000006)
        {
            var ngroups = (int)cur.U32();
            cur.Skip(4 * ngroups);
        }

        // Corps des blocs — chacun borné à sa taille déclarée.
        f.BlockCount = numBlocks;
        f.Strings = strings;
        var blocks = new Block[numBlocks];
        for (var i = 0; i < numBlocks; i++)
        {
            var t = bidx[i] < ntypes ? types[bidx[i]] : "?";
            var b = new Block { Type = t, Index = i };
            f.BlockTypeCounts[t] = f.BlockTypeCounts.GetValueOrDefault(t) + 1;
            var start = cur.Pos;
            var end = hasSizes
                ? Math.Min(start + sizes[i], data.Length)
                : data.Length;
            var next = hasSizes ? start + Math.Max(sizes[i], 0) : -1;
            try
            {
                var bc = new Cur(data, start, end);
                f.ReadBlock(b, bc, strings, f.Version);
                if (hasSizes && bc.Pos < end && b.Known)
                    f.Fail($"bloc {i} {t} consommé {bc.Pos - start}/{sizes[i]}");
                if (!b.Known)
                    f.NoteUntyped(t);
                if (!hasSizes) next = b.Known ? bc.Pos : -1;
            }
            catch (Exception e)
            {
                f.Fail($"bloc {i} {t} parse KO : {e.Message}");
            }
            blocks[i] = b;
            if (next < 0)
            {
                // Avant 20.2.0.5 : pas de tailles, impossible de se
                // resynchroniser sur un bloc inconnu ou en échec.
                f.Fail($"bloc {i} {t} : resynchronisation impossible " +
                    $"(version sans tailles) — {numBlocks - i - 1} blocs perdus");
                break;
            }
            cur.Pos = next;
            if (cur.Pos > data.Length) break;
        }
        for (var i = 0; i < numBlocks; i++)
            blocks[i] ??= new Block { Type = "?", Index = i };
        // Pied de fichier : racines de la scène.
        if (cur.Pos + 4 <= data.Length)
        {
            var fc = new Cur(data, cur.Pos, data.Length);
            var nr = (int)fc.U32();
            if (nr >= 0 && nr <= 64 && fc.Pos + 4 * nr <= data.Length)
                for (var i = 0; i < nr; i++)
                    f.RootBlocks.Add(fc.I32());
        }
        f.Resolve(blocks, strings);
        return f;
    }

    // ------------------------------------------------------------------
    // Corps de bloc
    // ------------------------------------------------------------------

    private sealed class Block
    {
        public string Type = "";
        public int Index;
        public bool Known;
        public string Name = "";
        public Vector3 T;
        public float[] R9 = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        public float S = 1f;
        public int[]? Props;
        public int[]? Children;
        public int Data = -1, Skin = -1;
        public int[]? SkinBones;                    // NiSkinInstance
        public Matrix4x4[]? BoneSkinXf;             // NiSkinData (row-vec)
        public int[][]? BoneVIdx;
        public float[][]? BoneVW;
        public int BaseTexRef = -1;
        public string? FileName;
        public bool HasStencil;
        public ushort StencilFlags;
        public Vector3[]? Verts;
        public Vector3[]? Normals;
        public Vector2[]? Uv;
        public int[] Tris = Array.Empty<int>();
        public Matrix4x4 World;
        // --- extension : objets typés -------------------------------
        public int[] Extra = Array.Empty<int>();    // refs extra data
        public int Ctl = -1;                        // 1er contrôleur
        public ushort Flags;                        // NiAVObject.Flags
        public int Collision = -1;
        public int[] Effects = Array.Empty<int>();
        public NifAv? Av;                           // objet de scène
        public NifNet? Prop;                        // propriété / texture
        public object? Obj;                         // donnée/contrôleur/…
        public Vector4[]? Colors;
        public Vector2[][]? UvSets;
        public bool Strips;
        public bool[]? LineFlags;
    }

    /// <summary>Types NiObject (sans nom/extra/controller) et NiObjectNET
    /// décodés : le reste est sauté par la taille déclarée.</summary>
    private void ReadBlock(Block b, Cur c, string[] strings, uint ver)
    {
        if (ReadObject(b, c, strings, ver))
        {
            b.Known = true;
            return;
        }
        if (!IsNetType(b.Type)) return;
        b.Known = true;
        b.Name = Idx(c.U32(), strings);
        var nx = (int)c.U32();
        b.Extra = new int[nx];
        for (var i = 0; i < nx; i++)
            b.Extra[i] = c.I32();
        b.Ctl = c.I32();                // premier contrôleur
        switch (b.Type)
        {
            case "NiNode":
            case "NiBillboardNode":
            case "NiSwitchNode":
            case "NiLODNode":
            case "NiSortAdjustNode":
            case "NiRoomGroup":
            case "NiRoom":
                AV(b, c);
                var nc = (int)c.U32();
                b.Children = new int[nc];
                for (var i = 0; i < nc; i++)
                    b.Children[i] = c.I32();
                var ne = (int)c.U32();  // effects
                b.Effects = new int[ne];
                for (var i = 0; i < ne; i++)
                    b.Effects[i] = c.I32();
                b.Av = NodeExtra(b, c);
                break;
            case "NiTriShape":
            case "NiTriStrips":
            case "NiLines":
                AV(b, c);
                Geom(b, c, ver);
                b.Av = b.Type == "NiLines" ? new NifLines() : new NifGeom();
                break;
            case "NiParticleSystem":
                AV(b, c);
                Geom(b, c, ver);
                var ps = new NifParticleSystem { WorldSpace = c.U8() != 0 };
                var nm = (int)c.U32();
                ps.ModifierBlocks = new int[nm];
                for (var i = 0; i < nm; i++)
                    ps.ModifierBlocks[i] = c.I32();
                b.Av = ps;
                break;
            case "NiPortal":
                AV(b, c);
                var po = new NifPortal
                {
                    PortalFlags = c.U16(),
                    PlaneCount = c.U16(),
                };
                var npv = (int)c.U16();
                po.Vertices = new Vector3[npv];
                for (var i = 0; i < npv; i++)
                    po.Vertices[i] = c.Vec3();
                po.AdjoinerBlock = c.I32();
                b.Av = po;
                break;
            case "NiDirectionalLight":
            case "NiAmbientLight":
                AV(b, c);
                var li = new NifLight
                {
                    Directional = b.Type == "NiDirectionalLight",
                    SwitchState = c.U8() != 0,
                };
                var nan = (int)c.U32();
                li.AffectedNodes = new int[nan];
                for (var i = 0; i < nan; i++)
                    li.AffectedNodes[i] = c.I32();
                li.Dimmer = c.F32();
                li.Ambient = c.Vec3();
                li.Diffuse = c.Vec3();
                li.Specular = c.Vec3();
                b.Av = li;
                break;
            case "NiCamera":
                AV(b, c);
                var cam = new NifCamera { CameraFlags = c.U16() };
                cam.Left = c.F32(); cam.Right = c.F32();
                cam.Top = c.F32(); cam.Bottom = c.F32();
                cam.Near = c.F32(); cam.Far = c.F32();
                cam.Orthographic = c.U8() != 0;
                cam.ViewLeft = c.F32(); cam.ViewRight = c.F32();
                cam.ViewTop = c.F32(); cam.ViewBottom = c.F32();
                cam.LodAdjust = c.F32();
                cam.SceneBlock = c.I32();
                c.U32(); c.U32();       // écrans polygones/textures
                b.Av = cam;
                break;
            case "NiTexturingProperty":
                b.Prop = TexProp(b, c, ver);
                break;
            case "NiSourceTexture":
                b.Prop = SrcTex(b, c, ver);
                break;
            case "NiMaterialProperty":
                b.Prop = new NifMaterial
                {
                    Ambient = c.Vec3(),
                    Diffuse = c.Vec3(),
                    Specular = c.Vec3(),
                    Emissive = c.Vec3(),
                    Glossiness = c.F32(),
                    Alpha = c.F32(),
                };
                break;
            case "NiAlphaProperty":
                var ap = new NifAlphaProperty();
                ap.Flags = c.U16();
                ap.Threshold = c.U8();
                // 20.3.1.0 : 2 octets de plus en fin de bloc (sens inconnu,
                // observé sur char/model/*.nif — consigné tel quel).
                if (ver >= 0x14030100)
                    ap.Tail = c.U16();
                b.Prop = ap;
                break;
            case "NiStencilProperty":
                var sp = new NifStencilProperty();
                sp.Flags = c.U16();
                sp.Ref = c.U32();
                sp.Mask = c.U32();
                b.HasStencil = true;
                b.StencilFlags = sp.Flags;
                b.Prop = sp;
                break;
            case "NiZBufferProperty":
                b.Prop = new NifZBufferProperty { Flags = c.U16() };
                break;
            case "NiVertexColorProperty":
                b.Prop = new NifVertexColorProperty { Flags = c.U16() };
                break;
            case "NiShadeProperty":
            case "NiSpecularProperty":
            case "NiDitherProperty":
            case "NiWireframeProperty":
                b.Prop = new NifProperty { Flags = c.U16() };
                break;
            case "NiFogProperty":
                b.Prop = new NifProperty { Flags = c.U16() };
                c.F32(); c.Vec3();      // profondeur + couleur
                break;
        }
        if (b.Prop != null)
        {
            b.Prop.Block = b.Index;
            b.Prop.Type = b.Type;
        }
    }

    /// <summary>Champs propres aux nœuds NiNode et dérivés.</summary>
    private NifNode NodeExtra(Block b, Cur c)
    {
        var n = new NifNode();
        switch (b.Type)
        {
            case "NiBillboardNode":
                n.BillboardMode = c.U16();
                break;
            case "NiSwitchNode":
            case "NiLODNode":
                n.SwitchFlags = c.U16();
                n.SwitchIndex = c.U32();
                if (b.Type == "NiLODNode")
                    n.LodDataBlock = c.I32();
                break;
            case "NiSortAdjustNode":
                n.SortingMode = c.U32();
                break;
            case "NiRoomGroup":
                n.ShellBlock = c.I32();
                n.RoomBlocks = RefArray(c);
                break;
            case "NiRoom":
                var nw = (int)c.U32();
                n.WallPlanes = new Vector4[nw];
                for (var i = 0; i < nw; i++)
                    n.WallPlanes[i] = new Vector4(c.Vec3(), c.F32());
                n.InPortalBlocks = RefArray(c);
                n.OutPortalBlocks = RefArray(c);
                n.FixtureBlocks = RefArray(c);
                break;
        }
        return n;
    }

    private static int[] RefArray(Cur c)
    {
        var n = (int)c.U32();
        if (n < 0 || n > c.Remaining / 4)
            throw new InvalidDataException("NIF : tableau de refs trop grand");
        var r = new int[n];
        for (var i = 0; i < n; i++)
            r[i] = c.I32();
        return r;
    }

    /// <summary>Types NiObjectNET (nom + extra + contrôleur) décodés.</summary>
    private static bool IsNetType(string t) => t is
        "NiNode" or "NiBillboardNode" or "NiSwitchNode" or "NiLODNode" or
        "NiSortAdjustNode" or "NiRoomGroup" or "NiRoom" or "NiPortal" or
        "NiTriShape" or "NiTriStrips" or "NiLines" or "NiParticleSystem" or
        "NiDirectionalLight" or "NiAmbientLight" or "NiCamera" or
        "NiTexturingProperty" or "NiSourceTexture" or
        "NiMaterialProperty" or "NiAlphaProperty" or
        "NiStencilProperty" or "NiVertexColorProperty" or
        "NiZBufferProperty" or "NiSpecularProperty" or
        "NiDitherProperty" or "NiShadeProperty" or
        "NiWireframeProperty" or "NiFogProperty";

    /// <summary>Blocs connus mais volontairement non décodés (rendu
    /// physique, pixels embarqués, partitions de skin) : sautés par la
    /// taille déclarée, nom consigné dans <see cref="SkippedBlockTypes"/>.</summary>
    private static bool IsSkippedByDesign(string t) => t is
        "NiSkinPartition" or
        "NiPhysXScene" or "NiPhysXSceneDesc";

    private void NoteUntyped(string t)
    {
        var l = IsSkippedByDesign(t) ? SkippedBlockTypes : UnknownBlockTypes;
        if (!l.Contains(t)) l.Add(t);
    }

    private void Fail(string msg)
    {
        Warnings.Add(msg);
        Errors.Add(msg);
    }

    // ------------------------------------------------------------------
    // Blocs NiObject (données, contrôleurs, interpolateurs, modifiers)
    // ------------------------------------------------------------------

    private bool ReadObject(Block b, Cur c, string[] strings, uint ver)
    {
        switch (b.Type)
        {
            case "NiTriShapeData":
            case "NiTriStripsData":
                b.Strips = b.Type == "NiTriStripsData";
                GeomData(b, c, ver, b.Strips);
                return true;
            case "NiLinesData":
                var lg = GeomCommon(c);
                SetGeom(b, lg);
                b.LineFlags = new bool[lg.NumVerts];
                for (var i = 0; i < lg.NumVerts; i++)
                    b.LineFlags[i] = c.U8() != 0;
                return true;
            case "NiPSysData":
                b.Obj = PSysData(b, c);
                return true;
            case "NiPixelData":
            case "NiPersistentSrcTextureRendererData":
                b.Obj = PixelData(c, ver, b.Type != "NiPixelData");
                return true;
            case "NiSkinInstance":
                b.Data = c.I32();
                c.I32();                    // skin partition ref
                c.I32();                    // skeleton root ptr
                var nb = (int)c.U32();
                b.SkinBones = new int[nb];
                for (var i = 0; i < nb; i++)
                    b.SkinBones[i] = c.I32();
                return true;
            case "NiSkinData":
                SkinData(b, c);
                return true;
            case "NiMorphData":
                b.Obj = MorphData(c, strings);
                return true;
            case "NiFloatData":
                b.Obj = Keys(c, k => k.F32());
                return true;
            case "NiPosData":
                b.Obj = Keys(c, k => k.Vec3());
                return true;
            case "NiColorData":
                b.Obj = Keys(c, k => new Vector4(k.F32(), k.F32(), k.F32(), k.F32()));
                return true;
            case "NiBoolData":
                b.Obj = Keys(c, k => k.U8() != 0);
                return true;
            case "NiTransformData":
                b.Obj = TransformData(c);
                return true;
            case "NiTextKeyExtraData":
            case "NiStringExtraData":
            case "NiBooleanExtraData":
            case "NiIntegerExtraData":
            case "NiIntegersExtraData":
            case "NiFloatExtraData":
                b.Obj = ReadExtraData(b, c, strings);
                return true;
            case "NiCollisionData":
                b.Obj = new NifCollision
                {
                    TargetBlock = c.I32(),
                    PropagationMode = c.U32(),
                    CollisionMode = c.U32(),
                    UseAbv = c.U8(),
                };
                if (((NifCollision)b.Obj).UseAbv == 1)
                    ((NifCollision)b.Obj).Volume = BoundVolume(c);
                return true;
            case "NiRangeLODData":
                var rl = new NifLodData { Center = c.Vec3() };
                var nl = (int)c.U32();
                rl.Ranges = new Vector2[nl];
                for (var i = 0; i < nl; i++)
                    rl.Ranges[i] = new Vector2(c.F32(), c.F32());
                b.Obj = rl;
                return true;
            case "NiControllerSequence":
                b.Obj = Sequence(b, c, strings, ver);
                return true;
            case "NiBSplineData":
                var sd = new NifBSplineData();
                sd.Floats = new float[CountOf(c, 4)];
                for (var i = 0; i < sd.Floats.Length; i++)
                    sd.Floats[i] = c.F32();
                sd.Shorts = new short[CountOf(c, 2)];
                for (var i = 0; i < sd.Shorts.Length; i++)
                    sd.Shorts[i] = (short)c.U16();
                b.Obj = sd;
                return true;
            case "NiBSplineBasisData":
                b.Obj = c.U32();
                return true;
        }
        if (ReadController(b, c, strings)) return true;
        if (ReadInterpolator(b, c)) return true;
        return ReadPSysObject(b, c, strings);
    }

    /// <summary>Lit un compteur u32 en vérifiant qu'il tient dans le bloc.</summary>
    private static int CountOf(Cur c, int elemSize)
    {
        var n = (int)c.U32();
        if (n < 0 || (long)n * elemSize > c.Remaining)
            throw new InvalidDataException($"NIF : compteur {n} hors bloc");
        return n;
    }

    // --------------------------- clés d'animation --------------------

    /// <summary>KeyGroup&lt;T&gt; : nombre, type d'interpolation, clés.</summary>
    private static NifKeys<T> Keys<T>(Cur c, Func<Cur, T> rd)
    {
        var k = new NifKeys<T>();
        var n = (int)c.U32();
        if (n < 0 || n > c.Remaining)
            throw new InvalidDataException($"NIF : {n} clés hors bloc");
        k.Times = new float[n];
        k.Values = new T[n];
        if (n == 0) return k;
        k.Type = (NifKeyType)c.U32();
        if (k.Type == NifKeyType.Quadratic)
        {
            k.Forward = new T[n];
            k.Backward = new T[n];
        }
        else if (k.Type == NifKeyType.Tbc)
            k.Tbc = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            k.Times[i] = c.F32();
            k.Values[i] = rd(c);
            if (k.Type == NifKeyType.Quadratic)
            {
                k.Forward![i] = rd(c);
                k.Backward![i] = rd(c);
            }
            else if (k.Type == NifKeyType.Tbc)
                k.Tbc![i] = c.Vec3();
        }
        return k;
    }

    private static Quaternion Quat(Cur c)
    {
        var w = c.F32(); var x = c.F32(); var y = c.F32(); var z = c.F32();
        return new Quaternion(x, y, z, w);
    }

    private static NifTransformData TransformData(Cur c)
    {
        var d = new NifTransformData();
        var nr = (int)c.U32();
        if (nr < 0 || nr > c.Remaining)
            throw new InvalidDataException("NIF : clés rotation hors bloc");
        d.RotTimes = new float[nr];
        d.Rot = new Quaternion[nr];
        if (nr > 0)
        {
            d.RotType = (NifKeyType)c.U32();
            if (d.RotType == NifKeyType.XyzRotation)
            {
                d.XyzRot = new NifKeys<float>[3];
                for (var a = 0; a < 3; a++)
                    d.XyzRot[a] = Keys(c, k => k.F32());
            }
            else
            {
                if (d.RotType == NifKeyType.Tbc) d.RotTbc = new Vector3[nr];
                for (var i = 0; i < nr; i++)
                {
                    d.RotTimes[i] = c.F32();
                    d.Rot[i] = Quat(c);
                    if (d.RotType == NifKeyType.Tbc)
                        d.RotTbc![i] = c.Vec3();
                }
            }
        }
        d.Pos = Keys(c, k => k.Vec3());
        d.Scale = Keys(c, k => k.F32());
        return d;
    }

    private static NifMorphData MorphData(Cur c, string[] strings)
    {
        var d = new NifMorphData();
        var nm = (int)c.U32();
        var nv = (int)c.U32();
        d.Relative = c.U8() != 0;
        if (nm < 0 || nv < 0 || (long)nm * (4 + 12L * nv) > c.Remaining)
            throw new InvalidDataException("NIF : NiMorphData hors bloc");
        d.Morphs = new NifMorph[nm];
        for (var m = 0; m < nm; m++)
        {
            var mo = new NifMorph { FrameName = Idx(c.U32(), strings) };
            mo.Vectors = new Vector3[nv];
            for (var i = 0; i < nv; i++)
                mo.Vectors[i] = c.Vec3();
            d.Morphs[m] = mo;
        }
        return d;
    }

    private static NifExtraData ReadExtraData(Block b, Cur c, string[] strings)
    {
        var e = new NifExtraData { Block = b.Index, Type = b.Type };
        e.Name = Idx(c.U32(), strings);
        switch (b.Type)
        {
            case "NiStringExtraData":
                e.StringValue = Idx(c.U32(), strings);
                break;
            case "NiBooleanExtraData":
                e.IntValue = c.U8();
                break;
            case "NiIntegerExtraData":
                e.IntValue = (int)c.U32();
                break;
            case "NiFloatExtraData":
                e.FloatValue = c.F32();
                break;
            case "NiIntegersExtraData":
                e.Ints = new int[CountOf(c, 4)];
                for (var i = 0; i < e.Ints.Length; i++)
                    e.Ints[i] = (int)c.U32();
                break;
            case "NiTextKeyExtraData":
                var n = CountOf(c, 8);
                for (var i = 0; i < n; i++)
                {
                    var t = c.F32();
                    e.TextKeys.Add(new NifTextKey(t, Idx(c.U32(), strings)));
                }
                break;
        }
        return e;
    }

    private static NifBoundingVolume BoundVolume(Cur c)
    {
        var v = new NifBoundingVolume { Kind = c.U32() };
        switch (v.Kind)
        {
            case 0:                         // sphère
                v.Center = c.Vec3();
                v.Radius = c.F32();
                break;
            case 1:                         // boîte orientée
                v.Center = c.Vec3();
                v.Axes = new[] { c.Vec3(), c.Vec3(), c.Vec3() };
                v.Extent = c.Vec3();
                break;
            case 2:                         // capsule
                v.Center = c.Vec3();
                v.Origin = c.Vec3();
                v.Extent = new Vector3(c.F32(), 0, 0);
                v.Radius = c.F32();
                break;
            case 4:                         // union
                var n = (int)c.U32();
                if (n < 0 || n > c.Remaining)
                    throw new InvalidDataException("NIF : union BV");
                v.Children = new NifBoundingVolume[n];
                for (var i = 0; i < n; i++)
                    v.Children[i] = BoundVolume(c);
                break;
            case 5:                         // demi-espace
                v.Origin = c.Vec3();        // normale
                v.Radius = c.F32();         // constante
                v.Center = c.Vec3();
                break;
        }
        return v;
    }

    // --------------------------- contrôleurs -------------------------

    private static bool IsPSysFloatCtl(string t) => t is
        "NiPSysEmitterSpeedCtlr" or "NiPSysEmitterLifeSpanCtlr" or
        "NiPSysEmitterInitialRadiusCtlr" or "NiPSysGravityStrengthCtlr" or
        "NiPSysEmitterDeclinationCtlr" or "NiPSysEmitterDeclinationVarCtlr" or
        "NiPSysEmitterPlanarAngleVarCtlr" or "NiPSysInitialRotSpeedCtlr" or
        "NiPSysInitialRotSpeedVarCtlr";

    private bool ReadController(Block b, Cur c, string[] strings)
    {
        var single = b.Type is "NiTextureTransformController" or
            "NiAlphaController" or "NiMaterialColorController" or
            "NiFlipController" or "NiVisController" or
            "NiTransformController" or "NiPSysEmitterCtlr" ||
            IsPSysFloatCtl(b.Type);
        if (!single && b.Type is not ("NiGeomMorpherController" or
            "NiMultiTargetTransformController" or "NiPSysUpdateCtlr" or
            "NiPSysResetOnLoopCtlr"))
            return false;
        var k = new NifController { Block = b.Index, Type = b.Type };
        k.Next = c.I32();
        k.Flags = c.U16();
        k.Frequency = c.F32();
        k.Phase = c.F32();
        k.StartTime = c.F32();
        k.StopTime = c.F32();
        k.TargetBlock = c.I32();
        if (single)
            k.InterpolatorBlock = c.I32();
        switch (b.Type)
        {
            case "NiTextureTransformController":
                k.ShaderMap = c.U8() != 0;
                k.TexSlot = c.U32();
                k.Operation = c.U32();
                break;
            case "NiMaterialColorController":
                k.TargetColor = c.U16();
                break;
            case "NiFlipController":
                k.TexSlot = c.U32();
                k.FlipSourceBlocks = RefArray(c);
                break;
            case "NiGeomMorpherController":
                k.MorpherFlags = c.U16();
                k.MorphDataBlock = c.I32();
                k.AlwaysUpdate = c.U8() != 0;
                var ni = CountOf(c, 8);
                k.MorphInterpolators = new int[ni];
                k.MorphWeights = new float[ni];
                for (var i = 0; i < ni; i++)
                {
                    k.MorphInterpolators[i] = c.I32();
                    k.MorphWeights[i] = c.F32();
                }
                break;
            case "NiMultiTargetTransformController":
                var nt = (int)c.U16();
                k.ExtraTargets = new int[nt];
                for (var i = 0; i < nt; i++)
                    k.ExtraTargets[i] = c.I32();
                break;
            case "NiPSysEmitterCtlr":
                k.ModifierName = Idx(c.U32(), strings);
                k.VisibilityInterpolatorBlock = c.I32();
                break;
            default:
                if (IsPSysFloatCtl(b.Type))
                    k.ModifierName = Idx(c.U32(), strings);
                break;
        }
        b.Obj = k;
        return true;
    }

    private bool ReadInterpolator(Block b, Cur c)
    {
        var ip = new NifInterpolator { Block = b.Index, Type = b.Type };
        switch (b.Type)
        {
            case "NiFloatInterpolator":
                ip.FloatValue = c.F32();
                ip.DataBlock = c.I32();
                break;
            case "NiBoolInterpolator":
                ip.BoolValue = c.U8() != 0;
                ip.DataBlock = c.I32();
                break;
            case "NiPoint3Interpolator":
                ip.PointValue = c.Vec3();
                ip.DataBlock = c.I32();
                break;
            case "NiTransformInterpolator":
                QuatXf(ip, c);
                ip.DataBlock = c.I32();
                break;
            case "NiPathInterpolator":
                ip.PathFlags = c.U16();
                ip.BankDir = c.I32();
                ip.MaxBankAngle = c.F32();
                ip.Smoothing = c.F32();
                ip.FollowAxis = (short)c.U16();
                ip.DataBlock = c.I32();         // NiPosData
                ip.PercentBlock = c.I32();      // NiFloatData
                break;
            case "NiBSplineCompTransformInterpolator":
                BSplineHead(ip, c);
                QuatXf(ip, c);
                ip.TranslationHandle = c.U32();
                ip.RotationHandle = c.U32();
                ip.ScaleHandle = c.U32();
                ip.TranslationOffset = c.F32();
                ip.TranslationHalfRange = c.F32();
                ip.RotationOffset = c.F32();
                ip.RotationHalfRange = c.F32();
                ip.ScaleOffset = c.F32();
                ip.ScaleHalfRange = c.F32();
                break;
            case "NiBSplineCompFloatInterpolator":
                BSplineHead(ip, c);
                ip.FloatValue = c.F32();
                ip.TranslationHandle = c.U32();
                ip.TranslationOffset = c.F32();
                ip.TranslationHalfRange = c.F32();
                break;
            case "NiBSplineCompPoint3Interpolator":
                BSplineHead(ip, c);
                ip.PointValue = c.Vec3();
                ip.TranslationHandle = c.U32();
                ip.TranslationOffset = c.F32();
                ip.TranslationHalfRange = c.F32();
                break;
            default:
                return false;
        }
        b.Obj = ip;
        return true;
    }

    private static void BSplineHead(NifInterpolator ip, Cur c)
    {
        ip.StartTime = c.F32();
        ip.StopTime = c.F32();
        ip.DataBlock = c.I32();             // NiBSplineData
        ip.PercentBlock = c.I32();          // NiBSplineBasisData
    }

    /// <summary>NiQuatTransform : T, quaternion (w,x,y,z), échelle.</summary>
    private static void QuatXf(NifInterpolator ip, Cur c)
    {
        ip.Translation = c.Vec3();
        ip.Rotation = Quat(c);
        ip.Scale = c.F32();
    }

    private static NifControllerSequence Sequence(
        Block b, Cur c, string[] strings, uint ver)
    {
        var s = new NifControllerSequence
        {
            Block = b.Index,
            Name = Idx(c.U32(), strings),
        };
        var n = (int)c.U32();
        c.U32();                            // array grow by
        if (n < 0 || (long)n * 28 > c.Remaining)
            throw new InvalidDataException("NIF : blocs contrôlés hors bloc");
        s.Blocks = new NifControlledBlock[n];
        for (var i = 0; i < n; i++)
            s.Blocks[i] = new NifControlledBlock
            {
                InterpolatorBlock = c.I32(),
                ControllerBlock = c.I32(),
                NodeName = Idx(c.U32(), strings),
                PropertyType = Idx(c.U32(), strings),
                ControllerType = Idx(c.U32(), strings),
                ControllerId = Idx(c.U32(), strings),
                InterpolatorId = Idx(c.U32(), strings),
            };
        s.Weight = c.F32();
        s.TextKeysBlock = c.I32();
        s.Cycle = (NifCycle)c.U32();
        s.Frequency = c.F32();
        s.StartTime = c.F32();
        s.StopTime = c.F32();
        s.ManagerBlock = c.I32();
        s.AccumRootName = Idx(c.U32(), strings);
        if (ver >= 0x14030008)
            s.AccumFlags = c.U32();
        return s;
    }

    // --------------------------- particules --------------------------

    private bool ReadPSysObject(Block b, Cur c, string[] strings)
    {
        NifPSysModifier m;
        switch (b.Type)
        {
            case "NiPSysBoxEmitter":
                m = new NifPSysEmitter { Shape = NifEmitterShape.Box };
                break;
            case "NiPSysSphereEmitter":
                m = new NifPSysEmitter { Shape = NifEmitterShape.Sphere };
                break;
            case "NiPSysMeshEmitter":
                m = new NifPSysEmitter { Shape = NifEmitterShape.Mesh };
                break;
            case "NiPSysAgeDeathModifier": m = new NifPSysAgeDeath(); break;
            case "NiPSysSpawnModifier": m = new NifPSysSpawn(); break;
            case "NiPSysPositionModifier": m = new NifPSysModifier(); break;
            case "NiPSysBoundUpdateModifier": m = new NifPSysBoundUpdate(); break;
            case "NiPSysGrowFadeModifier": m = new NifPSysGrowFade(); break;
            case "NiPSysRotationModifier": m = new NifPSysRotation(); break;
            case "NiPSysColorModifier": m = new NifPSysColor(); break;
            case "NiPSysGravityModifier": m = new NifPSysGravity(); break;
            case "NiPSysColliderManager": m = new NifPSysColliderManager(); break;
            case "NiPSysPlanarCollider":
                var pc = new NifPSysCollider { Block = b.Index, Type = b.Type };
                pc.Bounce = c.F32();
                pc.SpawnOnCollide = c.U8() != 0;
                pc.DieOnCollide = c.U8() != 0;
                pc.SpawnModifierBlock = c.I32();
                pc.ParentBlock = c.I32();
                pc.NextColliderBlock = c.I32();
                pc.ColliderObjectBlock = c.I32();
                pc.Width = c.F32();
                pc.Height = c.F32();
                pc.XAxis = c.Vec3();
                pc.YAxis = c.Vec3();
                b.Obj = pc;
                return true;
            default:
                return false;
        }
        m.Block = b.Index;
        m.Type = b.Type;
        m.Name = Idx(c.U32(), strings);
        m.Order = c.U32();
        m.TargetBlock = c.I32();
        m.Active = c.U8() != 0;
        switch (m)
        {
            case NifPSysEmitter e:
                e.Speed = c.F32();
                e.SpeedVariation = c.F32();
                e.Declination = c.F32();
                e.DeclinationVariation = c.F32();
                e.PlanarAngle = c.F32();
                e.PlanarAngleVariation = c.F32();
                e.InitialColor = new Vector4(c.F32(), c.F32(), c.F32(), c.F32());
                e.InitialRadius = c.F32();
                e.RadiusVariation = c.F32();
                e.LifeSpan = c.F32();
                e.LifeSpanVariation = c.F32();
                if (e.Shape == NifEmitterShape.Mesh)
                {
                    e.MeshBlocks = RefArray(c);
                    e.VelocityType = c.U32();
                    e.EmitFrom = c.U32();
                    e.EmissionAxis = c.Vec3();
                }
                else
                {
                    e.EmitterObjectBlock = c.I32();
                    if (e.Shape == NifEmitterShape.Box)
                    {
                        e.Width = c.F32();
                        e.Height = c.F32();
                        e.Depth = c.F32();
                    }
                    else
                        e.Radius = c.F32();
                }
                break;
            case NifPSysAgeDeath a:
                a.SpawnOnDeath = c.U8() != 0;
                a.SpawnModifierBlock = c.I32();
                break;
            case NifPSysSpawn s:
                s.NumSpawnGenerations = c.U16();
                s.PercentageSpawned = c.F32();
                s.MinNumToSpawn = c.U16();
                s.MaxNumToSpawn = c.U16();
                s.SpawnSpeedVariation = c.F32();
                s.SpawnDirVariation = c.F32();
                s.LifeSpan = c.F32();
                s.LifeSpanVariation = c.F32();
                break;
            case NifPSysBoundUpdate u:
                u.UpdateSkip = c.U16();
                break;
            case NifPSysGrowFade g:
                g.GrowTime = c.F32();
                g.GrowGeneration = c.U16();
                g.FadeTime = c.F32();
                g.FadeGeneration = c.U16();
                break;
            case NifPSysRotation r:
                r.Speed = c.F32();
                r.SpeedVariation = c.F32();
                r.Angle = c.F32();
                r.AngleVariation = c.F32();
                r.RandomSpeedSign = c.U8() != 0;
                r.RandomAxis = c.U8() != 0;
                r.Axis = c.Vec3();
                break;
            case NifPSysColor co:
                co.DataBlock = c.I32();
                break;
            case NifPSysGravity gr:
                gr.GravityObjectBlock = c.I32();
                gr.Axis = c.Vec3();
                gr.Decay = c.F32();
                gr.Strength = c.F32();
                gr.ForceType = c.U32();
                gr.Turbulence = c.F32();
                gr.TurbulenceScale = c.F32();
                break;
            case NifPSysColliderManager cm:
                cm.ColliderBlock = c.I32();
                break;
        }
        b.Obj = m;
        return true;
    }

    /// <summary>NiPSysData : géométrie + tableaux particules + infos.</summary>
    private static NifPSysData PSysData(Block b, Cur c)
    {
        var g = GeomCommon(c);
        SetGeom(b, g);
        var d = new NifPSysData
        {
            MaxParticles = g.NumVerts,
            BoundCenter = g.BoundCenter,
            BoundRadius = g.BoundRadius,
        };
        var nv = g.NumVerts;
        if (c.U8() != 0)                    // radii
        {
            d.Radii = new float[nv];
            for (var i = 0; i < nv; i++) d.Radii[i] = c.F32();
        }
        d.NumActive = c.U16();
        if (c.U8() != 0)                    // sizes
        {
            d.Sizes = new float[nv];
            for (var i = 0; i < nv; i++) d.Sizes[i] = c.F32();
        }
        if (c.U8() != 0)                    // rotations (quaternions)
        {
            d.Rotations = new Quaternion[nv];
            for (var i = 0; i < nv; i++) d.Rotations[i] = Quat(c);
        }
        if (c.U8() != 0)                    // rotation angles
        {
            d.RotationAngles = new float[nv];
            for (var i = 0; i < nv; i++) d.RotationAngles[i] = c.F32();
        }
        if (c.U8() != 0)                    // rotation axes
        {
            d.RotationAxes = new Vector3[nv];
            for (var i = 0; i < nv; i++) d.RotationAxes[i] = c.Vec3();
        }
        d.Info = new NifParticleInfo[nv];
        for (var i = 0; i < nv; i++)
            d.Info[i] = new NifParticleInfo
            {
                Velocity = c.Vec3(),
                Age = c.F32(),
                LifeSpan = c.F32(),
                LastUpdate = c.F32(),
                SpawnGeneration = c.U16(),
                Code = c.U16(),
            };
        if (c.U8() != 0)                    // rotation speeds
        {
            d.RotationSpeeds = new float[nv];
            for (var i = 0; i < nv; i++) d.RotationSpeeds[i] = c.F32();
        }
        d.NumAddedParticles = c.U16();
        d.AddedParticlesBase = c.U16();
        return d;
    }

    // --------------------------- squelette / skin --------------------

    /// <summary>Transform NiTransform (52 o) → matrice row-vector
    /// (lignes = axes, échelle appliquée).</summary>
    private static Matrix4x4 NiXf(Cur c)
    {
        var r = new float[9];
        for (var i = 0; i < 9; i++) r[i] = c.F32();
        var t = c.Vec3();
        var s = c.F32();
        return new Matrix4x4(
            r[0] * s, r[3] * s, r[6] * s, 0,
            r[1] * s, r[4] * s, r[7] * s, 0,
            r[2] * s, r[5] * s, r[8] * s, 0,
            t.X, t.Y, t.Z, 1);
    }

    /// <summary>NiSkinData : transform global puis, par os, transform
    /// skin→os + sphère + poids (index u16, poids f32).</summary>
    private static void SkinData(Block b, Cur c)
    {
        NiXf(c);                        // transform global (vérifié inutile
                                        // 2026-11-03 : non-identitaire même sur
                                        // les shapes corrects — les offsets
                                        // encodent déjà skin→os sur les verts)
        var nb = (int)c.U32();
        var hasW = c.U8() != 0;
        b.BoneSkinXf = new Matrix4x4[nb];
        b.BoneVIdx = new int[nb][];
        b.BoneVW = new float[nb][];
        for (var i = 0; i < nb; i++)
        {
            b.BoneSkinXf[i] = NiXf(c);
            c.Skip(16);                 // bounding sphere
            var nv = (int)c.U16();
            b.BoneVIdx[i] = new int[hasW ? nv : 0];
            b.BoneVW[i] = new float[hasW ? nv : 0];
            if (!hasW) continue;
            for (var k = 0; k < nv; k++)
            {
                b.BoneVIdx[i][k] = c.U16();
                b.BoneVW[i][k] = c.F32();
            }
        }
    }

    /// <summary>NiAVObject : flags, TRS, liste de propriétés, collision.</summary>
    private static void AV(Block b, Cur c)
    {
        b.Flags = c.U16();
        b.T = c.Vec3();
        b.R9 = new float[9];
        for (var i = 0; i < 9; i++)
            b.R9[i] = c.F32();
        b.S = c.F32();
        var np = (int)c.U32();
        b.Props = new int[np];
        for (var i = 0; i < np; i++)
            b.Props[i] = c.I32();
        b.Collision = c.I32();          // collision object ref
    }

    /// <summary>NiGeometry : data ref + skin ref + MaterialData.</summary>
    private static void Geom(Block b, Cur c, uint ver)
    {
        b.Data = c.I32();
        b.Skin = c.I32();
        // MaterialData : jusqu'à 20.1.0.3 « has shader » (+ nom + extra) ;
        // dès 20.2.0.5 noms fixed-string + extra + actif (+ u8 « needs
        // update » en 20.2.0.7+). Corps ignoré — le matériau visuel vient
        // des Properties du shape.
        if (ver <= 0x14010003)
        {
            if (c.U8() != 0)
                c.Skip(8);
            return;
        }
        var n = (int)c.U32();
        c.Skip(4 * n + 4 * n + 4);
        if (ver >= 0x14020007)
            c.U8();
    }

    /// <summary>Résultat de la lecture du préfixe NiGeometryData.</summary>
    private sealed class GeomHead
    {
        public int NumVerts;
        public ushort DataFlags;
        public Vector3[]? Verts, Normals;
        public Vector4[]? Colors;
        public Vector2[][]? Uv;
        public Vector3 BoundCenter;
        public float BoundRadius;
    }

    /// <summary>Préfixe commun NiGeometryData (NiTriShapeData, StripsData,
    /// LinesData, ParticlesData).</summary>
    private static GeomHead GeomCommon(Cur c)
    {
        var g = new GeomHead();
        c.I32();                        // group id
        var nv = (int)c.U16();
        g.NumVerts = nv;
        c.U8(); c.U8();                 // keep/compress flags
        if (c.U8() != 0)
        {
            g.Verts = new Vector3[nv];
            for (var i = 0; i < nv; i++)
                g.Verts[i] = c.Vec3();
        }
        g.DataFlags = c.U16();
        if (c.U8() != 0)
        {
            g.Normals = new Vector3[nv];
            for (var i = 0; i < nv; i++)
                g.Normals[i] = c.Vec3();
            if ((g.DataFlags & 0x1000) != 0)   // tangents+bitangents
                c.Skip(24 * nv);
        }
        g.BoundCenter = c.Vec3();       // bounding sphere
        g.BoundRadius = c.F32();
        if (c.U8() != 0)
        {
            g.Colors = new Vector4[nv];
            for (var i = 0; i < nv; i++)
                g.Colors[i] = new Vector4(c.F32(), c.F32(), c.F32(), c.F32());
        }
        var nuv = g.DataFlags & 63;
        if (nuv > 0)
        {
            g.Uv = new Vector2[nuv][];
            for (var s = 0; s < nuv; s++)
            {
                g.Uv[s] = new Vector2[nv];
                for (var i = 0; i < nv; i++)
                    g.Uv[s][i] = c.Uv2();
            }
        }
        c.U16();                        // consistency flags
        c.I32();                        // additional data ref
        return g;
    }

    private static void SetGeom(Block b, GeomHead g)
    {
        b.Verts = g.Verts;
        b.Normals = g.Normals;
        b.Colors = g.Colors;
        b.UvSets = g.Uv;
        b.Uv = g.Uv?[0];
    }

    /// <summary>NiTriShapeData / NiTriStripsData — géométrie complète.</summary>
    private void GeomData(Block b, Cur c, uint ver, bool strips)
    {
        var g = GeomCommon(c);
        SetGeom(b, g);
        var ntri = (int)c.U16();
        if (strips)
        {
            var nstrips = (int)c.U16();
            var lens = new int[nstrips];
            for (var i = 0; i < nstrips; i++)
                lens[i] = c.U16();
            var hasPts = c.U8() != 0;
            b.Tris = hasPts ? Unstrip(c, lens) : Array.Empty<int>();
        }
        else
        {
            c.U32();                    // num triangle points
            var hasT = c.U8() != 0;
            if (hasT)
            {
                b.Tris = new int[ntri * 3];
                for (var i = 0; i < ntri * 3; i++)
                    b.Tris[i] = c.U16();
            }
            var ng = (int)c.U16();      // match groups (skinnés) — skip
            for (var i = 0; i < ng; i++)
            {
                var mg = (int)c.U16();
                c.Skip(2 * mg);
            }
        }
        if (b.Tris.Length == 0 && ntri > 0)
            Warnings.Add(
                $"data bloc {b.Index} : {ntri} tris déclarés, 0 lus (stripé ?)");
    }

    /// <summary>Strip → liste de triangles (winding alterné,
    /// dégénérés exclus).</summary>
    private static int[] Unstrip(Cur c, int[] lens)
    {
        var tris = new List<int>();
        foreach (var len in lens)
        {
            var p = new int[len];
            for (var i = 0; i < len; i++)
                p[i] = c.U16();
            for (var i = 0; i + 2 < len; i++)
            {
                var a = p[i]; var b2 = p[i + 1]; var c2 = p[i + 2];
                if (a == b2 || b2 == c2 || a == c2) continue;
                if ((i & 1) == 0)
                    tris.AddRange(new[] { a, b2, c2 });
                else
                    tris.AddRange(new[] { b2, a, c2 });
            }
        }
        return tris.ToArray();
    }

    /// <summary>TexDesc (20.1.0.3+) : source, flags, transform optionnel.</summary>
    private static NifTexMap TexDesc(Cur c)
    {
        var m = new NifTexMap { SourceBlock = c.I32(), Flags = c.U16() };
        if (c.U8() != 0)                // transform présente
        {
            m.HasTransform = true;
            m.Translation = c.Uv2();
            m.Scale = c.Uv2();
            m.Rotation = c.F32();
            m.TransformMethod = c.U32();
            m.Center = c.Uv2();
        }
        return m;
    }

    /// <summary>NiTexturingProperty — tous les slots (base, dark, detail,
    /// gloss, glow, bump, normal, parallax, decals) et shader textures.</summary>
    private NifTexturingProperty TexProp(Block b, Cur c, uint ver)
    {
        var tp = new NifTexturingProperty { Flags = c.U16() };
        var count = (int)c.U32();
        NifTexMap? One() => c.U8() != 0 ? TexDesc(c) : null;
        tp.Base = One();
        tp.Dark = One();
        tp.Detail = One();
        tp.Gloss = One();
        tp.Glow = One();
        if (count > 5 && (tp.Bump = One()) != null)
        {
            tp.BumpLumaScale = c.F32();
            tp.BumpLumaOffset = c.F32();
            tp.BumpMatrix = new Vector4(c.F32(), c.F32(), c.F32(), c.F32());
        }
        // Avant 20.2.0.5 : pas de normal/parallax, decals dès count > 6.
        var decalStart = 6;
        if (ver >= 0x14020005)
        {
            if (count > 6) tp.Normal = One();
            if (count > 7 && (tp.Parallax = One()) != null)
                tp.ParallaxOffset = c.F32();
            decalStart = 8;
        }
        var decals = new List<NifTexMap>();
        for (var i = 0; i < 4; i++)
            if (count > decalStart + i && One() is { } d)
                decals.Add(d);
        tp.Decals = decals.ToArray();
        var ns = CountOf(c, 1);
        tp.ShaderMaps = new NifTexMap?[ns];
        for (var i = 0; i < ns; i++)
        {
            if (c.U8() != 0)
            {
                tp.ShaderMaps[i] = TexDesc(c);
                tp.ShaderMaps[i]!.MapId = c.U32();
            }
        }
        // Source de la « texture de base » historique : 1er slot présent.
        foreach (var m in new[] { tp.Base, tp.Dark, tp.Detail, tp.Gloss,
                     tp.Glow, tp.Bump, tp.Normal, tp.Parallax })
            if (m != null) { b.BaseTexRef = m.SourceBlock; break; }
        if (b.BaseTexRef < 0 && tp.Decals.Length > 0)
            b.BaseTexRef = tp.Decals[0].SourceBlock;
        return tp;
    }

    /// <summary>NiSourceTexture — nom (index string table) ; pixels
    /// embarqués non chargés (bloc NiPixelData sauté).</summary>
    private NifSourceTexture SrcTex(Block b, Cur c, uint ver)
    {
        var t = new NifSourceTexture { External = c.U8() != 0 };
        // FilePath = index de la string table en 20.1.0.3+ (tous les
        // nifs Eden sont ≥ 20.1) ; présent aussi pour les internes.
        t.FileName = Idx(c.U32(), Strings ?? Array.Empty<string>());
        t.PixelDataBlock = c.I32();
        t.PixelLayout = c.U32();
        t.UseMipmaps = c.U32();
        t.AlphaFormat = c.U32();
        c.U8();                         // is static
        c.U8();                         // direct render
        if (ver >= 0x14020004)
            c.U8();                     // persist render data
        if (t.External) b.FileName = t.FileName;
        return t;
    }

    /// <summary>NiPixelData (texture embarquée, nif ≥ 10.4.0.2 &lt; 20.3.0.4 : NiPixelFormat avec
    /// 4 canaux de 10 octets). Les sprites de particules de plusieurs effets (s81111, s82211…) n'ont
    /// pas de .dds externe : sans ces pixels ils étaient rendus en carrés pleins.</summary>
    private static NifPixelData PixelData(Cur c, uint ver, bool persistent)
    {
        var d = new NifPixelData { Format = (int)c.U32() };
        d.BitsPerPixel = c.U8();
        c.U32(); c.U32(); c.U8();          // renderer hint, extra data, flags
        c.U32();                           // tiling
        if (ver >= 0x14030004) c.U8();     // sRGB
        d.Channels = new int[4];
        for (var i = 0; i < 4; i++)
        {
            d.Channels[i] = (int)c.U32();  // type (0 R, 1 G, 2 B, 3 A, 4 compressed…)
            c.U32(); c.U8(); c.U8();       // convention, bits, signed
        }
        c.I32();                           // palette ref
        var nm = (int)c.U32();
        d.BytesPerPixel = (int)c.U32();
        if (nm < 0 || nm > 32) throw new InvalidDataException("NiPixelData: mips");
        d.Mips = new (int W, int H, int Offset)[nm];
        for (var i = 0; i < nm; i++) d.Mips[i] = ((int)c.U32(), (int)c.U32(), (int)c.U32());
        var np = (int)c.U32();
        if (persistent && ver >= 0x14020006) c.U32();   // pad num pixels
        var faces = ver >= 0x0A040002 ? (int)c.U32() : 1;
        if (persistent) c.U32();                        // platform id
        d.Data = c.Take(checked(np * Math.Max(1, faces))).ToArray();
        return d;
    }

    private string[]? Strings;

    private static string Idx(uint i, string[] strings) =>
        i == 0xFFFFFFFF || i >= strings.Length ? "" : strings[i];

    // ------------------------------------------------------------------
    // Résolution : transforms monde du nif + sortie NifShape + liens
    // ------------------------------------------------------------------

    private void Resolve(Block[] blocks, string[] strings)
    {
        bool Ok(int i) => i >= 0 && i < blocks.Length;
        // Chaîne parentale via les enfants NiNode.
        var parent = new int[blocks.Length];
        Array.Fill(parent, -1);
        foreach (var b in blocks)
            if (b.Children != null)
                foreach (var ch in b.Children)
                    if (ch >= 0 && ch < blocks.Length)
                        parent[ch] = b.Index;
        Matrix4x4 WorldOf(Block b)
        {
            var chain = new Stack<Block>();
            for (var p = b.Index; p >= 0 && p < blocks.Length;)
            {
                var x = blocks[p];
                chain.Push(x);
                if (x.Av == null || chain.Count > blocks.Length) break;
                p = parent[p];
            }
            var m = Matrix4x4.Identity;
            while (chain.Count > 0)
                m = Local(chain.Pop()) * m;
            return m;
        }
        foreach (var b in blocks)
            b.World = WorldOf(b);

        // --- objets typés : champs communs + registres -----------------
        foreach (var b in blocks)
        {
            if (!b.Known) continue;
            NifNet? net = b.Av ?? b.Prop;
            if (b.Obj is NifExtraData xd)
            {
                ExtraData.Add(xd);
                ExtraByBlock[b.Index] = xd;
            }
            else if (b.Obj is NifController ct)
            {
                Controllers.Add(ct);
                ControllerByBlock[b.Index] = ct;
            }
            else if (b.Obj is NifInterpolator ip)
            {
                Interpolators.Add(ip);
                InterpolatorByBlock[b.Index] = ip;
            }
            else if (b.Obj is NifControllerSequence sq)
                Sequences.Add(sq);
            if (net == null) continue;
            net.Block = b.Index;
            net.Type = b.Type;
            net.Name = b.Name;
            net.ExtraBlocks = b.Extra;
            net.ControllerBlock = b.Ctl;
            if (b.Prop is NifProperty pp)
            {
                Properties.Add(pp);
                PropertyByBlock[b.Index] = pp;
            }
            if (b.Prop is NifSourceTexture st)
                SourceTextures[b.Index] = st;
            if (b.Av is not { } av) continue;
            av.Flags = b.Flags;
            av.ParentBlock = parent[b.Index];
            av.Translation = b.T;
            av.Scale = b.S;
            av.Rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                b.R9[0], b.R9[3], b.R9[6], 0,
                b.R9[1], b.R9[4], b.R9[7], 0,
                b.R9[2], b.R9[5], b.R9[8], 0, 0, 0, 0, 1));
            av.BindLocal = Local(b);
            av.BindWorld = b.World;
            av.PropertyBlocks = b.Props ?? Array.Empty<int>();
            av.CollisionBlock = b.Collision;
            AvByBlock[b.Index] = av;
        }

        // Nœuds (NiNode et dérivés) — ordre des blocs, comme avant.
        foreach (var b in blocks)
        {
            if (!b.Known || b.Children == null || b.Av is not NifNode n)
                continue;
            n.ChildBlocks = b.Children;
            n.EffectBlocks = b.Effects;
            NodeByBlock[b.Index] = n;
            Nodes.Add(n);
        }
        foreach (var n in Nodes)
        {
            foreach (var ch in n.ChildBlocks)
                if (AvByBlock.TryGetValue(ch, out var cav))
                    n.Children.Add(cav);
            if (n.LodDataBlock >= 0 && Ok(n.LodDataBlock) &&
                blocks[n.LodDataBlock].Obj is NifLodData ld)
            {
                n.LodCenter = ld.Center;
                n.LodRanges = ld.Ranges;
            }
        }
        foreach (var rb in RootBlocks)
            if (AvByBlock.TryGetValue(rb, out var rav))
                RootObjects.Add(rav);

        foreach (var st in SourceTextures.Values)
            if (!st.External && st.PixelDataBlock >= 0 && st.PixelDataBlock < blocks.Length)
                st.Pixels = blocks[st.PixelDataBlock].Obj as NifPixelData;

        // --- liens des références --------------------------------------
        foreach (var b in blocks)
        {
            if (!b.Known) continue;
            switch (b.Prop)
            {
                case NifTexturingProperty tp:
                    foreach (var m in tp.AllMaps())
                        if (SourceTextures.TryGetValue(m.SourceBlock, out var s))
                        {
                            m.Source = s;
                            m.File = s.External ? s.FileName : null;
                        }
                    break;
            }
            if (b.Av is NifAv a0)
                foreach (var pb in a0.PropertyBlocks)
                    if (PropertyByBlock.TryGetValue(pb, out var p0))
                        a0.Properties.Add(p0);
            if (b.Prop != null || b.Av != null)
            {
                var net = b.Av ?? b.Prop!;
                foreach (var eb in net.ExtraBlocks)
                    if (ExtraByBlock.TryGetValue(eb, out var xe))
                        net.Extra.Add(xe);
                var seen = 0;
                for (var cb = net.ControllerBlock;
                     ControllerByBlock.TryGetValue(cb, out var cc) &&
                     seen++ < blocks.Length; cb = cc.Next)
                    net.ControllerList.Add(cc);
            }
        }
        foreach (var k in Controllers)
        {
            if (Ok(k.TargetBlock)) k.TargetName = blocks[k.TargetBlock].Name;
            k.Interpolator = Lookup(InterpolatorByBlock, k.InterpolatorBlock);
            k.VisibilityInterpolator =
                Lookup(InterpolatorByBlock, k.VisibilityInterpolatorBlock);
            if (Ok(k.MorphDataBlock))
                k.MorphData = blocks[k.MorphDataBlock].Obj as NifMorphData;
            k.MorphInterpolatorList = k.MorphInterpolators
                .Select(i => Lookup(InterpolatorByBlock, i)).ToArray();
            k.FlipFiles = k.FlipSourceBlocks
                .Select(i => SourceTextures.TryGetValue(i, out var s)
                    ? s.FileName : "").ToArray();
        }
        foreach (var ip in Interpolators)
        {
            var d = Ok(ip.DataBlock) ? blocks[ip.DataBlock].Obj : null;
            switch (ip.Type)
            {
                case "NiFloatInterpolator": ip.Floats = d as NifKeys<float>; break;
                case "NiBoolInterpolator": ip.Bools = d as NifKeys<bool>; break;
                case "NiPoint3Interpolator": ip.Points = d as NifKeys<Vector3>; break;
                case "NiTransformInterpolator":
                    ip.Transform = d as NifTransformData; break;
                case "NiPathInterpolator":
                    ip.Points = d as NifKeys<Vector3>;
                    ip.Floats = Ok(ip.PercentBlock)
                        ? blocks[ip.PercentBlock].Obj as NifKeys<float> : null;
                    break;
                default:                    // B-splines
                    ip.SplineData = d as NifBSplineData;
                    if (Ok(ip.PercentBlock) &&
                        blocks[ip.PercentBlock].Obj is uint ncp)
                        ip.NumControlPoints = (int)ncp;
                    break;
            }
        }
        foreach (var sq in Sequences)
        {
            if (Ok(sq.TextKeysBlock) &&
                blocks[sq.TextKeysBlock].Obj is NifExtraData tk)
                sq.TextKeys = tk.TextKeys;
            foreach (var cb in sq.Blocks)
            {
                cb.Interpolator = Lookup(InterpolatorByBlock, cb.InterpolatorBlock);
                cb.Controller = Lookup(ControllerByBlock, cb.ControllerBlock);
            }
        }

        // --- shapes, lignes, particules -----------------------------------
        foreach (var b in blocks)
        {
            if (b.Av is NifParticleSystem ps)
            {
                ParticleSystemLink(ps, b, blocks);
                ParticleSystems.Add(ps);
            }
            else if (b.Av is NifLines ln)
                BuildLines(ln, b, blocks);
            else if (b.Av is NifLight lt) Lights.Add(lt);
            else if (b.Av is NifPortal po) Portals.Add(po);
            else if (b.Av is NifCamera cm) Cameras.Add(cm);
            if (b.Av is NifNode { Type: "NiRoom" } rn) Rooms.Add(rn);
            else if (b.Av is NifNode { Type: "NiRoomGroup" } rg) RoomGroups.Add(rg);
        }

        T? Inherited<T>(Block geom, string? typeName = null)
            where T : NifProperty
        {
            var guard = 0;
            for (var bi = geom.Index; Ok(bi) && guard++ < 4096; bi = parent[bi])
                foreach (var pr in blocks[bi].Props ?? Array.Empty<int>())
                    if (Ok(pr) && blocks[pr].Prop is T t &&
                        (typeName == null || t.Type == typeName))
                        return t;
            return null;
        }

        foreach (var b in blocks)
        {
            if (!IsGeom(b) || b.Data < 0 || b.Data >= blocks.Length)
                continue;
            var d = blocks[b.Data];
            if (d.Verts == null || d.Verts.Length == 0)
                continue;               // stripé ou vide — mesh explicite
            NifSkin? skin = null;
            if (b.Skin >= 0 && b.Skin < blocks.Length &&
                blocks[b.Skin].SkinBones is { } bones &&
                b.Skin >= 0 && blocks[b.Skin].Data is var sdi and >= 0 &&
                sdi < blocks.Length && blocks[sdi].BoneSkinXf != null)
                skin = BuildSkin(blocks[b.Skin], blocks[sdi],
                    d.Verts.Length);
            var sh = new NifShape
            {
                BlockIndex = b.Index,
                Skin = skin,
                LocalVertices = skin != null
                    ? (Vector3[])d.Verts.Clone() : null,
                LocalNormals = skin != null && d.Normals != null
                    ? (Vector3[])d.Normals.Clone() : null,
                Name = b.Name,
                Vertices = d.Verts,
                Normals = d.Normals,
                Uvs = d.Uv,
                UvSets = d.UvSets ?? Array.Empty<Vector2[]>(),
                Colors = d.Colors,
                Indices = d.Tris,
                IsHelper = IsCollisionHelper(b.Name),
                FromStrips = d.Strips,
                World = b.World,
                Node = b.Av as NifGeom,
            };
            if (b.Av is NifGeom g) g.Shape = sh;
            var m = b.World;
            for (var i = 0; i < sh.Vertices.Length; i++)
                sh.Vertices[i] = Vector3.Transform(sh.Vertices[i], m);
            if (sh.Normals != null)
            {
                var mn = m;
                mn.Translation = Vector3.Zero;
                for (var i = 0; i < sh.Normals.Length; i++)
                {
                    var n = Vector3.TransformNormal(sh.Normals[i], mn);
                    sh.Normals[i] = n == Vector3.Zero
                        ? Vector3.UnitZ : Vector3.Normalize(n);
                }
            }
            foreach (var pr in b.Props ?? Array.Empty<int>())
            {
                if (pr < 0 || pr >= blocks.Length) continue;
                var p = blocks[pr];
                switch (p.Type)
                {
                    case "NiTexturingProperty":
                        if (p.BaseTexRef >= 0 &&
                            p.BaseTexRef < blocks.Length)
                            sh.TextureFile = blocks[p.BaseTexRef].FileName;
                        break;
                    case "NiAlphaProperty":
                        sh.AlphaScissor = true;
                        break;
                    case "NiStencilProperty":
                        sh.TwoSided = (p.StencilFlags & 1) == 0;
                        break;
                }
            }
            // Liaison typée complète (héritage parent → enfant).
            sh.Material = Inherited<NifMaterial>(b);
            sh.Texturing = Inherited<NifTexturingProperty>(b);
            sh.Alpha = Inherited<NifAlphaProperty>(b);
            sh.ZBuffer = Inherited<NifZBufferProperty>(b);
            sh.Stencil = Inherited<NifStencilProperty>(b);
            sh.VertexColor = Inherited<NifVertexColorProperty>(b);
            sh.Specular = Inherited<NifProperty>(b, "NiSpecularProperty");
            sh.Shade = Inherited<NifProperty>(b, "NiShadeProperty");
            sh.Dither = Inherited<NifProperty>(b, "NiDitherProperty");
            sh.Wireframe = Inherited<NifProperty>(b, "NiWireframeProperty");
            TriangleCount += sh.Indices.Length / 3;
            Shapes.Add(sh);
        }
    }

    private static T? Lookup<T>(Dictionary<int, T> d, int i) where T : class =>
        d.TryGetValue(i, out var v) ? v : null;

    /// <summary>Lie un système de particules à ses données et modifiers.</summary>
    private static void ParticleSystemLink(
        NifParticleSystem ps, Block b, Block[] blocks)
    {
        bool Ok(int i) => i >= 0 && i < blocks.Length;
        if (Ok(b.Data) && blocks[b.Data].Obj is NifPSysData pd)
            ps.Data = pd;
        foreach (var mb in ps.ModifierBlocks)
        {
            if (!Ok(mb) || blocks[mb].Obj is not NifPSysModifier m) continue;
            ps.Modifiers.Add(m);
            if (m is NifPSysColor co && Ok(co.DataBlock))
                co.Colors = blocks[co.DataBlock].Obj as NifKeys<Vector4>;
            else if (m is NifPSysColliderManager cm)
            {
                var guard = 0;
                for (var cb = cm.ColliderBlock;
                     Ok(cb) && blocks[cb].Obj is NifPSysCollider pc &&
                     guard++ < 64; cb = pc.NextColliderBlock)
                    cm.Colliders.Add(pc);
            }
        }
    }

    /// <summary>Construit les segments d'un NiLines (espace racine).</summary>
    private static void BuildLines(NifLines ln, Block b, Block[] blocks)
    {
        if (b.Data < 0 || b.Data >= blocks.Length) return;
        var d = blocks[b.Data];
        if (d.Verts == null) return;
        ln.Vertices = d.Verts.Select(v => Vector3.Transform(v, b.World)).ToArray();
        ln.Connect = d.LineFlags ?? Array.Empty<bool>();
        ln.Colors = d.Colors;
        ln.UvSets = d.UvSets ?? Array.Empty<Vector2[]>();
    }

    /// <summary>Top-4 influences par vertex depuis NiSkinData
    /// (poids renormalisés ; vertex sans poids → os 0 poids 1).</summary>
    private static NifSkin BuildSkin(Block inst, Block data, int nverts)
    {
        var nb = data.BoneSkinXf!.Length;
        var j = new ushort[nverts * 4];
        var w = new float[nverts * 4];
        for (var bi = 0; bi < nb; bi++)
        {
            var idx = data.BoneVIdx![bi];
            var wt = data.BoneVW![bi];
            for (var k = 0; k < idx.Length; k++)
            {
                var v = idx[k];
                if (v >= nverts || wt[k] <= 0) continue;
                // insertion dans les 4 meilleurs poids
                var o = v * 4;
                var slot = -1;
                var min = float.MaxValue;
                for (var q = 0; q < 4; q++)
                {
                    if (w[o + q] == 0) { slot = q; break; }
                    if (w[o + q] < min) { min = w[o + q]; slot = q; }
                }
                if (w[o + slot] != 0 && w[o + slot] >= wt[k]) continue;
                j[o + slot] = (ushort)bi;
                w[o + slot] = wt[k];
            }
        }
        for (var v = 0; v < nverts; v++)
        {
            var o = v * 4;
            var tot = w[o] + w[o + 1] + w[o + 2] + w[o + 3];
            if (tot <= 0) { w[o] = 1; continue; }
            for (var q = 0; q < 4; q++) w[o + q] /= tot;
        }
        return new NifSkin
        {
            BoneBlocks = inst.SkinBones!,
            BoneSkin = data.BoneSkinXf!,
            Joints = j,
            Weights = w,
        };
    }

    private static bool IsGeom(Block b) =>
        b.Type is "NiTriShape" or "NiTriStrips";

    /// <summary>Transform locale d'un nœud/shape (row-vector :
    /// v*S*R + T — la matrice stockée NIF a ses lignes = axes).</summary>
    private static Matrix4x4 Local(Block b)
    {
        var m = new Matrix4x4(
            b.R9[0] * b.S, b.R9[3] * b.S, b.R9[6] * b.S, 0,
            b.R9[1] * b.S, b.R9[4] * b.S, b.R9[7] * b.S, 0,
            b.R9[2] * b.S, b.R9[5] * b.S, b.R9[8] * b.S, 0,
            b.T.X, b.T.Y, b.T.Z, 1);
        return m;
    }

    /// <summary>Nom de mesh helper/collision natif (invisible dans le
    /// client — port strict de <c>WallHelperName</c> Godot, voir
    /// <c>ToonMaterializer</c> portage).</summary>
    private static readonly Regex HelperName = new(
        @"^(true ?wall\d*|blo(c)?|knock|.*block\d*|.*camera\d*|" +
        @"line\d*)([_:]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Vrai si <paramref name="name"/> est un helper de
    /// collision invisible (« true wall », « blo », « camera »…).</summary>
    public static bool IsCollisionHelper(string name) =>
        HelperName.IsMatch(name);

    // ------------------------------------------------------------------

    /// <summary>Curseur borné little-endian — toute lecture hors
    /// [pos,end) lève (blocs stripés/tronqués → warning, pas crash).
    /// Classe (pas struct) : les helpers partagent la position.</summary>
    private sealed class Cur
    {
        private readonly byte[] _b;
        public int Pos;
        public readonly int End;

        public Cur(byte[] b, int pos, int end) { _b = b; Pos = pos; End = end; }

        /// <summary>Octets restant avant la fin du bloc.</summary>
        public int Remaining => Math.Min(End, _b.Length) - Pos;

        public Span<byte> Take(int n)
        {
            if (n < 0 || Pos + n > End || Pos + n > _b.Length)
                throw new EndOfStreamException();
            var p = Pos;
            Pos += n;
            return _b.AsSpan(p, n);
        }

        public void Skip(int n) => Take(n);
        public byte U8() => _b[Pos < End ? Pos++ : throw new EndOfStreamException()];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public Vector3 Vec3() => new(F32(), F32(), F32());
        public Vector2 Uv2() => new(F32(), F32());
    }
}

/// <summary>Base NiObjectNET : nom, extra data, contrôleurs.</summary>
public class NifNet
{
    /// <summary>Index de bloc.</summary>
    public int Block = -1;
    /// <summary>Nom de type nif (« NiNode »…).</summary>
    public string Type = "";
    public string Name = "";
    public int[] ExtraBlocks = Array.Empty<int>();
    /// <summary>Premier contrôleur (chaîne via <see cref="NifController.Next"/>).</summary>
    public int ControllerBlock = -1;
    /// <summary>Extra data résolues.</summary>
    public List<NifExtraData> Extra { get; } = new();
    /// <summary>Contrôleurs résolus (chaîne complète).</summary>
    public List<NifController> ControllerList { get; } = new();
}

/// <summary>Objet de scène NiAVObject : TRS local, propriétés, collision.</summary>
public class NifAv : NifNet
{
    public ushort Flags;
    /// <summary>Bit 0 des flags NiAVObject : objet masqué.</summary>
    public bool Hidden => (Flags & 1) != 0;
    /// <summary>Index de bloc du parent (−1 = racine).</summary>
    public int ParentBlock = -1;
    /// <summary>TRS local de bind (rotation row-vector → quaternion
    /// System.Numerics).</summary>
    public Vector3 Translation;
    public Quaternion Rotation;
    public float Scale = 1f;
    /// <summary>Matrice locale / monde de bind (row-vector, lignes = axes).</summary>
    public Matrix4x4 BindLocal, BindWorld;
    public int[] PropertyBlocks = Array.Empty<int>();
    /// <summary>Propriétés attachées directement (sans héritage).</summary>
    public List<NifProperty> Properties { get; } = new();
    /// <summary>Bloc NiCollisionData (−1 = aucun).</summary>
    public int CollisionBlock = -1;
}

/// <summary>Nœud NiNode du nif — squelette pour les animations et
/// arbre de scène préservé (billboard, switch, LOD, tri, pièces).</summary>
public sealed class NifNode : NifAv
{
    public int[] ChildBlocks = Array.Empty<int>();
    public int[] EffectBlocks = Array.Empty<int>();
    /// <summary>Enfants résolus (nœuds, shapes, lumières, particules…).</summary>
    public List<NifAv> Children { get; } = new();
    /// <summary>NiBillboardNode : mode (0 face caméra, 1 axe haut, 2 rigide,
    /// 3 centre, 4 centre rigide, 5/9 rotation locale Z).</summary>
    public ushort BillboardMode;
    /// <summary>NiSwitchNode / NiLODNode : flags et enfant actif.</summary>
    public ushort SwitchFlags;
    public uint SwitchIndex;
    /// <summary>NiLODNode : bloc NiRangeLODData, centre et plages
    /// (x = près, y = loin) par niveau.</summary>
    public int LodDataBlock = -1;
    public Vector3 LodCenter;
    public Vector2[] LodRanges = Array.Empty<Vector2>();
    /// <summary>NiSortAdjustNode : mode de tri (0 hérite, 1 off, 2 sous-arbre).</summary>
    public uint SortingMode;
    /// <summary>NiRoomGroup : coque et salles.</summary>
    public int ShellBlock = -1;
    public int[] RoomBlocks = Array.Empty<int>();
    /// <summary>NiRoom : plans de murs (xyz = normale, w = constante),
    /// portails entrants/sortants, fixtures.</summary>
    public Vector4[] WallPlanes = Array.Empty<Vector4>();
    public int[] InPortalBlocks = Array.Empty<int>();
    public int[] OutPortalBlocks = Array.Empty<int>();
    public int[] FixtureBlocks = Array.Empty<int>();
}

/// <summary>Objet géométrie (NiTriShape/NiTriStrips) de l'arbre de scène.</summary>
public sealed class NifGeom : NifAv
{
    /// <summary>Shape triangulée correspondante (null si vide/stripée).</summary>
    public NifShape? Shape;
}

/// <summary>NiLines : segments (<see cref="Connect"/>[i] = relie le
/// sommet i au suivant).</summary>
public sealed class NifLines : NifAv
{
    public Vector3[] Vertices = Array.Empty<Vector3>();
    public bool[] Connect = Array.Empty<bool>();
    public Vector4[]? Colors;
    public Vector2[][] UvSets = Array.Empty<Vector2[]>();
}

/// <summary>NiParticleSystem + données + modifiers résolus.</summary>
public sealed class NifParticleSystem : NifAv
{
    /// <summary>Particules en coordonnées monde (sinon locales).</summary>
    public bool WorldSpace;
    public int[] ModifierBlocks = Array.Empty<int>();
    public NifPSysData? Data;
    /// <summary>Modifiers dans l'ordre du fichier (émetteurs inclus).</summary>
    public List<NifPSysModifier> Modifiers { get; } = new();
    public IEnumerable<T> Of<T>() where T : NifPSysModifier =>
        Modifiers.OfType<T>();
}

/// <summary>NiPortal : polygone de portail entre deux salles.</summary>
public sealed class NifPortal : NifAv
{
    public ushort PortalFlags, PlaneCount;
    public Vector3[] Vertices = Array.Empty<Vector3>();
    public int AdjoinerBlock = -1;
}

/// <summary>NiDirectionalLight / NiAmbientLight.</summary>
public sealed class NifLight : NifAv
{
    public bool Directional;
    public bool SwitchState;
    public int[] AffectedNodes = Array.Empty<int>();
    public float Dimmer;
    public Vector3 Ambient, Diffuse, Specular;
}

public sealed class NifCamera : NifAv
{
    public ushort CameraFlags;
    public float Left, Right, Top, Bottom, Near, Far;
    public bool Orthographic;
    public float ViewLeft, ViewRight, ViewTop, ViewBottom, LodAdjust;
    public int SceneBlock = -1;
}

// ----------------------------------------------------------------------
// Propriétés
// ----------------------------------------------------------------------

/// <summary>Propriété générique (flags) : shade, specular, dither,
/// wireframe, fog.</summary>
public class NifProperty : NifNet
{
    public ushort Flags;
}

/// <summary>NiMaterialProperty — couleurs RGB 0..1 + brillance + alpha.</summary>
public sealed class NifMaterial : NifProperty
{
    public Vector3 Ambient, Diffuse, Specular, Emissive;
    public float Glossiness, Alpha;
}

/// <summary>NiAlphaProperty — blend + test.</summary>
public sealed class NifAlphaProperty : NifProperty
{
    public byte Threshold;
    /// <summary>20.3.1.0 uniquement : 2 octets finaux de sens inconnu.</summary>
    public ushort Tail;
    public bool Blend => (Flags & 1) != 0;
    /// <summary>Facteurs de blend (0 ONE, 1 ZERO, 2 SRC_COLOR, 3 INV_SRC_COLOR,
    /// 4 DEST_COLOR, 5 INV_DEST_COLOR, 6 SRC_ALPHA, 7 INV_SRC_ALPHA…).</summary>
    public int SrcBlend => (Flags >> 1) & 0xF;
    public int DstBlend => (Flags >> 5) & 0xF;
    public bool Test => (Flags & 0x200) != 0;
    /// <summary>Fonction de test (0 ALWAYS, 1 LESS, 2 EQUAL, 3 LEQUAL,
    /// 4 GREATER, 5 NOTEQUAL, 6 GEQUAL, 7 NEVER).</summary>
    public int TestFunc => (Flags >> 10) & 7;
    public bool NoSorter => (Flags & 0x2000) != 0;
}

/// <summary>NiZBufferProperty.</summary>
public sealed class NifZBufferProperty : NifProperty
{
    public bool Test => (Flags & 1) != 0;
    public bool Write => (Flags & 2) != 0;
    public int TestFunc => (Flags >> 2) & 0xF;
}

/// <summary>NiStencilProperty — Enable, actions, mode de dessin.</summary>
public sealed class NifStencilProperty : NifProperty
{
    public uint Ref, Mask;
    public bool Enable => (Flags & 1) != 0;
    /// <summary>0 CCW_OR_BOTH, 1 CCW (face avant), 2 CW (face arrière), 3 BOTH.</summary>
    public int DrawMode => (Flags >> 10) & 3;
    public int TestFunc => (Flags >> 12) & 0xF;
}

/// <summary>NiVertexColorProperty — mode source et éclairage.</summary>
public sealed class NifVertexColorProperty : NifProperty
{
    /// <summary>0 ignore, 1 émissif, 2 ambiant+diffus.</summary>
    public int SourceMode => (Flags >> 4) & 3;
    /// <summary>0 émissif seul, 1 émissif + ambiant + diffus.</summary>
    public int LightingMode => (Flags >> 3) & 1;
}

/// <summary>Un slot de texture (TexDesc) d'un NiTexturingProperty.</summary>
public sealed class NifTexMap
{
    public int SourceBlock = -1;
    public NifSourceTexture? Source;
    /// <summary>Fichier externe de la source (« UE01301.dds »), null si interne.</summary>
    public string? File;
    public ushort Flags;
    /// <summary>Jeu UV utilisé (bits 0-7 des flags).</summary>
    public int UvSet => Flags & 0xFF;
    /// <summary>Filtre (0 NEAREST, 1 BILERP, 2 TRILERP, 3 NEAREST_MIP…).</summary>
    public int FilterMode => (Flags >> 8) & 0xF;
    /// <summary>Clamp (0 CLAMP_S_CLAMP_T, 1 CLAMP_S_WRAP_T, 2 WRAP_S_CLAMP_T, 3 WRAP).</summary>
    public int ClampMode => (Flags >> 12) & 3;
    public bool HasTransform;
    public Vector2 Translation, Scale = Vector2.One, Center;
    public float Rotation;
    public uint TransformMethod;
    /// <summary>Identifiant de map (shader textures uniquement).</summary>
    public uint MapId;
}

/// <summary>NiTexturingProperty — tous les slots.</summary>
public sealed class NifTexturingProperty : NifProperty
{
    public NifTexMap? Base, Dark, Detail, Gloss, Glow, Bump, Normal, Parallax;
    public float BumpLumaScale, BumpLumaOffset;
    public Vector4 BumpMatrix;
    public float ParallaxOffset;
    public NifTexMap[] Decals = Array.Empty<NifTexMap>();
    public NifTexMap?[] ShaderMaps = Array.Empty<NifTexMap?>();
    /// <summary>Mode d'application (0 REPLACE, 1 DECAL, 2 MODULATE, 3 HILIGHT…).</summary>
    public int ApplyMode => (Flags >> 1) & 7;
    /// <summary>Tous les slots présents, dans l'ordre de lecture.</summary>
    public IEnumerable<NifTexMap> AllMaps()
    {
        foreach (var m in new[] { Base, Dark, Detail, Gloss, Glow, Bump,
                     Normal, Parallax })
            if (m != null) yield return m;
        foreach (var m in Decals) yield return m;
        foreach (var m in ShaderMaps)
            if (m != null) yield return m;
    }
}

/// <summary>NiSourceTexture.</summary>
public sealed class NifSourceTexture : NifNet
{
    public bool External;
    public string FileName = "";
    public int PixelDataBlock = -1;
    public uint PixelLayout, UseMipmaps, AlphaFormat;
    /// <summary>Pixels embarqués (texture interne), null si externe ou illisible.</summary>
    public NifPixelData? Pixels;
}

/// <summary>NiPixelData : 0 RGB24, 1 RGBA32, 4/5/6 DXT1/3/5 ; niveaux de mip dans <see cref="Mips"/>.</summary>
public sealed class NifPixelData
{
    public int Format, BitsPerPixel, BytesPerPixel;
    public int[] Channels = new int[4];
    public (int W, int H, int Offset)[] Mips = Array.Empty<(int, int, int)>();
    public byte[] Data = Array.Empty<byte>();
}

// ----------------------------------------------------------------------
// Extra data, collision, LOD
// ----------------------------------------------------------------------

public readonly record struct NifTextKey(float Time, string Text);

/// <summary>NiStringExtraData / Integer / Integers / Boolean / Float /
/// TextKey.</summary>
public sealed class NifExtraData
{
    public int Block;
    public string Type = "";
    public string Name = "";
    public string StringValue = "";
    /// <summary>Entier ou booléen (0/1).</summary>
    public int IntValue;
    public float FloatValue;
    public int[] Ints = Array.Empty<int>();
    public List<NifTextKey> TextKeys { get; set; } = new();
}

/// <summary>Volume englobant (NiCollisionData).</summary>
public sealed class NifBoundingVolume
{
    /// <summary>0 sphère, 1 boîte, 2 capsule, 4 union, 5 demi-espace.</summary>
    public uint Kind;
    public Vector3 Center, Origin, Extent;
    public Vector3[] Axes = Array.Empty<Vector3>();
    public float Radius;
    public NifBoundingVolume[] Children = Array.Empty<NifBoundingVolume>();
}

public sealed class NifCollision
{
    public int TargetBlock = -1;
    public uint PropagationMode, CollisionMode;
    public byte UseAbv;
    public NifBoundingVolume? Volume;
}

public sealed class NifLodData
{
    public Vector3 Center;
    public Vector2[] Ranges = Array.Empty<Vector2>();
}

// ----------------------------------------------------------------------
// Clés, contrôleurs, interpolateurs, séquences
// ----------------------------------------------------------------------

public enum NifKeyType { Linear = 1, Quadratic = 2, Tbc = 3, XyzRotation = 4, Const = 5 }

/// <summary>Cycle d'une séquence/contrôleur : boucle, aller-retour, une fois.</summary>
public enum NifCycle { Loop = 0, Reverse = 1, Clamp = 2 }

/// <summary>Groupe de clés : temps + valeurs (+ tangentes Quadratic, TBC).</summary>
public sealed class NifKeys<T>
{
    public NifKeyType Type = NifKeyType.Linear;
    public float[] Times = Array.Empty<float>();
    public T[] Values = Array.Empty<T>();
    public T[]? Forward, Backward;
    public Vector3[]? Tbc;
    public int Count => Times.Length;
}

/// <summary>NiTransformData : rotation (quaternions ou Euler XYZ),
/// translation, échelle.</summary>
public sealed class NifTransformData
{
    public NifKeyType RotType = NifKeyType.Linear;
    public float[] RotTimes = Array.Empty<float>();
    public Quaternion[] Rot = Array.Empty<Quaternion>();
    public Vector3[]? RotTbc;
    public NifKeys<float>[]? XyzRot;
    public NifKeys<Vector3> Pos = new();
    public NifKeys<float> Scale = new();
}

public sealed class NifMorph
{
    public string FrameName = "";
    public Vector3[] Vectors = Array.Empty<Vector3>();
}

public sealed class NifMorphData
{
    public bool Relative;
    public NifMorph[] Morphs = Array.Empty<NifMorph>();
}

public sealed class NifBSplineData
{
    public float[] Floats = Array.Empty<float>();
    public short[] Shorts = Array.Empty<short>();
}

/// <summary>Contrôleur de temps (données seules). Les champs spécifiques
/// ne valent que pour le <see cref="Type"/> concerné.</summary>
public sealed class NifController
{
    public int Block;
    public string Type = "";
    public int Next = -1;
    public ushort Flags;
    public float Frequency = 1f, Phase, StartTime, StopTime;
    public int TargetBlock = -1;
    public string TargetName = "";
    /// <summary>Cycle (flags bits 1-2) : 0 boucle, 1 aller-retour, 2 une fois.</summary>
    public NifCycle Cycle => (NifCycle)((Flags >> 1) & 3);
    public bool Active => (Flags & 0x8) != 0;
    public bool PlayBackwards => (Flags & 0x10) != 0;
    public bool ManagerControlled => (Flags & 0x20) != 0;
    public int InterpolatorBlock = -1;
    public NifInterpolator? Interpolator;
    // NiTextureTransformController
    public bool ShaderMap;
    /// <summary>Slot de texture (0 base, 1 dark, 2 detail, 3 gloss, 4 glow…).</summary>
    public uint TexSlot;
    /// <summary>Membre animé (0 trans U, 1 trans V, 2 rotation, 3 scale U, 4 scale V).</summary>
    public uint Operation;
    // NiMaterialColorController : 0 ambiant, 1 diffus, 2 spéculaire, 3 émissif
    public ushort TargetColor;
    // NiFlipController
    public int[] FlipSourceBlocks = Array.Empty<int>();
    public string[] FlipFiles = Array.Empty<string>();
    // NiGeomMorpherController
    public ushort MorpherFlags;
    public int MorphDataBlock = -1;
    public NifMorphData? MorphData;
    public bool AlwaysUpdate;
    public int[] MorphInterpolators = Array.Empty<int>();
    public NifInterpolator?[] MorphInterpolatorList = Array.Empty<NifInterpolator?>();
    public float[] MorphWeights = Array.Empty<float>();
    // NiMultiTargetTransformController
    public int[] ExtraTargets = Array.Empty<int>();
    // NiPSys*Ctlr
    public string ModifierName = "";
    public int VisibilityInterpolatorBlock = -1;
    public NifInterpolator? VisibilityInterpolator;
}

/// <summary>Interpolateur : valeur de repos + données de clés résolues.</summary>
public sealed class NifInterpolator
{
    public int Block;
    public string Type = "";
    public float FloatValue;
    public bool BoolValue;
    public Vector3 PointValue;
    public int DataBlock = -1, PercentBlock = -1;
    public NifKeys<float>? Floats;
    public NifKeys<bool>? Bools;
    public NifKeys<Vector3>? Points;
    public NifTransformData? Transform;
    // pose NiQuatTransform (valeurs ±3.4e38 = invalide)
    public Vector3 Translation;
    public Quaternion Rotation;
    public float Scale;
    // NiPathInterpolator
    public ushort PathFlags;
    public int BankDir;
    public float MaxBankAngle, Smoothing;
    public short FollowAxis;
    // B-splines compressées
    public float StartTime, StopTime;
    public NifBSplineData? SplineData;
    public int NumControlPoints;
    public uint TranslationHandle, RotationHandle, ScaleHandle;
    public float TranslationOffset, TranslationHalfRange;
    public float RotationOffset, RotationHalfRange;
    public float ScaleOffset, ScaleHalfRange;
}

public sealed class NifControlledBlock
{
    public int InterpolatorBlock = -1, ControllerBlock = -1;
    public NifInterpolator? Interpolator;
    public NifController? Controller;
    public byte Priority;
    public string NodeName = "", PropertyType = "", ControllerType = "";
    public string ControllerId = "", InterpolatorId = "";
}

/// <summary>NiControllerSequence embarquée.</summary>
public sealed class NifControllerSequence
{
    public int Block;
    public string Name = "";
    public NifControlledBlock[] Blocks = Array.Empty<NifControlledBlock>();
    public float Weight = 1f, Frequency = 1f, StartTime, StopTime;
    public int TextKeysBlock = -1, ManagerBlock = -1;
    public List<NifTextKey> TextKeys { get; set; } = new();
    public NifCycle Cycle;
    public string AccumRootName = "";
    public uint AccumFlags;
}

// ----------------------------------------------------------------------
// Particules
// ----------------------------------------------------------------------

public struct NifParticleInfo
{
    public Vector3 Velocity;
    public float Age, LifeSpan, LastUpdate;
    public ushort SpawnGeneration, Code;
}

/// <summary>NiPSysData : capacité, tableaux optionnels par particule.</summary>
public sealed class NifPSysData
{
    /// <summary>Nombre max de particules (Num Vertices).</summary>
    public int MaxParticles;
    public ushort NumActive, NumAddedParticles, AddedParticlesBase;
    public Vector3 BoundCenter;
    public float BoundRadius;
    public float[]? Radii, Sizes, RotationAngles, RotationSpeeds;
    public Quaternion[]? Rotations;
    public Vector3[]? RotationAxes;
    public NifParticleInfo[] Info = Array.Empty<NifParticleInfo>();
}

public enum NifEmitterShape { Box, Sphere, Mesh }

public class NifPSysModifier
{
    public int Block;
    public string Type = "";
    public string Name = "";
    /// <summary>Ordre d'exécution (NiPSysModifierOrder).</summary>
    public uint Order;
    public int TargetBlock = -1;
    public bool Active;
}

/// <summary>Émetteur Box / Sphere / Mesh.</summary>
public sealed class NifPSysEmitter : NifPSysModifier
{
    public NifEmitterShape Shape;
    public float Speed, SpeedVariation, Declination, DeclinationVariation;
    public float PlanarAngle, PlanarAngleVariation;
    public Vector4 InitialColor;
    public float InitialRadius, RadiusVariation, LifeSpan, LifeSpanVariation;
    public int EmitterObjectBlock = -1;
    /// <summary>Box : dimensions.</summary>
    public float Width, Height, Depth;
    /// <summary>Sphere : rayon.</summary>
    public float Radius;
    /// <summary>Mesh : géométries émettrices, vitesse (0 monde, 1 local, 2 normale…),
    /// zone (0 sommets, 1 surface, 2 volume) et axe.</summary>
    public int[] MeshBlocks = Array.Empty<int>();
    public uint VelocityType, EmitFrom;
    public Vector3 EmissionAxis;
}

public sealed class NifPSysAgeDeath : NifPSysModifier
{
    public bool SpawnOnDeath;
    public int SpawnModifierBlock = -1;
}

public sealed class NifPSysSpawn : NifPSysModifier
{
    public ushort NumSpawnGenerations, MinNumToSpawn, MaxNumToSpawn;
    public float PercentageSpawned, SpawnSpeedVariation, SpawnDirVariation;
    public float LifeSpan, LifeSpanVariation;
}

public sealed class NifPSysBoundUpdate : NifPSysModifier
{
    public ushort UpdateSkip;
}

public sealed class NifPSysGrowFade : NifPSysModifier
{
    public float GrowTime, FadeTime;
    public ushort GrowGeneration, FadeGeneration;
}

public sealed class NifPSysRotation : NifPSysModifier
{
    public float Speed, SpeedVariation, Angle, AngleVariation;
    public bool RandomSpeedSign, RandomAxis;
    public Vector3 Axis;
}

public sealed class NifPSysColor : NifPSysModifier
{
    public int DataBlock = -1;
    /// <summary>Courbe RGBA sur la vie de la particule (NiColorData).</summary>
    public NifKeys<Vector4>? Colors;
}

public sealed class NifPSysGravity : NifPSysModifier
{
    public int GravityObjectBlock = -1;
    public Vector3 Axis;
    public float Decay, Strength, Turbulence, TurbulenceScale;
    /// <summary>0 = gravité plane, 1 = ponctuelle.</summary>
    public uint ForceType;
}

public sealed class NifPSysColliderManager : NifPSysModifier
{
    public int ColliderBlock = -1;
    public List<NifPSysCollider> Colliders { get; } = new();
}

/// <summary>NiPSysPlanarCollider (chaînés par <see cref="NextColliderBlock"/>).</summary>
public sealed class NifPSysCollider
{
    public int Block;
    public string Type = "";
    public float Bounce;
    public bool SpawnOnCollide, DieOnCollide;
    public int SpawnModifierBlock = -1, ParentBlock = -1;
    public int NextColliderBlock = -1, ColliderObjectBlock = -1;
    public float Width, Height;
    public Vector3 XAxis, YAxis;
}

/// <summary>Skin d'un shape : os (blocs), transform skin→os par os
/// (row-vector) et top-4 influences par vertex.</summary>
public sealed class NifSkin
{
    /// <summary>Index de bloc de chaque os (NiNode).</summary>
    public int[] BoneBlocks = Array.Empty<int>();
    /// <summary>Transform espace-skin → espace-os, par os.</summary>
    public Matrix4x4[] BoneSkin = Array.Empty<Matrix4x4>();
    /// <summary>4 indices d'os (dans BoneBlocks) par vertex.</summary>
    public ushort[] Joints = Array.Empty<ushort>();
    /// <summary>4 poids normalisés par vertex.</summary>
    public float[] Weights = Array.Empty<float>();
}

/// <summary>Un mesh triangulé prêt à rendre (espace nif-racine, Z-up).</summary>
public sealed class NifShape
{
    /// <summary>Index de bloc du shape.</summary>
    public int BlockIndex;
    /// <summary>Skin (null si rigide).</summary>
    public NifSkin? Skin;
    /// <summary>Vertices/normales non transformés (espace skin) — seulement
    /// pour les shapes skinnés.</summary>
    public Vector3[]? LocalVertices, LocalNormals;
    /// <summary>Nom du nœud shape (« Object06 », « true wall »…).</summary>
    public string Name = "";
    /// <summary>Vertices transformés dans l'espace racine du nif.</summary>
    public Vector3[] Vertices = Array.Empty<Vector3>();
    /// <summary>Normales transformées (null si absentes).</summary>
    public Vector3[]? Normals;
    /// <summary>Set UV 0 (null si absent).</summary>
    public Vector2[]? Uvs;
    /// <summary>Indices de triangles (3 par face).</summary>
    public int[] Indices = Array.Empty<int>();
    /// <summary>Fichier de texture de base (« UE01301.dds », null si
    /// absente ou interne).</summary>
    public string? TextureFile;
    /// <summary>NiAlphaProperty attachée → alpha-scissor côté moteur.</summary>
    public bool AlphaScissor;
    /// <summary>NiStencilProperty sans draw-front → rendu 2 faces.</summary>
    public bool TwoSided;
    /// <summary>Nom helper collision (invisible natif — murs « true
    /// wall », bloqueurs).</summary>
    public bool IsHelper;
    // --- extension : géométrie et matériaux typés -----------------
    /// <summary>Tous les jeux UV (index 0 = <see cref="Uvs"/>).</summary>
    public Vector2[][] UvSets = Array.Empty<Vector2[]>();
    /// <summary>Couleurs de vertex RGBA (null si absentes).</summary>
    public Vector4[]? Colors;
    /// <summary>Vrai si la source était un NiTriStrips (dé-stripé).</summary>
    public bool FromStrips;
    /// <summary>Transform monde du shape (row-vector).</summary>
    public Matrix4x4 World = Matrix4x4.Identity;
    /// <summary>Objet de scène du shape (parent, contrôleurs, extra).</summary>
    public NifGeom? Node;
    // Propriétés effectives (héritage des nœuds parents inclus).
    public NifMaterial? Material;
    public NifTexturingProperty? Texturing;
    public NifAlphaProperty? Alpha;
    public NifZBufferProperty? ZBuffer;
    public NifStencilProperty? Stencil;
    public NifVertexColorProperty? VertexColor;
    public NifProperty? Specular, Shade, Dither, Wireframe;
}
