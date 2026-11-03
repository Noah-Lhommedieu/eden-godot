using Eden.Core.Data;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Types de classement PVP — clés natives
    /// `CGlobalInfo::GetPVPRankCode` (@0x080720f0) remplies par
    /// `UpdatePVPRankInfo` : seuls ces types alimentent `PVPTopIDs`
    /// (le natif ne matérialise des chars virtuels que pour eux).</summary>
    private static bool IsPvpRankType(short type) =>
        type is >= 301 and <= 306 ||      // colosseum_type 1, b 1-6
        type is >= 320 and <= 322 ||      // colosseum_type 2, b 4-6
        type is 340 or 360 or 361 ||      // colo 3 / greedy island
        type is >= 400 and <= 420 ||      // classpk_rank, class b-6
        type is 500;                      // battle_royale_rank

    /// <summary>
    /// NCID 231 `CNE_CZ_ZoneServerPVPRankLookEquip` — Execute natif
    /// @0x0826edb0 : wire `[s16 type][str name]` ; cherche le rangé par
    /// nom dans les chars virtuels du type (map CZoneServer+0x61c) puis
    /// émet 571 `CNC_CZ_ClientPVPRankLookEquip` (profil + équipement
    /// complet). Type non-PVP, nom absent ou rangé hors top-3 → le
    /// natif n'émet RIEN (logs internes seulement) — on fait pareil.
    /// Divergence interne : le natif charge les top-3 via le push ZMis
    /// 182 (CExecutionerDBO → chars virtuels) ; notre cache RankSet
    /// porte déjà player_id en Cols[4] (colonne +0x14 des lignes PVP),
    /// on résout le perso depuis la DB au moment de la requête.
    /// </summary>
    private async Task HandlePvpRankLookEquipAsync(NetSession session,
        byte[] body, CancellationToken ct)
    {
        if (body.Length < 4) return;
        var type = BitConverter.ToInt16(body, 0);   // s16 — clé de rang
        var nlen = BitConverter.ToUInt16(body, 2);  // u16 — len nom
        if (4 + nlen > body.Length) return;
        var name = EdenEncoding.Big5
            .GetString(body, 4, nlen).TrimEnd('\0');  // str — nom rangé
        if (!IsPvpRankType(type) || name.Length == 0) return;
        var set = _services.RankInfos.Get(type);
        if (set == null) return;
        // Lookup par nom exact — le natif compare les chaînes octet à
        // octet (boucle d'égalité de l'Execute). Le vecteur natif des
        // chars virtuels = top min(3, count) du rang (PVPTopIDs) : un
        // rangé au-delà du 3ᵉ rang n'a pas d'inspect d'équipement.
        var idx = set.Names.FindIndex(n =>
            string.Equals(n, name, StringComparison.Ordinal));
        if (idx < 0 || idx >= 3 || idx >= set.Cols[4].Count) return;
        var pid = set.Cols[4][idx];    // +0x14 = player_id (lignes PVP)
        if (pid <= 0) return;
        var chr = await _services.Characters
            .GetCharacterByIdAsync(pid);
        if (chr == null) return;
        var classes = await _services.Classes
            .GetClassesByCharacterIdAsync(pid);
        // score +0x14 = Σ T(niveau de classe) — hashtable char+0xad8.
        var score = classes.Sum(c =>
            c.ClassLevel * (c.ClassLevel + 1) / 2);
        // class_lv +0x10 = niveau de la classe courante (char+0x47e).
        var classLv = classes
            .FirstOrDefault(c => c.ClassId == chr.ClassId)
            ?.ClassLevel ?? 0;
        var set0 = await _services.InventoryRepo
            .GetEquipmentEntitiesAsync(pid, 0);   // conteneur 0
        var set1 = await _services.InventoryRepo
            .GetEquipmentEntitiesAsync(pid, 1);   // conteneur 1
        var payload = PvpRankEquipPackets.Build571(
            pid, chr.GivenName,
            (short)(chr.ClassId & 0xff),
            (short)chr.Level, (short)classLv,
            heroFeature: 0,            // enchants -0x34df.. non stockés
            classLevelScore: score,
            pageFlag: 0,               // page courante live — absente DB
            appearance: chr.AppearanceData,
            set0, set1);
        await session.SendPacketAsync(571, payload, ct);
        EdenLog.Write(
            $"[Zone-In] 231 PVPRankLookEquip type={type} " +
            $"{name} → 571 ({payload.Length}B)");
    }
}
