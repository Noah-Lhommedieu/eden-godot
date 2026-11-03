using Eden.Core.Data;
using System.Buffers;
using System.Text;

namespace Eden.Core.Networking;

/// <summary>
/// Buffer d'écriture binaire reproduisant le format <c>lapis::COutBinStream</c>
/// du serveur d'origine. Objectif : produire EXACTEMENT les mêmes octets que
/// le serveur C++ pour rester compatible avec le client d'origine.
///
/// CONFIRMÉ PAR DÉSASSEMBLAGE (objdump ZoneServer) :
///  - Endianness little-endian (cible x86).
///  - operator>>(std::string&) @0x867af90 : lit u16 (movzwl) puis N octets
///    bruts — pas de NUL terminal.
///  - Le client Eden est Big5 (CP950) : ASCII-compatible pour les noms
///    anglais, couvre les chaînes chinoises des données.
///
/// Backing : ArrayBufferWriter&lt;byte&gt; — GetSpan/Advance sans
/// allocation intermédiaire (l'ancien List&lt;byte&gt; forçait un
/// span.ToArray()+AddRange par WriteBytes).
/// </summary>
public sealed class OutBinStream
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private static readonly Encoding DefaultEncoding;

    static OutBinStream()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DefaultEncoding = EdenEncoding.Big5;
    }

    public int Length => _buffer.WrittenCount;

    public void WriteByte(byte value)
    {
        _buffer.GetSpan(1)[0] = value;
        _buffer.Advance(1);
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteShort(short value)
    {
        // little-endian
        var span = _buffer.GetSpan(2);
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)((value >> 8) & 0xFF);
        _buffer.Advance(2);
    }

    public void WriteInt(int value)
    {
        var span = _buffer.GetSpan(4);
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)((value >> 8) & 0xFF);
        span[2] = (byte)((value >> 16) & 0xFF);
        span[3] = (byte)((value >> 24) & 0xFF);
        _buffer.Advance(4);
    }

    public void WriteLong(long value)
    {
        BitConverter.TryWriteBytes(_buffer.GetSpan(8), value);
        _buffer.Advance(8);
    }

    public void WriteFloat(float value)
    {
        BitConverter.TryWriteBytes(_buffer.GetSpan(4), value);
        _buffer.Advance(4);
    }

    public void WriteDouble(double value)
    {
        BitConverter.TryWriteBytes(_buffer.GetSpan(8), value);
        _buffer.Advance(8);
    }

    /// <summary>Chaîne préfixée par un entier 16 bits = longueur en octets.</summary>
    public void WriteString(string value)
    {
        var bytes = DefaultEncoding.GetBytes(value);
        WriteShort((short)bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_buffer.GetSpan(bytes.Length));
        _buffer.Advance(bytes.Length);
    }

    public void WriteBytes(byte[] bytes, int count)
    {
        if (bytes == null || count <= 0) return;
        WriteBytes(bytes.AsSpan(0, Math.Min(bytes.Length, count)));
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    /// <summary>Vue zéro-copie des octets écrits (comparaisons wire
    /// sans allocation — tests golden, traces).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;
}
