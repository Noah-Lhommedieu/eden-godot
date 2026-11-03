using System.Text;

namespace Eden.Core.Networking.Session;

/// <summary>
/// Anneau des N derniers paquets (entrée/sortie) d'une session, vidé
/// dans les logs à la déconnexion anormale ou à l'exception — permet de
/// reconstruire la séquence NCID qui a précédé un désync RC4 ou un
/// crash handler sans activer <see cref="NetDebug"/> en continu.
/// Aucune donnée utile (payload) n'est conservée : direction, NCID et
/// longueur seulement.
/// </summary>
public sealed class PacketTrace
{
    /// <summary>Nombre d'entrées conservées par session.</summary>
    public const int Capacity = 32;

    private readonly (bool Inbound, int Ncid, int Len, DateTime Ts)[]
        _entries = new (bool, int, int, DateTime)[Capacity];
    private int _head;
    private int _count;

    /// <summary>Enregistre un paquet (O(1), sans allocation).</summary>
    /// <param name="inbound">true = reçu du client, false = émis.</param>
    /// <param name="ncid">NCID du paquet (-1 si inconnu, ex. trame
    /// brute du handshake).</param>
    /// <param name="len">Taille du payload (NCID inclus).</param>
    public void Record(bool inbound, int ncid, int len)
    {
        _entries[_head] = (inbound, ncid, len, DateTime.Now);
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>Rend la trace du plus ancien au plus récent.</summary>
    public string Dump()
    {
        if (_count == 0) return "  (trace vide)";
        var sb = new StringBuilder(_count * 40);
        // Les _count dernières entrées, de la plus ancienne à la plus
        // récente : quand l'anneau est plein, _head pointe sur le plus
        // ancien ; sinon les entrées commencent à 0.
        var start = _count < Capacity ? 0 : _head;
        for (var i = 0; i < _count; i++)
        {
            var e = _entries[(start + i) % Capacity];
            sb.AppendLine(
                $"  [{e.Ts:HH:mm:ss.fff}] {(e.Inbound ? "IN " : "OUT")} " +
                $"NCID={(e.Ncid < 0 ? "?" : e.Ncid.ToString()),4} len={e.Len}");
        }
        return sb.ToString();
    }
}
