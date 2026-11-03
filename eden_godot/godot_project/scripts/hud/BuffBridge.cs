using System.Collections.Concurrent;
using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → buffs/debuffs (Vague 2) — même pattern de binding
/// paresseux que <see cref="HudNetBridge"/> : la session zone est
/// découverte via <see cref="SessionCarry.Session"/> puis les NCIDs
/// enchant sont abonnés une fois.
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
/// <see cref="_pending"/> drainée ici sur le thread principal.
/// </summary>
public partial class BuffBridge : Node
{
    private readonly HudRoot _hud;
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    private uint _lastTarget;
    private double _pruneTimer;

    public BuffBridge(HudRoot hud)
    {
        _hud = hud;
        Name = "BuffBridge";
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
                GD.PrintErr($"[buff] dispatch : {ex}");
            }
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
    /// file principale (EntityBuffs + repeints ciblés).</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;

        s.OnZone(299, r =>
        {
            var m = new ZoneEnchantAdd();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle,
                () => EntityBuffs.ApplyAdd(m)));
        });
        s.OnZone(300, r =>
        {
            var m = new ZoneEnchantRemove();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle,
                () => EntityBuffs.ApplyRemove(m)));
        });
        s.OnZone(301, r =>
        {
            var m = new ZoneEnchantUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle,
                () => EntityBuffs.ApplyUpdate(m)));
        });
        s.OnZone(315, r =>
        {
            var m = new ZoneEnchantReset();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle,
                () => EntityBuffs.ApplyReset(m)));
        });
        s.OnZone(372, r =>
        {
            var m = new ZoneTeamMemberEnchant();
            m.Deserialize(r);
            _pending.Enqueue(() => EntityBuffs.ApplyTeamMember(m));
        });
        s.OnZone(445, r =>
        {
            var m = new ZoneEnchantPause();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle, () =>
                EntityBuffs.ApplyPause(m.Handle, m.EffectId, true)));
        });
        s.OnZone(446, r =>
        {
            var m = new ZoneEnchantStart();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyAndRepaint(m.Handle, () =>
                EntityBuffs.ApplyPause(m.Handle, m.EffectId, false)));
        });

        // Self = charId du perso choisi (le « handle » joueur du
        // wire — les buffs self arrivent en 299/301/315 sur cet id).
        BuffBar.Instance?.SetSelfHandle((int)s.SelectedCharId);
        // Clic droit BuffBar → 85 EnchantCancel.
        if (BuffBar.Instance is { } bar)
            bar.CancelSender = eid => s.SendZoneAsync(
                new ZoneEnchantCancel { EffectId = eid },
                CancellationToken.None);

        GD.Print("[buff] binds 299/300/301/315/372/445/446 posés " +
            $"(self={s.SelectedCharId})");
    }

    /// <summary>Applique la mutation puis repeint la target frame si
    /// le handle concerné est la cible affichée.</summary>
    private void ApplyAndRepaint(int handle, Action apply)
    {
        apply();
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
