namespace Eden.Core.Networking;

/// <summary>
/// Classe de base de tous les messages réseau, miroir des classes <c>CNE_*</c>
/// du moteur <c>lapis</c>. Chaque message possède :
///  - un <see cref="Opcode"/> (équivalent de <c>GetNCID()</c>),
///  - une méthode <see cref="Serialize"/> (ordre des champs sur le réseau),
///  - une méthode <see cref="Deserialize"/> (lecture).
///
/// REMPLIR la table des opcodes depuis le reverse (voir docs/SETUP).
/// </summary>
public abstract class NetCommand
{
    /// <summary>Identifiant numérique d'opcode (GetNCID côté C++).</summary>
    public abstract int Opcode { get; }

    /// <summary>Nom lisible (GetName côté C++).</summary>
    public abstract string Name { get; }

    public abstract void Serialize(OutBinStream stream);

    public abstract void Deserialize(InBinStream stream);
}
