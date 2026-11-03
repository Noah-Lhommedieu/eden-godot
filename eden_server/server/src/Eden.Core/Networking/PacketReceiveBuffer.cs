namespace Eden.Core.Networking;

/// <summary>
/// Buffer de réception TCP → trames [varint len][payload] du
/// protocole (Phase 4 incrément b — remplace le préfixe u16
/// historique : 1 à 5 octets LEB128 au lieu de 2 fixes ; le payload
/// [u16 NCID][champs] est inchangé, `len` = sa taille).
///
/// Remplace le `List&lt;byte&gt;` précédent : GetRange+RemoveRange copiait
/// deux fois chaque paquet et déplaçait tout le buffer à chaque trame —
/// O(n²) pendant le burst d'entrée. Ici : une copie par trame extraite
/// (nécessaire — le RC4 déchiffre en place et le handler garde l'array),
/// compaction amortie quand l'espace de tête est épuisé.
/// </summary>
public sealed class PacketReceiveBuffer
{
    private byte[] _buf = new byte[8192];
    private int _start;
    private int _length;

    /// <summary>Octets actuellement en attente de trame complète.</summary>
    public int Pending => _length;

    /// <summary>Taille max d'un payload (NCID inclus) qu'un préfixe
    /// varint peut annoncer — au-delà la trame est malformée
    /// (anti-flood). <c>EDEN_MAX_FRAME_SIZE</c>, défaut 1 Mio.</summary>
    public int MaxFrameSize { get; init; } = EdenConfig.MaxFrameSize;

    /// <summary>Ajoute les octets lus du socket (agrandit si besoin).</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        EnsureSpace(data.Length);
        data.CopyTo(_buf.AsSpan(_start + _length));
        _length += data.Length;
    }

    /// <summary>Extrait la prochaine trame complète (payload de `len`
    /// octets — NCID inclus) ou false si incomplète. Un préfixe varint
    /// incomplet (bit7 de continuation sur le dernier octet reçu) est
    /// traité comme une trame incomplète : on attend plus d'input.</summary>
    /// <exception cref="InvalidDataException">Varint overlong/&gt;cap,
    /// ou `len &lt; 2` : trame corrompue ou pair hostile — l'appelant
    /// doit fermer la session (anti-flood).</exception>
    public bool TryTakePacket(out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (_length == 0) return false;
        // La fenêtre de décodage est bornée aux 5 premiers octets en
        // attente — un varint u32 ne peut pas en occuper davantage.
        var window = _buf.AsSpan(_start,
            Math.Min(_length, VarintCodec.MaxEncodedLength));
        if (!VarintCodec.TryReadVarint(window, (uint)MaxFrameSize,
                out var len, out var hdrLen))
            return false;   // préfixe varint incomplet → attendre
        if (len < 2)
            throw new InvalidDataException(
                $"Trame invalide : len={len} (< 2, NCID impossible)");
        if (_length < hdrLen + (int)len) return false;
        payload = new byte[len];
        Array.Copy(_buf, _start + hdrLen, payload, 0, (int)len);
        _start += hdrLen + (int)len;
        _length -= hdrLen + (int)len;
        if (_length == 0) _start = 0;
        return true;
    }

    /// <summary>Garantit <paramref name="count"/> octets libres en fin
    /// de fenêtre : compacte (memmove amorti) puis agrandit si requis.</summary>
    private void EnsureSpace(int count)
    {
        if (_buf.Length - (_start + _length) < count)
        {
            if (_buf.Length - _length >= count)
            {
                // Espace total suffisant — compacter la fenêtre au début.
                Array.Copy(_buf, _start, _buf, 0, _length);
            }
            else
            {
                var bigger = new byte[Math.Max(_buf.Length * 2,
                    _length + count)];
                Array.Copy(_buf, _start, bigger, 0, _length);
                _buf = bigger;
            }
            _start = 0;
        }
    }
}
