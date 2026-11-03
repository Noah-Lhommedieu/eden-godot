using System.Collections.Concurrent;
using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → couple / mariage (Vague 2) — binding paresseux
/// <see cref="SessionCarry.Session"/>, même pattern que
/// <see cref="BuffBridge"/>.
///
/// Trames routées (<c>ZoneLoverMessages.cs</c> — corps d'après
/// <c>SocialHandlers.cs</c>/<c>WeddingPackets.cs</c> serveur +
/// NCMap natif) :
///   - <b>475</b> ClientLoverInvite → popup ;
///   - <b>476</b> ClientLoverUpdate → mise à jour du lien ;
///   - <b>477</b> ClientLoverInfo → fiche couple ;
///   - <b>478</b> ClientLoverBreak → rupture ;
///   - <b>479</b> ClientLoverPosition → position partenaire ;
///   - <b>597</b> ClientMarryOperation → résultat op mariage ;
///   - <b>599</b> ClientWeddingsInfo → cérémonies planifiées.
///
/// C→S branchés sur la fenêtre : <b>154</b> LoverInvite,
/// <b>155</b> LoverMessage, <b>156</b> QueryLoverInfo,
/// <b>157</b> LoverBreak, <b>249</b> MarryOperate,
/// <b>251</b> QueryWeddings.
/// </summary>
public partial class LoverBridge : Node
{
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();

    public LoverBridge()
    {
        Name = "LoverBridge";
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
                GD.PrintErr($"[couple] dispatch : {ex}");
            }
        }
    }

    /// <summary>Abonnements NCID — parse réseau puis mutation du
    /// <see cref="LoverState"/> de la fenêtre en file principale.</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;
        var w = CoupleWindow.Instance;
        if (w == null) return;

        s.OnZone(475, r =>
        {
            var m = new ZoneLoverInvite();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(476, r =>
        {
            var m = new ZoneLoverUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(477, r =>
        {
            var m = new ZoneLoverInfo();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(478, r =>
        {
            var m = new ZoneLoverBreak();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(479, r =>
        {
            var m = new ZoneLoverPosition();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(597, r =>
        {
            var m = new ZoneMarryOperation();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });
        s.OnZone(599, r =>
        {
            var m = new ZoneWeddingsInfo();
            m.Deserialize(r);
            _pending.Enqueue(() => w.State.Apply(m));
        });

        // C→S : propose/accepte, message, queries, break, mariage.
        w.InviteSender = (charId, name) => s.SendZoneAsync(
            new ZoneLoverInviteRequest
            { CharId = charId, PeerName = name },
            CancellationToken.None);
        w.MessageSender = msg => s.SendZoneAsync(
            new ZoneLoverMessage { Value = msg },
            CancellationToken.None);
        w.QueryInfoSender = () => s.SendZoneAsync(
            new ZoneQueryLoverInfo(), CancellationToken.None);
        w.BreakSender = () => s.SendZoneAsync(
            new ZoneLoverBreakRequest(), CancellationToken.None);
        w.MarrySender = op => s.SendZoneAsync(
            new ZoneMarryOpRequest { Op = op },
            CancellationToken.None);
        w.WeddingsSender = () => s.SendZoneAsync(
            new ZoneWeddingInfoQuery(), CancellationToken.None);

        // Résolution de la cible pour « Propose » : le TargetFrame
        // expose le charId+nom du joueur sélectionné.
        w.TargetResolver = () =>
        {
            var t = TargetFrame.Instance;
            return t != null && t.TargetIsPlayer
                ? (t.TargetCharId, t.TargetName)
                : ((uint CharId, string Name)?)null;
        };

        GD.Print("[couple] binds 475/477/478/479/597/599 posés");
    }
}
