using System.Collections.Concurrent;
using Godot;
using EdenClient.Net;
using EdenClient.Combat;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau → fenêtres HUD (C7/C8) — même pattern que le binding
/// paresseux de <see cref="EntityRegistry"/> : la session zone est
/// découverte dans <see cref="_Process"/> via
/// <see cref="SessionCarry.Session"/> (flux écrans) puis les NCIDs
/// utiles aux fenêtres y sont abonnés une fois.
///
/// Trames routées (parsing inline — TODO(agent F) : promouvoir ces
/// codecs dans <c>scripts/protocol/</c> quand le lot protocole les
/// couvrira ; formats d'après ZoneMessages.g.cs serveur) :
///
///   - <b>444</b> ClientGetSelectLifeTarget `[i32 handle][u8][i32]`
///     → <see cref="TargetFrame"/> (handle 0 = déselection).
///   - <b>285</b> ClientDisplayDialogView `[i32 ctx][s16 n]
///     [n×(i32, str, s16)][u8][i32 npcUid]` →
///     <see cref="NpcDialogueWindow"/> ; les clics option renvoient
///     NCID 11 ReplyDialog `[s16 index]`.
///   - <b>280</b> UpdateAttr (réutilise <see cref="ZoneUpdateAttr"/>)
///     → rafraîchit la HP de la cible affichée (attrId 0).
///
/// Threading : les handlers tournent sur le thread réseau — ils ne
/// font que parser (pur) puis poster dans <see cref="_pending"/>,
/// drainée ici sur le thread principal. Résolution d'entités via le
/// nœud <c>NetEntities</c> découvert dans l'arbre (même convention
/// que PlayerController.Registry).
///
/// Limite connue (identique aux binds combat 272/305/263) : en mode
/// <c>--live</c> direct sans flux d'écrans, la session est privée à
/// GameScene → pas de binding (la fenêtre reste utilisable en mock).
/// </summary>
public partial class HudNetBridge : Node
{
    private readonly HudRoot _hud;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private readonly ConcurrentQueue<Action> _pending = new();

    public HudNetBridge(HudRoot hud)
    {
        _hud = hud;
        Name = "HudNetBridge";
    }

    private void OnLocalTarget(uint h) => ApplyTarget((int)h, 0);

    // Cible morte = plus de cible : cadre et anneau vidés tout de suite.
    private void OnEntityDied(World.RemoteEntity e)
    {
        if (_targetRingHandle != e.Handle) return;
        ApplyTarget(0, 0);
    }

    public override void _ExitTree()
    {
        Player.PlayerController.LocalTargeted -= OnLocalTarget;
        World.RemoteEntity.Died -= OnEntityDied;
        base._ExitTree();
    }

    public override void _Ready()
    {
        Player.PlayerController.LocalTargeted += OnLocalTarget;
        World.RemoteEntity.Died += OnEntityDied;
        // Réponses de dialogue : index d'option → ReplyDialog.
        if (_hud.Dialogue != null)
            _hud.Dialogue.OptionChosen += OnDialogOption;
        // Lot S3+S5 : quêtes (388/87/51), marqueurs NPC (345),
        // transport (393/88), portails (aires→15), monture (283/19).
        AddChild(new MissionBridge(_hud));
        // Familiers : 412/413/414/418/424/513/567/568/574/575/588 +
        // actions C→S (107/108/137/187/188/227/228/229).
        AddChild(new ElfBridge(_hud));
        // --banner-shot=<base> : capture la bannière combat aux deux
        // transitions (preuve U9) → <base>_enter.png / <base>_leave.png.
        foreach (var args in new[] { OS.GetCmdlineArgs(),
                     OS.GetCmdlineUserArgs() })
            foreach (var a in args)
                if (a.StartsWith("--banner-shot="))
                    _bannerShotBase = a.Split('=', 2)[1];
    }

    private string? _bannerShotBase;
    private string? _bannerShotSuffix;
    private int _bannerShotFrames = -1;

    private void ScheduleBannerShot(string suffix)
    {
        if (_bannerShotBase == null) return;
        _bannerShotSuffix = suffix;
        // enter : ~1,7 s pour que le ToT (rafraîchi à 0,5 s, posé par
        // le 444 d'aggro qui arrive après le lock) soit aussi visible
        // — la bannière tient 2,5 s.
        _bannerShotFrames = suffix == "enter" ? 100 : 15;
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        _registry ??= GetTree().Root
            .FindChild("NetEntities", true, false) as EntityRegistry;
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[hud-net] dispatch : {ex}");
            }
        }
        // Cible disparue du registre (despawn sans 444) → cadre vidé.
        var t = _hud.Target;
        // (Handles ≥ 0xFFFF0000 = cible de démo --hud-open=target.)
        if (t is { TargetHandle: > 0 and < 0xFFFF0000 } &&
            _registry != null &&
            !_registry.Entities.ContainsKey(t.TargetHandle))
        {
            t.Clear();
        }
        // « Target's Target » — rafraîchi ~2×/s tant qu'une cible est
        // affichée (la cible de la cible change avec l'aggro, sans
        // trame dédiée : déduit des 272 observés, expiration 8 s).
        _totTimer += delta;
        if (_totTimer >= 0.5)
        {
            _totTimer = 0;
            RefreshTargetOfTarget();
        }
        // Sortie de combat : 6 s sans coup reçu.
        if (_inCombat && _lastHitOnSelf > 0 &&
            Time.GetTicksUsec() / 1_000_000.0 - _lastHitOnSelf > 6.0)
        {
            _inCombat = false;
            _lastHitOnSelf = -1;
            _hud.Banner?.ShowCombat(entering: false);
            ScheduleBannerShot("leave");
        }
        if (_bannerShotFrames > 0 && --_bannerShotFrames == 0 &&
            _bannerShotBase != null && _bannerShotSuffix != null)
        {
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture()?.GetImage();
            // Relatif → res:// (non écrivable) : résoudre sur le cwd
            // comme --shot de GameScene.
            var root = System.IO.Path.IsPathRooted(_bannerShotBase)
                ? _bannerShotBase
                : System.IO.Path.GetFullPath(_bannerShotBase);
            var p = $"{root}_{_bannerShotSuffix}.png";
            var err = img?.SavePng(p) ?? Error.FileNotFound;
            GD.Print($"[banner-shot] {p} err={err}");
            _bannerShotSuffix = null;
        }
    }

    private double _totTimer;
    private double _lastHitOnSelf = -1;
    private bool _inCombat;

    /// <summary>272 : un coup dont self est la cible ou l'attaquant
    /// arme l'état combat ; la sortie est évaluée dans _Process
    /// (6 s sans hit).</summary>
    private void OnMeleeForCombatBanner(ZoneClientMeleeAttack m)
    {
        var s = SessionCarry.Session;
        if (s == null || (m.Target != s.SelectedCharId &&
            m.Attacker != s.SelectedCharId)) return;
        ArmCombat();
    }

    /// <summary>281 : un dégât de sort reçu (cible=self) ou infligé
    /// (lanceur=self) arme aussi l'état combat — les soins (Restore)
    /// n'y comptent pas.</summary>
    private void OnSpellForCombatBanner(ZoneClientSpellHitToLife m)
    {
        var s = SessionCarry.Session;
        if (s == null || (m.Flags & SpellHitConst.Restore) != 0 ||
            (m.Target != (int)s.SelectedCharId &&
             m.Caster != (int)s.SelectedCharId)) return;
        ArmCombat();
    }

    private void ArmCombat()
    {
        _lastHitOnSelf = Time.GetTicksUsec() / 1_000_000.0;
        if (!_inCombat)
        {
            _inCombat = true;
            _hud.Banner?.ShowCombat(entering: true);
            ScheduleBannerShot("enter");
        }
    }

    /// <summary>Résout la cible de la cible. Source native : le 3ᵉ
    /// champ du 444 (cible de l'entité sélectionnée — émis au lock
    /// d'aggro) ; repli = dernier handle frappé par l'entité ciblée
    /// (272, mobs dont le lock n'a pas été vu). Joueur self = charId
    /// de session (hors registre) → nom depuis la fiche perso.</summary>
    private void RefreshTargetOfTarget()
    {
        var t = _hud.Target;
        // Démo --hud-open=target : son ToT est posé une fois par
        // HudRoot ; les vrais handles mob sont aussi en 0xFFFFxxxx —
        // ne pas les exclure.
        if (_session == null) return;
        if (t is not { TargetHandle: > 0 } || _registry == null)
        {
            t?.ClearTargetOfTarget();
            return;
        }
        uint tot;
        if (_serverTot != 0)
        {
            tot = (uint)_serverTot;
        }
        else if (_registry.Entities.TryGetValue(t.TargetHandle, out var e) &&
            e.LastAtkTarget != 0 &&
            Time.GetTicksUsec() / 1_000_000.0 - e.LastAtkTime <= 8.0)
        {
            tot = e.LastAtkTarget;
        }
        else
        {
            t.ClearTargetOfTarget();
            return;
        }
        var s = SessionCarry.Session;
        if (s != null && tot == s.SelectedCharId)
        {
            var selfName = s.Characters
                .FirstOrDefault(c => c.Id == (int)tot)?.Name ?? "You";
            t.SetTargetOfTarget(selfName,
                HudPrefs.SelfHp, HudPrefs.SelfMaxHp);
            return;
        }
        if (_registry.Entities.TryGetValue(tot, out var e2))
        {
            t.SetTargetOfTarget(e2.DisplayName, e2.Hp, e2.MaxHp);
            return;
        }
        t.ClearTargetOfTarget();
    }

    /// <summary>Abonnements NCID — chaque handler parse sur le thread
    /// réseau puis poste l'application dans la file principale.</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(444, r =>
        {
            // [i32 handle cible][u8][i32 cible de l'entité] — corps 9B
            // (capture, test serveur SelectLifeTarget_NineBytes_
            // FromCapture ; lock d'aggro serveur = [mob][0][charId]).
            var handle = r.ReadInt();
            r.ReadByte();
            var tot = r.ReadInt();
            _pending.Enqueue(() => ApplyTarget(handle, tot));
        });
        s.OnZone(285, r =>
        {
            // [i32 ctx T_Dialogue][s16 n][n×(i32 displayId, u16str
            // name, s16 flag)][u8 type][i32 npcUid] — ZoneEntryFlow.
            // Dialogue.cs (SendLocalGossipAsync / DisplayDialogView).
            var ctx = r.ReadInt();
            var n = r.ReadShort();
            var opts = new List<NpcDialogueWindow.DialogOption>(
                n > 0 ? n : 0);
            for (var i = 0; i < n && r.Remaining >= 8; i++)
            {
                var id = r.ReadInt();
                var name = r.ReadString();
                var flag = r.ReadShort();
                opts.Add(new NpcDialogueWindow.DialogOption(
                    id, name, flag));
            }
            if (r.Remaining >= 1) r.ReadByte();
            var npcUid = r.Remaining >= 4 ? r.ReadInt() : 0;
            _pending.Enqueue(() =>
                _hud.Dialogue?.ShowDialogue(npcUid, ctx, opts));
        });
        // 272/281 : suivi de l'état combat (« Entering/Leaving Combat »
        // du ref — tout coup ou dégât de sort impliquant self, reçu ou
        // émis, entre dans l'état ; ~6 s sans coup en sort). OnZone
        // accumule les handlers : le registry et les floaters gardent
        // les leurs, on ne fait que la bannière.
        s.OnZone(272, r =>
        {
            var m = new ZoneClientMeleeAttack();
            m.Deserialize(r);
            _pending.Enqueue(() => OnMeleeForCombatBanner(m));
        });
        s.OnZone(281, r =>
        {
            var m = new ZoneClientSpellHitToLife();
            m.Deserialize(r);
            _pending.Enqueue(() => OnSpellForCombatBanner(m));
        });
        // 280 : HP de la cible pendant le combat (attrId 0 = HP).
        s.OnZone(280, r =>
        {
            var m = new ZoneUpdateAttr();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                var t = _hud.Target;
                if (t == null || t.TargetHandle != (uint)m.Handle) return;
                // attr 0x13 = patience (Break Point) — le registre l'a
                // déjà appliquée (HandleUpdateAttr) ; rafraîchit la barre.
                if (m.AttrId == 0x13)
                {
                    if (_registry != null &&
                        _registry.Entities.TryGetValue((uint)m.Handle,
                            out var be) && be.Kind == 12)
                        t.SetBreak(be.MonsterType, be.WeakMask,
                            be.Patience, be.MaxPatience);
                    return;
                }
                if (m.AttrId != 0 && m.AttrId != 0x11) return;
                // H6 : registre = source unique (courant + max réel).
                if (_registry != null &&
                    _registry.Entities.TryGetValue((uint)m.Handle, out var e))
                    t.SetVitals(e.Hp, e.MaxHp);
            });
        });
        GD.Print("[hud-net] binds 444/285/280 posés");
    }

    /// <summary>444 : remplit la target frame depuis l'entité du
    /// registre (nom « LvN … », kind, HP du 298/280) — et pose le
    /// cercle rouge de sélection au sol sous l'entité visée.</summary>
    private void ApplyTarget(int handle, int tot)
    {
        var t = _hud.Target;
        if (t == null) return;
        if (handle == 0)
        {
            UnmarkTargetRing();
            _serverTot = 0;
            t.Clear();
            return;
        }
        _serverTot = tot;
        if (_registry != null &&
            _registry.Entities.TryGetValue((uint)handle, out var e))
        {
            UnmarkTargetRing();
            e.SetTargeted(true);
            _targetRingHandle = (uint)handle;
            // Mob sans PV reçus (298 hp=0 / 280 pas encore arrivé) :
            // PV max = S_Monster col 8, courant = max (mob frais) —
            // plus de barre « 0 PV ».
            if (e.Kind == 12 && !e.IsElfPet && e.MaxHp <= 0 &&
                MobDanger.Lookup?.Invoke(e.EntityId) is { HP: > 0 } md)
            {
                e.MaxHp = md.HP;
                if (e.Hp <= 0 && !e.Dying) e.Hp = md.HP;
                e.UpdateHpBar();
            }
            t.SetTarget((uint)handle, e.DisplayName, e.Kind,
                e.Hp, e.MaxHp, e.Family);
            // Break Point : étoiles / faiblesses / barre violette.
            if (e.Kind == 12 && !e.IsElfPet)
                t.SetBreak(e.MonsterType, e.WeakMask, e.Patience,
                    e.MaxPatience);
        }
        else
        {
            // Entité inconnue du registre (handle stale) : cadre
            // minimal — le prochain 280/444 corrigera.
            t.SetTarget((uint)handle, $"0x{handle:x8}", 0, 0, 0);
        }
    }

    private uint _targetRingHandle;
    /// <summary>3ᵉ champ du dernier 444 — handle de la cible de
    /// l'entité sélectionnée (0 = elle ne cible rien / inconnu).</summary>
    private int _serverTot;

    private void UnmarkTargetRing()
    {
        if (_targetRingHandle != 0 && _registry != null &&
            _registry.Entities.TryGetValue(_targetRingHandle,
                out var old))
            old.SetTargeted(false);
        _targetRingHandle = 0;
    }

    /// <summary>Choix d'option de dialogue → ReplyDialog (NCID 11,
    /// [s16 index] — l'INDEX dans la liste reçue, pas le displayId,
    /// vérifié dans HandleReplyDialogAsync serveur).</summary>
    private void OnDialogOption(short index)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true)
        {
            // Solo/mock : le clic referme juste la fenêtre.
            if (_hud.Dialogue != null) _hud.Dialogue.Visible = false;
            return;
        }
        var send = s.SendZoneAsync(
            new ZoneReplyDialog { DialogId = index },
            CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[hud-net] ReplyDialog : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
