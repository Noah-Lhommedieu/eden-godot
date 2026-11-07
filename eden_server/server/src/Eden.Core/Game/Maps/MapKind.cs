namespace Eden.Core.Game.Maps;

/// <summary>
/// Type de map — UNE seule définition pour tout le serveur (PLAN-ACTION-3 §4.1
/// et §4.4). Remplace les deux définitions incohérentes de « donjon »
/// (<c>ZoneEntryFlow.IsDungeonNode</c> et <c>ZoneLiveWorld.IsDungeonNode</c>).
/// </summary>
public enum MapKind
{
    /// <summary>Carte inconnue / non classée.</summary>
    Unknown = 0,
    /// <summary>Monde ouvert : on perd de l'EXP sur une mort contre un monstre.</summary>
    Field,
    /// <summary>Instance partagée par tous (BTS, Dimension Dévastée, Labo Vingot…) :
    /// on croise des joueurs sans groupe, respawn rapide, boss durs.</summary>
    SharedInstance,
    /// <summary>Donjon classique, privé au personnage ou au groupe
    /// (inclut héroïque et épreuve).</summary>
    Dungeon,
    /// <summary>Mode spécial : tour, arènes, expéditions, champs de bataille, survie…</summary>
    SpecialMode,
    /// <summary>Épreuve scorée / Illusion Destructrice (système Evaluate).</summary>
    Trial,
    /// <summary>Ville de guilde (privée à la famille visitée).</summary>
    GuildCity,
}

/// <summary>Qui a causé la mort d'un joueur.</summary>
public enum KillerKind { Monster, Player, Environment }
