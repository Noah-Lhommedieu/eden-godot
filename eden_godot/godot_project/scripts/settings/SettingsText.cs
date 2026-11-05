namespace EdenClient.Settings;

/// <summary>Libellés FR du panneau d'options (EDENFR pur). Clé = chaîne
/// anglaise passée aux helpers de <see cref="SettingsWindow"/> ; une clé
/// absente retombe sur <c>UiTextEn.Translate</c> puis sur elle-même.
/// Pour ajouter une option : l'ajouter ici, rien d'autre.</summary>
internal static class SettingsText
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // onglets
        ["Display"] = "Affichage", ["Graphics"] = "Graphismes",
        ["Audio"] = "Son", ["Hotkeys"] = "Touches",
        ["UI Settings"] = "Interface", ["Preferences"] = "Préférences",
        // barre
        ["Default"] = "Défaut", ["Confirm"] = "Valider", ["Cancel"] = "Annuler",
        ["LOW"] = "Bas", ["MEDIUM"] = "Moyen", ["HIGH"] = "Élevé",
        ["ULTRA"] = "Ultra", ["CUSTOM"] = "Personnalisé",
        // affichage
        ["Window"] = "Mode d'affichage", ["Screen"] = "Écran", ["Resolution"] = "Résolution",
        ["Windowed"] = "Fenêtré", ["Fullscreen"] = "Plein écran",
        ["Borderless windowed"] = "Fenêtré sans bordure",
        ["VSync"] = "Synchronisation verticale", ["Max FPS"] = "Images/s max",
        ["Unlimited"] = "Illimité", ["Performance"] = "Performances",
        // graphismes
        ["Optimize"] = "Préréglage", ["Quality"] = "Qualité générale",
        ["Shadows"] = "Ombres", ["Shadow quality"] = "Qualité des ombres",
        ["Field"] = "Distance d'affichage", ["Char Draw"] = "Joueurs affichés",
        ["Max FX"] = "Effets simultanés max", ["Anti-aliasing"] = "Anticrénelage",
        ["Off"] = "Désactivé", ["Models"] = "Modèles", ["Low"] = "Bas",
        ["High"] = "Haut", ["Textures"] = "Textures", ["Mid"] = "Moyen",
        ["Glow FX"] = "Halo lumineux", ["Background"] = "Images/s en arrière-plan",
        ["Outline"] = "Contour", ["Character"] = "Personnages", ["All"] = "Tout",
        ["SSAO"] = "Occlusion ambiante (SSAO)", ["SSIL"] = "Illumination indirecte (SSIL)",
        ["Fog"] = "Brouillard", ["Groundcover"] = "Herbe et végétation",
        ["Particles"] = "Particules", ["Special FX"] = "Effets spéciaux",
        ["Death"] = "Mort", ["Photo"] = "Photo", ["Blur"] = "Flou",
        ["Glare"] = "Éblouissement", ["Water Wave"] = "Vagues", ["3D Effect"] = "Effet 3D",
        ["Lighting"] = "Ombres et lumière", ["World"] = "Monde et décor",
        ["Distance & density"] = "Distance et densité",
        ["Textures: anisotropic filtering applies now; asset resolution needs a restart."] =
            "Textures : le filtrage anisotrope s'applique immédiatement ; la résolution des ressources demande un redémarrage.",
        // interface
        ["Avoid setting font size larger than UI"] =
            "Évitez une taille de police supérieure à l'interface.",
        ["UI Size"] = "Taille de l'interface", ["UI Style"] = "Thème",
        ["Game Font"] = "Police du jeu", ["Font Size"] = "Taille de police",
        ["Chat Font Size"] = "Taille du chat", ["Language"] = "Langue",
        ["Size & style"] = "Taille et style", ["Fonts"] = "Polices",
        // touches
        ["Click a key, then press the new key or mouse button (wheel ignored, Esc cancels)."] =
            "Cliquez une touche, puis appuyez sur la nouvelle touche ou le bouton de souris (molette ignorée, Échap annule).",
        ["Sit / Stand is shown on your character only (the server has no sit message)."] =
            "S'asseoir / se lever n'est visible que sur votre personnage (le serveur n'a pas de message d'assise).",
        ["Move forward"] = "Avancer", ["Move back"] = "Reculer",
        ["Move left"] = "Aller à gauche", ["Move right"] = "Aller à droite",
        ["Run"] = "Courir", ["Jump"] = "Sauter",
        ["Camera: zoom in"] = "Caméra : zoom avant", ["Camera: zoom out"] = "Caméra : zoom arrière",
        ["Inventory"] = "Inventaire", ["Profile"] = "Profil", ["Skills"] = "Compétences",
        ["Quests"] = "Quêtes", ["Map"] = "Carte", ["Pet"] = "Familier",
        ["Sit / Stand"] = "S'asseoir / se lever", ["Toggle WPN"] = "Ranger / sortir l'arme",
        ["Attack"] = "Attaquer", ["Cancel cast"] = "Annuler l'incantation",
        ["Achievements"] = "Succès", ["Community"] = "Communauté",
        ["Chat"] = "Chat", ["System menu"] = "Menu système",
        // son
        ["Mute"] = "Couper le son", ["Master"] = "Volume général",
        ["Music"] = "Musique", ["SFX"] = "Effets sonores",
        ["Music/SFX buses are created on demand — no in-game sounds yet."] =
            "Les bus Musique / Effets sont créés à la demande.",
        // préférences
        ["Mouse"] = "Vitesse de la souris",
        ["My character"] = "Mon personnage", ["My name"] = "Mon nom",
        ["My guild"] = "Ma guilde", ["My title"] = "Mon titre",
        ["Other players"] = "Autres joueurs", ["Names"] = "Noms",
        ["Guilds"] = "Guildes", ["Titles"] = "Titres",
        ["Only shown when the server sends the guild of other players."] =
            "Affichée seulement si le serveur envoie la guilde des autres joueurs.",
        ["NPC Name"] = "Noms des PNJ", ["Vitals"] = "PV / PM",
        ["Damage"] = "Dégâts", ["FPS / Ping"] = "FPS / Ping",
        ["Show IDs"] = "Afficher les ID", ["Jelly Effect"] = "Effet gelée",
        ["Combat"] = "Combat", ["Self-Cast"] = "Lancer sur soi",
        ["Critical Shock"] = "Secousse critique", ["Vibration"] = "Vibration",
        ["Target"] = "Cible",
        ["Show HP bar above monsters"] = "Barre de PV au-dessus des monstres",
        ["Show HP bar above myself"] = "Barre de PV au-dessus de moi",
        ["Show HP bar above other players"] = "Barre de PV au-dessus des autres joueurs",
        ["Camera"] = "Caméra", ["Invert Camera"] = "Inverser la caméra",
        ["Camera Follow"] = "Caméra qui suit", ["Left-click to route"] = "Clic gauche : déplacement",
        ["Social"] = "Social", ["Refuse Trade"] = "Refuser les échanges",
        ["No Duel"] = "Refuser les duels", ["No Party"] = "Refuser les groupes",
        ["HP loss animation"] = "Animation de perte de PV",
        ["Animate HP loss (orange)"] = "Animer la perte de PV (orange)",
        ["Delay before drop (s)"] = "Délai avant la chute (s)",
        ["Drop duration (s)"] = "Durée de la chute (s)",
        ["Reset preferences"] = "Réinitialiser les préférences",
    };

    /// <summary>Traduit <paramref name="s"/> ; inchangé si inconnu.</summary>
    public static string T(string s)
    {
        if (Map.TryGetValue(s, out var fr)) return fr;
        if (s.StartsWith("Party Member ")) return "Membre du groupe " + s[13..];
        if (s.StartsWith("Hotbar slot ")) return "Barre rapide " + s[12..];
        return Data.UiTextEn.Translate(s);
    }
}
