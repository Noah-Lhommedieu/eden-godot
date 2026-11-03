using EdenClient.Net;

namespace EdenClient.Protocol;

/// <summary>
/// Wire custom Reborn du trait héroïque (A4.5), noyau pur testable
/// sans Godot. S→C 9058 <c>[u8 trait]</c> (0 = aucun, 1..8 = enchant
/// 52000+n) ; C→S : l'octet « Feature » du NCID 5 est 0-based
/// (natif : 0..7, ≥ 8 refusé par CreateCharacterIntoDB).
/// </summary>
public static class HeroTraitWire
{
    public const int Ncid = 9058;
    public const int Count = 8;

    /// <summary>Corps du 9058 → trait 0..8 (hors plage = 0).</summary>
    public static int Read(BinReader r)
    {
        var t = r.ReadByte();
        return t is >= 1 and <= Count ? t : 0;
    }

    /// <summary>Feature wire du NCID 5 pour un index de choix UI.</summary>
    public static byte FeatureFor(int index)
        => (byte)Math.Clamp(index, 0, Count - 1);
}
