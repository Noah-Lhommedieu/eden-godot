namespace Eden.Core.Networking;

/// <summary>
/// RC4 compatible avec l'implémentation OpenSSL utilisée par lapis
/// (fonction <c>RC4</c> @0x86a16a0 dans ZoneServer : S-box 256, i/j persistants).
///
/// (retiré en phase4-c2 — ne sert plus qu'en mode
/// <c>EDEN_TRANSPORT=legacy</c> via <c>LegacyLapisTransport</c>.)
///
/// L'état du flux est continu par direction : les deux côtés initialisent
/// chiffrement ET déchiffrement avec la même clé de session de 5 octets
/// (RC4_set_key @0x868cb93/cbb3), puis consomment le keystream en séquence.
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

    /// <summary>Chiffre/déchiffre en place (XOR keystream).</summary>
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
    /// <remarks>Nom distinct de <see cref="Process(Span{byte})"/> :
    /// sous C# 14 (.NET 10), un appel <c>Process(byteArray)</c> se
    /// résout vers l'overload <c>ReadOnlySpan</c> plutôt que
    /// <c>Span</c> — la variante en copie rendait alors la mutation
    /// silencieusement inactive.</remarks>
    public byte[] ProcessCopy(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        Process(copy.AsSpan());
        return copy;
    }
}
