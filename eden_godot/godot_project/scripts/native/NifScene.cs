using System.Numerics;
using Vector3 = System.Numerics.Vector3;
using EdenClient.World;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace EdenClient.Native;

/// <summary>
/// NifFile → arbre Node3D équivalent à l'import GLB (même repère NIF Z-up, mêmes noms, mêmes types :
/// MeshInstance3D + ArrayMesh par shape, Skeleton3D + Skin pour les shapes skinnés, BoneAttachment3D pour
/// les nœuds non-os sous un os, meta « extras »). Chaque Node3D issu d'un NifAv porte la meta
/// <c>nif_block</c> (index de bloc) pour NativeFx. Matériaux : <see cref="NifMaterials"/>.
/// </summary>
public static class NifScene
{
    public static Node3D? Build(NifFile nif, string name, string texPrefix)
        => new Builder(nif, texPrefix).Run(name);

    internal static Transform3D Xf(Matrix4x4 m) => new(
        new Basis(new Godot.Vector3(m.M11, m.M12, m.M13), new Godot.Vector3(m.M21, m.M22, m.M23),
                  new Godot.Vector3(m.M31, m.M32, m.M33)),
        new Godot.Vector3(m.M41, m.M42, m.M43));

    // Billboard au niveau du MATÉRIAU par défaut : les plans skinnés (FX de montures) ignorent la
    // rotation du nœud faite par BillboardDriver → vus par la tranche (lignes d'1 px).
    // EDEN_NATIVE_BB=0 : ancien mode (driver).
    static readonly bool s_billboard = System.Environment.GetEnvironmentVariable("EDEN_NATIVE_BB") != "0";

    sealed class Builder
    {
        readonly NifFile _nif;
        readonly string _prefix;
        readonly NifMaterials.Cache _mats = new();
        readonly HashSet<string> _used = new();
        readonly Dictionary<NifShape, ArrayMesh?> _meshes = new();
        readonly Dictionary<NifSkelGroup, Skeleton3D> _skels = new();
        readonly Dictionary<NifSkelGroup, Dictionary<int, string>> _boneNames = new();
        Dictionary<int, NifSkelGroup> _shapeGroup = new();
        List<NifSkelGroup> _groups = new();
        readonly HashSet<int> _visiting = new();
        readonly Dictionary<int, Node3D> _made = new();
        readonly Dictionary<int, (Skeleton3D Sk, string Bone)> _boneRef = new();
        // Nœuds NifAv sous un NiBillboardNode (mode), pour les matériaux.
        int _billboardMode = -1;
        // Groupe sémantique courant (NIF de map — rf_game FUN_006ebc70 :
        // les enfants des nœuds « block »/« nocamerablock »/
        // « camerablock »/« floor » sous « Allblock »/« Roomblock* » ou
        // un NiRoom/NiRoomGroup sont des listes de collision de
        // l'éditeur ; « terrain » = sol marchable). 0 = aucun.
        int _colGroup;
        bool _inBlockScope;

        public Builder(NifFile nif, string prefix) { _nif = nif; _prefix = prefix; }

        string Unique(string raw)
        {
            var b = raw.Length == 0 ? "Node" : raw;
            if (_used.Add(b)) return b;
            for (var i = 2; ; i++)
                if (_used.Add(b + i)) return b + i;
        }

        public Node3D? Run(string stem)
        {
            _groups = NifSkeleton.Analyze(_nif, out _shapeGroup);
            var roots = _nif.RootObjects.Where(r => r != null).ToList();
            if (roots.Count == 0)
                roots = _nif.AvByBlock.Values.Where(a => a.ParentBlock < 0 &&
                    a is NifNode or NifGeom or NifLines or NifParticleSystem).OrderBy(a => a.Block).Take(8).ToList();
            var top = new List<Node3D>();
            foreach (var r in roots)
                if (Make(r) is { } n) top.Add(n);
            var root = new Node3D { Name = Unique(stem) };
            if (top.Count == 0) top.Add(new Node3D { Name = Unique("Root") });
            foreach (var t in top) root.AddChild(t);
            BuildEmbeddedAnimation(root);
            return root;
        }

        // ---- nœuds ---------------------------------------------------------
        static IEnumerable<NifAv> KidsOf(NifNode n, NifFile nif)
        {
            foreach (var c in n.Children) yield return c;
            IEnumerable<int> extra = n.Type == "NiRoomGroup"
                ? (n.ShellBlock >= 0 ? new[] { n.ShellBlock } : Array.Empty<int>()).Concat(n.RoomBlocks)
                : n.Type == "NiRoom" ? n.FixtureBlocks : Array.Empty<int>();
            foreach (var b in extra)
                if (nif.AvByBlock.TryGetValue(b, out var av)) yield return av;
        }

        Node3D? Make(NifAv av)
        {
            if (!_visiting.Add(av.Block)) return null;     // cycle
            try { return MakeCore(av); }
            finally { _visiting.Remove(av.Block); }
        }

        Node3D? MakeCore(NifAv av)
        {
            // Racine d'un groupe de squelette : le Skeleton3D prend sa place.
            var grp = _groups.FirstOrDefault(g => g.HostBlock < 0 && g.RootBlock == av.Block);
            if (grp != null) return MakeSkeleton(grp);

            var name = Unique(av.Name.Length > 0 ? av.Name : av.Type);
            Node3D node;
            ArrayMesh? mesh = null;
            NifShape? shape = (av as NifGeom)?.Shape;
            if (shape != null && !(shape.Skin != null && _shapeGroup.ContainsKey(shape.BlockIndex)))
                mesh = MeshFor(shape, rigid: true);
            if (mesh != null)
            {
                var mi = new MeshInstance3D { Name = name, Mesh = mesh };
                mi.SetSurfaceOverrideMaterial(0, MaterialFor(shape!));
                ApplyGroup(mi, shape!);
                node = mi;
            }
            else node = new Node3D { Name = name };
            node.Transform = Xf(av.BindLocal);
            Tag(node, av);
            // NiAVObject::Hidden (bit 0) — culled dans Gamebryo : les
            // NIF de map le portent sur ~50 volumes de collision et
            // quelques doublons, jamais sur le décor rendu.
            if (av.Hidden) { node.Visible = false; node.SetMeta("nif_hidden", true); }

            var billboardHere = av is NifNode { Type: "NiBillboardNode" } bn ? bn.BillboardMode : -1;
            var saved = _billboardMode;
            if (billboardHere >= 0) _billboardMode = billboardHere;
            if (av is NifNode nn) AddKids(node, nn, null);
            foreach (var hg in _groups.Where(g => g.HostBlock == av.Block))
                node.AddChild(MakeSkeleton(hg));
            _billboardMode = saved;
            _made.TryAdd(av.Block, node);
            return node;
        }

        void Tag(Node3D node, NifAv av)
        {
            node.SetMeta("nif_block", av.Block);
            var ex = Extras(av);
            if (ex != null) node.SetMeta("extras", ex);
        }

        void AddKids(Node3D parent, NifNode nn, Skeleton3D? skelForAttach)
        {
            var kids = KidsOf(nn, _nif).ToList();
            var isSwitch = nn.Type == "NiSwitchNode" && nn.SwitchIndex < kids.Count;
            var isLod = nn.Type == "NiLODNode" && nn.LodRanges.Length > 0;
            // Les noms sémantiques (« block », « terrain »…) n'ont de
            // sens que sous « Allblock »/« Roomblock* » ou un
            // NiRoom/NiRoomGroup — ailleurs ce sont des noms de décor
            // ordinaires.
            var scope = _inBlockScope ||
                        nn.Type is "NiRoom" or "NiRoomGroup";
            for (var i = 0; i < kids.Count; i++)
            {
                if (kids[i] is NifGeom { Shape: { } ks } && ks.Skin != null && _shapeGroup.ContainsKey(ks.BlockIndex))
                    continue;
                if (_groups.Any(g => g.HostBlock == nn.Block && g.Bones.Contains(kids[i].Block))) continue;           // placé sous le Skeleton3D
                var sg = _colGroup; var ss = _inBlockScope;
                EnterGroup(kids[i], scope);
                var c = Make(kids[i]);
                if (c != null && _colGroup is 1 or 2 or 3)
                    c.Visible = false;      // conteneur de collision : tout le groupe est masqué
                _colGroup = sg; _inBlockScope = ss;
                if (c == null) continue;
                parent.AddChild(c);
                if (isSwitch && i != nn.SwitchIndex) HideTree(c);
                if (isLod && i < nn.LodRanges.Length) LodRange(c, nn.LodRanges[i]);
            }
        }

        /// <summary>Entrée dans un groupe sémantique éditeur — appelé
        /// sur chaque enfant ; les noms non-groupes héritent le groupe
        /// courant (les « block » contiennent des sous-nœuds).</summary>
        void EnterGroup(NifAv kid, bool scope)
        {
            var low = kid.Name;
            if (low.Equals("allblock", StringComparison.OrdinalIgnoreCase) ||
                low.StartsWith("roomblock", StringComparison.OrdinalIgnoreCase))
            {
                _inBlockScope = true;
                return;
            }
            if (!scope) return;
            // « DummyWallNN_NDLWL » (enveloppe murale d'une salle,
            // directement sous NiRoom) : le client original pose le
            // flag Hidden sur tout « _NDLWL » (FUN_00b08550) — géométrie
            // d'éditeur, ni rendue ni collidée.
            var isRoomShell = low.Contains("ndlwl",
                                  StringComparison.OrdinalIgnoreCase) ||
                              low.StartsWith("dummywall",
                                  StringComparison.OrdinalIgnoreCase);
            var g = isRoomShell ? 5 : low.ToLowerInvariant() switch
            {
                "block" or "nocamerablock" => 1,   // bloqueurs mouvement
                "camerablock" => 2,                // anti-caméra seul
                "floor" => 3,                      // proxy de sol masqué
                "terrain" => 4,                    // sol visible marchable
                // Listes de RENDU sans collision (rf_game : « noblock »
                // → liste 0, « enhide » → 1, « waternofog » → 2,
                // « nofog »/« nofog3 »/« lownofog » → 3-5,
                // « noalphanoblock »/« noalphanoblock3 » → 8-9,
                // « special » → 0xe) : coquilles de salles opaques,
                // surfaces d'eau, émetteurs d'effets — jamais des
                // colliders ni des helpers à masquer.
                "noblock" or "enhide" or "water" or "waternofog" or
                "nofog" or "nofog3" or "lownofog" or "noalphanoblock" or
                "noalphanoblock3" or "special" => 5,
                _ => -1,
            };
            if (g >= 0) _colGroup = g;
        }

        /// <summary>Metas de collision du groupe courant sur le mesh —
        /// « both » = coquille block (murs + sol), « floor » =
        /// floor/terrain. Le conteneur « camerablock » est déjà masqué
        /// sans collider (notre caméra n'a pas de collision dédiée).</summary>
        void ApplyGroup(MeshInstance3D mi, NifShape s)
        {
            switch (_colGroup)
            {
                case 1:
                    mi.SetMeta("collision_helper", true);
                    mi.SetMeta("helper_class", "both");
                    break;
                case 3:
                case 4:
                    mi.SetMeta("collision_helper", true);
                    mi.SetMeta("helper_class", "floor");
                    break;
                case 5:
                    // Groupe de rendu (« noalphanoblock », « water »,
                    // « enhide »…) : visible ET jamais collider — le
                    // meta interdit la collecte helper (noms
                    // « Object »/« Editable Poly » des coquilles).
                    mi.SetMeta("no_collision", true);
                    break;
            }
            // « block.dds » = texture marqueur des proxies de collision
            // Gamebryo : masqué où qu'il soit (décompilé rf_game) et
            // sol marchable par défaut hors groupe explicite.
            if (s.TextureFile != null &&
                s.TextureFile.Equals("block.dds",
                    StringComparison.OrdinalIgnoreCase))
            {
                mi.Visible = false;
                mi.SetMeta("nif_hidden", true);
                if (_colGroup == 0)
                {
                    mi.SetMeta("collision_helper", true);
                    mi.SetMeta("helper_class", "floor");
                }
            }
        }

        static void HideTree(Node3D n)
        {
            n.Visible = false;
            foreach (var c in n.GetChildren()) if (c is Node3D c3) HideTree(c3);
        }

        static void LodRange(Node n, System.Numerics.Vector2 r)
        {
            if (n is GeometryInstance3D g)
            {
                g.VisibilityRangeBegin = r.X;
                g.VisibilityRangeEnd = r.Y > 1e30f ? 0f : r.Y;
            }
            foreach (var c in n.GetChildren()) LodRange(c, r);
        }

        // ---- squelette -----------------------------------------------------
        Node3D MakeSkeleton(NifSkelGroup g)
        {
            var sk = new Skeleton3D { Name = Unique("Skeleton3D") };
            _skels[g] = sk;
            var names = new Dictionary<int, string>();
            _boneNames[g] = names;
            var idx = new Dictionary<int, int>();
            foreach (var b in g.Order)
            {
                var av = _nif.AvByBlock[b];
                var nm = Unique(av.Name.Length > 0 ? av.Name : av.Type);
                names[b] = nm;
                _boneRef.TryAdd(b, (sk, nm));
                var i = sk.GetBoneCount();
                sk.AddBone(nm);
                idx[b] = i;
                if (av.ParentBlock >= 0 && idx.TryGetValue(av.ParentBlock, out var pi)) sk.SetBoneParent(i, pi);
                sk.SetBoneRest(i, Xf(av.BindLocal));
            }
            sk.ResetBonePoses();
            // Nœuds non-os sous un os → BoneAttachment3D du même nom.
            foreach (var b in g.Order)
            {
                var kids = _nif.AvByBlock[b] is NifNode n ? KidsOf(n, _nif).Where(k => !g.Bones.Contains(k.Block)).ToList() : new();
                if (kids.Count == 0) continue;
                var made = new List<Node3D>();
                foreach (var k in kids)
                {
                    if (k is NifGeom { Shape: { } s } && s.Skin != null && _shapeGroup.TryGetValue(s.BlockIndex, out var sg) && sg == g)
                        continue;          // shape skinné de ce squelette : placé sous Skeleton3D
                    if (Make(k) is { } c) made.Add(c);
                }
                if (made.Count == 0) continue;
                var ba = new BoneAttachment3D { Name = names[b] };
                sk.AddChild(ba);
                ba.BoneName = names[b];
                foreach (var c in made) ba.AddChild(c);
            }
            // Meshes skinnés de ce squelette.
            foreach (var s in _nif.Shapes)
            {
                if (s.Skin == null || !_shapeGroup.TryGetValue(s.BlockIndex, out var sg) || sg != g) continue;
                var mesh = MeshFor(s, rigid: false);
                if (mesh == null) continue;
                var mi = new MeshInstance3D { Name = Unique(s.Name.Length > 0 ? s.Name : "Mesh"), Mesh = mesh };
                mi.SetSurfaceOverrideMaterial(0, MaterialFor(s));
                sk.AddChild(mi);
                var skin = new Skin();
                for (var i = 0; i < s.Skin.BoneBlocks.Length; i++)
                    skin.AddNamedBind(names[s.Skin.BoneBlocks[i]], Xf(s.Skin.BoneSkin[i]));
                mi.Skin = skin;
                mi.Skeleton = new NodePath("..");
                if (s.Node != null) { mi.SetMeta("nif_block", s.Node.Block); if (Extras(s.Node) is { } ex) mi.SetMeta("extras", ex); if (s.Node.Hidden) { mi.Visible = false; mi.SetMeta("nif_hidden", true); } }
            }
            sk.SetMeta("nif_block", g.RootBlock);
            return sk;
        }

        // ---- géométrie -----------------------------------------------------
        Material MaterialFor(NifShape s)
        {
            var m = NifMaterials.For(s, _prefix, _mats);
            if (s_billboard && _billboardMode >= 0 && _billboardMode != 2 && _billboardMode != 4)
            {
                m = (StandardMaterial3D)m.Duplicate();
                m.BillboardMode = _billboardMode == 1 ? BaseMaterial3D.BillboardModeEnum.FixedY
                                                      : BaseMaterial3D.BillboardModeEnum.Enabled;
                m.BillboardKeepScale = true;
            }
            return m;
        }

        ArrayMesh? MeshFor(NifShape s, bool rigid)
        {
            if (_meshes.TryGetValue(s, out var cached) && rigid == (s.Skin == null || cached?.GetMeta("rigid", true).AsBool() == true))
                return cached;
            var mesh = Create(s, rigid);
            if (rigid) _meshes[s] = mesh;
            return mesh;
        }

        static ArrayMesh? Create(NifShape s, bool rigid)
        {
            if (s.Indices.Length < 3) return null;
            var skinned = !rigid && s.Skin != null && s.LocalVertices != null;
            Vector3[] pos = skinned ? s.LocalVertices! : s.Vertices;
            var n = pos.Length;
            if (n == 0) return null;
            Vector3[]? nrm = skinned ? s.LocalNormals : s.Normals;
            // Rigide ou skin dégradé : sommets monde → espace local du shape.
            Matrix4x4 inv = Matrix4x4.Identity;
            var useLocal = !skinned;
            if (useLocal)
            {
                if (s.Skin != null && s.LocalVertices != null) { pos = s.LocalVertices; nrm = s.LocalNormals; useLocal = false; }
                else if (!Matrix4x4.Invert(s.World, out inv)) { inv = Matrix4x4.Identity; useLocal = false; }
            }
            var v = new Godot.Vector3[n];
            for (var i = 0; i < n; i++)
            {
                var p = useLocal ? Vector3.Transform(pos[i], inv) : pos[i];
                v[i] = new Godot.Vector3(p.X, p.Y, p.Z);
            }
            Godot.Vector3[] nn;
            if (nrm != null && nrm.Length == n)
            {
                nn = new Godot.Vector3[n];
                var mn = inv; mn.Translation = Vector3.Zero;
                for (var i = 0; i < n; i++)
                {
                    var q = useLocal ? Vector3.TransformNormal(nrm[i], mn) : nrm[i];
                    var l = q.Length();
                    if (l > 1e-8f) q /= l;
                    nn[i] = new Godot.Vector3(q.X, q.Y, q.Z);
                }
            }
            else nn = SmoothNormals(v, s.Indices);
            // glTF CCW → Godot CW : l'import inverse l'ordre des indices.
            var idx = new int[s.Indices.Length / 3 * 3];
            for (var i = 0; i < idx.Length; i += 3)
            {
                idx[i] = s.Indices[i]; idx[i + 1] = s.Indices[i + 2]; idx[i + 2] = s.Indices[i + 1];
            }
            var arr = new GArray();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = v;
            arr[(int)Mesh.ArrayType.Normal] = nn;
            var uv0 = s.Uvs ?? (s.UvSets.Length > 0 ? s.UvSets[0] : null);
            if (uv0 != null && uv0.Length == n)
                arr[(int)Mesh.ArrayType.TexUV] = uv0.Select(u => new Godot.Vector2(u.X, u.Y)).ToArray();
            if (s.UvSets.Length > 1 && s.UvSets[1].Length == n)
                arr[(int)Mesh.ArrayType.TexUV2] = s.UvSets[1].Select(u => new Godot.Vector2(u.X, u.Y)).ToArray();
            // NiVertexColorProperty SourceMode 0 (SRC_IGNORE) : le natif ignore COLOR_0. Les
            // plantes UD52x portent des couleurs noires + SRC_IGNORE : les multiplier rendait
            // les meshes opaques (toon) entièrement noirs.
            if (s.Colors != null && s.Colors.Length == n && s.VertexColor?.SourceMode != 0)
                arr[(int)Mesh.ArrayType.Color] = s.Colors.Select(c => new Color(c.X, c.Y, c.Z, c.W)).ToArray();
            if (skinned)
            {
                arr[(int)Mesh.ArrayType.Bones] = s.Skin!.Joints.Select(j => (int)j).ToArray();
                arr[(int)Mesh.ArrayType.Weights] = s.Skin.Weights;
            }
            arr[(int)Mesh.ArrayType.Index] = idx;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            mesh.SetMeta("rigid", !skinned);
            return mesh;
        }

        static Godot.Vector3[] SmoothNormals(Godot.Vector3[] v, int[] ix)
        {
            var nn = new Godot.Vector3[v.Length];
            for (var i = 0; i + 2 < ix.Length; i += 3)
            {
                int a = ix[i], b = ix[i + 1], c = ix[i + 2];
                if ((uint)a >= v.Length || (uint)b >= v.Length || (uint)c >= v.Length) continue;
                var f = (v[b] - v[a]).Cross(v[c] - v[a]);
                nn[a] += f; nn[b] += f; nn[c] += f;
            }
            for (var i = 0; i < nn.Length; i++)
                nn[i] = nn[i].LengthSquared() > 1e-12f ? nn[i].Normalized() : Godot.Vector3.Up;
            return nn;
        }

        // ---- animation embarquée (NiTransformController sur les nœuds) -----
        void BuildEmbeddedAnimation(Node3D root)
        {
            Animation? anim = null;
            float len = 0f;
            foreach (var av in _nif.AvByBlock.Values.OrderBy(a => a.Block))
            {
                foreach (var c in av.ControllerList)
                {
                    if (c.Type is not ("NiTransformController" or "NiKeyframeController" or "NiMultiTargetTransformController")) continue;
                    var td = c.Interpolator?.Transform;
                    if (td == null) continue;
                    string? basePath; bool bone = false;
                    if (_boneRef.TryGetValue(av.Block, out var br))
                    { basePath = root.GetPathTo(br.Sk) + ":" + br.Bone; bone = true; }
                    else if (_made.TryGetValue(av.Block, out var nd)) basePath = root.GetPathTo(nd);
                    else continue;
                    anim ??= new Animation();
                    len = Math.Max(len, AddTracks(anim, basePath!, bone, td));
                }
            }
            if (anim == null) return;
            anim.Length = len;
            var lib = new AnimationLibrary();
            lib.AddAnimation("embedded", anim);
            var ap = new AnimationPlayer { Name = "AnimationPlayer" };
            root.AddChild(ap);
            ap.AddAnimationLibrary("", lib);
        }

        static float AddTracks(Animation a, string path, bool bone, NifTransformData td)
        {
            float len = 0;
            // glTF/convert : clés brutes, interpolation linéaire.
            var rotT = new List<float>(); var rotV = new List<Godot.Quaternion>();
            if (td.RotType == NifKeyType.XyzRotation && td.XyzRot is { Length: >= 3 })
            {
                var times = td.XyzRot.SelectMany(k => k.Times).Distinct().OrderBy(t => t).ToList();
                if (times.Count > 4096) times = times.Where((_, i) => i % (times.Count / 4096 + 1) == 0).ToList();
                foreach (var t in times)
                {
                    float rx = Ev(td.XyzRot[0], t), ry = Ev(td.XyzRot[1], t), rz = Ev(td.XyzRot[2], t);
                    // file : Rx*Ry*Rz (row-vector) → quat qz*qy*qx en colonne (convert.py)
                    var qx = new Godot.Quaternion(Mathf.Sin(rx / 2), 0, 0, Mathf.Cos(rx / 2));
                    var qy = new Godot.Quaternion(0, Mathf.Sin(ry / 2), 0, Mathf.Cos(ry / 2));
                    var qz = new Godot.Quaternion(0, 0, Mathf.Sin(rz / 2), Mathf.Cos(rz / 2));
                    rotT.Add(t); rotV.Add(qz * qy * qx);
                }
            }
            else
                for (var i = 0; i < td.RotTimes.Length; i++)
                {
                    var q = td.Rot[i]; rotT.Add(td.RotTimes[i]); rotV.Add(new Godot.Quaternion(q.X, q.Y, q.Z, q.W));
                }
            if (rotT.Count > 0)
            {
                var t = a.AddTrack(Animation.TrackType.Rotation3D); a.TrackSetPath(t, path);
                for (var i = 0; i < rotT.Count; i++) { a.RotationTrackInsertKey(t, rotT[i], rotV[i].Normalized()); len = Math.Max(len, rotT[i]); }
            }
            if (td.Pos.Count > 0)
            {
                var t = a.AddTrack(Animation.TrackType.Position3D); a.TrackSetPath(t, path);
                for (var i = 0; i < td.Pos.Count; i++)
                { var v = td.Pos.Values[i]; a.PositionTrackInsertKey(t, td.Pos.Times[i], new Godot.Vector3(v.X, v.Y, v.Z)); len = Math.Max(len, td.Pos.Times[i]); }
            }
            if (td.Scale.Count > 0)
            {
                var t = a.AddTrack(Animation.TrackType.Scale3D); a.TrackSetPath(t, path);
                for (var i = 0; i < td.Scale.Count; i++)
                { var v = td.Scale.Values[i]; a.ScaleTrackInsertKey(t, td.Scale.Times[i], new Godot.Vector3(v, v, v)); len = Math.Max(len, td.Scale.Times[i]); }
            }
            return len;
        }

        static float Ev(NifKeys<float> k, float t)
        {
            if (k.Count == 0) return 0f;
            if (t <= k.Times[0]) return k.Values[0];
            for (var i = 1; i < k.Count; i++)
                if (t <= k.Times[i])
                {
                    var f = k.Times[i] == k.Times[i - 1] ? 0f : (t - k.Times[i - 1]) / (k.Times[i] - k.Times[i - 1]);
                    return k.Values[i - 1] + (k.Values[i] - k.Values[i - 1]) * f;
                }
            return k.Values[^1];
        }

        // ---- extras --------------------------------------------------------
        GDict? Extras(NifAv av)
        {
            var d = new GDict();
            foreach (var xb in av.ExtraBlocks)
            {
                if (!_nif.ExtraByBlock.TryGetValue(xb, out var x)) continue;
                switch (x.Type)
                {
                    case "NiStringExtraData": d[x.Name.Length > 0 ? x.Name : "string"] = x.StringValue; break;
                    case "NiIntegerExtraData": d[x.Name.Length > 0 ? x.Name : "int"] = (double)x.IntValue; break;
                    case "NiFloatExtraData": d[x.Name.Length > 0 ? x.Name : "float"] = (double)x.FloatValue; break;
                    case "NiBooleanExtraData": d[x.Name.Length > 0 ? x.Name : "bool"] = x.IntValue != 0; break;
                    case "NiIntegersExtraData":
                        d[x.Name.Length > 0 ? x.Name : "ints"] = new GArray(x.Ints.Select(i => Variant.From((double)i)).ToArray()); break;
                    case "NiTextKeyExtraData":
                        var ka = new GArray();
                        foreach (var k in x.TextKeys) ka.Add(new GDict { ["time"] = (double)k.Time, ["value"] = k.Text });
                        d[x.Name.Length > 0 ? x.Name : "textkeys"] = ka; break;
                }
            }
            switch (av.Type)
            {
                case "NiBillboardNode": d["billboard"] = true; break;
                case "NiParticleSystem": case "NiParticles": d["psys"] = true; break;
                case "NiLODNode": d["lod_node"] = true; break;
                case "NiPortal": d["portal"] = true; break;
                case "NiRoom": case "NiRoomGroup": d[av.Type] = true; break;
            }
            if (av.Type.EndsWith("Light") || av.Type == "NiCamera") d[av.Type] = true;
            return d.Count > 0 ? d : null;
        }
    }
}
