using System.Collections.Concurrent;
using Godot;
using EdenClient.Combat;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Scene;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient;

/// <summary>
/// Scène de jeu (<c>res://scenes/game.tscn</c>) — deux modes :
///
///   - <b>solo</b> (défaut) : charge <see cref="DefaultMap"/> ou
///     <c>--map=Sxxx</c>, spawn local sur REVIVE/GATEWAY/[position].
///   - <b>live</b> (<c>--live</c>) : enchaîne la séquence réseau
///     complète login→world→zone via <see cref="EdenSession"/>
///     (args <c>--host/--account/--password/--char-id/--world-id</c>),
///     recharge la map sur EnterNodeNodeData (313, Field1 = nodeId),
///     branche <see cref="EntityRegistry"/> sur les NCIDs d'entités
///     (298 spawn mob/NPC, 265 joueur, 326 mouvement, 269/294 despawn…)
///     et envoie MoveSelf (NCID 1) throttlé ~10 Hz quand le joueur
///     bouge.
///
/// Threading : les trames arrivent sur les threads de la session —
/// tous les handlers postent des actions dans <see cref="_pending"/>
/// drainée par <see cref="_Process"/> (thread principal Godot).
/// </summary>
public partial class GameScene : Node3D
{
    /// <summary>Map par défaut (mode solo et en attente du NodeData).</summary>
    public const string DefaultMap = "S009";

    private MapLoader? _map;
    /// <summary>Streaming des corps NPC après l'entrée en jeu —
    /// vidés à ~3/frame dans <see cref="_Process"/>.</summary>
    private bool _npcDrainBudget = true;
    private Node3D? _mapRoot;
    private PlayerController? _player;
    private Label? _debugLabel;
    private string _mapCode = DefaultMap;
    private bool _loadFailed;

    // ---- live ----
    private bool _live;
    /// <summary>Session attachée par le flux d'écrans (SessionCarry) —
    /// la sélection de perso + ticket zone sont déjà faits ; on ne
    /// fait que ZoneConnect + ZoneEntry.</summary>
    private bool _attached;
    private string _host = "127.0.0.1";
    private string _account = "admin";
    /// <summary>Mot de passe local par défaut = adminmdp (seed
    /// Database/login-poc.sql — « admin » ne marche pas).</summary>
    private string _password = "adminmdp";
    private uint? _charId;
    private short? _worldId;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly CancellationTokenSource _liveCts = new();
    private int _nodeId;              // node courant (Field1 du 313)
    private Vector3 _lastSentPos;
    private double _lastMoveSent;
    private bool _haveServerPos;
    private Dictionary<int, MonsterTable.MonsterDef>? _monsterTable;
    private string _netStatus = "solo";

    // ---- HUD (scenes/hud.tscn — monté en solo ET live) ----
    private bool _noHud;            // debug : --nohud
    private bool _debug;            // --debug ou F12 : DebugUI visible
    private int _hudDebugFrames;    // debug : --huddebug (compte à rebours)
    private Hud.HudRoot? _hud;
    private uint _selfCharId;
    private string _selfName = "";
    private long _hp, _maxHp, _mp, _maxMp;
    private string _zoneLabel = "";
    // --shot=<path> : capture après ~240 frames puis quit (démo HUD).
    private int _shotFrames = -1;
    private bool _shotArgParsed;
    private string _shotPath = "/tmp/eden_game.png";
    /// <summary>--shot=&lt;path&gt;:&lt;sec&gt; : deadline horloge — le
    /// compteur frames suppose 60 fps mais le jeu tourne souvent
    /// plus vite (scène légère, pas de vsync forcée) ; le délai
    /// réel est garanti par l'horloge.</summary>
    private DateTime _shotDeadline = DateTime.MaxValue;
    /// <summary>Horodatage du HudReady — le shot attend 1,5 s de HUD
    /// prêt pour laisser --hud-open/--hud-dump s'appliquer.</summary>
    private DateTime _hudReadyAt = DateTime.MaxValue;
    /// <summary>--talk-test : après l'entrée zone, se téléporte près du
    /// NPC le plus proche et envoie TalkToNpc (preuve live du 285).</summary>
    private bool _talkTest;
    /// <summary>--flow-test : pilote QA des interactions — répond aux
    /// listes de missions (285 type 0xff → ReplyDialog 0), accepte la
    /// fiche quête (388 op=1 → 87 MissionListOp accept), achète le 1er
    /// article d'une boutique (22), équipe le 1er item équipable du
    /// sac (7+8 → 282). Les états sont logués « [verify] … ».</summary>
    private bool _flowTest;
    private bool _flowEquipDone;
    /// <summary>NPCs dont la 1re option a déjà été « cliquée » par le
    /// --flow-test (anti-boucle gossip).</summary>
    private readonly HashSet<int> _flowReplied = new();
    /// <summary>--quest-test=&lt;npcId&gt; : téléporte le joueur près d'un
    /// NPC de quête précis de la scène et lui parle — combiné à
    /// --flow-test (ReplyDialog + MissionListOp) prouve accept/reward
    /// de quête par usage réel.</summary>
    private int _questNpc;
    /// <summary>--service-test : téléporte le joueur près de chaque
    /// NPC de la scène ([npc] de sNNN.ini) et parle — couvre les NPC
    /// de service hors du champ de vue initial (transporteur,
    /// banquier, artisan…) que --talk-test n'atteint pas.</summary>
    private bool _serviceTest;
    /// <summary>--mob-test : se déclare au spawn hostile réel le plus
    /// proche ([reborn_monsters]) pour forcer les 298 avant
    /// --combat-test — mêmes moyens que --service-test.</summary>
    private bool _mobTest;
    /// <summary>--broken-test : implique --mob-test, puis applique
    /// <c>SetBroken(true)</c> sur tous les mobs en vue — preuve
    /// visuelle du masquage KNOCK (B11) sans combat BP réel.</summary>
    private bool _brokenTest;
    /// <summary>--broken-restore : avec --broken-test, repasse
    /// <c>SetBroken(false)</c> après ~4 s — la capture finale montre
    /// le KNOCK restauré (cycle natif FUN_00771920).</summary>
    private bool _brokenRestore;
    /// <summary>--storage-test : pilote bout-en-bout banque + coffre
    /// famille — commandes GM (//transport_node 1 → banquier s001,
    /// //tele, //clone_item), TalkToNpc → 357/358, dépôt/retrait
    /// réels (60/61 + NCID 7 type 4), extension 69→373, coffre
    /// famille 179/177/178 (aucun NPC service 19 dans ce monde —
    /// preuve directe des opcodes). Capture via
    /// <c>--storage-shot=&lt;chemin&gt;</c>.</summary>
    private bool _storageTest;
    /// <summary>Chemin de capture de fin de <c>--storage-test</c>
    /// (vide = pas de capture).</summary>
    private string _storageShot = "";
    /// <summary>--trade-test[=init] : pilote QA d'échange J↔J —
    /// l'accepteur répond 37 à l'invite ; l'initiateur choisit le
    /// joueur le plus proche, émet 36 puis offre 1 or + 1 item et
    /// verrouille (41). Les deux verrouillent — le serveur exécute
    /// au double confirm (TradeService.ConfirmTradeAsync →
    /// ExecuteTradeAsync ; pas de 340 sur le C#).</summary>
    private bool _tradeTest;
    /// <summary>--repair-test : envoie 45 puis 47 (coûts de
    /// réparation) après l'entrée zone et logue les réponses
    /// 342/343 — preuve wire du flux forgeron sans NPC.</summary>
    private bool _repairTest;
    /// <summary>--buff-test : apprend le buff guerrier 50014 puis le
    /// lance sur soi → vérifie 299/302 + remplissage BuffBar.</summary>
    private bool _buffTest;
    /// <summary>--zone-cmd=&lt;text&gt; : envoie un ZoneTextCommand
    /// (« //… » ou texte brut) juste après l'entrée en zone — pilote
    /// générique pour les commandes GM en preuve live.</summary>
    private string? _zoneCmd;
    private bool _mailTest;
    private string? _mailTo;
    private bool _tradeTestInit;
    /// <summary>--vending-test[=buy] : pilote QA d'étal joueur —
    /// le vendeur se téléporte dans la région 600 de S001
    /// (//tele 247 396), clone un item et émet 77 ; l'acheteur se
    /// téléporte au même point, émet 72 puis 76 sur l'index 0.</summary>
    private bool _vendingTest;
    private bool _vendingBuyer;
    private bool _duelTest;
    private bool _duelAccept;
    private bool _pvpAttack;
    private bool _dailyTest;
    private bool _levelupTest;
    private bool _mallTest;
    /// <summary>--devspeed : ×5 sur MoveSpeed/RunSpeed du joueur pour
    /// les trajets de test. Le serveur tolère (position renégociée —
    /// MoveSelf déclaré à ~10 Hz comme d'habitude) ; à n'utiliser
    /// qu'en local.</summary>
    private bool _devSpeed;
    /// <summary>--route-test=ex,ey[;ex,ey…] : AutoRoute.ToEden vers
    /// ces points sol Eden (un toutes les 25 s) — diag auto-follow.</summary>
    private string? _routeTest;
    /// <summary>--tele=ex,ey : téléportation live façon --mob-test
    /// (set position + SnapToGround en gardant le Y courant + MoveSelf)
    /// — //tele serveur fait atterrir sur les canopées (snap pleine
    /// colonne), ce chemin garde le niveau de sol du joueur.</summary>
    private string? _teleDest;
    /// <summary>--chat-test : envoie un Say puis un whisper au premier
    /// autre joueur visible — preuve = écho 327 loggué [verify].</summary>
    private bool _chatTest;
    /// <summary>Captures PNG de fin de pilote (vides = pas de
    /// capture).</summary>
    private string _tradeShot = "", _vendingShot = "";

    public override void _Ready()
    {
        StartWatchdog();
        PlayerController.EnsureInputActions();
        _debugLabel = GetNodeOrNull<Label>("DebugUI/DebugLabel");
        // TextResolver (tables T_* EN) : résolution raw_assets puis
        // gamedata/ — les tables dérivées du dépôt (t_npc_extra.ini)
        // ne vivent que sous gamedata/ et ne sont pas des assets
        // Reforged modifiables.
        TextResolver.ResolveFile ??= rel =>
            AssetCatalog.ResolveRawAsset(rel) ??
            DataPaths.ResolveFile(rel);
        TextResolver.Log ??= m => GD.PrintErr(m);
        UiTextEn.ResolveFile ??= DataPaths.ResolveFile;
        LevelTable.ResolveFile ??= DataPaths.ResolveFile;

        // Args utilisateur : --live, --map=Sxxx, --host=, --account=,
        // --password=, --char-id=N, --world-id=N. Godot 4 sépare les
        // args utilisateur (après « -- ») dans GetCmdlineUserArgs —
        // on lit les deux sources.
        var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
            .ToArray();
        foreach (var arg in args)
        {
            if (arg == "--live") _live = true;
            else if (arg == "--solo") _live = false;
            else if (arg.StartsWith("--map=")) _mapCode = arg[6..];
            else if (arg.StartsWith("--host=")) _host = arg[7..];
            else if (arg.StartsWith("--account=")) _account = arg[10..];
            else if (arg.StartsWith("--password=")) _password = arg[11..];
            else if (arg.StartsWith("--char-id=") &&
                     uint.TryParse(arg[10..], out var cid)) _charId = cid;
            else if (arg.StartsWith("--world-id=") &&
                     short.TryParse(arg[11..], out var wid)) _worldId = wid;
            else if (arg == "--nohud") _noHud = true;
            else if (arg == "--debug") _debug = true;
            else if (arg == "--talk-test") _talkTest = true;
            else if (arg == "--flow-test") _flowTest = true;
            else if (arg == "--service-test") _serviceTest = true;
            else if (arg.StartsWith("--quest-test=") &&
                     int.TryParse(arg[13..], out var qn)) _questNpc = qn;
            else if (arg == "--mob-test") _mobTest = true;
            else if (arg == "--broken-test")
            { _brokenTest = true; _mobTest = true; }
            else if (arg == "--broken-restore")
            { _brokenRestore = true; _brokenTest = true; _mobTest = true; }
            else if (arg == "--storage-test") _storageTest = true;
            else if (arg.StartsWith("--storage-shot="))
                _storageShot = arg[15..];
            else if (arg == "--trade-test") _tradeTest = true;
            else if (arg == "--repair-test") _repairTest = true;
            else if (arg == "--buff-test") _buffTest = true;
            else if (arg.StartsWith("--zone-cmd="))
                _zoneCmd = arg.Substring("--zone-cmd=".Length);
            else if (arg == "--mail-test") _mailTest = true;
            else if (arg.StartsWith("--mail-test="))
            { _mailTest = true; _mailTo = arg[12..]; }
            else if (arg == "--trade-test=init")
            { _tradeTest = true; _tradeTestInit = true; }
            else if (arg == "--vending-test") _vendingTest = true;
            else if (arg == "--mall-test") _mallTest = true;
            else if (arg == "--vending-test=buy")
            { _vendingTest = true; _vendingBuyer = true; }
            else if (arg == "--duel-test") _duelTest = true;
            else if (arg == "--duel-test=accept")
            { _duelTest = true; _duelAccept = true; }
            else if (arg == "--pvp-attack") _pvpAttack = true;
            else if (arg == "--daily-test") _dailyTest = true;
            else if (arg == "--levelup-test") _levelupTest = true;
            else if (arg == "--chat-test") _chatTest = true;
            else if (arg == "--cinema-test") _cinemaTest = true;
            else if (arg.StartsWith("--trade-shot="))
                _tradeShot = arg[13..];
            else if (arg.StartsWith("--vending-shot="))
                _vendingShot = arg[15..];
            else if (arg == "--devspeed") _devSpeed = true;
            else if (arg.StartsWith("--route-test="))
                _routeTest = arg["--route-test=".Length..];
            else if (arg.StartsWith("--tele="))
                _teleDest = arg["--tele=".Length..];
        }

        // Texte debug (fps/pos/aide) masqué par défaut — visible avec
        // --debug ou la touche F12 (toggle en jeu).
        if (GetNodeOrNull<CanvasLayer>("DebugUI") is { } debugUi)
            debugUi.Visible = _debug;

        // Chemin normal du jeu : la session arrive attachée depuis le
        // flux d'écrans (boot → login → channel → sélection perso) via
        // <see cref="SessionCarry"/> — on ne refait PAS le login.
        if (SessionCarry.Session is { } carry)
        {
            _live = true;
            _attached = true;
            _session = carry;
            // Binds elf posés avant ZoneEntryAsync : sinon le burst
            // d'entrée (412/424) arrive pendant le chargement HUD et
            // part en « non géré » sans atteindre le livre.
            Hud.ElfBridge.EnsureBound(carry);
            Hud.MissionBridge.CaptureEarly(carry);
            // Idem pour les enchants : les 299 d'entrée (enchants
            // restaurés, état portail débutant) arrivent pendant le
            // chargement CEGUI — sans bind précoce ils sont jetés.
            Hud.BuffBridge.EnsureBound(carry);
            var ch = SessionCarry.Character;
            if (ch != null)
            {
                _charId = (uint)ch.Id;
                // Map initiale = node du perso (pas la constante S009).
                _mapCode = World.NodeGeometry.Resolve(ch.NodeId, MapExists) ?? $"S{ch.NodeId:D3}";
            }
        }

        // Map inexistante (données factices, node inconnu…) → repli
        // sur la map par défaut plutôt qu'un monde vide.
        if (!MapExists(_mapCode) && _mapCode.Length == 4 && _mapCode[0] is 'S' or 's'
            && int.TryParse(_mapCode[1..], out var nid)
            && World.NodeGeometry.Resolve(nid, MapExists) is { } donor)
        {
            GD.Print($"[GameScene] node {nid} → géométrie {donor}");
            _mapCode = donor;
        }
        if (!MapExists(_mapCode))
        {
            GD.PrintErr($"[GameScene] map {_mapCode} introuvable — " +
                        $"repli sur {DefaultMap}");
            _mapCode = DefaultMap;
        }

        Ui.NativeCursor.Apply();
        Screens.LoadingScreen.Report(0.50f, "Chargement de la zone…");
        _cliArgs = args;
        // Flux écrans : un LoadingScreen recouvre la scène → charge le
        // monde en tâche de fond (barre animée, phases réelles). Mode
        // solo/debug (--solo/--live direct) : pas d'écran mais MÊME
        // chemin async — LoadMap await des continuations qui repostent
        // sur le thread principal : synchrone = deadlock garanti.
        LoadWorldAsync();           // fire-and-forget (async void)
    }

    /// <summary>Args CLI conservés pour la suite du chargement
    /// (--huddebug lu dans <see cref="LoadWorldAsync"/>).</summary>
    private string[] _cliArgs = [];

    /// <summary>Chargement du monde en tâche de fond (lot « écran de
    /// chargement fluide ») : <see cref="MapLoader.LoadMap"/> ne touche
    /// jamais le SceneTree — il ne fait que construire une hiérarchie
    /// détachée (même garantie que le pré-chargement parallèle des
    /// protos GLB du lot G1) — il peut donc tourner sur un thread de
    /// fond pendant que l'écran de chargement continue de s'animer et
    /// de refléter les phases réelles (Terrain → Objets → Personnages
    /// → Personnage → Entrée). La continuation de l'<c>await</c>
    /// revient sur le thread principal (SyncContext Godot) : AddChild,
    /// joueur, HUD et StartLive s'y exécutent.</summary>
    /// <summary>Chargement du monde (toujours asynchrone, avec ou sans
    /// écran de chargement).</summary>

    /// <summary>Diag : localise les quaternions dénormalisés en scène
    /// (spam « The end quaternion must be normalized » —
    /// interpolate_via_rest). Scanne tracks Rotation3D de tous les
    /// AnimationPlayers + rests/poses des Skeleton3D + quats des
    /// Node3D et imprime les coupables par nom.</summary>
    private static void QuatAudit(Node root)
    {
        static bool Bad(Quaternion q) =>
            !float.IsFinite(q.X) || !float.IsFinite(q.Y) ||
            !float.IsFinite(q.Z) || !float.IsFinite(q.W) ||
            Mathf.Abs(q.LengthSquared() - 1f) > 1e-3f;
        var anims = 0; var rests = 0; var poses = 0; var nodes = 0;
        foreach (var p in root.FindChildren("*", "AnimationPlayer",
                true, false).Cast<AnimationPlayer>())
        {
            foreach (var ln in p.GetAnimationLibraryList())
                foreach (var an in p.GetAnimationLibrary(ln)
                        .GetAnimationList())
                {
                    var a = p.GetAnimationLibrary(ln).GetAnimation(an);
                    for (var t = 0; t < a.GetTrackCount(); t++)
                    {
                        if (a.TrackGetType(t) != Animation.TrackType
                                .Rotation3D) continue;
                        for (var k = 0; k < a.TrackGetKeyCount(t); k++)
                        {
                            var v = a.TrackGetKeyValue(t, k);
                            if (v.VariantType == Variant.Type.Quaternion
                                && Bad((Quaternion)v))
                            {
                                anims++;
                                if (anims < 6)
                                    GD.PrintErr($"[quat] anim " +
                                        $"'{p.GetPath()}'/{an} t{t} " +
                                        $"k{k} = {(Quaternion)v}");
                            }
                        }
                    }
                }
        }
        foreach (var sk in root.FindChildren("*", "Skeleton3D",
                true, false).Cast<Skeleton3D>())
        {
            for (var b = 0; b < sk.GetBoneCount(); b++)
            {
                if (Bad(sk.GetBoneRest(b).Basis
                        .GetRotationQuaternion()))
                {
                    rests++;
                    if (rests < 6)
                        GD.PrintErr($"[quat] boneRest '{sk.GetPath()}'" +
                            $" bone={b} ({sk.GetBoneName(b)})");
                }
                if (Bad(sk.GetBonePose(b).Basis
                        .GetRotationQuaternion()))
                {
                    poses++;
                    if (poses < 6)
                        GD.PrintErr($"[quat] bonePose '{sk.GetPath()}'" +
                            $" bone={b} ({sk.GetBoneName(b)})");
                }
            }
        }
        foreach (var n in root.FindChildren("*", "Node3D",
                true, false).Cast<Node3D>())
        {
            if (Bad(n.Quaternion))
            {
                nodes++;
                if (nodes < 6)
                    GD.PrintErr($"[quat] node '{n.GetPath()}'");
            }
        }
        GD.PrintErr($"[quat-audit] animKeys={anims} boneRests={rests}" +
            $" bonePoses={poses} nodes={nodes}");
    }

    private async void LoadWorldAsync()
    {
        var tLoad = Perf.PerfTrace.Mark();
        var loader = new MapLoader { SpawnSceneNpcs = !_live };
        loader.LoadProgress += (f, s) =>
            Screens.LoadingScreen.Report(f, s);
        try
        {
            Node3D root;
            if (DisplayServer.GetName() == "headless" ||
                OS.GetCmdlineArgs().Contains("--headless"))
            {
                root = loader.LoadMap(_mapCode);
            }
            else
            {
                root = await Task.Run(() => loader.LoadMap(_mapCode));
            }
            _map = loader;
            _mapRoot = root;
            AddChild(root);
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            GD.PrintErr($"[GameScene] Échec LoadMap({_mapCode}) : {ex}");
        }
        Perf.PerfTrace.Report($"loadmap {_mapCode}", tLoad);
        FinishWorldLoad();
    }

    /// <summary>--strip-colliders : QueueFree tous les
    /// StaticBody3D/CollisionShape3D du monde — bisect perf (le sol
    /// devient traversable, test only).</summary>
    private static void StripColliders(Node root)
    {
        var bodies = new Stack<Node>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is StaticBody3D or CollisionShape3D)
                bodies.Push(n);
            else
                foreach (var c in n.GetChildren()) stack.Push(c);
        }
        while (bodies.Count > 0) bodies.Pop().QueueFree();
    }

    /// <summary>Étapes post-chargement (thread principal) : GC, probe,
    /// joueur + visuel, HUD, mode live.</summary>
    private void FinishWorldLoad()
    {
        EdenClient.Perf.PerfTrace.Abs("game : loadmap fini");
        // Lot G1 / P0.4 : collecte légère sous l'écran de chargement
        // (jamais compactante en cours de jeu).
        var tGc = Perf.PerfTrace.Mark();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized,
            blocking: false, compacting: false);
        Perf.PerfTrace.Report("gc post-load", tGc);
        // Lot 1.4 : pré-chauffe les tables INI lourdes (c_spell, t_item,
        // talents, icônes de classes) en tâche de fond pendant la fin du
        // chargement — premier open du panneau Classes sans hoquet.
        _ = Task.Run(Data.DataWarmup.Run);
        if (Perf.PerfProbe.Enabled)
            AddChild(new Perf.PerfProbe { Name = "PerfProbe" });
        // Diagnostic perf : --strip=Objects,Entities,MapModel —
        // QueueFree les sous-arbres nommés du root de map pour bisecter
        // le coût frame par catégorie de scène.
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--strip=") && _mapRoot != null)
                foreach (var name in a[8..].Split(','))
                    _mapRoot.GetNodeOrNull<Node3D>(name.Trim())
                        ?.QueueFree();
            else if (a == "--strip-colliders" && _mapRoot != null)
                StripColliders(_mapRoot);
        Screens.LoadingScreen.Report(0.96f, "Personnage…");

        var t = EdenClient.Perf.PerfTrace.Mark();
        _player = new PlayerController { Name = "Player" };
        HookPlayerGround();
        if (_map != null)
        {
            var sp = _map.GetSpawnPoint();
            // Le Z des REVIVE/GATEWAY de scene ini n'est pas fiable
            // (0 ou −15 sous la map) : on snappe systématiquement sur
            // la hauteur terrain, sinon le joueur+caméra naissent sous
            // le monde → écran noir.
            _player.Position = SnapToGround(
                WorldConv.ToGodot(sp.X, sp.Y, sp.Z));
        }
        AddChild(_player);
        if (_devSpeed)
        {
            _player.MoveSpeed *= 5f;
            _player.RunSpeed *= 5f;
            GD.Print("[dev] vitesse ×5 (MoveSpeed=" +
                $"{_player.MoveSpeed} RunSpeed={_player.RunSpeed})");
        }
        // Visuel du perso : GLB corps (code corps du blob apparence →
        // char/model/<L>.glb, switch natif FUN_0073ddc0) ; capsule si
        // le modèle est absent.
        if (_attached && SessionCarry.Character is { } carryChar)
        {
            var (lcls, lsty) = CharVisual.LocalClass();
            var body = CharVisual.BuildBody(carryChar.Appearance, classId: lcls, style: lsty);
            if (body != null)
            {
                _player.SetVisual(body);
                CharVisual.PlayIdle(body);
            }
        }
        EdenClient.Perf.PerfTrace.Report("player+visual", t);
        if (OS.GetCmdlineUserArgs().Contains("--black-scan"))
            GetTree().CreateTimer(10).Timeout += () => EdenClient.Debugging.RenderAudit.BlackScan(this);
        foreach (var pa in OS.GetCmdlineUserArgs())
            if (pa.StartsWith("--pick-px="))
            {
                var xy = pa[10..].Split(',');
                var pxv = new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                GetTree().CreateTimer(10).Timeout += () => EdenClient.Debugging.RenderAudit.PickPixel(this, pxv);
            }
        if (OS.GetCmdlineUserArgs().Contains("--lum-scan"))
            for (var li = 0; li < 8; li++)
            {
                var idx = li;
                GetTree().CreateTimer(LumStart() + li * 0.5).Timeout += () =>
                {
                    GetViewport().GetTexture().GetImage().SavePng($"/tmp/lum_{idx}.png");
                    EdenClient.Debugging.RenderAudit.BigScan(this, $"t{idx}");
                    EdenClient.Debugging.RenderAudit.RideDir(this, $"t{idx}");
                    if (idx == 4) EdenClient.Debugging.RenderAudit.PlayerMats(this);
                };
            }
        if (OS.GetCmdlineUserArgs().Contains("--mesh-dump"))
            GetTree().CreateTimer(8).Timeout += () => EdenClient.Game.CharVisual.DumpVisible(_player!, "player");
        if (OS.GetCmdlineUserArgs().Contains("--black-audit"))
            GetTree().CreateTimer(12).Timeout += () => EdenClient.Game.CharVisual.AuditBlack(_player!, "player");
        GD.Print($"[GameScene] map={_mapCode} spawn={_player.Position} " +
                 $"live={_live}");

        if (!_noHud && _cliArgs.Contains("--huddebug"))
            _hudDebugFrames = 300;

        t = EdenClient.Perf.PerfTrace.Mark();
        if (!_noHud)
            MountHud();
        EdenClient.Perf.PerfTrace.Report("hud mount", t);

        if (_live)
            StartLive();
        if (_cliArgs.Contains("--quat-audit"))
            QuatAudit(this);
        EdenClient.Perf.PerfTrace.Abs("game : prêt (attente entrée zone)");
    }

    // ------------------------------------------------------------------
    // HUD — scenes/hud.tscn montée en solo (démo, valeurs factices)
    // comme en live (valeurs réelles via les NCIDs dispatchés).
    // ------------------------------------------------------------------

    /// <summary>Instancie le CanvasLayer HUD et branche le chat sur la
    /// session zone (<see cref="ZoneChannelMsg"/>, NCID 34).</summary>
    private void MountHud()
    {
        var scene = GD.Load<PackedScene>("res://scenes/hud.tscn");
        _hud = scene.Instantiate<Hud.HudRoot>();
        _hud.SendChatAsync = SendChatAsync;
        _hud.SendZoneAsync = SendZone;
        _hud.HudReady += PushHudState;
        _hud.HudReady += () => _hudReadyAt = DateTime.UtcNow;
        AddChild(_hud);
        // Mode cinéma (letterbox + verrou d'entrée, plan 02) — inerte
        // tant qu'aucune scène n'est lancée.
        _cinema = new CinemaMode { Name = "Cinema" };
        AddChild(_cinema);
        if (_cinemaTest) _cinema.Begin(6);
        // Données déjà connues avant la fin du chargement CEGUI async.
        PushHudState();
    }

    private CinemaMode? _cinema;
    private bool _cinemaTest;

    /// <summary>Diag --huddebug : liste les Controls visibles du HUD
    /// couvrant ≥ 30 % du viewport — suspects d'occultation du 3D.</summary>
    private void DumpHudCoverage()
    {
        if (_hud == null) { GD.Print("[huddebug] pas de hud"); return; }
        var vp = GetViewport().GetVisibleRect().Size;
        foreach (var top in _hud.GetChildren())
        {
            if (top is Control tc)
            {
                var r = tc.GetGlobalRect();
                GD.Print($"[huddebug] TOP {tc.Name} vis={tc.IsVisibleInTree()} " +
                    $"rect={r} mod={tc.Modulate}");
            }
        }
        var stack = new Stack<Node>();
        stack.Push(_hud);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is Control c && c.IsVisibleInTree())
            {
                var r = c.GetGlobalRect();
                var cover = r.Size.X / Math.Max(vp.X, 1) *
                            (r.Size.Y / Math.Max(vp.Y, 1));
                if (cover >= 0.8f)
                {
                    var sb = "";
                    if (c is Panel p2)
                        sb = $" panelSB={p2.GetThemeStylebox("panel")?.GetType().Name}";
                    if (c is TextureRect tr2)
                        sb = $" tex={(tr2.Texture == null ? "null" : "set")} stretch={tr2.StretchMode} mod={tr2.Modulate}";
                    GD.Print($"[huddebug] cover={cover:P0} " +
                        $"rect={r} type={c.GetType().Name} " +
                        $"name={c.Name} cegui={c.GetMeta("cegui_type", "")} " +
                        $"path={c.GetPath()}{sb}");
                }
            }
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
        GD.Print("[huddebug] fin dump");
    }

    /// <summary>Repousse l'état courant dans le HUD (appelé à sa
    /// construction effective — chargement CEGUI asynchrone).</summary>
    private void PushHudState()
    {
        if (_hud == null) return;
        // Nom de zone même en solo (bandeau « S002 » → « Limestone
        // Mtn. » comme le natif) — _zoneLabel n'est rempli qu'en live.
        if (_zoneLabel.Length == 0 && _mapCode.Length > 1 &&
            int.TryParse(_mapCode[1..], out var zid))
            _zoneLabel = Data.TextResolver.NodeName(zid) ?? "";
        _hud.SetZone(_mapCode, _zoneLabel.Length > 0 ? _zoneLabel : null);
        PushWorldSize();
        if (_attached && SessionCarry.Character is { } ch)
        {
            _selfCharId = (uint)ch.Id;
            _hud.SetSelf((uint)ch.Id, ch.Name, ch.Level);
            _hud.SetRace(ch.RaceId);
            _player?.SetNameplate(ch.Name);
            _hud.Profile?.SetClass(
                Hud.ProfileWindow.ResolveClassName(ch.ClassId),
                ch.ClassLv);
            _hud.Vitals?.SetJobLv(ch.ClassLv);
            _hud.Vitals?.SetClassIcon(Screens.CharSelectScreen.ClassIcon(ch.ClassId));
            _hud.Hotbar?.SetClassIcon(Screens.CharSelectScreen.ClassIcon(ch.ClassId));
            _hud.Hotbar?.SetLevels(ch.Level, ch.ClassLv);
        }
        else
        {
            _hud.SetSelf(_selfCharId,
                _selfCharId != 0 ? _selfCharId.ToString() : "Solo", 0);
        }
        _hud.SetVitals(_hp, _maxHp, _mp, _maxMp);
        // Portrait 3D du bloc vitaux — duplicata du visuel joueur.
        if (_player != null)
        {
            _hud.Vitals?.SetPortrait(_player.Visual);
            _hud.Profile?.SetPreview(_player.Visual);
            _player.SetNameplateHp(_hp, _maxHp);
        }
    }

    /// <summary>Envoie un message zone, erreurs loguées (pattern
    /// partagé par les helpers HUD).</summary>
    private Task SendZone(NetMessage msg)
    {
        if (_session?.Zone?.IsConnected != true)
            return Task.CompletedTask;
        var send = _session.SendZoneAsync(msg, _liveCts.Token);
        return send.ContinueWith(t => { _ = t.Exception; },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ---- API pour le HUD (fenêtres dialogue/shop/transport/banque) —
    // toutes thread principal, fire-and-forget vers la session zone.

    /// <summary>Répond à un dialogue NPC : NCID 11 ReplyDialog
    /// [s16 index] — index dans <see cref="DialogueState.Options"/>.</summary>
    public Task SendDialogReplyAsync(int optionIndex) =>
        SendZone(new ZoneReplyDialog { DialogId = (short)optionIndex });

    /// <summary>Achat boutique : NCID 22 BuyItems
    /// [u16 storeSlot][u16 count][u8 buyback].</summary>
    public Task BuyShopItemAsync(int storeSlot, int count,
        bool buyBack = false) => SendZone(new ZoneBuyItems
        {
            ShopIndex = (ushort)storeSlot,
            ItemIndex = (ushort)count,
            BuyBack = (byte)(buyBack ? 1 : 0),
        });

    /// <summary>Vente d'un slot inventaire : NCID 23 SellItems
    /// [i32 1][u16 slot][u16 count][u16 container].</summary>
    public Task SellItemAsync(int container, int slot, int count)
    {
        var m = new ZoneSellItems();
        m.Items.Add(((ushort)slot, (ushort)count, (ushort)container));
        return SendZone(m);
    }

    /// <summary>Fermeture boutique : NCID 63 LeaveShop (vide).</summary>
    public Task LeaveShopAsync()
    {
        ShopState.Close();
        return SendZone(ZoneEmptyRequest.LeaveShop);
    }

    /// <summary>Choix d'une route transport : NCID 88 [s16] — id route
    /// ou index dans <see cref="TransportState.Routes"/> (le serveur
    /// accepte les deux).</summary>
    public Task SelectTransportAsync(int routeIdOrIndex)
    {
        TransportState.Close();
        return SendZone(new ZoneTransport { Value = (short)routeIdOrIndex });
    }

    /// <summary>Demande de revive : NCID 20 [i32 type].</summary>
    public Task SendReviveAsync(int reviveType = 0) =>
        SendZone(new ZoneReviveRequest { ReviveType = reviveType });

    /// <summary>Demande le contenu banque : NCID 62 [s16 -1].</summary>
    public Task QueryStorageAsync() =>
        SendZone(new ZoneGetStorageData { Value = -1 });

    /// <summary>Franchit un portail : NCID 15 CheckNodeExit
    /// [s16 nodeId][s16 portalId].</summary>
    public Task SendCheckNodeExitAsync(short nodeId, short portalId) =>
        SendZone(new ZoneCheckNodeExit { NodeId = nodeId, PortalId = portalId });

    /// <summary>Signale un changement d'équipement : NCID 8
    /// [u32 locMask] — réponse serveur 282+283.</summary>
    public Task SendUpdateEquipmentAsync(uint locMask) =>
        SendZone(new ZoneUpdateEquipment { UpdateFlags = locMask });

    /// <summary>Envoi chat live — wire natif NCID 34
    /// (ZoneEntryFlow.Chat.cs serveur). Les textes préfixés
    /// <c>//</c> empruntent le chemin natif des commandes GM —
    /// NCID 3 TextCommand (gate privilège serveur, désasm Execute
    /// @0x8295120) et non le canal chat : //tele, //transport_node…
    /// </summary>
    private Task SendChatAsync(byte channel, string target, string text)
    {
        if (_session?.Zone?.IsConnected != true)
        {
            _hud?.Chat?.AddLocalEcho(channel, text);
            return Task.CompletedTask;
        }
        if (text.StartsWith("//"))
        {
            var cmd = _session.SendZoneAsync(new ZoneTextCommand
            { Text = text }, _liveCts.Token);
            return cmd.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }
        var send = _session.SendZoneAsync(new ZoneChannelMsg
        {
            Channel = channel, Target = target, Message = text,
        }, _liveCts.Token);
        return send.ContinueWith(t => { _ = t.Exception; },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------
    // Mode live : session réseau → entités + MoveSelf
    // ------------------------------------------------------------------

    /// <summary>Lance la séquence d'entrée en zone en tâche de fond et
    /// enregistre les handlers NCID (toutes les mutations de scène
    /// passent par <see cref="_pending"/>).</summary>
    private void StartLive()
    {
        if (!_attached)
        {
            // Options transport (--live direct, pas de flux d'écrans) :
            // client.cfg (transport/tls_thumbprint/tls_insecure) puis
            // overrides CLI --transport=/--tls-thumbprint=/--tls-insecure.
            var cfg = ClientConfig.Load();
            var transport = cfg.Transport;
            var thumbprint = cfg.TlsThumbprint;
            var insecure = cfg.TlsInsecure;
            foreach (var a in _cliArgs)
            {
                if (a.StartsWith("--transport=")) transport = a[12..];
                else if (a.StartsWith("--tls-thumbprint="))
                    thumbprint = a[17..];
                else if (a == "--tls-insecure") insecure = true;
            }
            _session = new EdenSession(new EdenSessionOptions
            {
                Host = _host,
                Account = _account,
                Password = _password,
                CharId = _charId,
                WorldId = _worldId,
                Transport = transport,
                TlsThumbprint = thumbprint,
                TlsInsecure = insecure,
            });
            SessionCarry.Session = _session;
        }
        _netStatus = "connexion zone…";
        Hud.ElfBridge.EnsureBound(_session!);
        Hud.MissionBridge.CaptureEarly(_session!);
        Hud.BuffBridge.EnsureBound(_session!);
        _session!.Log += m =>
            _pending.Enqueue(() => GD.Print($"[live] {m}"));
        _session.Disconnected += reason => _pending.Enqueue(() =>
        {
            if (!_attached) return;
            GD.PrintErr($"[live] déconnexion : {reason} → retour login");
            SessionCarry.Session = null;
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                "res://scenes/boot.tscn");
        });
        _session.Frame += (sent, ncid, payload) =>
        {
            // Trace wire → console (format zsniff-like ; dump hex ≤ 96B).
            var hex = Convert.ToHexString(payload).ToLowerInvariant();
            if (hex.Length > 160) hex = hex[..160] + "…";
            GD.Print($"[wire] {(sent ? 'D' : 'E')} NCID={ncid} " +
                     $"len={payload.Length} {hex}");
        };

        _registry = new EntityRegistry { Name = "NetEntities" };
        _registry.GroundHeight = (gx, gz) =>
            _map != null && GodotObject.IsInstanceValid(_map) &&
            _map.TryGetGodotHeight(new Vector3(gx, 0f, gz), out var y)
                ? y : (float?)null;
        _registry.ResolveMonster = ResolveMonster;
        _registry.ResolveNpc = ResolveNpc;
        // Les noms envoyés par le serveur (DisplayName) sont en Big5
        // — le hook post-spawn les remplace par les tables T_* EN
        // sans toucher scripts/world/ (E1).
        _registry.Spawned += RenameEntityEnglish;
        // 2.5 : couleur des noms de mobs selon le danger (rouge /
        // jaune clair / gris) — recolorée au changement de niveau.
        World.MobDanger.Lookup = MonsterDef;
        World.MobDanger.LevelChanged += () =>
        {
            if (_registry == null || !IsInstanceValid(_registry)) return;
            foreach (var en in _registry.Entities.Values)
                if (IsInstanceValid(en) && en.Kind == 12)
                    en.RefreshLabelColor();
        };
        AddChild(_registry);

        RegisterZoneDispatch();

        Task.Run(async () =>
        {
            try
            {
                if (_attached)
                {
                    // Session déjà à l'étape « ticket zone obtenu » ;
                    // l'écran de chargement a pu faire le handshake
                    // zone — on ne le refait pas si déjà connecté.
                    if (_session!.Zone?.IsConnected != true)
                        await _session.ZoneConnectAsync(_liveCts.Token);
                    var te = EdenClient.Perf.PerfTrace.Mark();
                    await _session.ZoneEntryAsync(_liveCts.Token);
                    _pending.Enqueue(() =>
                    {
                        EdenClient.Perf.PerfTrace.Report("ZoneEntry", te);
                        EdenClient.Perf.PerfTrace.Abs("game : EN JEU");
                        Screens.LoadingScreen.Report(1f, "Entering…");
                        Screens.LoadingScreen.Finish();
                    });
                }
                else
                {
                    await _session!.EnterZoneAsync(
                        host: null, account: null, password: null,
                        charId: _charId, worldId: _worldId,
                        ct: _liveCts.Token);
                    _pending.Enqueue(EnsureSelfVisual);
                }
                _pending.Enqueue(() => _netStatus =
                    $"en zone char#{_session.SelectedCharId}");
                if (_talkTest) _ = RunTalkTestAsync();
                if (_serviceTest) _ = RunServiceTestAsync();
                if (_questNpc > 0) _ = RunQuestTestAsync(_questNpc);
                if (_mobTest) _ = RunMobTestAsync();
                if (_storageTest) _ = RunStorageTestAsync();
                if (_tradeTest) _ = RunTradeTestAsync();
                if (_repairTest) _ = RunRepairTestAsync();
                if (_buffTest) _ = RunBuffTestAsync();
                if (_zoneCmd != null) _ = RunZoneCmdAsync(_zoneCmd);
                if (_routeTest != null) _ = RunRouteTestAsync(_routeTest);
                if (_teleDest != null) _ = RunTeleTestAsync(_teleDest);
                if (_mailTest) _ = RunMailTestAsync();
                if (_vendingTest) _ = RunVendingTestAsync();
                if (_duelTest) _ = RunDuelTestAsync();
                if (_dailyTest) _ = RunDailyTestAsync();
                if (_levelupTest) _ = RunLevelupTestAsync();
                if (_mallTest) _ = RunMallTestAsync();
                if (_chatTest) _ = RunChatTestAsync();
                // Pompe monde unique : la connexion :5200 reste ouverte
                // en jeu (push social/guilde + item mall 62/63/64/79).
                // Tous les consommateurs passent par OnWorld — jamais
                // de second lecteur (ils se voleraient les trames).
                if (_session.World?.IsConnected == true)
                    _ = Task.Run(() =>
                        _session.WorldListenLoopAsync(_liveCts.Token));
                await _session.ZoneListenLoopAsync(_liveCts.Token);
            }
            catch (Exception ex)
            {
                _pending.Enqueue(() =>
                {
                    _netStatus = $"échec : {ex.Message}";
                    GD.PrintErr($"[live] {ex}");
                });
            }
        });
    }

    /// <summary>Flux --live (session créée dans la scène, pas par les
    /// écrans) : <see cref="SessionCarry.Character"/> et
    /// <see cref="_attached"/> n'étaient pas renseignés à
    /// <see cref="FinishWorldLoad"/> — le joueur restait une capsule
    /// sans animator (chant/blast invisibles en test live). Construit
    /// le corps natif une fois le perso sélectionné connu.</summary>
    private void EnsureSelfVisual()
    {
        if (_player == null || _player.Animator != null) return;
        SessionCarry.Character ??= _session?.Characters
            .FirstOrDefault(c => c.Id == _session.SelectedCharId);
        if (SessionCarry.Character is not { } ch) return;
        var (lcls, lsty) = CharVisual.LocalClass();
        var body = CharVisual.BuildBody(ch.Appearance,
            classId: lcls, style: lsty);
        if (body == null) return;
        _attached = true;
        _player.SetVisual(body);
        CharVisual.PlayIdle(body);
        _selfCharId = (uint)ch.Id;
        _hud?.SetSelf((uint)ch.Id, ch.Name, ch.Level);
        _hud?.SetRace(ch.RaceId);
        _player.SetNameplate(ch.Name);
        GD.Print($"[live] corps self construit «{ch.Name}» " +
            $"(animator={_player.Animator != null})");
    }

    /// <summary>--talk-test : attend les spawns, puis parcourt les
    /// NPC kind 8 proches — MoveSelf + TalkToNpc sur chacun — pour
    /// reproduire le flux « clic droit » en headless et valider en
    /// live les réponses 285 (dialogue), 393 (transport), 304 (shop),
    /// 357 (banque) selon le type de NPC.</summary>
    private async Task RunTalkTestAsync()
    {
        try
        {
            GD.Print("[talk-test] lancé — attente des spawns 298…");
            await Task.Delay(2500);             // laisse arriver les 298
            // La sélection lit GlobalPosition → thread principal
            // obligatoire (Godot interdit l'accès hors main thread).
            var npcs = await ListNpcsAsync(14);
            if (npcs.Count == 0)
            {
                GD.Print("[talk-test] aucun NPC en vue");
                return;
            }
            foreach (var (handle, npcId, name, pos, dist) in npcs)
            {
                var epos = WorldConv.ToEdenGround(pos);
                GD.Print($"[talk-test] NPC «{name}» h=0x{handle:x8} " +
                         $"id={npcId} à {dist:F0}u ({epos.X:F0},{epos.Y:F0})");
                // Marche jusqu'au NPC (MoveSelf = déclare la position
                // au serveur — la portée ≤8u est vérifiée côté zone).
                await _session!.SendZoneAsync(new ZoneMoveSelf
                {
                    Tail =
                    {
                        [0] = epos.X, [1] = epos.Y,
                        [2] = epos.X, [3] = epos.Y,
                        [4] = 0f,
                    },
                    MoveType = 7,
                }, _liveCts.Token);
                await Task.Delay(400);
                await _session.SendZoneAsync(new ZoneTalkToNpc
                {
                    NpcId = (int)handle,
                }, _liveCts.Token);
                GD.Print($"[talk-test] TalkToNpc émis " +
                         $"h=0x{handle:x8} ({name})");
                // Laisse le temps à la réponse (285/393/304/357) de
                // remonter dans les logs [live].
                await Task.Delay(1200);
            }
            GD.Print("[talk-test] terminé");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[talk-test] {ex}");
        }
    }

    /// <summary>--repair-test : prouve le flux forgeron sans NPC —
    /// envoie 45 (coût réparation totale) et logue la réponse 342 du
    /// serveur (reçue via <see cref="MiscPackets.Received"/>).</summary>
    private async Task RunRepairTestAsync()
    {
        try
        {
            await Task.Delay(2500);             // laisse arriver les 298
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var tcsOne = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnMsg(NetMessage m)
            {
                if (m is ZoneClientAskRepairEquipGold q)
                    tcs.TrySetResult(q.Value);
                else if (m is ZoneClientAskRepairOneItemGold qo)
                    tcsOne.TrySetResult(qo.Field3);
            }
            MiscPackets.Received += OnMsg;
            try
            {
                await _session!.SendZoneAsync(
                    new ZoneServerAskRepairEquipGold(), _liveCts.Token);
                GD.Print("[repair-test] 45 émis (coût total)");
                var done = await Task.WhenAny(
                    tcs.Task, Task.Delay(20000));
                GD.Print(done == tcs.Task
                    ? $"[repair-test] 342 reçu — coût={tcs.Task.Result} g"
                    : "[repair-test] TIMEOUT — pas de 342");
                // 47 → 343 : même round-trip sur UN item (le premier
                // à durabilité — arme/armure ; coût 0 si intact).
                var it = PlayerBag.Items.FirstOrDefault(
                    x => x.MaxDurability > 0);
                if (it == null)
                {
                    GD.PrintErr("[repair-test] aucun item à " +
                        "durabilité — 47 sauté");
                    return;
                }
                await _session.SendZoneAsync(
                    new ZoneServerAskRepairOneItemGold
                    {
                        Field1 = (byte)it.Bag,
                        Field3 = (short)it.Slot,
                    }, _liveCts.Token);
                GD.Print($"[repair-test] 47 émis (item " +
                    $"bag={it.Bag} slot={it.Slot})");
                done = await Task.WhenAny(
                    tcsOne.Task, Task.Delay(20000));
                GD.Print(done == tcsOne.Task
                    ? $"[repair-test] 343 reçu — coût={tcsOne.Task.Result} g"
                    : "[repair-test] TIMEOUT — pas de 343");
            }
            finally { MiscPackets.Received -= OnMsg; }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[repair-test] {ex}");
        }
    }

    /// <summary>--buff-test : prouve la chaîne enchant live —
    /// <c>//learn_spell 50014</c> (Declaration of Anger, buff
    /// guerrier auto-ciblé) puis cast-to-life sur le handle
    /// joueur ; la réponse 299/302 doit remplir
    /// <see cref="EntityBuffs"/> (donc la BuffBar).</summary>
    /// <summary>--zone-cmd : envoie le texte en ZoneTextCommand
    /// (NCID 3) une fois en zone — préfixe « // » ajouté si absent
    /// (le serveur tolère aussi le texte nu).</summary>
    private async Task RunRouteTestAsync(string spec)
    {
        await Task.Delay(9000);
        foreach (var pt in spec.Split(';'))
        {
            var c = pt.Split(',');
            if (c.Length < 2 ||
                !float.TryParse(c[0], System.Globalization.CultureInfo.InvariantCulture, out var ex) ||
                !float.TryParse(c[1], System.Globalization.CultureInfo.InvariantCulture, out var ey))
                continue;
            var tcs = new TaskCompletionSource();
            _pending.Enqueue(() =>
            {
                GD.Print($"[route-test] vers ({ex},{ey})");
                EdenClient.Hud.AutoRoute.ToEden(this, ex, ey);
                tcs.TrySetResult();
            });
            await tcs.Task;
            await Task.Delay(25000);
        }
    }

    private async Task RunZoneCmdAsync(string text)
    {
        try
        {
            await Task.Delay(1500);             // laisse finir l'entry
            foreach (var part in text.Split(';',
                StringSplitOptions.RemoveEmptyEntries))
            {
                // « sleep N » : pause de N secondes (attendre la fin
                // d'une migration //go avant le //tele suivant).
                if (part.TrimStart().StartsWith("sleep ") &&
                    int.TryParse(part.TrimStart()[6..], out var sl))
                {
                    await Task.Delay(sl * 1000);
                    continue;
                }
                var t = part.TrimStart().StartsWith("//")
                    ? part.TrimStart() : "//" + part.TrimStart();
                await _session!.SendZoneAsync(
                    new ZoneTextCommand { Text = t }, _liveCts.Token);
                GD.Print($"[zone-cmd] envoyé : {t}");
                await Task.Delay(400);          // laisse la réponse passer
            }
        }
        catch (Exception ex) { GD.PrintErr($"[zone-cmd] {ex}"); }
    }

    /// <summary>--tele=ex,ey[,z[,yaw[,delaySec]]] : téléporte le joueur
    /// aux coords sol Eden en gardant son altitude courante (ou le z
    /// optionnel en unités Godot) avant SnapToGround, puis applique le
    /// yaw optionnel (degrés Godot) pour viser la zone avant un
    /// --walk-test — contraire à //tele (serveur) qui atterrit sur la
    /// géométrie la plus haute — puis déclare la position via
    /// MoveSelf. delaySec (défaut 3 s) retarde le tir pour laisser
    /// passer une migration //transport_node.</summary>
    private async Task RunTeleTestAsync(string dest)
    {
        try
        {
            var parts = dest.Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            await Task.Delay(parts.Length > 4 &&
                float.TryParse(parts[4], inv, out var dl)
                    ? (int)(dl * 1000f) : 3000);
            if (parts.Length < 2 ||
                !float.TryParse(parts[0], inv, out var ex) ||
                !float.TryParse(parts[1], inv, out var ey))
            {
                GD.PrintErr($"[tele] coords illisibles : {dest}");
                return;
            }
            float? hintY = parts.Length > 2 &&
                float.TryParse(parts[2], inv, out var hy) ? hy : null;
            float? yaw = parts.Length > 3 &&
                float.TryParse(parts[3], inv, out var yw) ? yw : null;
            var tcs = new TaskCompletionSource<bool>();
            _pending.Enqueue(() =>
            {
                if (_player != null)
                {
                    var dst = WorldConv.ToGodot(ex, ey,
                        hintY ?? _player.GlobalPosition.Y);
                    _player.GlobalPosition = SnapToGround(dst);
                    if (yaw.HasValue)
                        _player.SetCamYawDeg(yaw.Value);
                    _lastSentPos = _player.GlobalPosition;
                }
                tcs.SetResult(true);
            });
            await tcs.Task;
            await _session!.SendZoneAsync(new ZoneMoveSelf
            {
                Tail =
                {
                    [0] = ex, [1] = ey,
                    [2] = ex, [3] = ey,
                    [4] = 0f,
                },
                MoveType = 7,
            }, _liveCts.Token);
            GD.Print($"[tele] MoveSelf vers ({ex:F1},{ey:F1})");
        }
        catch (Exception ex2) { GD.PrintErr($"[tele] {ex2}"); }
    }

    private async Task RunBuffTestAsync()
    {
        try
        {
            await Task.Delay(2500);             // laisse arriver les 298
            var self = (uint)(_session?.SelectedCharId ?? 0);
            if (self == 0)
            {
                GD.PrintErr("[buff-test] pas de charId");
                return;
            }
            // ZoneTextCommand direct (SendChatAsync renvoie une
            // continuation OnlyOnFaulted — l'awaiter jette
            // TaskCanceled quand l'envoi réussit).
            await _session!.SendZoneAsync(new ZoneTextCommand
            { Text = "//learn_spell 50014" }, _liveCts.Token);
            await Task.Delay(2000);             // 311 skills update
            await _session!.SendZoneAsync(new ZoneSpellCastToLife
            {
                SpellId = 50014, TargetId = self,
            }, _liveCts.Token);
            GD.Print($"[buff-test] cast 50014 → self 0x{self:x8}");
            for (var i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                // Le dictionnaire EntityBuffs mute sur le thread
                // principal (MiscPackets) → lecture postée dessus.
                var tcs = new TaskCompletionSource<int>();
                _pending.Enqueue(() => tcs.SetResult(
                    EntityBuffs.Snapshot((int)self).Count));
                // WhenAny : si _Process est bloqué le tour compte
                // comme 0 au lieu de pendre indéfiniment.
                await Task.WhenAny(tcs.Task, Task.Delay(1500));
                if (tcs.Task.IsCompletedSuccessfully &&
                    tcs.Task.Result > 0)
                {
                    GD.Print($"[buff-test] buff(s)={tcs.Task.Result} " +
                        "— 299/302 OK");
                    return;
                }
            }
            GD.Print("[buff-test] TIMEOUT — aucun buff après 15 s");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[buff-test] {ex}");
        }
    }

    /// <summary>Lit un état mutable HUD/main-thread via <c>_pending</c>,
    /// borné à 1,5 s — le drain peut stagner ~10 s en entrée de zone ;
    /// un tour non drainé compte comme « pas encore » (défaut).</summary>
    private async Task<T> OnMain<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue(() => tcs.TrySetResult(f()));
        await Task.WhenAny(tcs.Task, Task.Delay(1500));
        return tcs.Task.IsCompletedSuccessfully
            ? tcs.Task.Result : default!;
    }

    /// <summary>--mail-test : round-trip courrier J→J en solo —
    /// ouvre la boîte (95/7→399), envoie un mail à soi-même
    /// (99→403 + débit d'or 280) puis re-interroge (96→400) :
    /// le courrier envoyé doit apparaître dans la liste. Lectures de
    /// <see cref="MailState"/> postées sur le thread principal —
    /// les mutations passent par <c>_pending</c> qui peut stagner
    /// ~10 s en entrée de zone.</summary>
    private async Task RunMailTestAsync()
    {
        try
        {
            await Task.Delay(2500);
            if (string.IsNullOrEmpty(_selfName))
            {
                GD.PrintErr("[mail-test] pas de nom de perso");
                return;
            }
            await _session!.SendZoneAsync(new ZoneMailOpenBox { Op = 7 },
                _liveCts.Token);
            GD.Print("[mail-test] 95 op=7 émis (ouverture boîte)");
            for (var i = 0; i < 20 &&
                !await OnMain(() => MailState.IsOpen); i++)
                await Task.Delay(1000);
            if (!await OnMain(() => MailState.IsOpen))
            {
                GD.PrintErr("[mail-test] TIMEOUT — pas de 399");
                return;
            }
            GD.Print("[mail-test] 399 reçu — boîte ouverte");
            var subject = $"limitless-{DateTime.Now:HHmmss}";
            var to = _mailTo ?? _selfName;
            await _session.SendZoneAsync(new ZoneMailSend
            {
                To = to, Title = subject, Body = "x", Gold = 1,
            }, _liveCts.Token);
            GD.Print($"[mail-test] 99 émis → «{to}» sujet={subject}");
            for (var i = 0; i < 20 &&
                await OnMain(() => MailState.LastSendResult) < 0; i++)
                await Task.Delay(1000);
            var res = await OnMain(() => MailState.LastSendResult);
            if (res < 0)
            {
                GD.PrintErr("[mail-test] TIMEOUT — pas de 403");
                return;
            }
            GD.Print(res == 0
                ? "[mail-test] 403 reçu — envoi OK"
                : $"[mail-test] 403 reçu — ÉCHEC res={res}");
            if (res != 0) return;
            // Destinataire distant : le mail n'apparaît pas dans la
            // boîte de l'expéditeur — la réception se prouve côté
            // destinataire (HeadlessBot mail-check).
            if (_mailTo != null) return;
            await _session.SendZoneAsync(new ZoneMailQuery(),
                _liveCts.Token);
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(1000);
                var found = await OnMain(() => MailState.Mails
                    .Any(x => x.Subject == subject));
                if (found)
                {
                    GD.Print($"[mail-test] 400 reçu — mail «{subject}» " +
                        "dans la boîte — round-trip PJ OK");
                    return;
                }
            }
            GD.PrintErr("[mail-test] TIMEOUT — mail absent du 400");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[mail-test] {ex}");
        }
    }

    /// <summary>--service-test : parcourt les NPC déclarés dans la
    /// scène ([npc] de sNNN.ini — transporteur/banquier/artisan sont
    /// souvent hors du champ de vue initial) : téléporte le joueur à
    /// ~3 u, déclare la position (MoveSelf), attend le spawn 298 puis
    /// envoie TalkToNpc au kind-8 le plus proche. Les réponses
    /// (285/304/393/357/496) remontent dans les logs [verify].</summary>
    private async Task RunServiceTestAsync()
    {
        try
        {
            GD.Print("[service-test] lancé — attente des spawns 298…");
            await Task.Delay(2500);
            var sceneNpcs = _map?.Scene?.Npcs;
            if (sceneNpcs == null || sceneNpcs.Count == 0)
            {
                GD.Print("[service-test] pas de table npc de scène");
                return;
            }
            var seen = new HashSet<int>();
            var visited = 0;
            foreach (var n in sceneNpcs)
            {
                if (!seen.Add(n.NpcId) || visited >= 20) continue;
                visited++;
                // Téléportation : position joueur + MoveSelf (thread
                // principal pour le Node3D — même pattern que
                // ListNpcsAsync).
                var tcs = new TaskCompletionSource<bool>();
                _pending.Enqueue(() =>
                {
                    if (_player != null)
                    {
                        var dst = WorldConv.ToGodot(n.X + 2.5f, n.Y + 2.5f,
                            _player.GlobalPosition.Y);
                        _player.GlobalPosition = SnapToGround(dst);
                        _lastSentPos = _player.GlobalPosition;
                    }
                    tcs.SetResult(true);
                });
                await tcs.Task;
                var epos = (X: n.X + 2.5f, Y: n.Y + 2.5f);
                await _session!.SendZoneAsync(new ZoneMoveSelf
                {
                    Tail =
                    {
                        [0] = epos.X, [1] = epos.Y,
                        [2] = epos.X, [3] = epos.Y,
                        [4] = 0f,
                    },
                    MoveType = 7,
                }, _liveCts.Token);
                await Task.Delay(1600);   // spawn 298 du NPC
                var npcs = await ListNpcsAsync(1);
                var near = npcs.FirstOrDefault();
                // Le joueur a été téléporté à ~3,5 u : le kind-8 le
                // plus proche doit être le NPC visé (marge 15 u).
                if (near.Handle == 0 || near.Dist > 15f)
                {
                    GD.Print($"[service-test] npc #{n.NpcId} " +
                             $"({n.X:F0},{n.Y:F0}) — pas d'entité " +
                             "en vue");
                    continue;
                }
                GD.Print($"[service-test] NPC «{near.Name}» " +
                         $"h=0x{near.Handle:x8} (#{n.NpcId})");
                await _session!.SendZoneAsync(new ZoneTalkToNpc
                {
                    NpcId = (int)near.Handle,
                }, _liveCts.Token);
                await Task.Delay(1500);
            }
            GD.Print($"[service-test] terminé ({visited} npcs visités)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[service-test] {ex}");
        }
    }

    /// <summary>--quest-test=&lt;npcId&gt; : téléporte le joueur près du
    /// NPC de la scène, déclare la position et TalkToNpc — le flux
    /// quête (285 liste → 388 fiche → 87 op) est piloté par
    /// --flow-test ; ici on ne fait qu'amener le joueur au NPC.</summary>
    private async Task RunQuestTestAsync(int npcId)
    {
        try
        {
            GD.Print($"[quest-test] lancé — cible npc #{npcId}");
            // Attendre que le NPC apparaisse dans la scène — supporte
            // une migration //transport_node en parallèle (la liste est
            // repeuplée au chargement de la nouvelle map).
            SceneTable.NpcSpawn? n = null;
            for (var i = 0; i < 120 && n == null; i++)
            {
                await Task.Delay(500);
                n = _map?.Scene?.Npcs.FirstOrDefault(x => x.NpcId == npcId);
            }
            if (n == null)
            {
                GD.PrintErr($"[quest-test] npc #{npcId} absent de la scène");
                return;
            }
            var tcs = new TaskCompletionSource<bool>();
            _pending.Enqueue(() =>
            {
                if (_player != null)
                {
                    var dst = WorldConv.ToGodot(n.X + 2.5f, n.Y + 2.5f,
                        _player.GlobalPosition.Y);
                    _player.GlobalPosition = SnapToGround(dst);
                    _lastSentPos = _player.GlobalPosition;
                }
                tcs.SetResult(true);
            });
            await tcs.Task;
            var epos = (X: n.X + 2.5f, Y: n.Y + 2.5f);
            await _session!.SendZoneAsync(new ZoneMoveSelf
            {
                Tail =
                {
                    [0] = epos.X, [1] = epos.Y,
                    [2] = epos.X, [3] = epos.Y,
                    [4] = 0f,
                },
                MoveType = 7,
            }, _liveCts.Token);
            // Attendre le spawn 298 du NPC visé puis TalkToNpc — le
            // handle est le charId de vue, pas le npcId. Pas de cap à 6 :
            // la liste est triée par distance, en ville >6 NPC peuvent
            // être plus proches que la cible.
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(500);
                var npcs = await ListNpcsAsync(300);
                var near = npcs.FirstOrDefault(x => x.NpcId == npcId);
                if (near.Handle != 0 && near.Dist <= 15f)
                {
                    GD.Print($"[quest-test] NPC «{near.Name}» " +
                        $"h=0x{near.Handle:x8} à {near.Dist:F1}u — talk");
                    await _session!.SendZoneAsync(new ZoneTalkToNpc
                    { NpcId = (int)near.Handle }, _liveCts.Token);
                    return;
                }
            }
            var seen = await ListNpcsAsync(8);
            GD.PrintErr($"[quest-test] npc #{npcId} jamais en vue — " +
                $"proches: [{string.Join(", ",
                    seen.Select(s => $"{s.NpcId}@{s.Dist:F0}"))}]");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[quest-test] {ex}");
        }
    }

    /// <summary>--chat-test : envoie un Say (canal 0, écho 268) puis
    /// un whisper (canal 1, écho 327) au premier autre joueur en vue —
    /// les deux paquets retour sont loggués [verify].</summary>
    private async Task RunChatTestAsync()
    {
        try
        {
            GD.Print("[chat-test] lancé");
            await Task.Delay(3000);
            await _session!.SendZoneAsync(new ZoneChannelMsg
            {
                Channel = 0, Target = "",
                Message = "chat-test say",
            }, _liveCts.Token);
            await Task.Delay(1500);
            var ps = await ListPlayersAsync();
            var t = ps.FirstOrDefault();
            if (t.Handle != 0)
            {
                GD.Print($"[chat-test] whisper → «{t.Name}» " +
                         $"(0x{t.Handle:x8})");
                await _session.SendZoneAsync(new ZoneChannelMsg
                {
                    Channel = 1, Target = t.Name,
                    Message = "chat-test whisper",
                }, _liveCts.Token);
            }
            else
                GD.Print("[chat-test] aucun autre joueur en vue — " +
                         "whisper sauté");
            await Task.Delay(3000);
            GD.Print("[chat-test] terminé");
        }
        catch (Exception ex) { GD.PrintErr($"[chat-test] {ex}"); }
    }

    /// <summary>--storage-test : scénario banque + coffre famille —
    /// <list type="number">
    /// <item>//transport_node 1 (banquier 50346 de s001 ~247,405) ;</item>
    /// <item>//tele 250 402 → TalkToNpc du kind-8 le plus proche →
    /// 357/62/358 (binds existants — la fenêtre s'ouvre) ;</item>
    /// <item>dépôt réel 60 puis retrait 61 (count=1, stack sûr) ;</item>
    /// <item>extension 69 → 373 (+62 refresh automatique du bind) ;</item>
    /// <item>NCID 7 type 4 : dépôt drag (0→4), swap banque↔banque
    /// (4→4 sur 2 items clonés), retrait drag (4→0) ;</item>
    /// <item>coffre famille : journal 179→506, dépôt 177 (ligne
    /// entière) → push 505 + 275 sac, retrait 178 → 504 + 274 —
    /// aucun garde NPC côté serveur, test direct valide ;</item>
    /// <item>capture PNG finale via --storage-shot.</item>
    /// </list></summary>
    private async Task RunStorageTestAsync()
    {
        try
        {
            GD.Print("[storage-test] lancé — attente du spawn…");
            await Task.Delay(3000);
            // Donne un item sûr à déposer (38572 = consommable I00757).
            await _session!.SendZoneAsync(new ZoneTextCommand
            { Text = "//clone_item 38572" }, _liveCts.Token);
            await _session.SendZoneAsync(new ZoneTextCommand
            { Text = "//clone_item 38572" }, _liveCts.Token);
            await Task.Delay(1200);
            // Le banquier est-il déjà en vue ? (perso semé en DB sur
            // s001 près de 50346 — HandleToEntity n'est repeuplé
            // côté serveur qu'à l'entrée initiale, pas après une
            // migration //transport_node : TalkToNpc post-migration
            // renvoie « handle inconnu » — limite serveur constatée
            // live 2026-04-13.) Sinon migration de secours.
            var npcs = await WaitNpcsAsync(24, 8000);
            // Le banquier de S001 est le npcId 50346 (service type 6)
            // — des NPC décoratifs proches (S002 a aussi ses kind-8)
            // ne doivent pas faire sauter la migration : on migre
            // tant que 50346 n'est pas en vue.
            if (!npcs.Any(n => n.NpcId == 50346))
            {
                GD.Print("[storage-test] banquier 50346 absent — " +
                         "migration //transport_node 1 + //tele " +
                         "(TalkToNpc peut échouer : handle inconnu " +
                         "post-migration côté serveur)");
                await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//transport_node 1" }, _liveCts.Token);
                await Task.Delay(6000);
                await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//tele 250 402" }, _liveCts.Token);
                npcs = await WaitNpcsAsync(24, 15000);
            }
            foreach (var n in npcs)
                GD.Print($"[storage-test] npc «{n.Name}» " +
                         $"id={n.NpcId} h=0x{n.Handle:x8} à {n.Dist:F1}u");
            var banker = npcs.FirstOrDefault(n => n.NpcId == 50346);
            if (banker.Handle == 0)
            {
                GD.PrintErr("[storage-test] banquier 50346 toujours " +
                            "absent — abandon");
                return;
            }
            // Le talk est refusé au-delà de ~5u : s'approcher par les
            // coords de scène puis re-déclarer la position (même
            // recette que --quest-test).
            if (banker.Dist > 5f)
            {
                var sn = _map?.Scene?.Npcs.FirstOrDefault(
                    x => x.NpcId == 50346);
                if (sn != null)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _pending.Enqueue(() =>
                    {
                        if (_player != null)
                        {
                            var dst = WorldConv.ToGodot(sn.X + 2.5f,
                                sn.Y + 2.5f, _player.GlobalPosition.Y);
                            _player.GlobalPosition = SnapToGround(dst);
                            _lastSentPos = _player.GlobalPosition;
                        }
                        tcs.SetResult(true);
                    });
                    await tcs.Task;
                    var epos = (X: sn.X + 2.5f, Y: sn.Y + 2.5f);
                    await _session.SendZoneAsync(new ZoneMoveSelf
                    {
                        Tail =
                        {
                            [0] = epos.X, [1] = epos.Y,
                            [2] = epos.X, [3] = epos.Y,
                            [4] = 0f,
                        },
                        MoveType = 7,
                    }, _liveCts.Token);
                    for (var i = 0; i < 30; i++)
                    {
                        await Task.Delay(400);
                        banker = (await ListNpcsAsync(6))
                            .FirstOrDefault(n => n.NpcId == 50346);
                        if (banker.Handle != 0 && banker.Dist <= 5f)
                            break;
                    }
                    GD.Print($"[storage-test] approche : «{banker.Name}»" +
                             $" à {banker.Dist:F1}u");
                }
            }
            await _session.SendZoneAsync(new ZoneTalkToNpc
            { NpcId = (int)banker.Handle }, _liveCts.Token);
            GD.Print($"[storage-test] TalkToNpc «{banker.Name}» émis");
            // 357 → le bind demande 62 → 358 remplit StorageState.
            for (var i = 0; i < 30 && !StorageState.IsOpen; i++)
                await Task.Delay(300);
            if (!StorageState.IsOpen)
            {
                GD.PrintErr("[storage-test] 357 jamais reçu — stop");
                return;
            }
            for (var i = 0; i < 20 &&
                    StorageState.Containers.Count == 0; i++)
                await Task.Delay(300);
            GD.Print($"[verify] banque ouverte npc={StorageState.NpcUid} " +
                     $"onglets={StorageState.Containers.Count} " +
                     $"items={StorageState.TotalItems}");
            // ----- dépôt réel 60 / retrait 61 -----
            var it = PlayerBag.Items.FirstOrDefault(
                i2 => i2.ItemId == 38572) ?? PlayerBag.Items.FirstOrDefault();
            if (it != null)
            {
                var before = StorageState.TotalItems;
                GD.Print($"[storage-test] dépôt 60 item={it.ItemId} " +
                         $"sac={it.Bag} slot={it.Slot} x1");
                await _session.SendZoneAsync(new ZoneStorageDeposit
                {
                    Field1 = StorageState.NpcUid,
                    Field2 = (ushort)it.Bag,
                    Field3 = (short)it.Slot,
                    Field4 = 1,
                }, _liveCts.Token);
                await Task.Delay(1600);
                GD.Print($"[verify] banque après dépôt : " +
                         $"{before} → {StorageState.TotalItems} items");
            }
            var banked = StorageState.Containers
                .SelectMany(c => c.Items).FirstOrDefault();
            if (banked != null)
            {
                GD.Print($"[storage-test] retrait 61 loc={banked.Slot}");
                await _session.SendZoneAsync(new ZoneStorageWithdraw
                {
                    Field1 = StorageState.NpcUid,
                    Field2 = (ushort)banked.Bag,
                    Field3 = (short)banked.Slot,
                    Field4 = 1,
                }, _liveCts.Token);
                await Task.Delay(1600);
                GD.Print($"[verify] banque après retrait : " +
                         $"{StorageState.TotalItems} items");
            }
            // ----- extension 69 → 373 -----
            await _session.SendZoneAsync(new ZoneStorageExpand(),
                _liveCts.Token);
            GD.Print("[storage-test] extension 69 émise");
            await Task.Delay(1800);
            // ----- NCID 7 type 4 : dépôt/swap/retrait -----
            var a = PlayerBag.Items.FirstOrDefault(
                i2 => i2.ItemId == 38572) ?? PlayerBag.Items.FirstOrDefault();
            if (a != null)
            {
                await _session.SendZoneAsync(
                    ZoneBankActions.DepositRequest(a.Bag, a.Slot),
                    _liveCts.Token);
                GD.Print($"[storage-test] drag dépôt (0→4) sac{a.Bag} " +
                         $"slot{a.Slot}");
                await Task.Delay(1600);
            }
            var b = PlayerBag.Items.FirstOrDefault(
                i2 => i2.ItemId == 38572 &&
                      (i2.Bag != a?.Bag || i2.Slot != a?.Slot)) ??
                PlayerBag.Items.FirstOrDefault(i2 =>
                    i2.Bag != a?.Bag || i2.Slot != a?.Slot);
            if (b != null)
            {
                await _session.SendZoneAsync(
                    ZoneBankActions.DepositRequest(b.Bag, b.Slot),
                    _liveCts.Token);
                await Task.Delay(1600);
            }
            var bankItems = StorageState.Containers
                .SelectMany(c => c.Items).Take(2).ToList();
            if (bankItems.Count == 2)
            {
                await _session.SendZoneAsync(
                    ZoneBankActions.MoveRequest(bankItems[0].Slot,
                        bankItems[0].Bag, bankItems[1].Slot,
                        bankItems[1].Bag), _liveCts.Token);
                GD.Print($"[storage-test] swap 4→4 loc " +
                         $"{bankItems[0].Slot}↔{bankItems[1].Slot}");
                await Task.Delay(1600);
            }
            var wd = StorageState.Containers
                .SelectMany(c => c.Items).FirstOrDefault();
            if (wd != null)
            {
                await _session.SendZoneAsync(
                    ZoneBankActions.WithdrawRequest(wd.Slot, wd.Bag),
                    _liveCts.Token);
                GD.Print($"[storage-test] drag retrait (4→0) " +
                         $"loc={wd.Slot}");
                await Task.Delay(1600);
            }
            // ----- coffre famille (test direct — pas de NPC 19) -----
            await _session.SendZoneAsync(new ZoneFamilyStorageQueryLog(),
                _liveCts.Token);
            GD.Print("[storage-test] journal famille 179 émis");
            await Task.Delay(1500);
            GD.Print($"[verify] journal famille : " +
                     $"{FamilyStorageState.Log.Count} entrée(s)");
            var fit = PlayerBag.Items.FirstOrDefault(
                i2 => i2.ItemId == 38572) ?? PlayerBag.Items.FirstOrDefault();
            if (fit != null)
            {
                await _session.SendZoneAsync(new ZoneFamilyStoragePut
                {
                    StorageIndex = 0, Loc = -1,
                    Bag = (short)fit.Bag, Slot = (short)fit.Slot,
                }, _liveCts.Token);
                GD.Print($"[storage-test] dépôt famille 177 " +
                         $"item={fit.ItemId} → coffre 0");
                await Task.Delay(1800);
                var chest0 = FamilyStorageState.Chest(0);
                GD.Print($"[verify] coffre famille 0 : " +
                         $"{chest0?.Count ?? 0} item(s)");
                var stored = chest0?.Values.FirstOrDefault();
                if (stored != null)
                {
                    await _session.SendZoneAsync(new ZoneFamilyStorageGet
                    {
                        StorageIndex = 0,
                        Loc = (short)stored.Slot,
                        Bag = -1, Slot = -1,
                    }, _liveCts.Token);
                    GD.Print($"[storage-test] retrait famille 178 " +
                             $"loc={stored.Slot}");
                    await Task.Delay(1800);
                    GD.Print($"[verify] coffre famille 0 après " +
                             $"retrait : " +
                             $"{FamilyStorageState.Chest(0)?.Count ?? 0}" +
                             " item(s)");
                }
            }
            // Capture finale (thread principal via _pending).
            if (_storageShot.Length > 0)
            {
                var tcs = new TaskCompletionSource<bool>();
                _pending.Enqueue(() =>
                {
                    RenderingServer.ForceDraw();
                    var img = GetViewport().GetTexture()?.GetImage();
                    img?.SavePng(_storageShot);
                    GD.Print($"[storage-test] shot {_storageShot}");
                    tcs.SetResult(true);
                });
                await tcs.Task;
            }
            GD.Print("[storage-test] terminé");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[storage-test] {ex}");
        }
    }

    /// <summary>Poll <see cref="ListNpcsAsync"/> jusqu'à
    /// <paramref name="ms"/> ms ou un NPC ≤15 u — les 298 montent
    /// au rythme du sight tick serveur.</summary>
    private async Task<List<(uint Handle, int NpcId, string Name,
        Vector3 Pos, float Dist)>> WaitNpcsAsync(int max, int ms)
    {
        var list = await ListNpcsAsync(max);
        for (var waited = 0;
             waited < ms && (list.Count == 0 || list[0].Dist > 15f);
             waited += 500)
        {
            await Task.Delay(500);
            list = await ListNpcsAsync(max);
        }
        return list;
    }

    /// <summary>--mob-test : les spawns wire n'arrivent que dans la
    /// zone de visibilité du personnage ; S002 contient des mobs, mais
    /// hors du rayon initial. On se déclare donc aux spawns
    /// [reborn_monsters] proches jusqu'à recevoir des 298 — le mob
    /// engagé ensuite est un vrai objet serveur, pas un spawn mock.</summary>
    /// <summary>Vrai tant que le pilote --mob-test cherche un mob —
    /// --combat-test attend la fin de la migration/scan avant de
    /// basculer en cast sans cible.</summary>
    public static bool MobTestActive { get; private set; }

    private async Task RunMobTestAsync()
    {
        try
        {
            MobTestActive = true;
            GD.Print("[mob-test] lancé — attente de la table monstre…");
            await Task.Delay(2000);
            for (var w = 0; w < 45 && (_migratingMap || _map == null); w++)
                await Task.Delay(1000, _liveCts.Token);
            var spawns = _map?.Scene?.Monsters;
            if (spawns is { Count: > 0 })
            {
                var tcs = new TaskCompletionSource<List<SceneTable.MonsterSpawn>>();
                _pending.Enqueue(() =>
                {
                    var peX = 0f;
                    var peY = 0f;
                    if (_player != null)
                    {
                        var pe = WorldConv.ToEdenGround(
                            _player.GlobalPosition);
                        peX = pe.X;
                        peY = pe.Y;
                    }
                    tcs.SetResult(spawns
                        .OrderBy(m => MonsterDef(m.MonsterId)
                            is { IsStationary: false } ? 0 : 1)
                        .ThenBy(m =>
                        {
                            var dx = m.X - peX;
                            var dy = m.Y - peY;
                            return dx * dx + dy * dy;
                        }).Take(24).ToList());
                });
                foreach (var target in await tcs.Task)
                {
                    var moveTcs = new TaskCompletionSource<bool>();
                    _pending.Enqueue(() =>
                    {
                        if (_player != null)
                        {
                            var dst = WorldConv.ToGodot(
                                target.X + 4f, target.Y + 4f,
                                _player.GlobalPosition.Y);
                            _player.GlobalPosition = SnapToGround(dst);
                            _lastSentPos = _player.GlobalPosition;
                        }
                        moveTcs.SetResult(true);
                    });
                    await moveTcs.Task;
                    var epos = (X: target.X + 4f, Y: target.Y + 4f);
                    await _session!.SendZoneAsync(new ZoneMoveSelf
                    {
                        Tail =
                        {
                            [0] = epos.X, [1] = epos.Y,
                            [2] = epos.X, [3] = epos.Y,
                            [4] = 0f,
                        },
                        MoveType = 7,
                    }, _liveCts.Token);
                    GD.Print($"[mob-test] MoveSelf vers #{target.MonsterId} " +
                             $"{target.ModelCode} ({epos.X:F1},{epos.Y:F1})");
                    await Task.Delay(2000);
                    var countTcs = new TaskCompletionSource<int>();
                    _pending.Enqueue(() => countTcs.SetResult(
                        _registry?.Entities.Values.Count(e => e.Kind == 12) ?? 0));
                    var mobs = await countTcs.Task;
                    GD.Print($"[mob-test] {mobs} mob(s) kind=12 en vue");
                    if (mobs > 0) { await ApplyBrokenTestAsync(); return; }
                }
            }
            // Carte urbaine (S001 = Aven : spawn table sans hostiles)
            // → migration vers la première carte de champ (node 2 =
            // Limestone Mtn.) puis une seconde passe sur la nouvelle
            // scene table — même mécanisme que //transport_node du
            // storage-test.
            GD.Print("[mob-test] aucun mob sur cette carte — " +
                     "migration //transport_node 2");
            await _session!.SendZoneAsync(new ZoneTextCommand
            { Text = "//transport_node 2" }, _liveCts.Token);
            // La migration recharge la scène : on attend que la
            // table de la nouvelle carte soit peuplée puis on
            // relance une passe.
            await Task.Delay(12000, _liveCts.Token);
            // La migration recharge la scène : attente bornée jusqu'à
            // ce que la nouvelle table monstre soit lisible (~45 s max
            // — LoadMapNode est synchrone, l'herbe arrive en dernier).
            List<SceneTable.MonsterSpawn> spawns2 = new();
            for (var i = 0; i < 45; i++)
            {
                var mapTcs = new TaskCompletionSource<
                    List<SceneTable.MonsterSpawn>>();
                _pending.Enqueue(() => mapTcs.SetResult(
                    _nodeId == 2 && !_migratingMap
                        ? _map?.Scene?.Monsters?.ToList() ?? new()
                        : new()));
                spawns2 = await mapTcs.Task;
                if (spawns2.Count > 0) break;
                await Task.Delay(1000, _liveCts.Token);
            }
            foreach (var target in spawns2
                         .OrderBy(m => MonsterDef(m.MonsterId)
                             is { IsStationary: false } ? 0 : 1)
                         .Take(24))
            {
                var moveTcs = new TaskCompletionSource<bool>();
                _pending.Enqueue(() =>
                {
                    if (_player != null)
                    {
                        var dst = WorldConv.ToGodot(
                            target.X + 4f, target.Y + 4f,
                            _player.GlobalPosition.Y);
                        _player.GlobalPosition = SnapToGround(dst);
                        _lastSentPos = _player.GlobalPosition;
                    }
                    moveTcs.SetResult(true);
                });
                await moveTcs.Task;
                var epos = (X: target.X + 4f, Y: target.Y + 4f);
                await _session!.SendZoneAsync(new ZoneMoveSelf
                {
                    Tail =
                    {
                        [0] = epos.X, [1] = epos.Y,
                        [2] = epos.X, [3] = epos.Y,
                        [4] = 0f,
                    },
                    MoveType = 7,
                }, _liveCts.Token);
                GD.Print($"[mob-test] MoveSelf vers #{target.MonsterId} " +
                         $"{target.ModelCode} ({epos.X:F1},{epos.Y:F1})");
                await Task.Delay(2000);
                var countTcs = new TaskCompletionSource<int>();
                _pending.Enqueue(() => countTcs.SetResult(
                    _registry?.Entities.Values.Count(e => e.Kind == 12)
                        ?? 0));
                var mobs = await countTcs.Task;
                GD.Print($"[mob-test] {mobs} mob(s) kind=12 en vue");
                if (mobs > 0) { await ApplyBrokenTestAsync(); return; }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[mob-test] {ex}");
        }
        finally
        {
            MobTestActive = false;
        }
    }

    /// <summary>--broken-test : force l'état Broken sur tous les mobs
    /// en vue (KNOCK/KNOCKNODE doivent se masquer — B11), puis
    /// temporise pour laisser la capture --shot cadrer l'état.</summary>
    private async Task ApplyBrokenTestAsync()
    {
        if (!_brokenTest) return;
        var done = new TaskCompletionSource<int>();
        _pending.Enqueue(() =>
        {
            var n = 0;
            if (_registry != null)
                foreach (var e in _registry.Entities.Values)
                    if (e.Kind == 12)
                    {
                        e.SetBroken(true); n++;
                        if (n == 1)
                        {
                            var vis = e.GetNodeOrNull<Node3D>("Visual");
                            if (vis != null)
                                GD.Print("[broken-test] nœuds : " +
                                    string.Join(", ",
                                        vis.FindChildren("*", "Node3D",
                                            true, false)
                                            .Select(c => c.Name.ToString())
                                            .Take(80)));
                        }
                    }
            GD.Print($"[broken-test] SetBroken(true) sur {n} mob(s)");
            done.SetResult(n);
        });
        await done.Task;
        if (_brokenRestore)
        {
            // Laisse l'œil constater l'état brisé dans le log/
            // stream, puis restaure (FUN_00771920) : la capture
            // finale montre les KNOCK de retour.
            await Task.Delay(4000, _liveCts.Token);
            var back = new TaskCompletionSource<int>();
            _pending.Enqueue(() =>
            {
                var n = 0;
                if (_registry != null)
                    foreach (var e in _registry.Entities.Values)
                        if (e.Kind == 12) { e.SetBroken(false); n++; }
                GD.Print($"[broken-test] SetBroken(false) sur {n} mob(s)");
                back.SetResult(n);
            });
            await back.Task;
        }
        // Petite temporisation : le --shot part 150 f après
        // MobTestActive=false — l'état Broken reste appliqué.
        await Task.Delay(500, _liveCts.Token);
    }

    /// <summary>Liste les NPC kind 8 en vue triés par distance —
    /// exécuté sur le thread principal (GlobalPosition est interdit
    /// hors main thread), résultat via TaskCompletionSource.</summary>
    private Task<List<(uint Handle, int NpcId, string Name,
        Vector3 Pos, float Dist)>> ListNpcsAsync(int max)
    {
        var tcs = new TaskCompletionSource<
            List<(uint, int, string, Vector3, float)>>();
        _pending.Enqueue(() =>
        {
            var ppos = _player?.GlobalPosition ?? Vector3.Zero;
            var list = _registry!.Entities.Values
                .Where(e => e.Kind == 8 &&
                       e.GlobalPosition.LengthSquared() > 4f)
                .Select(e => (e.Handle, e.EntityId, e.DisplayName,
                    e.GlobalPosition,
                    (e.GlobalPosition - ppos).Length()))
                .OrderBy(t => t.Item5)
                .Take(max)
                .ToList();
            tcs.SetResult(list);
        });
        return tcs.Task;
    }

    /// <summary>Liste les autres JOUEURS en vue (kind 0 / entityId 0,
    /// handle = charId ≠ self) triés par distance — même marshalling
    /// main-thread que <see cref="ListNpcsAsync"/>.</summary>
    private Task<List<(uint Handle, string Name, float Dist)>>
        ListPlayersAsync()
    {
        var tcs = new TaskCompletionSource<
            List<(uint, string, float)>>();
        _pending.Enqueue(() =>
        {
            var ppos = _player?.GlobalPosition ?? Vector3.Zero;
            var list = _registry!.Entities.Values
                .Where(e => e.Kind == 0 && e.EntityId == 0 &&
                       e.Handle != _selfCharId &&
                       e.GlobalPosition.LengthSquared() > 4f)
                .Select(e => (e.Handle, e.DisplayName,
                    (e.GlobalPosition - ppos).Length()))
                .OrderBy(t => t.Item3)
                .ToList();
            tcs.SetResult(list);
        });
        return tcs.Task;
    }

    /// <summary>Capture le viewport dans <paramref name="path"/> —
    /// exécuté sur le thread principal via <see cref="_pending"/>
    /// (GetViewport/ForceDraw interdits hors main thread).</summary>
    private Task<bool> SaveViewportPng(string path)
    {
        var tcs = new TaskCompletionSource<bool>();
        _pending.Enqueue(() =>
        {
            try
            {
                RenderingServer.ForceDraw();
                var img = GetViewport().GetTexture()?.GetImage();
                var p = System.IO.Path.IsPathRooted(path)
                    ? path : System.IO.Path.GetFullPath(path);
                img?.SavePng(p);
                GD.Print($"[shot] {p}");
                tcs.SetResult(img != null);
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>Enfile une mutation d'état trade/vending sur le thread
    /// principal (les events Changed repeignent les fenêtres —
    /// interdit hors main thread).</summary>
    private void PostUi(Action act) => _pending.Enqueue(act);

    /// <summary>--trade-test : round trip réel d'échange J↔J —
    /// l'initiateur (rôle <c>init</c>) clone un consommable, émet 36
    /// sur le joueur le plus proche (≤ 10 u côté serveur), offre
    /// 1 or + 1 item puis verrouille (41) ; l'accepteur répond 37,
    /// offre 1 or et verrouille aussi. Le double verrouillage
    /// déclenche ExecuteTradeAsync — preuve : deltas or (280) +
    /// inventaire (274/275/291) et <see cref="TradeState.Completed"/>.
    /// Les écritures d'état UI passent par <see cref="PostUi"/>.</summary>
    private async Task RunTradeTestAsync()
    {
        try
        {
            GD.Print($"[trade-test] lancé " +
                $"({(_tradeTestInit ? "initiateur" : "accepteur")})");
            await Task.Delay(3000);   // spawns + binds HUD
            // Les deux parties se rejoignent sur S001 (node 1) —
            // CloseEnough(cible, 10.0) exigé par RequestTrade.
            await _session!.SendZoneAsync(new ZoneTextCommand
                { Text = "//transport_node 1" }, _liveCts.Token);
            await Task.Delay(6000);   // migration + re-spawn
            await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//tele 250 402" }, _liveCts.Token);
            await Task.Delay(2000);
            var gold0 = PlayerVitals.Gold;
            var bag0 = PlayerBag.Items.Count(i => i.ItemId != 0);
            if (_tradeTestInit)
            {
                await _session!.SendZoneAsync(new ZoneTextCommand
                    { Text = "//clone_item 38572" }, _liveCts.Token);
                await Task.Delay(1200);
                // Retry par cible : un autre perso GM peut traîner dans
                // la zone (ex. --storage-test parallèle) — si le 36
                // tombe dessus, l'invite expire sans réponse. On tente
                // chaque joueur visible, avec 42 pour purger l'invite
                // morte avant de passer au suivant.
                var tried = new HashSet<uint>();
                for (var round = 0; round < 6 && !TradeState.IsOpen;
                        round++)
                {
                    var ps = await ListPlayersAsync();
                    var t = ps.FirstOrDefault(
                        p => !tried.Contains(p.Handle));
                    t = t.Handle == 0 && ps.Count > 0 ? ps[0] : t;
                    if (t.Handle == 0) { await Task.Delay(1000); continue; }
                    tried.Add(t.Handle);
                    GD.Print($"[trade-test] cible {t.Handle} " +
                        $"«{t.Name}» à {t.Dist:F1}u → 36");
                    TradeState.MarkOutgoingInvite(t.Handle, t.Name);
                    await _session!.SendZoneAsync(
                        new ZoneTradeRequest
                        { TargetCharId = t.Handle },
                        _liveCts.Token);
                    for (var i = 0; i < 70 && !TradeState.IsOpen; i++)
                        await Task.Delay(300);
                    if (!TradeState.IsOpen)
                    {
                        GD.Print($"[trade-test] invite sans réponse " +
                            $"({t.Handle}) — cancel puis suivant");
                        await _session.SendZoneAsync(new ZoneTradeCancel
                        { PartnerCharId = t.Handle, Reason = 0 },
                            _liveCts.Token);
                    }
                }
            }
            // Invite entrante → 37 (les deux rôles). Attente large :
            // l'initiateur met ~1 min à spawn+tele+scan, l'accepteur
            // doit patienter (preuve run 1 : le 331 arrivait juste
            // après l'abandon à 45 s).
            for (var i = 0; i < 500 &&
                    TradeState.PendingInvite == null &&
                    !TradeState.IsOpen; i++)
                await Task.Delay(300);
            if (TradeState.PendingInvite is { } inv)
            {
                GD.Print($"[trade-test] invite {inv.CharId} " +
                    $"«{inv.Name}» → 37");
                await _session!.SendZoneAsync(
                    new ZoneTradeRequestAccept
                    { InitiatorCharId = inv.CharId },
                    _liveCts.Token);
                PostUi(TradeState.ClearInvite);
            }
            for (var i = 0; i < 200 && !TradeState.IsOpen; i++)
                await Task.Delay(300);
            if (!TradeState.IsOpen)
            {
                GD.PrintErr("[trade-test] échange jamais ouvert");
                return;
            }
            GD.Print($"[trade-test] OUVERT vs " +
                $"{TradeState.PartnerName} — offres puis lock");
            await _session!.SendZoneAsync(new ZoneTradeModifyGold
                { GoldMirror = 1, Gold = 1 }, _liveCts.Token);
            PostUi(() => TradeState.SetMyGold(1));
            if (_tradeTestInit)
            {
                var it = PlayerBag.Items.FirstOrDefault(
                    x => x.ItemId == 38572) ?? PlayerBag.Items
                    .FirstOrDefault(x => x.ItemId != 0);
                if (it != null)
                {
                    await _session.SendZoneAsync(new ZoneTradeAddItem
                    {
                        Bag = (ushort)it.Bag, Slot = (ushort)it.Slot,
                        TradeSlot = 0,
                        Count = (ushort)Math.Max(1, it.Amount),
                    }, _liveCts.Token);
                    PostUi(() => TradeState.AddMyItem(0, it));
                }
            }
            await Task.Delay(800);
            PostUi(TradeState.MarkConfirmSent);
            await Task.Delay(300);   // laisse la marque monter avant 41
            await _session!.SendZoneAsync(new ZoneTradeConfirm(),
                _liveCts.Token);
            GD.Print("[trade-test] lock 41 émis — attente…");
            await Task.Delay(1500);
            if (_tradeShot.Length > 0)
                await SaveViewportPng(_tradeShot);   // fenêtre ouverte,
                                                     // offres+lock visibles
            for (var i = 0; i < 100 && !TradeState.Completed &&
                    TradeState.IsOpen; i++)
                await Task.Delay(300);
            await Task.Delay(1500);   // laisse tomber les deltas
            var bag1 = PlayerBag.Items.Count(i => i.ItemId != 0);
            GD.Print($"[trade-test] RÉSULTAT " +
                $"complet={TradeState.Completed} " +
                $"ouvert={TradeState.IsOpen} " +
                $"gold {gold0}→{PlayerVitals.Gold} " +
                $"items {bag0}→{bag1}");
            if (_tradeShot.Length > 0)
                await SaveViewportPng(_tradeShot);
        }
        catch (Exception ex) { GD.PrintErr($"[trade-test] {ex}"); }
    }

    /// <summary>--vending-test : round trip réel d'étal joueur —
    /// vendeur : //clone_item 38572, //transport_node 1, //tele 247
    /// 396 (centre région 600 de s001.ini), 77 VendorSellItem
    /// (prix 1) puis attente du statut [1][2] ; acheteur : même
    /// téléportation, 72 AskVendorContainer sur le joueur vendeur
    /// (polling — le serveur ignore silencieusement une cible sans
    /// étal), 76 VendorBuy de l'index 0 puis vérifie or/sac.
    /// Preuve : 377 [1][2], 378 goods, 274/275/291 + 280.</summary>
    private async Task RunVendingTestAsync()
    {
        try
        {
            GD.Print($"[vending-test] lancé " +
                $"({(_vendingBuyer ? "acheteur" : "vendeur")})");
            await Task.Delay(3000);
            var gold0 = PlayerVitals.Gold;
            var bag0 = PlayerBag.Items.Count(i => i.ItemId != 0);
            // Place du marché S001 : node 1 puis téléportation dans
            // la région d'étal 600 (centre 246.9,395.9 r=35 —
            // s001.ini [position] grp 600).
            await _session!.SendZoneAsync(new ZoneTextCommand
                { Text = "//transport_node 1" }, _liveCts.Token);
            await Task.Delay(6000);   // migration + re-spawn
            await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//tele 247 396" }, _liveCts.Token);
            GD.Print("[vending-test] //tele 247 396 (région 600)");
            await Task.Delay(2500);
            if (!_vendingBuyer)
            {
                // Clone APRÈS la migration + attente du resync du sac
                // (le miroir reste vide quelques secondes après
                // transport_node — vu : bag0=0 puis 58 en fin de test).
                for (var i = 0; i < 40 &&
                        !PlayerBag.Items.Any(x => x.ItemId != 0); i++)
                    await Task.Delay(300);
                await _session!.SendZoneAsync(new ZoneTextCommand
                    { Text = "//clone_item 38572" }, _liveCts.Token);
                await Task.Delay(1500);
            }
            if (!_vendingBuyer)
            {
                var it = PlayerBag.Items.FirstOrDefault(
                    x => x.ItemId == 38572) ?? PlayerBag.Items
                    .FirstOrDefault(x => x.ItemId != 0);
                if (it == null)
                {
                    GD.PrintErr("[vending-test] sac vide — abandon " +
                        $"(miroir={PlayerBag.Items.Count} " +
                        $"ids={string.Join(',', PlayerBag.Items.Take(8).Select(x => x.ItemId))})");
                    return;
                }
                var m = new ZoneVendorSellItem
                { RegionType = 600, Title = "QA Stall" };
                m.Entries.Add(new ZoneVendorSellItem.SellEntry(
                    (short)it.Bag, (short)it.Slot,
                    (short)Math.Max(1, it.Amount), 1));
                VendingState.MarkSellRequested();
                await _session.SendZoneAsync(m, _liveCts.Token);
                GD.Print($"[vending-test] 77 émis item={it.ItemId} " +
                    $"sac={it.Bag} slot={it.Slot} prix=1");
                for (var i = 0; i < 40 && !VendingState.Selling; i++)
                    await Task.Delay(300);
                GD.Print($"[vending-test] RÉSULTAT vendeur " +
                    $"selling={VendingState.Selling} " +
                    $"statut={VendingState.LastStatus}");
                // Garde l'étal ouvert pour l'acheteur (~2 min de boot
                // + tele) : attend la vente (l'item quitte le sac,
                // 275) ou 4 min max.
                var sold = 0;
                for (var i = 0; i < 80; i++)
                {
                    await Task.Delay(3000);
                    if (!PlayerBag.Items.Any(x => x.ItemId == it.ItemId))
                    { sold = 1; break; }
                }
                GD.Print($"[vending-test] étal : item vendu={sold} " +
                    $"gold {gold0}→{PlayerVitals.Gold}");
                if (_vendingShot.Length > 0)
                    await SaveViewportPng(_vendingShot);
                await _session.SendZoneAsync(new ZoneVendorClose
                    { Mode = 0, Ignored = 0 }, _liveCts.Token);
                GD.Print("[vending-test] 74 émis — étal fermé");
                return;
            }
            // Acheteur : repère le vendeur puis 72 en polling.
            for (var i = 0; i < 60 && !VendingState.Viewing; i++)
            {
                var ps = await ListPlayersAsync();
                if (ps.Count > 0)
                {
                    var seller = ps[0];
                    VendingState.MarkViewRequested((int)seller.Handle);
                    await _session!.SendZoneAsync(
                        new ZoneAskVendorContainer
                        { Value = (int)seller.Handle }, _liveCts.Token);
                    await Task.Delay(2500);
                }
                else await Task.Delay(1000);
            }
            if (!VendingState.Viewing || VendingState.Goods.Count == 0)
            {
                GD.PrintErr("[vending-test] étal jamais vu " +
                    "(378 absente/vide)");
                return;
            }
            var g = VendingState.Goods[0];
            GD.Print($"[vending-test] étal {VendingState.SellerId} " +
                $"«{VendingState.Title}» — achat idx={g.Index} " +
                $"item={g.ItemId} prix={g.Price} lot={g.LotQuantity}");
            var buy = new ZoneVendorBuy
            { SellerId = VendingState.SellerId };
            buy.Lines.Add(new ZoneVendorBuy.BuyLine(
                (short)g.LotQuantity, g.Index));
            await _session!.SendZoneAsync(buy, _liveCts.Token);
            await Task.Delay(2500);
            var bag1 = PlayerBag.Items.Count(i => i.ItemId != 0);
            var got = PlayerBag.Items.Any(i => i.ItemId == 38572);
            GD.Print($"[vending-test] RÉSULTAT acheteur " +
                $"items {bag0}→{bag1} (38572={got}) " +
                $"gold {gold0}→{PlayerVitals.Gold}");
            if (_vendingShot.Length > 0)
                await SaveViewportPng(_vendingShot);
            await _session.SendZoneAsync(new ZoneVendorCloseView
            { SellerId = VendingState.SellerId }, _liveCts.Token);
        }
        catch (Exception ex) { GD.PrintErr($"[vending-test] {ex}"); }
    }

    /// <summary>--duel-test[=accept] : défi 1v1 réel — même rendez-vous
    /// que l'étal (transport_node 1 + //tele 247 396). Le challenger
    /// émet 135 ZoneDuelRequest sur l'autre joueur en vue (polling —
    /// re-émet tant que 452 absente) ; l'accepteur répond 136 via
    /// <see cref="DuelDialog.AutoAccept"/>. Preuve : 452 résultat
    /// côté challenger + 453 DuelBegin des deux côtés
    /// (<see cref="MiscPackets.Latest"/>).</summary>
    private async Task RunDuelTestAsync()
    {
        try
        {
            GD.Print($"[duel-test] lancé " +
                $"({(_duelAccept ? "accepteur" : "challenger")})");
            if (_duelAccept) Hud.DuelDialog.AutoAccept = true;
            await Task.Delay(3000);
            await _session!.SendZoneAsync(new ZoneTextCommand
                { Text = "//transport_node 1" }, _liveCts.Token);
            await Task.Delay(6000);   // migration + re-spawn
            await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//tele 247 396" }, _liveCts.Token);
            await Task.Delay(2500);
            if (_duelAccept)
            {
                ZoneClientDuelRequest? req = null;
                var answered = false;
                for (var i = 0; i < 300 &&
                    !MiscPackets.Latest.ContainsKey(453); i++)
                {
                    await Task.Delay(1000);
                    if (req == null && MiscPackets.Latest
                            .TryGetValue(451, out var m) &&
                        m is ZoneClientDuelRequest r)
                        req = r;
                    // Repli : si le dialogue n'a pas répondu (session
                    // en migration au moment du 451), le pilote envoie
                    // 136 lui-même.
                    if (req != null && !answered && i > 8)
                    {
                        answered = true;
                        await _session.SendZoneAsync(
                            new ZoneDuelRequestAnswer
                            { Accept = 1, RequesterId = req.Field1 },
                            _liveCts.Token);
                        GD.Print($"[duel-test] 136 accept émis → " +
                            $"{req.Field1:x8}");
                    }
                }
                GD.Print($"[duel-test] RÉSULTAT accepteur " +
                    $"451={req != null} " +
                    $"453={MiscPackets.Latest.ContainsKey(453)}");
                return;
            }
            if (_pvpAttack)
            {
                // Attaque pré-duel : hors contexte ennemi le serveur
                // doit refuser (IsPlayerEnemy → 410, aucun 272).
                for (var i = 0; i < 30; i++)
                {
                    var ps0 = await ListPlayersAsync();
                    if (ps0.Count > 0)
                    {
                        await _session!.SendZoneAsync(new ZoneMeleeAttack
                            { TargetId = (int)ps0[0].Handle, AttackSlot = 0 },
                            _liveCts.Token);
                        GD.Print($"[pvp-test] NCID5 pré-duel → " +
                            $"{ps0[0].Handle:x8}");
                        break;
                    }
                    await Task.Delay(1000);
                }
                await Task.Delay(2500);
            }
            for (var i = 0; i < 40 &&
                !MiscPackets.Latest.ContainsKey(453); i++)
            {
                var ps = await ListPlayersAsync();
                if (ps.Count > 0)
                {
                    await _session!.SendZoneAsync(new ZoneDuelRequest
                        { TargetId = ps[0].Handle }, _liveCts.Token);
                    GD.Print($"[duel-test] 135 émis → «{ps[0].Name}» " +
                        $"({ps[0].Handle:x8})");
                    for (var j = 0; j < 16 &&
                        !MiscPackets.Latest.ContainsKey(452); j++)
                        await Task.Delay(500);
                    if (MiscPackets.Latest.ContainsKey(452)) break;
                }
                else await Task.Delay(2000);
            }
            for (var i = 0; i < 30 &&
                !MiscPackets.Latest.ContainsKey(453); i++)
                await Task.Delay(1000);
            if (MiscPackets.Latest.TryGetValue(452, out var rr) &&
                rr is ZoneClientDuelRequestResult drr)
                GD.Print($"[duel-test] 452 «{drr.Field1}» " +
                    $"«{drr.Field2}» code={drr.Field3}");
            GD.Print($"[duel-test] RÉSULTAT challenger " +
                $"452={MiscPackets.Latest.ContainsKey(452)} " +
                $"453={MiscPackets.Latest.ContainsKey(453)}");
            if (_pvpAttack &&
                MiscPackets.Latest.ContainsKey(453))
            {
                // Countdown natif 5 s puis duel actif → l'attaque doit
                // désormais passer (272 émis, non-létal clamp 1 HP).
                await Task.Delay(6500);
                var ps2 = await ListPlayersAsync();
                if (ps2.Count > 0)
                {
                    await _session!.SendZoneAsync(new ZoneMeleeAttack
                        { TargetId = (int)ps2[0].Handle, AttackSlot = 0 },
                        _liveCts.Token);
                    GD.Print($"[pvp-test] NCID5 duel actif → " +
                        $"{ps2[0].Handle:x8}");
                }
                await Task.Delay(2500);
            }
        }
        catch (Exception ex) { GD.PrintErr($"[duel-test] {ex}"); }
    }

    /// <summary>--daily-test : calendrier quotidien — 258 op=1 (état)
    /// → 615 type 1 lu via un handler OnZone propre au pilote, puis
    /// claim du premier jour complété non réclamé (op=2) → 615 type 2.
    /// Verdict honnête : « aucun jour réclamable » si le masque est
    /// vide (le claim légitime renvoie simplement rien).</summary>
    private async Task RunDailyTestAsync()
    {
        try
        {
            await Task.Delay(2500);
            ZoneUpdateDailyAwards? last = null;
            void H(BinReader r)
            {
                var m = new ZoneUpdateDailyAwards();
                m.Deserialize(r); last = m;
            }
            _session!.OnZone(615, H);
            await _session.SendZoneAsync(
                new ZoneDailyAwardOp { Field1 = 1 }, _liveCts.Token);
            for (var i = 0; i < 15 && last == null; i++)
                await Task.Delay(1000);
            if (last == null)
            {
                GD.PrintErr("[daily-test] TIMEOUT — pas de 615");
                return;
            }
            if (last.Kind == 4)
            {
                GD.Print("[daily-test] système désactivé côté " +
                    "serveur (type 4)");
                return;
            }
            // Poll op=1 chaque minute : le jour 1 exige 600 s en ligne
            // (C_DailyAward.ini col5) — le temps s'accumule pendant
            // que le pilote reste connecté.
            var day = -1;
            for (var t = 0; t < 12 && day < 0; t++)
            {
                GD.Print($"[daily-test] 615 type={last.Kind} " +
                    $"jour={last.DayIndex} online=" +
                    $"{last.OnlineSeconds}s jours={last.AwardDays:x} " +
                    $"réclamés={last.AwardRecords:x}");
                for (var d = 0; d < 32; d++)
                {
                    var bit = 1L << d;
                    if ((last.AwardDays & bit) != 0 &&
                        (last.AwardRecords & bit) == 0)
                    { day = d + 1; break; }
                }
                if (day > 0) break;
                // ~60 s entre deux requêtes : le temps en ligne doit
                // s'accumuler côté serveur entre les op=1.
                await Task.Delay(60000);
                last = null;
                await _session.SendZoneAsync(
                    new ZoneDailyAwardOp { Field1 = 1 }, _liveCts.Token);
                for (var i = 0; i < 15 && last == null; i++)
                    await Task.Delay(1000);
                if (last == null)
                {
                    GD.PrintErr("[daily-test] TIMEOUT — 615 perdu " +
                        "en requête");
                    return;
                }
            }
            if (day < 0)
            {
                GD.Print("[daily-test] RÉSULTAT — état 615 reçu, " +
                    "aucun jour réclamable après 11 min en ligne");
                return;
            }
            last = null;
            await _session.SendZoneAsync(new ZoneDailyAwardOp
                { Field1 = 2, Field2 = (uint)day }, _liveCts.Token);
            GD.Print($"[daily-test] 258 claim jour {day} émis");
            for (var i = 0; i < 15 && last == null; i++)
                await Task.Delay(1000);
            GD.Print(last is { Kind: 2 }
                ? $"[daily-test] RÉSULTAT — 615 type 2 : jour {day} " +
                    "réclamé (item livré)"
                : "[daily-test] RÉSULTAT — claim refusé/silencieux " +
                    "(pas de 615 type 2)");
        }
        catch (Exception ex) { GD.PrintErr($"[daily-test] {ex}"); }
    }

    /// <summary>--levelup-test : level-up réel via `//gain_exp` —
    /// guette 284 ClientLevelUp + les octrois de sorts 286 dans
    /// <see cref="MiscPackets.Seen"/>, puis restaure le niveau
    /// d'origine via `//set_level` (perso de test).</summary>
    private async Task RunLevelupTestAsync()
    {
        try
        {
            await Task.Delay(3000);
            int Seen(int op) => MiscPackets.Seen
                .TryGetValue(op, out var c) ? c : 0;
            var u0 = Seen(284);
            // set_level force une vraie notif 284 (gain_exp n'émet
            // aucun paquet si le seuil n'est pas atteint).
            await _session!.SendZoneAsync(new ZoneTextCommand
                { Text = "//set_level 31" }, _liveCts.Token);
            GD.Print("[levelup-test] //set_level 31 émis");
            for (var i = 0; i < 30 && Seen(284) == u0; i++)
                await Task.Delay(1000);
            var lv = MiscPackets.Latest.TryGetValue(284, out var m) &&
                m is ZoneClientLevelUp u ? (int)u.Field3 : -1;
            GD.Print($"[levelup-test] RÉSULTAT — 284=+{Seen(284) - u0} " +
                $"niveau={lv} 286=+{Seen(286)} 316=+{Seen(316)} " +
                $"600=+{Seen(600)}");
            if (Seen(284) == u0)
                GD.PrintErr("[levelup-test] pas de 284 — level-up " +
                    "non déclenché");
            // Restaure le niveau de test (perso QA).
            await _session.SendZoneAsync(new ZoneTextCommand
                { Text = "//set_level 30" }, _liveCts.Token);
            GD.Print("[levelup-test] //set_level 30 émis (restauration)");
        }
        catch (Exception ex) { GD.PrintErr($"[levelup-test] {ex}"); }
    }

    /// <summary>--mall-test : pilote QA bout-en-bout de l'item mall —
    /// soldes (CW25) + catalogue (CW24) puis achat (CW23) de l'entrée
    /// la moins chère ; vérifie le résultat 63 (points débités) et la
    /// réception de l'item (directe ou via reçu 57 + claim).</summary>
    private async Task RunMallTestAsync()
    {
        try
        {
            GD.Print("[mall-test] lancé");
            await Task.Delay(3000);
            var s = _session!;
            if (s.World?.IsConnected != true)
            {
                GD.PrintErr("[mall-test] pas de connexion monde");
                return;
            }
            var ct = _liveCts.Token;
            var sendW = (NetMessage m) => s.SendWorldAsync(m, ct);
            await sendW(new WorldItemMallPointInfoQuery());
            await sendW(new WorldItemMallListQuery
                { Version = ItemMallState.Version });
            await sendW(new WorldItemMallAnythingQuery());
            // Le catalogue arrive en lots de 20 (62) — attendre un
            // volume raisonnable avant de choisir.
            for (var i = 0; i < 60 && ItemMallState.Entries.Count < 20;
                 i++)
                await Task.Delay(500, ct);
            var e = ItemMallState.Entries.Values
                .Where(x => x.MoneyUnit == 1 && x.EffectivePrice > 0 &&
                    x.ItemNum > 0)
                .OrderBy(x => x.EffectivePrice)
                .FirstOrDefault();
            if (e == null)
            {
                GD.PrintErr("[mall-test] catalogue vide " +
                    $"(entries={ItemMallState.Entries.Count})");
                return;
            }
            var pts0 = ItemMallState.Points;
            var bag0 = PlayerBag.Items.Count(i => i.ItemId != 0);
            GD.Print($"[mall-test] achat grp={e.Group} idx={e.Index} " +
                $"item={e.ItemId} prix={e.EffectivePrice} " +
                $"pts={pts0} (catalogue={ItemMallState.Entries.Count})");
            await sendW(new WorldItemMallBuyRequest
            {
                Key = (short)e.Key,
                Index = (short)e.Index,
                Amount = 1,
                Version = ItemMallState.Version,
            });
            // 63 : résultat + soldes frais (mutation précoce, mais
            // poll sur Points ET statut par sécurité).
            for (var i = 0; i < 40 &&
                    ItemMallState.Points == pts0 &&
                    ItemMallState.LastStatus != "OK"; i++)
                await Task.Delay(300, ct);
            GD.Print($"[mall-test] résultat 63 : statut=" +
                $"«{ItemMallState.LastStatus}» " +
                $"pts {pts0}→{ItemMallState.Points} " +
                $"bonus={ItemMallState.Bonus}");
            if (ItemMallState.LastStatus != "OK") return;
            // Livraison : directe en sac ou reçu à réclamer (57→56).
            for (var i = 0; i < 40; i++)
            {
                if (PlayerBag.Items.Any(x => x.ItemId == e.ItemId))
                    break;
                if (ItemMallState.Receipts.Count > 0)
                    await sendW(new WorldItemMallReceiveAttempt
                        { ReceiptId = ItemMallState.Receipts[0].Id });
                await Task.Delay(500, ct);
            }
            var bag1 = PlayerBag.Items.Count(i => i.ItemId != 0);
            var got = PlayerBag.Items.Any(i => i.ItemId == e.ItemId);
            GD.Print($"[mall-test] RÉSULTAT item={e.ItemId} " +
                $"reçu={got} sac {bag0}→{bag1} " +
                $"reçus={ItemMallState.Receipts.Count}");
        }
        catch (Exception ex) { GD.PrintErr($"[mall-test] {ex}"); }
    }

    /// <summary>Routeur des paquets « complétude » (postés main
    /// thread par <see cref="MiscPackets.Received"/>) : applique les
    /// mises à jour d'état dont la sémantique est prouvée — les autres
    /// restent disponibles dans <see cref="MiscPackets.Latest"/>.</summary>
    private void OnMiscPacket(NetMessage m)
    {
        using var _pt = Perf.PerfTrace.Scope($"misc:{m.Opcode}");
        switch (m.Opcode)
        {
            // 270 ResetCoord [f32 x][f32 z] — position autoritaire
            // serveur (NoMove lock, désasm ZoneEntryFlow.Movement).
            case 270 when m is ZoneClientResetCoord rc:
                _haveServerPos = true;
                // Field2 = y sol EDEN (le serveur envoie st.PosZ = y du
                // plan) → Godot z = −y : l'ancien code posait z = +y
                // et MIROITAIT le joueur à chaque refus de mouvement
                // (mort/étourdi/immobilisé) — plan 3.2.2.
                if (_player != null)
                    _player.GlobalPosition = new Vector3(rc.Field1,
                        _player.GlobalPosition.Y, -rc.Field2);
                _lastSentPos = new Vector3(rc.Field1,
                    _lastSentPos.Y, -rc.Field2);
                break;
            // 606 UpdateAttrNewHp [u32 target][s16 0][i32 hpLeft]
            // (broadcast CombatHandlers : HP courant d'une cible).
            case 606 when m is ZoneClientUpdateAttrNewHp hp:
                if (hp.Field1 == _registry?.SelfCharId)
                    PlayerVitals.SetVitals(hp.Field3, PlayerVitals.MaxHp,
                        PlayerVitals.Mp, PlayerVitals.MaxMp);
                else if (_registry != null &&
                         _registry.Entities.TryGetValue(
                             (uint)hp.Field1, out var te))
                {
                    te.Hp = hp.Field3;
                    te.UpdateHpBar();
                }
                break;
            // 284 ClientLevelUp [u32 charId][u8 flag][s16 niveau]…
            // flag 0 = niveau perso, 1 = niveau de classe — le FX est
            // posé par SpellFx ; ici on met à jour le niveau affiché
            // et la base du ratio de la barre EXP/Classe.
            case 284 when m is ZoneClientLevelUp lu &&
                          lu.Field1 == _registry?.SelfCharId:
                if (lu.Field2 == 0) _hud?.SetSelfLevel(lu.Field3);
                else
                {
                    // Flag 1 = level-up de CLASSE : hotbar + le JobLv
                    // du portrait (sinon libellé figé à l'entrée).
                    _hud?.Hotbar?.SetLevels(0, lu.Field3);
                    _hud?.Vitals?.SetJobLv(lu.Field3);
                }
                break;
            // 441 TurnChar [u32 handle][f32 facing] — orientation
            // distante hors déplacement.
            case 441 when m is ZoneClientTurnChar tc:
                if (_registry?.Entities.TryGetValue(
                        (uint)tc.Field1, out var te2) == true)
                    te2.Rotation = new Vector3(0f,
                        EntityRegistry.EdenFacingToYaw(tc.Field2), 0f);
                break;
            // 341 Resurrect / 273 Restore — retour à la vie (toujours
            // adressé au self).
            case 341: case 273:
                PlayerVitals.ApplyRevive(new ZoneRevive
                    { Handle = _registry?.SelfCharId ?? 0 }, 
                    _registry?.SelfCharId ?? 0);
                break;
            // 352 ReceiveShortcutOfPage — page de raccourcis demandée
            // par le 55 de la hotbar (▲/▼ FastBar1).
            case 352 when m is ZoneClientReceiveShortcutOfPage scp:
                PlayerProfileState.Apply(scp);
                break;
            // 620 ArrangeItemOK / 614 BackItem — mutations sac déjà
            // poussées : on re-notifie l'UI.
            case 620: case 614:
                PlayerBag.RaiseChanged();
                break;
            // Monnaies dédiées (576 warcoin, 580 alpaca, 555 lifelong
            // gold, 554 lifelong point [s16][s16][i32 montant]).
            case 576 when m is ZoneClientUpdateWarCoin w:
                PlayerCoins.WarCoin = w.Value; break;
            case 580 when m is ZoneClientUpdateAlpacaCoin a:
                PlayerCoins.AlpacaCoin = a.Value; break;
            case 555 when m is ZoneClientUpdateLifelongGold g:
                PlayerCoins.LifelongGold = g.Value; break;
            case 554 when m is ZoneClientUpdateLifelongPoint lp:
                PlayerCoins.LifelongPoint = lp.Field3; break;
            // 495 PlayMonsterAction [u32 handle][s16 kfId] — motion KF
            // de CAI::SetEventAction (Event_MonsterAction des scènes) :
            // motion.ini exact puis convention (MonsterActionAnim).
            case 495 when m is ZoneClientPlayMonsterAction pma:
                _registry?.HandleMonsterAction(pma);
                break;
            // 494 PlaySound [str file][f32 x][f32 y][f32 z] (Event_PlaySound)
            // — pas de moteur audio client : journal seulement.
            case 494 when m is ZoneClientPlaySound ps:
                GD.Print($"[scene] son 494 «{ps.Field1}» " +
                    $"@({ps.Field2:F0},{ps.Field3:F0},{ps.Field4:F0}) " +
                    "(aucun moteur audio client)");
                break;
            // 383 PlayEffect (Event_PlayEffect) — pas de lecteur d'effets
            // de scène : journal seulement.
            case 383 when m is ZoneClientPlayEffect pe:
                GD.Print($"[scene] effet 383 «{pe.Field7}» " +
                    $"@({pe.Field1:F0},{pe.Field2:F0},{pe.Field3:F0}) " +
                    "(rendu d'effet de scène non porté)");
                break;
            case 42 when m is WorldClientTell t:
                _hud?.AddChat(2, t.Field1, t.Field2);
                break;
            case 43 when m is WorldClientTextOutputIndex to:
                _hud?.AddChat(7, "", to.Field2);
                break;
        }
    }

    /// <summary>Table de dispatch NCID → parse → action en file
    /// (chaque handler tourne hors thread Godot : on n'y touche QUE
    /// la file).</summary>
    private void RegisterZoneDispatch()
    {
        var s = _session!;
        void Bind<T>(T msg, Action<T> act) where T : NetMessage, new() =>
            s.OnZone(msg.Opcode, r =>
            {
                var m = new T();
                try { m.Deserialize(r); }
                catch (Exception ex)
                {
                    _pending.Enqueue(() => GD.PrintErr(
                        $"[live] parse {m.Name} : {ex.Message}"));
                    return;
                }
                _pending.Enqueue(() =>
                {
                    using var _pt = Perf.PerfTrace.Scope($"pkt:{m.Name}");
                    act(m);
                });
            });

        // Données d'entrée du personnage self.
        Bind(new ZoneEnterNodePlayerData(), m =>
        {
            GD.Print($"[live] self #{m.CharId} «{m.CharName}» " +
                     $"(+{m.Rest.Length}B bruts)");
            _selfCharId = m.CharId;
            _selfName = m.CharName;
            var pd = NativePlayerDataParser.Parse(m.Rest);
            if (pd != null)
            {
                _hp = pd.Hp; _maxHp = pd.MaxHp;
                _mp = pd.Mp; _maxMp = pd.MaxMp;
                _hud?.SetSelf(m.CharId, m.CharName, pd.Level);
                _hud?.SetPlayerData(pd);
            }
            else
            {
                _hud?.SetSelf(m.CharId, m.CharName, 0);
            }
        });
        Bind(new ZoneEnterNodeEquipAndSpell(), m =>
        {
            // 311 décodé : 6 conteneurs d'équipement (bitmap locs) +
            // liste des sorts connus — EquipAndSpellParser (codec
            // NativeItemCodec serveur, golden ncid_311.json).
            var (equipped, spells) = EquipAndSpellParser.Parse(m.Body);
            GD.Print($"[live] equip/spell {m.Body.Length}B → " +
                     $"{equipped.Count} items portés, {spells.Count} sorts");
            PlayerEquipment.ApplyEnterNode(equipped);
            PlayerSkills.ApplyEnterNode(spells);
            GD.Print($"[verify] compétences : {spells.Count} sorts " +
                     $"appris (311), {equipped.Count} items portés");
        });
        Bind(new ZoneEnterNodeInventory(), m =>
        {
            var items = NativeInventoryParser.Parse(m.Body);
            GD.Print($"[live] inventory {m.Body.Length}B → " +
                     $"{items.Count} items");
            _hud?.SetInventory(items);
            GD.Print($"[verify] inventaire : {items.Count} items " +
                     $"réels reçus");
            // --flow-test : équipe le 1er item équipable du sac —
            // même flux que le double-clic (7 SwitchItem + 8 mask
            // → confirmation 282).
            if (_flowTest && !_flowEquipDone)
            {
                foreach (var it in items)
                {
                    var def = Hud.InventoryWindow.LookupDef(
                        it.ItemId);
                    if (def == null ||
                        !ZoneItemActions.IsPlayerEquippableKind(def.Kind) ||
                        ZoneItemActions.AllowedLocations(def.EquipLoc)
                            .Length == 0)
                        continue;
                    _flowEquipDone = true;
                    _ = SendZone(ZoneItemActions.EquipRequest(
                        it.Bag, it.Slot, def.EquipLoc,
                        PlayerEquipment.Worn));
                    _ = SendZone(new ZoneUpdateEquipment
                    {
                        UpdateFlags =
                            ZoneItemActions.LocMask(def.EquipLoc),
                    });
                    GD.Print($"[flow-test] équipe {it.ItemId} " +
                             $"(sac{it.Bag},slot{it.Slot}) " +
                             $"loc={def.EquipLoc}");
                    break;
                }
            }
        });
        Bind(new ZoneEnterNodeNodeData(), OnNodeData);
        // Extension Eden : stats avancées (32001 → 32002).
        Bind(new ZoneAdvStatsInfo(), EdenClient.Hud.AdvancedStatsWindow.Apply);
        EdenClient.Hud.AdvancedStatsWindow.Request = () => _ = s.SendZoneAsync(
            new ZoneAdvStatsQuery(), CancellationToken.None);
        Bind(new ZoneAttrRefresh(), m =>
        {
            // 96B stats self — Fields15to18[1] = MaxMP (ProfileStat-
            // Emitter.cs serveur : [i32 0][i32 MaxMP] en 2e bloc i32).
            _maxMp = m.Fields15to18[1];
            _hud?.SetVitals(_hp, _maxHp, _mp, _maxMp);
            PlayerVitals.ApplyAttrRefresh(m);   // stats totales → profil
        });

        // Vitaux self via 280 — attr 0=HP cur, 1=MP cur,
        // 0x11=MaxHP, 0x12=MaxMP (ZoneEntryFlow.Combat.cs serveur).
        Bind(new ZoneUpdateAttr(), m =>
        {
            _registry!.HandleUpdateAttr(m);
            if (m.Handle == _selfCharId)
            {
                switch (m.AttrId)
                {
                    case 0: _hp = m.Current; break;
                    case 1: _mp = m.Current; break;
                    case 0x11: _maxHp = m.Max; break;
                    case 0x12: _maxMp = m.Current; break;
                }
                _hud?.SetVitals(_hp, _maxHp, _mp, _maxMp);
                PlayerVitals.ApplyUpdateAttr(m);   // or/XP/vitaux → HUD
            }
        });
        Bind(new ZoneUpdateAttrNewExp(), m =>
        {
            _hud?.SetExp(m.Exp);
            PlayerVitals.ApplyExp(m.Exp);
        });

        // Parseurs « complétude » (170+ NCIDs portés des codecs
        // serveur) : tout arrive désérialisé — ce hook applique les
        // états directs (vitals, entités, chat monde, monnaies…).
        MiscPackets.Received += OnMiscPacket;

        // Chat + textes système (ZoneHudMessages.cs).
        Bind(new ZoneClientSay(), m =>
        {
            // Bulle 3D au-dessus de l'émetteur (spec shot_0082) ; texte
            // localisé quand le blob porte un id de dialogue (MonsterSay).
            var text = SceneBubbleLogic.ResolveSayText(m.Message, m.Blob,
                TextResolver.Dialogue);
            var tgt = _registry?.ShowSay(m.CharId, text, _player)
                ?? EntityRegistry.SayTarget.None;
            // PNJ/monstre : bulle seule ; joueur/inconnu : chat aussi.
            if (tgt != EntityRegistry.SayTarget.Npc)
                _hud?.AddChat(0, "", m.Message);
            GD.Print($"[verify] chat 268 : say «{text}» → {tgt}");
        });
        Bind(new ZoneClientChannelMsg(), m =>
        {
            _hud?.AddChat(m.Channel, m.Sender, m.Message);
            GD.Print($"[verify] chat 327 : ch={m.Channel} " +
                     $"de=«{m.Sender}» «{m.Message}»");
        });
        Bind(new ZoneClientTextOutput(), m =>
            _hud?.AddChat(7, "", m.Text));
        // 316 : index t_textindex.ini + paramètre %s1 (ex. 1524 « You
        // have learned skill "%s1". ») ; texte brut si index inconnu.
        Bind(new ZoneClientTextOutputIndex(), m =>
        {
            // World boss / mutants (plan 12) : index natifs 6044-6053 +
            // 1647 et extensions 30001-30003 résolus FR/EN côté client.
            var txt = WorldBossText.Compose(m.Index, m.Text,
                TextResolver.MonsterNameShort, TextResolver.SpellName,
                (i, a) => TextResolver.TextIndex(i, a),
                UiTextEn.Translate) ?? m.Text;
            _hud?.AddChat(7, "", txt);
            if (WorldBossText.IsAnnouncement(m.Index))
                _hud?.ShowScreenMessage(txt);
            // Ref NouveauSkillApresLevelUp : le sort appris s'affiche
            // aussi en gros texte blanc centré (« You have learned
            // skill Ambush. »).
            if (m.Index == 1524)
                _hud?.Banner?.ShowNotice(
                    TextResolver.TextIndex(2014, m.Text) ?? txt);
        });
        Bind(new ZoneClientScreenMessage(), m =>
            _hud?.ShowScreenMessage(m.Text));

        // Quêtes : registre complet (348, réponse au 50 du burst) +
        // updates unitaires (391) — relayés au tracker HUD.
        Bind(new ZoneMissionStates(), m =>
        {
            _hud?.SetMissions(m);
            GD.Print($"[verify] quêtes : {m.InProgress.Count} en " +
                     $"cours / {m.Fms.Count} états dans le tracker " +
                     "(348)");
        });
        Bind(new ZoneUpdateMissionState(), m =>
        {
            _hud?.UpdateMission(m);
            GD.Print($"[verify] quête update : mid={m.MissionId} " +
                     $"key=0x{m.Key:x} state={m.State} (391)");
        });

        // Dialogue NPC (285, réponse au TalkToNpc 10) — texte résolu
        // via t_dialogue.ini ; options relayées à DialogueState pour
        // la fenêtre HUD (réponse = NCID 11 index).
        Bind(new ZoneDisplayDialogView(), m =>
        {
            DialogueState.OnDialogView(m);
            GD.Print($"[live] DialogView ctx={m.ContextId} " +
                     $"«{m.ContextText}» opts={m.Entries.Count} " +
                     $"npc={m.NpcUid} type={m.ViewType}");
            foreach (var e in m.Entries)
                GD.Print($"[live]   opt #{e.DisplayId} «{e.Label}» f={e.Flag}");
            GD.Print($"[verify] dialogue : npc={m.NpcUid} " +
                     $"type=0x{m.ViewType:x2} opts={m.Entries.Count}");
            // --flow-test : on clique la 1re option (index renvoyé
            // au 11) — une fois par NPC pour éviter les boucles de
            // gossip. Une vue « liste de missions » (type 0xff) mène
            // à la fiche quête (388) → accept (87) ; les options de
            // service ouvrent shop/transport/banque.
            if (_flowTest && m.Entries.Count > 0 &&
                _flowReplied.Add(m.NpcUid))
            {
                _ = SendDialogReplyAsync(0);
                GD.Print($"[flow-test] ReplyDialog(0) npc={m.NpcUid} " +
                         $"type=0x{m.ViewType:x2}");
            }
        });
        // 388 EnterMissionList — --flow-test : une fiche d'offre
        // (op=1) → 87 MissionListOp accept.
        Bind(new ZoneEnterMissionList(), m =>
        {
            GD.Print($"[verify] fiche-quête : op={m.Op} mid={m.Arg} " +
                     $"npc={m.NpcUid}");
            if (_flowTest && m.Op is MissionSheetOps.Open
                    or MissionSheetOps.Reward)
            {
                var op = m.Op == MissionSheetOps.Open
                    ? MissionListOps.Accept : MissionListOps.Reward;
                _ = SendZone(new ZoneMissionListOp
                {
                    Field1 = op,
                    Field2 = m.Arg,
                    Field3 = (ushort)(m.NpcUid > 0 ? m.NpcUid : 0xFFFF),
                });
                GD.Print($"[flow-test] MissionListOp op={op} " +
                         $"mid={m.Arg} npc={m.NpcUid}");
            }
        });

        // Stats complémentaires : 492 base, 493 flottantes, 328 modif
        // — alimentent PlayerVitals (lecture HUD profil).
        Bind(new ZoneBaseAttrRefresh(), m =>
            PlayerVitals.ApplyBaseAttrRefresh(m));
        Bind(new ZoneAttrLevelRefresh(), m =>
            PlayerVitals.ApplyAttrLevelRefresh(m));
        Bind(new ZoneModifyAttr(), m =>
        {
            if (m.Handle == _selfCharId)
                PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
                {
                    Handle = m.Handle, AttrId = m.AttrId,
                    Current = m.Value, Max = m.Value64,
                });
        });

        // Mort/respawn (305/306) — self → PlayerVitals ; log sinon.
        Bind(new ZoneDie(), m =>
        {
            PlayerVitals.ApplyDie(m, _selfCharId);
            GD.Print($"[live] Die h=0x{m.Handle:x8} other=0x{m.Other:x8}");
        });
        Bind(new ZoneRevive(), m =>
        {
            PlayerVitals.ApplyRevive(m, _selfCharId);
            // Le MarkDying self coupe AutoLocomotion : le rallumer au revive.
            if (m.Handle == _selfCharId && _player?.Animator is { } pa)
                pa.AutoLocomotion = true;
            GD.Print($"[live] Revive h=0x{m.Handle:x8} type={m.ReviveType}");
        });

        // Équipement porté : 282 (réponse au C→S 8, état complet) et
        // 276 (delta broadcast vue) — PlayerEquipment pour profil/apparence.
        Bind(new ZoneUpdateEquipmentView(), m =>
        {
            PlayerEquipment.ApplyUpdateEquipment(m, _selfCharId);
            GD.Print($"[live] UpdateEquipment char=0x{m.CharId:x8} " +
                     $"mask=0x{m.LocMask:x8} items={m.Items.Count}");
            if (m.CharId == _selfCharId)
                GD.Print($"[verify] équipement : 282 reçu " +
                         $"mask=0x{m.LocMask:x8} {m.Items.Count} items " +
                         "portés");
        });
        Bind(new ZoneChangeEquip(), m =>
        {
            PlayerEquipment.ApplyChangeEquip(m, _selfCharId);
            GD.Print($"[live] ChangeEquip char=0x{m.CharId:x8} " +
                     $"slot={m.Slot} item={m.ItemId} cont={m.Container}");
        });

        // Services PNJ : boutique (304), transport (393), banque
        // (357 + contenu 358) — états pour les fenêtres HUD.
        Bind(new ZoneEnterShop(), m =>
        {
            ShopState.OnEnterShop(m);
            GD.Print($"[live] EnterShop npc={m.NpcUid} store={m.StoreId} " +
                     $"→ {ShopState.Entries.Count} articles");
            GD.Print($"[verify] shop : npc={m.NpcUid} " +
                     $"store={m.StoreId} {ShopState.Entries.Count} " +
                     "articles");
            // --flow-test : achat du 1er article (NCID 22 —
            // vérification BuyUp/inventaire par les trames qui
            // suivent).
            if (_flowTest && ShopState.Entries.Count > 0)
            {
                var e0 = ShopState.Entries[0];
                _ = BuyShopItemAsync(e0.Index, 1);
                GD.Print($"[flow-test] BuyItems slot={e0.Index} " +
                         $"«{e0.Name}» {e0.Price}g");
            }
        });
        Bind(new ZoneBuyUp(), m => GD.Print("[live] BuyUp (achat ack)"));
        Bind(new ZoneEnterTransport(), m =>
        {
            TransportState.OnEnterTransport(m);
            GD.Print($"[live] EnterTransport npc={m.NpcUid} " +
                     $"routes={m.Routes.Count}");
            GD.Print($"[verify] transport : npc={m.NpcUid} " +
                     $"routes={m.Routes.Count} → " +
                     string.Join(", ",
                         TransportState.Routes.Select(
                             x => x.Destination)));
        });
        Bind(new ZoneEnterStorage(), m =>
        {
            StorageState.OnEnterStorage(m);
            GD.Print($"[live] EnterStorage npc={m.NpcUid} — demande 62");
            GD.Print($"[verify] banque : npc={m.NpcUid} ouverte " +
                     "(357), contenu demandé (62)");
            // Flux natif : le client demande le contenu au serveur
            // (62 GetStorageData [s16 -1]) après l'ouverture.
            var send = _session!.SendZoneAsync(
                new ZoneGetStorageData { Value = -1 }, _liveCts.Token);
            _ = send.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        });
        Bind(new ZoneClientGetStorageData(), m =>
        {
            StorageState.OnStorageData(m);
            GD.Print($"[live] GetStorageData type={m.DataType} " +
                     $"tabs={m.Containers.Count} items={StorageState.TotalItems}");
            GD.Print($"[verify] banque : {m.Containers.Count} onglets " +
                     $"{StorageState.TotalItems} items (358)");
        });
        // 373 — résultat d'extension banque (0 ok/1 cap/2 or) ;
        // au succès le 358 suivant porte la capacité agrandie —
        // on la redemande (62) pour repeindre les onglets.
        Bind(new ZoneStorageExpandResult(), m =>
        {
            GD.Print($"[live] StorageExpandResult={m.Result} " +
                     "(0 ok / 1 cap / 2 or)");
            if (!m.Ok) return;
            GD.Print("[verify] banque : extension +20 acceptée " +
                     "— contenu re-demandé (62)");
            var send = _session!.SendZoneAsync(
                new ZoneGetStorageData { Value = -1 }, _liveCts.Token);
            _ = send.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        });

        // Coffre de famille (service NPC 19 « family_storage ») :
        // 507 ouvre, 508 = un coffre complet, 505/504 deltas pushés
        // aux membres en ligne, 506 journal d'audit.
        Bind(new ZoneEnterFamilyStorage(), m =>
        {
            FamilyStorageState.OnEnter(m);
            GD.Print($"[live] EnterFamilyStorage npc={m.NpcUid}");
            GD.Print($"[verify] coffre famille : ouvert (507), " +
                     "contenus 508 attendus");
        });
        Bind(new ZoneFamilyStorageData(), m =>
        {
            FamilyStorageState.OnData(m);
            GD.Print($"[live] FamilyStorageData chest={m.StorageIndex} " +
                     $"items={m.Items.Count}");
            GD.Print($"[verify] coffre famille : chest " +
                     $"{m.StorageIndex} → {m.Items.Count} items (508)");
        });
        Bind(new ZoneFamilyStoragePutNotify(), m =>
        {
            FamilyStorageState.OnPut(m);
            GD.Print($"[live] FamilyStoragePut chest={m.StorageIndex} " +
                     $"loc={m.Loc} item={m.Item?.ItemId ?? 0}");
        });
        Bind(new ZoneFamilyStorageDelete(), m =>
        {
            FamilyStorageState.OnDelete(m);
            GD.Print($"[live] FamilyStorageDelete chest=" +
                     $"{m.StorageIndex} loc={m.Loc}");
        });
        Bind(new ZoneFamilyStorageLog(), m =>
        {
            FamilyStorageState.OnLog(m);
            GD.Print($"[live] FamilyStorageLog {m.Entries.Count} " +
                     "entrée(s)");
        });

        // Courrier (service NPC 7 → 399 ; la zone relaie la mission
        // en interne — pas de connexion mission directe côté client).
        // Réponses mission 68-77 traduites en 400-409 par la zone.
        Bind(new ZoneMailOpenBoxNotify(), m =>
        {
            MailState.OnOpenBox(m.NpcUid);
            GD.Print($"[live] MailOpenBox npc={m.NpcUid}");
            GD.Print("[verify] courrier : boîte ouverte (399)");
        });
        Bind(new ZoneMailList(), m =>
        {
            MailState.OnMailList(m);
            GD.Print($"[live] MailList {m.Entries.Count} courrier(s)");
            GD.Print($"[verify] courrier : {m.Entries.Count} " +
                     "courrier(s) listés (400)");
        });
        Bind(new ZoneMailReadResult(), m =>
            GD.Print($"[live] MailRead res={m.Result}"));
        Bind(new ZoneMailDeleteResult(), m =>
        {
            MailState.OnDeleteResult(m);
            GD.Print($"[live] MailDelete res={m.Result} id={m.MailId}");
        });
        Bind(new ZoneMailSendResult(), m =>
        {
            MailState.OnSendResult(m);
            GD.Print($"[live] MailSend res={m.Result} " +
                     (m.Result == 0 ? "(sent)" : "(declined)"));
        });
        Bind(new ZoneMailReceive(), m =>
        {
            MailState.OnReceive();
            GD.Print("[live] MailReceive — nouveau courrier");
        });
        Bind(new ZoneMailGetItemResult(), m =>
        {
            MailState.OnGetItemResult(m);
            GD.Print($"[live] MailGetItem res={m.Result}");
        });
        Bind(new ZoneMailGetGoldResult(), m =>
        {
            MailState.OnGetGoldResult(m);
            GD.Print($"[live] MailGetGold res={m.Result}");
        });
        Bind(new ZoneMailReturnResult(), m =>
        {
            MailState.OnReturnResult(m);
            GD.Print($"[live] MailReturn res={m.Result}");
        });

        // Hôtel des ventes (service NPC 18 → 430 ; 100% zone-local,
        // livraison des items par courrier).
        Bind(new ZoneAuctionHouse(), m =>
        {
            AuctionState.OnOpenHouse(m.NpcUid);
            GD.Print($"[live] AuctionHouse npc={m.NpcUid}");
            GD.Print("[verify] enchères : hôtel ouvert (430)");
        });
        Bind(new ZoneAuctionBuyList(), m =>
        {
            AuctionState.OnBuyList(m);
            GD.Print($"[live] AuctionBuyList {m.Entries.Count} " +
                     "annonce(s)");
            GD.Print($"[verify] enchères : {m.Entries.Count} " +
                     "annonce(s) catalogue (425)");
        });
        Bind(new ZoneAuctionBuyOk(), m =>
        {
            AuctionState.OnBuyOk();
            GD.Print("[live] AuctionBuyOK — achat livré par courrier");
        });
        Bind(new ZoneAuctionCancelOk(), m =>
        {
            AuctionState.OnCancelOk();
            GD.Print("[live] AuctionCancelOK — item rendu par courrier");
        });
        Bind(new ZoneAuctionSellList(), m =>
        {
            AuctionState.OnSellList(m);
            GD.Print($"[live] AuctionSellList {m.Entries.Count} " +
                     "annonce(s) à moi");
        });
        Bind(new ZoneAuctionSellResult(), m =>
        {
            AuctionState.OnSellResult(m);
            GD.Print($"[live] AuctionSell res={m.Result}");
        });

        // Entités : spawn / move / despawn / attrs.
        // Pendant une migration asynchrone de map (P0.1), les paquets
        // d'entités de la nouvelle map sont mis en attente dans
        // _deferredDuringMigration jusqu'à ce que le terrain soit prêt.
        void DeferIfMigrating(Action a)
        {
            if (_migratingMap) _deferredDuringMigration.Enqueue(a);
            else a();
        }
        Bind(new ZoneSeeMob(), m =>
            DeferIfMigrating(() => _registry!.HandleSeeMob(m)));
        Bind(new ZoneSeeChar(), m => DeferIfMigrating(() =>
        {
            _registry!.HandleSeeChar(m);
            // 265 self (charId == self) porte Hp/MaxHp/MaxMp réels.
            if (m.CharId == _selfCharId)
            {
                _hp = m.Hp; _maxHp = m.MaxHp; _maxMp = m.MaxMp;
                _hud?.SetVitals(_hp, _maxHp, _mp, _maxMp);
            }
        }));
        Bind(new ZoneMovePrediction(), m =>
            DeferIfMigrating(() => _registry!.HandleMovePrediction(m)));
        Bind(new ZoneMoveChar(), m =>
            DeferIfMigrating(() => _registry!.HandleMoveChar(m)));
        Bind(new ZoneClientGoto(), m => DeferIfMigrating(() =>
        {
            // Self (charge DistanceGoto, recalage serveur) : le serveur
            // a déjà déplacé st.Pos — le client snappe sa position au
            // lieu de marcher (le self n'est jamais dans _entities).
            if (m.Handle == _registry!.SelfCharId && _player != null)
            {
                var dst = SnapToGround(
                    WorldConv.ToGodot(m.X, m.Y, 0f));
                _player.TeleportTo(dst);
                _lastSentPos = dst;
                return;
            }
            _registry!.HandleGoto(m);
        }));
        Bind(new ZoneLifeEradicate(), m =>
            DeferIfMigrating(() => _registry!.HandleLifeEradicate(m)));
        Bind(new ZoneLeaveNode(), m =>
            DeferIfMigrating(() => _registry!.HandleLeaveNode(m)));
        Bind(new ZoneSeeTreasure(), m =>
            DeferIfMigrating(() => _registry!.HandleSeeTreasure(m)));
        Bind(new ZoneTreasureDisappear(), m =>
            DeferIfMigrating(() => _registry!.HandleTreasureDisappear(m)));
        // NB : 280 est bindé plus haut (vitaux HUD + registre) —
        // ne pas le doubler (OnZone accumule les handlers).
        Bind(new ZoneMobTransform(),
            m => _registry!.HandleMobTransform(m));
        Bind(new ZoneDisplayMonster(), m =>
            GD.Print($"[live] DisplayMonster h=0x{m.MobHandle:x8} " +
                     $"owner=0x{m.OwnerHandle:x8}"));
        Bind(new ZoneModelModify(), m =>
            GD.Print($"[live] ModelModify h=0x{m.Handle:x8}"));
        Bind(new ZoneNodeExit(), m =>
        {
            _netStatus = "sortie de node…";
            _registry!.Clear();
            // Fenêtres ouvertes invalidées par la migration —
            // dialogue/shop/transport/banque se referment.
            DialogueState.Close();
            ShopState.Close();
            TransportState.Close();
            StorageState.Close();
            MailState.Close();
            AuctionState.Close();
        });
        Bind(new ZoneUpdateZoneMonster(), m =>
            GD.Print($"[live] UpdateZoneMonster h=0x{m.Handle:x8} " +
                     $"{m.Field2}/{m.Field3} ({m.Field4},{m.Field5})"));

        // Correction self : si le serveur replace notre position
        // (drift > 5 u), on snappe — sinon on ignore (client pred.).
        _registry!.SelfMoved += (ex, ey) =>
        {
            if (_player == null) return;
            var srv = WorldConv.ToGodot(ex, ey, _player.GlobalPosition.Y);
            if ((srv - _player.GlobalPosition).Length() > 5f)
            {
                _player.GlobalPosition = srv;
                _lastSentPos = srv;
            }
        };
    }

    private bool _migratingMap;
    private readonly Queue<Action> _deferredDuringMigration = new();
    private int _migrationGen;
    /// <summary>Dernier 313 reçu PENDANT la migration — prend le pas
    /// sur l'ancre d'arrivée (un //goto/serveur qui replace le joueur
    /// pendant le chargement ne doit pas être écrasé par l'ancre).</summary>
    private (float X, float Y, float Z)? _migrationAnchor;

    /// <summary>313 EnterNodeNodeData — arrivée/migration sur un node :
    /// [s16 nodeId][u8 times][f32 x][f32 y][f32 z]. Recharge la map de
    /// façon asynchrone sous LoadingScreen si le node diffère (P0.1) et
    /// repositionne le joueur.</summary>
    private void OnNodeData(ZoneEnterNodeNodeData m)
    {
        Hud.PortalArrival.Begin();   // plan 3.2.2 : pas de portail avant l'arrivée
        _nodeId = m.Field1;
        Hud.PortalArrival.CurrentNode = _nodeId;
        var nodeMap = $"S{_nodeId:D3}";
        _netStatus = $"zone {nodeMap} ({m.X:F1},{m.Y:F1})";
        _zoneLabel = Data.TextResolver.NodeName(_nodeId) ?? nodeMap;
        _hud?.SetZone(nodeMap, _zoneLabel);
        GD.Print($"[live] NodeData node={_nodeId} times={m.Field2} " +
                 $"pos=({m.X:F1},{m.Y:F1},{m.Z:F1})");

        var geoMap = World.NodeGeometry.Resolve(_nodeId, MapExists) ?? nodeMap;
        if (!string.Equals(geoMap, _mapCode,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!MapExists(geoMap))
            {
                GD.PrintErr($"[live] node {_nodeId} → map {nodeMap} " +
                            "introuvable — map courante conservée");
                RepositionPlayer(m.X, m.Y, m.Z);
            }
            else
            {
                _ = MigrateMapAsync(geoMap, _nodeId, m.X, m.Y, m.Z);
            }
            return;
        }

        // Même node, migration encore en cours : mémoriser — l'ancre
        // de fin l'appliquera (évite d'écraser par la pos d'arrivée).
        if (_migratingMap)
        {
            _migrationAnchor = (m.X, m.Y, m.Z);
            return;
        }
        RepositionPlayer(m.X, m.Y, m.Z);
    }

    private void RepositionPlayer(float ex, float ey, float ez)
    {
        if (_player == null) { Hud.PortalArrival.Arrived(); return; }
        var pos = WorldConv.ToGodot(ex, ey, ez);
        pos = SnapToGround(pos);
        if (_map?.TryGetGodotHeight(pos, out var gy) == true &&
            pos.Y < gy)
            pos.Y = gy;
        _player.GlobalPosition = pos;
        _lastSentPos = pos;
        _haveServerPos = true;
        GD.Print($"[live] reposition eden=({ex:F1},{ey:F1},{ez:F1}) → " +
                 $"godot=({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) " +
                 $"map={_mapCode}");
        Hud.PortalArrival.Arrived();
        Hud.QuestRoute.OnArrived(this, _nodeId);
    }

    /// <summary>Migration asynchrone entre maps (P0.1) : monte
    /// l'overlay LoadingScreen, libère l'ancienne map et les entités
    /// avant de construire la nouvelle sur un worker, puis rejoue les
    /// paquets reçus pendant le chargement et réarme le HUD/streaming.</summary>
    private async Task MigrateMapAsync(string nodeMap, int nodeId,
        float ex, float ey, float ez)
    {
        var gen = ++_migrationGen;
        _migratingMap = true;
        _haveServerPos = false;
        _mapCode = nodeMap;

        Screens.LoadingScreen.EnsureOverlay(GetTree(), nodeId);
        Screens.LoadingScreen.Report(0.10f, $"Chargement de {_zoneLabel}…");

        // C1 : l'ancien MapLoader va être libéré — plus aucun accès sol
        // ni physique joueur tant que la nouvelle map n'est pas posée.
        if (_player != null)
        {
            _player.GroundHeight = null;
            _player.SetPhysicsProcess(false);
        }

        _registry?.Clear();
        if (_mapRoot != null)
        {
            RemoveChild(_mapRoot);
            _mapRoot.QueueFree();
            _mapRoot = null;
        }
        _map?.QueueFree();
        _map = null;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var loader = new MapLoader { SpawnSceneNpcs = !_live };
        loader.LoadProgress += (f, s) =>
            Screens.LoadingScreen.Report(f, s);
        Node3D? root = null;
        try
        {
            root = await Task.Run(() => loader.LoadMap(nodeMap));
        }
        catch (Exception err)
        {
            GD.PrintErr($"[live] LoadMap({nodeMap}) : {err.Message}");
        }

        if (gen != _migrationGen || !IsInsideTree())
        {
            root?.Free();
            loader.Free();
            return;
        }

        if (root != null)
        {
            _map = loader;
            _mapRoot = root;
            AddChild(root);
        }
        HookPlayerGround();
        PushWorldSize();
        _npcDrainBudget = !_live;
        var anchor = _migrationAnchor ?? (ex, ey, ez);
        _migrationAnchor = null;
        RepositionPlayer(anchor.X, anchor.Y, anchor.Z);
        _player?.SetPhysicsProcess(true);
        _hud?.SetZone(nodeMap, _zoneLabel);

        _migratingMap = false;
        // Le reliquat différé est vidé par le drain borné de
        // _Process (prioritaire sur _pending, 8 ms/frame) : les ~300
        // SeeMob empilés pendant le chargement s'étalent au lieu
        // d'un pic de ~1,2 s dans cette frame (mesuré --perf).

        Screens.LoadingScreen.Report(1f, "Entering…");
        Screens.LoadingScreen.Finish();
        GD.Print($"[live] migration {nodeMap} terminée");
    }

    /// <summary>Résolution mob : scene [reborn_monsters] (col 12 =
    /// stem modèle M025…) puis c_monster.ini (code M025010 → stem
    /// m025, convention monsterlist/npclist : code = stem + 2
    /// chiffres de variante). Le nom est anglais via
    /// <see cref="TextResolver"/> (t_monster.ini — le nom Big5 de la
    /// scene/c_monster sert de repli).</summary>
    private (string Model, string Name, int Level)? ResolveMonster(int id)
    {
        var spawn = _map?.Scene?.Monsters
            .FirstOrDefault(mm => mm.MonsterId == id);
        // c_monster d'abord : c'est le gabarit du monstre (le client
        // natif l'affiche par template). La colonne « modèle » de
        // [reborn_monsters] n'est qu'un repère d'éditeur : 2 395 des
        // 3 320 lignes des scènes diffèrent du gabarit (M001/M008
        // génériques pour œufs, coffres, machines, « monstres
        // transparents » d'event…) — l'utiliser en premier donnait
        // aux entités de quête le mauvais modèle.
        var def = MonsterDef(id);
        // EDEN_MONSTER_SCENE_MODEL=1 : ancien ordre (scène d'abord) —
        // capture « avant » pour comparaison.
        if (System.Environment.GetEnvironmentVariable(
                "EDEN_MONSTER_SCENE_MODEL") == "1" &&
            spawn != null && spawn.ModelCode.Length > 0 &&
            AssetCatalog.TryGetGlb(
                $"monster/model/{spawn.ModelCode}", out _))
            return (spawn.ModelCode,
                TextResolver.MonsterName(id) ?? spawn.Name, 0);
        if (def == null)
        {
            // Pas de gabarit : repli sur le code scène s'il résout un
            // GLB (les ini contiennent des typos — S039 « M0M01 »).
            if (spawn != null && spawn.ModelCode.Length > 0 &&
                AssetCatalog.TryGetGlb(
                    $"monster/model/{spawn.ModelCode}", out _))
                return (spawn.ModelCode,
                    TextResolver.MonsterName(id) ?? spawn.Name, 0);
            return null;
        }
        var code = def.ModelCode;
        // Stems candidats — les conventions cohabitent (« M363040 » →
        // M363 ; codes à 5 chars de stem) : premier qui résout.
        foreach (var stem in StemCandidates(code))
            if (stem.Length > 0 &&
                AssetCatalog.TryGetGlb(
                    $"monster/model/{stem}", out _))
                return (stem,
                    TextResolver.MonsterName(id) ?? def.Name,
                    def.Level);
        // Gabarit sans GLB : code scène en dernier recours avant la
        // capsule.
        if (spawn != null && spawn.ModelCode.Length > 0 &&
            AssetCatalog.TryGetGlb(
                $"monster/model/{spawn.ModelCode}", out _))
            return (spawn.ModelCode,
                TextResolver.MonsterName(id) ?? spawn.Name, def.Level);
        // Dernier recours : le code brut (capsule si rien ne résout).
        return (code.Length > 5 ? code[..^2] : code,
            TextResolver.MonsterName(id) ?? def.Name,
            def.Level);
    }

    /// <summary>Candidats de stem GLB pour un code c_monster —
    /// ordre : ^2 (variante 2 chiffres), ^3 (variante 3 chiffres,
    /// convention npclist/partner), code brut.</summary>
    private static IEnumerable<string> StemCandidates(string code)
    {
        if (code.Length > 5) yield return code[..^2];
        if (code.Length > 4) yield return code[..^3];
        yield return code;
    }

    /// <summary>Résolution NPC : scene [npc] (col 5 = stem M014 — le
    /// « motion » de la scène est le stem modèle m014.kfm/m014.glb,
    /// même convention que npclist.ini). Nom EN via
    /// <see cref="TextResolver"/> (t_npc.ini — « Titre\nNom »).</summary>
    private (string Model, string Name)? ResolveNpc(int id)
    {
        var npc = _map?.Scene?.Npcs.FirstOrDefault(n => n.NpcId == id);
        if (npc == null) return null;
        var en = TextResolver.NpcName(id);
        var title = TextResolver.NpcTitle(id);
        var name = en == null ? npc.Name
            : title != null ? $"<{title}>\n{en}"
            : en;
        return (npc.ModelCode, name);
    }

    /// <summary>Réécrit le label d'une entité fraîchement spawnée avec
    /// le nom anglais des tables T_* : le registry préfère le
    /// <c>DisplayName</c> serveur (Big5) quand il est non vide, ce hook
    /// (<see cref="EntityRegistry.Spawned"/>) corrige après coup —
    /// scripts/world/ reste intact. kind 12 = mob (Lv prefix),
    /// 8 = NPC (titre + nom), 0+entityId = trésor (nom d'item).</summary>
    private void RenameEntityEnglish(uint handle, int entityId, byte kind)
    {
        if (_registry == null ||
            !_registry.Entities.TryGetValue(handle, out var e)) return;
        // Elf-pet (entityId fixe −2537) : le label serveur =
        // nom du familier — ne pas le masquer par C_Monster.
        if (e.IsElfPet) return;
        string? label = null;
        if (kind == 12)
        {
            var id = entityId & 0xFFFF;
            var en = TextResolver.MonsterName(id);
            if (en != null)
            {
                var lvl = ResolveMonster(id)?.Level ?? 0;
                label = EdenClient.Data.BreakPointUi.FormatMobLabel(lvl, en);
            }
        }
        else if (kind == 8)
        {
            var id = entityId & 0xFFFF;
            var en = TextResolver.NpcName(id);
            if (en != null)
            {
                var title = TextResolver.NpcTitle(id);
                label = title != null ? $"<{title}>\n{en}" : en;
            }
        }
        else if (kind == 0 && entityId > 0)
        {
            // Trésor au sol : « treasure#id×n » → « Nom EN ×n ».
            var en = TextResolver.ItemName(entityId);
            if (en != null)
            {
                var suffix = e.DisplayName.Contains('×')
                    ? e.DisplayName[(e.DisplayName.IndexOf('×'))..] : "";
                label = en + suffix;
            }
        }
        if (label != null && e.DisplayName != label)
        {
            e.DisplayName = label;
            e.SetLabel(label);
        }
    }

    /// <summary>c_monster.ini chargé à la demande (2,1 Mo — parse
    /// paresseux, thread principal).</summary>
    private MonsterTable.MonsterDef? MonsterDef(int id)
    {
        if (_monsterTable == null)
        {
            var path = AssetCatalog.ResolveRawAsset("data/db/c_monster.ini");
            _monsterTable = path != null
                ? MonsterTable.Load(path)
                : new Dictionary<int, MonsterTable.MonsterDef>();
        }
        return _monsterTable.GetValueOrDefault(id);
    }

    // ------------------------------------------------------------------
    // Envoi MoveSelf (NCID 1) throttlé — wire natif :
    // [s16 count=0][f32 x][f32 y][f32 dstX][f32 dstY][f32 dir][s16 7].

    private double _moveThrottle = 0.1; // ~10 Hz

    /// <summary>Envoie ZoneServerMoveSelf si le joueur a bougé depuis
    /// le dernier envoi et que ≥100 ms se sont écoulées.</summary>
    private void MaybeSendMove(double delta)
    {
        if (_session?.Zone?.IsConnected != true || _player == null ||
            !_haveServerPos)
            return;
        _lastMoveSent += delta;
        if (_lastMoveSent < _moveThrottle) return;

        var pos = _player.GlobalPosition;
        var dist = (pos - _lastSentPos).Length();
        // Immobile + cible nulle → rien à signaler.
        var target = _player.MoveTarget;
        if (dist < 0.05f && target == null) return;
        _lastMoveSent = 0;
        _lastSentPos = pos;

        // Coords réseau Eden : (x, y sol) ; destination = cible
        // point&click ou position courante (arrêt).
        var dst = target ?? pos;
        var dstE = WorldConv.ToEdenGround(dst);
        // Direction : facing du modèle (yaw Godot → dir Eden).
        // Godot forward = (−sin ψ, −cos ψ) en XZ → eden (−sinψ, cosψ).
        var yaw = _player.ModelYaw;
        var dir = EdenClient.Game.EdenDir.FromYaw(yaw);
        var e = WorldConv.ToEdenGround(pos);
        var send = _session.SendZoneAsync(new ZoneMoveSelf
        {
            Tail =
            {
                [0] = e.X, [1] = e.Y,
                [2] = dstE.X, [3] = dstE.Y,
                [4] = dir,
            },
            // Record de direction natif (7 avant, 3/5 côtés, 1 arrière,
            // diagonales 0/2/6/8) : relayé en Flag du 326 → anim des
            // autres clients (docs/ANIMS-DIRECTIONNELLES.md).
            MoveType = (short)_player.MoveRecord,
        }, _liveCts.Token);
        // Erreurs d'envoi (session coupée) : on les observe pour ne pas
        // produire d'exception non gérée.
        _ = send.ContinueWith(t => { _ = t.Exception; },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------

    /// <summary>Vrai si la map a une ressource chargeable (.fsm ou
    /// GLB mono-modèle) — sinon on retombe sur <see cref="DefaultMap"/>
    /// plutôt que de montrer un monde vide.</summary>
    private static bool MapExists(string code) =>
        AssetCatalog.ResolveRawAsset($"map/model/{code}/{code}.fsm")
            != null ||
        AssetCatalog.TryGetGlb($"map/model/{code}/{code}", out _) ||
        // Le node peut n'avoir aucun dossier map/model propre mais une
        // scène qui déclare sa géométrie partagée ([globe_data] col0,
        // « S055.fsm » dans s137.ini) : il charge alors SA scène avec
        // la géométrie déclarée — pas la scène du donneur.
        AssetCatalog.ResolveRawAsset($"data/scene/{code}.ini") is
            { } scIni &&
        SceneTable.GlobeModelName(scIni) is { Length: > 0 } gm &&
        DeclaredGeometryExists(gm);

    private static bool DeclaredGeometryExists(string declared)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(declared);
        return AssetCatalog.ResolveRawAsset(
                $"map/model/{stem}/{stem}.fsm") != null ||
            AssetCatalog.TryGetGlb($"map/model/{stem}/{stem}", out _);
    }

    /// <summary>Recale une position Godot sur le terrain : hauteur
    /// sous le point si une tuile la couvre, sinon centre de la tuile
    /// terrain la plus proche (spawns hors grille, ex. S009).</summary>
    private Vector3 SnapToGround(Vector3 pos)
    {
        if (_map == null) return pos;
        if (_map.TryGetGodotHeight(pos, out var gy))
            // max(z serveur, terrain) : le z serveur est résolu depuis
            // les points de scène — il décrit parfois un sol intérieur
            // (pont, mine) AU-DESSUS du heightfield ; le rabaisser au
            // terrain posait le joueur sous ce sol.
            return new Vector3(pos.X, MathF.Max(pos.Y, gy), pos.Z);
        var fsm = _map.Fsm;
        if (fsm == null || fsm.TileWorldSize <= 0f) return pos;
        var (ex, ey) = WorldConv.ToEdenGround(pos);
        var tw = fsm.TileWorldSize;
        TerrainTile? best = null;
        var bestD = float.MaxValue;
        foreach (var t in fsm.Tiles)
        {
            if (t.Kind != FsmFile.TileKindTerrain) continue;
            var cx = t.OriginX + tw * 0.5f;
            var cy = t.OriginY + tw * 0.5f;
            var d = (cx - ex) * (cx - ex) + (cy - ey) * (cy - ey);
            if (d < bestD) { bestD = d; best = t; }
        }
        if (best == null) return pos;
        var tx = best.OriginX + tw * 0.5f;
        var ty = best.OriginY + tw * 0.5f;
        var tz = best.GetHeightLocal(tx, ty);
        GD.Print($"[GameScene] spawn hors terrain " +
                 $"({ex:F0},{ey:F0}) → tuile {best.TileX},{best.TileY} " +
                 $"({tx:F0},{ty:F0},z={tz:F1})");
        return WorldConv.ToGodot(tx, ty, tz);
    }

    /// <summary>(Re)branche le snap terrain du joueur sur la map
    /// courante.</summary>
    /// <summary>H11 : taille monde réelle → minimap / Region Map.</summary>
    private void PushWorldSize()
    {
        var f = _map?.Fsm;
        if (f != null) _hud?.Minimap?.SetWorldSize(f.Header.WorldSizeX);
    }

    private void HookPlayerGround()
    {
        if (_player == null || _map == null) return;
        // Pas de capture de l'instance : lecture de _map à chaque appel,
        // gardée par IsInstanceValid (C1). Variante y-aware : le y du
        // corps sert de référence au raycast sol — sans heightfield
        // (maps intérieures type mine), le rayon depuis 0 ne trouvait
        // jamais le sol au-dessus et le marcheur restait « hors sol ».
        _player.GroundHeight = (gx, gz) =>
            _map != null && GodotObject.IsInstanceValid(_map) &&
            _player != null && GodotObject.IsInstanceValid(_player) &&
            _map.TryGetWalkableHeight(new Vector3(gx,
                _player.GlobalPosition.Y, gz), out var y)
                ? y : (float?)null;
        _player.Blocked = (gx, gz) =>
            _map != null && GodotObject.IsInstanceValid(_map) &&
            _map.IsNodeBlocked(gx, gz);
        _player.NodeMapForRoute = () =>
            _map != null && GodotObject.IsInstanceValid(_map)
                ? _map.NodeMap : null;
        _player.Planner = (from, to) =>
            _map != null && GodotObject.IsInstanceValid(_map)
                ? EdenClient.Hud.AutoRoute.Plan(_map.NodeMap, from, to)
                : null;
    }

    /// <summary>Charge la map via MapLoader et l'ajoute à la scène ;
    /// conserve la racine retournée (<see cref="_mapRoot"/>) pour
    /// pouvoir la libérer au changement de node.</summary>
    private void LoadMapNode(string code)
    {
        var loader = new MapLoader();
        var root = loader.LoadMap(code);
        _map = loader;
        _mapRoot = root;
        AddChild(root);
    }

    private ulong _lastFrameTickUs;
    private long _heartbeatMs;

    /// <summary>C2 : thread témoin — logue pendant qu'une frame dépasse
    /// 5 s (le thread principal ne peut pas le faire lui-même), avec
    /// l'état de migration.</summary>
    private void StartWatchdog()
    {
        var t = new System.Threading.Thread(() =>
        {
            var warned = false;
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                var last = System.Threading.Interlocked.Read(ref _heartbeatMs);
                if (last == 0) continue;
                var stall = System.Environment.TickCount64 - last;
                if (stall > 5000 && !warned)
                {
                    warned = true;
                    GD.PrintErr($"[watchdog] thread principal bloqué > 5 s " +
                        $"(migrating={_migratingMap} gen={_migrationGen} " +
                        $"map={_mapCode}) — dotnet-stack report -p " +
                        $"{System.Environment.ProcessId}");
                }
                else if (stall < 1000) warned = false;
            }
        }) { IsBackground = true, Name = "eden-watchdog" };
        t.Start();
    }

    private double _hObjs, _hTex;

    /// <summary>--lum-start=S : début (s) des captures de --lum-scan (8 par défaut).</summary>
    private static double LumStart()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--lum-start=") && double.TryParse(a[12..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return 8;
    }
    private static readonly bool s_hitch = OS.GetCmdlineUserArgs().Contains("--hitch");

    public override void _Process(double delta)
    {
        // --hitch : journalise toute image > 40 ms (corrélée aux lignes de log voisines).
        if (s_hitch)
        {
            var objs = Performance.GetMonitor(Performance.Monitor.ObjectCount);
            var tex = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
            var proc = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
            var phys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
            if (delta > 0.040)
                GD.Print($"[hitch] {delta * 1000:F0} ms t={Time.GetTicksMsec()} process={proc:F0}ms phys={phys:F0}ms Δobjets={objs - _hObjs:F0} Δtex={(tex - _hTex) / 1048576:F1}Mo");
            _hObjs = objs; _hTex = tex;
        }
        var nowUs = Time.GetTicksUsec();
        if (_lastFrameTickUs > 0 && nowUs - _lastFrameTickUs > 5_000_000)
        {
            GD.PrintErr($"[watchdog] frame stall {(nowUs - _lastFrameTickUs) / 1000} ms " +
                        $"(map={_mapCode} migrating={_migratingMap} pending={_pending.Count})");
        }
        _lastFrameTickUs = nowUs;
        System.Threading.Interlocked.Exchange(ref _heartbeatMs, System.Environment.TickCount64);

        // Drain des actions postées par les handlers réseau — borné
        // en ms/frame : le burst d'entrée de zone (des centaines de
        // SeeMob/SeeChar → ~4 ms de création d'entité pièce, pic de
        // 1,2 s mesuré au --perf sur migration S002) se répartit sur
        // plusieurs images au lieu de figer le client. Le reliquat
        // différé de migration passe en premier (ordre 298→326
        // préservé vis-à-vis des actions déjà empilées).
        var drainT0 = Time.GetTicksUsec();
        const ulong drainBudgetUs = 8000;
        using (EdenClient.Perf.PerfTrace.Scope("gs.pending"))
        for (;;)
        {
            Action act;
            if (!_migratingMap &&
                _deferredDuringMigration.TryDequeue(out var dact))
                act = dact;
            else if (!_pending.TryDequeue(out var pact))
                break;
            else
                act = pact;
            var actT0 = Time.GetTicksUsec();
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[live] dispatch : {ex}");
            }
            var actMs = (Time.GetTicksUsec() - actT0) / 1000.0;
            if (actMs >= 4.0)
                // Une action seule peut dépasser le budget du drain
                // (ex. construction d'un visuel) — on la nomme pour
                // l'attribuer (closure générée → méthode englobante).
                Perf.PerfTrace.Event(
                    $"act:{act.Method.DeclaringType?.Name}" +
                    $" {actMs:F0}ms");
            // Cap dur : le producteur remplit la file pendant les
            // rafales (kill + loot + attrs) — sans lui le drain suivait
            // toute la rafale en direct (~70 ms de gel).
            if (Time.GetTicksUsec() - drainT0 >= drainBudgetUs) break;
        }
        if (_live) MaybeSendMove(delta);

        // Streaming des corps NPC (mode solo : ~3 corps/frame, borné
        // à 4 ms/frame — P0.4, sans GC.Collect bloquant).
        if (_map != null && _npcDrainBudget)
        {
            var left = _map.DrainNpcBodies(1, 2.0,
                _player?.GlobalPosition);
            if (left == 0)
            {
                _npcDrainBudget = false;
                Perf.PerfTrace.Abs("npc stream : terminé");
            }
        }

        // HUD : position/joueur → minimap ; focus chat → WASD suspendu.
        if (_hud != null)
        {
            if (_player != null)
            {
                var e = WorldConv.ToEdenGround(_player.GlobalPosition);
                _hud.SetSelfPos(e.X, e.Y, _player.ModelYaw);
                // Migration : le freeze voulu  ne doit pas
                // être rallumé ici.
                if (!_migratingMap)
                    _player.SetPhysicsProcess(!_hud.ChatHasFocus);
            }
        }

        // --huddebug : dump des Controls HUD recouvrant l'écran (diag
        // recouvrement du viewport 3D).
        if (_hudDebugFrames > 0 && --_hudDebugFrames == 0)
            DumpHudCoverage();

        // --shot=<path>[:<sec>] : capture d'écran après ~4 s (démo HUD)
        // ou après <sec> secondes (vérifs différées — invasions,
        // cycles d'events de zone).
        if (!_shotArgParsed)
        {
            _shotArgParsed = true;
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--shot"))
                {
                    _shotFrames = 240;
                    var shotP = a.Contains('=')
                        ? a.Split('=', 2)[1] : "/tmp/eden_game.png";
                    var colon = shotP.LastIndexOf(':');
                    if (colon > 0 && int.TryParse(shotP[(colon + 1)..],
                            out var shotSec) && shotSec > 0)
                    {
                        _shotFrames = int.MaxValue;
                        _shotDeadline = DateTime.UtcNow +
                            TimeSpan.FromSeconds(shotSec);
                        shotP = shotP[..colon];
                    }
                    // Un chemin relatif tomberait dans res:// (non
                    // écrivable) — on le résout sur le cwd.
                    _shotPath = System.IO.Path.IsPathRooted(shotP)
                        ? shotP : System.IO.Path.GetFullPath(shotP);
                }
        }
        if (_shotFrames > 0 && (--_shotFrames == 0 ||
            DateTime.UtcNow >= _shotDeadline))
        {
            // Le HUD se charge en async (CEGUI ~1 s) et l'écran de
            // chargement peut encore être en fondu de sortie — si le
            // compte à rebours a couru avant HudReady / avant la fin
            // du fondu, on repousse la capture pour laisser
            // --hud-open/--hud-dump produire leur effet.
            if (_hudReadyAt == DateTime.MaxValue ||
                (DateTime.UtcNow - _hudReadyAt).TotalSeconds < 1.5 ||
                Screens.LoadingScreen.Active != null ||
                // --combat-test : capturer APRÈS l'engage/le cast —
                // la fenêtre de test (mob-test + engage + cast ≈
                // 1-2 min) dépasse le délai standard de 4 s.
                Combat.SkillCast.CombatTestActive ||
                // --mob-test seul : attendre la fin du pilotage
                // (téléportation + attente du sight) pour que la
                // capture cadre le mob, pas le trajet.
                MobTestActive)
            {
                // 150 f (~2,5 s) > timers hud-open (1,0 s) et
                // hud-dump (1,2 s) — ils doivent s'exécuter avant la
                // capture (reparti après un stall de chargement, le
                // temps horloge HudReady est déjà dépassé).
                _shotFrames = 150;
                return;
            }
            // --hud-dump : les timers n'ont pas toujours survécu au
            // stall de chargement — on rejoue le dump ici avant la
            // capture pour garantir la sortie.
            if (OS.GetCmdlineUserArgs().Contains("--hud-dump") ||
                OS.GetCmdlineArgs().Contains("--hud-dump"))
                _hud?.DumpRects();
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture()?.GetImage();
            img?.SavePng(_shotPath);
            GD.Print($"[shot] {_shotPath}");
            GetTree().Quit();
        }

        if (_debugLabel == null) return;
        if (_loadFailed)
        {
            _debugLabel.Text =
                $"LoadMap({_mapCode}) a échoué — voir console";
            return;
        }
        var p = _player?.GlobalPosition ?? Vector3.Zero;
        var fsm = _map?.Fsm;
        var line = _live
            ? $"net={_netStatus}  entités={_registry?.Count ?? 0} " +
              $"node={_nodeId}\n"
            : "";
        _debugLabel.Text =
            $"Eden Limitless — {_mapCode}   fps={Engine.GetFramesPerSecond()}\n" +
            $"pos=({p.X:F1}, {p.Y:F1}, {p.Z:F1})   " +
            $"tuiles={fsm?.Tiles.Count ?? 0} obj={fsm?.SceneObjects.Count ?? 0}\n" +
            line +
            "WASD move · left-click move · right-click orbit · wheel zoom";
    }

    /// <summary>F12 : bascule l'overlay debug (fps/pos/aide).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo &&
            k.Keycode == Key.F12)
        {
            _debug = !_debug;
            if (GetNodeOrNull<CanvasLayer>("DebugUI") is { } ui)
                ui.Visible = _debug;
        }
    }

    public override void _ExitTree()
    {
        _liveCts.Cancel();
        _ = _session?.Zone?.DisposeAsync();
        // Libère les caches statiques de la partie (protos GLB/char,
        // textures, matériaux) — sinon ObjectDB les compte fuités à
        // l'arrêt (mesuré ~12,7k instances + RIDs render associés).
        AssetCatalog.ReleaseCaches();
        CharBodyCache.ReleaseCache();
        ToonMaterializer.ReleaseCache();
        WaterAnimator.ReleaseCaches();
        base._ExitTree();
    }
}
