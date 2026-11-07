using System.Security.Cryptography;
using System.Text;

namespace Eden.Core.Game.Admin;

/// <summary>Chaînage de l'audit : <c>hash = SHA-256(prevHash | contenu)</c>. Modifier une ligne passée casse toute la suite.</summary>
public static class AuditChain
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string Hash(string prevHash, string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prevHash + "|" + content))).ToLowerInvariant();

    public static string Content(DateTimeOffset at, int accountId, string accountName, int charId, string command, string argsJson,
        bool dryRun, bool ok, string message, string requestId) =>
        string.Join("\u001f", at.ToUnixTimeMilliseconds(), accountId, accountName, charId, command, argsJson, dryRun ? 1 : 0, ok ? 1 : 0, message, requestId);

    /// <summary>Vérifie une suite d'entrées triées par id : renvoie l'id de la première ligne altérée, ou null si la chaîne est intacte.</summary>
    public static long? FirstBroken(IEnumerable<AuditEntry> orderedEntries)
    {
        var prev = Genesis;
        foreach (var e in orderedEntries)
        {
            var expected = Hash(prev, Content(e.At, e.AccountId, e.AccountName, e.CharId, e.Command, e.ArgsJson, e.DryRun, e.Ok, e.Message, e.RequestId));
            if (e.PrevHash != prev || e.Hash != expected) return e.AuditId;
            prev = e.Hash;
        }
        return null;
    }
}

/// <summary>TOTP (RFC 6238, SHA-1, 6 chiffres, pas de 30 s) pour la double authentification des administrateurs.</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    public static string Code(string base32Secret, DateTimeOffset at)
    {
        var key = Base32Decode(base32Secret);
        var counter = at.ToUnixTimeSeconds() / 30;
        var msg = BitConverter.GetBytes(counter); if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var h = HMACSHA1.HashData(key, msg);
        var off = h[^1] & 0x0f;
        var bin = ((h[off] & 0x7f) << 24) | (h[off + 1] << 16) | (h[off + 2] << 8) | h[off + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    /// <summary>Accepte le pas courant et ±<paramref name="window"/> pas (dérive d'horloge).</summary>
    public static bool Verify(string base32Secret, string? code, DateTimeOffset now, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6) return false;
        for (var i = -window; i <= window; i++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, now.AddSeconds(30 * i))), Encoding.ASCII.GetBytes(code)))
                return true;
        return false;
    }

    public static string OtpAuthUri(string account, string secret, string issuer = "Eden") =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string s)
    {
        var res = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var ch in s.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var v = Alphabet.IndexOf(ch); if (v < 0) continue;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { res.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return res.ToArray();
    }
}

/// <summary>Limiteur à fenêtre glissante par compte (garde-fou contre les rafales de commandes).</summary>
public sealed class AdminRateLimiter
{
    private readonly int _max; private readonly TimeSpan _window;
    private readonly Dictionary<int, Queue<DateTimeOffset>> _hits = new();
    private readonly object _lock = new();

    public AdminRateLimiter(int maxPerWindow = 30, TimeSpan? window = null) { _max = maxPerWindow; _window = window ?? TimeSpan.FromMinutes(1); }

    public bool TryAcquire(int accountId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_hits.TryGetValue(accountId, out var q)) _hits[accountId] = q = new Queue<DateTimeOffset>();
            while (q.Count > 0 && now - q.Peek() >= _window) q.Dequeue();
            if (q.Count >= _max) return false;
            q.Enqueue(now); return true;
        }
    }
}
