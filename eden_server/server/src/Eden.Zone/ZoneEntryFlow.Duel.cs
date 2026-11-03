// Portée : duel 1v1 — NCIDs entrants 135 DuelRequest
// [i32 cible] et 136 DuelRequestAnswer [s16 accept][i32 demandeur].
// Cycle natif reversé :
//   CPlayerFactory::DuelRequest @0x080f2bf0 — validations (demandeur
//     +0x95c==-1, cible sur le même node non-duelée non-combat,
//     CloseEnough 20u) → 451 [i32 demandeur][str nom] + texte 0x64a.
//   DuelReject @0x080fe680 → 452 [str nomReq][str nomRej][s16] :
//     code 2 au demandeur ("refusé par X"), 0 au refuseur.
//   CDuelFactory::AddToDuel @0x0805d300 — char+0x95c=serial, +0x960=1
//     (pending), 453 DuelBegin[0][0][5][0] aux deux, puis
//     CEventDuelCountdown (+5 s).
//   CEventDuelCountdown::Execute @0x08198340 — +0x960=2 (actif),
//     453 [serial][handleAdversaire][99][1] à chacun, puis
//     CEventDuelRound (+99 s — le 99 = durée du round).
//   CEventDuelRound::Execute @0x08198ef0 → DuelFinish(duelId)
//     @0x0805d670 — %HP comparé → 0/1/3 + broadcast 4/5.
//   DuelFinish(char) @0x0805df80 — mort/quit/teleport : vainqueur
//     [perdantHandle][nomPerdant][""][0], perdant
//     [vainqueurHandle][nomVainqueur][""][1|2], témoins
//     [vainqueurHandle][nomPerdant][nomVainqueur][4].

using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Duels joueur↔joueur (1v1) : requête/réponse, activation
/// après le countdown natif de 5 s, fin par mort/sortie/téléport ou
/// par timeout round de 99 s (comparaison %HP).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Compteur de série des duels — équivalent du SerialNum
    /// d'AllDuelData natif (champ1 du DuelBegin d'activation).</summary>
    private int _duelSerial;

    /// <summary>135 DuelRequest — relais 451 avec les validations de
    /// `CPlayerFactory::DuelRequest` @0x080f2bf0 : demandeur pas déjà
    /// en duel/requête, cible migrée sur le même node/instance, pas en
    /// duel, à portée parlée 20 u (`IGameObj::CloseEnough`).</summary>
    private async Task HandleDuelRequestAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0 || !st.Migrated)
            return;
        var targetId = BitConverter.ToUInt32(payload, 0);
        // char+0x95c != -1 → texte 0x64c ; on n'a pas de texte par
        // chaîne pour chaque refus — le silence natif vaut aussi.
        if (targetId == 0 || targetId == st.CharId ||
            st.DuelOpponentId != 0 || st.DuelPendingTo != 0 ||
            st.Dead) return;
        var tst = FindStateByChar((int)targetId);
        if (tst == null || !tst.Migrated || tst.Dead ||
            tst.NodeId != st.NodeId ||
            tst.InstanceGroup != st.InstanceGroup ||
            tst.DuelOpponentId != 0 || tst.DuelPendingFrom != 0) return;
        var dx = tst.PosX - st.PosX;
        var dz = tst.PosZ - st.PosZ;
        if (dx * dx + dz * dz > 400f) return;   // CloseEnough 20 u²

        // Ops natives : la cible mémorise le demandeur (InviteeOp),
        // le demandeur mémorise la cible (RequestOp).
        tst.DuelPendingFrom = st.CharId;
        st.DuelPendingTo = targetId;

        var tsession = _services.Sessions.FindByCharacter((int)targetId);
        if (tsession != null)
            await tsession.SendAsync(new CNC_CZ_ClientDuelRequest
            {
                Field1 = (int)st.CharId,        // i32 : charId demandeur
                Field2 = st.CharName ?? "",     // str : nom demandeur
            }, token);
        // Texte 0x64a natif au demandeur ("%s" = nom de la cible).
        await SendTextIndexArgsAsync(session, 0x64a,
            tst.CharName ?? "", token);
        EdenLog.Write($"[Zone] DuelRequest {st.CharId} → {targetId}");
    }

    /// <summary>136 DuelRequestAnswer — `accept != 0` : AddToDuel
    /// (453 [0][0][5][0] ×2, pending 5 s). `accept == 0` : DuelReject
    /// (452 [nomReq][nomRej][2] au demandeur, […][0] au refuseur).</summary>
    private async Task HandleDuelAnswerAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6 || st.CharId == 0) return;
        var accept = BitConverter.ToInt16(payload, 0) != 0;
        var requesterId = BitConverter.ToInt32(payload, 2);
        if (st.DuelPendingFrom != (uint)requesterId) return;
        st.DuelPendingFrom = 0;
        var rst = FindStateByChar(requesterId);
        var rsession = _services.Sessions.FindByCharacter(requesterId);
        var reqName = rst?.CharName ?? "";
        var rejName = st.CharName ?? "";

        if (!accept)
        {
            if (rst != null) rst.DuelPendingTo = 0;
            var toRequester = new CNC_CZ_ClientDuelRequestResult
            {
                Field1 = reqName,   // str : nom du demandeur
                Field2 = rejName,   // str : nom du refuseur
                Field3 = 2,         // s16 : 2 = refus subi
            };
            var toRejecter = new CNC_CZ_ClientDuelRequestResult
            {
                Field1 = reqName,
                Field2 = rejName,
                Field3 = 0,         // s16 : 0 = refus émis
            };
            if (rsession != null)
                await rsession.SendAsync(toRequester, token);
            await session.SendAsync(toRejecter, token);
            EdenLog.Write($"[Zone] Duel reject {requesterId} vs " +
                $"{st.CharId}");
            return;
        }

        if (rst == null || rsession == null || !rst.Migrated ||
            rst.Dead || st.Dead ||
            rst.DuelOpponentId != 0 || st.DuelOpponentId != 0)
        {
            return;
        }
        rst.DuelPendingTo = 0;

        // AddToDuel : 453 [0][0][5][0] aux deux — notification
        // d'acceptation, le vrai begin arrive au countdown (+5 s).
        var acceptNotify = new CNC_CZ_ClientDuelBegin
        {
            Field1 = 0, Field2 = 0,
            Field3 = 5,               // s16 : décompte (s)
            Field4 = 0,
        };
        await session.SendAsync(acceptNotify, token);
        await rsession.SendAsync(acceptNotify, token);

        var serial = ++_duelSerial;
        st.DuelOpponentId = (uint)requesterId;
        st.DuelOpponentName = reqName;
        st.DuelSerial = serial;
        rst.DuelOpponentId = st.CharId;
        rst.DuelOpponentName = rejName;
        rst.DuelSerial = serial;
        // DuelActive reste faux jusqu'au countdown (char+0x960 1→2).

        ScheduleOnLoop(TimeSpan.FromSeconds(5), _ =>
            DuelActivateAsync(serial, st.CharId, (uint)requesterId));
        EdenLog.Write($"[Zone] Duel {requesterId} vs {st.CharId} " +
            $"serial={serial}");
    }

    /// <summary>`CEventDuelCountdown::Execute` (+5 s) : les deux
    /// duelistes passent actifs (+0x960=2) et reçoivent chacun
    /// `DuelBegin [serial][handleAdversaire][99][1]` ; le round de
    /// 99 s est armé (timeout → comparatif %HP).</summary>
    private async Task DuelActivateAsync(int serial, uint chA, uint chB)
    {
        var a = FindStateByChar((int)chA);
        var b = FindStateByChar((int)chB);
        if (a == null || b == null || !a.Migrated || !b.Migrated ||
            a.DuelSerial != serial || b.DuelSerial != serial ||
            a.DuelOpponentId != chB || b.DuelOpponentId != chA) return;
        a.DuelActive = b.DuelActive = true;
        var sa = _services.Sessions.FindByCharacter((int)chA);
        var sb = _services.Sessions.FindByCharacter((int)chB);
        if (sa != null)
            await sa.SendAsync(new CNC_CZ_ClientDuelBegin
            {
                Field1 = serial,          // i32 : serial du duel
                Field2 = (int)chB,        // i32 : handle adversaire
                Field3 = 99,              // s16 : durée round (s)
                Field4 = 1,               // s16 : flag actif
            }, CancellationToken.None);
        if (sb != null)
            await sb.SendAsync(new CNC_CZ_ClientDuelBegin
            {
                Field1 = serial,
                Field2 = (int)chA,
                Field3 = 99,
                Field4 = 1,
            }, CancellationToken.None);
        ScheduleOnLoop(TimeSpan.FromSeconds(99), _ =>
            DuelTimeoutAsync(serial, chA, chB));
    }

    /// <summary>`DuelFinish(duelId)` du timeout round : le plus haut
    /// %HP gagne — résultats 0/1 aux duellistes + broadcast 4
    /// (X bat Y) ; égalité → 3/3 + broadcast 5. Adversaire absent →
    /// séquence forfait (idem mort).</summary>
    private async Task DuelTimeoutAsync(int serial, uint chA, uint chB)
    {
        var a = FindStateByChar((int)chA);
        var b = FindStateByChar((int)chB);
        if (a == null || b == null ||
            a.DuelSerial != serial || b.DuelSerial != serial ||
            !a.DuelActive || !b.DuelActive) return;
        a.DuelActive = b.DuelActive = false;

        var pa = a.PlayerMaxHp > 0 ? a.PlayerHp * 100 / a.PlayerMaxHp : 0;
        var pb = b.PlayerMaxHp > 0 ? b.PlayerHp * 100 / b.PlayerMaxHp : 0;
        var sa = _services.Sessions.FindByCharacter((int)chA);
        var sb = _services.Sessions.FindByCharacter((int)chB);

        var outcome = DuelOutcome(pa, pb);
        if (outcome == 0)
        {
            // Égalité : 3/3 aux duellistes + (0, nomA, nomB, 5) aux
            // témoins (branche else de DuelFinish).
            if (sa != null)
                await sa.SendAsync(new CNC_CZ_ClientDuelResult
                {
                    Field1 = (int)chB, Field2 = b.CharName ?? "",
                    Field3 = "", Field4 = 3,
                }, CancellationToken.None);
            if (sb != null)
                await sb.SendAsync(new CNC_CZ_ClientDuelResult
                {
                    Field1 = (int)chA, Field2 = a.CharName ?? "",
                    Field3 = "", Field4 = 3,
                }, CancellationToken.None);
            await BroadcastDuelResultAsync(a, sa, sb,
                new CNC_CZ_ClientDuelResult
                {
                    Field1 = 0, Field2 = a.CharName ?? "",
                    Field3 = b.CharName ?? "", Field4 = 5,
                });
        }
        else
        {
            var winner = outcome == 1 ? a : b;
            var loser = outcome == 1 ? b : a;
            var ws = outcome == 1 ? sa : sb;
            var ls = outcome == 1 ? sb : sa;
            await EmitDuelResultAsync(winner, loser, ws, ls, 1);
        }
        ClearDuel(a);
        ClearDuel(b);
    }

    /// <summary>Comparatif %HP natif du timeout round : 0 = égalité
    /// (résultats 3/3 + broadcast 5), 1 = A gagne, 2 = B gagne
    /// (0/1 + broadcast 4).</summary>
    internal static int DuelOutcome(long pctA, long pctB)
        => pctA == pctB ? 0 : pctA > pctB ? 1 : 2;

    /// <summary>`DuelFinish(char)` natif — fin par mort, déconnexion
    /// ou téléport de <paramref name="st"/> (le perdant). Vainqueur :
    /// `[perdantHandle][nomPerdant][""][0]` ; perdant :
    /// `[vainqueurHandle][nomVainqueur][""][1|2]` ; témoins :
    /// `[vainqueurHandle][nomPerdant][nomVainqueur][4]`.
    /// Renvoie true si un duel a été soldé.</summary>
    private async Task<bool> FinishDuelAsync(NetSession session,
        EntryState st)
    {
        var oppId = st.DuelOpponentId;
        if (oppId == 0) return false;
        var active = st.DuelActive;
        var loserName = st.CharName ?? "";
        var winnerName = st.DuelOpponentName;
        ClearDuel(st);

        var ost = FindStateByChar((int)oppId);
        var osession = _services.Sessions.FindByCharacter((int)oppId);
        if (ost != null)
        {
            winnerName = ost.CharName ?? winnerName;
            ClearDuel(ost);
        }

        // Vainqueur (adversaire) : "vous avez battu <perdant>".
        if (osession != null && ost is { Migrated: true })
            await osession.SendAsync(new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)st.CharId,   // i32 : handle du perdant
                Field2 = loserName,        // str : nom du perdant
                Field3 = "",
                Field4 = 0,                // s16 : 0 = victoire
            }, CancellationToken.None);
        // Perdant : "vaincu par <vainqueur>" — 1 duel actif, 2 annulé
        // (le paquet peut échouer sur session morte : on logge et on
        // poursuit le broadcast comme le natif).
        try
        {
            await session.SendAsync(new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)oppId,       // i32 : handle du vainqueur
                Field2 = winnerName,       // str : nom du vainqueur
                Field3 = "",
                Field4 = (short)(active ? 1 : 2),
            }, CancellationToken.None);
        }
        catch { }
        // Témoins : "vainqueur bat perdant" (code 4).
        await BroadcastDuelResultAsync(st, session, osession,
            new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)oppId,
                Field2 = loserName,
                Field3 = winnerName,
                Field4 = 4,
            });
        return true;
    }

    /// <summary>Couple vainqueur/perdant → les trois émissions
    /// `DuelResult` natives (0 vainqueur, `loserResult` perdant,
    /// 4 broadcast).</summary>
    private async Task EmitDuelResultAsync(EntryState winner,
        EntryState loser, NetSession? ws, NetSession? ls,
        short loserResult)
    {
        if (ws != null)
            await ws.SendAsync(new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)loser.CharId, Field2 = loser.CharName ?? "",
                Field3 = "", Field4 = 0,
            }, CancellationToken.None);
        if (ls != null)
            await ls.SendAsync(new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)winner.CharId, Field2 = winner.CharName ?? "",
                Field3 = "", Field4 = loserResult,
            }, CancellationToken.None);
        await BroadcastDuelResultAsync(winner, ws, ls,
            new CNC_CZ_ClientDuelResult
            {
                Field1 = (int)winner.CharId,
                Field2 = loser.CharName ?? "",
                Field3 = winner.CharName ?? "",
                Field4 = 4,
            });
    }

    /// <summary>Broadcast `DuelResult` aux témoins — même filtre
    /// node/instance que `BroadcastToNodePeersAsync`, en excluant les
    /// deux sessions participantes (déjà notifiées individuellement,
    /// comme le split NotifyClient/BroadCastToCanSee natif).</summary>
    private async Task BroadcastDuelResultAsync(EntryState st,
        NetSession? s1, NetSession? s2, CNC_CZ_ClientDuelResult pkt)
    {
        var (op, payload) = PreparePacket(pkt);
        foreach (var s in _services.Sessions.All)
        {
            if (s == s1 || s == s2) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            await SendPreparedToAsync(s, op, payload,
                CancellationToken.None);
        }
    }

    /// <summary>Purge des champs duel d'un état (char+0x95c=-1,
    /// +0x960=0 natifs).</summary>
    private static void ClearDuel(EntryState st)
    {
        st.DuelOpponentId = 0;
        st.DuelOpponentName = "";
        st.DuelActive = false;
        st.DuelSerial = 0;
        st.DuelPendingFrom = 0;
        st.DuelPendingTo = 0;
    }
}
