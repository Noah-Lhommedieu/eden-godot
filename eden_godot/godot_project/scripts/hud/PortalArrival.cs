namespace EdenClient.Hud;

/// <summary>
/// Synchronisation « arrivée sur un node » entre <c>GameScene</c>
/// (réception 313 EnterNodeNodeData → chargement de la map → position
/// serveur appliquée) et la détection des portails de
/// <see cref="MissionBridge"/> (plan 3.2.2 : le joueur n'arrivait
/// jamais au bon point de spawn).
///
/// Bug corrigé : la scène du NOUVEAU node était chargée au 313 alors
/// que le joueur portait encore l'ANCIENNE position → CheckNodeExit
/// envoyé avec une position d'un autre repère (rejet serveur
/// « hors aire » ou, pire, ré-entrée dans un portail inexistant), et à
/// l'arrivée sur l'aire de portail jumelée (spawn = centre de l'aire
/// pairée) le portail se redéclenchait aussitôt (ping-pong de maps).
/// Natif : l'entrée dans une aire est un front (il faut en sortir puis
/// y rentrer) — on arme donc l'aire où l'on atterrit comme « déjà
/// dedans ».
/// </summary>
public static class PortalArrival
{
    private static volatile bool _pending;
    private static int _version;

    /// <summary>Vrai entre la réception du 313 et l'application de la
    /// position serveur (map en chargement) : aucun portail ne doit
    /// se déclencher.</summary>
    public static bool Pending => _pending;

    /// <summary>Incrémenté à chaque arrivée appliquée.</summary>
    public static int Version => _version;

    /// <summary>Dernier node annoncé par un 313 (posé par GameScene).
    /// Le 313 du login arrive AVANT que MissionBridge se lie : sans ce
    /// relais sa scène (aires GATEWAY) restait vide tant qu'aucun 2e 313
    /// n'arrivait → aucun portail utilisable sur la map d'entrée.</summary>
    public static volatile int CurrentNode;

    public static void Begin() => _pending = true;

    public static void Arrived()
    {
        Interlocked.Increment(ref _version);
        _pending = false;
    }
}
