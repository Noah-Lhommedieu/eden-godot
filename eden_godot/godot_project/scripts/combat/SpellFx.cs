using Godot;
using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Player;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Effets visuels de sorts (lot « anims de skill »). Modèles
/// <c>effect/model/S&lt;code&gt;.glb</c> choisis par C_Spell
/// (<see cref="SpellFxTable"/>) :
///   - cast  (col 5, « E10121 » → S10121) : posé sous le lanceur à
///     278 (autre joueur) / 279 (self), suit le lanceur, durée du cast ;
///   - impact (col 10, « s10141 ») : posé sur la victime à 288/289
///     (blast) et à 281 (repli, dédoublonné), + hurt01 sur la cible.
/// Le glb porte une anim « embedded » jouée une fois puis le nœud est
/// libéré. Thread réseau → file → thread principal.
/// </summary>
public partial class SpellFx : Node
{
    public static SpellFx? Instance { get; private set; }

    /// <summary>Effets de sort actuellement à l'écran (plafond
    /// Settings « Max FX »).</summary>
    public static int ActiveFx { get; private set; }

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action>
        _pending = new();
    private EdenSession? _session;
    private PlayerController? _player;
    private EntityRegistry? _registry;
    private bool _searched;
    // (spell,target) → date du dernier impact, pour fusionner 281/288/289.
    private readonly Dictionary<long, double> _lastImpact = new();

    private static readonly bool _fxHold = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-hold");

    public override void _Ready()
    {
        Instance = this;
        if (OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs())
            .Contains("--fx-survey"))
            CallDeferred(nameof(Survey));
    }

    /// <summary>--fx-survey : taille (m) max de chaque effet de sort,
    /// animation échantillonnée — détecte les effets démesurés.
    /// Sortie CSV « [fx-survey] code,scale,maxDim ».</summary>
    private void Survey()
    {
        var seen = new HashSet<string>();
        foreach (var (_, c, h) in SpellFxTable.All())
            foreach (var d in new[] { c, h })
            {
                if (d is not { } def || !seen.Add(def.Model)) continue;
                if (!AssetCatalog.TryGetGlb($"effect/model/{def.Model}",
                        out var abs)) continue;
                var fx = AssetCatalog.LoadGlbNode(abs);
                if (fx == null) continue;
                AddChild(fx);
                fx.Scale = Vector3.One * def.Scale;
                var ap = fx.FindChildren("*", "AnimationPlayer", true, false)
                    .OfType<AnimationPlayer>().FirstOrDefault();
                var name = ap?.GetAnimationList().FirstOrDefault();
                float len = name != null ? (float)ap!.GetAnimation(name).Length : 0f;
                float best = 0f;
                for (var k = 0; k < 6; k++)
                {
                    if (ap != null && name != null)
                    {
                        ap.Play(name);
                        ap.Seek(len * k / 5f, true);
                    }
                    var box = new Aabb();
                    var first = true;
                    foreach (var n in fx.FindChildren("*", "MeshInstance3D",
                                 true, false))
                    {
                        if (n is not MeshInstance3D mi || mi.Mesh == null)
                            continue;
                        var b = fx.GlobalTransform.AffineInverse() *
                                mi.GlobalTransform * mi.Mesh.GetAabb();
                        box = first ? b : box.Merge(b);
                        first = false;
                    }
                    if (!first)
                        best = Math.Max(best,
                            Math.Max(box.Size.X, Math.Max(box.Size.Y,
                                box.Size.Z)) * def.Scale);
                }
                GD.Print($"[fx-survey] {def.Model},{def.Scale:0.##}," +
                         $"{best:0.0}");
                fx.QueueFree();
            }
        GD.Print("[fx-survey] fin");
        GetTree().Quit();
    }

    // --fx-show=<spellId,...> : rejoue cast+impact des sorts sur le
    // joueur toutes les 4 s (preuve visuelle Flameshot sans combat).
    private readonly List<int> _showSpells = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs())
        .Where(a => a.StartsWith("--fx-show="))
        .SelectMany(a => a[10..].Split(','))
        .Select(x => int.TryParse(x, out var v) ? v : 0)
        .Where(v => v > 0).ToList();
    private double _showTimer = -8;
    private int _showIdx;
    private bool _townSent;
    private static readonly bool _fxTown = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-town");

    public override void _Process(double delta)
    {
        if (_fxTown && !_townSent && _session?.Zone?.IsConnected == true &&
            _player != null)
        {
            // --fx-town : preuve visuelle sans mobs/clones alentour.
            _townSent = true;
            _ = _session.SendZoneAsync(new ZoneTextCommand
            { Text = "//transport_node 1" });
            _showTimer = -25;
        }
        if (_showSpells.Count > 0 && _player != null)
        {
            _showTimer += delta;
            if (_showTimer >= 4)
            {
                _showTimer = 0;
                var id = _showSpells[_showIdx++ % _showSpells.Count];
                GD.Print($"[fx-show] spell #{id} cast={SpellFxTable.CastFx(id)} " +
                         $"hit={SpellFxTable.HitFx(id)}");
                Spawn(SpellFxTable.CastFx(id), _player, 3.5f);
                Spawn(SpellFxTable.HitFx(id), _player, 3.5f);
            }
        }
        if (_session == null && SessionCarry.Session is { } s) Bind(s);
        if (!_searched)
        {
            _searched = true;
            _player = GetTree().Root.FindChild("Player", true, false)
                as PlayerController;
            _registry = GetTree().Root.FindChild("NetEntities", true, false)
                as EntityRegistry;
        }
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[spellfx] {ex.Message}"); }
        }
    }

    private void Bind(EdenSession s)
    {
        _session = s;
        s.OnZone(278, r =>
        {
            var m = new ZoneClientSpellCast();
            m.Deserialize(r);
            _pending.Enqueue(() => OnOtherCast(m));
        });
        s.OnZone(279, r =>
        {
            var m = new ZoneClientSpellCastOK();
            m.Deserialize(r);
            _pending.Enqueue(() => OnSelfCast(m));
        });
        s.OnZone(281, r =>
        {
            var m = new ZoneClientSpellHitToLife();
            m.Deserialize(r);
            _pending.Enqueue(() => Impact((ushort)m.SpellId,
                (uint)m.Target, true));
        });
        s.OnZone(288, r =>
        {
            var m = new ZoneClientSpellBlastOK();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                // Le 288 n'arrive qu'au lanceur. Formes directionnelles
                // (secteur/croix/direction) : le visuel de blast émane
                // DU LANCEUR vers la pointe (X,Y) — sommet du cône /
                // centre de la croix / origine de l'onde natifs — pas
                // posé sur la victime.
                var id = (ushort)m.SpellId;
                if (Directional(id))
                    DirectionalBlast(id, PlayerEden(), (m.X, m.Y));
                else
                    Impact(id, (uint)m.VictimHandle, false);
            });
        });
        s.OnZone(289, r =>
        {
            var m = new ZoneClientSpellBlastToLife();
            m.Deserialize(r);
            // Layout réel : champ 3 = LANCEUR, champ 7 = CIBLE, champ 11 = kfId de blast résolu.
            _pending.Enqueue(() =>
            {
                RemoteBlastAnim((uint)m.Caster, m.BlastIdx, (ushort)m.SpellId);
                var id = (ushort)m.SpellId;
                if (Directional(id))
                    DirectionalBlast(id, (m.X, m.Y),
                        (m.Field8, m.Field9));
                else
                    Impact(id, (uint)m.TargetHandle, false);
            });
        });
        s.OnZone(297, r =>
        {
            var m = new ZoneClientSpellBlastToLocation();
            m.Deserialize(r);
            _pending.Enqueue(() => OnGroundBlast(m));
        });
        GD.Print("[spellfx] binds 278/279/281/288/289/297 posés");
    }

    /// <summary>297 SpellBlastToLocation : effet de blast posé au point
    /// d'ancrage, orienté sur l'axe lanceur → point.
    /// Cercle au sol : ancrage = point visé (centre). Formes
    /// directionnelles (secteur/croix/direction) : ancrage = POSITION DU
    /// LANCEUR — le sommet du cône, le centre de la croix et l'origine de
    /// l'onde sont au lanceur dans la géométrie native
    /// (HitTest @0x84fb410/@0x84f5020/@0x84f6ad0) — le point ne sert
    /// qu'à l'orientation.</summary>
    private void OnGroundBlast(ZoneClientSpellBlastToLocation m)
    {
        var id = (ushort)m.SpellId;
        var dir = Directional(id);
        // Ancrage : lanceur (src) pour les formes directionnelles, point
        // visé (dst) pour le cercle au sol.
        var px = dir ? m.SrcX : m.DstX;
        var py = dir ? m.SrcY : m.DstY;
        // Hauteur : sol « marchable » du joueur (même source que ses pas), repli registre puis Y joueur.
        var hp = _player?.GroundHeight?.Invoke(px, -py);
        var hr = _registry?.GroundHeight?.Invoke(px, -py);
        var h = hp ?? hr ?? _player?.GlobalPosition.Y ?? 0f;
        var (ax, ay) = SpellZone.AxisTo(m.SrcX, m.SrcY, m.DstX, m.DstY, 0f);
        var anchor = new Node3D { Name = $"GroundFx_{id}" };
        AddChild(anchor);
        anchor.GlobalPosition = new Vector3(px, h, -py);
        anchor.Rotation = new Vector3(0f, MathF.Atan2(-ax, ay), 0f);   // même formule qu'EdenDir.ToYaw
        GD.Print($"[spellfx] zone #{id} forme={(CastShape)SpellCatalog.Shape(id)} " +
                 $"src=({m.SrcX:0.#},{m.SrcY:0.#}) dst=({m.DstX:0.#},{m.DstY:0.#}) " +
                 $"ancre=({px:0.#},{py:0.#}) h={h:0.#} " +
                 $"(marchable={hp?.ToString("0.#") ?? "-"} registre={hr?.ToString("0.#") ?? "-"} joueurY={_player?.GlobalPosition.Y:0.#}) " +
                 $"axe=({ax:0.##},{ay:0.##}) rayon={SpellZone.AreaRadius(SpellCatalog.RadiusUnits(id)):0.#}");
        Spawn(SpellFxTable.HitFx(id), anchor, 0f);
        // Lanceur distant : le 297 ne porte pas son handle. Si la source
        // est le joueur local (≤ 1,5 u), sa gestuelle vient déjà du 288/
        // 297 via SkillCast.PlayCastAnim — sinon un voisin au même point
        // rejouerait SON anim à sa place. L'entité la plus proche de la
        // source (joueur OU mob) joue alors la gestuelle de blast.
        var selfD = _player != null
            ? new Vector2(_player.GlobalPosition.X - m.SrcX,
                _player.GlobalPosition.Z + m.SrcY).LengthSquared() : 99f;
        if (_registry != null && selfD > 2.25f)
        {
            RemoteEntity? best = null; var bestD = 2.25f;
            foreach (var e in _registry.Entities.Values)
            {
                // Joueurs (kind 0, corps 1 lettre) ou mobs (kind 12) avec
                // un modèle — les trésors (kind 0, stem long) exclus.
                var ok = e.Kind == 0 && e.ModelStem.Length == 1 ||
                         e.Kind == 12 && e.ModelStem.Length > 0;
                if (!ok || !IsInstanceValid(e)) continue;
                var d = new Vector2(e.GlobalPosition.X - m.SrcX, e.GlobalPosition.Z + m.SrcY).LengthSquared();
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best != null) RemoteBlastAnim(best.Handle, m.BlastId, id);
        }
        GetTree().CreateTimer(6f).Timeout += () =>
        { if (IsInstanceValid(anchor)) anchor.QueueFree(); };
    }

    /// <summary>Vrai pour les formes ancrées au lanceur (sommet du
    /// secteur, centre de la croix, origine de l'onde) — le point ne
    /// sert qu'à l'orientation.</summary>
    private static bool Directional(int spellId) =>
        (CastShape)SpellCatalog.Shape(spellId) is CastShape.Direction
            or CastShape.Cross or CastShape.Sector;

    /// <summary>Position Eden (x,y sol) du lanceur local.</summary>
    private (float X, float Y) PlayerEden() => _player != null
        ? WorldConv.ToEdenGround(_player.GlobalPosition) : (0f, 0f);

    /// <summary>Effet de blast directionnel ancré à la position du
    /// lanceur (srcX,srcY Eden), orienté vers la pointe (dstX,dstY) —
    /// formes 2/5/6 (secteur/croix/direction) : cône/croix/onde
    /// émanent du lanceur. Utilisé par le 288 (lanceur self — src =
    /// position du joueur, dst = champ position du paquet) et le 289
    /// (témoin — src/dst du paquet).</summary>
    private void DirectionalBlast(int spellId, (float X, float Y) src,
        (float X, float Y) dst)
    {
        var hp = _player?.GroundHeight?.Invoke(src.X, -src.Y);
        var hr = _registry?.GroundHeight?.Invoke(src.X, -src.Y);
        var h = hp ?? hr ?? _player?.GlobalPosition.Y ?? 0f;
        var (ax, ay) = SpellZone.AxisTo(src.X, src.Y, dst.X, dst.Y, 0f);
        var anchor = new Node3D { Name = $"DirFx_{spellId}" };
        AddChild(anchor);
        anchor.GlobalPosition = new Vector3(src.X, h, -src.Y);
        anchor.Rotation = new Vector3(0f, MathF.Atan2(-ax, ay), 0f);
        GD.Print($"[verify] blast dir #{spellId} " +
                 $"forme={(CastShape)SpellCatalog.Shape(spellId)} " +
                 $"src=({src.X:0.#},{src.Y:0.#}) dst=({dst.X:0.#},{dst.Y:0.#}) " +
                 $"axe=({ax:0.##},{ay:0.##}) h={h:0.#}");
        Spawn(SpellFxTable.HitFx(spellId), anchor, 0f);
        GetTree().CreateTimer(6f).Timeout += () =>
        { if (IsInstanceValid(anchor)) anchor.QueueFree(); };
    }

    /// <summary>Gestuelle de blast d'un lanceur distant : kfId résolu du paquet (repli : col3 du sort).</summary>
    private void RemoteBlastAnim(uint caster, int blastIdx, int spellId)
    {
        if (_registry == null || !_registry.Entities.TryGetValue(caster, out var e)) return;
        var kf = blastIdx > 0 ? blastIdx : SpellCatalog.ActionKf(spellId);
        var anim = MotionTable.Shared.Lookup(e.ModelStem, kf)
            ?? MotionTable.Shared.Lookup(e.ModelStem, SpellCatalog.ActionKf(spellId));
        if (anim != null) e.PlayOnce(anim);
    }

    private void OnSelfCast(ZoneClientSpellCastOK m)
    {
        if (m.Ok == 0 || _player == null) return;
        var id = (ushort)m.SpellId;
        Spawn(SpellFxTable.CastFx(id), _player, m.CastTimeDs / 10f);
    }

    private void OnOtherCast(ZoneClientSpellCast m)
    {
        var id = (ushort)m.SpellId;
        var caster = (uint)m.Caster;
        if (_registry == null ||
            !_registry.Entities.TryGetValue(caster, out var e)) return;
        // Anim d'action du lanceur distant : sort instantané → tout de suite ; sort TEMPORISÉ → la
        // gestuelle part au blast (289/297, kfId résolu), pas au début de l'incantation (le serveur
        // décale la résolution de swing = temps 'hit' à partir du blast).
        if (SpellCatalog.CastDs(id) == 0)
        {
            var anim = MotionTable.Shared.Lookup(e.ModelStem,
                SpellCatalog.ActionKf(id));
            if (anim != null) e.PlayOnce(anim);
        }
        Spawn(SpellFxTable.CastFx(id), e,
            SpellCatalog.CastDs(id) / 10f);
    }

    /// <summary>Effet d'impact sur la victime + réaction hurt01.</summary>
    private void Impact(int spellId, uint target, bool hitMsg)
    {
        var key = ((long)spellId << 32) | target;
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastImpact.TryGetValue(key, out var t) && now - t < 0.4)
            return;
        _lastImpact[key] = now;
        if (_lastImpact.Count > 256) _lastImpact.Clear();

        Node3D? host = null;
        if (_registry != null &&
            _registry.Entities.TryGetValue(target, out var e))
        {
            host = e;
            if (hitMsg && !e.Dying && SpellCatalog.TargetType(spellId) == 1)
                e.PlayOnce("hurt01");
        }
        else if (target == (_registry?.SelfCharId ?? 0u)) host = _player;
        if (host == null) return;
        Spawn(SpellFxTable.HitFx(spellId), host, 0f);
    }

    /// <summary>Animation de matériau d'une couche d'effet (extras
    /// <c>fx</c> du glb : NiAlphaController / NiMaterialColorController /
    /// NiTextureTransformController natifs, perdus par glTF). Sans elle
    /// toutes les couches restaient visibles à alpha plein en même temps
    /// (amas de grands sprites).</summary>
    private sealed class Layer
    {
        public BaseMaterial3D Mat = null!;
        public float[][]? Alpha;      // [t, v]
        public float AlphaStop, AlphaFreq = 1f; public int AlphaCyc = 2;
        public float[][]? Color;      // [t, r, g, b]
        public float[][]? UvU, UvV, UvSU, UvSV;
        public float UvStop, UvFreq = 1f; public int UvCyc = 2;
        public Color BaseColor = Colors.White;
    }

    private static float[][]? Keys(Godot.Collections.Dictionary d, string key)
    {
        if (!d.ContainsKey(key)) return null;
        var arr = d[key].AsGodotArray();
        var res = new float[arr.Count][];
        for (var i = 0; i < arr.Count; i++)
        {
            var row = arr[i].AsGodotArray();
            res[i] = new float[row.Count];
            for (var j = 0; j < row.Count; j++)
                res[i][j] = (float)row[j].AsDouble();
        }
        return res;
    }

    /// <summary>Échantillonne des clés [t, v...] au temps t (borné
    /// ou bouclé selon le cycle natif : 0 boucle, 2 clamp).</summary>
    private static float Sample(float[][] k, int comp, float t, float stop,
        float freq, int cyc)
    {
        t *= freq;
        if (cyc == 0 && stop > 0) t %= stop;
        if (t <= k[0][0]) return k[0][comp];
        for (var i = 1; i < k.Length; i++)
            if (t <= k[i][0])
            {
                var u = (t - k[i - 1][0]) /
                        Math.Max(1e-5f, k[i][0] - k[i - 1][0]);
                return Mathf.Lerp(k[i - 1][comp], k[i][comp], u);
            }
        return k[^1][comp];
    }

    private static bool UnderBillboard(Node n, Node root)
    {
        for (var p = n.GetParent(); p != null && p != root; p = p.GetParent())
            if (p.HasMeta("extras") &&
                p.GetMeta("extras").AsGodotDictionary() is { } d &&
                d.ContainsKey("billboard")) return true;
        return false;
    }

    /// <summary>Look natif des effets : additif, sans éclairage ni
    /// écriture de profondeur (le toon-pass les rendait ternes) ;
    /// une <see cref="Layer"/> par couche animée.</summary>
    private static List<Layer> MakeAdditive(Node3D root)
    {
        var layers = new List<Layer>();
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            Godot.Collections.Dictionary? fx = null;
            if (mi.HasMeta("extras") &&
                mi.GetMeta("extras").AsGodotDictionary() is { } ex &&
                ex.ContainsKey("fx"))
                fx = ex["fx"].AsGodotDictionary();
            var bb = UnderBillboard(mi, root);
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.GetActiveMaterial(i) as BaseMaterial3D;
                var m = src != null
                    ? (BaseMaterial3D)src.Duplicate()
                    : new StandardMaterial3D();
                m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                m.NoDepthTest = false;
                m.DisableReceiveShadows = true;
                if (bb && !_noBillboard)
                {
                    m.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
                    m.BillboardKeepScale = true;
                }
                // L'alpha du glb vaut 0 (animé par le KF natif) : forcé
                // à 1 ; les couches à contrôleur d'alpha le recalculent
                // à chaque image (Layer.Alpha), le fondu global vient
                // de Spawn.
                m.AlbedoColor = new Color(m.AlbedoColor, 1f);
                var l = new Layer { Mat = m, BaseColor = m.AlbedoColor };
                if (fx != null && !_noLayers)
                {
                    l.Alpha = Keys(fx, "a");
                    if (fx.ContainsKey("am"))
                    {
                        var am = fx["am"].AsGodotDictionary();
                        l.AlphaStop = (float)am["stop"].AsDouble();
                        l.AlphaFreq = (float)am["freq"].AsDouble();
                        l.AlphaCyc = am["cyc"].AsInt32();
                    }
                    l.Color = Keys(fx, "c3") ?? Keys(fx, "c1");
                    if (fx.ContainsKey("uv"))
                        foreach (var u in fx["uv"].AsGodotArray())
                        {
                            var ud = u.AsGodotDictionary();
                            var k = Keys(ud, "k");
                            switch (ud["op"].AsInt32())
                            {
                                case 0: l.UvU = k; break;
                                case 1: l.UvV = k; break;
                                case 3: l.UvSU = k; break;
                                case 4: l.UvSV = k; break;
                            }
                            l.UvStop = (float)ud["stop"].AsDouble();
                            l.UvFreq = (float)ud["freq"].AsDouble();
                            l.UvCyc = ud["cyc"].AsInt32();
                        }
                }
                layers.Add(l);
                mi.SetSurfaceOverrideMaterial(i, m);
            }
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        return layers;
    }

    // Diagnostics : --fx-nolayers (sans animation de matériau),
    // --fx-nobillboard.
    private static readonly bool _noLayers = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-nolayers");
    private static readonly bool _noBillboard = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-nobillboard");

    /// <summary>Met à jour alpha/couleur/UV d'une couche au temps t.</summary>
    private static void Apply(Layer l, float t, float fade)
    {
        var a = 1f;
        if (l.Alpha != null)
            a = Mathf.Clamp(Sample(l.Alpha, 1, t, l.AlphaStop, l.AlphaFreq,
                l.AlphaCyc), 0f, 1f);
        var c = l.BaseColor;
        if (l.Color != null)
            c = new Color(
                Mathf.Clamp(Sample(l.Color, 1, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f),
                Mathf.Clamp(Sample(l.Color, 2, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f),
                Mathf.Clamp(Sample(l.Color, 3, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f));
        l.Mat.AlbedoColor = new Color(c, a * fade);
        if (l.UvU != null || l.UvV != null || l.UvSU != null ||
            l.UvSV != null)
        {
            var ox = l.UvU != null
                ? Sample(l.UvU, 1, t, l.UvStop, l.UvFreq, l.UvCyc) : 0f;
            var oy = l.UvV != null
                ? Sample(l.UvV, 1, t, l.UvStop, l.UvFreq, l.UvCyc) : 0f;
            l.Mat.Uv1Offset = new Vector3(ox, oy, 0f);
            if (l.UvSU != null || l.UvSV != null)
                l.Mat.Uv1Scale = new Vector3(
                    l.UvSU != null ? Sample(l.UvSU, 1, t, l.UvStop,
                        l.UvFreq, l.UvCyc) : 1f,
                    l.UvSV != null ? Sample(l.UvSV, 1, t, l.UvStop,
                        l.UvFreq, l.UvCyc) : 1f, 1f);
        }
    }

    /// <summary>EDEN_NATIVE=fx : .nif d'origine + NativeFx (particules, contrôleurs) au lieu du GLB.
    /// Durée : <paramref name="hold"/> ou 3 s ; faux = repli GLB.</summary>
    private bool SpawnNative(SpellFxTable.Fx def, Node3D host, float hold)
    {
        var swLoad = System.Diagnostics.Stopwatch.StartNew();
        var fx = AssetCatalog.LoadNativeScene($"effect/model/{def.Model}", out var nif);
        if (swLoad.Elapsed.TotalMilliseconds > 15)
            GD.Print($"[fx-load] {def.Model} {swLoad.Elapsed.TotalMilliseconds:F0} ms (thread principal)");
        if (fx == null || nif == null) return false;
        host.AddChild(fx);
        fx.Position = Vector3.Zero;
        fx.Scale = Vector3.One * def.Scale;
        var swAtt = System.Diagnostics.Stopwatch.StartNew();
        EdenClient.Native.NativeFx.Attach(fx, nif, new EdenClient.Native.NativeFxOptions
        {
            TexPrefix = "effect/texture/",
            HandleBillboards = System.Environment.GetEnvironmentVariable("EDEN_NATIVE_BB") == "0",
        });
        if (System.Environment.GetEnvironmentVariable("EDEN_LAT_TRACE") == "1")
            GD.Print($"[lat] fx attach {def.Model} {swAtt.Elapsed.TotalMilliseconds:F1} ms");
        float life = Math.Max(hold, _fxHold ? 5f : 3f);
        ActiveFx++;
        GD.Print($"[spellfx] natif {def.Model} x{def.Scale:0.##} sur {host.Name} ({life:0.0}s)");
        GetTree().CreateTimer(life).Timeout += () =>
        {
            ActiveFx = Math.Max(0, ActiveFx - 1);
            if (IsInstanceValid(fx)) World.SlowFree.Free(fx);
        };
        return true;
    }

    /// <summary>Instancie le glb d'effet sous <paramref name="host"/>.
    /// <paramref name="hold"/> &gt; 0 : l'effet vit au moins ce temps
    /// (anim rebouclée pendant le cast).</summary>
    private void Spawn(SpellFxTable.Fx? fxDef, Node3D host, float hold)
    {
        if (fxDef is not { } def || !IsInstanceValid(host)) return;
        using var _pt = EdenClient.Perf.PerfTrace.Scope($"fx:{def.Model}");
        // Settings : « 3D Effect » (coupe tous les modèles d'effets) et
        // « Max FX » (plafond d'effets simultanés ; les siens passent
        // toujours).
        var cfg = EdenClient.Settings.SettingsApplier.Instance?.S;
        if (cfg != null)
        {
            if (!cfg.Fx3D) return;
            if (host != _player && cfg.MaxFx < 60 && ActiveFx >= cfg.MaxFx)
                return;
        }
        var code = def.Model;
        if (EdenClient.Native.NativeMode.Fx && SpawnNative(def, host, hold)) return;
        if (!AssetCatalog.TryGetGlb($"effect/model/{code}", out var abs))
        {
            GD.Print($"[spellfx] modèle absent : {code}");
            return;
        }
        var fx = AssetCatalog.LoadGlbNode(abs);
        if (fx == null) return;
        var layers = MakeAdditive(fx);
        host.AddChild(fx);
        fx.Position = Vector3.Zero;
        fx.Scale = Vector3.One * def.Scale;
        var ap = fx.FindChildren("*", "AnimationPlayer", true, false)
            .OfType<AnimationPlayer>().FirstOrDefault();
        float life = 1.2f;
        if (ap != null)
        {
            var name = ap.GetAnimationList().FirstOrDefault();
            if (name != null)
            {
                var anim = ap.GetAnimation(name);
                life = (float)Math.Clamp(anim.Length, 0.6, 3.0);
                if (hold > life) anim.LoopMode = Animation.LoopModeEnum.Linear;
                ap.Play(name);
            }
        }
        life = Math.Max(life, hold);
        if (_fxHold) life = Math.Max(life, 5f);   // --fx-hold : preuve visuelle
        GD.Print($"[spellfx] {code} x{def.Scale:0.##} sur {host.Name} " +
                 $"({life:0.0}s, {layers.Count} couches, " +
                 $"{layers.Count(l => l.Alpha != null)} alpha)");
        // Fondu d'entrée 0,1 s / sortie sur le dernier quart ; l'alpha
        // propre à chaque couche (contrôleurs natifs) s'y multiplie.
        var tw = fx.CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            var fade = Math.Min(t / 0.1f, 1f) *
                       Math.Clamp((life - t) / (life * 0.25f), 0f, 1f);
            foreach (var l in layers) Apply(l, t, fade);
        }), 0f, life, life);
        ActiveFx++;
        GetTree().CreateTimer(life).Timeout += () =>
        {
            ActiveFx = Math.Max(0, ActiveFx - 1);
            if (IsInstanceValid(fx)) World.SlowFree.Free(fx);
        };
    }
}

/// <summary>Résolution native C_Spell → effet visuel.
///   - col 5 (« E10121 ») = clé <c>dynEffect.ini</c> (col 2 = clé effet,
///     PAS forcément les mêmes chiffres : e20021 → s105721) ;
///   - col 10 (« s10141 ») = clé <c>effect.ini</c> directe ;
///   - <c>effect.ini</c> : col 1 = fichier nif (peut différer de la clé :
///     s90113 → s90112.nif), col 4 = échelle native (FUN_00cffc50,
///     « Effect.ini Scale error »). Ignorer cette table affichait des
///     effets à l'échelle brute du nif (jusqu'à ×6 trop grands) ou
///     le mauvais modèle.</summary>
public static class SpellFxTable
{
    /// <summary>Effet résolu : stem du glb (S&lt;chiffres&gt;) + échelle.</summary>
    public readonly record struct Fx(string Model, float Scale);

    private static Dictionary<int, (Fx? Cast, Fx? Hit)>? _t;
    private static Dictionary<string, Fx>? _effects;
    private static Dictionary<string, string>? _dyn;
    // --fx-legacy : ancien mapping (S + chiffres, échelle 1) — preuve
    // avant/après uniquement.
    private static readonly bool _legacy = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-legacy");

    internal static IEnumerable<(int Id, Fx? Cast, Fx? Hit)> All()
    {
        if (_t == null) Load();
        return _t!.Select(kv => (kv.Key, kv.Value.Cast, kv.Value.Hit));
    }

    public static Fx? CastFx(int spellId) => Get(spellId).Cast;
    public static Fx? HitFx(int spellId) => Get(spellId).Hit;

    private static (Fx? Cast, Fx? Hit) Get(int id)
    {
        if (_t == null) Load();
        return _t!.TryGetValue(id, out var v) ? v : (null, null);
    }

    private static IEnumerable<string[]> IniRows(string name)
    {
        var path = AssetCatalog.ResolveRawAsset("data/db/" + name);
        if (path == null) yield break;
        foreach (var line in Vfs.EdenVfs.ReadLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB")) continue;
            yield return line[(eq + 1)..].Trim().Split(',');
        }
    }

    private static string Stem(string nif)
    {
        var d = nif.LastIndexOf('.');
        return d > 0 ? nif[..d] : nif;
    }

    /// <summary>Clé effect.ini → (modèle, échelle). Clé absente de la
    /// table → modèle de même nom à l'échelle 1 (repli).</summary>
    internal static Fx? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim().ToLowerInvariant();
        if (key is "null" or "0" || key.Length < 2) return null;
        if (!_legacy && _effects!.TryGetValue(key, out var fx)) return fx;
        return new Fx(key, 1f);
    }

    private static void Load()
    {
        _t = new();
        _effects = new();
        _dyn = new();
        try
        {
            foreach (var f in IniRows("effect.ini"))
            {
                if (f.Length < 5) continue;
                var scale = float.TryParse(f[4],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sc) && sc > 0 ? sc : 1f;
                var nif = f[1].Equals("NULL", StringComparison.OrdinalIgnoreCase)
                    ? f[0] : Stem(f[1]);
                _effects[f[0].ToLowerInvariant()] =
                    new Fx(nif.ToLowerInvariant(), scale);
            }
            foreach (var f in IniRows("DynEffect.ini"))
                if (f.Length >= 3)
                    _dyn[f[0].ToLowerInvariant()] = f[2].ToLowerInvariant();
        }
        catch (Exception e)
        { GD.PrintErr($"[spellfx] effect.ini : {e.Message}"); }
        var path = AssetCatalog.ResolveRawAsset("data/db/c_spell.ini")
            ?? AssetCatalog.ResolveRawAsset("data/db/C_Spell.ini");
        if (path == null) return;
        try
        {
            foreach (var row in IniTable.ParseFile(path).Rows)
            {
                var id = IniTable.GetInt(row, 0);
                if (id <= 0) continue;
                var ck = IniTable.GetStr(row, 5).Trim().ToLowerInvariant();
                Fx? c = null;
                if (ck.Length > 1 && ck != "null")
                    c = !_legacy && _dyn.TryGetValue(ck, out var ek) ? Resolve(ek)
                        : Resolve("s" + ck[1..]);
                var h = Resolve(IniTable.GetStr(row, 10));
                if (c != null || h != null) _t[id] = (c, h);
            }
        }
        catch (Exception e) { GD.PrintErr($"[spellfx] c_spell : {e.Message}"); }
    }
}
