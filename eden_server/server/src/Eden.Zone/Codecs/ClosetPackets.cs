using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>
/// Codec de la garde-robe costumes — NCID 488
/// <c>CNC_CZ_ClientPlayerClosets</c>. La classe générée de
/// <c>ZoneMessages.g.cs</c> a aplati la structure (entrées fixes
/// 27B) et ne représente pas le format réel : on sérialise depuis
/// le désasm.
///
/// Source de preuve : zone/decompiled.c —
/// <c>CNC_CZ_ClientPlayerClosets::Serialize</c> : <c>[u8 type]
/// [u16 n][n × {u8 class][s16 count][count × SCharacterCloset}]</c>
/// où <c>SCharacterCloset::Serialize</c> = 3×u64 (helmet_color,
/// clothes_color, cloak_color — colonnes de <c>player_closet</c>).
/// Capture live d'un perso sans costume : <c>00 0100 05 01</c> +
/// 24×00 (30B — une entrée classe 5 avec un slot zéro).
/// </summary>
public static class ClosetPackets
{
    /// <summary>Taille d'un slot de costume sur le fil
    /// (3 couleurs u64).</summary>
    public const int EntrySize = 24;

    /// <summary>Corps du 488 — miroir de la hashtable char+0xab0
    /// native (classe → vector de slots positionnels).</summary>
    public static byte[] Build(byte type,
        SortedDictionary<byte, List<(ulong Helmet, ulong Clothes,
            ulong Cloak)>> closets)
    {
        var w = new OutBinStream();
        w.WriteByte(type);                            // u8 : type (0)
        w.WriteShort((short)closets.Count);           // u16 : nb classes
        foreach (var (classId, slots) in closets)
        {
            w.WriteByte(classId);                     // u8 : classe
            w.WriteShort((short)slots.Count);         // s16 : nb slots
            foreach (var (helmet, clothes, cloak) in slots)
            {
                w.WriteLong((long)helmet);            // u64 : helmet_color
                w.WriteLong((long)clothes);           // u64 : clothes_color
                w.WriteLong((long)cloak);             // u64 : cloak_color
            }
        }
        return w.ToArray();
    }
}
