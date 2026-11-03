using Eden.Core.Game.Classes;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Certificats de classe — NCID 181 QueryLicense, 182 PlayerLicense,
/// 183 QueryLicenseSet. Fidélité native :
/// <code>
///   181 → 509 [u16 n]{s16 itemId,u8 loc} (vecteur +0xaf0 complet)
///   182 [u16 n]{s16 lic,u8 slot}n [s16 classId] →
///       classe courante : SetCurrentClassLicense @0x82714f0 → 509
///       autre classe    : SetOtherClassLicense @0x82717f0 → 489
///   183 [u16 n]{u16 lic}n-1 [u16 flag] → InvokeSpellStonesSet
///       @0x8418aa0 (flag 0 = slots 1-4, 1 = slots 4-7)
/// </code>
/// Capacité `charLevel/10 + 1`, refus texte 0x325 natif.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>NCID 181 QueryLicense — le natif renvoie le 509 avec
    /// la copie intégrale du vecteur +0xaf0 (toutes les licences
    /// possédées avec leur loc). Recharge depuis `player_license`
    /// pour couvrir les octrois en session (level-up, quêtes).</summary>
    private async Task HandleQueryLicenseAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        // Le prefetch d'entrée a déjà appelé LoadAsync (cache du
        // service licences) — le rechargement ne sert qu'à couvrir
        // les octrois en session (level-up, quêtes), impossibles
        // avant le premier MigrationDone : on le saute tant que le
        // bundle n'est pas « consommé » (Emitted), comme pour les
        // autres données d'entrée.
        if (st.Entry is not { Emitted: false })
            await _services.Licenses.LoadAsync((int)st.CharId, token);
        var st2 = _services.Licenses.StateOf((int)st.CharId);
        var msg = new CNC_CZ_ClientPlayerLicense();
        foreach (var (itemId, loc) in st2.Owned)
            msg.Licenses.Add((itemId, loc));
        await session.SendAsync(msg, token);
    }

    /// <summary>NCID 182 PlayerLicense — pose/déplace/déséquipe des
    /// certificats. `classId == st.ClassId` → chemin classe courante
    /// (persiste `player_license.loc` + écho 509 des licences
    /// modifiées) ; sinon → chemin autre classe (réécrit
    /// `lic_1..lic_7` + 489 complet).</summary>
    private async Task HandlePlayerLicenseAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 4) return;
        var req = new CNE_CZ_ZoneServerPlayerLicense();
        req.Deserialize(new InBinStream(payload));

        // Le natif refuse si la classe cible n'est pas possédée
        // (recherche dans la table de hachage +0xad8).
        var rows = await _services.Classes
            .GetClassesByCharacterIdAsync((int)st.CharId);
        if (rows.All(r => r.ClassId != req.ClassId)) return;

        if (req.ClassId == st.ClassId)
        {
            var (err, changed) = await _services.Licenses
                .SetCurrentClassLicenseAsync((int)st.CharId, st.Level,
                    req.Selections);
            var echo = new CNC_CZ_ClientPlayerLicense();
            foreach (var (itemId, loc) in changed)
                echo.Licenses.Add((itemId, loc));
            if (echo.Licenses.Count > 0)
                await session.SendAsync(echo, token);
            if (err != 0)
            {
                // Refus natif : TextOutputIndex(0x325, -105, fmt,
                // lv/10+2) — vtable+0x58 des deux sites. La sélection
                // fautive est sautée, les autres restent appliquées.
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                {
                    Field1 = (short)LicenseService.ErrorOverCapacity,
                    Field2 = LicenseService.DisplayedCapacityFor(st.Level).ToString(),
                }, token);
            }
            // Les enchants des slots ont changé → stats à jour
            // (RefreshEQsAttr/InvokeSpellStones natif).
            st.WeaponResolved = false;
            st.EntryDirty = true; // bundle d'entrée périmé (Phase 2)
            await ResolveWeaponAsync(st);
            await SendStatBlocksAsync(session, st, st.ClassId,
                st.ClassLevel, token);
            return;
        }

        // Classe non active : le service filtre les licences non
        // possédées (Execute natif ne pousse que les ids de +0xaf0).
        var layout = new int[7];
        var err2 = _services.Licenses.ValidateOtherClassLayout(
            (int)st.CharId, st.Level, req.Selections, layout);
        // Les sélections retenues sont persistées même si une a été
        // refusée par capacité (comportement natif : l'erreur ne
        // bloque que la sélection fautive).
        await _services.Classes.SetClassLicensesAsync((int)st.CharId,
            req.ClassId, layout);
        await HandleQueryClassListAsync(session, st, token);
        if (err2 != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = (short)LicenseService.ErrorOverCapacity,
                Field2 = LicenseService.DisplayedCapacityFor(st.Level).ToString(),
            }, token);
        }
    }

    /// <summary>NCID 183 QueryLicenseSet — active un combo de
    /// certificats. `InvokeSpellStonesSet` natif applique l'enchant
    /// résolu via EquipSetPool (QueryEquipSetEnchant) puis l'inscrit
    /// en +0xb14[flag]. Aucun paquet de retour natif — le client ne
    /// reçoit que le changement de stats (296/492/493).</summary>
    private async Task HandleQueryLicenseSetAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 4) return;
        var req = new CNE_CZ_ZoneServerQueryLicenseSet();
        req.Deserialize(new InBinStream(payload));
        var applied = _services.Licenses.InvokeSpellStonesSet(
            (int)st.CharId, req.LicenseIds);
        if (applied != 0)
        {
            st.WeaponResolved = false;
            st.EntryDirty = true; // bundle d'entrée périmé (Phase 2)
            await ResolveWeaponAsync(st);
            await SendStatBlocksAsync(session, st, st.ClassId,
                st.ClassLevel, token);
        }
    }

    /// <summary>Bascule de layout licences au changement de classe —
    /// `CPlayerFactory::ChangeClass` @0x8105xxx : chaque licence
    /// possédée voit sa loc recalculée en recherchant son id dans
    /// lic_1..7 de la NOUVELLE classe (+0xe..+0x1a de la ligne
    /// classlist), puis InvokeSpellStones ré-applique les enchants.
    /// Persiste `license = id loc` pour chaque ligne modifiée et
    /// sauvegarde l'ancien layout dans lic_1..7 de l'ancienne classe
    /// — sinon l'aller-retour perdait les slots.</summary>
    private async Task SyncLicensesOnClassChangeAsync(EntryState st,
        int oldClassId, int newClassId)
    {
        if (st.CharId == 0) return;
        var charId = (int)st.CharId;
        var lic = _services.Licenses;
        var state = lic.StateOf(charId);

        // Sauvegarde l'ancien layout (lic_1..7 = loc courantes) —
        // le natif ne le fait que via SetOtherClassLicense, mais la
        // classe active vit dans player_license.loc : on la persiste
        // dans la ligne classlist de l'ancienne classe pour la
        // restaurer au retour.
        var oldLayout = new int[7];
        for (var s = 1; s <= 7; s++)
            oldLayout[s - 1] = state.SlotLicense[s];
        await _services.Classes.SetClassLicensesAsync(charId,
            oldClassId, oldLayout);

        // Charge le layout de la nouvelle classe : pour chaque
        // licence possédée, loc = position dans lic_1..7 (0 sinon).
        var target = await _services.Classes.GetClassProgressAsync(
            charId, newClassId);
        var byLic = new Dictionary<int, int>();
        if (target != null)
            for (var i = 0; i < 7; i++)
                if (target.Licenses[i] != 0)
                    // Les ids > 32767 arrivent positifs (integer) ou
                    // négatifs (s16) selon le writer — normalisation
                    // s16 pour matcher Owned.ItemId (short).
                    byLic[unchecked((short)target.Licenses[i])] = i + 1;

        for (var i = 0; i < state.Owned.Count; i++)
        {
            var (itemId, loc) = state.Owned[i];
            var nl = (byte)(byLic.TryGetValue(itemId, out var sl)
                ? sl : 0);
            if (nl == loc) continue;
            state.Owned[i] = (itemId, nl);
            await _services.Licenses.SetLocAsync(charId,
                itemId, (short)nl);
        }
        // Rebuild slots/enchants (InvokeSpellStones natif : réévalue
        // +0xafc depuis le vecteur +0xaf0 après la bascule).
        await lic.LoadAsync(charId);
    }
}
