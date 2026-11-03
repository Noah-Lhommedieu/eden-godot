//  ZoneEntryFlow.Treasure.Roll.cs — roll de loot d'équipe
//  (need/greed/pass) sur les trésors au sol.
//
//  Port de PlayerPickUpTreasure (roll-path) + CheckRollItem +
//  StartRollTreasure + CEventRollTreasure natifs — voir
//  RollTreasureService pour les références d'adresses.
//
//  Déclenchement (PlayerPickUpTreasure @0x80b3130) : un membre de
//  team tente de ramasser un trésor non roulé ; si l'équipe a ≥2
//  membres sur le node, que la règle de loot n'est pas
//  « Taunt-Exclusive » (team+0x64 == 3) et que la qualité de l'item
//  (clone+0x2c) passe le seuil RollRule (team+0x58 : 0=toujours,
//  1=quality!=0, 2=quality∉{0,2} — vérifié désasm), le trésor passe
//  en roll : SetRoll + 520 ClientRollTreasure à chaque participant
//  + CEventRollTreasure (120 s, S_Parameter[341]).
//
//  Réponse NCID 193 [u32 treasureId][s16 choice] (choice :
//  0=pass/1=need/2=greed) → CheckRollItem @0x84bc220 : jet 0-98
//  dédupliqué, texte 316 à la team (0xb7b/0xb7d/0xb7c), puis
//  résolution quand tous ont répondu — sinon CEventRollTreasure
//  (120 s) force pass les absents.
//
//  Résolution (StartRollTreasure @0x84bb560) : need triés décroissant
//  puis greed — le premier gagne → RollPickUpTreasure @0x80b2210
//  (insertion serveur + 386 + despawn 330) ; tout le monde passe →
//  texte 0xb7a + SetRollGiveUp (le trésor repasse en pickup normal).

using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Gate « trésor éligible au roll » de
    /// PlayerPickUpTreasure — retourne vrai quand le pickup doit
    /// lancer un roll au lieu de la prise directe. Conditions
    /// natives : trésor non roulé, team non null, règle loot != 3
    /// (Taunt-Exclusive, team+0x64), ≥2 membres de la team sur ce
    /// node, quality (clone+0x2c) selon RollRule (team+0x58), et
    /// masque d'entraînement de l'item (S_Item+0x2c) non
    /// trivial — le natif n'exclut du chemin pet-pickup que
    /// hors {5,7,0xc,0xe,0x12}, on réplique le gate tel quel.</summary>
    private bool TryRollGate(EntryState st,
        GroundTreasureService.Treasure t,
        out Eden.Core.Game.Party.TeamEntity team)
    {
        team = null!;
        if (t.IsRolled) return false;
        team = _services.Teams.GetTeamOf((int)st.CharId)!;
        if (team == null || team.LootRuleValue == 3) return false;
        // iVar64 = nb de membres de la team sur le node du trésor —
        // set local car un `out` ne peut pas être capté par lambda.
        var memberIds = team.Members.Select(m => m.CharacterId)
            .ToHashSet();
        var onNode = _states.Values.Count(o => o.Migrated &&
            o.NodeId == t.NodeId && o.InstanceGroup == t.InstanceKey &&
            memberIds.Contains((int)o.CharId));
        if (onNode < 2) return false;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == t.ItemId);
        // S_Item+0x2c = TrainMask — exclu du chemin « param_5 »
        // (pickup par elf, non implémenté) ; gate replié tel quel.
        if (def is { TrainMask: 5 or 7 or 0xc or 0xe or 0x12 })
            return false;
        // team+0x58 = RollRule (seuil : 0=White/1=Green/2=Blue) ×
        // clone+0x2c (quality) — conditions décodées dans
        // PlayerPickUpTreasure @0x80b3130.
        var q = def?.Quality ?? 0;
        return team.RollRule switch
        {
            1 => q != 0,
            2 => q != 0 && q != 2,
            _ => team.RollRule == 0,
        };
    }

    /// <summary>Démarre le roll : participants = membres de la team
    /// sur le node, `SetRoll`, 520 à chacun, CEventRollTreasure
    /// (120 s → non-répondants pass).</summary>
    private async Task StartTreasureRollAsync(EntryState st,
        GroundTreasureService.Treasure t,
        Eden.Core.Game.Party.TeamEntity team, CancellationToken token)
    {
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == t.ItemId);
        t.IsRolled = true;
        var participants = _states.Values.Where(o => o.Migrated &&
                o.NodeId == t.NodeId &&
                o.InstanceGroup == t.InstanceKey &&
                o.Session != null &&
                team.Members.Any(m =>
                    m.CharacterId == (int)o.CharId))
            .ToList();
        _services.RollTreasure.Create(t.Id, t.NodeId, t.InstanceKey,
            def?.Name ?? t.ItemId.ToString(),
            participants.Select(p => (int)p.CharId));

        // 520 ClientRollTreasure — dump fixe du clone affiché dans la
        // fenêtre de roll (ctor natif lit chaque champ de CCloneItem) :
        // [u32 rollId][u16 itemId][s16 quality +0x2c][s16 count +0xc]
        // [s16 +0x1c][s16 durability +0xe][6×s16 embedded vec+0x40]
        // [9×u16 extra_combo vec+0x34][s16 +0x2e][i32 +0x50]
        // [u16 +0x58][u32 +0x5c] puis, dans l'ordre mémoire du
        // Serialize (+0x40,+0x46,+0x42,+0x48,+0x44,+0x4a) :
        // [u16 +0x60][u16 +0x66][u16 +0x62][u16 +0x68][u16 +0x64]
        // [u16 +0x6a].
        var pkt = new CNC_CZ_ClientRollTreasure
        {
            Field1 = t.Id,
            Field2 = (short)t.ItemId,
            Field3 = (short)(def?.Quality ?? 0),   // clone+0x2c quality
            Field4 = (short)t.Count,               // clone+0xc counts
            Field5 = 0,                            // clone+0x1c (non mappé)
            Field6 = (short)(def?.MaxDurability ?? 0), // clone+0xe durability
            // vec+0x40 = embedded_id1..6 — −1 quand vides (ctor clone).
            Field7 = -1, Field8 = -1, Field9 = -1,
            Field10 = -1, Field11 = -1, Field12 = -1,
            // vec+0x34 = extra_combo_id1..9 — 0.
            Field13 = 0, Field14 = 0, Field15 = 0, Field16 = 0,
            Field17 = 0, Field18 = 0, Field19 = 0, Field20 = 0,
            Field21 = 0,
            Field22 = 0,   // clone+0x2e plus_parameter
            Field23 = 0,   // clone+0x50 (color i64 low)
            Field24 = 0,   // attrs+0x58 train_level
            Field25 = 0,   // attrs+0x5c train_exp
            Field26 = 0, Field27 = 0, Field28 = 0, // +0x60/+0x66/+0x62
            Field29 = 0, Field30 = 0, Field31 = 0, // +0x68/+0x64/+0x6a
        };
        var (rollOp, rollPayload) = PreparePacket(pkt);
        foreach (var p in participants)
            if (p.Session is { } ps)
                await SendPreparedToAsync(ps, rollOp, rollPayload, token);

        EdenLog.Write($"[Zone] roll treasure {t.Id} item={t.ItemId} " +
            $"node={t.NodeId} participants={participants.Count}");

        // CEventRollTreasure phase 1 → re-arm S_Parameter[341] = 120 s,
        // puis UpdateRoll : les non-répondants passent.
        var tid = t.Id;
        ScheduleOnLoop(
            TimeSpan.FromSeconds(RollTreasureService.RollSeconds),
            async ct =>
            {
                if (!_services.RollTreasure.TryGet(tid, out _))
                    return;
                await ResolveTreasureRollAsync(tid, ct);
            });
    }

    /// <summary>NCID 193 `ZoneServerRollTreasure` [u32 rollId]
    /// [s16 choice] — réponse du client à la fenêtre de roll
    /// (<c>CheckRollItem</c> natif : membre de team requis, jet
    /// dédupliqué, texte de broadcast team).</summary>
    private async Task HandleRollTreasureAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6) return;
        var rollId = BitConverter.ToUInt32(payload, 0);
        var choice = BitConverter.ToInt16(payload, 4);
        if (choice is < 0 or > 2) return;
        var (result, roll) = _services.RollTreasure.CheckRoll(rollId,
            (int)st.CharId, choice);
        if (result is RollTreasureService.RollCheckResult.NotParticipant
            or RollTreasureService.RollCheckResult.AlreadyAnswered)
            return;

        // Texte natif à la team : index par choix
        // (0→0xb7b pass, 1→0xb7d need, 2→0xb7c greed) avec le corps
        // formaté "%s,%d,%s" = nom, jet, nom d'item
        // (CCharacter::TextOutputIndex @0x8429b20 — Field2 = LPVprintf).
        var textId = (short)(choice == 1 ? 0xb7d
            : choice == 2 ? 0xb7c : 0xb7b);
        if (_services.RollTreasure.TryGet(rollId, out var r))
            await BroadcastRollTextAsync(
                r.Entries.Select(e => e.CharId).ToHashSet(),
                $"{st.CharName},{roll},{r.ItemName}", textId, token);

        if (result == RollTreasureService.RollCheckResult.Complete)
            await ResolveTreasureRollAsync(rollId, token);
    }

    /// <summary>`StartRollTreasure` + `UpdateRoll` — non-répondants
    /// passés au forcé (UpdateRoll) puis gagnant = meilleur need,
    /// sinon meilleur greed ; all-pass → giveup.</summary>
    private async Task ResolveTreasureRollAsync(uint rollId,
        CancellationToken token)
    {
        // Participants capturés AVANT la résolution — le roll est
        // retiré du registre après (RemoveRollItem natif).
        var participantIds = _services.RollTreasure.TryGet(rollId,
            out var rr) ? rr.Entries.Select(e => e.CharId).ToHashSet()
            : null;
        var winner = _services.RollTreasure.Resolve(rollId);
        _services.RollTreasure.Remove(rollId);
        if (!_services.GroundTreasure.TryGet(rollId, out var t))
            return;

        if (winner is not { } winnerId)
        {
            // All-pass — SetRollGiveUp + texte 0xb7a à la team : le
            // trésor redevient ramassable normalement (fenêtre
            // propriétaire déjà gérée par le cycle trésor).
            t.IsRolled = false;
            t.IsPublic = true;
            await BroadcastRollTextAsync(participantIds, "", 0xb7a,
                token);
            return;
        }

        // RollPickUpTreasure natif : treasure+0x60 = winner puis
        // pickup serveur — insertion directe dans le sac du gagnant
        // + notification ; en échec le trésor reste au sol, propriété
        // du gagnant (il pourra le prendre à la main).
        t.OwnerCharId = winnerId;
        t.IsRolled = false;
        var winnerState = _states.Values.FirstOrDefault(o =>
            (int)o.CharId == winnerId && o.Session != null);
        if (winnerState == null)
        {
            EdenLog.Write($"[Zone] roll {rollId} : gagnant {winnerId} " +
                "hors-ligne — trésor conservé");
            return;
        }
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, _, _, _) = await _services.Inventory.AddItemAsync(
            winnerId, t.ItemId, t.Count, 0, 0, false, touched);
        if (!ok)
        {
            EdenLog.Write($"[Zone] roll {rollId} : sac du gagnant " +
                $"{winnerId} plein — trésor conservé");
            return;
        }
        _services.GroundTreasure.TryRemove(rollId, out _);
        var ws = winnerState.Session!;
        await NotifyItemSlotsAsync(ws, winnerState, touched, token);
        await ws.SendAsync(new CNC_CZ_ClientTreasurePickupOK
        {
            Field1 = winnerState.CharId, Field2 = (ushort)t.ItemId,
            Field3 = (short)t.Count,
        }, token);
        await BroadcastToNodeAsync(t.NodeId, t.InstanceKey, 330,
            Codecs.TreasureCodec.BuildTreasureDisappear(rollId));
        _services.NotifyItemChanged(winnerId, t.ItemId);
        EdenLog.Write($"[Zone] roll {rollId} : item={t.ItemId} " +
            $"x{t.Count} attribué à char={winnerId}");
    }

    /// <summary>`316 TextOutputIndex` aux sessions des participants
    /// d'un roll (le natif itère les membres de la team — nos
    /// participants sont déjà restreints au node).</summary>
    private async Task BroadcastRollTextAsync(
        HashSet<int>? charIds, string args, short textId,
        CancellationToken token)
    {
        if (charIds == null) return;
        var (op, payload) = PreparePacket(new CNC_CZ_ClientTextOutputIndex
        { Field1 = textId, Field2 = args });
        foreach (var (sid, ost) in _states)
        {
            if (!charIds.Contains((int)ost.CharId)) continue;
            if (ost.Session is not { } s) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }
}
