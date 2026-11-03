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
/// Session : <see cref="SessionCarry.Session"/> (flux écrans). En mode
/// <c>--live</c> direct la session appartient à GameScene et n'est pas
/// accessible — le combat est alors désactivé (log WARN une fois).
/// </summary>
public partial class AutoAttack : Node
{
    /// <summary>Portée de swing côté client (u) — le serveur tranche
    /// la portée réelle de l'arme ; on s'arrête assez près pour que le
    /// coup passe (désarmé natif ~7.5 u, mêlée ~2-3 u + marges).</summary>
    [Export] public float AttackReach { get; set; } = 3.0f;
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
    /// approche + swings.</summary>
    public void Engage(RemoteEntity target)
    {
        if (_target == target) return;
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

    /// <summary>Arrêt explicite (clic, WASD, despawn…) — notifie le
    /// serveur [0][2].</summary>
    public void Cancel() => CancelSend();

    private void CancelSend()
    {
        if (_target == null) return;
        _target = null;
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
        _swingTimer = Math.Min(_swingTimer + delta, SwingInterval);
        _farTimer += delta;
        _rechaseTimer += delta;

        if (dist > AttackReach)
        {
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
            var played = p.Animator?.PlayOnceAny("attack01", "attack02");
            var t1 = Time.GetTicksMsec();
            // Sonde latence : envoi → début d'anim (le décodage KF
            // paresseux se paie ici, sur le thread principal).
            AttackProbe.Swing(t0, t1, played ?? "");
        }
    }

    // ------------------------------------------------------------------

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
