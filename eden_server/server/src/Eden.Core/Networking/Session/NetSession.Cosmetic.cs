using System.Buffers;
using Eden.Core.Logging;

namespace Eden.Core.Networking.Session;

/// <summary>Envoi cosmétique non bloquant de <see cref="NetSession"/> (PLAN-ACTION-3 lot N) : fichier séparé pour ne pas grossir la classe principale.</summary>
public sealed partial class NetSession
{
    /// <summary>Charge de la file d'envoi, de 0 (vide) à 1 (pleine) : sert à dégrader les mises à jour cosmétiques AVANT la saturation.</summary>
    public double QueuePressure => _isDisposed ? 1 : _sendQueue.Reader.Count / (double)SendQueueCapacity;

    /// <summary>Seuil au-delà duquel une mise à jour cosmétique est abandonnée au lieu d'être enfilée (laisse de la place au gameplay).</summary>
    public const double CosmeticBacklogLimit = 0.5;

    /// <summary>
    /// Envoi NON bloquant d'un paquet cosmétique (déplacement d'une entité vue) : jamais d'attente, jamais de fermeture de session.
    /// Abandonné (compté dans <c>eden.cosmetic_drops</c>) si la file dépasse <see cref="CosmeticBacklogLimit"/> : le prochain déplacement
    /// rattrape la position. Renvoie vrai si le paquet est parti en file.
    /// </summary>
    public bool TrySendCosmeticPacket(ushort ncid, byte[] payload)
    {
        if (_isDisposed) return false;
        if (QueuePressure >= CosmeticBacklogLimit)
        {
            EdenMetrics.CosmeticDrops.Add(1, new KeyValuePair<string, object?>("reason", "backlog"));
            return false;
        }
        var frame = ArrayPool<byte>.Shared.Rent(VarintCodec.MaxEncodedLength + 2 + payload.Length);
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)(2 + payload.Length));
        BitConverter.GetBytes(ncid).CopyTo(frame, hdrLen);
        payload.CopyTo(frame, hdrLen + 2);
        if (!_sendQueue.Writer.TryWrite((frame, hdrLen + 2 + payload.Length, hdrLen, 2 + payload.Length)))
        {
            ArrayPool<byte>.Shared.Return(frame);
            EdenMetrics.CosmeticDrops.Add(1, new KeyValuePair<string, object?>("reason", "full"));
            return false;
        }
        Interlocked.Increment(ref _globalQueued);
        _trace.Record(inbound: false, ncid, 2 + payload.Length);
        return true;
    }
}
