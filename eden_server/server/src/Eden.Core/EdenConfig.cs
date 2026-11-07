namespace Eden.Core;

/// <summary>Configuration centralisée du serveur — toutes les variables
/// d'environnement lues par les services, avec leurs valeurs par
/// défaut documentées. Lecture unique au premier accès (les services
/// s'arrêtent au démarrage si la valeur est invalide, pas plus tard).
/// Chaque propriété porte le nom de la variable et son rôle.</summary>
public static class EdenConfig
{
    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name);

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Env(name), out var v) ? v : fallback;

    private static ushort EnvUShort(string name, ushort fallback) =>
        ushort.TryParse(Env(name), out var v) ? v : fallback;

    private static bool EnvFlag(string name, bool defaultOn = true) =>
        Env(name) is { } v
            ? v is "1" or "true" or "TRUE"
            : defaultOn;

    // ---------- Base de données ----------
    /// <summary><c>EDEN_DB_CONN</c> — chaîne Npgsql UNIQUE de la base fusionnée
    /// <c>edenfr</c> (schémas game/account/member). Prioritaire sur les
    /// 3 variables historiques ci-dessous, lues via <c>DbConnectionFactory</c>.</summary>
    public static string? UnifiedDbConn => Env("EDEN_DB_CONN");
    /// <summary><c>EDEN_DB_POOL_MAX</c> — plafond de connexions Npgsql PAR POOL
    /// (3 pools par process : game/account/member). Absent = défaut Npgsql (100).</summary>
    public static int? DbPoolMax => int.TryParse(Env("EDEN_DB_POOL_MAX"), out var v) && v > 0 ? v : null;
    /// <summary><c>DB_CONN</c> — (legacy, rollback) base de jeu <c>edenfr_db</c>.</summary>
    public static string? DbConn => Env("DB_CONN");
    /// <summary><c>GAME_DB_CONN</c> — alias historique de <see cref="DbConn"/>.</summary>
    public static string? GameDbConn => Env("GAME_DB_CONN");
    /// <summary><c>ACCOUNT_DB_CONN</c> — base comptes (edenfr_account) pour Login.</summary>
    public static string? AccountDbConn => Env("ACCOUNT_DB_CONN");
    /// <summary><c>MEMBER_DB_CONN</c> — base membre du TicketServer.</summary>
    public static string? MemberDbConn => Env("MEMBER_DB_CONN");

    /// <summary><c>EDEN_REDIS_CONN</c> — chaîne StackExchange.Redis (ex.
    /// <c>localhost:6380</c>). Absente ou <c>off</c> : Redis désactivé,
    /// tickets et or/position/vitals restent 100 % PostgreSQL (rollback DB-3).</summary>
    public static string? RedisConn => Env("EDEN_REDIS_CONN") is { } v
        && !v.Equals("off", StringComparison.OrdinalIgnoreCase) && v.Length > 0 ? v : null;

    // ---------- Données ----------
    /// <summary><c>DATA_DIR</c> — racine des .ini parsés (S_Item, S_Spell…).</summary>
    public static string? DataDir => Env("DATA_DIR");
    /// <summary><c>EDEN_DATA</c> — alias de <see cref="DataDir"/>.</summary>
    public static string? EdenData => Env("EDEN_DATA");
    /// <summary><c>SCENE_DIR</c> — répertoire des fichiers de scène (S_xxx).</summary>
    public static string? SceneDir => Env("SCENE_DIR");
    /// <summary><c>NODEMAP_DIR</c> — répertoire des <c>Sxxx.smp</c>
    /// (murs invisibles ; défaut <c>&lt;Data&gt;/NodeMap</c>).</summary>
    public static string? NodeMapDir => Env("NODEMAP_DIR");
    /// <summary><c>EDEN_NODEMAP=0</c> coupe la validation serveur des
    /// déplacements contre les murs invisibles.</summary>
    public static bool NodeMapEnforce => Env("EDEN_NODEMAP") != "0";
    /// <summary><c>EDEN_LIMITLESS_PASS_INI</c> — chemin du .ini des
    /// récompenses du Limitless Pass (défaut
    /// <c>&lt;DATA_DIR&gt;/limitless_pass.ini</c>).</summary>
    public static string? LimitlessPassIni => Env("EDEN_LIMITLESS_PASS_INI");

    // ---------- Ports / adresses services ----------
    /// <summary><c>GATEWAY_PORT</c> — port Gateway (défaut 5560).</summary>
    public static int GatewayPort => EnvInt("GATEWAY_PORT", 5560);
    /// <summary><c>LOGIN_PORT</c> — port LoginServer (défaut 6543).</summary>
    public static int LoginPort => EnvInt("LOGIN_PORT", 6543);
    /// <summary><c>WORLD_IP</c> — adresse annoncée du WorldServer.</summary>
    public static string WorldIp => Env("WORLD_IP") ?? "127.0.0.1";
    /// <summary><c>WORLD_PORT</c> — port client du World (défaut 5200).</summary>
    public static int WorldPort => EnvInt("WORLD_PORT", 5200);
    /// <summary><c>WORLD_LINK_IP</c> — écoute du lien Zone→World.</summary>
    public static string WorldLinkIp => Env("WORLD_LINK_IP") ?? "127.0.0.1";
    /// <summary><c>WORLD_LINK_PORT</c> — port du lien Zone→World (5301).</summary>
    public static int WorldLinkPort => EnvInt("WORLD_LINK_PORT", 5301);
    /// <summary><c>WORLD_ADV_PORT</c> — port annoncé (défaut WORLD_PORT).</summary>
    public static int WorldAdvPort => EnvInt("WORLD_ADV_PORT", WorldPort);
    /// <summary><c>WORLD_CRYPTO</c> — RC4 activé côté World (défaut oui).
    /// (retiré en phase4-c2 — mode <c>EDEN_TRANSPORT=legacy</c> uniquement ;
    /// en mode modern le TLS chiffre toujours).</summary>
    public static bool WorldCrypto => EnvFlag("WORLD_CRYPTO");
    /// <summary><c>WORLD_FLOW</c> — mode de flux world (défaut "full").</summary>
    public static string WorldFlow => Env("WORLD_FLOW") ?? "full";
    /// <summary><c>WORLD_TICKET_CHECK_ADDR</c> — vérification d'adresse
    /// de ticket (défaut oui).</summary>
    public static bool WorldTicketCheckAddr => EnvFlag("WORLD_TICKET_CHECK_ADDR");
    /// <summary><c>ZONE_ID</c> — id de zone (défaut 1011).</summary>
    public static ushort ZoneId => EnvUShort("ZONE_ID", 1011);
    /// <summary><c>ZONE_IP</c> — adresse annoncée de la zone.</summary>
    public static string ZoneIp => Env("ZONE_IP") ?? "127.0.0.1";
    /// <summary><c>ZONE_PORT</c> — port client de la zone (défaut 10022).</summary>
    public static int ZonePort => EnvInt("ZONE_PORT", 10022);
    /// <summary><c>ZW_PORT</c> — port du lien Zone→World (défaut 5301).</summary>
    public static int ZwPort => EnvInt("ZW_PORT", 5301);
    /// <summary><c>ZONE_CRYPTO</c> — RC4 activé côté Zone (défaut oui).
    /// (retiré en phase4-c2 — mode <c>EDEN_TRANSPORT=legacy</c> uniquement ;
    /// en mode modern le TLS chiffre toujours).</summary>
    public static bool ZoneCrypto => EnvFlag("ZONE_CRYPTO");
    /// <summary><c>NODE_ID</c> — node par défaut d'entrée (défaut 5).</summary>
    public static int NodeId => EnvInt("NODE_ID", 5);

    /// <summary><c>EDEN_ZONE_LOCATE</c> — localisation du serveur
    /// (bit du masque Locate des S_Monster/S_Npc, cf.
    /// <c>lapis::ConvertLocate</c> @0x08589260 : HK=1 JP=2 CN=3 MY=4
    /// <b>US=5</b> KR=6 FR=7 DE=8 ES=9 PT=10 RU=11 TH=12 SG=13 ID=14
    /// VN=15). Défaut 5 — notre <c>server_us/setup.ini</c> dit
    /// <c>Locate=US</c>.</summary>
    public static int ZoneLocate => EnvInt("EDEN_ZONE_LOCATE", 5);
    /// <summary><c>EDEN_ZONE_PREWARM_MAPS</c> — maps supplémentaires
    /// dont le monde vivant (<c>ZoneLiveWorld.EnsureNodePopulated</c>)
    /// est préchauffé au boot : codes « S001 » ou ids numériques,
    /// séparés par des virgules. Les maps à spawn joueur (aire BORN)
    /// sont toujours préchauffées, cette liste ne fait qu'ajouter
    /// (ex. la ville S001, 431 mobs + 256 NPC).</summary>
    public static string? ZonePrewarmMaps => Env("EDEN_ZONE_PREWARM_MAPS");
    /// <summary><c>EDEN_DEV_WORK_CAP</c> — plafond optionnel des durées
    /// d'ordres de travail en unités de 100 ms (100 = 10 s). 0/absent =
    /// durées natives des tables (col min × 600 — mine 30 min, etc.) ;
    /// une valeur &gt;0 borne les ordres pour itérer vite en dev. Le cap
    /// s'applique à l'armement ET au rechargement des ordres encore en
    /// cours à la reconnexion.</summary>
    public static int DevWorkCapUnits => EnvInt("EDEN_DEV_WORK_CAP", 0);
    /// <summary><c>MISSION_HOST</c> — hôte du MissionServer.</summary>
    public static string MissionHost => Env("MISSION_HOST") ?? "127.0.0.1";
    /// <summary><c>MISSION_PORT</c> — port du MissionServer (7654).</summary>
    public static int MissionPort => EnvInt("MISSION_PORT", 7654);
    /// <summary><c>TICKET_ENABLED</c> — TicketServer actif (défaut oui).</summary>
    public static bool TicketEnabled => EnvFlag("TICKET_ENABLED");
    /// <summary><c>TICKET_IP</c> — hôte du TicketServer.</summary>
    public static string TicketIp => Env("TICKET_IP") ?? "127.0.0.1";
    /// <summary><c>TICKET_PORT</c> — port du TicketServer (défaut 2067).</summary>
    public static int TicketPort => EnvInt("TICKET_PORT", 2067);
    /// <summary><c>POC_ADVERTISE_IP</c> — IP annoncée au client dans le
    /// ticket (dev).</summary>
    public static string? AdvertiseIp => Env("POC_ADVERTISE_IP");
    /// <summary><c>POC_ADVERTISE_PORT</c> — port annoncé au client.</summary>
    public static int? AdvertisePort =>
        int.TryParse(Env("POC_ADVERTISE_PORT"), out var v) ? v : null;

    // ---------- Sécurité / crypto ----------
    /// <summary><c>EDEN_RSA_PEM</c> — chemin de la clé RSA serveur.
    /// (retiré en phase4-c2 — mode legacy uniquement).</summary>
    public static string? RsaPem => Env("EDEN_RSA_PEM");
    /// <summary><c>EDEN_PLAIN_HELLO</c> — hello en clair (diag, défaut non).
    /// (retiré en phase4-c2 — mode legacy uniquement).</summary>
    public static bool PlainHello => EnvFlag("EDEN_PLAIN_HELLO", false);

    // ---------- Transport (phase 4 incrément c : TLS 1.3) ----------
    /// <summary><c>EDEN_TRANSPORT</c> — « <c>modern</c> » (TLS 1.3 via
    /// <c>SslStream</c>, défaut — le client Godot est le seul client) ou
    /// « <c>legacy</c> » (handshake RSA blob + RC4 lapis, repli de
    /// transition exactement identique à l'état pré-c).</summary>
    public static string Transport => Env("EDEN_TRANSPORT") ?? "modern";
    /// <summary>Vrai en mode <c>EDEN_TRANSPORT=legacy</c> — les
    /// accept-paths et liens internes gardent alors le RSA/RC4
    /// historique (<see cref="EdenConfig.RsaPem"/>, <c>*_CRYPTO</c>).</summary>
    public static bool LegacyTransport =>
        string.Equals(Transport, "legacy", StringComparison.OrdinalIgnoreCase);
    /// <summary><c>EDEN_TLS_PFX</c> — chemin du certificat TLS serveur
    /// (PFX production). Absent → certificat dev auto-signé persistant
    /// <c>&lt;server&gt;/certs/eden-dev.pfx</c> généré par
    /// <c>TlsCertificateProvider</c>.</summary>
    public static string? TlsPfx => Env("EDEN_TLS_PFX");
    /// <summary><c>EDEN_TLS_PFX_PASSWORD</c> — mot de passe du PFX
    /// <see cref="TlsPfx"/> (vide/null = PFX non protégé).</summary>
    public static string? TlsPfxPassword => Env("EDEN_TLS_PFX_PASSWORD");
    /// <summary><c>EDEN_TLS_THUMBPRINT</c> — empreinte SHA-256 hex du
    /// certificat attendu côté clients/liens (pinning ; séparateurs
    /// « : » ou espaces tolérés, casse ignorée).</summary>
    public static string? TlsThumbprint => Env("EDEN_TLS_THUMBPRINT");
    /// <summary><c>EDEN_LINK_TLS</c> — validation TLS des liens internes
    /// (loopback serveur↔serveur) : « <c>pin</c> » = empreinte SHA-256
    /// (<see cref="TlsThumbprint"/>, sinon le cert local partagé),
    /// « <c>insecure</c> » = accept-any dev (warning log). Défaut : pin
    /// quand une empreinte est disponible, insecure sinon.</summary>
    public static string? LinkTls => Env("EDEN_LINK_TLS");

    // ---------- Handshake de version (phase 4 incrément d) ----------
    /// <summary><c>EDEN_VERSION_CHECK</c> — handshake de version
    /// obligatoire sur les accept-paths client-facing (Login/World/
    /// Zone) en mode modern (défaut oui). Jamais appliqué en mode
    /// <c>EDEN_TRANSPORT=legacy</c> ni sur les liens internes.</summary>
    public static bool VersionCheck => EnvFlag("EDEN_VERSION_CHECK");
    /// <summary><c>EDEN_PROTOCOL_VERSION</c> — version de protocole du
    /// serveur « major.minor » (défaut « 1.0 ») — le client dont la
    /// version est strictement supérieure est rejeté « TooNew ».</summary>
    public static string ProtocolVersion =>
        Env("EDEN_PROTOCOL_VERSION") ?? "1.0";
    /// <summary><c>EDEN_MIN_PROTOCOL_VERSION</c> — version de protocole
    /// minimale acceptée « major.minor » (défaut
    /// = <see cref="ProtocolVersion"/> : tout client parlant le
    /// protocole courant). En dessous → rejet « UpdateRequired ».</summary>
    public static string MinProtocolVersion =>
        Env("EDEN_MIN_PROTOCOL_VERSION") ?? ProtocolVersion;
    /// <summary><c>EDEN_MIN_CLIENT_VERSION</c> — version applicative
    /// minimale du client (« 002.003.05.67 », comparaison numérique
    /// pointée). Absente → pas de contrôle de version applicative.</summary>
    public static string? MinClientVersion => Env("EDEN_MIN_CLIENT_VERSION");
    /// <summary><c>EDEN_GAMEDATA_DIR</c> — racine du corpus gamedata
    /// dont le manifest SHA-256 est calculé au boot
    /// (<c>*.ini</c> récursif, chemins triés). Repli :
    /// <see cref="DataDir"/> puis <see cref="EdenData"/>.</summary>
    public static string? GameDataDir =>
        Env("EDEN_GAMEDATA_DIR") ?? DataDir ?? EdenData;
    /// <summary><c>EDEN_GAMEDATA_HASH</c> — empreinte SHA-256 hex du
    /// manifest gamedata à annoncer aux clients — prioritaire sur le
    /// calcul depuis <see cref="GameDataDir"/> (services sans fichiers
    /// gamedata locaux, ex. Login/World déployés sans DATA_DIR).</summary>
    public static string? GameDataHash => Env("EDEN_GAMEDATA_HASH");
    /// <summary><c>EDEN_MANIFEST_STRICT</c> — 1 = un manifest client
    /// différent du manifest serveur rejette la session
    /// (UpdateRequired) ; 0 (défaut) = mismatch logué mais admis
    /// (la version applicative reste le seul critère bloquant).</summary>
    public static bool ManifestStrict => EnvFlag("EDEN_MANIFEST_STRICT", false);

    // ---------- Limites / tuning ----------
    /// <summary><c>EDEN_MAX_PACKETS_PER_SEC</c> — paquets entrants max
    /// par session (0 = off, défaut 300).</summary>
    public static int MaxPacketsPerSecond =>
        EnvInt("EDEN_MAX_PACKETS_PER_SEC", 300);
    /// <summary><c>EDEN_LOGIN_MAX_ATTEMPTS</c> — tentatives de login par
    /// IP et par minute (défaut 10).</summary>
    public static int LoginMaxAttempts =>
        EnvInt("EDEN_LOGIN_MAX_ATTEMPTS", 10);
    /// <summary><c>EDEN_SEND_TIMEOUT_MS</c> — délai max d'enfilement
    /// d'une trame sortante (défaut 5000 ms).</summary>
    public static int SendTimeoutMs => EnvInt("EDEN_SEND_TIMEOUT_MS", 5000);
    /// <summary><c>EDEN_SEND_QUEUE_CAPACITY</c> — trames max en attente
    /// par session avant backpressure (défaut 256).</summary>
    public static int SendQueueCapacity =>
        EnvInt("EDEN_SEND_QUEUE_CAPACITY", 256);
    /// <summary><c>EDEN_ZONE_COMMAND_BUDGET_MS</c> — budget de drain
    /// des commandes ZoneLoop par itération (défaut 2 ms).</summary>
    public static int ZoneCommandBudgetMs =>
        EnvInt("EDEN_ZONE_COMMAND_BUDGET_MS", 2);
    /// <summary><c>EDEN_ZONE_MAX_SESSIONS</c> — sessions TCP max
    /// acceptées par la zone (défaut 2048, au-delà → refus immédiat).
    /// </summary>
    public static int ZoneMaxSessions =>
        EnvInt("EDEN_ZONE_MAX_SESSIONS", 2048);
    /// <summary><c>EDEN_TCP_KEEPALIVE_TIME_SEC</c> — silence avant la
    /// première sonde keep-alive TCP (défaut 60 s).</summary>
    public static int TcpKeepAliveTimeSec =>
        EnvInt("EDEN_TCP_KEEPALIVE_TIME_SEC", 60);
    /// <summary><c>EDEN_TCP_KEEPALIVE_INTERVAL_SEC</c> — intervalle
    /// entre sondes keep-alive (défaut 10 s).</summary>
    public static int TcpKeepAliveIntervalSec =>
        EnvInt("EDEN_TCP_KEEPALIVE_INTERVAL_SEC", 10);
    /// <summary><c>EDEN_TCP_KEEPALIVE_RETRIES</c> — sondes sans
    /// réponse avant que le noyau déclare la connexion morte
    /// (défaut 5).</summary>
    public static int TcpKeepAliveRetryCount =>
        EnvInt("EDEN_TCP_KEEPALIVE_RETRIES", 5);
    /// <summary><c>EDEN_HANDSHAKE_TIMEOUT_SEC</c> — délai max du
    /// handshake RSA/RC4 après accept (défaut 10 s).</summary>
    public static int HandshakeTimeoutSec =>
        EnvInt("EDEN_HANDSHAKE_TIMEOUT_SEC", 10);
    /// <summary><c>EDEN_IDLE_TIMEOUT_SEC</c> — délai sans octet reçu
    /// avant fermeture (défaut 300 s, ≤0 désactive).</summary>
    public static int IdleTimeoutSec =>
        EnvInt("EDEN_IDLE_TIMEOUT_SEC", 300);
    /// <summary><c>EDEN_TICKET_TTL_SEC</c> — durée de validité d'un
    /// ticket login→world ou world→zone (défaut 300 s — P0.2).</summary>
    public static int TicketTtlSec =>
        EnvInt("EDEN_TICKET_TTL_SEC", 300);
    /// <summary><c>EDEN_MAX_FRAME_SIZE</c> — taille max du payload
    /// d'une trame (NCID inclus) annoncée par le préfixe varint ;
    /// au-delà → trame malformée, fermeture (anti-flood). Défaut
    /// 1 Mio.</summary>
    public static int MaxFrameSize =>
        EnvInt("EDEN_MAX_FRAME_SIZE", 1024 * 1024);
    /// <summary><c>EDEN_PERSIST_FLUSH_MS</c> — période de flush
    /// write-behind (défaut 15000 ms).</summary>
    public static int PersistFlushMs =>
        EnvInt("EDEN_PERSIST_FLUSH_MS", 15_000);
    /// <summary><c>EXP_RATE</c> — multiplicateur d'exp globale
    /// (défaut 100 — cadence de test rapide demandée).</summary>
    public static double ExpRate =>
        double.TryParse(Env("EXP_RATE"),
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 100.0;
    /// <summary><c>CLASS_EXP_RATE</c> — multiplicateur d'exp de classe
    /// (défaut = <see cref="ExpRate"/>).</summary>
    public static double ClassExpRate =>
        double.TryParse(Env("CLASS_EXP_RATE"),
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : ExpRate;

    // ---------- World boss / mutants (plan futur-feature/12) ----------
    /// <summary><c>EDEN_MUTANTS</c> — apparition dynamique des 6 mutants
    /// 60761-60766 (défaut oui ; 0 = désactivé).</summary>
    public static bool MutantsEnabled => EnvFlag("EDEN_MUTANTS", true);
    /// <summary><c>EDEN_MUTANT_MIN_MIN</c> / <c>EDEN_MUTANT_MAX_MIN</c> —
    /// attente (minutes) entre la disparition d'un mutant et son retour
    /// (défaut 30 / 90 ; cadence non présente dans les exports).</summary>
    public static int MutantMinIntervalMin => EnvInt("EDEN_MUTANT_MIN_MIN", 30);
    /// <summary>Voir <see cref="MutantMinIntervalMin"/>.</summary>
    public static int MutantMaxIntervalMin => EnvInt("EDEN_MUTANT_MAX_MIN", 90);
    /// <summary><c>EDEN_MUTANT_LIFETIME_MIN</c> — présence maximale d'un
    /// mutant non tué (minutes, défaut 60).</summary>
    public static int MutantLifetimeMin => EnvInt("EDEN_MUTANT_LIFETIME_MIN", 60);
    /// <summary><c>EDEN_MUTANT_MAX_CONCURRENT</c> — mutants simultanés
    /// au plus (défaut 3).</summary>
    public static int MutantMaxConcurrent => EnvInt("EDEN_MUTANT_MAX_CONCURRENT", 3);
    /// <summary><c>EDEN_WORLDBOSS_KILL_ANNOUNCE</c> — annonce monde à la
    /// mort d'un world boss fixe (défaut NON : aucune preuve d'annonce
    /// native pour les boss fixes ; extension Eden, indice 30001).</summary>
    public static bool WorldBossKillAnnounce =>
        EnvFlag("EDEN_WORLDBOSS_KILL_ANNOUNCE", false);
    /// <summary><c>EDEN_WORLDBOSS_CAST_WARN</c> — texte 1647 « %s1 explosera
    /// à la fin du compte à rebours » diffusé quand un boss lance un sort
    /// à temps d'incantation long (défaut NON : déclencheur natif non
    /// prouvé, HYPOTHESIS).</summary>
    public static bool WorldBossCastWarn =>
        EnvFlag("EDEN_WORLDBOSS_CAST_WARN", false);
    /// <summary><c>EDEN_WORLDBOSS_CAST_WARN_MS</c> — seuil de cast (ms)
    /// déclenchant l'avertissement (défaut 3000).</summary>
    public static int WorldBossCastWarnMs =>
        EnvInt("EDEN_WORLDBOSS_CAST_WARN_MS", 3000);

    // ---------- Debug / observabilité ----------
    /// <summary><c>EDEN_NET_DEBUG</c> — dump hex des trames (défaut non).</summary>
    public static bool NetDebug => EnvFlag("EDEN_NET_DEBUG", false);
    /// <summary><c>EDEN_ZONE_TRACE</c> — trace détaillée de zone.</summary>
    public static bool ZoneTrace => EnvFlag("EDEN_ZONE_TRACE", false);
    /// <summary><c>EDEN_MISSION_TRACE</c> — trace du MissionServer.</summary>
    public static bool MissionTrace => EnvFlag("EDEN_MISSION_TRACE", false);
    /// <summary><c>EDEN_LOG_LEVEL</c> — niveau MEL global.</summary>
    public static string? LogLevel => Env("EDEN_LOG_LEVEL");
    /// <summary><c>EDEN_OBSERVABILITY_PORT</c> — port HTTP de
    /// l'endpoint <c>/metrics</c> + <c>/health</c> (0 = désactivé).</summary>
    public static int ObservabilityPort =>
        EnvInt("EDEN_OBSERVABILITY_PORT", 0);
    /// <summary><c>EDEN_OBSERVABILITY_BIND</c> — préfixe HTTP
    /// d'écoute (défaut loopback ; mettre <c>http://0.0.0.0</c> en
    /// conteneur ou pour Prometheus distant).</summary>
    public static string ObservabilityBind =>
        Env("EDEN_OBSERVABILITY_BIND") ?? "http://127.0.0.1";
}
