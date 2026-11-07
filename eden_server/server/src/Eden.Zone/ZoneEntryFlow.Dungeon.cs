using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Entrée des donjons à deux intensités (Solo = node 7xx/8xx, Multi =
/// node 1xx/2xx, cf. <see cref="DungeonCatalog"/>) : le portail de la
/// scène déclenche une offre (9060) au lieu du Event_Transport direct ;
/// le choix (9061) est revalidé puis décompte une entrée du jour
/// (<see cref="DungeonEntryRules.MaxDailyEntries"/> par intensité).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    private DungeonCatalog? _dungeons;
    private DungeonCatalog Dungeons =>
        _dungeons ??= new DungeonCatalog(_services.GameData.Nodes);

    private static bool HasDungeonPass(EntryState st) =>
        st.Statuses.ContainsKey(DungeonEntryRules.DevPassBuffId);

    /// <summary>Node de donjon (catalogue Solo/Multi, ou node de type 5 :
    /// héroïque, épreuve…). Sert la règle « pas de perte d'exp à la
    /// mort en donjon » : le revive fait
    /// <c>expLoss = AppliesExpLoss(type) &amp;&amp; !IsDungeonNode(st.NodeId)</c>.</summary>
    internal bool IsDungeonNode(int nodeId) =>
        Dungeons.ByNode(nodeId) != null || NodeType(nodeId) == 5;

    /// <summary>Coefficient exp/or du node (mode multijoueur).</summary>
    private double DungeonRewardMult(int nodeId) =>
        Dungeons.IsMulti(nodeId) ? DungeonEntryRules.MultiRewardMultiplier : 1.0;

    /// <summary>Passe dev permanente : enchant caché non persisté posé à
    /// chaque entrée de zone (comme le buff de vitesse dev).</summary>
    private async Task ApplyDevDungeonPassAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var id = DungeonEntryRules.DevPassBuffId;
        // EDEN_DEV_DUNGEON_PASS=off : teste le décompte réel.
        if (Environment.GetEnvironmentVariable("EDEN_DEV_DUNGEON_PASS") == "off")
            return;
        if (_services.Enchants.Get(id) == null ||
            st.Statuses.ContainsKey(id)) return;
        st.EntryDirty = true;
        await AddPlayerEnchantAsync(session, st, (int)st.CharId, id,
            -9, 1, token, refreshStats: false, hidden: true,
            persist: false);
    }

    /// <summary>Aire d'arrivée d'une scène pour une clé [normal_area]
    /// (ex. 21 = entrée), null si la clé est absente.</summary>
    private static SceneParser.EventArea? ResolveArrivalArea(
        SceneParser.Scene scene, int key)
    {
        if (key <= 0) return null;
        var eaId = scene.NormalLinks.Where(kv => kv.Value == key)
            .Select(kv => kv.Key).FirstOrDefault();
        return eaId == 0 ? null
            : scene.EventAreas.FirstOrDefault(a => a.Id == eaId);
    }

    private int EntriesLeft(int used) =>
        Math.Max(0, DungeonEntryRules.MaxDailyEntries - used);

    /// <summary>Portail d'un donjon (event dont le Event_Transport vise un
    /// node du catalogue) : envoie l'offre Solo/Multi. Retourne vrai si
    /// l'event est pris en charge (le transport direct n'a pas lieu).</summary>
    private async Task<bool> TryOfferDungeonAsync(NetSession session,
        EntryState st, SceneParser.Scene scene,
        SceneParser.SceneEvent ev, int gatewayAreaId,
        CancellationToken token)
    {
        var d = Dungeons.ByNode(ev.TransportNode);
        if (d == null) return false;
        var minLv = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == d.SoloNode)?.MinLevel ?? 0;
        var cmd = ev.Branches.SelectMany(b => b.Actions)
            .FirstOrDefault(a => a.Name == "Event_Transport");
        if (minLv > 0 && st.Level < minLv)
        {
            await SendTextIndexAsync(session, 5259, token);
            return true;
        }
        var today = DungeonEntryRules.DayIndex(DateTime.Now);
        var unlimited = HasDungeonPass(st);
        var solo = await _services.EvaluateRepo.GetDungeonEntriesAsync(
            (int)st.CharId, d.SoloNode, today);
        var multi = await _services.EvaluateRepo.GetDungeonEntriesAsync(
            (int)st.CharId, d.MultiNode, today);
        st.OfferDungeonKey = d.Key;
        st.OfferArrivalKey = cmd != null && cmd.Args.Length > 1
            ? cmd.Args[1] : 0;
        st.OfferX = st.PosX; st.OfferZ = st.PosZ;
        await session.SendAsync(new Handlers.CNC_CZ_ClientDungeonOffer
        {
            SoloNode = (short)d.SoloNode,
            MultiNode = (short)d.MultiNode,
            SoloLeft = (byte)EntriesLeft(solo),
            MultiLeft = (byte)EntriesLeft(multi),
            Max = DungeonEntryRules.MaxDailyEntries,
            Unlimited = unlimited,
        }, token);
        EdenLog.Write($"[Dungeon] offre char={st.CharId} " +
            $"{d.Name} solo={d.SoloNode}({EntriesLeft(solo)}) " +
            $"multi={d.MultiNode}({EntriesLeft(multi)}) " +
            $"illimité={unlimited}");
        return true;
    }

    /// <summary>9061 [s16 node] : revalide l'offre (même donjon, joueur
    /// toujours près du portail, niveau, entrées du jour) puis migre.</summary>
    private async Task HandleDungeonEnterAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2) return;
        var node = BitConverter.ToInt16(payload, 0);
        var d = Dungeons.ByNode(node);
        var key = st.OfferDungeonKey;
        st.OfferDungeonKey = 0;                      // offre à usage unique
        if (d == null || d.Key != key || st.Dead) return;
        var dx0 = st.PosX - st.OfferX; var dz0 = st.PosZ - st.OfferZ;
        if (dx0 * dx0 + dz0 * dz0 > 30f * 30f) return;
        var minLv = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == node)?.MinLevel ?? 0;
        if (minLv > 0 && st.Level < minLv)
        {
            await SendTextIndexAsync(session, 5259, token);
            return;
        }
        var dest = _services.Scenes.GetScene($"S{node:D3}");
        if (dest == null) return;
        var today = DungeonEntryRules.DayIndex(DateTime.Now);
        if (!HasDungeonPass(st))
        {
            var used = await _services.EvaluateRepo.GetDungeonEntriesAsync(
                (int)st.CharId, node, today);
            if (EntriesLeft(used) <= 0)
            {
                EdenLog.Write($"[Dungeon] refus char={st.CharId} " +
                    $"node={node} : 0 entrée");
                await SendTextIndexAsync(session, 5228, token);
                return;
            }
            var times = await _services.EvaluateRepo.AddDungeonEntryAsync(
                (int)st.CharId, node, today);
            await session.SendAsync(new CNC_CZ_ClientUpdateNodeTimes
            {
                Field1 = (short)node, Field2 = (ushort)times,
            }, token);
        }
        var entry = ResolveArrivalArea(dest, st.OfferArrivalKey);
        float x = 100, y = 100, z = 0;
        if (entry != null) (x, y, z) = (entry.X, entry.Y, entry.Z);
        else if (dest.Positions.Count > 0) (x, y, z) = dest.Positions[0];
        EdenLog.Write($"[Dungeon] entrée char={st.CharId} node={node} " +
            $"{(Dungeons.IsMulti(node) ? "multi" : "solo")} " +
            $"pass={HasDungeonPass(st)}");
        await MigrateToNodeAsync(session, st, node, x, y, z, token);
    }

    /// <summary>9062 : aires de portail de donjon de la scène courante
    /// (gateway dont l'event vise un node du catalogue).</summary>
    private async Task SendDungeonPortalsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        if (scene == null) return;
        var msg = new Handlers.CNC_CZ_ClientDungeonPortals();
        foreach (var gw in scene.Gateways)
        {
            var evId = gw.ScriptEventId is -1 or 0 ? gw.KeyId
                : gw.ScriptEventId;
            if (!scene.Events.TryGetValue(evId, out var ev) ||
                Dungeons.ByNode(ev.TransportNode) == null) continue;
            var a = scene.EventAreas.FirstOrDefault(
                e => e.Id == gw.EventAreaId);
            if (a != null) msg.Portals.Add((a.X, a.Y, a.Z, a.Radius));
        }
        await session.SendAsync(msg, token);
        EdenLog.Write($"[Dungeon] {msg.Portals.Count} portail(s) " +
            $"envoyés node={st.NodeId}");
    }
}
