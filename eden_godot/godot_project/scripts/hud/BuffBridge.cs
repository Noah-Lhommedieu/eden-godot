using System.Collections.Concurrent;
using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → buffs/debuffs (Vague 2) — même pattern de binding
/// précoce que <see cref="ElfBridge"/> : <see cref="EnsureBound"/>
/// pose les handlers sur la session zone avant que le pont n'existe
/// (le serveur émet les 299 du burst d'entrée — enchants restaurés,
/// état « pierre de portail gratuite » — dès OnMigrationDone, pendant
/// que la CEGUI charge encore ; sans bind précoce ils partaient en
/// « ncid non géré », cf. docs/knowledge/statuses-flow-audit.md).
///
/// Trames routées (codecs <c>ZoneEnchantMessages.cs</c>, corps
/// prouvés côté serveur — Status.cs / DisplayChar.cs /
/// ZoneMessages.g.cs) :
///
///   - <b>299</b> ClientEnchantAdd → EntityBuffs (self + autres
///     joueurs vus : broadcast can-see) ;
///   - <b>300</b> ClientEnchantRemove → EntityBuffs ;
///   - <b>301</b> ClientEnchantUpdate → EntityBuffs (snapshot —
///     SeeChar joueur + réponse au 18) ;
///   - <b>315</b> ClientEnchantReset → EntityBuffs (jamais émis par
///     le serveur actuel) ;
///   - <b>372</b> ClientTeamMemberEnchant → EntityBuffs (membres —
///     consommé par PartyHud) ;
///   - <b>445/446</b> ClientEnchantPause/Start → décompte figé.
///
/// C→S : à chaque changement de cible <see cref="TargetFrame"/> un
/// <b>18</b> EnchantUpdate est envoyé (le serveur répond un 301 —
/// vide pour les mobs : leurs enchants n'ont pas de chemin
/// d'émission, limite serveur documentée) ; le clic droit de
/// <see cref="BuffBar"/> envoie un <b>85</b> EnchantCancel.
///
/// Threading : parse sur le thread réseau puis file
/// <see cref="Ops"/> drainée ici sur le thread principal.
/// </summary>
public partial class BuffBridge : Node
{
    private readonly HudRoot _hud;
    /// <summary>Ops parsées sur le thread réseau, appliquées sur
    /// l'instance au prochain _Process — statique pour que les binds
    /// posés tôt (<see cref="EnsureBound"/>, avant la création du
    /// pont) ne perdent pas le burst d'entrée (299).</summary>
    private static readonly ConcurrentQueue<Action<BuffBridge>> Ops =
        new();
    /// <summary>Session déjà équipée de nos handlers — évite le double
    /// abonnement si EnsureBound est appelé tôt puis que _Process
    /// retombe sur la même session.</summary>
    private static EdenSession? _boundSession;
    private EdenSession? _session;
    private uint _lastTarget;
    private double _pruneTimer;
    private bool _selfWired;

    public BuffBridge(HudRoot hud)
    {
        _hud = hud;
        Name = "BuffBridge";
    }

    public override void _Process(double delta)
    {
        if (_boundSession == null && SessionCarry.Session is { } s)
            EnsureBound(s);     // repli : flux sans GameScene attachée
        _session ??= _boundSession;
        while (Ops.TryDequeue(out var act))
        {
            try { act(this); }
            catch (Exception ex)
            {
                GD.PrintErr($"[buff] dispatch : {ex}");
            }
        }
        // Self = charId du perso choisi (le « handle » joueur du wire —
        // les buffs self arrivent en 299/301/315 sur cet id) + clic
        // droit → 85. Câblés dès que la BuffBar existe : le bind
        // session a pu se faire bien avant son montage.
        if (!_selfWired && _session != null &&
            BuffBar.Instance is { } bar)
        {
            var ss = _session;
            bar.SetSelfHandle((int)ss.SelectedCharId);
            bar.CancelSender = eid => ss.SendZoneAsync(
                new ZoneEnchantCancel { EffectId = eid },
                CancellationToken.None);
            _selfWired = true;
        }
        // Cible changée (444 ou frame cachée) → requête 18 + repaint.
        var t = _hud.Target;
        var cur = t?.TargetHandle ?? 0;
        if (cur != _lastTarget)
        {
            _lastTarget = cur;
            if (cur != 0) RequestEnchantUpdate(cur);
            if (t != null)
                t.SetBuffs(EntityBuffs.Snapshot((int)cur));
        }
        // Expirations locales (le 300 serveur suit — anticipation).
        _pruneTimer += delta;
        if (_pruneTimer >= 1.0)
        {
            _pruneTimer = 0;
            EntityBuffs.PruneAll();
        }
    }

    /// <summary>Abonnements NCID — parse réseau puis application en
    /// file principale (EntityBuffs + repeints ciblés). Sans instance
    /// de pont encore montée, les ops s'accumulent dans
    /// <see cref="Ops"/> et seront appliquées au premier _Process.
    /// Appelé le plus tôt possible (<b>GameScene</b>, avant
    /// ZoneEntryAsync) pour ne pas perdre les 299 du burst
    /// d'entrée.</summary>
    public static void EnsureBound(EdenSession s)
    {
        if (ReferenceEquals(_boundSession, s)) return;
        _boundSession = s;

        void Bind<T>(T msg, Action<BuffBridge, T> act)
            where T : NetMessage, new() =>
            s.OnZone(msg.Opcode, r =>
            {
                var m = new T();
                try { m.Deserialize(r); }
                catch (Exception ex)
                {
                    Ops.Enqueue(_ => GD.PrintErr(
                        $"[buff] parse {m.Name} : {ex.Message}"));
                    return;
                }
                Ops.Enqueue(b => act(b, m));
            });

        Bind(new ZoneEnchantAdd(), (b, m) =>
        {
            EntityBuffs.ApplyAdd(m);
            b.RepaintTarget(m.Handle);
        });
        Bind(new ZoneEnchantRemove(), (b, m) =>
        {
            EntityBuffs.ApplyRemove(m);
            b.RepaintTarget(m.Handle);
        });
        Bind(new ZoneEnchantUpdate(), (b, m) =>
        {
            EntityBuffs.ApplyUpdate(m);
            b.RepaintTarget(m.Handle);
        });
        Bind(new ZoneEnchantReset(), (b, m) =>
        {
            EntityBuffs.ApplyReset(m);
            b.RepaintTarget(m.Handle);
        });
        Bind(new ZoneTeamMemberEnchant(), (_, m) =>
            EntityBuffs.ApplyTeamMember(m));
        Bind(new ZoneEnchantPause(), (b, m) =>
        {
            EntityBuffs.ApplyPause(m.Handle, m.EffectId, true);
            b.RepaintTarget(m.Handle);
        });
        Bind(new ZoneEnchantStart(), (b, m) =>
        {
            EntityBuffs.ApplyPause(m.Handle, m.EffectId, false);
            b.RepaintTarget(m.Handle);
        });

        GD.Print("[buff] binds 299/300/301/315/372/445/446 posés " +
            $"(self={s.SelectedCharId})");
    }

    /// <summary>Repeint la target frame si le handle concerné est la
    /// cible affichée.</summary>
    private void RepaintTarget(int handle)
    {
        var t = _hud.Target;
        if (t != null && t.TargetHandle == (uint)handle)
            t.SetBuffs(EntityBuffs.Snapshot(handle));
    }

    /// <summary>18 EnchantUpdate [u32 handle] → 301 de la cible.</summary>
    private void RequestEnchantUpdate(uint handle)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var send = s.SendZoneAsync(
            new ZoneEnchantUpdateRequest { TargetHandle = handle },
            CancellationToken.None);
        _ = send.ContinueWith(t => GD.PrintErr(
                $"[buff] req18 0x{handle:x8} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
