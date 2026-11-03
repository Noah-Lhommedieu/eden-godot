using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.GameInput;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Ramassage des trésors au sol (lot S1 — « SeeTreasure → TakeItem ») —
/// nœud posé sous la GameScene par <see cref="Hud.HudExtensions"/>.
///
///   - <b>329</b> <c>SeeTreasure</c> spawn déjà géré par
///     <see cref="EntityRegistry"/> (entité kind 0) ;
///   - <b>clic droit</b> sur le trésor (même picking écran que
///     <c>PlayerController</c> : projection caméra + rayon
///     <see cref="PickCore"/>) → approche à portée puis NCID 35
///     <see cref="ZoneTreasurePickup"/> <c>[u32 id]</c> (DESASM ;
///     portée serveur = 4 u — <c>HandleTreasurePickupAsync</c>) ;
///   - <b>386</b> <see cref="ZoneClientTreasurePickupOK"/> → message
///     système « ramassé » ; le 330 despawn retire l'entité.
///
/// Conflit clic : <c>PlayerController.OnRightClick</c> envoie un
/// TargetUpdate (124) pour les kind 0 — inoffensif, les deux partent.
/// </summary>
public partial class TreasurePickup : Node
{
    /// <summary>Portée de ramassage (4 u — CloseEnough natif).</summary>
    [Export] public float PickupRange { get; set; } = 4.0f;
    /// <summary>Rayon de picking écran (px — même convention que
    /// PlayerController.PickRadiusPx).</summary>
    [Export] public float PickRadiusPx { get; set; } = 36f;

    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private PlayerController? _player;
    private bool _nodesSearched;

    private bool _rDown;
    private Vector2 _pressPos;
    private RemoteEntity? _lootTarget;

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        if (!_nodesSearched)
        {
            _nodesSearched = true;
            _registry = GetTree().Root
                .FindChild("NetEntities", true, false) as EntityRegistry;
            _player = GetTree().Root
                .FindChild("Player", true, false) as PlayerController;
            // Annulation (clic sol, touche, cast…) : arrêter l'approche du loot.
            if (_player != null)
                _player.Cancelled += () => _lootTarget = null;
        }
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[loot] dispatch : {ex}");
            }
        }
    }

    /// <summary>Approche + ramassage — boucle physique (même shape
    /// que l'approche NPC de PlayerController : re-cible le point du
    /// trésor puis envoie le 35 à portée).</summary>
    public override void _PhysicsProcess(double delta)
    {
        var t = _lootTarget;
        var p = _player;
        if (t == null || p == null) return;
        if (!GodotObject.IsInstanceValid(t) || t.IsQueuedForDeletion())
        {
            _lootTarget = null;
            return;
        }
        var to = t.GlobalPosition - p.GlobalPosition;
        if (new Vector2(to.X, to.Z).Length() > PickupRange - 0.4f)
        {
            p.SetMoveTarget(t.GlobalPosition);
            return;
        }
        p.ClearMoveTarget();
        p.FaceTowards(t.GlobalPosition, 1f);
        Send(new ZoneTreasurePickup { TreasureId = t.Handle });
        GD.Print($"[loot] TakeItem trésor h=0x{t.Handle:x8} " +
            $"«{t.DisplayName}»");
        _lootTarget = null;
    }

    // ------------------------------------------------------------------
    // Input : clic droit (sans drag) sur une entité kind 0 = trésor.
    // _UnhandledInput ne voit que les clics non mangés par les Control.
    // ------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb ||
            mb.ButtonIndex != MouseButton.Right) return;
        if (mb.Pressed)
        {
            _rDown = true;
            _pressPos = mb.Position;
            return;
        }
        if (!_rDown) return;
        _rDown = false;
        if ((mb.Position - _pressPos).Length() > 6f) return;  // drag
        var ent = PickTreasure(mb.Position);
        if (ent == null) return;
        BeginPickup(ent);
    }

    /// <summary>Démarre l'approche + ramassage d'un trésor — chemin
    /// fil commun au clic droit (<see cref="_UnhandledInput"/>) et à
    /// la sonde <c>--pet-test</c> (qui court-circuite uniquement le
    /// picking écran, pas les paquets).</summary>
    public void BeginPickup(RemoteEntity ent)
    {
        _lootTarget = ent;
        // Sélection côté serveur (cohérent avec le comportement natif
        // qui surligne l'objet cliqué).
        Send(new ZoneTargetUpdate { Value = (int)ent.Handle });
        _player?.SetMoveTarget(ent.GlobalPosition);
        GD.Print($"[loot] ramassage → trésor h=0x{ent.Handle:x8} " +
            $"«{ent.DisplayName}»");
    }

    /// <summary>Projection écran des entités kind 0 (trésors) —
    /// identique au picking de PlayerController (+1.1 u).</summary>
    private RemoteEntity? PickTreasure(Vector2 screenPos)
    {
        var reg = _registry;
        var cam = _player?.Camera;
        if (cam == null || reg == null) return null;
        var pts = new List<(float X, float Y)>();
        var ents = new List<RemoteEntity>();
        foreach (var (_, e) in reg.Entities)
        {
            if (e.Kind != 0 || e.EntityId == 0) continue;   // joueurs exclus
            if (!GodotObject.IsInstanceValid(e) || !e.IsInsideTree())
                continue;
            var sp = cam.UnprojectPosition(
                e.GlobalPosition + new Vector3(0f, 0.45f, 0f));
            if (cam.IsPositionBehind(e.GlobalPosition)) continue;
            ents.Add(e);
            pts.Add((sp.X, sp.Y));
        }
        var idx = PickCore.NearestInRadius(pts,
            screenPos.X, screenPos.Y, PickRadiusPx);
        return idx >= 0 ? ents[idx] : null;
    }

    // ------------------------------------------------------------------
    // Binds session
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(386, r =>
        {
            var m = new ZoneClientTreasurePickupOK();
            m.Deserialize(r);
            _pending.Enqueue(() => OnPickupOk(m));
        });
        GD.Print("[loot] bind 386 posé");
    }

    /// <summary>386 — ramassage confirmé : message système.</summary>
    private void OnPickupOk(ZoneClientTreasurePickupOK m)
    {
        if (m.CharId != SessionCarry.Character?.Id) return;
        var name = TextResolver.ItemName(m.ItemId) ?? $"#{m.ItemId}";
        Hud.HudExtensions.FindHud()?.AddChat(7, "",
            string.Format(UiTextEn.Translate(
                EdenClient.Data.TreasureRules.PickupTemplate(m.Count)),
                name, m.Count));
    }

    private void Send(NetMessage msg)
    {
        var s = _session ?? SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var task = s.SendZoneAsync(msg, _cts.Token);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[loot] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _cts.Dispose();
        base._ExitTree();
    }
}
