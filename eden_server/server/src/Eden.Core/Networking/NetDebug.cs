using Eden.Core;
namespace Eden.Core.Networking;

/// <summary>
/// Indicateur de débogage réseau (<c>EDEN_NET_DEBUG=1</c>), lu une seule
/// fois au premier accès puis mis en cache — la variable d'environnement
/// était évaluée à chaque paquet émis (hot path).
/// </summary>
public static class NetDebug
{
    /// <summary><c>true</c> si <c>EDEN_NET_DEBUG=1</c> au démarrage du
    /// processus.</summary>
    public static readonly bool Enabled = EdenConfig.NetDebug;
}
