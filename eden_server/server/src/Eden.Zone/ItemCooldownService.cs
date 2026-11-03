using System.Collections.Concurrent;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Cooldowns d'items d'usage par personnage — miroir de la map native
/// <c>CCharacter+0x6a8</c> (std::map&lt;short group,
/// CEventCoolDownItemGroup*&gt;) : <c>CheckIsCoolDowning</c> @0x8411480
/// refuse un usage tant que le groupe a du temps restant,
/// <c>AddCoolDownItem</c> @0x84283c0 arme le groupe à l'usage réussi.
/// La durée (S_Item col21 → CItemData+0x58, en 1/10 s) et le groupe
/// (col22 → +0x5c) viennent de la définition — le groupe est PARTAGÉ
/// entre les items, 0 = groupe global.
/// État volatil comme le natif : rien n'est persisté, la map meurt
/// avec la session du perso. Émet NCID 368
/// <c>CNC_CZ_ClientUpdateCoolDown</c>.
/// </summary>
public sealed class ItemCooldownService
{
    /// <summary>charId → (groupe → (itemId déclencheur, expiration)).
    /// Même forme que la std::map native keyed par groupe.</summary>
    private readonly ConcurrentDictionary<int,
        ConcurrentDictionary<short, (int ItemId, DateTime ExpiryUtc)>>
        _byChar = new();

    /// <summary><c>CheckIsCoolDowning</c> natif @0x8411480 : true si le
    /// groupe a encore du temps restant.</summary>
    public bool IsCooling(int charId, short group)
        => _byChar.TryGetValue(charId, out var m)
            && m.TryGetValue(group, out var e)
            && e.ExpiryUtc > DateTime.UtcNow;

    /// <summary><c>AddCoolDownItem</c> natif @0x84283c0 : arme le
    /// groupe pour <paramref name="ds"/> dixièmes de seconde à partir
    /// de maintenant (le ctor natif de <c>CEventCoolDownItemGroup</c>
    /// @0x8663ac0 découpe la valeur en <c>v/10</c> secondes + reste en
    /// µs), en remplaçant une éventuelle entrée (le natif détruit
    /// l'event précédent avant de réinsérer — vfunc +0x40).</summary>
    public void Arm(int charId, short group, int itemId, int ds)
    {
        var m = _byChar.GetOrAdd(charId,
            _ => new ConcurrentDictionary<short,
                (int ItemId, DateTime ExpiryUtc)>());
        m[group] = (itemId, DateTime.UtcNow.AddMilliseconds(ds * 100));
    }

    /// <summary>Purge les entrées expirées (l'event natif s'auto-retire
    /// à l'échéance) et liste les cooldowns restants en dixièmes de
    /// seconde — unité du champ i32 du 368 (<c>µs_rest/100000</c> dans
    /// <c>::Serialize</c> @0x8237d30).</summary>
    public List<(short Group, int ItemId, int Ds)> GetCoolingEntries(
        int charId)
    {
        var entries = new List<(short, int, int)>();
        if (!_byChar.TryGetValue(charId, out var m)) return entries;
        var now = DateTime.UtcNow;
        foreach (var (g, e) in m)
        {
            var remain = e.ExpiryUtc - now;
            if (remain <= TimeSpan.Zero)
            {
                m.TryRemove(g, out _);
                continue;
            }
            entries.Add((g, e.ItemId,
                (int)(remain.TotalMilliseconds / 100)));
        }
        return entries;
    }

    /// <summary>Corps du 368 <c>ClientUpdateCoolDown</c> — format natif
    /// (<c>::Serialize</c> @0x8237d30) :
    /// <c>[u8 count]{u8 kind=0 item|s16 group|u16 itemId|i32 restant}
    /// </c> — le restant est en DIXIÈMES de seconde. Ne porte que les
    /// entrées kind=0 ; les entrées kind=1 des spell-downtimes
    /// (<c>CSpellMaster::GetDownTime</c>) sont fusionnées par
    /// <see cref="SpellCooldownService.SendUpdateAsync"/>.</summary>
    public CNC_CZ_ClientUpdateCoolDown BuildUpdateBody(int charId)
    {
        var msg = new CNC_CZ_ClientUpdateCoolDown();
        foreach (var (g, itemId, ds) in GetCoolingEntries(charId))
            msg.Entries.Add(new() { Kind = 0, Id = g,
                Aux = (ushort)itemId, RemainingDs = ds });
        return msg;
    }

    /// <summary>Émet le 368 avec le corps de
    /// <see cref="BuildUpdateBody"/>.</summary>
    public async Task SendUpdateAsync(NetSession session, int charId,
        CancellationToken token)
        => await session.SendAsync(BuildUpdateBody(charId), token);
}
