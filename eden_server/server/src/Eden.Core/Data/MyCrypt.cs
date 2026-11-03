using Eden.Core.Data;
using System;
using System.IO;
using System.Text;

namespace Eden.Core.Data;

/// <summary>
/// Decodeur MyCrypt pour les fichiers .ini du serveur Eden Eternal.
///
/// Algorithme reverse-engineeré depuis ZoneServer (x86 ELF):
/// - MyCrypt::init_keys      @ 0x08594d70 — cles {0x12345678, 0x23456789, 0x34567890}
/// - MyCrypt::update_keys    @ 0x08594be0 — CRC32 key scheduling (poly 0xEDB88320)
/// - MyCrypt::decrypt_byte   @ 0x08594bc0 — t=(k2&amp;0xFFFF)|2; ret (t*(t^1))&gt;&gt;8
/// - MyCrypt::crypt_decode   @ 0x08595050 — boucle decrypt+update
/// - MyCrypt::crypthead      @ 0x085952e0 — header rand() cote encodage uniquement
/// - MyCrypt::coding         @ 0x08595b90 — dispatcher modes 0/1/2/3
///
/// Password: "ghfdsjdl" (.rodata @ 0x87cdff0, appele par
/// main @ 0x80dd1bc et GameData::ReadINIFile @ 0x85b9453).
///
/// Format fichier: "&lt;encode&gt;" (8 bytes) + ciphertext brut.
/// Le plaintext commence au byte 0 du ciphertext (pas d'IV/header stocke).
///
/// L'algorithme est le cipher ZipCrypto (PKZIP legacy): memes constantes,
/// meme update_keys, meme decrypt_byte.
/// </summary>
public static class MyCrypt
{
    /// <summary>Mot de passe hardcode dans le binaire serveur.</summary>
    public const string Password = "ghfdsjdl";

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    static MyCrypt()
    {
        // Big5/GBK ne sont pas natifs en .NET Core — provider CodePages requis
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decode un fichier .ini complet (avec ou sans header &lt;encode&gt;).
    /// </summary>
    public static byte[] DecodeFile(byte[] raw)
    {
        if (raw.Length >= 8 &&
            raw[0] == (byte)'<' && raw[1] == (byte)'e' && raw[2] == (byte)'n' &&
            raw[3] == (byte)'c' && raw[4] == (byte)'o' && raw[5] == (byte)'d' &&
            raw[6] == (byte)'e' && raw[7] == (byte)'>')
        {
            return CryptDecode(raw.AsSpan(8), Password);
        }
        return raw;
    }

    /// <summary>
    /// Decode un fichier depuis son chemin.
    /// </summary>
    public static byte[] DecodeFile(string path) => DecodeFile(File.ReadAllBytes(path));

    /// <summary>
    /// Decode un fichier et retourne le texte en encodage Big5/GBK
    /// (les donnees contiennent du chinois traditionnel).
    /// </summary>
    public static string DecodeFileToText(string path, Encoding? encoding = null)
    {
        encoding ??= EdenEncoding.Big5;
        return encoding.GetString(DecodeFile(path));
    }

    /// <summary>
    /// crypt_decode: init keys from password, then XOR-decode each byte
    /// updating keys with the decrypted plaintext.
    /// </summary>
    public static byte[] CryptDecode(ReadOnlySpan<byte> ciphertext, string password)
    {
        var keys = new uint[3] { 0x12345678, 0x23456789, 0x34567890 };
        foreach (var c in Encoding.Latin1.GetBytes(password))
            UpdateKeys(keys, c);

        var output = new byte[ciphertext.Length];
        for (var i = 0; i < ciphertext.Length; i++)
        {
            var plain = (byte)(ciphertext[i] ^ DecryptByte(keys[2]));
            UpdateKeys(keys, plain);
            output[i] = plain;
        }
        return output;
    }

    /// <summary>
    /// crypt_encode: inverse de crypt_decode (meme keystream, update avec ciphertext).
    /// </summary>
    public static byte[] CryptEncode(ReadOnlySpan<byte> plaintext, string password)
    {
        var keys = new uint[3] { 0x12345678, 0x23456789, 0x34567890 };
        foreach (var c in Encoding.Latin1.GetBytes(password))
            UpdateKeys(keys, c);

        var output = new byte[plaintext.Length];
        for (var i = 0; i < plaintext.Length; i++)
        {
            var cipher = (byte)(plaintext[i] ^ DecryptByte(keys[2]));
            UpdateKeys(keys, plaintext[i]);
            output[i] = cipher;
        }
        return output;
    }

    /// <summary>
    /// update_keys @ 0x08594be0
    /// k0 = CRC32(k0, b); k1 = (k1 + (k0 &amp; 0xFF)) * 0x8088405 + 1; k2 = CRC32(k2, k1&gt;&gt;24)
    /// </summary>
    private static void UpdateKeys(uint[] keys, byte b)
    {
        keys[0] = Crc32(keys[0], b);
        keys[1] = (keys[1] + (keys[0] & 0xFF)) * 0x08088405 + 1;
        keys[2] = Crc32(keys[2], (byte)(keys[1] >> 24));
    }

    /// <summary>
    /// decrypt_byte @ 0x08594bc0 — seuls les 16 bits bas de k2 sont utilises.
    /// t = (k2 &amp; 0xFFFF) | 2; return (t * (t ^ 1)) &gt;&gt; 8
    /// </summary>
    private static byte DecryptByte(uint key2)
    {
        uint temp = (key2 & 0xFFFF) | 2;
        return (byte)((temp * (temp ^ 1)) >> 8);
    }

    private static uint Crc32(uint crc, byte b) =>
        (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];

    /// <summary>
    /// CreateCrc32Table @ 0x08594cb0 — poly 0xEDB88320
    /// </summary>
    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
