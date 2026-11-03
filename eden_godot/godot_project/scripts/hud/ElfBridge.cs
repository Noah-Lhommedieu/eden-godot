using System.Collections.Concurrent;
using Godot;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Pont réseau ↔ fenêtre Familier — même pattern que
/// <see cref="MissionBridge"/> : nœud fils de <c>HudNetBridge</c>,
/// binding paresseux de la session zone via <see cref="SessionCarry.
/// Session"/>, parsing des trames sur le thread réseau puis mutation
/// du modèle (<see cref="ElfBook"/>) et de la fenêtre sur le thread
/// principal via <see cref="_pending"/>.
///
/// NCIDs reliés (formats prouvés dans
/// <see cref="Protocol.ZoneElfMessages"/> — NativeElfCodec +
/// ZoneEntryFlow.Elf{,.Work,.Spell,.Mix}.cs serveur) :
///
///   S→C : 412 tableau, 413 ajout, 414 retrait, 417 event, 418 attr,
///   419/420 skill/soul (aplati), 424 conteneur équipement, 431
///   broadcast change, 442 action, 470 action pet live, 513 shortcut
///   invocation, 567 mix elf, 568 résultat fusion, 574/575 cast
///   fusion, 588 sorts appris.
///
///   C→S : 107 ElfWork (0 repos / 1 mix / 2 invoquer), 108 ElfReturn
///   (libérer / sceller), 137 ElfTalk (caresse — chemin serveur
///   assumé), 187 PetModeChange (2 passif / 1 combat), 188 SendPetCmd
///   (1 attaque, 101-105 sorts), 227 ElfMix (fusion), 228
///   ElfChangeName, 229 ElfRevive (nourrir — chemin assumé).
///
/// Le bridge expose aussi <see cref="EntityRegistry.ResolveElfPet"/>
/// (stem GLB + nom du pet invoqué du self) pour que le 298 elf-pet
/// (entityId −2537, kind 12, owner en Unk48) obtienne son modèle
/// C_Partner quand le propriétaire est le joueur local.
/// </summary>
public partial class ElfBridge : Node
{
    private readonly HudRoot _hud;
    private readonly ElfBook _book = ElfBook.Shared;
    /// <summary>Ops parsees sur le thread réseau, appliquées sur
    /// l'instance au prochain _Process — statique pour que les binds
    /// posés tôt (<see cref="EnsureBound"/>, avant la création du
    /// pont) ne perdent pas le burst d'entrée (412/424).</summary>
    private static readonly ConcurrentQueue<Action<ElfBridge>> Ops =
        new();
    /// <summary>Session déjà équipée de nos handlers — évite le double
    /// abonnement si EnsureBound est appelé tôt puis que _Process
    /// retombe sur la même session.</summary>
    private static EdenSession? _boundSession;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private ElfWindow? _win;
    private bool _passive = true;
    private bool _elfTest;
    private bool _elfTestDone;
    private bool _petTest;
    private PetFunctionBar? _petBar;
    private uint _selfCharId;

    /// <summary>Livre d'elfs courant (modèle partagé UI/bridge).</summary>
    public ElfBook Book => _book;

    public ElfBridge(HudRoot hud)
    {
        _hud = hud;
        Name = "ElfBridge";
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a == "--elf-test") _elfTest = true;
            else if (a == "--pet-test") { _petTest = true; _petTestStep = -1; }
            else if (a.StartsWith("--pet-shot="))
                _petTestShot = a.Split('=', 2)[1];
            else if (a.StartsWith("--pet-mob=") &&
                     int.TryParse(a.Split('=', 2)[1], out var pm))
                _petTestMob = pm;
        }
    }

    /// <summary>Appelé quand la fenêtre elf est montée.</summary>
    public void Attach(ElfWindow win)
    {
        _win = win;
        _win.SetBook(_book);
        _win.ActionRequested += OnAction;
    }

    public override void _Ready()
    {
        _win = _hud.FindChild("Elf", true, false) as ElfWindow
            ?? GetParent()?.FindChild("Elf", true, false) as ElfWindow;
        if (_win != null) Attach(_win);
        TryBindPetBar();
    }

    /// <summary>Lie la barre pet au livre — paresseux car l'ordre
    /// <c>_Ready</c> ElfBridge↔PetBar n'est pas garanti (et un
    /// remontage après migration recrée le nœud).</summary>
    private void TryBindPetBar()
    {
        if (_petBar != null && IsInstanceValid(_petBar)) return;
        _petBar = _hud.FindChild("PetBar", true, false)
            as PetFunctionBar;
        if (_petBar == null) return;
        _petBar.SetBook(_book);
        _petBar.ActionRequested += OnAction;
    }

    public override void _Process(double delta)
    {
        if (_selfCharId == 0)
            _selfCharId = (uint)(SessionCarry.Character?.Id ?? 0);
        if (_boundSession == null && SessionCarry.Session is { } s)
            EnsureBound(s);     // repli : flux sans GameScene attachée
        _session ??= _boundSession;
        _registry ??= GetTree().Root
            .FindChild("NetEntities", true, false) as EntityRegistry;
        if (_registry != null && _registry.ResolveElfPet == null)
            _registry.ResolveElfPet = ResolveElfPet;
        TryBindPetBar();
        while (Ops.TryDequeue(out var act))
        {
            try { act(this); }
            catch (Exception ex)
            {
                GD.PrintErr($"[elf] dispatch : {ex}");
            }
        }
        // --elf-test : injecte un tableau de démonstration par les
        // handlers réels (même chemin ApplySnapshot que le 412) —
        // preuve visuelle quand le compte test n'a pas d'elf.
        if (_elfTest && !_elfTestDone && _win != null &&
            _book.Elfs.Count == 0)
        {
            _elfTestDone = true;
            InjectTestElfs();
        }
        // --pet-test : capture différée d'un tick (layout résolu),
        // puis machine d'états du cycle live.
        if (_petPendingShot != null)
        {
            var suf = _petPendingShot; _petPendingShot = null;
            PetShot(suf);
        }
        if (_petTest) TickPetTest(delta);
    }

    // -------------------- bindings S→C -----------------------------

    /// <summary>Pose les handlers elf sur la session — appelé le plus
    /// tôt possible (<b>GameScene._Ready</b>, dès que la session
    /// attachée est connue, avant ZoneEntryAsync) pour que le burst
    /// d'entrée (412/424 émis pendant le handshake zone) soit bien
    /// dispatché au lieu de partir en « non géré ». Sans instance de
    /// pont encore montée, les ops s'accumulent dans <see cref="Ops"/>
    /// et seront appliquées au premier _Process.</summary>
    public static void EnsureBound(EdenSession s)
    {
        if (ReferenceEquals(_boundSession, s)) return;
        _boundSession = s;
        void Bind<T>(T msg, Action<ElfBridge, T> act)
            where T : NetMessage, new() =>
            s.OnZone(msg.Opcode, r =>
            {
                var m = new T();
                try { m.Deserialize(r); }
                catch (Exception ex)
                {
                    Ops.Enqueue(_ => GD.PrintErr(
                        $"[elf] parse {m.Name} : {ex.Message}"));
                    return;
                }
                Ops.Enqueue(b => act(b, m));
            });

        Bind(new ZoneEnterNodeElfInfo(), (b, m) =>
        {
            b._book.ApplySnapshot(m);
            GD.Print($"[elf] tableau 412 : {m.Elfs.Count} familiers");
        });
        Bind(new ZoneElfAdd(), (b, m) =>
        {
            b._book.ApplyAdd(m);
            if (m.Elf != null)
                GD.Print($"[elf] +familier loc={m.Elf.Loc} " +
                         $"tpl={m.Elf.TemplateId} lv={m.Elf.Level}");
        });
        Bind(new ZoneElfReturnOk(), (b, m) => b._book.ApplyReturnOk(m));
        Bind(new ZoneElfUpdateAttr(), (b, m) =>
            b._book.ApplyUpdateAttr(m));
        Bind(new ZoneEnterNodeElfContainer(), (b, m) =>
            b._book.ApplyContainer(m));
        Bind(new ZonePetShortCut(), (b, m) =>
        {
            b._book.ApplyShortCut(m);
            GD.Print($"[elf] 513 shortcut flag={m.Flag} loc={m.Loc}");
        });
        Bind(new ZoneUpdateMixElf(), (b, m) =>
        {
            // 567 broadcast à tous les témoins — ne suit que le self.
            if (b._selfCharId != 0 && m.CharId == b._selfCharId)
                b._book.ApplyMixElf(m.MixTemplateId);
        });
        Bind(new ZoneUpdateElfMixElf(), (b, m) =>
            b._book.ApplyMixResult(m));
        Bind(new ZonePetUpgradeCasting(), (b, m) =>
            b._book.ApplyUpgradeCasting(m.Ticks));
        Bind(new ZonePetUpgradeCancel(), (b, m) =>
            b._book.ApplyUpgradeCancel());
        Bind(new ZoneUpdateElfSpell(), (b, m) =>
            b._book.ApplySpellUpdate(m));
        Bind(new ZoneChangeElf(), (b, m) =>
        {
            if (b._selfCharId != 0 && m.CharId == b._selfCharId)
                GD.Print($"[elf] 431 change elf {m.ElfDefId} «{m.ElfName}»");
        });
        Bind(new ZonePlayElfPetAction(), (b, m) =>
        {
            // Le serveur relaie l'ordre au pet live (handle = sight
            // handle du mob −2537). Les cmds 6/7/8 = SetEventAction
            // natif — le mapping emote→anim kfId n'est pas prouvé,
            // on se borne à logguer (résiduel documenté).
            GD.Print($"[elf] 470 pet action handle=0x{m.PetHandle:x8} " +
                     $"cmd={m.Cmd}");
            var anims = PetClientRules.EmoteAnims(m.Cmd);
            if (anims.Length > 0 && b._registry != null &&
                b._registry.Entities.TryGetValue((uint)m.PetHandle,
                    out var pe) && pe.Animator != null)
                pe.Animator.PlayOnceAny(anims);
        });
        Bind(new ZoneElfEvent(), (b, m) =>
            GD.Print($"[elf] 417 event elf={m.ElfId} ev={m.Event}"));
        Bind(new ZoneElfUpdateSkill(), (b, m) =>
            GD.Print($"[elf] 419 update skill f1={m.Field1} " +
                     $"f5={m.Field5}"));
        Bind(new ZoneElfUpdateSoulAbility(), (b, m) =>
            GD.Print($"[elf] 420 soul ability f1={m.Field1}"));
        Bind(new ZoneElfAction(), (b, m) =>
            GD.Print($"[elf] 442 action f1={m.Field1} f4={m.Field4}"));
        GD.Print("[elf] binds 412/413/414/417/418/419/420/424/431/" +
            "442/470/513/567/568/574/575/588 posés (early)");
    }

    // -------------------- actions C→S ------------------------------

    /// <summary>Action de la fenêtre — dispatch vers le NCID prouvé.</summary>
    private void OnAction(string action, int loc, string arg)
    {
        var elf = _book.ByLoc(loc);
        switch (action)
        {
            case "battle":
                // Invoquer ↔ renvoyer : 107 ToWork mode 2 / 0 natif.
                if (elf != null && elf.Status == 2)
                    Send(new ZoneElfWork { Loc = (short)loc, Mode = 0 });
                else
                    Send(new ZoneElfWork { Loc = (short)loc, Mode = 2 });
                break;
            case "pray":
                // Intégration (mix) ↔ repos : ToWork mode 1 / 0.
                if (elf != null && elf.Status == 1)
                    Send(new ZoneElfWork { Loc = (short)loc, Mode = 0 });
                else
                    Send(new ZoneElfWork { Loc = (short)loc, Mode = 1 });
                break;
            case "release":
                Send(new ZoneElfReturn
                {
                    Loc = (short)loc, ToEgg = 0,
                    SealBag = -1, SealSlot = -1,
                });
                break;
            case "seal":
                Send(new ZoneElfReturn
                {
                    Loc = (short)loc, ToEgg = 1,
                    SealBag = -1, SealSlot = -1,
                });
                break;
            case "rename":
                if (arg.Length > 0)
                    Send(new ZoneElfChangeName
                    { Loc = (short)loc, NewName = arg });
                break;
            case "feed":
                if (elf != null)
                    Send(new ZoneElfRevive { ElfId = (short)elf.ElfId });
                break;
            case "caress":
                if (elf != null)
                    Send(new ZoneElfTalk { ElfId = (short)elf.ElfId });
                break;
            case "passive":
                _passive = !_passive;
                Send(new ZonePetModeChange
                { Mode = (short)(_passive ? 2 : 1) });
                break;
            case "petmode":
                // PetFunctionBar : mode explicite (case cochée =
                // combat 1, décochée = loot/passif 2).
                if (int.TryParse(arg, out var pm))
                {
                    _passive = pm == 2;
                    Send(new ZonePetModeChange { Mode = (short)pm });
                }
                break;
            case "attack":
                Send(new ZoneSendPetCmd
                {
                    Instruction = 1,
                    Target = (int)(_hud.Target?.TargetHandle ?? 0),
                });
                break;
            case "petcmd":
                if (int.TryParse(arg, out var instr))
                    Send(new ZoneSendPetCmd
                    {
                        Instruction = (short)instr,
                        Target = (int)(_hud.Target?.TargetHandle ?? 0),
                    });
                break;
            case "fuse":
                if (int.TryParse(arg, out var consumed))
                {
                    // Catalyseur réel (0x1790-0x1792) du sac s'il y en
                    // a un ; sinon −1/−1 (accepté par le natif).
                    var (mb, ms) = PetClientRules.PickCatalyst(
                        InventoryWindow.AllItems().Select(i =>
                            (i.Bag, i.Slot, i.ItemId)));
                    Send(new ZoneElfMix
                    {
                        LocMain = (short)loc,
                        LocConsumed = (short)consumed,
                        MaterialBag = (short)mb, MaterialSlot = (short)ms,
                    });
                }
                break;
            case "petequip":
                // arg = "bag,bagSlot,elfSlot" — drag sac → slot gear.
                if (arg.Split(',') is { Length: 3 } pe &&
                    int.TryParse(pe[0], out var eb) &&
                    int.TryParse(pe[1], out var es) &&
                    int.TryParse(pe[2], out var ei))
                    Send(PetClientRules.EquipMessage(eb, es, loc, ei));
                break;
            case "petunequip":
                if (int.TryParse(arg, out var us))
                    Send(PetClientRules.UnequipMessage(loc, us));
                break;
            case "petswap":
                // arg = "from,to" — paires adjacentes uniquement.
                if (arg.Split(',') is { Length: 2 } sw &&
                    int.TryParse(sw[0], out var sf) &&
                    int.TryParse(sw[1], out var st2))
                    Send(PetClientRules.SwapMessage(loc, sf, st2));
                break;
        }
    }

    private void Send(NetMessage msg)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.Print($"[elf] {msg.Name} — pas de session zone");
            return;
        }
        var send = s.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[elf] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // -------------------- résolution entité elf --------------------

    /// <summary>Résolution stem+nom du pet elf invoqué (entityId
    /// −2537, kind 12, Unk48 = owner). Le wire ne porte PAS le
    /// template — pour le self on le déduit de l'elf invoqué du
    /// livre (status 2) ; pour les autres joueurs on ne connaît que
    /// le nom porté par le 298.</summary>
    private (string Model, string Name, int Level)? ResolveElfPet(
        int ownerCharId)
    {
        if (_selfCharId == 0)
            _selfCharId = (uint)(SessionCarry.Character?.Id ?? 0);
        if (ownerCharId != 0 && (uint)ownerCharId != _selfCharId)
            return null;
        var s = _book.Summoned;
        if (s == null) return null;
        var stem = PartnerTable.ModelStemFor(s.TemplateId);
        var name = ElfWindow.DisplayName(s);
        return stem.Length > 0 || name.Length > 0
            ? (stem, $"Lv{s.Level} {name}", s.Level) : null;
    }

    // -------------------- injection de test ------------------------

    /// <summary>Injecte un tableau de démonstration par les vrais
    /// handlers du livre (ApplySnapshot) — chemin identique au 412
    /// réel, pour les comptes sans familier (--elf-test).</summary>
    private void InjectTestElfs()
    {
        var m = new ZoneEnterNodeElfInfo();
        var mk = (short loc, int tpl, short lvl, short st) =>
            new ElfRecord
            {
                Loc = loc, ElfId = 9000 + loc,
                OwnerCharId = (int)_selfCharId,
                TemplateId = tpl, Level = lvl, Exp = 450, Hp = 620,
                Status = st, Quality = 1, Name = "",
                MixStar = (short)(loc + 1),
                Battle = { [0] = 58751, [1] = 58762, [2] = 58775 },
                Integrate = { [0] = 58801 },
            };
        m.Elfs.Add(mk(0, 21834, 22, 2));
        m.Elfs.Add(mk(1, 21850, 13, 0));
        m.Elfs.Add(mk(2, 21845, 5, 1));
        _book.ApplySnapshot(m);
        GD.Print("[elf] --elf-test : 3 familiers factices injectés " +
            "via le handler 412");
    }

    // ------------------------------------------------------------------
    // --pet-test [--pet-shot=<racine>] — cycle live complet du lot U12,
    // fil réel uniquement (aucun paquet injecté côté client) :
    //   412 (elf en DB) → « battle » (107 mode 2, invocation) → 513 →
    //   shot « _pet » (PetFunctionBar + pet en monde) →
    //   //clone_monster 61167 (drop S_DropItem 100 %) → 124 sélection →
    //   444 → pression réelle du slot1 (_pfb0) → 188 attaque (272) →
    //   //kill 61167 → loot → sac plein → 329 trésor → clic droit →
    //   35 → 0x4f9 (le trésor persiste — natif) → 6 DropItem (slot
    //   libéré) → clic droit → 35 → 386 + 330 → shot « _loot » →
    //   « battle » (107 mode 0, rappel).
    // ------------------------------------------------------------------

    private int _petTestStep = -2;
    private double _petTestT;
    private string _petTestShot = "";
    private string? _petPendingShot;
    private uint _petMobHandle, _petTreasureHandle;
    private int _petPickupSent;
    /// <summary>Mob à cloner — 61167 (drop 100 % item 715) par
    /// défaut ; 63402 (drop équipement 28585, jamais stackable)
    /// pour forcer le trésor sur un sac plein.</summary>
    private int _petTestMob = 61167;

    private void PetNext(int s) { _petTestStep = s; _petTestT = 0; }

    private void PetTestFrame(bool sent, ushort ncid, byte[] _)
    {
        if (sent && ncid == 35) _petPickupSent++;
    }

    private void PetFail(string why)
    {
        GD.PrintErr($"[pet-test] FAIL step{_petTestStep}: {why}");
        _petTestStep = -2;
    }

    private void PetShot(string suffix)
    {
        if (_petTestShot.Length == 0) return;
        RenderingServer.ForceDraw();
        var img = GetViewport().GetTexture()?.GetImage();
        var root = System.IO.Path.IsPathRooted(_petTestShot)
            ? _petTestShot : System.IO.Path.GetFullPath(_petTestShot);
        var err = img?.SavePng($"{root}_{suffix}.png")
            ?? Error.FileNotFound;
        GD.Print($"[pet-shot] {root}_{suffix}.png err={err}");
    }

    private RemoteEntity? FindMob(int mobId)
    {
        if (_registry == null) return null;
        foreach (var (_, e) in _registry.Entities)
            if (e.Kind == 12 && e.EntityId == mobId) return e;
        return null;
    }

    /// <summary>Trésor au sol = entité kind 0 avec un entityId non
    /// nul (les joueurs ont 0 — convention du picking
    /// TreasurePickup).</summary>
    private RemoteEntity? FindTreasure()
    {
        if (_registry == null) return null;
        foreach (var (_, e) in _registry.Entities)
            if (e.Kind == 0 && e.EntityId != 0) return e;
        return null;
    }

    /// <summary>Démarre le ramassage du trésor courant par le chemin
    /// fil réel de <c>TreasurePickup.BeginPickup</c> (124 + approche +
    /// 35). Le picking écran n'est pas rejoué : l'input synthétique
    /// passait sous des Controls HUD avant d'atteindre
    /// <c>_UnhandledInput</c> (no-op silencieux en live).</summary>
    private bool ClickTreasure()
    {
        if (_registry == null ||
            !_registry.Entities.TryGetValue(_petTreasureHandle,
                out var t))
            return false;
        var loot = GetTree().Root
            .FindChild("TreasurePickup", true, false)
            as Combat.TreasurePickup;
        if (loot == null) return false;
        loot.BeginPickup(t);
        return true;
    }

    private void TickPetTest(double delta)
    {
        if (_petTestStep == -2) return;
        _petTestT += delta;
        switch (_petTestStep)
        {
            case -1: // armement : session + 412 reçu
                if (_session == null || _registry == null ||
                    _book.Elfs.Count == 0) return;
                GD.Print($"[pet-test] elfs={_book.Elfs.Count} " +
                    $"shot='{_petTestShot}'");
                // Le Frame de session compte les 35 partis — le check
                // « sac plein » n'est valide que si le ramassage a
                // RÉELLEMENT été émis (sinon le trésor persiste sans
                // preuve de refus).
                _session.Frame += PetTestFrame;
                // Status 2 persisté (run précédent tué avant rappel —
                // natif : l'elf reste invoqué entre sessions) → on le
                // rappelle d'abord pour repartir d'un état connu.
                if (_book.Summoned == null) PetNext(0);
                else { OnAction("battle", 0, ""); PetNext(12); }
                break;
            case 12: // 513 flag=0 → elf renvoyé, état propre
                if (_book.Summoned == null) PetNext(0);
                else if (_petTestT > 15)
                    PetFail("513 flag=0 jamais reçu");
                break;
            case 0: // invocation réelle (107 mode 2)
                if (_petTestT < 3) return;
                OnAction("battle", 0, "");
                PetNext(1);
                break;
            case 1: // 513 → elf invoqué (barre visible)
                if (_book.Summoned != null) PetNext(2);
                else if (_petTestT > 15) PetFail("513 jamais reçu");
                break;
            case 2: // pose → shot barre+pet → clone à drop garanti
                if (_petTestT < 1.5) return;
                _petPendingShot = "pet";
                Send(new ZoneTextCommand
                    { Text = $"//clone_monster {_petTestMob}" });
                PetNext(3);
                break;
            case 3: // mob spawné → sélection (124)
                if (FindMob(_petTestMob) is { } mob)
                {
                    _petMobHandle = mob.Handle;
                    Send(new ZoneTargetUpdate
                        { Value = (int)mob.Handle });
                    PetNext(4);
                }
                else if (_petTestT > 15)
                    PetFail($"mob {_petTestMob} jamais spawné");
                break;
            case 4: // 444 → cible acquise → pression réelle slot1 (188)
                if (_hud.Target?.TargetHandle == _petMobHandle)
                {
                    var bar = _hud.FindChild("PetBar", true, false);
                    // Bouton « vivant » du Slot1 (ordre d'attaque) —
                    // les nœuds morts en file de free gardent leur
                    // place jusqu'en fin de frame.
                    var slot = bar?.FindChildren("*Slot1*", "", true,
                        false).OfType<Control>().FirstOrDefault();
                    var btn = slot?.GetChildren().OfType<Button>()
                        .LastOrDefault(b => !b.IsQueuedForDeletion());
                    if (btn == null)
                    { PetFail("bouton attaque Slot1 absent"); return; }
                    btn.EmitSignal(BaseButton.SignalName.Pressed);
                    PetNext(5);
                }
                else if (_petTestT > 10)
                    PetFail("444 cible jamais reçu");
                break;
            case 5: // le pet tape (272) → kill → trésor (sac plein)
                if (_petTestT < 4) return;
                Send(new ZoneTextCommand { Text = $"//kill {_petTestMob}" });
                PetNext(6);
                break;
            case 6: // 329 → entité trésor kind 0
                if (FindTreasure() is { } tr)
                { _petTreasureHandle = tr.Handle; PetNext(7); }
                else if (_petTestT > 15)
                    PetFail("329 trésor jamais reçu");
                break;
            case 7: // clic droit → 35 → sac plein : le trésor PERSISTE
                if (_petTestT < 0.5) return;
                if (!ClickTreasure())
                { PetFail("trésor hors écran"); return; }
                PetNext(8);
                break;
            case 8: // 0x4f9 : 35 émis + trésor encore là = refus réel
                if (_petTestT < 4) return;
                if (_petPickupSent == 0)
                { PetFail("35 jamais émis (BeginPickup sans effet)"); return; }
                if (_registry == null ||
                    _registry.Entities.ContainsKey(_petTreasureHandle))
                {
                    GD.Print("[pet-test] sac plein confirmé — " +
                        "35 refusé, trésor persistant");
                    // DropItem natif (6) : détruit un item sac → le
                    // prochain ramassage trouve un slot libre.
                    Send(new ZoneServerDropItem
                    { ContainerType = 0, Slot = 8, ContainerIndex = 0 });
                    PetNext(9);
                }
                else
                {
                    // Le 35 a réussi (slot libre trouvé) — on saute
                    // l'étape DropItem.
                    _petPendingShot = "loot";
                    PetNext(10);
                }
                break;
            case 9: // slot libéré → nouveau clic → 35 → 386+330
                if (_petTestT < 1.5) return;
                if (!ClickTreasure())
                { PetFail("reclic impossible"); return; }
                PetNext(10);
                break;
            case 10: // trésor ramassé → despawn du registry (330)
                if (_registry == null ||
                    !_registry.Entities.ContainsKey(_petTreasureHandle))
                { _petPendingShot = "loot"; PetNext(11); }
                else if (_petTestT > 15)
                    PetFail("35/386 sans effet");
                break;
            case 11: // rappel de l'elf (107 mode 0)
                if (_petTestT < 1) return;
                OnAction("battle", 0, "");
                GD.Print("[pet-test] done");
                _petTestStep = -2;
                break;
        }
    }
}
