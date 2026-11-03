using Eden.Core.Data;
using System.Text;

namespace Eden.Core.Networking;

/// <summary>
/// Buffer de lecture binaire reproduisant <c>lapis::CInBinStream</c>.
/// Symétrique de <see cref="OutBinStream"/> : même endianness, même format.
/// </summary>
public sealed class InBinStream
{
    private readonly byte[] _buffer;
    private int _position;
    // Le protocole natif transporte des octets bruts (operator>>(std::string&)
    // lit u16 len + bytes). Le client Eden est Big5 (CP950) — ASCII-compatible.
    private static readonly Encoding DefaultEncoding;

    static InBinStream()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DefaultEncoding = EdenEncoding.Big5;
    }

    public InBinStream(byte[] buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _position = 0;
    }

    public int Remaining => _buffer.Length - _position;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _buffer[_position++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public short ReadShort()
    {
        EnsureAvailable(2);
        var value = (short)(_buffer[_position]
                            | (_buffer[_position + 1] << 8));
        _position += 2;
        return value;
    }

    public ushort ReadUShort()
    {
        EnsureAvailable(2);
        var value = (ushort)(_buffer[_position]
                             | (_buffer[_position + 1] << 8));
        _position += 2;
        return value;
    }

    public int ReadInt()
    {
        EnsureAvailable(4);
        var value = _buffer[_position]
                    | (_buffer[_position + 1] << 8)
                    | (_buffer[_position + 2] << 16)
                    | (_buffer[_position + 3] << 24);
        _position += 4;
        return value;
    }

    public long ReadLong()
    {
        EnsureAvailable(8);
        var value = BitConverter.ToInt64(_buffer, _position);
        _position += 8;
        return value;
    }

    public float ReadFloat()
    {
        EnsureAvailable(4);
        var value = BitConverter.ToSingle(_buffer, _position);
        _position += 4;
        return value;
    }

    public double ReadDouble()
    {
        EnsureAvailable(8);
        var value = BitConverter.ToDouble(_buffer, _position);
        _position += 8;
        return value;
    }

    public string ReadString()
    {
        // operator>>(std::string&) lit un u16 non signé (movzwl)
        var length = ReadUShort();
        EnsureAvailable(length);
        var value = DefaultEncoding.GetString(_buffer, _position, length);
        _position += length;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        EnsureAvailable(count);
        var bytes = new byte[count];
        Array.Copy(_buffer, _position, bytes, 0, count);
        _position += count;
        return bytes;
    }

    private void EnsureAvailable(int count)
    {
        if (_position + count > _buffer.Length)
            throw new EndOfStreamException("Trame tronquée : pas assez d'octets pour lire le champ demandé.");
    }
}
