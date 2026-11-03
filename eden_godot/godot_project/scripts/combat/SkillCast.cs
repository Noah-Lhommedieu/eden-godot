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
    private string? _castAnim;   // gestuelle du cast en cours (timed)
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
        }
        if (_combatTest) CombatTest(delta);
        if (_lockPhase != 0) LockTest(delta);
        if (CastingSpell != 0 &&
            (PlayerVitals.IsDead ||
             _lock.Expired(Time.GetTicksMsec())))
            ForceReleaseCast(PlayerVitals.IsDead ? "mort" : "échéance");
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
        // Cast temporisé : rejouer la gestuelle tant que le cast
        // n'est pas résolu (la fin du one-shot rend la main à la
        // locomotion — la relance est immédiate, fondu 0,12 s).
        if (_castAnim != null && CastingSpell != 0 &&
            _player?.Animator is { } an && !an.OneShotPlaying)
            an.PlayOnce(_castAnim);
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

    /// <summary>Lance un sort : sur la cible courante (9) ou sans
    /// cible (17, direction = cap du perso). Refusé si un cast est
    /// déjà en cours (le serveur répondrait 279 flag=0) ou si le perso
    /// est mort.</summary>
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
        // Sort de ZONE : au sol sur la cible (shape 1), ou centré sur le lanceur (shape 3,
        // NCID 17 sans cible) même quand un ennemi est ciblé — le natif n'envoie pas un
        // cast ciblé dans ces cas.
        var shape = SpellCatalog.Shape(spellId);
        if (shape == 3 && SpellCatalog.RadiusUnits(spellId) > 0 && target != 0 &&
            SpellCatalog.TargetType(spellId) == 1)
            target = 0;
        if (shape == 1 && target != 0 && _registry != null &&
            _registry.Entities.TryGetValue(target, out var gt) && GodotObject.IsInstanceValid(gt))
        {
            var (gx, gz) = WorldConv.ToEdenGround(gt.GlobalPosition);
            CastAt(spellId, gx, gz);
            return;
        }
        if (target != 0 && BeyondReach(spellId, target, out var reach))
        {
            // Hors de portée : poursuite puis envoi du 9 à l'arrivée —
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

    /// <summary>Envoi effectif du cast (9 ciblé / 17 sans cible) —
    /// partagé par <see cref="Cast"/> et l'arrivée de la poursuite.</summary>
    private void SendCast(int spellId, uint target)
    {
        if (target != 0)
        {
            Send(new ZoneSpellCastToLife
            {
                SpellId = (ushort)spellId, TargetId = target,
            });
        }
        else
        {
            Send(new ZoneSpellCastToNowhere
            {
                SpellId = (ushort)spellId,
                Direction = PlayerFacingEden(),
            });
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
            aa.Engage(t);
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

    /// <summary>Sort de zone au point sol Eden (x,z) — NCID 16.</summary>
    public void CastAt(int spellId, float x, float z)
    {
        if (spellId <= 0 || IsCasting || PlayerVitals.IsDead) return;
        Send(new ZoneSpellCastToLocation
        {
            SpellId = (ushort)spellId, Target = 0, X = x, Z = z,
        });
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
        _lock.Released();
        _chaseSpell = 0;
        _chaseTarget = 0;
        CastBar.Instance?.CancelCast();
        CastStateChanged?.Invoke();
    }

    /// <summary>Annulation manuelle (touche Échap/déplacement…) —
    /// NCID 13 SpellBreak.</summary>
    public void Break()
    {
        if (!IsCasting) return;
        Send(new ZoneSpellBreak());
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
        GD.Print("[skill] binds 279/287/281/410/313/302 posés");
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
                CastBar.Instance?.BeginCast(m.CastTimeDs / 10f,
                    SpellCatalog.Name((ushort)m.SpellId));
            if (_castBreakMs > 0 && m.CastTimeDs > 0)
                _breakAt = Time.GetTicksMsec() + (ulong)_castBreakMs;
            // Anim d'action du sort (C_Spell col3 = KF → motion.ini
            // via le stem KFM du corps) — le natif entre en état
            // d'action sur le flag 302 0x2000 ; pour self, 279 est le
            // déclencheur équivalent.
            var stem = CharModelSpec.BodyStem(
                SessionCarry.Character?.Appearance);
            var anim = MotionTable.Shared.Lookup(stem,
                SpellCatalog.ActionKf((ushort)m.SpellId));
            if (anim != null)
            {
                _player?.Animator?.PlayOnce(anim);
                // Cast temporisé : la gestuelle est tenue (rejouée)
                // jusqu'à la résolution — le GLB ne boucle pas les
                // one-shots (281/287/410 effacent _castAnim).
                _castAnim = m.CastTimeDs > 0 ? anim : null;
                GD.Print($"[verify] cast anim : {anim} " +
                    $"(kf={SpellCatalog.ActionKf((ushort)m.SpellId)}" +
                    $", stem={stem})");
            }
            else _castAnim = null;
        }
        CastStateChanged?.Invoke();
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
                    // cast reste prouvé live.
                    if (_testTimer < 16.0) break;
                    var spell0 = PlayerSkills.Spells.FirstOrDefault();
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
                    shape[id] = p1 switch   // LoadAllSpellData @0x08380130 (voir SpellDef.Param1)
                    {
                        0 => sub == 3 ? 5 : 3,
                        >= 1 and <= 3 => 3,
                        4 => sub == 2 ? 6 : sub == 3 ? 5 : sub == 1 ? 2 : 0,
                        5 => sub == 2 ? 6 : sub == 3 ? 5 : sub == 1 ? 2 : 1,
                        _ => 0,
                    };
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
            _actions = actions; _pay = payT; _masks = masks;
            _castDs = castDs; _ttype = ttype; _family = family;
            _upBase = upBase; _mpBase = mpBase; _cdDs = cdDs;
            _result = result; _reqLv = reqLv; _dmgPow = dmgPow;
            _healPow = healPow; _range = range; _radius = radius; _shape = shape;
            _payable = payable; _icons = icons;
        }
    }
}
