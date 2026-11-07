using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>S→C 9060 offre d'entrée de donjon (Solo/Multi, entrées du
/// jour) et 9062 portails du node ; C→S 9061 choix. Paquets custom
/// Reborn (hors protocole natif) : lus sur le thread réseau, repostés
/// sur le thread principal.</summary>
public static class DungeonUi
{
    public const int OfferNcid = 9060, EnterNcid = 9061, PortalsNcid = 9062;

    public sealed record Offer(int SoloNode, int MultiNode, int SoloLeft,
        int MultiLeft, int Max, bool Unlimited);

    public static event Action<Offer>? OfferReceived;
    public static event Action<IReadOnlyList<Vector3>>? PortalsChanged;

    public static void Bind(EdenSession s, Action<Action> post)
    {
        s.OnZone(OfferNcid, r =>
        {
            var solo = r.ReadShort(); var multi = r.ReadShort();
            var o = new Offer(solo, multi, r.ReadByte(), r.ReadByte(),
                r.ReadByte(), r.ReadByte() != 0);
            post(() => OfferReceived?.Invoke(o));
        });
        s.OnZone(PortalsNcid, r =>
        {
            var n = r.ReadByte();
            var list = new List<Vector3>(n);
            for (var i = 0; i < n; i++)
            {
                var x = r.ReadFloat(); var y = r.ReadFloat();
                var z = r.ReadFloat(); r.ReadFloat();       // rayon
                list.Add(WorldConv.ToGodot(x, y, z));
            }
            post(() => PortalsChanged?.Invoke(list));
        });
    }

    /// <summary>C→S 9061 [s16 node].</summary>
    public static void Enter(int node)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(new ZoneDungeonEnter { Node = (short)node },
            System.Threading.CancellationToken.None);
    }
}

/// <summary>C→S 9061 — choix d'intensité.</summary>
public sealed class ZoneDungeonEnter : NetMessage
{
    public short Node { get; set; }
    public override int Opcode => DungeonUi.EnterNcid;
    public override string Name => "Zone.DungeonEnter";
    public override void Serialize(BinWriter s) => s.WriteShort(Node);
    public override void Deserialize(BinReader s) => Node = s.ReadShort();
}

/// <summary>Fenêtre du portail : Solo / Multijoueur + entrées restantes.</summary>
public partial class DungeonEntryWindow : Control
{
    private Panel? _panel;
    private Label? _title, _soloInfo, _multiInfo;
    private Button? _solo, _multi;
    private DungeonUi.Offer? _offer;

    private int _arrivalVersion;

    public override void _Ready()
    {
        Visible = false;
        _arrivalVersion = PortalArrival.Version;
        _panel = new Panel { Name = "__dungeon" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.Position = new Vector2(-230, -120);
        _panel.Size = new Vector2(460, 290);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.06f, 0.16f, 0.96f),
            BorderColor = new Color(0.62f, 0.38f, 0.9f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "Dungeon Portal");
        MouseFilter = MouseFilterEnum.Pass;

        _title = Row(38, 26, "");
        _solo = Btn(66, "Solo", () => Pick(_offer?.SoloNode ?? 0));
        _soloInfo = Row(102, 48, "");
        _multi = Btn(156, "Multiplayer", () => Pick(_offer?.MultiNode ?? 0));
        _multiInfo = Row(192, 48, "");
        var close = new Button { Text = "Close" };
        close.Position = new Vector2(180, 250);
        close.Size = new Vector2(100, 28);
        close.Pressed += () => Visible = false;
        _panel.AddChild(close);
        DungeonUi.OfferReceived += Show;
    }

    public override void _ExitTree() => DungeonUi.OfferReceived -= Show;

    private Label Row(float y, float h, string t)
    {
        var l = new Label { Text = t };
        l.Position = new Vector2(20, y);
        l.Size = new Vector2(420, h);
        l.AutowrapMode = TextServer.AutowrapMode.Word;
        _panel!.AddChild(l);
        return l;
    }

    private Button Btn(float y, string t, Action a)
    {
        var b = new Button { Text = t };
        b.Position = new Vector2(20, y);
        b.Size = new Vector2(420, 32);
        b.Pressed += a;
        _panel!.AddChild(b);
        return b;
    }

    private void Show(DungeonUi.Offer o)
    {
        _offer = o;
        _arrivalVersion = PortalArrival.Version;
        _title!.Text = Data.TextResolver.NodeName(o.SoloNode) ??
            Data.TextResolver.NodeName(o.MultiNode) ?? "Dungeon";
        string Left(int n) => o.Unlimited
            ? "Entries: unlimited (dev pass)"
            : string.Format(UiTextEn.Translate(
                "Entries left today: {0}/{1}"), n, o.Max);
        _soloInfo!.Text = "Difficulty scaled for a solo player. " +
            Left(o.SoloLeft);
        _multiInfo!.Text = "Strengthened monsters, increased exp and gold. " +
            Left(o.MultiLeft);
        _solo!.Disabled = !o.Unlimited && o.SoloLeft <= 0;
        _multi!.Disabled = !o.Unlimited && o.MultiLeft <= 0;
        Visible = true;
        RaiseToFront();
        // --dungeon-pick=solo|multi : choix automatique (test live).
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--dungeon-pick="))
            {
                var node = a.EndsWith("multi") ? o.MultiNode : o.SoloNode;
                GetTree().CreateTimer(2.0).Timeout += () => Pick(node);
            }
    }

    private void Pick(int node)
    {
        if (node == 0) return;
        Visible = false;
        DungeonUi.Enter(node);
    }

    /// <summary>L'offre est liée à la position dans l'aire du portail :
    /// toute arrivée serveur (migration, téléport, revive) la rend
    /// caduque — la fenêtre se ferme sinon elle survivait au changement
    /// de map (bug live : panneau encore ouvert dans le donjon).</summary>
    public override void _Process(double delta)
    {
        if (Visible && PortalArrival.Version != _arrivalVersion)
            Visible = false;
    }

    private void RaiseToFront() => GetParent()?.MoveChild(this, -1);
}

/// <summary>Portails de donjon : rubans violets qui tournent posés sur les
/// aires de portail du node courant (liste serveur 9062).</summary>
public partial class DungeonPortals : Node3D
{
    private readonly List<Node3D> _fx = new();
    private float _t;

    public override void _Ready() => DungeonUi.PortalsChanged += Rebuild;
    public override void _ExitTree() => DungeonUi.PortalsChanged -= Rebuild;

    private void Rebuild(IReadOnlyList<Vector3> pts)
    {
        foreach (var n in _fx) n.QueueFree();
        _fx.Clear();
        foreach (var p in pts)
        {
            var root = new Node3D();
            AddChild(root);
            root.GlobalPosition = Ground(p);
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableReceiveShadows = true,
            };
            for (var i = 0; i < 6; i++)
            {
                var m = new MeshInstance3D
                {
                    Mesh = Ribbon(i), MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                m.Rotation = new Vector3(0, i * Mathf.Tau / 6f, 0);
                root.AddChild(m);
            }
            var light = new OmniLight3D
            {
                LightColor = new Color(0.7f, 0.35f, 1f), OmniRange = 9f,
                LightEnergy = 1.6f, Position = new Vector3(0, 2, 0),
            };
            root.AddChild(light);
            _fx.Add(root);
        }
    }

    /// <summary>Sol sous le point : rayon vers le bas sur la physique
    /// monde, repli sur la hauteur reçue.</summary>
    private Vector3 Ground(Vector3 p)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return p;
        var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 60f,
            p + Vector3.Down * 120f);
        var hit = space.IntersectRay(q);
        return hit.Count > 0 ? (Vector3)hit["position"] : p;
    }

    /// <summary>Ruban vertical vrillé (hélice de 5,5 m), dégradé alpha.</summary>
    private static ArrayMesh Ribbon(int seed)
    {
        const int Seg = 40;
        var verts = new Vector3[(Seg + 1) * 2];
        var cols = new Color[(Seg + 1) * 2];
        var idx = new int[Seg * 6];
        for (var k = 0; k <= Seg; k++)
        {
            var u = k / (float)Seg;
            var ang = u * Mathf.Tau * 1.5f + seed * 0.5f;
            var r = 1.5f + 0.35f * Mathf.Sin(u * Mathf.Pi * 3f + seed);
            var c = new Vector3(Mathf.Cos(ang) * r, u * 5.5f,
                Mathf.Sin(ang) * r);
            var tan = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang));
            var w = 0.22f * (1f - Mathf.Abs(u - 0.5f));
            verts[k * 2] = c + Vector3.Up * w;
            verts[k * 2 + 1] = c - Vector3.Up * w + tan * 0.0f;
            var a = Mathf.Sin(u * Mathf.Pi);
            var col = new Color(0.62f, 0.25f, 1f, 0.9f * a);
            cols[k * 2] = col; cols[k * 2 + 1] = col;
        }
        for (var k = 0; k < Seg; k++)
        {
            var i = k * 6; var v = k * 2;
            idx[i] = v; idx[i + 1] = v + 1; idx[i + 2] = v + 2;
            idx[i + 3] = v + 1; idx[i + 4] = v + 3; idx[i + 5] = v + 2;
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = verts;
        arr[(int)Mesh.ArrayType.Color] = cols;
        arr[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return mesh;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        for (var i = 0; i < _fx.Count; i++)
            _fx[i].Rotation = new Vector3(0, _t * 1.4f, 0);
    }
}
