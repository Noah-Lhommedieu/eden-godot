using Eden.Core.Game.Classes;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>`AddClassFromLimit` natif (KB-03) : débloque les classes
    /// dont le niveau perso et les classes prérequises sont atteints
    /// (<see cref="ClassUnlockRules"/>). Appelé aux level-ups (classe et
    /// perso) — là seulement que les conditions changent.</summary>
    private async Task UnlockClassesFromLimitAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var rows = await _services.Classes
            .GetClassesByCharacterIdAsync((int)st.CharId);
        var todo = ClassUnlockRules.Unlockable(_services.GameData.Classes,
            st.RaceId, st.Level,
            rows.Select(r => (r.ClassId, r.ClassLevel)).ToList());
        foreach (var cls in todo)
            await GrantNewClassAsync(session, st, cls, token);
    }

    /// <summary>Octroi complet d'une classe (KB-04 / D2 + D6 + Bug D) —
    /// `CCharacter::AddClass` @0x080fd070 : insertion (éveillée = lv80 /
    /// aa80), certificats des paliers <c>level/10</c>, sorts du vecteur
    /// +0x50 à spell_level 1 et du vecteur +0x5c à spell_level 80 (classe
    /// éveillée), `RefreshJPAward` (296 recalculé) puis 489. Chemin
    /// partagé par `eden-add-class` (mission) et le GM `add_class`.
    /// Retourne false si la classe est déjà possédée.</summary>
    private async Task<bool> GrantNewClassAsync(NetSession session,
        EntryState st, int cls, CancellationToken token)
    {
        var rows = await _services.Classes
            .GetClassesByCharacterIdAsync((int)st.CharId);
        if (cls <= 0 || rows.Any(r => r.ClassId == cls)) return false;
        await _services.Classes.AddClassAsync((int)st.CharId, cls);
        var cdef = ClassSkillTable.ClassFor(
            _services.GameData.Classes, st.RaceId, cls);
        var awakened = ClassRules.IsAwakened(cls);
        var level = awakened ? ClassRules.AwakenedStartLevel : 1;

        // Certificats : `level/10` premiers de la classe (GetLicense).
        if (cdef != null)
            for (var i = 0; i < Math.Min(level / 10, cdef.CertificateIds.Length); i++)
            {
                if (cdef.CertificateIds[i] <= 0) continue;
                var licId = unchecked((short)cdef.CertificateIds[i]);
                await _services.Licenses.GrantLicenseAsync(
                    (int)st.CharId, licId);
                await session.SendAsync(new CNC_CZ_ClientPlayerLicense
                {
                    Licenses = { (licId, 0) },
                }, token);
            }

        // Sorts : +0x50 niveau 1, +0x5c à 80 (éveillée) — D6.
        var known = (await _services.Classes
            .GetPlayerSpellsAsync((int)st.CharId))
            .Select(x => unchecked((ushort)x.Spell)).ToHashSet();
        var high = cdef?.ClassSpellIdsHigh.ToHashSet() ?? new HashSet<int>();
        foreach (var sm in ClassSkillTable.GrantableSpells(cls, level,
                     _services.GameData.Spells, cdef, st.RaceId))
        {
            if (!known.Add(unchecked((ushort)sm))) continue;
            await _services.Classes.LearnSpellAsync(
                (int)st.CharId, unchecked((short)sm));
            if (awakened && high.Contains(sm))
                await _services.Classes.SetSpellLevelAsync(
                    (int)st.CharId, unchecked((short)sm),
                    (short)ClassRules.AwakenedStartLevel);
        }
        _services.SpellLevelsDirty?.Invoke((int)st.CharId);

        await HandleQueryClassListAsync(session, st, token);
        // RefreshJPAward : le palier de la nouvelle classe change le cumul
        // (Bug D) — le 296 recalcule toutes les classes.
        await ProfileStatEmitter.SendForCharacterAsync(session,
            _services, (int)st.CharId, token);
        EdenLog.Write($"[Zone] AddClass char={st.CharId} class={cls} " +
            $"lv={level}{(awakened ? " (éveillée)" : "")}");
        return true;
    }
}
