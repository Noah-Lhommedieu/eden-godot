using System.Collections.Concurrent;
using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → chemin d'éveil + fortune (Vague 2) — binding
/// paresseux <see cref="SessionCarry.Session"/>, même pattern que
/// <see cref="BuffBridge"/>.
///
/// Trames routées (<c>ZoneDestinyMessages.cs</c>, corps prouvés
/// ZoneEntryFlow.Destiny.cs / ZoneMessages.g.cs serveur) :
///   - <b>534</b> ClientPlayerDestinyRoad → état complet ;
///   - <b>535</b> ClientPlayerDestinyRoadEvent → bandeau ;
///   - <b>589</b> ClientUpdateDestinyRoad → updates atomiques ;
///   - <b>590</b> ClientDestinyDiceFailed → refus d'op ;
///   - <b>557</b> ClientUpdateFortuneBagCounter → compteurs.
///
/// C→S branchés sur la fenêtre : <b>203</b> DestinyDice [op][p2][p3],
/// <b>204</b> QueryDestinyRoad, <b>223</b> QueryFortuneBagCounter.
/// </summary>
public partial class DestinyBridge : Node
{
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();

    public DestinyBridge()
    {
        Name = "DestinyBridge";
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[destiny] dispatch : {ex}");
            }
        }
    }

    /// <summary>Abonnements NCID — parse réseau puis mutation du
    /// <see cref="DestinyState"/> de la fenêtre en file principale.</summary>
    private void BindSession(EdenSession s)
    {
        var w = DestinyWindow.Instance;
        // Ne poser _session qu'une fois les binds installés — sinon
        // une fenêtre montée après la session (chargement de map)
        // ne retentait jamais : les trames 534/557 du burst
        // d'entrée partaient en « non géré » (constat live).
        if (w == null) return;
        _session = s;

        // S→C 534/535/589/590/557 : binds précoces dans HudExtensions
        // (DestinyState.Shared — data thread réseau, événements file
        // principale). Ne pas doubler ici : un second bind rejouerait
        // les mutations atomiques 589/557.

        // C→S : actions destin + requêtes.
        w.DiceSender = (op, p2, p3) => s.SendZoneAsync(
            new ZoneDestinyDice { Op = op, P2 = p2, P3 = p3 },
            CancellationToken.None);
        w.QuerySender = () => s.SendZoneAsync(
            new ZoneQueryDestinyRoad(), CancellationToken.None);
        w.FortuneSender = () => s.SendZoneAsync(
            new ZoneQueryFortuneBagCounter(), CancellationToken.None);

        // Synchro initiale : la route persistée n'est émise qu'à la
        // demande (HandleQueryDestinyRoadAsync natif → 534).
        var send = s.SendZoneAsync(new ZoneQueryDestinyRoad(),
            CancellationToken.None);
        _ = send.ContinueWith(t => GD.PrintErr(
                $"[destiny] query204 : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);

        GD.Print("[destiny] binds 534/535/589/590/557 posés");
    }
}
