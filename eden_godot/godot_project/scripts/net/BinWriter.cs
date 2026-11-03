using System.Buffers;
using System.Text;

namespace EdenClient.Net;

/// <summary>
/// Buffer d'écriture binaire reproduisant <c>lapis::COutBinStream</c>.
/// Miroir de $EDEN/server/src/Eden.Core/Networking/OutBinStream.cs :
/// little-endian, string = u16 len + octets Big5 (CP950, sans NUL).
/// </summary>
public sealed class BinWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private static readonly Encoding DefaultEncoding;

    static BinWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DefaultEncoding = Encoding.GetEncoding(950);
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
        var span = _buffer.GetSpan(2);
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)((value >> 8) & 0xFF);
        _buffer.Advance(2);
    }

    public void WriteUShort(ushort value) => WriteShort(unchecked((short)value));

    public void WriteInt(int value)
    {
        var span = _buffer.GetSpan(4);
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)((value >> 8) & 0xFF);
        span[2] = (byte)((value >> 16) & 0xFF);
        span[3] = (byte)((value >> 24) & 0xFF);
        _buffer.Advance(4);
    }

    public void WriteUInt(uint value) => WriteInt(unchecked((int)value));

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

    /// <summary>Chaîne préfixée par un u16 = longueur en octets Big5.</summary>
    public void WriteString(string value)
    {
        var bytes = DefaultEncoding.GetBytes(value ?? "");
        WriteShort((short)bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_buffer.GetSpan(bytes.Length));
        _buffer.Advance(bytes.Length);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;
}
