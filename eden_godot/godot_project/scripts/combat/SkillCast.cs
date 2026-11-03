using Godot;
using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Orchestrateur de lancement de sorts (lot S1) — nœud monde posé par
/// <see cref="Hud.HudExtensions"/> sous la GameScene.
///
/// Émission : <see cref="Cast"/> envoie
///   - NCID 9 <see cref="ZoneSpellCastToLife"/> quand une cible est
///     sélectionnée (<see cref="Hud.TargetFrame.TargetHandle"/>) ;
///   - NCID 17 <see cref="ZoneSpellCastToNowhere"/> sinon (buff/sort
///     sans cible — direction = facing du perso, même formule que
///     <c>PlayerController.EndSteer</c>).
///   Sorts de zone : NCID 16 <see cref="ZoneSpellCastToLocation"/>
///   via <see cref="CastAt"/>.
///
/// Réception (thread réseau → file) :
///   - 279 <see cref="ZoneClientSpellCastOK"/> → démarre la
///     <see cref="CastBar"/> (durée = castDs/10 s) ; ok=0 = refus ;
///   - 287 <see cref="ZoneClientSpellBreak"/> → cast interrompu ;
///   - 410 <see cref="ZoneClientCommandFailReturn"/> → refus sec
///     (cooldown/silence/MP…) : cast annulé + message chat ;
///   - 281 <see cref="ZoneClientSpellHitToLife"/> → clôt le cast self.
///
/// Session : <see cref="SessionCarry.Session"/> (flux écrans — même
/// limite documentée qu'AutoAttack : en --live direct le binding est
/// absent et <see cref="Cast"/> retombe sur l'envoi direct).
/// </summary>
public partial class SkillCast : Node
{
    /// <summary>Instance courante (posée à l'entrée dans l'arbre —
    /// consommée par <see cref="Hud.SkillBarWindow"/>).</summary>
    public static SkillCast? Instance { get; private set; }

    /// <summary>Émis quand l'état de cast change (début/fin/échec) —
    /// thread principal.</summary>
    public event Action? CastStateChanged;

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action>
        _pending = new();
    private EdenSession? _session;
    private PlayerController? _player;
    private bool _playerSearched;
    private readonly CancellationTokenSource _cts = new();
    private bool _combatTest;   // --combat-test : cast auto sur le 1er mob
    private int _cloneMobId;    // --clone-mob=<id> : //clone_monster d'abord
    private int _forcedSpell;   // --cast-spell=<id> : 1er cast forcé du combat-test
    private bool _castNoEngage; // --cast-no-engage : cast sans auto-attack (prouve la poursuite)
    private bool _castNear;     // --cast-near : cible la plus proche (preuve debuff/icône, pas de poursuite)
    private bool _lockTest;     // --lock-test : //kill pendant le vol du sort, puis 2ᵉ cast SANS libération manuelle
    private int _lockSpell;
    private int _lockPhase;
    private double _lockTimer;
    private bool _castNowhere;  // --cast-nowhere : pas de sélection → le cast part en NCID 17
    private int _castBreakMs;   // --cast-break=<ms> : Break() (NCID 13) en vol après le 279
    private ulong _breakAt;     // tick d'envoi du break (0 = inactif)
    private bool _cloneSent;
    private readonly List<int> _testSpellQueue = new(); // casts suivants du combat-test
    private int _testCast2;      // candidat en vol (0 = aucun)
    private bool _testGot279;    // 279 reçu pour le candidat courant
    private double _testTimer;
    private int _testStep;
    private EntityRegistry? _registry;
    private string? _castAnim;   // (hérité) — plus de rejeu : l'incantation tient la pose, le blast joue le clip
    private CastCircle? _circle;  // cercle magique au sol pendant l'incantation temporisée
    private ulong _rawAnimDeadline;   // repli gestuelle brute si aucun blast n'arrive (sorts instantanés)
    private int _rawAnimSpell;
    private GroundTargeter? _targeter;   // visée au sol (sorts Ground)
    // --zshot-times=0.5,2,4 --zshot-dir=<dossier> : captures du viewport à t+N s après la demande de cast
    // (preuve visuelle du cercle / de la pose d'incantation / de l'impact).
    private readonly List<double> _shotTimes = new();
    private string _shotDir = "";
    private ulong _shotT0;
    private int _shotNext;
    private string _shotPrefix = "cast_";
    // --ground-demo=<spellId> : après l'entrée en jeu, ouvre la visée au sol (centre écran), capture, puis valide.
    private int _groundDemo;
    private ulong _groundDemoAt;   // 0 = pas armé ; tick de départ
    private bool _groundDemoConfirmed;
    private bool _autoGround;            // --ground-auto : pas de visée, point devant/sous la cible (tests)
    // Chien de garde du verrou de cast : le serveur peut ne jamais
    // répondre (cible morte avant l'impact, TP en plein cast, paquet
    // perdu) ; sans échéance CastingSpell restait posé et plus aucun
    // sort ne partait (« impossible de lancer des sorts après un
    // combat / un tp »).
    private readonly CastLock _lock = new();
    private bool _castAcked;        // 279 OK reçu pour le cast courant
    private bool _flagCasting;      // 302 self : bit 0x2000 (action) posé
    // Poursuite avant cast (natif : le client connaît la portée et
    // approche la cible AVANT d'envoyer le 9 — pas de gate distance
    // serveur à 60 u près).
    private int _chaseSpell;        // sort en attente d'approche
    private uint _chaseTarget;      // handle de la cible poursuivie
    private float _chaseReach;      // portée d'arrêt (u)
    private double _rechaseTimer;   // re-ciblage périodique

    /// <summary>Re-ciblage de la poursuite de cast (s) — la cible
    /// peut bouger.</summary>
    [Export] public float RechaseInterval { get; set; } = 0.30f;

    /// <summary>SpellId du cast en cours (0 = aucun).</summary>
    public int CastingSpell { get; private set; }
    /// <summary>Cible du cast en cours.</summary>
    public uint CastingTarget { get; private set; }
    /// <summary>true pendant qu'un cast est en cours (279 reçu, pas
    /// encore résolu/interrompu).</summary>
    public bool IsCasting => CastingSpell != 0;
    /// <summary>Vrai tant que le pilote --combat-test n'a pas terminé
    /// — <c>--shot</c> attend sa fin pour capturer le combat.</summary>
    public static bool CombatTestActive { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        if (KillLoopProbe.Requested(out _, out _))
            AddChild(new KillLoopProbe { Name = "KillLoopProbe" });
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
            if (a == "--combat-test")
            {
                _combatTest = true;
                CombatTestActive = true;
            }
            else if (a.StartsWith("--clone-mob=") &&
                     int.TryParse(a.Split('=', 2)[1], out var cm))
                _cloneMobId = cm;
            else if (a.StartsWith("--cast-spell=") &&
                     int.TryParse(a.Split('=', 2)[1], out var cs))
                _forcedSpell = cs;
            else if (a == "--lock-test")
                _lockTest = true;
            else if (a == "--cast-no-engage")
                _castNoEngage = true;
            else if (a == "--cast-near")
                _castNear = true;
            else if (a == "--cast-nowhere")
                _castNowhere = true;
            else if (a == "--ground-auto")
                _autoGround = true;
            else if (a.StartsWith("--ground-demo=") &&
                     int.TryParse(a[14..], out var gd))
                _groundDemo = gd;
            else if (a.StartsWith("--zshot-times="))
                foreach (var t in a[14..].Split(','))
                { if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                      System.Globalization.CultureInfo.InvariantCulture, out var d)) _shotTimes.Add(d); }
            else if (a.StartsWith("--zshot-dir="))
                _shotDir = a[12..];
            else if (a.StartsWith("--cast-break=") &&
                     int.TryParse(a.Split('=', 2)[1], out var cb))
                _castBreakMs = cb;
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        if (!_playerSearched)
        {
            _playerSearched = true;
            _player = GetTree().Root
                .FindChild("Player", true, false) as PlayerController;
            _registry = GetTree().Root
                .FindChild("NetEntities", true, false) as EntityRegistry;
            if (_player != null)
                _player.Cancelled += () =>
                {
                    _chaseSpell = 0;
                    _chaseTarget = 0;
                };
            // Visée au sol des sorts de zone « à choix » (cercle de préparation).
            _targeter = new GroundTargeter { Name = "GroundTargeter" };
            AddChild(_targeter);
            _targeter.Bind(this, _player);
        }
        if (_combatTest) CombatTest(delta);
        if (_lockPhase != 0) LockTest(delta);
        if (CastingSpell != 0 &&
            (PlayerVitals.IsDead ||
             _lock.Expired(Time.GetTicksMsec())))
            ForceReleaseCast(PlayerVitals.IsDead ? "mort" : "échéance");
        // « Annuler le cast » (bind configurable, Shift+S par défaut —
        // settings/InputBindings) : NCID 13 + libération locale.
        // Pas de break pendant la saisie chat — Shift+S y = « S ».
        if (InputMap.HasAction("eden_cancel_cast") &&
            Input.IsActionJustPressed("eden_cancel_cast") &&
            GetViewport().GuiGetFocusOwner() is not
                (LineEdit or TextEdit))
            Break();
        // --cast-break : interruption volontaire en vol (NCID 13 →
        // 287 serveur → barre annulée).
        if (_breakAt != 0 && Time.GetTicksMsec() >= _breakAt)
        {
            _breakAt = 0;
            if (IsCasting)
            {
                GD.Print("[combat-test] break en vol (NCID 13)");
                Break();
            }
        }
        if (_groundDemo != 0 && _targeter != null && _player != null && PlayerSkills.Spells.Count > 0)
        {
            if (_groundDemoAt == 0)
                _groundDemoAt = Time.GetTicksMsec();
            else if (!_targeter.Active && !_groundDemoConfirmed && Time.GetTicksMsec() - _groundDemoAt > 6000)
            {
                _targeter.UseScreenCenter = true;
                _targeter.Begin(_groundDemo);
                _shotPrefix = "pick_";
                _shotT0 = Time.GetTicksMsec();
                _groundDemoAt = Time.GetTicksMsec();
            }
            else if (_targeter.Active && Time.GetTicksMsec() - _groundDemoAt > 4500)
            {
                _groundDemoConfirmed = true;
                _targeter.Confirm();      // clic gauche simulé au centre du cercle
                _shotPrefix = "post_";
                _shotT0 = Time.GetTicksMsec();
                _shotNext = 0;
            }
        }
        if (_shotT0 != 0 && _shotNext < _shotTimes.Count &&
            (Time.GetTicksMsec() - _shotT0) / 1000.0 >= _shotTimes[_shotNext])
        {
            var path = System.IO.Path.Combine(_shotDir.Length > 0 ? _shotDir : "/tmp",
                $"{_shotPrefix}{_shotNext}_{_shotTimes[_shotNext]:0.0#}s.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"[shot] {path}");
            _shotNext++;
        }
        // Repli : sort instantané dont aucun blast (288/297) n'est venu → gestuelle brute (col3).
        if (_rawAnimDeadline != 0 && Time.GetTicksMsec() >= _rawAnimDeadline)
        {
            _rawAnimDeadline = 0;
            if (_rawAnimSpell != 0) PlayCastAnim(0, _rawAnimSpell);
        }
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[skill] dispatch : {ex}");
            }
        }
    }

    /// <summary>Cible courante = celle du cadre cible HUD (handle
    /// réseau posé par le 444 via HudNetBridge). 0 = rien.</summary>
    public uint CurrentTarget =>
        Hud.HudExtensions.FindHud()?.Target?.TargetHandle ?? 0;

    /// <summary>Lance un sort : sur la cible courante (9), au sol
    /// (16, formes zone/directionnelles sans cible) ou sans cible
    /// (17, forme 3 auto-centrée). Une forme 0 (cible unique) sans
    /// cible est REFUSÉE en local (textindex 2195) — le natif n'a pas
    /// de message « dans le vide » pour elle. Refusé aussi si un cast
    /// est déjà en cours (le serveur répondrait 279 flag=0) ou si le
    /// perso est mort.</summary>
    public void Cast(int spellId)
    {
        if (spellId <= 0 || IsCasting || PlayerVitals.IsDead ||
            PlayerStateLocks.NoAnySkill) return;
        var target = CurrentTarget;
        // Sort non-hostile sur un ennemi (mob) → self-cast : un
        // adversaire désigné ne reçoit jamais de buff/soin — le
        // natif redirige sur le lanceur.
        if (target != 0 && SpellCatalog.TargetType(spellId) != 1 &&
            _registry != null &&
            _registry.Entities.TryGetValue(target, out var te) &&
            te.Kind == 12)
            target = 0;
        // Pref « Self-Cast » : sort de soutien/soin (type C_Spell 2-5 ;
        // 1/6/7 = offensifs, 4/5 = restauration) sans cible → sur soi
        // (sinon il partirait « dans le vide »).
        if (target == 0 && Hud.HudPrefs.SelfCast &&
            SpellCatalog.TargetType(spellId) is >= 2 and <= 5 &&
            (_registry?.SelfCharId ?? 0u) != 0)
            target = _registry!.SelfCharId;
        // Forme du sort (SSpellData+0x18) : voir SpellZone — Self (3) part toujours en
        // NCID 17 centré sur le lanceur même avec un ennemi ciblé ; Ground/Direction/Cross/Sector
        // sans cible partent en NCID 16 devant le lanceur (le natif refuse le NCID 17).
        var shape = (CastShape)SpellCatalog.Shape(spellId);
        if (shape is CastShape.Self or CastShape.Nearby) target = 0;
        // Sort de zone « à choix » (ref-shot « cercle preparation… ») : cercle au curseur, clic = lancer.
        // Rappuyer sur le même sort annule. Les pilotes de test (--combat-test, --ground-auto) lancent direct.
        if (shape == CastShape.Ground && !_combatTest && !_autoGround && _targeter != null)
        {
            if (_targeter.Active && _targeter.Spell == spellId) _targeter.Cancel();
            else _targeter.Begin(spellId);
            return;
        }
        if (target != 0 && BeyondReach(spellId, target, out var reach))
        {
            // Hors de portée : poursuite puis envoi à l'arrivée —
            // CastingSpell n'est PAS posé (aucun paquet émis).
            _chaseSpell = spellId;
            _chaseTarget = target;
            _chaseReach = reach;
            _rechaseTimer = RechaseInterval;   // 1er re-ciblage immédiat
            GD.Print($"[skill] poursuite cast #{spellId} → " +
                $"0x{target:x8} (portée {reach:0.#} u)");
            return;
        }
        SendCast(spellId, target);
    }

    /// <summary>Vrai si la cible est hors de portée du sort :
    /// portée = C_Spell col28 × 1,5 (même facteur que l'AttackRange
    /// serveur) ; col28 à 0 → repli portée arme 4 u. Cible absente du
    /// registre → envoi direct (le serveur tranche).</summary>
    private bool BeyondReach(int spellId, uint handle, out float reach)
    {
        var r = SpellCatalog.RangeUnits(spellId);
        reach = r > 0 ? r * 1.5f : 4f;
        var p = _player;
        if (p == null || _registry == null ||
            !_registry.Entities.TryGetValue(handle, out var t) ||
            !GodotObject.IsInstanceValid(t))
            return false;
        var to = t.GlobalPosition - p.GlobalPosition;
        return new Vector2(to.X, to.Z).Length() > reach;
    }

    /// <summary>Envoi effectif du cast (9 ciblé / 16 au sol / 17 sans cible) selon
    /// <see cref="SpellZone.Plan"/> — partagé par <see cref="Cast"/> et l'arrivée de la poursuite.</summary>
    private void SendCast(int spellId, uint target)
    {
        var shape = (CastShape)SpellCatalog.Shape(spellId);
        var (cx, cy) = _player != null
            ? WorldConv.ToEdenGround(_player.GlobalPosition) : (0f, 0f);
        float tx = 0, ty = 0;
        if (target != 0 && _registry != null &&
            _registry.Entities.TryGetValue(target, out var te) &&
            GodotObject.IsInstanceValid(te))
            (tx, ty) = WorldConv.ToEdenGround(te.GlobalPosition);
        if (_shotTimes.Count > 0 && _shotT0 == 0) _shotT0 = Time.GetTicksMsec();
        var plan = SpellZone.Plan(shape, target != 0, tx, ty, cx, cy, PlayerFacingEden(),
            SpellZone.Reach(SpellCatalog.RangeUnits(spellId)));
        if (plan.Route == CastRoute.Refused)
        {
            // Forme 0 (sort ciblé) sans cible : le client natif n'a
            // aucun message « dans le vide » pour elle — blocage local
            // + prompt textindex 2195 « Veuillez sélectionner une
            // cible pour utiliser cette compétence. » ; AUCUN paquet
            // n'est émis (le serveur refuserait de toute façon 410).
            var msg = TextResolver.TextIndex(2195)
                ?? "Veuillez sélectionner une cible pour utiliser cette compétence.";
            SysMsg(msg);
            GD.Print($"[skill] cast #{spellId} refusé : cible requise " +
                $"(textindex 2195, forme 0)");
            return;
        }
        switch (plan.Route)
        {
            case CastRoute.ToLife:
                Send(new ZoneSpellCastToLife
                { SpellId = (ushort)spellId, TargetId = target });
                break;
            case CastRoute.ToLocation:
                Send(new ZoneSpellCastToLocation
                { SpellId = (ushort)spellId, Target = 0, X = plan.X, Z = plan.Y });
                // Natif : le lanceur se tourne vers le point visé avant la gestuelle.
                if (_player != null)
                    _player.FaceTowards(new Vector3(plan.X,
                        _player.GlobalPosition.Y, -plan.Y), 1f);
                target = 0;
                break;
            default:
                Send(new ZoneSpellCastToNowhere
                { SpellId = (ushort)spellId, Direction = PlayerFacingEden() });
                target = 0;
                break;
        }
        // Sort offensif sur un mob : l'auto-attaque reprend dès la fin du cast
        // (natif : le combat continue au corps à corps après un sort).
        if (target != 0 && SpellCatalog.TargetType(spellId) == 1 &&
            _registry != null && _registry.Entities.TryGetValue(target, out var hostile) &&
            hostile.Kind == 12)
        {
            _engageAfter = target;
            _engageArmed = false;
            _engageSince = Time.GetTicksMsec();
        }
        CastingSpell = spellId;   // « demande » — confirmé/infirmé par 279/410
        CastingTarget = target;
        _castAcked = false;
        _lock.Requested(Time.GetTicksMsec());
        CastStateChanged?.Invoke();
        GD.Print($"[skill] cast #{spellId} → " +
            (target != 0 ? $"cible 0x{target:x8}" : "sans cible"));
    }

    /// <summary>Poursuite d'un cast ciblé : re-cible le point de la
    /// cible tant qu'elle est hors de portée ; à portée → arrêt,
    /// face, envoi du 9. Annulé par <c>PlayerController.Cancelled</c>
    /// (WASD/clic), despawn cible, NoMove/NoAnySkill.</summary>
    private uint _engageAfter;
    private ulong _engageSince;
    private bool _engageArmed;

    /// <summary>Engage l'auto-attaque sur la cible du dernier sort offensif, une fois le cast fini.</summary>
    private void EngageAfterCast()
    {
        if (_engageAfter == 0) return;
        if (IsCasting) { _engageArmed = true; return; }   // cast en cours : on attend
        if (!_engageArmed && Time.GetTicksMsec() - _engageSince < 300) return;   // sort instantané
        var h = _engageAfter;
        _engageAfter = 0;
        _engageArmed = false;
        if (_registry != null && _registry.Entities.TryGetValue(h, out var t) &&
            GodotObject.IsInstanceValid(t) && !t.Dying && !t.IsQueuedForDeletion() &&
            _player?.AutoAttacker is { Engaged: false } aa)
            // Natif (AutoReAttackb) : la reprise post-sort ne reprend
            // QUE si la cible est déjà à portée — sinon engagement
            // armé sans poursuite (jamais de course vers la cible).
            aa.ResumeAfterCast(t);
    }

    public override void _PhysicsProcess(double delta)
    {
        EngageAfterCast();
        if (_chaseSpell == 0) return;
        var p = _player;
        RemoteEntity? t = null;
        if (p == null || _registry == null ||
            !_registry.Entities.TryGetValue(_chaseTarget, out t) ||
            !GodotObject.IsInstanceValid(t) || t!.IsQueuedForDeletion() ||
            PlayerStateLocks.NoMove || PlayerStateLocks.NoAnySkill)
        {
            _chaseSpell = 0;
            _chaseTarget = 0;
            return;
        }
        var to = t.GlobalPosition - p.GlobalPosition;
        if (new Vector2(to.X, to.Z).Length() > _chaseReach)
        {
            _rechaseTimer += delta;
            if (_rechaseTimer >= RechaseInterval)
            {
                _rechaseTimer = 0;
                p.SetMoveTarget(t.GlobalPosition);
            }
            return;
        }
        var spellId = _chaseSpell;
        var handle = _chaseTarget;
        _chaseSpell = 0;
        _chaseTarget = 0;
        p.ClearMoveTarget();
        p.FaceTowards(t.GlobalPosition, 1f);
        SendCast(spellId, handle);
    }

    /// <summary>Sort de zone au point sol Eden (x,y) — NCID 16 (le champ Z du message porte le y Eden).</summary>
    public void CastAt(int spellId, float x, float z)
    {
        if (spellId <= 0 || IsCasting || PlayerVitals.IsDead) return;
        if (_shotTimes.Count > 0 && _shotT0 == 0) _shotT0 = Time.GetTicksMsec();
        Send(new ZoneSpellCastToLocation
        {
            SpellId = (ushort)spellId, Target = 0, X = x, Z = z,
        });
        if (_player != null)
            _player.FaceTowards(new Vector3(x, _player.GlobalPosition.Y, -z), 1f);
        CastingSpell = spellId;
        CastingTarget = 0;
        _lock.Requested(Time.GetTicksMsec());
        CastStateChanged?.Invoke();
    }

    /// <summary>Libère le verrou de cast sans réponse serveur
    /// (échéance, mort, changement de node).</summary>
    private void ForceReleaseCast(string why)
    {
        if (CastingSpell == 0) return;
        GD.Print($"[skill] verrou de cast #{(ushort)CastingSpell} libéré ({why})");
        CastingSpell = 0;
        CastingTarget = 0;
        _castAnim = null;
        StopCastVisuals();
        _lock.Released();
        _chaseSpell = 0;
        _chaseTarget = 0;
        CastBar.Instance?.CancelCast();
        CastStateChanged?.Invoke();
    }

    /// <summary>Annulation manuelle (touche « annuler le cast » —
    /// Shift+S par défaut) : NCID 13 SpellBreak au serveur + libération
    /// LOCALE immédiate — le 287 confirme en idempotent
    /// (<see cref="OnSpellBreak"/> no-ope sur un cast déjà clos) et un
    /// 410/279 tardif n'a plus de self à réveiller. La poursuite avant
    /// cast (sort pas encore émis) est annulée aussi.</summary>
    public void Break()
    {
        _chaseSpell = 0;
        _chaseTarget = 0;
        if (!IsCasting) return;
        Send(new ZoneSpellBreak());
        ForceReleaseCast("break manuel");
    }

    /// <summary>Cap du perso en coords Eden (même conversion que
    /// <c>PlayerController.EndSteer</c> : Atan2(cos(yaw), −sin(yaw))).</summary>
    private float PlayerFacingEden()
    {
        var yaw = _player?.ModelYaw ?? 0f;
        return EdenClient.Game.EdenDir.FromYaw(yaw);
    }

    // ------------------------------------------------------------------
    // Binds session (thread réseau → parse pur → file principale)
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(279, r =>
        {
            var m = new ZoneClientSpellCastOK();
            m.Deserialize(r);
            _pending.Enqueue(() => OnCastOk(m));
        });
        s.OnZone(288, r =>
        {
            var m = new ZoneClientSpellBlastOK();
            m.Deserialize(r);
            _pending.Enqueue(() => PlayCastAnim(m.BlastId, (ushort)m.SpellId));
        });
        s.OnZone(297, r =>
        {
            var m = new ZoneClientSpellBlastToLocation();
            m.Deserialize(r);
            _pending.Enqueue(() => PlayCastAnim(m.BlastId, (ushort)m.SpellId));
        });
        s.OnZone(287, r =>
        {
            var m = new ZoneClientSpellBreak();
            m.Deserialize(r);
            _pending.Enqueue(() => OnSpellBreak(m));
        });
        s.OnZone(281, r =>
        {
            var m = new ZoneClientSpellHitToLife();
            m.Deserialize(r);
            _pending.Enqueue(() => OnSpellHit(m));
        });
        s.OnZone(410, r =>
        {
            var m = new ZoneClientCommandFailReturn();
            m.Deserialize(r);
            _pending.Enqueue(() => OnFailReturn(m));
        });
        // 313 EnterNodeNodeData : un TP/portail en plein cast ne doit
        // pas laisser le verrou posé sur la nouvelle map.
        s.OnZone(313, r =>
        {
            var m = new ZoneEnterNodeNodeData();
            m.Deserialize(r);
            _pending.Enqueue(() => ForceReleaseCast("changement de node"));
        });
        // 302 FlagModify self : fin de l'état « action » (bit 0x2000
        // retiré) = fin du cast pour le natif. Les sorts instantanés
        // sans cible/sans dégâts (buffs…) ne reçoivent AUCUN 281 — le
        // verrou restait posé jusqu'à l'échéance (2,5 s sans pouvoir
        // relancer de sort).
        s.OnZone(302, r =>
        {
            var m = new ZoneFlagModify();
            m.Deserialize(r);
            if (m.Rest.Length < 2) return;
            var flags = (ushort)(m.Rest[0] | (m.Rest[1] << 8));
            _pending.Enqueue(() => OnSelfFlag(m.Field1, flags));
        });
        GD.Print("[skill] binds 279/288/297/287/281/410/313/302 posés");
    }

    /// <summary>279 — accusé de cast : ok → barre de cast
    /// (castDs décisecondes) ; refus → rien (le lock est levé).</summary>
    private void OnCastOk(ZoneClientSpellCastOK m)
    {
        // Comparaison en u16 : le codec lit SpellId en s16 signé —
        // les sorts d'id ≥ 0x8000 (ex. 50011) arrivent négatifs.
        var isSelfCast = (ushort)CastingSpell == (ushort)m.SpellId;
        if (m.Ok == 0)
        {
            // Refus natif (cast déjà en cours / GCD).
            if (isSelfCast)
            { CastingSpell = 0; CastingTarget = 0; _castAnim = null; }
            SysMsg($"Cannot cast {SpellCatalog.Name((ushort)m.SpellId)} " +
                "right now.");
        }
        else if (isSelfCast)
        {
            _testGot279 = true;
            _lock.Acknowledged(Time.GetTicksMsec(), m.CastTimeDs);
            _castAcked = true;
            CastingSpell = m.SpellId;
            CastingTarget = (uint)m.Target;
            GD.Print($"[verify] cast : 279 OK spell={(ushort)m.SpellId} " +
                     $"target=0x{(uint)m.Target:x8} " +
                     $"castDs={m.CastTimeDs}");
            // castDs=0 = cast instantané : pas de barre à animer.
            if (m.CastTimeDs > 0)
            {
                CastBar.Instance?.BeginCast(m.CastTimeDs / 10f,
                    SpellCatalog.Name((ushort)m.SpellId));
                // Incantation temporisée (natif) : gestuelle « chant »
                // C_Spell col 2 (301-317 → magicNN) tenue en boucle
                // jusqu'au blast + cercle magique doré tournant sous
                // le lanceur — les deux s'arrêtent à la résolution
                // (blast/break/fin). Le blast joue le clip shootNN.
                var id = (ushort)m.SpellId;
                var stem = CharModelSpec.BodyStem(
                    SessionCarry.Character?.Appearance);
                var chant = SpellCatalog.ChantKf(id) is { } ck && ck > 0
                    ? MotionTable.Shared.Lookup(stem, ck) : null;
                if (chant != null && _player?.Animator != null)
                {
                    _player.Animator.HoldAnim = chant;
                    GD.Print($"[verify] cast : chant {chant} " +
                             $"tenu (kf={ck})");
                }
                _circle = CastCircle.Begin(_player!,
                    m.CastTimeDs / 10f + 1.5f);
            }
            if (_castBreakMs > 0 && m.CastTimeDs > 0)
                _breakAt = Time.GetTicksMsec() + (ulong)_castBreakMs;
            // Gestuelle : natif = clip du kfId RÉSOLU porté par le blast (288 ciblé/sans cible, 297 au sol,
            // GetBlastID @0x080e03f0), joué AU BLAST. Pendant une incantation temporisée le perso tient la
            // pose (stance + glyphe de cast + barre), comme le ref-shot TimedCastSkill — plus de rejeu
            // du swing. Filet : sort instantané sans blast sous 400 ms → clip brut col3.
            _castAnim = null;
            _rawAnimSpell = m.CastTimeDs == 0 ? (ushort)m.SpellId : 0;
            _rawAnimDeadline = m.CastTimeDs == 0 ? Time.GetTicksMsec() + 400 : 0;
        }
        CastStateChanged?.Invoke();
    }

    /// <summary>Fin des visuels d'incantation : le cercle magique
    /// disparaît en fondu et la gestuelle « chant » est relâchée
    /// (la locomotion reprend, ou le one-shot du blast s'il joue).</summary>
    private void StopCastVisuals()
    {
        _circle?.End();
        _circle = null;
        if (_player?.Animator != null)
            _player.Animator.HoldAnim = "";
    }

    /// <summary>Joue la gestuelle de blast du lanceur local : kfId résolu du serveur (repli col3).</summary>
    private void PlayCastAnim(int blastId, int spellId)
    {
        _rawAnimDeadline = 0;
        _rawAnimSpell = 0;
        // Le blast libère la gestuelle « chant » : le one-shot shootNN
        // prend la main, puis la locomotion reprend (sans HoldAnim).
        StopCastVisuals();
        var stem = CharModelSpec.BodyStem(SessionCarry.Character?.Appearance);
        var raw = SpellCatalog.ActionKf(spellId);
        var kf = blastId > 0 ? blastId : raw;
        var anim = MotionTable.Shared.Lookup(stem, kf) ?? MotionTable.Shared.Lookup(stem, raw);
        if (anim == null) return;
        _player?.Animator?.PlayOnce(anim);
        GD.Print($"[verify] cast anim : {anim} (blastKf={blastId}, colKf={raw}, stem={stem})");
    }

    private void OnSelfFlag(int handle, ushort flags)
    {
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (handle != selfId) return;
        var casting = (flags & 0x2000) != 0;
        if (!casting && _flagCasting && CastingSpell != 0 && _castAcked)
        {
            GD.Print($"[skill] cast #{(ushort)CastingSpell} clos par 302 " +
                "(état action retiré)");
            CastingSpell = 0;
            CastingTarget = 0;
            _castAnim = null;
            StopCastVisuals();
            _lock.Released();
            CastBar.Instance?.CompleteCast();
            CastStateChanged?.Invoke();
        }
        _flagCasting = casting;
    }

    /// <summary>287 — cast interrompu (si c'est le self).</summary>
    private void OnSpellBreak(ZoneClientSpellBreak m)
    {
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (m.Caster == selfId)
        {
            CastingSpell = 0;
            CastingTarget = 0;
            _castAnim = null;
            StopCastVisuals();
            CastBar.Instance?.CancelCast();
            CastStateChanged?.Invoke();
        }
    }

    /// <summary>281 — impact du sort : clôt le cast self en cours.</summary>
    private void OnSpellHit(ZoneClientSpellHitToLife m)
    {
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (m.Caster == selfId && CastingSpell != 0)
        {
            CastingSpell = 0;
            CastingTarget = 0;
            _castAnim = null;
            StopCastVisuals();
            CastBar.Instance?.CompleteCast();
            CastStateChanged?.Invoke();
            GD.Print($"[verify] cast : 281 impact " +
                     $"spell={(ushort)m.SpellId} " +
                     $"target=0x{(uint)m.Target:x8}");
        }
    }

    /// <summary>410 — refus d'une commande de cast (cooldown, cible
    /// morte, silence, MP…) : on libère l'état et on affiche le
    /// message système rouge du client natif.</summary>
    private void OnFailReturn(ZoneClientCommandFailReturn m)
    {
        if (m.FailedOpcode is not (9 or 16 or 17 or 59)) return;
        if (CastingSpell != 0)
        {
            CastingSpell = 0;
            CastingTarget = 0;
            _castAnim = null;
            StopCastVisuals();
            CastBar.Instance?.CancelCast();
            CastStateChanged?.Invoke();
        }
        SysMsg(m.Param != 0
            ? $"Cannot use {SpellCatalog.Name(m.Param)}."
            : "Action refused.");
    }

    /// <summary>Pilote QA --combat-test : attend les spawns, engage le
    /// mob le plus proche en auto-attack, puis lance le 1er sort connu
    /// dessus (validation live 5/9/272/279/281).</summary>
    private void CombatTest(double delta)
    {
        _testTimer += delta;
        switch (_testStep)
        {
            case 0 when _testTimer > 4.0 && _registry != null:
            {
                // --clone-mob : un vrai mob serveur au pied du perso
                // (spawns naturels parfois hors portée de vue) — envoyé
                // une fois, ~1 s pour que le 298 arrive au registre.
                if (_cloneMobId > 0 && !_cloneSent)
                {
                    _cloneSent = true;
                    Send(new ZoneTextCommand
                    { Text = $"//clone_monster {_cloneMobId}" });
                    GD.Print($"[combat-test] //clone_monster {_cloneMobId}");
                    _testTimer = 3.0;
                    break;
                }
                // --cast-nowhere : aucune sélection — le cast partira
                // en NCID 17 (preuve du cast temporisé sans cible).
                if (_castNowhere)
                {
                    _testStep = 1;
                    _testTimer = 0;
                    break;
                }
                // --clone-mob : engager le clone par son EntityId, pas
                // un mob sauvage plus proche (le clone doit survivre à
                // la sélection pour prouver ses sorts temporisés).
                if (_cloneMobId > 0 &&
                    !_registry.Entities.Values.Any(e =>
                        e.Kind == 12 && e.EntityId == _cloneMobId))
                {
                    // Clone pas encore spawnt (le //clone_monster a ~1 s
                    // de latence) — attendre au lieu d'engager un yak.
                    if (_testTimer < 16.0) break;
                    _cloneMobId = 0;   // timeout → repli mob sauvage
                }
                var mob = _cloneMobId > 0 &&
                    _registry.Entities.Values.Any(e =>
                        e.Kind == 12 && e.EntityId == _cloneMobId)
                    ? _registry.Entities.Values
                        .Where(e => e.Kind == 12 &&
                            e.EntityId == _cloneMobId)
                        .OrderBy(e =>
                            (e.GlobalPosition -
                             (_player?.GlobalPosition ?? Vector3.Zero))
                            .LengthSquared())
                        .FirstOrDefault()
                    // --cast-no-engage : cible la plus LOINTAINE — la
                    // poursuite de cast doit couvrir du terrain (le mob
                    // aggro rapproche, la portée 6 u est vite atteinte).
                    : _castNoEngage && !_castNear
                    ? _registry.Entities.Values
                        .Where(e => e.Kind == 12)
                        .OrderByDescending(e =>
                            (e.GlobalPosition -
                             (_player?.GlobalPosition ?? Vector3.Zero))
                            .LengthSquared())
                        .FirstOrDefault()
                    : _registry.Entities.Values
                        .Where(e => e.Kind == 12)
                        .OrderBy(e =>
                            (e.GlobalPosition -
                             (_player?.GlobalPosition ?? Vector3.Zero))
                            .LengthSquared())
                        .FirstOrDefault();
                if (mob == null)
                {
                    // --mob-test encore en migration/scan → on attend
                    // sa fin avant le repli « cast sans cible ».
                    if (GameScene.MobTestActive) break;
                    // Zone sans mob (ville) : après ~16 s on bascule
                    // sur un cast sans cible (17 → 279) — le flux de
                    // cast reste prouvé live. On préfère un sort dont
                    // la forme accepte le 17/16 (≠ cible unique 0, qui
                    // est désormais refusée en local — textindex 2195).
                    if (_testTimer < 16.0) break;
                    var spell0 = PlayerSkills.Spells.FirstOrDefault(s =>
                        !SpellZone.RequiresTarget(
                            (CastShape)SpellCatalog.Shape(s.SpellMaster)))
                        ?? PlayerSkills.Spells.FirstOrDefault();
                    GD.Print("[combat-test] aucun mob en vue " +
                        "(zone urbaine) — cast hors cible");
                    if (spell0 != null)
                    {
                        Cast(spell0.SpellMaster);
                        GD.Print($"[verify] cast : sort " +
                                 $"#{spell0.SpellMaster} émis (17)");
                    }
                    else
                        GD.Print("[verify] cast : aucun sort connu " +
                            "(311 vide)");
                    _testStep = 2;
                    _testTimer = 0;
                    break;
                }
                if (_castNoEngage)
                {
                    // Sélection seule (124 → 444) : la poursuite de
                    // cast (2.1) fait l'approche au lieu de l'auto-attaque.
                    Send(new ZoneTargetUpdate
                    { Value = (int)mob.Handle });
                    GD.Print($"[combat-test] select-only " +
                        $"0x{mob.Handle:x8} «{mob.DisplayName}»");
                    // Cast ~1 s après la sélection : le 444 a posé la
                    // cible et l'aggro n'a pas encore rapproché le mob.
                    _testTimer = 5.0;
                }
                else
                {
                    _player?.AutoAttacker?.Engage(mob);
                    GD.Print($"[combat-test] engage 0x{mob.Handle:x8} " +
                        $"«{mob.DisplayName}»");
                }
                _testStep = 1;
                _testTimer = 0;
                break;
            }
            case 1 when _testTimer > 6.0:
            {
                // Premier sort connu autorisé pour la classe courante
                // (gate serveur SpellAllowedForClass — le FirstOrDefault
                // tombait sur un sort d'une autre classe apprise et le
                // cast était refusé sans réponse) ; un sort temporisé
                // est préféré pour rendre la CastBar visible.
                var cls = SessionCarry.Character?.ClassId ?? 0;
                bool Allowed(int id)
                {
                    var m = SpellCatalog.ClassMask(id);
                    return m == 0 || (cls is >= 1 and <= 63 &&
                        (m & (1UL << (cls - 1))) != 0);
                }
                // Préférence : sort hostile temporisé (prouve barre
                // ET 288/281 en un seul cast — ex. Bear's Fury voleur),
                // puis hostile instantané, puis tout sort temporisé,
                // puis tout sort autorisé.
                var spell = PlayerSkills.Spells.FirstOrDefault(s =>
                        Allowed(s.SpellMaster) &&
                        SpellCatalog.TargetType(s.SpellMaster) == 1 &&
                        SpellCatalog.CastDs(s.SpellMaster) > 0) ??
                    PlayerSkills.Spells.FirstOrDefault(s =>
                        Allowed(s.SpellMaster) &&
                        SpellCatalog.TargetType(s.SpellMaster) == 1) ??
                    PlayerSkills.Spells.FirstOrDefault(s =>
                        Allowed(s.SpellMaster) &&
                        SpellCatalog.CastDs(s.SpellMaster) > 0) ??
                    PlayerSkills.Spells.FirstOrDefault(s =>
                        Allowed(s.SpellMaster));
                var castId = _forcedSpell != 0
                    ? _forcedSpell : spell?.SpellMaster ?? 0;
                if (castId != 0)
                {
                    Cast(castId);
                    GD.Print($"[combat-test] cast #{castId} " +
                        $"«{SpellCatalog.Name(castId)}» " +
                        $"castDs={SpellCatalog.CastDs(castId)}");
                    if (_lockTest)
                    {
                        // Mob tué entre le 279 et l'impact : avant le
                        // correctif serveur aucun 281/410 ne revenait.
                        Send(new ZoneTextCommand
                        {
                            Text = _cloneMobId > 0
                                ? $"//kill {_cloneMobId}" : "//kill",
                        });
                        _lockSpell = castId;
                        _lockTimer = 0;
                        GD.Print("[lock-test] //kill envoyé pendant le vol");
                        _combatTest = false;
                        CombatTestActive = false;
                        _lockPhase = 1;
                        break;
                    }
                }
                else GD.Print("[combat-test] aucun sort connu (311)");
                // Seconds casts de caractéristique opposée (instantané
                // ↔ temporisé) : couvre à la fois le 281 instantané et
                // la CastBar dans le même run. File de 3 candidats —
                // les temporisés d'abord, un refus (cooldown serveur)
                // laisse la place au suivant.
                if (castId != 0)
                {
                    var firstDs = SpellCatalog.CastDs(castId) > 0;
                    foreach (var timed in new[] { true, false })
                    {
                        foreach (var s in PlayerSkills.Spells)
                        {
                            if (_testSpellQueue.Count >= 3) break;
                            if (s.SpellMaster == castId ||
                                !Allowed(s.SpellMaster) ||
                                _testSpellQueue.Contains(s.SpellMaster))
                                continue;
                            var isTimed =
                                SpellCatalog.CastDs(s.SpellMaster) > 0;
                            if ((isTimed != firstDs) == timed)
                                _testSpellQueue.Add(s.SpellMaster);
                        }
                    }
                }
                _testStep = 2;
                _testTimer = 0;
                break;
            }
            // Attendre la résolution du cast (jusqu'à 15 s) avant de
            // clore le test — sinon //kill tombe pendant un cast
            // temporisé et le 288/281 n'est jamais observé.
            case 2 when _testTimer > 4.0:
            {
                if (_testCast2 != 0)
                {
                    // Candidat en vol : 279 attendu (la sortie du
                    // `when` garantit >4 s) ; résolution ≤15 s après.
                    if (_testGot279 && IsCasting && _testTimer < 15.0)
                        break;
                    var dropped = _testCast2;
                    _testCast2 = 0;
                    if (!_testGot279 &&
                        (ushort)CastingSpell == (ushort)dropped)
                    {
                        // Refus silencieux → libérer la demande.
                        CastingSpell = 0;
                        CastingTarget = 0;
                    }
                }
                if (_testSpellQueue.Count > 0)
                {
                    // Cast précédent encore en vol (ou resté armé
                    // faute de résolution) : attendre 15 s après un
                    // 279, 8 s sans réponse ; puis libérer.
                    if (IsCasting)
                    {
                        if (_testTimer < (_testGot279 ? 15.0 : 8.0))
                            break;
                        CastingSpell = 0;
                        CastingTarget = 0;
                    }
                    var id = _testSpellQueue[0];
                    _testSpellQueue.RemoveAt(0);
                    Cast(id);
                    _testCast2 = id;
                    _testGot279 = false;
                    GD.Print($"[combat-test] 2ᵉ cast #{id} " +
                        $"«{SpellCatalog.Name(id)}» " +
                        $"castDs={SpellCatalog.CastDs(id)}");
                    _testTimer = 0;
                    break;
                }
                // Mob cloné : achevé par le chemin de mort normal +
                // auto-attaque coupée (le serveur accepte encore les
                // coups sur le handle mort — sans Cancel le combat ne
                // se termine jamais) → « Leaving Combat » ~6 s après.
                if (_cloneSent)
                {
                    _player?.AutoAttacker?.Cancel();
                    Send(new ZoneTextCommand { Text = "//kill" });
                }
                _combatTest = false;
                CombatTestActive = false;
                GD.Print("[combat-test] terminé" +
                    (_cloneSent ? " — //kill envoyé" : ""));
                break;
            }
        }
    }

    /// <summary>--lock-test : 1,5 s après le //kill le verrou doit être
    /// levé (410 serveur ou échéance) ; un nouveau cast sur un mob
    /// vivant est ensuite émis SANS libération manuelle.</summary>
    private void LockTest(double delta)
    {
        _lockTimer += delta;
        if (_lockPhase == 1 && _lockTimer > 1.5)
        {
            GD.Print($"[lock-test] +1,5 s : IsCasting={IsCasting} " +
                $"spell={CastingSpell}");
            _lockPhase = 2;
        }
        else if (_lockPhase == 2 && _lockTimer > 7.0)
        {
            GD.Print($"[lock-test] +7 s : IsCasting={IsCasting} " +
                (IsCasting ? "VERROU RESTE POSÉ" : "verrou libre"));
            if (!IsCasting)
            {
                Cast(_lockSpell);
                GD.Print($"[lock-test] cast #{_lockSpell} émis après " +
                    "la mort du mob (sans cible → 17 ou refus 410 ok)");
            }
            _lockPhase = 3;
            _lockTimer = 0;
        }
        else if (_lockPhase == 3 && _lockTimer > 1.5)
        {
            GD.Print($"[lock-test] fin : IsCasting={IsCasting}");
            _lockPhase = 0;
        }
    }

    private void SysMsg(string text) =>
        Hud.HudExtensions.FindHud()?.AddChat(7, "", text);

    /// <summary>Envoi fire-and-forget tracé (même pattern
    /// qu'AutoAttack).</summary>
    private void Send(NetMessage msg)
    {
        var s = _session ?? SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[skill] pas de session — {msg.Name} " +
                "non émis");
            return;
        }
        var task = s.SendZoneAsync(msg, _cts.Token);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[skill] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        _cts.Cancel();
        _cts.Dispose();
        base._ExitTree();
    }
}

/// <summary>
/// Résolution d'icône/nom d'un sort (spellmaster id → données
/// client) : <c>data/db/C_Spell.ini</c> col 1 = code icône
/// (<c>UI/skillicon/&lt;code&gt;.png</c>), nom EN via
/// <see cref="TextResolver.SpellName"/> (t_spell.ini). Chargement
/// paresseux (~60k lignes) — même pattern que c_item dans
/// InventoryWindow.
/// </summary>
public static class SpellCatalog
{
    private static Dictionary<int, string>? _icons;   // id → code icône
    private static Dictionary<int, int>? _actions;    // id → KF motion
    private static Dictionary<int, int>? _chant;      // id → KF chant col2 (magicNN incantation)
    private static Dictionary<int, int>? _pay;        // id → col42 paiement
    private static Dictionary<int, ulong>? _masks;    // id → col24 class mask
    private static Dictionary<int, int>? _castDs;     // id → col27 cast ds
    private static Dictionary<int, int>? _ttype;      // id → col19 target type
    private static Dictionary<int, int>? _family;     // id → col17 famille
    private static Dictionary<int, int>? _upBase;     // id → col43 base coût montée
    private static Dictionary<int, int>? _mpBase;     // id → col32 coût MP base
    private static Dictionary<int, int>? _cdDs;       // id → col27 down-time ds
    private static Dictionary<int, int>? _result;     // id → col39 enchant résultat
    private static Dictionary<int, int>? _reqLv;      // id → col22 niveau classe requis
    private static Dictionary<int, int>? _dmgPow;     // id → col35 power
    private static Dictionary<int, int>? _healPow;    // id → col36 heal power
    private static Dictionary<int, int>? _range;      // id → col28 portée
    private static Dictionary<int, int>? _shape;      // id → shape dérivée (0 ciblé, 1 sol, 3 lanceur…)
    private static Dictionary<int, int>? _radius;     // id → col29 rayon
    private static List<(int Id, ulong Mask, int Pay)>? _payable; // pay 1/9

    /// <summary>Sorts « payants » (montée en or col42=1 / CP col42=9) dont
    /// le masque de classes (col24) contient la classe de base
    /// <paramref name="baseClass"/> (bit classe-1) et vise au plus 30
    /// classes (exclut les sorts génériques Soul Jump/Guidance…).
    /// Ordre = id croissant.</summary>
    public static IEnumerable<(int Id, int Pay)> ClassSpells(int baseClass)
    {
        EnsureLoaded();
        if (_payable == null || baseClass < 1 || baseClass > 64)
            yield break;
        var bit = 1UL << (baseClass - 1);
        foreach (var (id, mask, pay) in _payable)
            if ((mask & bit) != 0 &&
                System.Numerics.BitOperations.PopCount(mask) <= 30)
                yield return (id, pay);
    }

    /// <summary>Base de coût de montée (C_Spell col43 — même colonne que
    /// SpellParser.UpgradeCostBase côté serveur).</summary>
    public static int UpgradeBase(int spellId)
    {
        EnsureLoaded();
        return _upBase != null &&
               _upBase.TryGetValue(spellId, out var b) ? b : 0;
    }

    /// <summary>Type de cible (C_Spell col 19 — même index que le
    /// <c>TargetType</c> du SpellParser serveur). 1 = hostile.</summary>
    public static int TargetType(int spellId)
    {
        EnsureLoaded();
        return _ttype != null &&
               _ttype.TryGetValue(spellId, out var t) ? t : 0;
    }

    /// <summary>Masque de classes autorisées (C_Spell col 24 — même
    /// colonne que S_Spell lue par le gate serveur SpellAllowedForClass ;
    /// bit cls-1). 0/vide = sort utilisable par toutes les classes.</summary>
    public static ulong ClassMask(int spellId)
    {
        EnsureLoaded();
        return _masks != null &&
               _masks.TryGetValue(spellId, out var m) ? m : 0;
    }

    /// <summary>Famille du sort (C_Spell col 17 — même colonne que
    /// Family du SpellParser serveur) : 6/7 = sorts de classe,
    /// 7 = passifs de classe (non castables — jamais sur la hotbar).</summary>
    public static int Family(int spellId)
    {
        EnsureLoaded();
        return _family != null &&
               _family.TryGetValue(spellId, out var f) ? f : 0;
    }

    /// <summary>Temps de cast en décisecondes (C_Spell col 27 — même
    /// colonne que S_Spell CastTimeMs serveur). 0 = instantané.</summary>
    public static int CastDs(int spellId)
    {
        EnsureLoaded();
        return _castDs != null &&
               _castDs.TryGetValue(spellId, out var c) ? c : 0;
    }

    /// <summary>Monnaie de montée de niveau (C_Spell col42) : 9 = points
    /// de classe (« Class Skills »), 1 = or (« Reg. Skills »), 0 = ?</summary>
    public static int PayType(int spellId)
    {
        EnsureLoaded();
        return _pay != null && _pay.TryGetValue(spellId, out var p) ? p : 0;
    }

    /// <summary>Base du coût MP (C_Spell col 32 — même colonne que
    /// SpellParser.MpCost serveur). Coût affiché =
    /// <see cref="SpellReference.Coef"/>(niveau, 1) × base / 100.</summary>
    public static int MpCostBase(int spellId)
    {
        EnsureLoaded();
        return _mpBase != null &&
               _mpBase.TryGetValue(spellId, out var m) ? m : 0;
    }

    /// <summary>Down-time/cooldown en décisecondes (C_Spell col 27 —
    /// même colonne que SpellParser.CooldownDs serveur).</summary>
    public static int CooldownDs(int spellId)
    {
        EnsureLoaded();
        return _cdDs != null &&
               _cdDs.TryGetValue(spellId, out var c) ? c : 0;
    }

    /// <summary>Enchant résultat lié (C_Spell col 39 — id c_enchant
    /// appliqué à l'impact ; sa description T_Enchant alimente le
    /// tooltip de sort).</summary>
    public static int ResultEnchantId(int spellId)
    {
        EnsureLoaded();
        return _result != null &&
               _result.TryGetValue(spellId, out var r) ? r : 0;
    }

    /// <summary>Niveau de classe requis (C_Spell col 22 — « Must reach
    /// class level N » du tooltip).</summary>
    public static int RequiredLevel(int spellId)
    {
        EnsureLoaded();
        return _reqLv != null &&
               _reqLv.TryGetValue(spellId, out var l) ? l : 0;
    }

    /// <summary>Puissance de dégât direct (C_Spell col 36 — affichée
    /// « P-ATK +N » = power × coef physique du palier / 100).</summary>
    public static int DamagePower(int spellId)
    {
        EnsureLoaded();
        return _dmgPow != null &&
               _dmgPow.TryGetValue(spellId, out var p) ? p : 0;
    }

    /// <summary>Puissance de soin (C_Spell col 35 — affichée
    /// « +N HP » = power × coef soin du palier / 100).</summary>
    public static int HealPower(int spellId)
    {
        EnsureLoaded();
        return _healPow != null &&
               _healPow.TryGetValue(spellId, out var p) ? p : 0;
    }

    /// <summary>Portée en unités (C_Spell col 28 — « Range: N meters »).</summary>
    /// <summary>Shape dérivée du sort (0 cible, 1 sol, 2/5/6 direction/croix/secteur, 3 centré lanceur).</summary>
    public static int Shape(int spellId)
    {
        EnsureLoaded();
        return _shape != null && _shape.TryGetValue(spellId, out var v) ? v : 0;
    }

    public static int RangeUnits(int spellId)
    {
        EnsureLoaded();
        return _range != null &&
               _range.TryGetValue(spellId, out var r) ? r : 0;
    }

    /// <summary>Rayon AoE brut (C_Spell col 29 — affiché ×0.5 :
    /// « within N meters »).</summary>
    public static int RadiusUnits(int spellId)
    {
        EnsureLoaded();
        return _radius != null &&
               _radius.TryGetValue(spellId, out var r) ? r : 0;
    }

    /// <summary>Code icône du sort (col 1 de C_Spell — J001…), ou
    /// chaîne vide.</summary>
    public static string IconCode(int spellId)
    {
        EnsureLoaded();
        return _icons != null &&
               _icons.TryGetValue(spellId, out var code) ? code : "";
    }

    /// <summary>Id motion KF de l'incantation du sort (col 2 de
    /// C_Spell — 301-317 magicNN « chant » tenu en boucle pendant un
    /// cast temporisé, avant la gestuelle de release col 3). 0 si
    /// le sort n'a pas de chant (instantané de mêlée…).</summary>
    public static int ChantKf(int spellId)
    {
        EnsureLoaded();
        return _chant != null &&
               _chant.TryGetValue(spellId, out var kf) ? kf : 0;
    }

    /// <summary>Id motion KF de l'action du sort (col 3 de C_Spell —
    /// 301-317 magicNN chant, 4xx shootNN release, 10xx attackNN) —
    /// 0 si inconnu.</summary>
    public static int ActionKf(int spellId)
    {
        EnsureLoaded();
        return _actions != null &&
               _actions.TryGetValue(spellId, out var kf) ? kf : 0;
    }

    /// <summary>Nom anglais du sort (t_spell.ini), repli « #id ».</summary>
    public static string Name(int spellId) =>
        TextResolver.SpellName(spellId) ?? $"Spell #{spellId}";

    /// <summary>Texture d'icône prête à l'emploi (null si absente).</summary>
    public static Texture2D? Icon(int spellId)
    {
        var code = IconCode(spellId);
        return code.Length > 0
            ? AssetCatalog.GetTexture($"UI/skillicon/{code}.png")
            : null;
    }

    private static readonly object _loadLock = new();

    // Tables construites en local puis publiées d'un bloc (_icons en
    // dernier — les lecteurs le testent comme indicateur « prêt ») pour
    // que le warm-up en tâche de fond ne produise pas de lecture
    // partielle pendant le parse.
    private static void EnsureLoaded()
    {
        if (_icons != null) return;
        var path = AssetCatalog.ResolveRawAsset("data/db/c_spell.ini")
            ?? AssetCatalog.ResolveRawAsset("data/db/C_Spell.ini");
        lock (_loadLock)
        {
            if (_icons != null) return;
            var icons = new Dictionary<int, string>();
            var actions = new Dictionary<int, int>();
            var chant = new Dictionary<int, int>();
            var payT = new Dictionary<int, int>();
            var masks = new Dictionary<int, ulong>();
            var castDs = new Dictionary<int, int>();
            var ttype = new Dictionary<int, int>();
            var family = new Dictionary<int, int>();
            var upBase = new Dictionary<int, int>();
            var mpBase = new Dictionary<int, int>();
            var cdDs = new Dictionary<int, int>();
            var result = new Dictionary<int, int>();
            var reqLv = new Dictionary<int, int>();
            var dmgPow = new Dictionary<int, int>();
            var healPow = new Dictionary<int, int>();
            var range = new Dictionary<int, int>();
            var radius = new Dictionary<int, int>();
            var shape = new Dictionary<int, int>();
            var payable = new List<(int, ulong, int)>();
            if (path != null)
            try
            {
                var t = IniTable.ParseFile(path);
                foreach (var row in t.Rows)
                {
                    var id = IniTable.GetInt(row, 0);
                    var code = IniTable.GetStr(row, 1);
                    if (id <= 0) continue;
                    if (code.Length > 0) icons[id] = code;
                    var kf = IniTable.GetInt(row, 3);
                    if (kf > 0) actions[id] = kf;
                    var ck = IniTable.GetInt(row, 2);
                    if (ck > 0) chant[id] = ck;
                    var pay = IniTable.GetInt(row, 42);
                    if (pay > 0) payT[id] = pay;
                    var mask = IniTable.GetStr(row, 23);
                    if (ulong.TryParse(mask, out var m) && m != 0)
                        masks[id] = m;
                    var ub = IniTable.GetInt(row, 43);
                    if (ub > 0) upBase[id] = ub;
                    if (pay is 1 or 9 && m != 0 &&
                        ulong.TryParse(mask, out var pm))
                        payable.Add((id, pm, pay));
                    var cds = IniTable.GetInt(row, 26);
                    if (cds > 0) castDs[id] = cds;
                    var tt = IniTable.GetInt(row, 18);
                    if (tt > 0) ttype[id] = tt;
                    var fam = IniTable.GetInt(row, 17);
                    if (fam > 0) family[id] = fam;
                    var mp = IniTable.GetInt(row, 32);
                    if (mp > 0) mpBase[id] = mp;
                    var cd = IniTable.GetInt(row, 27);
                    if (cd > 0) cdDs[id] = cd;
                    var res = IniTable.GetInt(row, 39);
                    if (res > 0) result[id] = res;
                    var rl = IniTable.GetInt(row, 22);
                    if (rl > 0) reqLv[id] = rl;
                    var dp = IniTable.GetInt(row, 36);   // #29# (ATQ) — 1252/1426 textes #29# ont col36
                    if (dp > 0) dmgPow[id] = dp;
                    var hp = IniTable.GetInt(row, 35);   // #30# (soin) — 127/135 textes #30# ont col35
                    if (hp > 0) healPow[id] = hp;
                    var rg = IniTable.GetInt(row, 28);
                    if (rg > 0) range[id] = rg;
                    var p1 = IniTable.GetInt(row, 20);
                    var sub = IniTable.GetInt(row, 30);
                    shape[id] = (int)SpellZone.ShapeOf(p1, sub);   // LoadAllSpellData @0x08380130
                    var rd = IniTable.GetInt(row, 29);
                    if (rd > 0) radius[id] = rd;
                }
                GD.Print($"[skill] C_Spell : {icons.Count} icônes, " +
                    $"{actions.Count} actions");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[skill] c_spell.ini : {e.Message}");
            }
            _actions = actions; _chant = chant; _pay = payT; _masks = masks;
            _castDs = castDs; _ttype = ttype; _family = family;
            _upBase = upBase; _mpBase = mpBase; _cdDs = cdDs;
            _result = result; _reqLv = reqLv; _dmgPow = dmgPow;
            _healPow = healPow; _range = range; _radius = radius; _shape = shape;
            _payable = payable; _icons = icons;
        }
    }
}
