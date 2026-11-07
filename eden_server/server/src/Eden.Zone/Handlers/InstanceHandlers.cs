using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Instances : champs de bataille (join/leave/liste), salles de
/// joueur (EnterRoom), achat de sorts chez le maître de classe.
/// </summary>
public static class InstanceHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // BattlefieldJoin [short bfId, short tier?, short team?] —
        // relais natif zone→mission op149 `[i32 char][s16 bf][s16 side]`
        // : la mission file/matchmake (BattlefieldService). Si la
        // mission est hors ligne, repli sur l'instance solo (dungeon)
        // pour que le champ reste au moins entrant.
        d.RegisterChar(() => new CNE_CZ_ZoneServerBattlefieldJoin(),
            async (session, msg, charId, token) =>
            {
                var bfId = msg.Field1;
                var side = msg.Field2;         // camp demandé
                if (s.Mission is { Connected: true } mission)
                {
                    var b = new byte[8];
                    BitConverter.GetBytes(charId).CopyTo(b, 0);
                    BitConverter.GetBytes(bfId).CopyTo(b, 4);
                    BitConverter.GetBytes(side).CopyTo(b, 6);
                    await mission.SendAsync(149, b, token);
                    EdenLog.Write($"[Zone] BattlefieldJoin char={charId} " +
                        $"bf={bfId} side={side} -> mission");
                    return;
                }
                var pc = await s.Characters.GetCharacterByIdAsync(charId);
                var inst = s.Dungeons.Enter(bfId, new[] { charId },
                    pc?.Level ?? 1);
                await session.SendAsync(new CNC_CZ_ClientBattlefieldJoinResult
                {
                    Field1 = bfId,
                    Field4 = inst?.InstanceId ?? 0,
                }, token);
                EdenLog.Write($"[Zone] BattlefieldJoin char={charId} bf={bfId} " +
                    $"-> instance={inst?.InstanceId.ToString() ?? "refusée"}");
            });

        // BFListQuery [short ?, short ?] — liste des champs de bataille
        d.Register(() => new CNE_CZ_ZoneServerBFListQuery(),
            async (session, msg, token) =>
                // TODO(phase reverse): sérialiser les entrées boucle (bfId, nom, min/max)
                await session.SendAsync(new CNC_CZ_ClientBattlefieldList(), token));

        d.RegisterChar(() => new CNE_CZ_ZoneServerBattlefieldLeave(),
            (session, msg, charId, token) =>
            {
                if (s.Dungeons.GetInstanceOf(charId) is { } inst)
                {
                    s.Dungeons.Abandon(inst.InstanceId);
                    EdenLog.Write($"[Zone] BattlefieldLeave char={charId} inst={inst.InstanceId}");
                }
                return Task.CompletedTask;
            });

        // EnterRoom [int roomId][s16 hotel_type] — intercepté par
        // ZoneEntryFlow (HandleEnterRoomAsync = EnterRoomCheck natif
        // @0x814eaf0 : validations registre + relais mission).
        // Enregistrement inerte (documentation NCID).
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterRoom(),
            (session, msg, charId, token) => Task.CompletedTask);

        // BuySpell [short spellId] — MORT EN NATIF : Execute zone
        // @0x0823f570 = corps vide (l'apprentissage passe par le flux
        // spellmaster CSpellMasterFactory, pas ce NCID). L'ancien
        // handler spéculatif (débit d'or + 280/286) est retiré :
        // aucun paquet spéculatif vers le client.
        d.RegisterChar(() => new CNE_CZ_ZoneServerBuySpell(),
            (session, msg, charId, token) => Task.CompletedTask);
    }
}
