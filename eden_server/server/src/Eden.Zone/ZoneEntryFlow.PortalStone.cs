using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Portal Stone / MissionGoto — NCID 160
/// <c>CNE_CZ_ZoneServerMissionGoto</c> wire
/// <c>[s16 missionId][s16 type][s16 npcId][s16 flag]</c>
/// (Deserialize @0x826d610). Execute natif @0x826b970 :
///   - refuse en "newer town" (safe zone) ;
///   - <b>flag == 1</b> = case "Use Portal Stone" : exige l'item
///     <c>0x9c54</c> ou <c>0xa476</c> (CheckItemNum) puis consomme ;
///   - field2 = type de marqueur, field3 = npc cible ;
///   - termine par <c>CZoneServer::TransportCharacterToPoint</c>
///     (téléport aux coords du marqueur/NPC).
/// Implémentation : on retrouve le spawn du NPC (NpcId de scène,
/// S*.ini [npc]) — même map : migration intra-node ; autre map :
/// migration complète via MigrateToNodeAsync.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>MissionGoto — téléportation au NPC de la quête trackée.
    /// La Portal Stone (flag==1) est consommée depuis player_items ;
    /// sans pierre, refus silencieux (comportement natif : bail avant
    /// tout envoi).</summary>
    private async Task HandleMissionGotoAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 8 || st.CharId == 0 || !st.Migrated) return;
        var missionId = BitConverter.ToInt16(payload, 0);  // s16 : mid
        var type = BitConverter.ToInt16(payload, 2);       // s16 : type marqueur
        var npcId = BitConverter.ToInt16(payload, 4);      // s16 : npc cible
        var flag = BitConverter.ToInt16(payload, 6);       // s16 : 1 = portal stone

        // B3 : état « TP libre » = flag GM (Privilege > 0) → gratuit.
        if (flag == 1 && st.Privilege <= 0)
        {
            // Consomme une Portal Stone (0x9c54 ou 0xa476 — ids natifs
            // de l'Execute). Source : la vue native `inventory`
            // (player_items est vide pour les persos natifs).
            // Sans pierre : refus, rien n'est envoyé.
            var items = await _services.InventoryRepo
                .GetLiveItemsAsync((int)st.CharId);
            var stone = items.FirstOrDefault(
                i => i.ItemId is 0x9c54 or 0xa476);
            if (stone == null)
            {
                EdenLog.Write($"[Zone-Entry] MissionGoto refusé " +
                    $"char={st.CharId} — pas de portal stone");
                return;
            }
            await _services.InventoryRepo.RemoveLiveItemAsync(
                (int)st.CharId, stone.Id);
        }

        // La cible : NPC (NpcId de scène) OU monstre objectif
        // (MonsterId — wire live `C1DA` = 56001, un monstre de la
        // quête 1011, lu en s16 signé = -9535). Les ids de scène sont
        // des u16 — comparaison en non signé. D'abord la scène
        // courante, puis toutes les scènes chargées.
        var target = (ushort)npcId;
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var npc = scene?.Npcs.FirstOrDefault(n => n.NpcId == target);
        var mob = npc == null
            ? scene?.Monsters.FirstOrDefault(m => m.MonsterId == target)
            : null;
        var targetNode = (int)st.NodeId;
        float tx = 0, ty = 0, tz = 0;
        if (npc != null) { tx = npc.X; ty = npc.Y; tz = npc.Z; }
        else if (mob != null) { tx = mob.X; ty = mob.Y; tz = mob.Z; }
        if (npc == null && mob == null)
        {
            foreach (var code in _services.Scenes.SceneCodes)
            {
                if (!int.TryParse(code.TrimStart('S'), out var n)) continue;
                var sc = _services.Scenes.GetScene(code);
                npc = sc?.Npcs.FirstOrDefault(n2 => n2.NpcId == target);
                if (npc != null)
                { targetNode = n; tx = npc.X; ty = npc.Y; tz = npc.Z; break; }
                mob = sc?.Monsters.FirstOrDefault(
                    m2 => m2.MonsterId == target);
                if (mob != null)
                { targetNode = n; tx = mob.X; ty = mob.Y; tz = mob.Z; break; }
            }
        }
        if (npc == null && mob == null)
        {
            EdenLog.Write($"[Zone-Entry] MissionGoto char={st.CharId} " +
                $"mid={missionId} cible={npcId} introuvable");
            return;
        }

        EdenLog.Write($"[Zone-Entry] MissionGoto char={st.CharId} " +
            $"mid={missionId} cible={npcId} -> node={targetNode} " +
            $"({tx:F1},{ty:F1}) type={type} flag={flag}");

        // Même node ou node distant : le flux de migration gère la
        // téléportation (le client reçoit la séquence standard).
        await MigrateToNodeAsync(session, st, targetNode, tx, ty, tz, token);
    }
}
