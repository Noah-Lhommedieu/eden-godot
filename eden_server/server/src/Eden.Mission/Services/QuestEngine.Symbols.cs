using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>Partiel symboles NPC « ! »/« ? » (ZMis 65 → client 345) —
/// jusqu'ici jamais émis par le moteur : les marqueurs ne s'affichaient
/// pas. Règles : <see cref="QuestSymbolRules"/>.</summary>
public sealed partial class QuestEngine
{
    /// <summary>Symbole par PNJ pour ce perso : donneur d'une quête
    /// offrable → 1 ; PNJ de rendu d'une quête active → 2 (objectifs
    /// remplis) ou 3 ; donneur d'une quête active non rendue ici → 3.
    /// Mêmes critères que <see cref="QuestEntries"/>.</summary>
    internal Dictionary<int, short> ComputeNpcSymbols(CharContext c)
    {
        var map = new Dictionary<int, short>();
        foreach (var m in _data.Missions)
        {
            var giver = Col(m, 11);
            var submit = Col(m, 17);
            if (c.Active.TryGetValue(m.Id, out var q))
            {
                if (q.Status == 0xFE) continue;
                if (submit > 0)
                    QuestSymbolRules.Add(map, submit,
                        q.Status == 2 ? QuestSymbolRules.Ready
                            : QuestSymbolRules.InProgress);
                if (giver > 0 && giver != submit)
                    QuestSymbolRules.Add(map, giver,
                        QuestSymbolRules.InProgress);
            }
            else if (giver > 0 && IsOfferable(c, m))
                QuestSymbolRules.Add(map, giver,
                    QuestSymbolRules.Available);
        }
        return map;
    }

    /// <summary>Pousse l'état complet des symboles au perso (65 :
    /// [i32 charUid][s16 node][u16 n][n×(i32 npc, s16 sym)]). La zone
    /// filtre sur les PNJ de sa scène avant le 345.</summary>
    internal async Task PushNpcSymbolsAsync(MissionSession s, CharContext c)
    {
        var map = ComputeNpcSymbols(c);
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(c.CharUid));           // i32 : charUid
        b.AddRange(BitConverter.GetBytes((short)c.NodeId));     // s16 : node
        b.AddRange(BitConverter.GetBytes((ushort)map.Count));   // u16 : n
        foreach (var (npc, sym) in map)
        {
            b.AddRange(BitConverter.GetBytes(npc));             // i32 : npcUid
            b.AddRange(BitConverter.GetBytes(sym));             // s16 : symbole
        }
        await (c.Session ?? s).SendAsync(65, b.ToArray());
    }
}
