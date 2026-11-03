using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Logging;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Items de quête à usage positionnel — partial de
/// ZoneEntryFlow (plan futur-feature/01, étape 1). Le « ItemTrigger »
/// du plan n'existe pas comme commande native : l'usage positionnel
/// des quest items est la commande <c>SummonInEventArea</c>
/// (1074/1075/2045) dont <c>CanExecute</c> @0x08559980 contrôle le node
/// et la clé de position, puis <c>Execute</c> @0x08559b70 invoque le mob
/// à la ligne <c>[position]</c> de la scène. Le lien avec la quête passe
/// ensuite par les chemins existants : kill du mob invoqué →
/// <c>MonsterKilled</c> (144 MobDieReport), item reçu → 146
/// QuestItemChanged. Règles pures : <see cref="QuestItemRules"/>.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Pré-contrôle <c>CanExecute</c> des commandes d'usage de
    /// l'enchant d'un item (natif DoUseItem : CheckExecute avant la
    /// consommation). Refus = false, l'appelant garde l'item.</summary>
    private async Task<bool> CanUseEnchantAsync(NetSession session,
        EntryState st, int enchantId, CancellationToken token)
    {
        foreach (var c in _services.Enchants.GetUseCommands(enchantId))
        {
            var name = EnchantCommands.Names.TryGetValue(c.CommandId,
                out var n) ? n : "";
            if (!QuestItemRules.IsSummonCommand(name)) continue;
            var spec = QuestItemRules.ParseSummon(
                c.Args.Select(v => (int)v).ToArray());
            if (spec == null) continue;
            var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
            var pos = QuestItemRules.ResolvePosition(scene,
                spec.PositionKey);
            switch (QuestItemRules.CheckSummon(spec, st.NodeId,
                        pos != null))
            {
                case QuestItemRules.Refusal.WrongNode:
                    // 0x5c0 : « cet item ne s'utilise pas ici » natif.
                    await SendTextIndexAsync(session,
                        QuestItemRules.TextWrongNode, token);
                    EdenLog.Write($"[QuestItem] char={st.CharId} " +
                        $"enchant={enchantId} : node requis " +
                        $"{spec.NodeId} ≠ {st.NodeId} — refus");
                    return false;
                case QuestItemRules.Refusal.PositionMissing:
                    EdenLog.Write($"[QuestItem] char={st.CharId} " +
                        $"enchant={enchantId} : position " +
                        $"{spec.PositionKey} absente du node " +
                        $"{spec.NodeId} — refus");
                    return false;
            }
        }
        return true;
    }

    /// <summary>Exécute <c>SummonInEventArea</c> : spawn des mobs de
    /// quête à la ligne [position] (ou à la position du lanceur), sans
    /// respawn, broadcast SeeMob + effet 383, et engagement du lanceur
    /// (BecomeFightingCandicate natif).</summary>
    private async Task SummonInEventAreaAsync(NetSession session,
        EntryState st, int[] args, CancellationToken token)
    {
        var spec = QuestItemRules.ParseSummon(args);
        if (spec == null) return;
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var pos = spec.NodeId > 0
            ? QuestItemRules.ResolvePosition(scene, spec.PositionKey)
            : null;
        // Position : ligne [position] résolue, sinon le lanceur (aire
        // nulle côté natif → coordonnées de l'acteur).
        var (x, y, z, facing) = pos ?? (st.PosX, st.PosZ,
            _services.Live.GroundHeightAt(scene, st.PosX, st.PosZ),
            st.Facing);
        var spawn = new SceneParser.MonsterSpawn
        {
            MonsterId = spec.MobId, Count = spec.Count,
            X = x, Y = y, Z = z, Facing = facing,
        };
        foreach (var e in _services.Live.SpawnGroup(st.NodeId,
                     st.InstanceGroup, spawn))
        {
            var mob = _services.Live.GetMob(e.Handle);
            if (mob == null) continue;
            mob.SummonedByCharId = (int)st.CharId;
            mob.NoRespawn = true;
            await BroadcastSeeMobAsync(st.NodeId, st.InstanceGroup, e,
                token);
            if (spec.EffectId > 0)
                await BroadcastSummonEffectAsync(session, st, spec,
                    e.Handle, token);
            // Le mob invoqué attaque le lanceur (BecomeFightingCandicate)
            // — sauf décor/structure qui n'a pas de FSM de combat.
            if (mob.Def is { IsStructure: false } && st.Session != null)
                await PassiveEngageAsync(st, mob);
        }
        EdenLog.Write($"[QuestItem] char={st.CharId} SummonInEventArea " +
            $"mob={spec.MobId} x{spec.Count} node={st.NodeId} " +
            $"pos={(pos != null ? $"key {spec.PositionKey} ({x:F1},{y:F1})" : "lanceur")}");
    }

    /// <summary>383 ClientPlayEffect du summon (BroadCastToCanSee natif
    /// dans Execute) — même corps que l'effet des pets.</summary>
    private async Task BroadcastSummonEffectAsync(NetSession session,
        EntryState st, QuestItemRules.SummonSpec spec, uint handle,
        CancellationToken token)
    {
        var body = Concat(
            BitConverter.GetBytes(0f),                       // f32 : x
            BitConverter.GetBytes(0f),                       // f32 : y
            BitConverter.GetBytes(0f),                       // f32 : z
            BitConverter.GetBytes(spec.EffectId),            // i32 : effect id
            BitConverter.GetBytes(handle),                   // i32 : cible
            BitConverter.GetBytes((float)spec.EffectScale),  // f32 : échelle
            BitConverter.GetBytes((short)0),                 // str : ""
            BitConverter.GetBytes(0),                        // i32 : +0x20
            new byte[] { 0 });                               // u8  : +0x24
        await session.SendPacketAsync(383, body, token);
        await BroadcastToNodeAsync(session, st, 383, body, token,
            handle: handle);
    }

    private HashSet<int>? _trackedQuestEnchants;

    /// <summary>Enchants référencés par un objectif CheckBuff (calculé
    /// une fois depuis S_Mission — voir <see cref="QuestBuffRules"/>).</summary>
    private HashSet<int> TrackedQuestEnchants =>
        _trackedQuestEnchants ??= QuestBuffRules.TrackedEnchants(
            _services.GameData.Missions);

    /// <summary>Notifie la mission d'un changement de buff suivi par une
    /// quête : commande DBA <c>CheckMissionEnchant &lt;id&gt; &lt;n&gt;</c>
    /// (TC_CheckMissionEnchant @0x081f2960 ; le compteur est une
    /// extension — le natif relit son miroir). No-op hors plage native
    /// ou si aucune quête ne référence l'enchant.</summary>
    private void NotifyQuestEnchant(EntryState st, int enchantId, int count)
    {
        if (!_mission.Connected || !TrackedQuestEnchants.Contains(enchantId))
            return;
        _ = _mission.SendAgentDbaCommandAsync(st.CharId,
            $"CheckMissionEnchant {enchantId} {count}");
    }
}
