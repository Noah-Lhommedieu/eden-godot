namespace EdenClient.Net;

/// <summary>
/// Classe de base de tous les messages réseau, miroir des classes <c>CNE_*</c>
/// du moteur <c>lapis</c> (et de NetCommand côté serveur C#).
/// Chaque message possède un <see cref="Opcode"/> (= GetNCID()), un
/// <see cref="Name"/>, <see cref="Serialize"/> et <see cref="Deserialize"/>.
///
/// Référence : $EDEN/server/src/Eden.Core/Networking/NetCommand.cs.
/// </summary>
public abstract class NetMessage
{
    /// <summary>Identifiant numérique d'opcode (GetNCID côté C++).</summary>
    public abstract int Opcode { get; }

    /// <summary>Nom lisible (GetName côté C++).</summary>
    public abstract string Name { get; }

    /// <summary>Écrit le corps du message (sans le u16 NCID).</summary>
    public abstract void Serialize(BinWriter stream);

    /// <summary>Lit le corps du message (sans le u16 NCID).</summary>
    public abstract void Deserialize(BinReader stream);

    /// <summary>Payload complet = [u16 NCID][corps sérialisé].</summary>
    public byte[] ToPayload()
    {
        var w = new BinWriter();
        w.WriteShort((short)Opcode);
        Serialize(w);
        return w.ToArray();
    }
}
