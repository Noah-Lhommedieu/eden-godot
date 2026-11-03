namespace EdenClient.Pkg;

/// <summary>
/// CRC du manifeste officiel <c>GameDataFileList.txt</c> et du champ crc de
/// <c>pkg.idx</c> : CRC32C (Castagnoli, init/xorout 0xFFFFFFFF). Voir docs/formats/MANIFEST-CRC.md.
/// </summary>
public static class ManifestCrc
{
    public static uint Compute(ReadOnlySpan<byte> data) => Castagnoli(data);

    static readonly uint[] Table = BuildTable();

    static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0x82F63B78u : c >> 1;
            t[i] = c;
        }
        return t;
    }

    static uint Castagnoli(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
