using Godot;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Boucle d'auto-attaque façon client EE : approche de la cible puis
/// envoi de <see cref="ZoneMeleeAttack"/> (NCID 5) à chaque tick de
/// swing — le serveur résout les dégâts et renvoie les 272.
///
/// Comportement natif reproduit : le paquet part MÊME hors de portée
/// (le serveur marque juste l'état combat, sans coup — cf.
/// ZoneEntryFlow.Combat.Mob.cs : « le client poursuit la cible tout
/// seul et renvoie le 5 au prochain tick »). Arrêt : WASD / clic sol /
/// nouveau clic droit / mort ou despawn de la cible → envoi
/// <c>[0][2]</c> (stop).
///
/// Cast (PostCastRules — docs/knowledge/combat-autoattack-cast.md) :
/// pendant <c>SkillCast.IsCasting</c> la boucle est gelée (aucun 5 —
/// une attaque casserait le cast côté natif) ; à la fin du cast la
/// poursuite ne reprend QUE si la cible est déjà à portée, sinon
/// <c>_holdAfterCast</c> garde l'engagement armé sans déplacement.
///
/// Session : <see cref="SessionCarry.Session"/> (flux écrans). En mode
/// <c>--live</c> direct la session appartient à GameScene et n'est pas
/// accessible — le combat est alors désactivé (log WARN une fois).
/// </summary>
public partial class AutoAttack : Node
{
    /// <summary>Portée de swing de repli (u) — utilisée sans arme ou
    /// sans donnée de portée (désarmé natif ~7.5 u, mêlée ~2-3 u +
    /// marges). La portée réelle vient de <see cref="ReachNow"/>.</summary>
    [Export] public float AttackReach { get; set; } = 3.0f;

    /// <summary>Portée d'arrêt réelle : S_Item col48 de l'arme portée
    /// (loc 7 prioritaire, loc 10 arme longue), brute — le
    /// <c>CCharFormula::AttackRange</c> natif (@0x083f6f10) lit
    /// <c>CItemData+0x48</c> SANS multiplicateur ; CanAttack
    /// (@0x08417120) ajoute rayons + tolérance. Arc/fusil : le perso
    /// s'arrête à distance de tir au lieu de courir au contact.
    /// Désarmé/kind non-arme → repli <see cref="AttackReach"/>.</summary>
    private float ReachNow()
    {
        var worn = Protocol.PlayerEquipment.Worn;
        var itemId = worn.TryGetValue(7, out var m) ? m
            : worn.TryGetValue(10, out var l) ? l : 0;
        var def = itemId != 0
            ? Data.Tables.ItemTable.Lookup(itemId) : null;
        // Kinds arme S_Item : 0x11-0x21 + awakened 0x3d-0x42 (miroir
        // serveur IsWeaponKind) — le bouclier loc 8 n'est pas une arme.
        // +1 : le client vise légèrement hors de la portée native —
        // le serveur garde une marge supplémentaire de son côté.
        return def is { AttackRange: > 0 } && IsWeaponKind(def.Kind)
            ? Math.Max(2f, def.AttackRange + 1f)
            : AttackReach;
    }
    /// <summary>Cadence de réémission du NCID 5 (s).</summary>
    [Export] public float SwingInterval { get; set; } = 1.2f;
    /// <summary>Re-ciblage de la poursuite (s) — le mob bouge.</summary>
    [Export] public float RechaseInterval { get; set; } = 0.30f;

    private PlayerController? _player;
    private EntityRegistry? _registry;
    private RemoteEntity? _target;
    private double _swingTimer;
    private double _farTimer;   // cadence du NCID 5 envoyé hors portée
    private double _rechaseTimer;
    private bool _warnedNoSession;
    // Règles natives cast → auto-attaque (PostCastRules) :
    // pendant un cast, l'auto-attaque est gelée (attaque = cast cassé
    // côté natif) ; à la fin du cast la poursuite ne reprend QUE si
    // la cible est déjà à portée — sinon _holdAfterCast garde
    // l'engagement armé sans jamais courir après la cible.
    private bool _wasCasting;       // cast en vol vu au dernier tick
    private bool _explicitInCast;   // Engage explicite PENDANT le cast
    private bool _holdAfterCast;    // armée mais immobile (hors portée)
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Cible courante (null = pas d'engagement).</summary>
    public RemoteEntity? Target => _target;

    /// <summary>Vrai si un engagement est actif.</summary>
    public bool Engaged => _target != null;

    /// <summary>Émis quand l'engagement démarre (handle cible).</summary>
    public event Action<uint>? EngagedEvent;
    /// <summary>Émis quand l'engagement cesse.</summary>
    public event Action? Disengaged;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        if (_player != null)
            _player.Cancelled += Cancel;
    }

    /// <summary>Registre injecté par le contrôleur (découverte tardive
    /// — « NetEntities » est ajouté après le joueur par GameScene).</summary>
    public EntityRegistry? Registry
    {
        get => _registry;
        set => _registry = value;
    }

    /// <summary>Démarre l'engagement : sélection réseau (124) +
    /// approche + swings. Engagement EXPLICITE (clic droit, hotkey) —
    /// lève le hold post-cast : le joueur a demandé la poursuite.</summary>
    public void Engage(RemoteEntity target)
    {
        // Resynchronise le suivi de cast : un ordre explicite pendant
        // un cast re-donne la poursuite à la fin du cast ; hors cast,
        // aucune retenue post-cast ne doit s'appliquer.
        _explicitInCast = SkillCast.Instance?.IsCasting == true;
        _wasCasting = _explicitInCast;
        if (_target == target)
        {
            // Même cible : un clic explicite re-donne la poursuite.
            _holdAfterCast = false;
            return;
        }
        CancelSend();
        _target = target;
        _swingTimer = SwingInterval;   // premier swing immédiat dès la portée
        _farTimer = SwingInterval;
        _rechaseTimer = 0;
        // Sélection de la cible côté serveur (124 TargetUpdate → 444).
        Send(new ZoneTargetUpdate { Value = (int)target.Handle });
        GD.Print($"[combat] engage mob h=0x{target.Handle:x8} " +
            $"«{target.DisplayName}»");
        EngagedEvent?.Invoke(target.Handle);
    }

    /// <summary>Reprise après un cast offensif (natif
    /// <c>AutoReAttackb</c>) : l'auto-attaque ne reprend QUE si la
    /// cible est DÉJÀ à portée — sinon l'engagement reste armé mais
    /// le perso ne court PAS après elle (pas de SetMoveTarget, pas
    /// de NCID 5 hors portée). Le swing repart dès qu'elle revient
    /// à portée ; tout ordre explicite (<see cref="Engage"/>,
    /// <see cref="Cancel"/>) re-donne la main.</summary>
    public void ResumeAfterCast(RemoteEntity target)
    {
        var p = _player;
        var to = p != null
            ? target.GlobalPosition - p.GlobalPosition : Vector3.Zero;
        var dist = p != null
            ? new Vector2(to.X, to.Z).Length() : float.MaxValue;
        if (PostCastRules.Decide(true, dist, ReachNow())
                == PostCastAttack.Resume)
        {
            Engage(target);
            return;
        }
        if (_target != target)
        {
            CancelSend();
            _target = target;
            _swingTimer = SwingInterval;
            _farTimer = SwingInterval;
            _rechaseTimer = 0;
            Send(new ZoneTargetUpdate { Value = (int)target.Handle });
            EngagedEvent?.Invoke(target.Handle);
        }
        _holdAfterCast = true;
        GD.Print($"[combat] post-cast : 0x{target.Handle:x8} hors " +
            $"portée ({dist:0.#} u > {ReachNow():0.#}) — armée, " +
            "pas de poursuite");
    }

    /// <summary>Arrêt explicite (clic, WASD, despawn…) — notifie le
    /// serveur [0][2].</summary>
    public void Cancel() => CancelSend();

    private void CancelSend()
    {
        if (_target == null) return;
        _target = null;
        _holdAfterCast = false;
        // NCID 5 stop : [i32 0][u8 2] — natif : handle=0 OU type=2.
        Send(new ZoneMeleeAttack { TargetId = 0, AttackSlot = 2 });
        Disengaged?.Invoke();
        GD.Print("[combat] stop attack");
    }

    public override void _PhysicsProcess(double delta)
    {
        var t = _target;
        var p = _player;
        if (t == null || p == null) return;
        // Cible despawnée / libérée → arrêt.
        if (!GodotObject.IsInstanceValid(t) || t.IsQueuedForDeletion())
        {
            CancelSend();
            return;
        }

        // Cible morte (cadavre encore en anim de mort) : l'engagement
        // est fini — plus aucun coup, et le swing en cours est coupé.
        if (t.Dying)
        {
            p.Animator?.CancelAttackOneShot();
            CancelSend();
            return;
        }

        // StateNoAttack (1003) : le serveur refuse les coups, on les
        // retient aussi côté client (le timer reste armé).
        if (PlayerStateLocks.NoAttack) return;
        var to = t.GlobalPosition - p.GlobalPosition;
        var dist = new Vector2(to.X, to.Z).Length();
        var casting = SkillCast.Instance?.IsCasting == true;
        // Fin de cast (front descendant) : la poursuite ne reprend QUE
        // si la cible est à portée — sinon engagement armé immobile
        // (natif : l'auto-attaque post-sort ne court jamais vers la
        // cible). _explicitInCast est verrouillé jusqu'ici : un Engage
        // explicite reçu PENDANT le cast re-donne la poursuite.
        if (_wasCasting && !casting)
        {
            if (!_holdAfterCast && !_explicitInCast &&
                PostCastRules.Decide(true, dist, ReachNow())
                    == PostCastAttack.Hold)
                _holdAfterCast = true;
            _explicitInCast = false;
        }
        _wasCasting = casting;
        // Règle native : pendant un cast (temporisé ou vol d'un
        // instantané) AUCUNE auto-attaque — ni swing ni approche —
        // une attaque casserait le cast (natif MeleeAttack →
        // ISpell::Break, zone @0x080f6930). Le déplacement reste libre
        // (cast mobile) — seule la boucle d'attaque est gelée.
        if (casting) return;
        _swingTimer = Math.Min(_swingTimer + delta, SwingInterval);
        _farTimer += delta;
        _rechaseTimer += delta;

        if (dist > ReachNow())
        {
            // Retenue post-cast : armée mais jamais de poursuite ni de
            // NCID 5 tant que la cible est hors de portée.
            if (_holdAfterCast) return;
            // Poursuite : cible le point au sol du mob, re-ciblée
            // périodiquement (il peut bouger).
            if (_rechaseTimer >= RechaseInterval)
            {
                _rechaseTimer = 0;
                p.SetMoveTarget(t.GlobalPosition);
            }
            // Le natif envoie quand même le NCID 5 au tick —
            // le serveur ne rend aucun coup mais marque l'engagement.
            // Le timer de swing reste armé : le coup part dès l'arrivée à portée.
            if (_farTimer >= SwingInterval)
            {
                _farTimer = 0;
                Send(new ZoneMeleeAttack
                {
                    TargetId = (int)t.Handle, AttackSlot = 0,
                });
            }
            return;
        }

        // À portée : la retenue post-cast tombe — l'engagement redevient
        // un engagement normal (poursuite si la cible repart).
        _holdAfterCast = false;
        // À portée : arrêt net, face à la cible, swing périodique.
        p.ClearMoveTarget();
        p.FaceTowards(t.GlobalPosition, 1f);
        if (_swingTimer >= SwingInterval)
        {
            _swingTimer = 0;
            var t0 = Time.GetTicksMsec();
            Send(new ZoneMeleeAttack
            {
                TargetId = (int)t.Handle, AttackSlot = 0,
            });
            var played = p.Animator?.PlayOnceAny(AttackAnimNames());
            var t1 = Time.GetTicksMsec();
            // Sonde latence : envoi → début d'anim (le décodage KF
            // paresseux se paie ici, sur le thread principal).
            AttackProbe.Swing(t0, t1, played ?? "");
        }
    }

    // ------------------------------------------------------------------
    // Motions d'attaque par arme — miroir client des pools kfId natifs
    // (MotionParser.WeaponAttackKfIds : CCharKfm::Integration
    // @0x80e09e0). L'arc joue « attack12 » (bow1), le fusil
    // « attack13 »… au lieu du swing 1H générique.

    /// <summary>Kind S_Item → noms d'anim d'attaque (attack01-27).</summary>
    private static readonly Dictionary<int, string[]> KindAnims = new()
    {
        [0x11] = new[] { "attack01", "attack02" },
        [0x12] = new[] { "attack01", "attack02" },
        [0x13] = new[] { "attack01", "attack02" },
        [0x3d] = new[] { "attack01", "attack02" },
        [0x18] = new[] { "attack03", "attack04" },
        [0x1a] = new[] { "attack03", "attack04" },
        [0x19] = new[] { "attack05", "attack06" },
        [0x3e] = new[] { "attack05", "attack06" },
        [0x14] = new[] { "attack08", "attack09" },
        [0x15] = new[] { "attack08", "attack09" },
        [0x41] = new[] { "attack08", "attack09" },
        [0x16] = new[] { "attack10", "attack11" },
        [0x1b] = new[] { "attack10", "attack11" },
        [0x1c] = new[] { "attack12" },
        [0x3f] = new[] { "attack12" },
        [0x1d] = new[] { "attack13" },
        [0x40] = new[] { "attack13" },
        [0x1f] = new[] { "attack14" },
        [0x42] = new[] { "attack14" },
        [0x20] = new[] { "attack15" },
        [0x21] = new[] { "attack16" },
        [0x17] = new[] { "attack25", "attack26" },
        [0x1e] = new[] { "attack27" },
    };

    /// <summary>Pools double-port natifs C[main×3+off] : classes
    /// 0 = lame (0x11-0x13/0x3d), 1 = hache (0x18/0x1a), 2 = autre.</summary>
    private static readonly string[][] DualAnims =
    {
        new[] { "attack17", "attack18" },  // lame×lame
        new[] { "attack23", "attack24" },  // lame×hache
        new[] { "attack23", "attack24" },  // lame×autre
        new[] { "attack23", "attack24" },  // hache×lame
        new[] { "attack19", "attack20" },  // hache×hache
        new[] { "attack23", "attack24" },  // hache×autre
        new[] { "attack23", "attack24" },  // autre×lame
        new[] { "attack23", "attack24" },  // autre×hache
        new[] { "attack21", "attack22" },  // autre×autre
    };

    private static bool IsWeaponKind(int k) =>
        k is >= 0x11 and <= 0x21 or >= 0x3d and <= 0x42;

    private static int DualClass(int k) =>
        k is >= 0x11 and <= 0x13 or 0x3d ? 0
        : k is 0x18 or 0x1a ? 1 : 2;

    /// <summary>Noms d'anim du swing courant d'après l'équipement
    /// porté (même résolution loc 7 → 10 que <see cref="ReachNow"/>,
    /// loc 8 compte le double-port). Repli = poings désarmés.</summary>
    private static string[] AttackAnimNames()
    {
        var worn = Protocol.PlayerEquipment.Worn;
        var mainId = worn.TryGetValue(7, out var m) ? m
            : worn.TryGetValue(10, out var l) ? l : 0;
        var main = mainId != 0
            ? Data.Tables.ItemTable.Lookup(mainId) : null;
        if (main == null || !IsWeaponKind(main.Kind))
            return new[] { "attack05", "attack06" };
        if (worn.TryGetValue(8, out var o) &&
            Data.Tables.ItemTable.Lookup(o) is { } off &&
            IsWeaponKind(off.Kind))
            return DualAnims[DualClass(main.Kind) * 3 +
                             DualClass(off.Kind)];
        return KindAnims.TryGetValue(main.Kind, out var anims)
            ? anims : new[] { "attack01", "attack02" };
    }

    /// <summary>Envoie sur la session portée par
    /// <see cref="SessionCarry"/> (fire-and-forget tracé).</summary>
    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s == null)
        {
            if (!_warnedNoSession)
            {
                _warnedNoSession = true;
                GD.PushWarning("[combat] pas de session " +
                    "(SessionCarry vide — mode --live direct ?)");
            }
            return;
        }
        var task = s.SendZoneAsync(msg, _cts.Token);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[combat] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _cts.Dispose();
        base._ExitTree();
    }
}
