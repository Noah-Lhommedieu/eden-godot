namespace EdenClient.Net;

/// <summary>
/// RC4 compatible avec l'implémentation OpenSSL utilisée par lapis
/// (fonction <c>RC4</c> @0x86a16a0 dans ZoneServer : S-box 256, i/j persistants).
/// Miroir de $EDEN/server/src/Eden.Core/Networking/Rc4.cs.
///
/// L'état du flux est continu par direction : client et serveur initialisent
/// chiffrement ET déchiffrement avec la même clé de session de 5 octets,
/// puis consomment le keystream en séquence.
/// </summary>
public sealed class Rc4
{
    private readonly byte[] _s = new byte[256];
    private int _i;
    private int _j;

    public Rc4(ReadOnlySpan<byte> key)
    {
        if (key.Length == 0) throw new ArgumentException("Clé RC4 vide", nameof(key));
        for (var i = 0; i < 256; i++) _s[i] = (byte)i;
        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + _s[i] + key[i % key.Length]) & 0xFF;
            (_s[i], _s[j]) = (_s[j], _s[i]);
        }
    }

    /// <summary>Chiffre/déchiffre en place (XOR keystream continu).</summary>
    public void Process(Span<byte> data)
    {
        for (var k = 0; k < data.Length; k++)
        {
            _i = (_i + 1) & 0xFF;
            _j = (_j + _s[_i]) & 0xFF;
            (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
            data[k] ^= _s[(_s[_i] + _s[_j]) & 0xFF];
        }
    }

    /// <summary>Retourne une copie chiffrée/déchiffrée.</summary>
    public byte[] ProcessCopy(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        Process(copy.AsSpan());
        return copy;
    }
}
