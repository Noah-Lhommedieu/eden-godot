using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → quêtes/dialogues-services/transports/portails/
/// monture (lot S3+S5) — même pattern lazy-bind que
/// <see cref="HudNetBridge"/> : la session zone est découverte via
/// <see cref="SessionCarry.Session"/> puis les NCIDs y sont abonnés
/// une fois. Monté par <see cref="HudNetBridge"/> (instancié par
/// HudRoot.BuildAll — le seul point d'entrée HUD du lot).
///
/// Trames routées (formats prouvés — commentaires des codecs) :
///   - <b>388</b> EnterMissionList → fiche quête de
///     <see cref="NpcDialogueWindow"/> (offre/récompense/suivi) ;
///     ses boutons renvoient 87 MissionListOp.
///   - <b>345</b> UpdateNPCSymbols → marqueurs « ! »/« ? » billboard
///     posés sur les <see cref="RemoteEntity"/> NPC (kind 8).
///   - <b>313</b> EnterNodeNodeData → recharge la scène pour le
///     watch des aires GATEWAY (<b>portails</b>) : entrée dans une
///     event_area GATEWAY → C→S 15 CheckNodeExit
///     <c>[s16 keyId][s16 0]</c> (le serveur résout la destination).
///   - <b>283</b> ModelModify mask=2 → état monture (rideKey) →
///     modèle <c>ride/model/&lt;stem&gt;.glb</c>
///     attaché au joueur/l'entité.
///   - <b>312</b> EnterNodeInventory → snapshot d'inventaire pour
///     trouver l'item monture (ItemUse 19 toggle le ride — via
///     l'inventaire, pas de bouton HUD : hud-interfaces).
///
/// Envois : 87 MissionListOp (accept/reward…), 51 GiveUpMission,
/// 88 Transport (clic route), 15 CheckNodeExit (aire), 19 ItemUse
/// (monture). Threading identique à HudNetBridge : les handlers
/// parsent sur le thread réseau puis postent dans
/// <see cref="_pending"/> drainée ici.
/// </summary>
public partial class MissionBridge : Node
{
    private readonly HudRoot _hud;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private Node3D? _player;
    private readonly ConcurrentQueue<Action> _pending = new();

    // Fenêtres montées par ce lot.
    public TransportWindow? Transport { get; private set; }

    // ---- portails (aires GATEWAY de la scène courante) ----
    private int _nodeId;
    private SceneTable.Scene? _scene;
    private readonly HashSet<int> _insideGatewayKeys = new();
    private readonly List<int> _insideScratch = new();
    private int _arrivalSeen;
    private double _rearmSec;

    // ---- monture ----
    private Node3D? _mountNode;
    private int _rideKey;

    // Monture portée d'une scène à l'autre : le HUD (et ce pont) est
    // reconstruit à chaque changement de map alors que le serveur garde
    // le ride (RideStates) — sans ce relais la monture disparaissait au
    // portail. Liée à la session : une reconnexion repart à pied.
    private static int _carryRideKey;
    private static object? _carryRideSession;

    public MissionBridge(HudRoot hud)
    {
        _hud = hud;
        Name = "MissionBridge";
    }

    public override void _Ready()
    {
        // Fenêtres du lot montées sur le même canvas (plein écran +
        // MouseFilter Ignore — même convention que HudRoot.Mount).
        Transport = new TransportWindow { Name = "Transport" };
        Transport.Build(_hud);
        MountPanel(Transport);
        Transport.RouteChosen += route =>
            Send(new ZoneTransport { Value = (short)route });

        // Fiche quête (388) : boutons → MissionListOp 87.
        if (_hud.Dialogue != null)
            _hud.Dialogue.MissionAction += (op, mid, npc) =>
                Send(new ZoneMissionListOp
                {
                    Field1 = op, Field2 = mid,
                    // op 7 : le 3ᵉ champ est le flag d'acceptation (0/1),
                    // jamais remplacé par 0xFFFF.
                    Field3 = op == MissionListOps.ShareAnswer
                        ? (ushort)npc
                        : (ushort)(npc > 0 ? npc : 0xFFFF),
                });
        // Abandon du journal → GiveUpMission 51.
        if (_hud.QuestLog != null)
        {
            _hud.QuestLog.GiveUpRequested += mid =>
                Send(new ZoneGiveUpMission { MissionId = (short)mid });
            // Partage d'équipe → 87 op 6 (CMissionListOp natif).
            _hud.QuestLog.ShareRequested += mid =>
                Send(new ZoneMissionListOp
                {
                    Field1 = MissionListOps.ShareStart, Field2 = mid,
                    Field3 = 0xFFFF,
                });
            // Suivi quêtes (shift+clic / Track All) → C→S 186 padé à
            // 10 slots natifs + miroir local du tracker (le serveur
            // persiste sans écho).
            _hud.QuestLog.TraceChanged += ids =>
            {
                var msg = new ZoneSetMissionTrace();
                msg.MissionIds.AddRange(ids);
                while (msg.MissionIds.Count < 10) msg.MissionIds.Add(0);
                Send(msg);
                _hud.Quests?.SetTrace(ids);
            };
        }
        // 293 NodeExit → DialogueState.Close : masque la fenêtre.
        DialogueState.Closed += () =>
            _pending.Enqueue(() =>
            {
                if (_hud.Dialogue != null) _hud.Dialogue.Visible = false;
                TransportState.Close();
            });
    }

    private void MountPanel(Control panel)
    {
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hud.AddChild(panel);
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        if (!_rideCarried && SessionCarry.Session is { } cs)
        {
            _rideCarried = true;
            if (ReferenceEquals(_carryRideSession, cs) && _rideKey == 0)
                _rideKey = _carryRideKey;
        }
        _registry ??= GetTree().Root
            .FindChild("NetEntities", true, false) as EntityRegistry;
        _player ??= GetTree().Root
            .FindChild("Player", true, false) as Node3D;
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[mission-net] dispatch : {ex}");
            }
        }
        // 313 reçu avant la liaison de ce pont (login / nouvelle scène) :
        // on charge la scène du node courant.
        if (_scene == null && PortalArrival.CurrentNode > 0 &&
            _nodeId != PortalArrival.CurrentNode)
            LoadScene((short)PortalArrival.CurrentNode);
        WatchGateways(delta);
        // Monture reçue (283 d'entrée) avant que le joueur existe : on
        // la pose dès que le nœud « Model » est là.
        if (_rideKey != 0 && !_rideApplied &&
            _player?.GetNodeOrNull<Node3D>("Model") != null)
            SetRideModel(_rideKey);
        // Debug placement (RIDE_KEY / /tmp/ride_key.txt) — voir RideDebug.
        if (RideDebug.Poll() is { } dbg &&
            _player is EdenClient.Player.PlayerController dpc &&
            _player.GetNodeOrNull<Node3D>("Model") != null)
        {
            RideDebug.Ack();
            if (dbg.Key >= 0)
            {
                RideSeat.SeatDrop = dbg.Drop;
                _rideKey = dbg.Key;
                SetRideModel(dbg.Key);
            }
            dpc.DebugCam(dbg.Yaw, dbg.Pitch, dbg.Dist, dbg.Height);
        }
    }

    private bool _rideApplied;
    private bool _rideCarried;

    // ------------------------------------------------------------------
    // Binds NCID (thread réseau → file principale)
    // ------------------------------------------------------------------

    // 283 de la rafale d'entrée (monture restaurée au login) : arrive AVANT
    // que ce pont se lie (BindSession tourne en _Process une fois le HUD
    // monté) → perdu, le client restait « à pied » alors que le serveur
    // avait le ride actif : le 1er usage de l'item DÉMONTAIT (bug
    // « 1er équipement monture ne marche jamais »). Capture précoce
    // posée par GameScene dès la création de la session, rejouée au bind.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<
        (int Handle, int Mask, List<uint> Vec)> _earlyRide = new();
    private static EdenSession? _earlyFor;
    private static EdenSession? _boundFor;

    /// <summary>Pose la capture précoce des 283 (idempotent par session).
    /// </summary>
    public static void CaptureEarly(EdenSession s)
    {
        if (ReferenceEquals(_earlyFor, s)) return;
        _earlyFor = s;
        _earlyRide.Clear();
        s.OnZone(283, r =>
        {
            if (ReferenceEquals(_boundFor, s)) return;   // pont lié
            var handle = r.ReadInt();
            r.ReadByte();
            var mask = r.ReadInt();
            var n = r.ReadUShort();
            var vec = new List<uint>();
            for (var i = 0; i < n && r.Remaining >= 4; i++)
                vec.Add(r.ReadUInt());
            _earlyRide.Enqueue((handle, mask, vec));
        });
    }

    private void BindSession(EdenSession s)
    {
        _session = s;
        _boundFor = s;
        while (_earlyRide.TryDequeue(out var e))
        {
            var (h, m, v) = e;
            _pending.Enqueue(() => ApplyRideMask(h, m, v));
        }
        // Node courant → recharge la scène (aires gateway).
        s.OnZone(313, r =>
        {
            var nodeId = r.ReadShort();
            PortalArrival.Begin();   // thread réseau : avant tout _Process
            _pending.Enqueue(() => LoadScene(nodeId));
        });
        // Fiche quête (offre/récompense/suivi/fin).
        s.OnZone(388, r =>
        {
            var op = r.ReadShort();
            var arg = r.ReadInt();
            var npcUid = r.ReadInt();
            _pending.Enqueue(() =>
                _hud.Dialogue?.ShowMissionSheet(op, arg, npcUid));
        });
        // Marqueurs « ! »/« ? » au-dessus des NPC.
        s.OnZone(345, r =>
        {
            var m = new ZoneUpdateNpcSymbols();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyNpcSymbols(m));
        });
        // Équipement/modèle : mask=2 = visuel monture.
        s.OnZone(283, r =>
        {
            var handle = r.ReadInt();
            r.ReadByte();                       // version
            var mask = r.ReadInt();
            var n = r.ReadUShort();
            var vec = new List<uint>();
            for (var i = 0; i < n && r.Remaining >= 4; i++)
                vec.Add(r.ReadUInt());
            _pending.Enqueue(() => ApplyRideMask(handle, mask, vec));
        });
        // 293 NodeExit : reset portails + monture + symboles.
        s.OnZone(293, r =>
        {
            _pending.Enqueue(() =>
            {
                _insideGatewayKeys.Clear();
                _scene = null;
                SetRideModel(0);
            });
        });
        GD.Print("[mission-net] binds 313/388/345/283/312/293 posés");
    }

    /// <summary>Envoi zone protégé (aucune session → no-op).</summary>
    private void Send(NetMessage msg)
    {
        if (_session?.Zone?.IsConnected != true) return;
        var send = _session.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[mission-net] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------
    // Portails : entrée dans une event_area GATEWAY → CheckNodeExit
    // ------------------------------------------------------------------

    /// <summary>Recharge la scène du node (data/scene/sNNN.ini) pour
    /// les aires gateway ; reset de l'état « dans l'aire ».</summary>
    private void LoadScene(short nodeId)
    {
        _nodeId = nodeId;
        _insideGatewayKeys.Clear();
        _scene = null;
        var path = AssetCatalog.ResolveRawAsset(
            $"data/scene/s{nodeId:D3}.ini");
        if (path == null) return;
        try { _scene = SceneTable.Load(path); }
        catch (Exception e)
        {
            GD.PrintErr($"[portal] scene s{nodeId:D3} : {e.Message}");
        }
    }

    /// <summary>Détecte l'entrée du joueur dans une event_area GATEWAY
    /// et émet CheckNodeExit une fois par entrée (le serveur valide
    /// TestIn + anti-retrigger ; la migration arrive via 293/313 —
    /// la transition map est déjà gérée par GameScene.OnNodeData).</summary>
    private void WatchGateways(double delta)
    {
        if (_rearmSec > 0) { _rearmSec -= delta; return; }
        if (_scene == null || _player == null ||
            _session?.Zone?.IsConnected != true)
            return;
        // Map en chargement : la position du joueur est encore celle de
        // l'ancien node (repère étranger) — pas de portail.
        if (PortalArrival.Pending) return;
        var (ex, ey) = WorldConv.ToEdenGround(_player.GlobalPosition);
        var inside = _insideScratch;
        inside.Clear();
        foreach (var g in _scene.Gateways)
        {
            var a = _scene.EventAreas
                .FirstOrDefault(e => e.Id == g.EventAreaId);
            if (a == null) continue;
            var dx = ex - a.X; var dy = ey - a.Y;
            if (dx * dx + dy * dy <= a.Radius * a.Radius)
                inside.Add(g.KeyId);
        }
        // Arrivée appliquée : les aires où l'on atterrit (aire jumelée
        // du portail) sont « déjà dedans » — front montant requis
        // (natif).
        if (_arrivalSeen != PortalArrival.Version)
        {
            _arrivalSeen = PortalArrival.Version;
            _insideGatewayKeys.Clear();
            foreach (var k in inside) _insideGatewayKeys.Add(k);
            return;
        }
        _insideGatewayKeys.IntersectWith(inside);  // sorties : réarme.
        // Aire nouvellement entrée → CheckNodeExit, une par cycle (le
        // re-arm 2 s évite le spam, les aires suivantes partent aux
        // cycles d'après). Le suivi est par aire : un petit portail
        // noyé dans une grande aire gateway (ex. zone de fouille S7)
        // doit déclencher même quand l'autre reste « dedans ».
        foreach (var key in inside)
        {
            if (!_insideGatewayKeys.Add(key)) continue;
            // Wire natif CheckNodeExit : [s16 keyId col4][s16 dstNode 0=
            // résolution serveur] — les champs du codec s'appellent
            // NodeId/PortalId mais c'est bien (keyId, dstNode) sur le
            // fil (ZoneEntryFlow.Migration.cs:43-45 serveur).
            Send(new ZoneCheckNodeExit
            {
                NodeId = (short)key,
                PortalId = 0,
            });
            _rearmSec = 2.0;                // anti-spam local
            GD.Print($"[portal] aire gateway key={key} → CheckNodeExit");
            break;
        }
    }

    // ------------------------------------------------------------------
    // Marqueurs NPC « ! »/« ? » (345)
    // ------------------------------------------------------------------

    /// <summary>Applique les marqueurs aux NPC en vue (kind 8,
    /// EntityId = template id = npcUid du 345).</summary>
    private void ApplyNpcSymbols(ZoneUpdateNpcSymbols m)
    {
        if (_registry == null) return;
        var byUid = new Dictionary<int, (short, short)>();
        foreach (var e in m.Entries) byUid[e.NpcUid] = (e.Sym1, e.Sym2);
        foreach (var (_, e) in _registry.Entities)
        {
            if (e.Kind != 8) continue;
            if (byUid.TryGetValue(e.EntityId, out var sym))
                SetSymbolMarker(e, sym.Item1, sym.Item2);
            else
                ClearSymbolMarker(e);
        }
    }

    /// <summary>Crée/maj le billboard Label3D au-dessus du NPC.
    /// Valeurs natives sym (passthru serveur — le glyph est cosmétique
    /// côté client) : 1 = « ! » offre, 2 = « ? » rendre, 3+ = « ? »
    /// suivi (gris).</summary>
    private static void SetSymbolMarker(RemoteEntity e, short s1, short s2)
    {
        var (text, color) = s1 switch
        {
            1 => ("!", new Color(1f, 0.85f, 0.2f)),
            2 => ("?", new Color(0.4f, 0.9f, 1f)),
            _ => ("?", new Color(0.7f, 0.7f, 0.7f)),
        };
        var mark = e.GetNodeOrNull<Label3D>("QuestMark");
        if (mark == null)
        {
            mark = new Label3D
            {
                Name = "QuestMark",
                Position = new Vector3(0f, 2.2f, 0f),
                FontSize = 96,
                PixelSize = 0.01f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
            };
            e.AddChild(mark);
        }
        mark.Text = text;
        mark.Modulate = color;
        mark.Visible = true;
    }

    private static void ClearSymbolMarker(RemoteEntity e)
    {
        var mark = e.GetNodeOrNull<Label3D>("QuestMark");
        if (mark != null) mark.Visible = false;
    }

    // ------------------------------------------------------------------
    // Monture (283 mask=2 + ItemUse 19)
    // ------------------------------------------------------------------

    /// <summary>283 mask=2 (EModelTransition Ride) : vec[0]=rideKey,
    /// vec[1]=flag — pose/retire le modèle de monture sur l'entité
    /// (désasm CEC_Ride::SetState, SendRideModelAsync serveur).</summary>
    private void ApplyRideMask(int handle, int mask, List<uint> vec)
    {
        if ((mask & 2) == 0) return;
        var rideKey = vec.Count > 0 ? (int)vec[0] : 0;
        var selfId = (int)(SessionCarry.Character?.Id ?? 0);
        GD.Print($"[mount] 283 handle={handle} self={selfId} key={rideKey}");
        if (handle == selfId && selfId != 0)
        {
            _rideKey = rideKey;
            _carryRideKey = rideKey;
            _carryRideSession = SessionCarry.Session;
            SetRideModel(rideKey);
        }
        else if (_registry != null &&
                 _registry.Entities.TryGetValue((uint)handle, out var e))
        {
            AttachRide(e, rideKey);
        }
    }

    /// <summary>rideKey → stem modèle : code ridelist « r »+key:D6
    /// (ex. 12010 → r012010 → r012.kfm → ride/model/R012.glb —
    /// convention npclist/monsterlist : les 3 derniers chiffres sont
    /// la variante).</summary>
    private static string? RideStem(int rideKey)
    {
        if (rideKey <= 0) return null;
        var code = $"r{rideKey:D6}";
        var stem = RideList.Stem(code);
        return stem ?? $"r{rideKey / 1000:D3}";
    }

    /// <summary>Attache/détache le visuel de monture du joueur local —
    /// même assemblage que <see cref="EntityRegistry.SetRide"/> : monture
    /// animée (<see cref="CharVisual.BuildRideVisual"/>, −90°/X + 180°/Y)
    /// enfant du nœud « Model » (elle suit donc le yaw du perso) et
    /// corps hissé sur la selle (≈ 38 % de la hauteur de la monture, réglé sur capture).</summary>
    private void SetRideModel(int rideKey)
    {
        if (_mountNode != null)
        {
            if (GodotObject.IsInstanceValid(_mountNode))
            {
                _mountNode.GetParent()?.RemoveChild(_mountNode);
                _mountNode.QueueFree();
            }
            _mountNode = null;
        }
        _rideApplied = false;
        var modelNode = _player?.GetNodeOrNull<Node3D>("Model");
        var body = (_player as EdenClient.Player.PlayerController)?.Visual;
        var pc = _player as EdenClient.Player.PlayerController;
        if (pc != null) pc.MountAnimator = null;
        // RideSeat réécrit tout le Transform du visuel (base + origine) :
        // on mémorise la pose de repos (rotation d'import comprise) et on
        // la restaure à la descente — sinon le perso restait décalé /
        // incliné après avoir quitté sa monture.
        if (body != null)
        {
            if (!body.HasMeta("ride_rest"))
                body.SetMeta("ride_rest", body.Transform);
            else
                body.Transform = body.GetMeta("ride_rest").AsTransform3D();
        }
        // Descente : le corps redescend au sol.
        if (rideKey == 0)
        {
            _rideApplied = true;
            if (body != null && body.HasMeta("ride_rest"))
                body.Transform = body.GetMeta("ride_rest").AsTransform3D();
            else if (body != null) body.Position = Vector3.Zero;
            return;
        }
        if (modelNode == null) return;
        _rideApplied = true;
        var mount = EdenClient.Game.CharVisual.BuildRideVisual(rideKey);
        if (mount == null)
        {
            GD.PrintErr($"[mount] pas de GLB pour rideKey={rideKey}");
            return;
        }
        _mountNode = new Node3D { Name = "_mount" };
        _mountNode.Rotation = new Vector3(0f, 0f, 0f);
        _mountNode.AddChild(mount);
        modelNode.AddChild(_mountNode);
        if (pc != null)
            pc.MountAnimator = EdenClient.Game.CharVisual.MountAnimator(mount);
        var fit = RideFit.Apply(rideKey, _mountNode, mount);
        var lift = fit.Lift;
        if (body != null) body.Position = new Vector3(0f, lift, fit.BackZ);
        // Placement natif dynamique : racine cavalier sur le nœud L17.
        var onL17 = body != null && RideFit.Follow(_mountNode, mount, body);
        GD.Print($"[mount] visuel key={rideKey} lift={lift:0.00} " +
            $"top={World.RemoteEntity.VisualTopHeight(mount):0.00} " +
            $"L17={(onL17 ? body!.Position.ToString("0.00") : "absent")}");
    }

    /// <summary>Monture d'une entité distante (même 283 broadcast).</summary>
    private void AttachRide(RemoteEntity e, int rideKey)
    {
        e.SetRide(rideKey);
    }

}

/// <summary>
/// <c>data/db/ridelist.ini</c> — code « rNNNxxx » → stem du modèle
/// (col2 « rNNN.kfm ») pour <c>ride/model/&lt;stem&gt;.glb</c>.
/// Chargement paresseux.
/// </summary>
public static class RideList
{
    private static Dictionary<string, string>? _stems;
    private static Dictionary<string, string>? _texs;
    private static Dictionary<string, (float Scale, float F5)>? _cols;
    private static bool _tried;

    /// <summary>Col 2 (scale) et col 9 (f5, sémantique « hauteur
    /// selle » NON confirmée) d'un code ridelist ; (1,0) si absent.
    /// </summary>
    public static (float Scale, float F5) Cols(string code)
    {
        Ensure();
        return _cols != null &&
               _cols.TryGetValue(code.ToLowerInvariant(), out var c)
            ? c : (1f, 0f);
    }

    /// <summary>Stem kfm d'un code ridelist (« r012010 » → « r012 »),
    /// null si absent.</summary>
    public static string? Stem(string code)
    {
        Ensure();
        return _stems != null &&
               _stems.TryGetValue(code.ToLowerInvariant(), out var s)
            ? s : null;
    }

    /// <summary>Stem de la texture variante d'un code ridelist
    /// (col4 « r25302.dds » → « r25302 ») : chaque variante a sa
    /// propre texture corps dans le jeu d'origine — null si absent.
    /// </summary>
    public static string? Tex(string code)
    {
        Ensure();
        return _texs != null &&
               _texs.TryGetValue(code.ToLowerInvariant(), out var t)
            ? t : null;
    }

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        _stems = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        _texs = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        _cols = new Dictionary<string, (float, float)>(
            StringComparer.OrdinalIgnoreCase);
        var path = (TextResolver.ResolveFile
                ?? StoreTable.ResolveDefault)("data/db/ridelist.ini");
        if (path == null) return;
        try
        {
            foreach (var line in Vfs.EdenVfs.ReadLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var fields = line[(eq + 1)..].Trim().Split(',');
                if (fields.Length >= 10 && fields[0].StartsWith('r'))
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    float.TryParse(fields[2], System.Globalization.NumberStyles.Float, ci, out var sc);
                    float.TryParse(fields[9], System.Globalization.NumberStyles.Float, ci, out var f5);
                    _cols[fields[0].Trim().ToLowerInvariant()] =
                        (sc > 0f ? sc : 1f, f5);
                }
                if (fields.Length >= 2 && fields[0].StartsWith('r'))
                    _stems[fields[0].Trim().ToLowerInvariant()] =
                        Path.GetFileNameWithoutExtension(
                            fields[1].Trim());
                if (fields.Length >= 5 && fields[0].StartsWith('r'))
                {
                    var t = fields[4].Trim();
                    if (t.Length > 0)
                        _texs[fields[0].Trim().ToLowerInvariant()] =
                            Path.GetFileNameWithoutExtension(t);
                }
            }
        }
        catch (Exception e)
        {
            TextResolver.Log?.Invoke($"[ride] ridelist.ini : {e.Message}");
        }
    }
}


/// <summary>
/// Placement cavalier/monture, UNE lookup pour le joueur local et les
/// distants (A6.2). Par stem modèle (les variantes partagent le stem) :
/// LiftY = hauteur de selle absolue (0 = auto), BackZ = recul du
/// cavalier, YawDeg = rotation de la monture, ScaleMul. Auto = haut
/// du visuel × <see cref="DefaultLiftFactor"/>, borné 0.5-1.6.
/// Le scale (col 2 ridelist) est appliqué. Col 9 (f5) : RÉFUTÉ comme
/// hauteur de selle (corrélation f5/hauteur max GLB r=0.39, f5/longueur
/// r=0.54 sur 140 stems ; r241 f5=4.5 selle=0, r211 f5=4.0 selle=2.7) —
/// c'est une classe de taille 1-5 (empreinte/caméra), non utilisée ici.
/// <see cref="ByStem"/> vient de <see cref="RideFitData"/> (généré) ;
/// ne sert plus qu'au repli quand L17 est absent/aberrant — le placement
/// nominal est le nœud L17 (<see cref="RideSeat"/>).
/// weaponLoc : l'arme reste sur Bip01 Prop1 du cavalier, qui suit le
/// corps soulevé — rien de spécifique monture.
/// </summary>
public static class RideFit
{
    /// <summary>Nom du nœud d'attache du cavalier dans le modèle de la
    /// monture. Natif : <c>CRide::Attach</c> (rf_game FUN_0073fc60) pose
    /// la racine du cavalier sur le nœud « L17 » de la monture
    /// (<c>DAT_00f069f4</c> = "L17\0", lu dans game_.exe) ; les
    /// passagers vont sur « L5x » (<c>L%d</c>, siège+0x32, FUN_0073f150).
    /// 356 des 359 modèles de monture portent L17 (docs/MONTURES-L17.md). Le nœud est animé
    /// avec la monture → placement dynamique.</summary>
    public const string DriverNode = "L17";

    /// <summary>Pose le cavalier sur le nœud L17 de la monture et l'y
    /// garde chaque frame (<see cref="RideSeat"/>). false = nœud absent
    /// → repli statique (<see cref="Apply"/>).</summary>
    public static bool Follow(Node3D holder, Node3D mount, Node3D body)
    {
        var seat = RideSeat.Create(holder, mount, body);
        if (seat == null || !seat.Plausible(holder)) return false;
        holder.AddChild(seat);
        seat.Snap();
        return true;
    }

    /// <summary>Facteur auto unifié (ex-local 0.38 ; le distant
    /// utilisait 0.72 sans validation).</summary>
    public const float DefaultLiftFactor = 0.38f;

    public readonly record struct Entry(float LiftY, float BackZ,
        float YawDeg, float ScaleMul);

    /// <summary>Surcharges par stem (minuscule) — à remplir après
    /// validation visuelle par monture.</summary>
    public static readonly Dictionary<string, Entry> ByStem = Build();

    private static Dictionary<string, Entry> Build()
    {
        var d = new Dictionary<string, Entry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (stem, lift, back) in RideFitData.Rows)
            d[stem] = new Entry(lift, back, 0f, 1f);
        return d;
    }

    public readonly record struct Result(float Lift, float BackZ);

    /// <summary>Applique scale/yaw à la monture et renvoie lift/recul
    /// du cavalier.</summary>
    public static Result Apply(int rideKey, Node3D holder, Node3D mount)
    {
        var stem = EdenClient.Game.CharVisual.RideStem(rideKey) ?? "";
        var (colScale, _) = RideList.Cols($"r{rideKey:D6}");
        ByStem.TryGetValue(stem, out var e);
        var mul = (e.ScaleMul > 0f ? e.ScaleMul : 1f) * colScale;
        if (!Mathf.IsEqualApprox(mul, 1f)) mount.Scale *= mul;
        if (e.YawDeg != 0f)
            holder.Rotation = new Vector3(0f, Mathf.DegToRad(e.YawDeg), 0f);
        // LiftY/BackZ sont en unités modèle : suivent le scale appliqué.
        // Repli statique (modèles sans L17 exploitable : M412, R521, R890,
        // JEFF, AR979) : dos mesuré au runtime, sinon table/auto.
        var back = SaddleHeight(holder, mount);
        var lift = back > 0f ? back
            : e.LiftY > 0f ? e.LiftY * mul : Mathf.Clamp(
                World.RemoteEntity.VisualTopHeight(mount) * DefaultLiftFactor,
                0.5f, 1.6f);
        return new Result(lift, e.BackZ * mul);
    }

    /// <summary>Hauteur du dos de la monture (repère du holder) : sommets
    /// proches de l'axe central horizontal du corps (hors tête/queue),
    /// 97e centile en Y — là où le cavalier s'assoit.</summary>
    private static float SaddleHeight(Node3D holder, Node3D mount)
    {
        var pts = new List<Vector3>();
        var inv = holder.GlobalTransform.AffineInverse();
        foreach (var n in mount.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D { Mesh: { } mesh, Visible: true } mi) continue;
            if (!mi.IsInsideTree() || !holder.IsInsideTree()) return 0f;
            var rel = inv * mi.GlobalTransform;
            for (var sf = 0; sf < mesh.GetSurfaceCount(); sf++)
            {
                var arr = mesh.SurfaceGetArrays(sf);
                if (arr.Count == 0 || arr[(int)Mesh.ArrayType.Vertex].VariantType
                    != Variant.Type.PackedVector3Array) continue;
                foreach (var v in arr[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    pts.Add(rel * v);
            }
        }
        if (pts.Count < 20) return 0f;
        float minX = float.MaxValue, maxX = float.MinValue,
              minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in pts)
        {
            minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X);
            minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z);
        }
        var cx = (minX + maxX) * 0.5f; var cz = (minZ + maxZ) * 0.5f;
        var hx = (maxX - minX) * 0.18f; var hz = (maxZ - minZ) * 0.18f;
        var ys = pts.Where(p => Mathf.Abs(p.X - cx) <= hx &&
                Mathf.Abs(p.Z - cz) <= hz).Select(p => p.Y).OrderBy(y => y)
            .ToList();
        if (ys.Count < 10) return 0f;
        var top = ys[(int)(ys.Count * 0.97f)];
        var h = ys[(int)(ys.Count * 0.78f)];
        return h - ys[0];
    }
}


/// <summary>Garde le cavalier sur le nœud « L17 » de la monture, comme
/// l'attache native (<c>CRide::Attach</c> FUN_0073fc60 : transform local
/// du cavalier remis à l'identité puis enfant du nœud L17 → la racine du
/// cavalier prend la position ET l'orientation complètes de L17, qui
/// suit l'animation de la monture). Vérifié sur B.kf : ride01 n'a
/// AUCUNE translation de bassin (Bip01 = pose statique 0, Pelvis = 2
/// clés de rotation nulles) → le natif ne rabaisse pas le cavalier,
/// ses hanches (~0,45 au-dessus de la racine) sont au-dessus de L17 ;
/// les concepteurs ont donc posé L17 sous la surface de selle.
/// Le « Visual » du cavalier porte sa propre rotation d'import
/// (−90°X/180°Y) qui se compose après l'alignement sur L17.</summary>
public sealed partial class RideSeat : Node
{
    private Node3D _body = null!;
    private Node3D _mount = null!;
    private Node3D? _node;
    private Skeleton3D? _sk;
    private int _bone = -1;
    private Basis _mountRot = Basis.Identity;
    private Basis _bodyRot = Basis.Identity;
    private int _dbgN;

    /// <summary>Calage vertical (unités monde), 0 = natif. Réglable en
    /// debug (6e valeur de /tmp/ride_key.txt).</summary>
    public static float SeatDrop { get; set; } = 0f;

    public static RideSeat? Create(Node3D holder, Node3D mount, Node3D body)
    {
        var s = new RideSeat { Name = "_ride_seat", _body = body,
            _mount = mount, _mountRot = mount.Basis.Orthonormalized(),
            _bodyRot = body.Basis.Orthonormalized() };
        foreach (var n in mount.FindChildren("*", "Skeleton3D", true, false))
            if (n is Skeleton3D sk &&
                sk.FindBone(RideFit.DriverNode) is var b and >= 0)
            { s._sk = sk; s._bone = b; goto found; }
        if (mount.FindChild(RideFit.DriverNode, true, false) is Node3D nd)
            s._node = nd;
        else return null;
    found:
        if (RideDebug.Verbose)
            GD.Print($"[ride-seat] create body={body.Name} rotDeg={body.RotationDegrees} scale={body.Scale} parent={body.GetParent()?.Name} mountRotDeg={mount.RotationDegrees} mountScale={mount.Scale}");
        return s;
    }

    /// <summary>Transform global du nœud L17 (orthonormalisé : le scale
    /// de la monture n'est pas transmis au cavalier).</summary>
    private Transform3D SeatGlobal()
    {
        var t = _sk != null
            ? _sk.GlobalTransform * _sk.GetBoneGlobalPose(_bone)
            : _node!.GlobalTransform;
        return new Transform3D(t.Basis.Orthonormalized(), t.Origin);
    }

    public void Snap()
    {
        if (!GodotObject.IsInstanceValid(_body) ||
            !GodotObject.IsInstanceValid(_mount) ||
            _body.GetParent() is not Node3D parent) return;
        var seat = SeatGlobal();
        // Le repère GLB du cavalier est aligné sur celui de L17 : même
        // convention −90°X/180°Y que la monture, d'où le retrait de la
        // rotation d'import de la monture (RideSeatMath, testé xUnit).
        var rb = EdenClient.Game.RideSeatMath.RiderBasis(B3(seat.Basis),
            B3(_mountRot));
        // Le nœud « Visual » du cavalier porte sa propre rotation
        // d'import (−90°X/180°Y) : elle se compose après l'alignement.
        var basisG = new Basis(V(rb.X), V(rb.Y), V(rb.Z)) * _bodyRot;
        var originG = V(EdenClient.Game.RideSeatMath.RiderOrigin(
            V(seat.Origin), SeatDrop));
        var local = parent.GlobalTransform.AffineInverse() *
                    new Transform3D(basisG, originG);
        _body.Transform = local;
        if (++_dbgN % 120 == 1 && RideDebug.Verbose)
            GD.Print($"[ride-seat] mount={_mount.Name} seatBasis={seat.Basis.GetEuler() * 57.2958f} " +
                $"mountRot={_mountRot.GetEuler() * 57.2958f} riderEuler={basisG.GetEuler() * 57.2958f} " +
                $"seatO={seat.Origin} bodyLocalPos={local.Origin}");
    }

    private static EdenClient.Game.RideSeatMath.Basis3 B3(Basis b) =>
        new(V(b.Column0), V(b.Column1), V(b.Column2));
    private static System.Numerics.Vector3 V(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>L17 dans le repère du porteur est-il exploitable ?
    /// (JEFF, AR979… → repli statique).</summary>
    public bool Plausible(Node3D holder)
    {
        var p = holder.ToLocal(SeatGlobal().Origin);
        return EdenClient.Game.RideSeatMath.IsPlausibleSeat(
            new System.Numerics.Vector3(p.X, p.Z, p.Y));
    }

    private Vector3 _pp; private int _pf;
    public override void _Process(double delta)
    {
        Snap();
        if (++_pf % 20 != 0 || _sk == null) return;
        var h = _sk.FindBone("Bip01 Head"); var pel = _sk.FindBone("Bip01 Pelvis");
        if (h < 0) h = _sk.FindBone("Bip01 Neck");
        var gp = _mount.GlobalPosition;
        var hd = _sk.GlobalTransform * _sk.GetBoneGlobalPose(h);
        var pl = pel >= 0 ? _sk.GlobalTransform * _sk.GetBoneGlobalPose(pel) : new Transform3D(Basis.Identity, gp);
        GD.Print($"[ZPROBE] move={(gp - _pp):F3} headDir={(hd.Origin - pl.Origin).Normalized():F2} cam={GetViewport().GetCamera3D()?.GlobalBasis.Z:F2}");
        _pp = gp;
    }
}
