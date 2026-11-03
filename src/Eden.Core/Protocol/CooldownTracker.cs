using System;
using System.Collections.Generic;

namespace EdenClient.Protocol;

/// <summary>
/// Cooldowns actifs du perso, alimentés par le NCID 368
/// <c>ClientUpdateCoolDown</c> — snapshot complet
/// <c>[u8 n]{u8 kind|s16 id|s16 aux|i32 restant en 1/10 s}</c> :
/// kind 0 = groupe de cooldown d'objet (id = groupe S_Item col22,
/// aux = itemId déclencheur), kind 1 = sort (id = spellId). Le serveur
/// n'envoie pas d'update à l'échéance : le client compte à rebours sur
/// l'horloge locale à partir du restant reçu.
/// </summary>
public static class CooldownTracker
{
    private readonly record struct Entry(long EndMs, long TotalMs);

    private static readonly Dictionary<short, Entry> Items = new();
    private static readonly Dictionary<short, Entry> Spells = new();
    private static readonly object Lock = new();

    /// <summary>Remplace l'état par le snapshot du 368. Le total d'un
    /// cooldown est le restant vu au premier snapshot qui l'arme (sert
    /// au balayage proportionnel de l'overlay).</summary>
    public static void Apply(ZoneUpdateCoolDown m)
    {
        var now = Environment.TickCount64;
        lock (Lock)
        {
            Merge(Items, m.Entries, 0, now);
            Merge(Spells, m.Entries, 1, now);
        }
    }

    private static void Merge(Dictionary<short, Entry> map,
        List<(byte Kind, short Id, short Aux, int RemainDs)> entries,
        byte kind, long now)
    {
        var seen = new HashSet<short>();
        foreach (var (k, id, _, ds) in entries)
        {
            if (k != kind) continue;
            seen.Add(id);
            var end = now + ds * 100L;
            map[id] = map.TryGetValue(id, out var old) &&
                      old.EndMs > now && end <= old.EndMs + 300
                ? old with { EndMs = end }
                : new Entry(end, ds * 100L);
        }
        foreach (var id in new List<short>(map.Keys))
            if (!seen.Contains(id)) map.Remove(id);
    }

    /// <summary>Restant / total en ms du groupe d'objet ; false si libre.</summary>
    public static bool Item(int group, out long remainMs, out long totalMs) =>
        Query(Items, (short)group, out remainMs, out totalMs);

    /// <summary>Restant / total en ms d'un sort ; false si libre.</summary>
    public static bool Spell(int spellId, out long remainMs,
        out long totalMs) =>
        Query(Spells, (short)spellId, out remainMs, out totalMs);

    private static bool Query(Dictionary<short, Entry> map, short id,
        out long remain, out long total)
    {
        remain = total = 0;
        lock (Lock)
        {
            if (!map.TryGetValue(id, out var e)) return false;
            remain = e.EndMs - Environment.TickCount64;
            total = e.TotalMs;
            if (remain > 0) return true;
            map.Remove(id);
            return false;
        }
    }
}
