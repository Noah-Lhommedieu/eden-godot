using EdenClient.Native;
using Godot;

namespace EdenClient.Game;

/// <summary>
/// Composant d'animation pour un visuel d'entité (corps joueur, mob,
/// NPC) : charge <b>paresseusement</b> les anims voulues depuis les .kf
/// d'origine (<see cref="KfAnimation"/>, noms d'os natifs : aucun retarget)
/// et pilote un AnimationPlayer.
///
/// Auto-locomotion : quand <see cref="AutoLocomotion"/> est actif,
/// _Process mesure le déplacement du parent et alterne
/// <see cref="MoveAnim"/>/<see cref="IdleAnim"/> — fonctionne pour le
/// joueur (déplacement direct) comme pour les RemoteEntity
/// (interpolation réseau), sans câblage supplémentaire.
///
/// Les GLB d'anims sont mis en cache par chemin (un MobStem partagé
/// par N mobs n'est lu qu'une fois).
/// </summary>
public partial class EntityAnimator : Node
{
    /// <summary>Anim jouée à l'arrêt.</summary>
    [Export] public string IdleAnim { get; set; } = "stand01";
    /// <summary>Anim jouée en déplacement.</summary>
    [Export] public string MoveAnim { get; set; } = "move01";
    /// <summary>Seuil de vitesse (u/s) bascule stand→move.</summary>
    [Export] public float MoveSpeedThreshold { get; set; } = 0.3f;
    /// <summary>Vrai quand le contrôleur local bouge (input direct —
    /// bascule move immédiate, sans attendre la fenêtre de mesure de
    /// vitesse ; H10) ou qu'une entité distante reçoit un 326 avec
    /// destination (EntityRegistry — même symétrie). Posé par
    /// PlayerController à chaque physique / par trame réseau.</summary>
    public bool InputMoving
    {
        get => _inputMoving;
        set
        {
            _inputMoving = value;
            if (ValidMount() is { } m) m.InputMoving = value;
        }
    }
    private bool _inputMoving;
    /// <summary>Vrai tant que le parent est en l'air (saut local —
    /// posé par PlayerController). En vol stationnaire on tient
    /// <see cref="AirborneAnim"/> au lieu de retomber sur l'idle ;
    /// en vol avec déplacement horizontal, MoveAnim garde la main.</summary>
    public bool Airborne
    {
        get => _airborne;
        set
        {
            _airborne = value;
            if (ValidMount() is { } m)
            {
                m.JumpPriority = true;
                m.Airborne = value;
            }
        }
    }
    private bool _airborne;

    /// <summary>Posé par le cavalier sur l'animator de sa monture : en
    /// l'air l'anim de saut PRIME sur la marche (natif : jump01 = 601
    /// élan, jump02 = 602 vol/chute — motion.ini r001…r999), y compris
    /// pendant un déplacement ; au sol la locomotion reprend.</summary>
    public bool JumpPriority { get; set; }

    /// <summary>Anim tenue en vol : jump02 (chute) quand le modèle l'a,
    /// sinon la pose de saut <see cref="AirborneAnim"/>.</summary>
    private string AirHoldAnim() =>
        Has("jump02") ? "jump02" : AirborneAnim;

    /// <summary>Animator de la monture quand ce corps est un cavalier
    /// : le natif (FUN_007785d0, client_game) dérive l'action cavalier
    /// de l'état de la monture — stand01→4001 (ride01), stand02/03→
    /// ride02/03, move01-04 (101-104)→4004-4007 (ride04-07), jump
    /// (601/602)→4008 (ride08). La locomotion du cavalier suit donc
    /// l'anim COURANTE de la monture au lieu de sa propre mesure de
    /// vitesse (<see cref="RiderAnimFor"/>).</summary>
    public EntityAnimator? RideMount { get; set; }

    private EntityAnimator? ValidMount() =>
        RideMount != null && GodotObject.IsInstanceValid(RideMount)
            ? RideMount : null;

    /// <summary>Anim de déplacement, repli move01 si le modèle n'a pas
    /// la variante directionnelle (natif : HasAction sinon 101).
    /// </summary>
    private string MoveOrFallback() =>
        _player != null && !Has(MoveAnim)
            ? "move01" : MoveAnim;

    /// <summary>Action cavalier pour une anim de monture.</summary>
    public static string RiderAnimFor(string mountAnim) => mountAnim switch
    {
        "move01" => "ride04",
        "move02" => "ride05",
        "move03" => "ride06",
        "move04" => "ride07",
        "jump01" or "jump02" => "ride08",
        "stand02" => "ride02",
        "stand03" => "ride03",
        "stand04" => "ride05",
        "stand05" => "ride06",
        _ => "ride01",
    };

    /// <summary>Cavalier : transpose une anim à pied vers sa variante
    /// montée (attackNN→rattackNN, magicNN→rmagicNN, battleNN→
    /// rbattleNN, jump→ride08) quand elle existe.</summary>
    private string Resolve(string name)
    {
        if (ValidMount() == null || _player == null) return name;
        if (name is "jump01" or "jump02") return "ride08";
        foreach (var pre in new[] { "attack", "magic", "battle", "shoot" })
            if (name.StartsWith(pre) && Has("r" + name))
                return "r" + name;
        return name;
    }
    /// <summary>Anim tenue/rejouée en vol stationnaire (« jump01 » —
    /// non bouclée : Play la relance à sa fin, ce qui revient à
    /// garder la pose de saut tant que le vol dure).</summary>
    [Export] public string AirborneAnim { get; set; } = "jump01";
    /// <summary>Vrai = alterne idle/move selon le déplacement réel du
    /// parent (désactivé pendant un one-shot).</summary>
    [Export] public bool AutoLocomotion { get; set; } = true;
    /// <summary>Fondu de transition (s) — one-shots et PlayLoop.</summary>
    [Export] public float Blend { get; set; } = 0.12f;
    /// <summary>Fondu des bascules stand↔move (s) — réduit vs
    /// <see cref="Blend"/> : la transition locomotion est le geste le
    /// plus fréquent et les 120 ms s'ajoutaient au « run-on » perçu à
    /// l'arrêt (phase5 §4). 0,07 garde un fondu visible sans traînée.</summary>
    [Export] public float LocoBlend { get; set; } = 0.07f;

    private AnimationPlayer? _player;
    private string _current = "";
    private string _oneShot = "";
    private Vector3 _lastPos;
    private bool _hasLast;
    private Node3D? _tracked;
    private bool _warned;

    /// <summary>AnimationPlayer créé sous le modèle (null si aucune
    /// anim n'a pu être chargée — le visuel reste statique).</summary>
    public AnimationPlayer? Player => _player;

    /// <summary>Nom de l'anim en cours ("" si aucune).</summary>
    public string Current => _current;

    /// <summary>Vrai pendant la lecture d'une anim one-shot
    /// (attaque/dégât…).</summary>
    public bool OneShotPlaying => _oneShot.Length > 0;

    // ------------------------------------------------------------------
    // Attachement

    /// <summary>Attache un animator à <paramref name="modelRoot"/> et
    /// charge les anims <paramref name="wanted"/> du GLB
    /// <paramref name="animRelPath"/> (chemin relatif à la racine
    /// d'assets, ex. « anims/monster/animation/M025 »). Retourne null
    /// si le GLB d'anims est absent ou aucun channel ne se résout —
    /// le visuel reste alors statique (fallback T-pose, log WARN).</summary>
    /// <summary>Diag perf : <c>--no-entity-anim</c> shunte
    /// l'attachement (visuels figés) — isole le coût des
    /// AnimationPlayers dans le frame time.</summary>
    private static bool? _animOff;

    public static EntityAnimator? Attach(Node3D modelRoot,
        string animRelPath, IReadOnlySet<string>? wanted = null,
        string? modelKey = null)
    {
        _animOff ??= OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Contains("--no-entity-anim");
        if (_animOff.Value) return null;
        if (AttachNative(modelRoot, animRelPath, modelKey, wanted) is { } nat)
            return nat;
        GD.Print($"[anim] pas de .kf pour «{animRelPath}» — visuel statique");
        return null;
    }

    private static EntityAnimator? AttachNative(Node3D modelRoot,
        string animRelPath, string? modelKey, IReadOnlySet<string>? wanted)
    {
        var set = KfAnimation.GetSet(animRelPath, modelRoot, modelKey);
        if (set == null) return null;            // pas de .kf → repli GLB
        // Idle/locomotion d'abord : la librairie ne doit pas être vide.
        var any = false;
        foreach (var n in new[] { "stand01", "move01" })
            any |= set.Ensure(n);
        if (!any)
        {
            GD.PushWarning($"[anim] {animRelPath}.kf : ni stand01 ni move01");
            return null;
        }
        var animator = new EntityAnimator { Name = KfAnimation.EventNode };
        modelRoot.AddChild(animator);
        var player = new AnimationPlayer
        {
            Name = "EntityAnims",
            RootNode = new NodePath(".."),
        };
        modelRoot.AddChild(player);
        player.AddAnimationLibrary("", set.Lib);
        animator._player = player;
        animator._tracked = modelRoot;
        animator._native = set;
        animator._motions = KfmMotions.For(animRelPath);
        // Attaques/sorts préchauffés (cuisson hors thread, construction étalée) : la 1re attaque ne décode plus.
        if (!Combat.AttackProbe.Legacy) set.WarmAsync(wanted ?? AnimNames.EntityWanted.ToHashSet());
        return animator;
    }

    // ------------------------------------------------------------------
    // Pilotage

    /// <summary>Joue une anim en boucle si disponible (no-op sinon).</summary>
    public void PlayLoop(string name) => Play(name, force: false);

    /// <summary>Coupe net un one-shot d'attaque/sort en cours (cible
    /// morte : plus de coup dans le vide) et rend la main à la
    /// locomotion. No-op si aucun one-shot ou s'il n'est pas offensif
    /// (dégât, mort, saut…).</summary>
    public void CancelAttackOneShot()
    {
        ValidMount()?.CancelAttackOneShot();
        if (_player == null || _oneShot.Length == 0) return;
        var n = _oneShot;
        if (!(n.StartsWith("attack") || n.StartsWith("shoot") ||
              n.StartsWith("magic") || n.StartsWith("rattack") ||
              n.StartsWith("rmagic") || n.StartsWith("rshoot")))
            return;
        _oneShot = "";
        _current = "";
        _player.Stop();
    }

    /// <summary>Action de combat jouée À LA FOIS par le cavalier et par
    /// sa monture de combat. Natif : <c>FUN_00778660</c> (rf_game
    /// l.724328) — pour une monture de combat (flag +0xa8 de la monture)
    /// l'id d'action du sort/de l'attaque (301/309/319/320 magic,
    /// 401/409/412 et 3001+ shoot, 1001/1020-1024 attack) est conservé ;
    /// la monture le joue sur SON .kfm (motion.ini r2xx : attack01-04
    /// 1021-1024, magic01 301, shoot01 401…) et le cavalier joue la
    /// variante montée rattack/rmagic/rshoot (+4000/+7000). Une monture
    /// sans ces séquences (non combat) ignore l'appel (<see cref="Has"/>).
    /// </summary>
    private static bool IsCombatAction(string name) =>
        name.StartsWith("attack") || name.StartsWith("magic") ||
        name.StartsWith("shoot");

    /// <summary>Joue une anim one-shot (attaque, dégât…) puis revient
    /// à la locomotion automatique. Ignorée si l'anim n'existe pas —
    /// et <b>ne relance pas</b> la même anim tant qu'elle joue (un
    /// swing toutes les 1,2 s ne doit pas couper un coup plus long).</summary>
    public void PlayOnce(string name)
    {
        if (ValidMount() is { } m && (name is "jump01" or "jump02" ||
                                      IsCombatAction(name)))
            m.PlayOnce(name);
        name = Resolve(name);
        if (_player == null || !Has(name)) return;
        if (_oneShot == name && _current == name &&
            _player.IsPlaying())
            return;
        _oneShot = name;
        var blend = BlendFor(_current, name, Blend);
        _current = name;
        _player.Play(name, blend);
    }

    /// <summary>Joue la première anim dispo parmi <paramref name="candidates"/>
    /// (one-shot). Retourne le nom joué ou "".</summary>
    public string PlayOnceAny(params string[] candidates)
    {
        foreach (var c in candidates)
            if (_player != null && Has(Resolve(c)))
            {
                PlayOnce(c);
                return Resolve(c);
            }
        return "";
    }

    /// <param name="blend">Fondu de transition explicite (s) —
    /// &lt;0 = <see cref="Blend"/> par défaut.</param>
    private void Play(string name, bool force, float blend = -1f)
    {
        if (_player == null || !Has(name)) return;
        // Ne PAS rappeler Play() si l'anim demandée est déjà la
        // courante ET tourne encore — Play() repart à 0, ce qui
        // produisait le « cancel/restart » à chaque _Process. Une
        // anim non-bouclée terminée (loop absent du GLB) est en
        // revanche rejouée : IsPlaying() false → on relance.
        if (!force && _current == name && _player.IsPlaying()) return;
        var b = blend >= 0f ? blend : Blend;
        if (_motions != null) b = BlendFor(_current, name, b);
        _current = name;
        _player.Play(name, b);
    }

    // ------------------------------------------------------------------
    // Mode natif (EDEN_NATIVE=skin,anim) : .kf lus directement, séquences
    // décodées à la demande, fondus tirés du .kfm, text keys → événement.

    private KfAnimSet? _native;
    private KfmMotions? _motions;

    /// <summary>Animation disponible ? En natif, la séquence du .kf est
    /// décodée et ajoutée à la librairie partagée à la première demande.</summary>
    public bool Has(string name) =>
        _player != null && (_native?.Ensure(name) == true ||
                            _player.HasAnimation(name));

    private float BlendFor(string from, string to, float dflt) =>
        _motions?.BlendSeconds(from, to, dflt) ?? dflt;

    /// <summary>Événement de text key d'une anim native (impact, pas,
    /// son, effet) — appelé par la piste d'appel de l'Animation
    /// (<see cref="KfAnimation.EventMethod"/>).</summary>
    public void OnAnimTextKey(int kind, int index, string key)
    {
        var ev = new AnimTextEvent((AnimEventKind)kind, index, key, _current);
        AnimEventFired?.Invoke(this, ev);
        GlobalAnimEvent?.Invoke(this, ev);
    }

    public readonly record struct AnimTextEvent(AnimEventKind Kind,
        int Index, string Key, string Anim);

    /// <summary>Par instance.</summary>
    public event Action<EntityAnimator, AnimTextEvent>? AnimEventFired;
    /// <summary>Global : point d'accroche audio / CombatFx.</summary>
    public static event Action<EntityAnimator, AnimTextEvent>? GlobalAnimEvent;

    // Hystérésis de la bascule stand↔move : le seuil d'entrée est plus
    // haut que le seuil de sortie et le nouvel état doit tenir
    // <see cref="LocoHoldSeconds"/>. La vitesse est mesurée sur une
    // FENÊTRE glissante (<see cref="SpeedWindowSeconds"/>) et non par
    // frame : à fps(idle) > fps(physique) le déplacement n'arrive
    // qu'une frame sur deux — la mesure instantanée oscillait 0/vmax
    // (d'où le clignotement d'avant ET, avec le hold seul, l'impossi-
    // bilité de jamais basculer en « move » : hold remis à zéro par
    // chaque frame à vitesse nulle — constat diag live spd=14/0/0).
    private bool _locoMoving;
    private float _locoHold;
    private float _smoothSpeed;
    private Vector3 _winFrom;
    private float _winTime;
    private const float LocoHoldSeconds = 0.06f;
    private const float SpeedWindowSeconds = 0.09f;
    // Arrêt franc (phase5 §4) : input relâché ET déplacement ~nul en
    // continu → bascule immédiate, sans la fenêtre de 90 ms ni le
    // hold. 45 ms d'immobilité ≈ 2-3 frames rendu — au-dessus du
    // plus long trou quand les fps rendu dépassent les fps physique
    // (les frames sans déplacement ne s'enchaînent jamais aussi
    // longtemps pendant une marche réelle : un frame animé remet le
    // compteur à 0).
    private float _stillTime;
    /// <summary>Déplacement²/frame sous lequel l'entité est réputée
    /// immobile (~1 mm² — la marche à 6 u/s produit ~0,01 u²/frame à
    /// 60 fps, très au-dessus).</summary>
    private const float StillMoveEpsSq = 1e-6f;
    private const float StillStopSeconds = 0.045f;

    // Diag EDEN_ANIM_DIAG=1 : état du player + pose d'un bone —
    // diagnostic des régressions « perso figé » (l'anim joue-t-elle ?
    // les tracks modifient-elles le squelette ?). Boucle à timers —
    // indépendante de _Process pour mesurer aussi la santé de
    // _Process lui-même.
    private int _diagFrames;
    private static readonly bool DiagEnabled =
        System.Environment.GetEnvironmentVariable("EDEN_ANIM_DIAG")
            == "1";

    // LOD d'animation : au-delà de cette distance caméra le
    // squelette n'est plus lisible (~10 px) — on fige le player
    // (PlaybackActive=false coupe le calcul de pose : ~10 ms/frame
    // regagnés sur S001 à 263 NPC). Re-mesuré toutes les ~24 frames.
    [Export] public float AnimLodDistance { get; set; } = 80f;
    /// <summary>Facteur global sur <see cref="AnimLodDistance"/>
    /// (Settings &gt; Models Low = 0,4 / High = 1).</summary>
    public static float AnimLodScale { get; set; } = 1f;
    private int _lodCounter;
    private bool _lodPaused;

    public override void _Ready()
    {
        if (DiagEnabled) _ = DiagLoop();
    }

    private async System.Threading.Tasks.Task DiagLoop()
    {
        for (var i = 0; i < 40; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.25),
                SceneTreeTimer.SignalName.Timeout);
            var tracked = _tracked;
            if (_player == null || tracked == null ||
                !IsInsideTree())
                return;
            var skel = tracked.FindChildren("*", "Skeleton3D", true,
                false).Cast<Skeleton3D>().FirstOrDefault();
            string pose = "-";
            if (skel != null && skel.GetBoneCount() > 1)
            {
                var b = skel.GetBonePose(1);
                pose = $"{b.Origin:F3}/{b.Basis.GetRotationQuaternion()}";
            }
            var a = _player.HasAnimation(_current)
                ? _player.GetAnimation(_current) : null;
            GD.Print($"[animdiag] t{_diagFrames} cur='{_current}' " +
                $"one='{_oneShot}' playing={_player.IsPlaying()} " +
                $"assigned='{_player.AssignedAnimation}' " +
                $"pos={_player.CurrentAnimationPosition:F2} " +
                $"len={(a != null ? a.Length : -1):F2} " +
                $"tracks={(a != null ? a.GetTrackCount() : -1)} " +
                $"loco={_locoMoving} spd={_diagSpeed:F2} " +
                $"gpos={tracked.GlobalPosition:F2} " +
                $"bone1={pose} ({tracked.Name})");
        }
    }

    private float _diagSpeed;

    /// <summary>Tick autonome (joueur local, avatars des écrans).
    /// Sous <c>EntityRegistry</c> le process est désactivé et la
    /// registry appelle <see cref="Tick"/> — un seul callback
    /// natif→C# par frame pour ~260 animators.</summary>
    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        if (_native is { WarmPending: true } warm) warm.PumpWarm();
        var tracked = _tracked;
        if (tracked == null || _player == null) return;

        // LOD : distance caméra mesurée à bas bruit (toutes les 24
        // frames — pas à chaque frame, le GlobalPosition lui-même
        // coûte). Entité hors portée → squelette figé, _Process
        // réduit au compteur.
        if (++_lodCounter >= 24)
        {
            _lodCounter = 0;
            var cam = GetViewport().GetCamera3D();
            if (cam != null)
            {
                var lod = AnimLodDistance * AnimLodScale;
                var far = cam.GlobalPosition.DistanceSquaredTo(
                    tracked.GlobalPosition) > lod * lod;
                if (far != _lodPaused)
                {
                    _lodPaused = far;
                    _player.PlaybackActive = !far;
                }
            }
        }
        if (_lodPaused) return;

        if (DiagEnabled && ++_diagFrames % 30 == 0)
        {
            var skel = tracked.FindChildren("*", "Skeleton3D", true,
                false).Cast<Skeleton3D>().FirstOrDefault();
            string pose = "-";
            if (skel != null && skel.GetBoneCount() > 1)
            {
                var b = skel.GetBonePose(1);
                pose = $"rot={b.Basis.GetRotationQuaternion()}";
            }
            var a = _player.HasAnimation(_current)
                ? _player.GetAnimation(_current) : null;
            GD.Print($"[animdiag] cur='{_current}' one='{_oneShot}' " +
                $"playing={_player.IsPlaying()} " +
                $"assigned='{_player.AssignedAnimation}' " +
                $"pos={_player.CurrentAnimationPosition:F2} " +
                $"len={(a != null ? a.Length : -1):F2} " +
                $"tracks={(a != null ? a.GetTrackCount() : -1)} " +
                $"root='{_player.RootNode}' bone1={pose}");
        }

        if (AutoLocomotion && !OneShotPlaying)
        {
            var pos = tracked.GlobalPosition;
            var prev = _hasLast ? _lastPos : pos;
            if (!_hasLast) { _winFrom = pos; _winTime = 0f; }
            _lastPos = pos;
            _hasLast = true;
            _winTime += (float)delta;
            if (_winTime >= SpeedWindowSeconds)
            {
                _smoothSpeed = (pos - _winFrom).Length() / _winTime;
                _winFrom = pos;
                _winTime = 0f;
            }
            if (DiagEnabled) _diagSpeed = _smoothSpeed;
            // Le contrôleur local connaît l'input avant toute mesure
            // physique : sa transition est immédiate. Le lissage et le
            // hold sont réservés aux entités interpolées à distance.
            if (InputMoving)
            {
                _locoMoving = true;
                _locoHold = 0f;
                _stillTime = 0f;
            }
            else
            {
                // Arrêt franc (phase5 §4) : l'input est coupé ET le
                // déplacement mesuré est ~nul en continu depuis ≥
                // StillStopSeconds → _locoMoving=false tout de suite.
                // Le joueur local est kinematic : il s'arrête net à la
                // frame du relâchement, la fenêtre+hold ne servaient
                // qu'à temporiser un bruit de mesure qui n'existe pas
                // dans ce cas. Un remote qui glisse encore vers sa
                // dernière destination (interpolation) garde un
                // déplacement > 0 → _stillTime reste à 0 et le chemin
                // fenêtre+hold gère la décélération réelle.
                _stillTime =
                    (pos - prev).LengthSquared() < StillMoveEpsSq
                        ? _stillTime + (float)delta : 0f;
                if (_locoMoving && _stillTime >= StillStopSeconds)
                {
                    _locoMoving = false;
                    _locoHold = 0f;
                }
                else
                {
                    var wantMove = _locoMoving
                        ? _smoothSpeed > MoveSpeedThreshold * 0.5f
                        : _smoothSpeed > MoveSpeedThreshold;
                    if (wantMove != _locoMoving)
                    {
                        _locoHold += (float)delta;
                        if (_locoHold >= LocoHoldSeconds)
                        {
                            _locoMoving = wantMove;
                            _locoHold = 0f;
                        }
                    }
                    else
                    {
                        _locoHold = 0f;
                    }
                }
            }
            // En vol stationnaire (saut local) on tient la pose de
            // saut ; en vol avec déplacement, la marche garde la main.
            if (ValidMount() is { } mount)
                Play(RiderAnimFor(mount.Current), force: false,
                    blend: LocoBlend);
            else if (!(JumpPriority && Airborne && _current == "jump02" &&
                       !_player.IsPlaying()))
                // (jump02 terminée en vol : pose de chute tenue)
                Play(JumpPriority && Airborne ? AirHoldAnim()
                    : _locoMoving ? MoveOrFallback()
                    : Airborne ? AirborneAnim
                    : IdleAnim, force: false, blend: LocoBlend);
        }
        else
        {
            _lastPos = tracked.GlobalPosition;
            _winFrom = _lastPos;
            _winTime = 0f;
            _stillTime = 0f;
            _hasLast = true;
        }

        if (OneShotPlaying && !_player.IsPlaying())
        {
            _oneShot = "";
            _current = "";
            if (!_warned && _player.GetAnimationList().Length == 0)
                _warned = true;
        }
    }

    // ------------------------------------------------------------------
    // Préchargement

    /// <summary>Précharge en parallèle les .kf des stems listés : ouvre
    /// l'index et cuit les anims voulues — pur C#, hors thread principal.
    /// À appeler pendant le chargement map avant les Attach.</summary>
    public static int PreloadAnimFiles(IEnumerable<string> relPaths,
        IReadOnlySet<string>? wanted = null)
    {
        wanted ??= new HashSet<string>(AnimNames.EntityWanted);
        var distinct = relPaths.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        System.Threading.Tasks.Parallel.ForEach(distinct,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(
                System.Environment.ProcessorCount - 2, 2, 8) },
            p =>
            {
                var src = KfSource.Get(p);
                if (src == null) return;
                foreach (var n in wanted) src.Baked(n);
            });
        return distinct.Length;
    }
}
