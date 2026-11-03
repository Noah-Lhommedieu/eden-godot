namespace Eden.Core.Networking;

/// <summary>
/// Codec varint LEB128 non signé — remplace le préfixe
/// <c>[u16 len]</c> historique du framing lapis (Phase 4 incrément b) :
/// la longueur d'une trame (payload NCID-inclus, inchangé) tient en
/// 1 à 5 octets au lieu de 2 fixes. 7 bits utiles par octet, bit7 =
/// continuation, groupes little-endian.
///
/// Miroir : eden_godot/godot_project/scripts/net/VarintCodec.cs
/// — même code ; toute correction doit y être reportée (cutover
/// atomique serveur/client/outils).
/// </summary>
public static class VarintCodec
{
    /// <summary>Nombre max d'octets d'un u32 encodé LEB128
    /// (32 bits / 7 bits par groupe = 5 groupes).</summary>
    public const int MaxEncodedLength = 5;

    /// <summary>Écrit <paramref name="value"/> en LEB128 dans
    /// <paramref name="destination"/> (qui doit avoir au moins
    /// <see cref="EncodedSize"/> octets de marge — 5 au pire) et
    /// retourne le nombre d'octets écrits.</summary>
    public static int WriteVarint(Span<byte> destination, uint value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            destination[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[i++] = (byte)value;
        return i;
    }

    /// <summary>Nombre d'octets que prendrait <paramref name="value"/>
    /// une fois encodé (1 à <see cref="MaxEncodedLength"/>).</summary>
    public static int EncodedSize(uint value)
    {
        var n = 1;
        while (value >= 0x80) { value >>= 7; n++; }
        return n;
    }

    /// <summary>
    /// Tente de décoder un varint LEB128 en tête de
    /// <paramref name="source"/>. Retourne <c>false</c> quand les
    /// octets disponibles n'en forment pas un complet (bit7 de
    /// continuation posé sur le dernier lu) — ce n'est PAS une
    /// erreur : l'appelant attend davantage d'input réseau.
    /// </summary>
    /// <param name="maxValue">Borne haute acceptée — cap anti-flood
    /// du framing (MaxFrameSize) ; une valeur décodée supérieure est
    /// rejetée comme malformée.</param>
    /// <exception cref="InvalidDataException">Varint overlong (&gt; 5
    /// octets de continuation), valeur &gt; u32.MaxValue (bits 4-6 du
    /// 5e octet non nuls) ou &gt; <paramref name="maxValue"/> — trame
    /// corrompue ou pair hostile ; l'appelant ferme la session.</exception>
    public static bool TryReadVarint(ReadOnlySpan<byte> source,
        uint maxValue, out uint value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        var result = 0u;
        var shift = 0;
        for (var i = 0; i < MaxEncodedLength; i++)
        {
            if (i >= source.Length)
                return false;               // varint incomplet — attendre
            var b = source[i];
            // 5e octet : seuls les 4 bits de poids faible codent des
            // bits ≤ 31 — bits 4..6 posés = valeur > u32.MaxValue.
            if (i == MaxEncodedLength - 1 && (b & 0x70) != 0)
                throw new InvalidDataException(
                    "Varint malformé : valeur > u32 " +
                    $"(octet 5 = 0x{b:X2})");
            result |= (uint)(b & 0x7F) << shift;
            bytesRead = i + 1;
            if ((b & 0x80) == 0)
            {
                if (result > maxValue)
                    throw new InvalidDataException(
                        $"Varint malformé : {result} > cap {maxValue}");
                value = result;
                return true;
            }
            shift += 7;
        }
        // Les 5 octets ont tous leur bit de continuation posé.
        throw new InvalidDataException("Varint overlong : > 5 octets");
    }
}
