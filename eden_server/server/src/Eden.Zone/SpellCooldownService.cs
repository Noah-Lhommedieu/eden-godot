using System.Collections.Concurrent;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Cooldowns de sorts par personnage — miroir du mécanisme natif
/// <c>CSpellMaster</c> : chaque master porte son dernier sort et son
/// expiration (<c>master+0x14/+0x18/+0x1c</c>). <c>CSpellMaster::
/// PutInDownTime</c> @0x84f3820 arme l'expiration via
/// <c>CCharFormula::CoolDownTime(entity, SSpellData+0x3c)</c> (ds) ;
/// <c>CheckInDownTime</c> @0x84f3720 retourne 1 tant que l'expiration
/// n'est pas atteinte puis efface le marqueur (+0x14 = -1).
/// Le downtimes est regroupé par spell-master côté natif ; notre modèle
/// joueur ne connaît qu'un niveau par master → la clé spellId est
/// équivalente.
/// État volatil : le natif ne persiste que les downtimes &gt;3000 ds
/// (<c>SavePlayerCoolDown</c> dans <c>CCharacter::PutInDownTime</c>
/// @0x842f380) — non porté, les longs CDs sont rares.
/// Émet les entrées kind=1 du NCID 368
/// <c>CNC_CZ_ClientUpdateCoolDown</c> — voir
/// <see cref="ItemCooldownService"/> pour le kind=0.
/// </summary>
public sealed class SpellCooldownService
{
    /// <summary>charId → (spellId → expiration UTC). Même sémantique
    /// que <c>master+0x14</c> (dernier sort) + <c>+0x18/+0x1c</c>
    /// (expiration) — clé par sort déclencheur.</summary>
    private readonly ConcurrentDictionary<int,
        ConcurrentDictionary<short, DateTime>> _byChar = new();

    /// <summary><c>CSpellMaster::CheckInDownTime</c> natif @0x84f3720 :
    /// true tant que l'expiration du sort n'est pas atteinte.</summary>
    public bool IsCooling(int charId, int spellId)
        => _byChar.TryGetValue(charId, out var m)
            && m.TryGetValue((short)spellId, out var expiry)
            && expiry > DateTime.UtcNow;

    /// <summary><c>PutInDownTime</c> natif @0x84f3820 : arme le
    /// downtime pour <paramref name="ds"/> dixièmes de seconde (le
    /// natif convertit en sec+µs ; plancher natif 4-5 ds appliqué par
    /// <c>CCharFormula::CoolDownTime</c> — la valeur ici est déjà le
    /// résultat formule).</summary>
    public void Arm(int charId, int spellId, int ds)
    {
        if (ds <= 0) return;
        var m = _byChar.GetOrAdd(charId,
            _ => new ConcurrentDictionary<short, DateTime>());
        m[(short)spellId] = DateTime.UtcNow.AddMilliseconds(ds * 100);
    }

    /// <summary>Purge les entrées expirées (le natif efface +0x14=-1 à
    /// la première lecture après échéance) et liste les downtime
    /// restants en dixièmes de seconde — mêmes unités que
    /// <c>CSpellMaster::GetDownTime</c> @0x84f3920
    /// (<c>µs_rest/100000 + sec*10</c>).</summary>
    public List<(short SpellId, int Ds)> GetCoolingEntries(int charId)
    {
        var entries = new List<(short, int)>();
        if (!_byChar.TryGetValue(charId, out var m)) return entries;
        var now = DateTime.UtcNow;
        foreach (var (spellId, expiry) in m)
        {
            var remain = expiry - now;
            if (remain <= TimeSpan.Zero)
            {
                m.TryRemove(spellId, out _);
                continue;
            }
            entries.Add((spellId, (int)(remain.TotalMilliseconds / 100)));
        }
        return entries;
    }

    /// <summary>Émet le 368 fusionné (kind=0 items + kind=1 sorts) —
    /// le natif sérialise les DEUX maps dans un seul paquet
    /// (<c>::Serialize</c> @0x8237d30 : u8 count = items + sorts,
    /// entrées item d'abord puis <c>[u8 1][s16 spellId][u16 0][i32 ds]</c>).</summary>
    public async Task SendUpdateAsync(NetSession session, int charId,
        ItemCooldownService itemCooldowns, CancellationToken token)
    {
        var items = itemCooldowns.GetCoolingEntries(charId);
        var spells = GetCoolingEntries(charId);
        var msg = new CNC_CZ_ClientUpdateCoolDown();
        foreach (var (g, itemId, ds) in items)      // kind 0 = item
            msg.Entries.Add(new() { Kind = 0, Id = g,
                Aux = (ushort)itemId, RemainingDs = ds });
        foreach (var (spellId, ds) in spells)       // kind 1 = sort
            msg.Entries.Add(new() { Kind = 1, Id = spellId,
                Aux = 0, RemainingDs = ds });
        await session.SendAsync(msg, token);
    }
}
