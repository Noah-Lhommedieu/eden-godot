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

        // BuySpell [short spellId] — achat/apprentissage chez le maître
        // de classe. Execute natif vide côté zone (0823f580) : le learn
        // réel passe par CSpellMasterFactory (spell_level++ en DB) et
        // répond 286 ClientGetSpell + attr or.
        // MORT EN NATIF : Execute = corps vide (achat de sort passe
        // par le vendeur/drop, pas ce NCID).
        d.RegisterChar(() => new CNE_CZ_ZoneServerBuySpell(),
            async (session, msg, charId, token) =>
            {
                var spellId = msg.Value;
                var spell = s.GameData.Spells.FirstOrDefault(
                    sp => sp.Id == spellId);
                if (spell == null) return;

                // Coût ~ niveau du sort × 100 or — la vraie formule de
                // prix vit dans le spellshop natif (coût lu depuis
                // S_SpellMaster à terme).
                var cost = Math.Max(1, spell.Value) * 100;
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < cost) return;

                var level = await s.Classes.LearnSpellAsync(charId,
                    spellId);
                s.SpellLevelsDirty?.Invoke(charId);
                if (level <= 0) return;
                var newGold = await s.Characters.AddGoldAsync(charId,
                    -cost);

                // 280 attr=2 — rafraîchit l'or affiché (idem achat shop).
                await session.SendAsync(new CNC_CZ_ClientUpdateAttr
                {
                    Field1 = (int)charId, Field2 = 2,
                    Field3 = (int)newGold, Field4 = 0,
                }, token);

                // 286 ClientGetSpell — format du flux de classe :
                // [s16 spell][s16 spell][s16 level][i32 0].
                await session.SendAsync(new CNC_CZ_ClientGetSpell
                {
                    Field1 = spellId, Field2 = spellId,
                    Field3 = (short)level, Field4 = 0,
                }, token);
                EdenLog.Write($"[Zone] BuySpell char={charId} " +
                    $"spell={spellId} lv={level} cost={cost}");
            });
    }
}
