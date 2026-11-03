using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>Codec du 615 <c>CNC_CZ_ClientUpdateDailyAwards</c> —
/// calendrier de connexion. Le Serialize natif (@0x81e9650) écrit
/// <c>[s16 day_index][i32 online_s][i64 award_days][i64
/// award_records][s16 type][s16 flag=1]</c> — 26 octets ; la classe
/// générée aplatie (16B) est incorrecte, d'où ce codec dédié.
/// Types : 1 = état courant, 2 = récompense réclamée, 4 = système
/// désactivé.</summary>
public static class DailyAwardPackets
{
    /// <summary>Corps du 615 : l'<paramref name="onlineSeconds"/> est
    /// le cumul cooldown+delta session (char+0xdd8+0xdf0 natif).
    /// <paramref name="flag"/> = champ +0x1e natif : pour l'op 0 il
    /// vaut `local_26` (même jour ET login_status≠0 → 1), sinon la
    /// constante 1 posée au tail partagé de l'Execute.</summary>
    public static byte[] Build(short dayIndex, int onlineSeconds,
        ulong awardDays, ulong awardRecords, short type,
        short flag = 1)
    {
        var s = new OutBinStream();
        s.WriteShort(dayIndex);        // s16 : index de jour courant
        s.WriteInt(onlineSeconds);     // i32 : temps en ligne cumulé
        s.WriteLong((long)awardDays);  // i64 : bitmask jours complétés
        s.WriteLong((long)awardRecords); // i64 : bitmask jours réclamés
        s.WriteShort(type);            // s16 : 0/5 jour, 1 état,
                                       //     2 claim, 4 off
        s.WriteShort(flag);            // s16 : +0x1e natif
        return s.ToArray();
    }
}
