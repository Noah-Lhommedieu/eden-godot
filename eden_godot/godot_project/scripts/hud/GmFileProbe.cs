using Godot;
using System.Globalization;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>Canal de commandes GM par fichier (<c>--cmd-file=path</c>) :
/// chaque nouvelle ligne du fichier est envoyée comme
/// <see cref="ZoneTextCommand"/> (NCID 3) une fois la zone connectée —
/// pilotage de session live pour les captures/gameplay sans passer par
/// l'EditBox du chat (injection XTEST peu fiable). Diag uniquement.</summary>
public partial class GmFileProbe : Node
{
    private string _path = "";
    private long _offset;
    private double _acc;
    private double _hb;

    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public GmFileProbe(string path) { _path = path; }

    public override void _Ready()
    {
        if (File.Exists(_path))
            _offset = new FileInfo(_path).Length;
        GD.Print($"[gmfile] canal actif sur {_path}");
    }

    public override void _Process(double delta)
    {
        _acc += delta;
        _hb += delta;
        if (_hb > 10)
        {
            _hb = 0;
            var fiD = new FileInfo(_path);
            GD.Print($"[gmfile] hb sess={(SessionCarry.Session == null ? "null" : "ok")} " +
                $"zone={(SessionCarry.Session?.Zone?.IsConnected == true)} " +
                $"len={(fiD.Exists ? fiD.Length : -1)} off={_offset}");
        }
        if (_acc < 0.5) return;
        _acc = 0;
        var s = SessionCarry.Session;
        if (s == null) return;
        string pending;
        try
        {
            var fi = new FileInfo(_path);
            if (fi.Exists && fi.Length < _offset) _offset = 0;
            if (!fi.Exists || fi.Length <= _offset) return;
            using var fs = new FileStream(_path, FileMode.Open,
                System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            fs.Seek(_offset, SeekOrigin.Begin);
            using var rd = new StreamReader(fs);
            pending = rd.ReadToEnd();
            _offset = fs.Position;
        }
        catch (IOException) { return; }
        foreach (var raw in pending.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Lignes « @action args… » = input local injecté dans le
            // viewport (clic fiable — XTEST saute sous Wayland). Le
            // reste part en texte zone (« //commande » native).
            if (line[0] == '@') { InjectInput(line); continue; }
            var task = s.SendZoneAsync(new ZoneTextCommand { Text = line },
                CancellationToken.None);
            var l = line;
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[gmfile] envoi «{l}» : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
            GD.Print($"[gmfile] → {line}");
        }
    }

    /// <summary>@click x y [l|r] / @rclick x y / @key <nom> : injecte un
    /// événement souris/touche dans le viewport (coordonnées logiques
    /// 1280×720 du jeu, pas l'écran). Pressed+Released en une frame pour
    /// les clics ; la touche est posée puis relevée.</summary>
    private void InjectInput(string line)
    {
        // L'injection input n'a pas besoin de la session zone (elle
        // agit comme un vrai clic/touche). Les paquets C→S émis par
        // les commandes @ utilisent s ci-dessous — l'échec d'envoi
        // reste visible via les erreurs réseau normales.
        var s = SessionCarry.Session;
        if (s == null) return;
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 && p[0] is not ("@inv" or "@closeall" or "@auctq" or "@wins" or "@optclick"
                or "@mallcheapest")) return;
        switch (p[0])
        {
            case "@click" or "@rclick" or "@dclick":
                if (p.Length < 3) return;
                var btn = p[0] == "@rclick" ? MouseButton.Right
                    : p.Length > 3 && p[3] == "r" ? MouseButton.Right
                    : MouseButton.Left;
                var pos = new Vector2(float.Parse(p[1], CI), float.Parse(p[2], CI));
                var press = new InputEventMouseButton
                {
                    Position = pos, ButtonIndex = btn, Pressed = true,
                    DoubleClick = p[0] == "@dclick",
                };
                var rel = new InputEventMouseButton
                {
                    Position = pos, ButtonIndex = btn, Pressed = false,
                };
                Input.ParseInputEvent(new InputEventMouseMotion
                    { Position = pos });
                Input.ParseInputEvent(press);
                // Relâché différé ~80 ms : les Controls Godot exigent
                // press/release sur deux frames pour un clic réel.
                var relEv = rel;
                GetTree().CreateTimer(0.08).Timeout += () =>
                    Input.ParseInputEvent(relEv);
                if (p[0] == "@dclick")
                    GetTree().CreateTimer(0.16).Timeout += () =>
                    {
                        Input.ParseInputEvent(press);
                        var r2 = (InputEventMouseButton)relEv.Duplicate();
                        Input.ParseInputEvent(r2);
                    };
                GD.Print($"[gmfile] @click {pos} {btn}");
                break;
            case "@drag":
                // @drag x1 y1 x2 y2 — vrai drag GUI : press à (x1,y1),
                // motions intermédiaires, release à (x2,y2). Sert aux
                // drops dans les slots (combine, enclume, boîte…).
                if (p.Length < 5) return;
                var from = new Vector2(float.Parse(p[1], CI),
                    float.Parse(p[2], CI));
                var to = new Vector2(float.Parse(p[3], CI),
                    float.Parse(p[4], CI));
                Input.ParseInputEvent(new InputEventMouseMotion
                    { Position = from });
                Input.ParseInputEvent(new InputEventMouseButton
                {
                    Position = from, ButtonIndex = MouseButton.Left,
                    Pressed = true,
                });
                GetTree().CreateTimer(0.1).Timeout += () =>
                {
                    Input.ParseInputEvent(new InputEventMouseMotion
                    {
                        Position = from.Lerp(to, 0.5f),
                        ButtonMask = MouseButtonMask.Left,
                    });
                    GetTree().CreateTimer(0.1).Timeout += () =>
                    {
                        Input.ParseInputEvent(new InputEventMouseMotion
                        {
                            Position = to,
                            ButtonMask = MouseButtonMask.Left,
                        });
                        GetTree().CreateTimer(0.1).Timeout += () =>
                            Input.ParseInputEvent(
                                new InputEventMouseButton
                                {
                                    Position = to,
                                    ButtonIndex = MouseButton.Left,
                                    Pressed = false,
                                });
                    };
                };
                GD.Print($"[gmfile] @drag {from}→{to}");
                break;
            case "@move":
                // @move x y — motion seule (tooltip sans clic).
                Input.ParseInputEvent(new InputEventMouseMotion
                {
                    Position = new Vector2(float.Parse(p[1], CI),
                        float.Parse(p[2], CI)),
                });
                break;
            case "@cast":
                // @cast <spellId> — même chemin que la hotkey/clic
                // droit hotbar (SkillCast.Cast : gate portée/classe,
                // choix ToSelf/ToLife/ToLocation selon le sort).
                Combat.SkillCast.Instance?.Cast(
                    (ushort)int.Parse(p[1]));
                GD.Print($"[gmfile] @cast {p[1]}");
                break;
            case "@attack":
                // @attack [mobId] — engage l'auto-attack (même chemin
                // que clic droit mob / hotkey Attack, sans picking).
                if (GetTree().CurrentScene is EdenClient.GameScene ga)
                    ga.AttackMobId(p.Length > 1 ? int.Parse(p[1]) : 0);
                break;
            case "@uiat":
                // @uiat x y — déplace la souris et imprime le Control survolé
                // (diagnostic : qui reçoit réellement le clic ?).
                if (p.Length >= 3)
                {
                    var hp = new Vector2(float.Parse(p[1], CI), float.Parse(p[2], CI));
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = hp });
                    GetTree().CreateTimer(0.2).Timeout += () =>
                    {
                        var hc = GetViewport().GuiGetHoveredControl();
                        GD.Print($"[gmfile] @uiat {hp} → {(hc == null ? "(aucun)" : hc.GetPath() + " filtre=" + hc.MouseFilter)}");
                    };
                }
                break;
            case "@tree":
                // @tree <fragment> [profondeur] — arbre des Controls (rect global, filtre souris)
                // sous le premier Control visible dont le chemin contient le fragment.
                if (p.Length >= 2)
                {
                    var depth = p.Length > 2 ? int.Parse(p[2]) : 4;
                    var rootC = GetTree().Root.FindChildren("*", "Control", true, false)
                        .OfType<Control>().FirstOrDefault(c => c.IsVisibleInTree() &&
                            c.GetPath().ToString().Contains(p[1]));
                    if (rootC == null) { GD.Print($"[gmfile] @tree {p[1]} : introuvable"); break; }
                    void Dump(Node n, int d)
                    {
                        if (n is Control c)
                            GD.Print($"[tree] {new string(' ', d * 2)}{n.Name} {n.GetType().Name} " +
                                     $"rect={c.GetGlobalRect()} filtre={c.MouseFilter} vis={c.Visible}");
                        if (d < depth) foreach (var ch in n.GetChildren()) Dump(ch, d + 1);
                    }
                    Dump(rootC, 0);
                }
                break;
            case "@optclick":
                // @optclick <n> — VRAI clic au centre de la n-ième option (0 = 1ʳᵉ) du dialogue
                // PNJ ouvert (leur position dépend de la longueur du texte).
                {
                    var idx = p.Length > 1 ? int.Parse(p[1]) : 0;
                    var opts = GetTree().Root.FindChildren("__options", "Control", true, false)
                        .OfType<Control>().FirstOrDefault(c => c.IsVisibleInTree());
                    var row = opts != null && idx < opts.GetChildCount() ? opts.GetChild(idx) as Control : null;
                    if (row == null) { GD.Print($"[gmfile] @optclick {idx} : option introuvable"); break; }
                    var c0 = row.GetGlobalRect().GetCenter();
                    GD.Print($"[gmfile] @optclick {idx} « {row.GetChildren().OfType<Label>().FirstOrDefault()?.Text} » ({c0.X:0},{c0.Y:0})");
                    InjectInput($"@click {c0.X.ToString(CI)} {c0.Y.ToString(CI)}");
                }
                break;
            case "@npcclick":
                // @npcclick <npcId> — VRAI clic droit au pixel sur le PNJ (picking 3D,
                // approche, dialogue) : le parcours joueur réel, sans raccourci @talk.
                if (GetTree().CurrentScene is EdenClient.GameScene gsc &&
                    gsc.NpcScreenPos(int.Parse(p[1])) is { } sp)
                {
                    GD.Print($"[gmfile] @npcclick {p[1]} écran=({sp.X:0},{sp.Y:0})");
                    InjectInput($"@rclick {sp.X.ToString(CI)} {sp.Y.ToString(CI)}");
                }
                else GD.Print($"[gmfile] @npcclick {p[1]} : PNJ hors de vue ou derrière la caméra");
                break;
            case "@talk":
                if (GetTree().CurrentScene is EdenClient.GameScene gs)
                    gs.TalkToNpcId(int.Parse(p[1]));
                break;
            case "@walk":
                // @walk <x> <z> — marche point&click vers (x,z) Eden
                // (même chemin que le routeur du QuestTracker : A* sur
                // la grille .smp + SetMovePath). Sert à traverser les
                // zones de trigger que le //tp ne déclenche pas.
                AutoRoute.ToEden(this, float.Parse(p[1], CI),
                    float.Parse(p[2], CI));
                break;
            case "@walkx":
                // @walkx <x> <z> — marche directe sans A* (rampes et
                // passerelles absentes de la grille .smp).
                if (GetTree().Root.FindChild("Player", true, false)
                        is PlayerController pc)
                {
                    var g = WorldConv.ToGodot(float.Parse(p[1], CI),
                        float.Parse(p[2], CI), 0f);
                    pc.SetMoveTarget(new Vector3(g.X,
                        pc.GlobalPosition.Y, g.Z));
                    GD.Print($"[gmfile] @walkx {p[1]},{p[2]}");
                }
                break;
            case "@portal":
                // @portal <keyId> — CheckNodeExit (C→S 15) comme
                // WatchGateways quand le perso entre dans l'aire.
                _ = s.SendZoneAsync(new ZoneCheckNodeExit
                {
                    NodeId = (short)int.Parse(p[1]),
                    PortalId = 0,
                }, CancellationToken.None);
                GD.Print($"[gmfile] @portal {p[1]}");
                break;
            case "@pickup":
                // @pickup <treasureId> — NCID 35 ramassage direct
                // (le //tele place à portée 4 u du trésor).
                _ = s.SendZoneAsync(new ZoneTreasurePickup
                    { TreasureId = uint.Parse(p[1]) },
                    CancellationToken.None);
                GD.Print($"[gmfile] @pickup {p[1]}");
                break;
            case "@revive":
                // @revive <type> — NCID 20 (0=savepoint, 1=ici,
                // 5=item, 6=feuille) — même paquet que les boutons
                // du DeathWindow.
                if (GetTree().CurrentScene is EdenClient.GameScene gr)
                    _ = gr.SendReviveAsync(int.Parse(p[1]));
                GD.Print($"[gmfile] @revive {p[1]}");
                break;
            case "@teamop":
                // @teamop <op> <p1> <p2> — NCID 79 TeamOperation brut,
                // le paquet réel de la fenêtre trial native :
                // [4][3][nodeTid] lance le trial evaluate, [5][n]
                // répond au consentement 577.
                _ = s.SendZoneAsync(new ZoneServerTeamOperation
                {
                    Op = (short)int.Parse(p[1]),
                    Param1 = (short)int.Parse(p[2]),
                    Param2 = (short)int.Parse(p[3]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @teamop {p[1]},{p[2]},{p[3]}");
                break;
            case "@dj":
                // @dj <node> — choix d'intensité donjon (C→S 9061,
                // même paquet que le bouton Solo/Multi du portail).
                DungeonUi.Enter(int.Parse(p[1]));
                GD.Print($"[gmfile] @dj {p[1]}");
                break;
            case "@dlg":
                // @dlg <index> — choisit l'option de dialogue comme un
                // clic (NCID 11, index dans la liste reçue). Le clic
                // natif ferme aussi la fenêtre (HudNetBridge.
                // OnDialogOption) — un service ouvre la sienne, un
                // nouveau 285 réaffiche si le dialogue continue.
                _ = s.SendZoneAsync(new ZoneReplyDialog
                    { DialogId = (short)int.Parse(p[1]) },
                    CancellationToken.None);
                foreach (var d in GetTree().Root
                        .FindChildren("*NpcDialogue*", "Control", true, false)
                        .OfType<Control>())
                    d.Visible = false;
                GD.Print($"[gmfile] @dlg {p[1]}");
                break;
            case "@quest":
                // @quest <op> <mid> <npcUid> — NCID 87 MissionListOp
                // (op : 1 Accept, 2 Reward, 4 Finish…) — même paquet que
                // les boutons de la fiche quête.
                _ = s.SendZoneAsync(new ZoneMissionListOp
                {
                    Field1 = (short)int.Parse(p[1]),
                    Field2 = int.Parse(p[2]),
                    Field3 = (ushort)int.Parse(p[3]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @quest op={p[1]} mid={p[2]}");
                break;
            case "@giveup":
                // @giveup <mid> — NCID 51 GiveUpMission, même paquet
                // que le bouton « Abandonner » du journal.
                _ = s.SendZoneAsync(new ZoneGiveUpMission
                    { MissionId = (short)int.Parse(p[1]) },
                    CancellationToken.None);
                GD.Print($"[gmfile] @giveup {p[1]}");
                break;
            case "@act":
                // @act <action> <p1> <p2> — NCID 14 PlayerAction, même
                // paquet que les boutons de fonctions (13 = fortify).
                _ = s.SendZoneAsync(new ZonePlayerAction
                {
                    Action = byte.Parse(p[1]),
                    Param1 = short.Parse(p[2]),
                    Param2 = short.Parse(p[3]),
                    Param3 = -1,
                }, CancellationToken.None);
                GD.Print($"[gmfile] @act {p[1]}");
                break;
            case "@str":
                // @str <combineId> <tb> <ts> <mb> <ms> <cb> <cs> [quick]
                // — NCID 119 StrengthenItem, même paquet que le bouton
                // Renforcer (cb/cs=-1 = pas de pierre de chance).
                _ = s.SendZoneAsync(new ZoneServerStrengthenItem
                {
                    Field1 = 0,
                    Field2 = int.Parse(p[1]),
                    Field3 = short.Parse(p[2]),
                    Field4 = short.Parse(p[3]),
                    Field5 = short.Parse(p[4]),
                    Field6 = short.Parse(p[5]),
                    Field7 = short.Parse(p[6]),
                    Field8 = short.Parse(p[7]),
                    Field9 = (short)(p.Length > 8 ? int.Parse(p[8]) : 1),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @str combine={p[1]} t={p[2]},{p[3]} " +
                    $"m={p[4]},{p[5]} c={p[6]},{p[7]}");
                break;
            case "@buy":
                // @buy <slot> [n] — achat comme le double-clic de ligne
                // (NCID 22, slot = index dans le store ouvert).
                _ = s.SendZoneAsync(new ZoneBuyItems
                {
                    ShopIndex = (ushort)int.Parse(p[1]),
                    ItemIndex = (ushort)(p.Length > 2 ? int.Parse(p[2]) : 1),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @buy {p[1]}");
                break;
            case "@sell":
                // @sell <slot> [n] [conteneur] — vente NCID 23 (même
                // paquet que le clic droit sac en boutique).
                {
                    var m = new ZoneSellItems();
                    m.Items.Add(((ushort)int.Parse(p[1]),
                        (ushort)(p.Length > 2 ? int.Parse(p[2]) : 1),
                        (ushort)(p.Length > 3 ? int.Parse(p[3]) : 0)));
                    _ = s.SendZoneAsync(m, CancellationToken.None);
                    GD.Print($"[gmfile] @sell {p[1]}");
                }
                break;
            case "@buyback":
                // @buyback <index> — rachat NCID 22 BuyBack=1.
                _ = s.SendZoneAsync(new ZoneBuyItems
                {
                    ShopIndex = (ushort)int.Parse(p[1]), ItemIndex = 1,
                    BuyBack = 1,
                }, CancellationToken.None);
                GD.Print($"[gmfile] @buyback {p[1]}");
                break;
            case "@drop":
                // @drop <slot> — NCID 6 DropItem : détruit l'item du
                // sac 0 à la loc (même paquet que le drag hors du sac).
                _ = s.SendZoneAsync(new ZoneServerDropItem
                {
                    ContainerType = 0, ContainerIndex = 0,
                    Slot = (short)int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @drop {p[1]}");
                break;
            case "@use":
                // @use <slot> — NCID 19 ItemUse sur l'item du sac 0 à
                // la loc donnée (même paquet que le clic droit).
                _ = s.SendZoneAsync(new ZoneItemUse
                {
                    ContainerType = 0, ContainerIndex = 0,
                    Slot = (short)int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @use {p[1]}");
                break;
            case "@useon":
                // @useon <slot> <tgtCont> <tgtIdx> <tgtLoc> — NCID 19
                // item-sur-item (gemme→équipement, scroll→cible) :
                // même paquet que le drag d'un parchemin sur un item.
                _ = s.SendZoneAsync(new ZoneItemUse
                {
                    ContainerType = 0, ContainerIndex = 0,
                    Slot = (short)int.Parse(p[1]),
                    TargetContainerType = (short)int.Parse(p[2]),
                    TargetContainerIndex = (short)int.Parse(p[3]),
                    TargetLoc = (short)int.Parse(p[4]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @useon {p[1]}→{p[2]},{p[3]},{p[4]}");
                break;
            case "@combine":
                // @combine <recipe> <catSlot> [boostSlot] — NCID 170
                // PlayerWork de la station combine (op 10) : catalyseur
                // au slot, booster optionnel (-1 sinon).
                _ = s.SendZoneAsync(new ZonePlayerWork
                {
                    WorkId = (uint)int.Parse(p[1]),
                    CatalystBag = 0,
                    CatalystSlot = (short)int.Parse(p[2]),
                    BoostBag = (short)(p.Length > 3 ? 0 : -1),
                    BoostSlot = (short)(p.Length > 3
                        ? int.Parse(p[3]) : -1),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @combine recipe={p[1]} cat={p[2]}");
                break;
            case "@mallbuy":
                // @mallbuy <group> <index> <moneyUnit> [amount] | auto
                // — NCID world 23 ItemMallBuyRequest, même paquet que
                // le bouton Acheter (Key = mu*100+group). « auto »
                // prend l'entrée pvalues la moins chère (RunMallTest).
                ItemMallEntry? me;
                if (p[1] == "auto")
                {
                    _ = s.SendWorldAsync(new WorldItemMallPointInfoQuery(),
                        CancellationToken.None);
                    _ = s.SendWorldAsync(new WorldItemMallListQuery
                        { Version = ItemMallState.Version },
                        CancellationToken.None);
                    _ = s.SendWorldAsync(new WorldItemMallAnythingQuery(),
                        CancellationToken.None);
                    GD.Print($"[gmfile] @mallbuy auto — " +
                        $"catalogue={ItemMallState.Entries.Count} " +
                        $"pts={ItemMallState.Points}");
                    break;
                }
                me = ItemMallState.Entries.Values.FirstOrDefault(
                    x => x.Group == int.Parse(p[1]) &&
                         x.Index == int.Parse(p[2]) &&
                         x.MoneyUnit == int.Parse(p[3]));
                _ = s.SendWorldAsync(new WorldItemMallBuyRequest
                {
                    Key = (short)(int.Parse(p[3]) * 100 +
                        int.Parse(p[1])),
                    Index = (short)int.Parse(p[2]),
                    Amount = (short)(p.Length > 4
                        ? int.Parse(p[4]) : 1),
                    Version = ItemMallState.Version,
                }, CancellationToken.None);
                GD.Print($"[gmfile] @mallbuy g={p[1]} i={p[2]} mu={p[3]}" +
                    (me != null ? $" item={me.ItemId}" : ""));
                break;
            case "@mallcheapest":
                // @mallcheapest — achète l'entrée pvalues la moins
                // chère du catalogue chargé (driver RunMallTest).
                {
                    var e = ItemMallState.Entries.Values
                        .Where(x => x.MoneyUnit == 1 &&
                            x.EffectivePrice > 0 && x.ItemNum > 0)
                        .OrderBy(x => x.EffectivePrice).FirstOrDefault();
                    if (e == null)
                    {
                        GD.Print("[gmfile] @mallcheapest : " +
                            "catalogue vide");
                        break;
                    }
                    _ = s.SendWorldAsync(new WorldItemMallBuyRequest
                    {
                        Key = (short)e.Key, Index = (short)e.Index,
                        Amount = 1, Version = ItemMallState.Version,
                    }, CancellationToken.None);
                    GD.Print($"[gmfile] @mallcheapest g={e.Group} " +
                        $"i={e.Index} mu={e.MoneyUnit} item={e.ItemId} " +
                        $"prix={e.EffectivePrice}");
                }
                break;
            case "@auctsell":
                // @auctsell <slot> <price> <buyout> — NCID 117
                // AuctionSell (même paquet que le bouton Mettre en
                // vente de la fenêtre HdV).
                _ = s.SendZoneAsync(new ZoneAuctionSell
                {
                    Bag = 0,
                    Slot = (short)int.Parse(p[1]),
                    Price = int.Parse(p[2]),
                    Buyout = int.Parse(p[3]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @auctsell slot={p[1]} " +
                    $"p={p[2]} bo={p[3]}");
                break;
            case "@auctq":
                // @auctq [search] — NCID 114 AuctionBuyQuery (recherche
                // globale vide par défaut).
                _ = s.SendZoneAsync(new ZoneAuctionBuyQuery
                {
                    SearchText = p.Length > 1 ? p[1] : "",
                }, CancellationToken.None);
                GD.Print("[gmfile] @auctq");
                break;
            case "@auctbuy":
                // @auctbuy <auctionId> — NCID 115 AuctionBuy.
                _ = s.SendZoneAsync(new ZoneAuctionBuy
                {
                    AuctionId = int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @auctbuy {p[1]}");
                break;
            case "@auctcancel":
                // @auctcancel <auctionId> — NCID 118 AuctionCancel.
                _ = s.SendZoneAsync(new ZoneAuctionCancel
                {
                    AuctionId = int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @auctcancel {p[1]}");
                break;
            case "@work":
                // @work <workId> <bag> <slot> [boostBag boostSlot] —
                // NCID 170 PlayerWork (commande métier, catalyseur =
                // parchemin de recette au slot indiqué).
                _ = s.SendZoneAsync(new ZonePlayerWork
                {
                    WorkId = uint.Parse(p[1]),
                    CatalystBag = (short)int.Parse(p[2]),
                    CatalystSlot = (short)int.Parse(p[3]),
                    BoostBag = (short)(p.Length > 4 ? int.Parse(p[4]) : -1),
                    BoostSlot = (short)(p.Length > 5 ? int.Parse(p[5]) : -1),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @work {p[1]} cat={p[2]}:{p[3]}");
                break;
            case "@workclaim":
                // @workclaim <workNum> — NCID 171 PlayerWorkResultOK.
                _ = s.SendZoneAsync(new ZonePlayerWorkResultOK
                {
                    WorkNum = (short)int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @workclaim {p[1]}");
                break;
            case "@famcreate":
                // @famcreate <nom> — NCID 49 ZoneServerFamilyCreate
                // (création guilde, débit 50 000 or côté monde).
                _ = s.SendZoneAsync(new ZoneFamilyCreate
                {
                    Value = p[1],
                }, CancellationToken.None);
                GD.Print($"[gmfile] @famcreate {p[1]}");
                break;
            case "@fament":
                // @fament <familyId|-1> — NCID 176 FamilyTownEnter :
                // entrée/sortie du hall VDG (chemin wire natif, pose
                // char+0xaac puis transport vers family.house_open).
                _ = s.SendZoneAsync(new ZoneServerFamilyTownEnter
                {
                    Value = int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @fament {p[1]}");
                break;
            case "@famgive":
                // @famgive <montant> — NCID 159 ZoneServerFamilyDonateMoney
                // (don trésorerie : GainGold(-n) + treasury+= côté zone).
                _ = s.SendZoneAsync(new ZoneFamilyDonateMoney
                {
                    Value = int.Parse(p[1]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @famgive {p[1]}");
                break;
            case "@stdep":
                // @stdep <chest> <bag> <slot> — NCID 177
                // FamilyStoragePutItem (stack entier sac → coffre).
                _ = s.SendZoneAsync(new ZoneFamilyStoragePut
                {
                    StorageIndex = (short)int.Parse(p[1]),
                    Loc = -1,
                    Bag = (short)int.Parse(p[2]),
                    Slot = (short)int.Parse(p[3]),
                }, CancellationToken.None);
                GD.Print($"[gmfile] @stdep chest={p[1]} bag={p[2]}:{p[3]}");
                break;
            case "@stget":
                // @stget <chest> <loc> — NCID 178
                // FamilyStorageGetItem (stack entier coffre → sac).
                _ = s.SendZoneAsync(new ZoneFamilyStorageGet
                {
                    StorageIndex = (short)int.Parse(p[1]),
                    Loc = (short)int.Parse(p[2]),
                    Bag = -1,
                    Slot = -1,
                }, CancellationToken.None);
                GD.Print($"[gmfile] @stget chest={p[1]} loc={p[2]}");
                break;
            case "@uiclick":
                // @uiclick <sous-chaîne de nom> — vrai press/release au
                // centre du premier Control visible correspondant
                // (évite la calibration écran↔viewport de @click).
                {
                    // « parent/enfant » : le dernier segment matche le
                    // nom, les précédents le chemin (désambiguïsation
                    // entre deux dialogues homonymes).
                    var segs = p[1].Split('/');
                    Control? target = null;
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                    {
                        if (!c.IsVisibleInTree()
                            || !c.Name.ToString().Contains(segs[^1]))
                            continue;
                        var path = c.GetPath().ToString();
                        var ok = true;
                        for (var i = 0; i < segs.Length - 1; i++)
                            if (!path.Contains(segs[i])) { ok = false; break; }
                        if (ok) { target = c; break; }
                    }
                    if (target == null)
                    { GD.Print($"[gmfile] @uiclick {p[1]} introuvable"); break; }
                    var ctr = target.GetGlobalRect().GetCenter();
                    var bp = new InputEventMouseButton
                        { Position = ctr, ButtonIndex = MouseButton.Left, Pressed = true };
                    var br = (InputEventMouseButton)bp.Duplicate();
                    br.Pressed = false;
                    Input.ParseInputEvent(new InputEventMouseMotion
                        { Position = ctr });
                    Input.ParseInputEvent(bp);
                    GetTree().CreateTimer(0.08).Timeout += () =>
                        Input.ParseInputEvent(br);
                    GD.Print($"[gmfile] @uiclick {target.GetPath()} @{ctr}");
                    break;
                }
            case "@hit":
                // @hit x y — bouge le pointeur puis log le Control visé
                // (diagnostic coordonnées/clics GUI).
                Input.ParseInputEvent(new InputEventMouseMotion
                    { Position = new Vector2(float.Parse(p[1], CI), float.Parse(p[2], CI)) });
                GetTree().CreateTimer(0.1).Timeout += () =>
                {
                    var c = GetViewport().GuiGetHoveredControl();
                    GD.Print($"[gmfile] @hit {(c == null ? "null" : c.GetPath())}");
                };
                break;
            case "@tabclick":
                // @tabclick <n> — vrai clic au centre de l'onglet n du
                // TabContainer visible (passe par le TabBar interne).
                foreach (var tc0 in GetTree().Root
                        .FindChildren("*", "TabContainer", true, false)
                        .OfType<TabContainer>())
                    if (tc0.IsVisibleInTree())
                    {
                        var bar = tc0.GetTabBar();
                        var r = bar.GetTabRect(
                            Math.Clamp(int.Parse(p[1]), 0, tc0.GetTabCount() - 1));
                        var ctr = bar.GetGlobalRect().Position + r.GetCenter();
                        var bp = new InputEventMouseButton
                            { Position = ctr, ButtonIndex = MouseButton.Left, Pressed = true };
                        var br = (InputEventMouseButton)bp.Duplicate();
                        br.Pressed = false;
                        Input.ParseInputEvent(new InputEventMouseMotion
                            { Position = ctr });
                        Input.ParseInputEvent(bp);
                        var ctr2 = ctr;
                        GetTree().CreateTimer(0.08).Timeout += () =>
                            Input.ParseInputEvent(br);
                        GD.Print($"[gmfile] @tabclick {p[1]} @{ctr2}");
                        break;
                    }
                break;
            case "@tab":
                // @tab <n> — force l'onglet du TabContainer visible
                // (boutique → volet rachat) ; le clic GUI synthétique
                // ne déclenche pas le TabBar sous ParseInputEvent.
                foreach (var tc in GetTree().Root
                        .FindChildren("*", "TabContainer", true, false)
                        .OfType<TabContainer>())
                    if (tc.IsVisibleInTree())
                    {
                        // @tab <nom> : titre contenant <nom> ; l'index
                        // numérique garde le comportement historique.
                        if (int.TryParse(p[1], out var tabIdx))
                        {
                            tc.CurrentTab = Math.Clamp(tabIdx, 0,
                                tc.GetTabCount() - 1);
                            continue;
                        }
                        var tabName = string.Join(' ', p.Skip(1));
                        for (var tabK = 0; tabK < tc.GetTabCount(); tabK++)
                            if (tc.GetTabTitle(tabK).Contains(tabName,
                                    StringComparison.OrdinalIgnoreCase))
                            { tc.CurrentTab = tabK; break; }
                    }
                break;
            case "@inv":
                GD.Print($"[gmfile] inv n={InventoryWindow.AllItems().Count} " +
                    $"bag={PlayerBag.Items.Count}");
                foreach (var it in InventoryWindow.AllItems())
                    GD.Print($"[gmfile] inv bag={it.Bag} slot={it.Slot} " +
                        $"item={it.ItemId} x{it.Amount}");
                break;
            case "@hover":
                // @hover <itemId> — pointeur au centre du premier
                // slot visible affichant cet item (tooltip seul,
                // sans clic). ItemCell (sac) est privé → réflexion.
                {
                    var want = int.Parse(p[1]);
                    Control? tgt = null;
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                    {
                        if (!c.IsVisibleInTree()) continue;
                        if (c is ItemSlotCell sc && sc.ItemId == want)
                        { tgt = c; break; }
                        if (c.GetType().Name == "ItemCell" &&
                            c.GetType().GetProperty("Item")
                                    ?.GetValue(c) is { } it &&
                            (int?)(it.GetType().GetProperty("ItemId")
                                    ?.GetValue(it)) == want)
                        { tgt = c; break; }
                    }
                    if (tgt == null)
                    { GD.Print($"[gmfile] @hover {want} introuvable"); break; }
                    var ctr = tgt.GetGlobalRect().GetCenter();
                    Input.ParseInputEvent(new InputEventMouseMotion
                        { Position = ctr });
                    GD.Print($"[gmfile] @hover {want} {tgt.GetPath()} @{ctr}");
                    break;
                }
            case "@dragitem":
                // @dragitem <itemId> <leaf> — vrai drag GUI du
                // premier slot affichant itemId vers le Control dont
                // le nom finit par <leaf> (UseItemSlot, Scroll…).
                {
                    var want = int.Parse(p[1]);
                    Control? dst = null;
                    var srcs = new List<Control>();
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                    {
                        if (!c.IsVisibleInTree()) continue;
                        if (c is ItemSlotCell sc0 && sc0.ItemId == want)
                            srcs.Add(c);
                        else if (c.GetType().Name == "ItemCell" &&
                            c.GetType().GetProperty("Item")
                                    ?.GetValue(c) is { } it &&
                            (int?)(it.GetType().GetProperty("ItemId")
                                    ?.GetValue(it)) == want)
                            srcs.Add(c);
                        if (dst == null &&
                            c.Name.ToString().EndsWith(p[2]))
                            dst = c;
                    }
                    // Le premier cell porteur de data draggable gagne —
                    // un SetItemId display-only (_item null) rend nil.
                    Control? src = null;
                    var data = default(Variant);
                    foreach (var cand in srcs)
                    {
                        var d2 = (Variant)cand.GetType()
                            .GetMethod("_GetDragData")!
                            .Invoke(cand, new object[] { Vector2.Zero })!;
                        if (d2.VariantType != Variant.Type.Nil)
                        { src = cand; data = d2; break; }
                    }
                    if (src == null || dst == null)
                    {
                        GD.Print($"[gmfile] @dragitem {want}→{p[2]} " +
                            $"src={(src == null ? $"null({srcs.Count})" : "ok")} " +
                            $"dst={(dst == null ? "null" : "ok")}");
                        break;
                    }
                    var dFrom = src.GetGlobalRect().GetCenter();
                    var dTo = dst.GetGlobalRect().GetCenter();
                    // ForceDrag = le vrai pipeline du moteur (le
                    // payload est celui de _GetDragData : bag/slot/
                    // itemId) — le release au-dessus de la cible
                    // déclenche _CanDropData/_DropData comme un drag
                    // souris réel.
                    // Le release synthétique ne délivre pas le drop en
                    // 4.8 — on invoque le même couple _CanDropData/
                    // _DropData que le moteur appelle sur un vrai drag.
                    var canDrop = (bool)dst.GetType()
                        .GetMethod("_CanDropData")!
                        .Invoke(dst, new object[] { dTo, data })!;
                    GD.Print($"[gmfile] @dragitem {want}→{p[2]} " +
                        $"{dFrom}→{dTo} can={canDrop} dst={dst.GetPath()}");
                    if (!canDrop) break;
                    dst.GetType().GetMethod("_DropData")!
                        .Invoke(dst, new object[] { dTo, data });
                    break;
                }
            case "@selrow":
                // @selrow <itemId> — clique la ligne lifeskill dont la
                // cellule produit affiche cet item (les lignes n'ont
                // pas de nom unique — la cellule oui).
                {
                    var want2 = int.Parse(p[1]);
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                    {
                        if (!c.IsVisibleInTree() ||
                            c.GetType().Name != "ItemSlotCell" ||
                            !c.GetPath().ToString().Contains("__rows"))
                            continue;
                        if (c.GetType().GetProperty("ItemId")
                                ?.GetValue(c) is not int iid ||
                            iid != want2) continue;
                        var row = c;
                        while (row.GetParent() is Control par &&
                               !par.Name.ToString().StartsWith("__rows"))
                            row = par;
                        // La ligne peut être hors du clip du
                        // ScrollPane → la faire défiler, puis cliquer
                        // après que le scroll a pris effet.
                        if (row.GetParent()?.GetParent()
                                is ScrollContainer sc)
                            sc.EnsureControlVisible(row);
                        GetTree().CreateTimer(0.3).Timeout += () =>
                        {
                            if (!IsInstanceValid(row)) return;
                            var rect = row.GetGlobalRect();
                            var ctr = new Vector2(rect.Position.X + 4,
                                rect.GetCenter().Y);
                            var bp2 = new InputEventMouseButton
                            { Position = ctr,
                              ButtonIndex = MouseButton.Left,
                              Pressed = true };
                            var br2 = (InputEventMouseButton)bp2
                                .Duplicate();
                            br2.Pressed = false;
                            Input.ParseInputEvent(
                                new InputEventMouseMotion
                                    { Position = ctr });
                            Input.ParseInputEvent(bp2);
                            GetTree().CreateTimer(0.08).Timeout += () =>
                                Input.ParseInputEvent(br2);
                            GD.Print(
                                $"[gmfile] @selrow {want2} @{ctr}");
                        };
                        break;
                    }
                    break;
                }
            case "@cellpos":
                // @cellpos <slot> [bag] — centre écran du ItemCell
                // adressant ce slot (pour @click/@rclick réels).
                {
                    var slot = int.Parse(p[1]);
                    var bag = p.Length > 2 ? int.Parse(p[2]) : 0;
                    var found = false;
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                    {
                        if (!c.IsVisibleInTree() ||
                            c.GetType().Name != "ItemCell") continue;
                        if (c.GetType().GetProperty("Slot")
                                ?.GetValue(c) is not int cs || cs != slot ||
                            c.GetType().GetProperty("Bag")
                                ?.GetValue(c) is not int cb || cb != bag)
                            continue;
                        GD.Print($"[gmfile] @cellpos {bag}/{slot} " +
                            $"@{c.GetGlobalRect().GetCenter()}");
                        found = true;
                        break;
                    }
                    if (!found)
                        GD.Print($"[gmfile] @cellpos {bag}/{slot} absent");
                    break;
                }
            case "@open":
                // @open <clé> — même ouverture forcée que --hud-open=<clé>
                // (guild, familystorage, townlist, shop, itemmall…).
                HudExtensions.FindHud()?.ForceOpen(p[1]);
                GD.Print($"[gmfile] @open {p[1]}");
                break;
            case "@nodepos":
                // @nodepos <fragment> — centre écran du premier Control
                // visible dont le nom contient le fragment (cible de @drag).
                {
                    var hit = false;
                    foreach (var c in GetTree().Root
                            .FindChildren("*", "Control", true, false)
                            .OfType<Control>())
                        if (c.IsVisibleInTree() &&
                            c.Name.ToString().Contains(p[1]))
                        {
                            var r = c.GetGlobalRect();
                            GD.Print($"[gmfile] @nodepos {c.Name} @(" +
                                $"{r.GetCenter().X:0}, {r.GetCenter().Y:0}) size={r.Size}");
                            hit = true;
                            break;
                        }
                    if (!hit) GD.Print($"[gmfile] @nodepos {p[1]} absent");
                    break;
                }
            case "@under":
                // @under x y — mouvement souris puis chemin du Control
                // survolé (diagnostic des drops interceptés).
                if (p.Length < 3) return;
                Input.ParseInputEvent(new InputEventMouseMotion
                    { Position = new Vector2(float.Parse(p[1], CI),
                        float.Parse(p[2], CI)) });
                GetTree().CreateTimer(0.3).Timeout += () =>
                    GD.Print("[gmfile] @under → " +
                        GetViewport().GuiGetHoveredControl()?.GetPath());
                break;
            case "@shot":
                // @shot <chemin.png> — capture du viewport du jeu seul
                // (jamais le bureau).
                GetViewport().GetTexture().GetImage().SavePng(p[1]);
                GD.Print($"[gmfile] @shot {p[1]}");
                break;
            case "@cam":
                // @cam <yawDeg> <pitchDeg> <dist> — caméra orbitale.
                if (p.Length >= 4 && GetTree().Root
                        .FindChild("Player", true, false) is PlayerController cpc)
                    cpc.SetCamera(float.Parse(p[1], CI), float.Parse(p[2], CI),
                        float.Parse(p[3], CI));
                GD.Print($"[gmfile] @cam {line}");
                break;
            case "@hold":
                // @hold <action> <secondes> — maintient une action d'input
                // (move_forward/back/left/right/move_run) comme une touche.
                if (p.Length >= 3)
                {
                    var act = p[1];
                    Input.ActionPress(act);
                    GetTree().CreateTimer(double.Parse(p[2], CI)).Timeout +=
                        () => Input.ActionRelease(act);
                    GD.Print($"[gmfile] @hold {act} {p[2]}s");
                }
                break;
            case "@burst":
                // @burst <préfixe> <n> <ms> — n captures du viewport à
                // intervalle régulier (mini-vidéo) : <préfixe>_000.png…
                if (p.Length >= 4)
                {
                    var pre = p[1]; var n = int.Parse(p[2]);
                    var ms = double.Parse(p[3], CI) / 1000.0;
                    for (var i = 0; i < n; i++)
                    {
                        var idx = i;
                        GetTree().CreateTimer(ms * i).Timeout += () =>
                            GetViewport().GetTexture().GetImage()
                                .SavePng($"{pre}_{idx:D3}.png");
                    }
                    GD.Print($"[gmfile] @burst {pre} n={n} {p[3]}ms");
                }
                break;
            case "@cjk":
                // @cjk [étiquette] — liste tous les textes CJK (chinois/
                // japonais) visibles à l'écran (Label, Button, RichText,
                // LineEdit, Tree/MclList, TabContainer) : audit i18n.
                {
                    var tag = p.Length > 1 ? p[1] : "";
                    var hits = 0;
                    static bool Cjk(string t) =>
                        t.Any(c => c is >= '\u3040' and <= '\u30ff' or
                            >= '\u3400' and <= '\u9fff' or >= '\uff00' and <= '\uffef');
                    void Rep(Node n, string txt)
                    {
                        if (n is Control c2 && !c2.IsVisibleInTree()) return;
                        if (!Cjk(txt)) return;
                        hits++;
                        GD.Print($"[cjk] {tag} {n.GetPath()} « {txt.Replace('\n', ' ')} »");
                    }
                    foreach (var n in GetTree().Root.FindChildren("*", "", true, false))
                    {
                        switch (n)
                        {
                            case Label l: Rep(n, l.Text); break;
                            case BaseButton b when b is Button bt: Rep(n, bt.Text); break;
                            case RichTextLabel r: Rep(n, r.Text); break;
                            case LineEdit le: Rep(n, le.Text + le.PlaceholderText); break;
                            case TabContainer tc:
                                for (var i = 0; i < tc.GetTabCount(); i++)
                                    Rep(n, tc.GetTabTitle(i));
                                break;
                            case Tree tr:
                                var root = tr.GetRoot();
                                if (root != null)
                                    for (var it = root.GetFirstChild(); it != null; it = it.GetNext())
                                        for (var col = 0; col < tr.Columns; col++)
                                            Rep(n, it.GetText(col));
                                for (var col = 0; col < tr.Columns; col++)
                                    Rep(n, tr.GetColumnTitle(col));
                                break;
                        }
                    }
                    GD.Print($"[cjk] {tag} total={hits}");
                }
                break;
            case "@cam2":
                // @cam2 <yaw> <pitch> <dist> <hauteur> — caméra libre
                // (collision caméra coupée, distance illimitée, pas de
                // nameplate) : vues aériennes pour auditer un lieu.
                if (p.Length >= 5 && GetTree().Root
                        .FindChild("Player", true, false) is PlayerController c2pc)
                    c2pc.DebugCam(float.Parse(p[1], CI), float.Parse(p[2], CI),
                        float.Parse(p[3], CI), float.Parse(p[4], CI));
                GD.Print($"[gmfile] @cam2 {line}");
                break;
            case "@closeall":
                // @closeall — ferme toutes les fenêtres HUD (captures propres).
                HudWindows.CloseAll();
                foreach (var w in GetTree().Root
                        .FindChildren("*", "Control", true, false)
                        .OfType<Control>())
                    if (w.Visible && w.GetType().Name.EndsWith("Window") &&
                        w.GetType().Name != "InventoryWindow")
                        w.Visible = false;
                GD.Print("[gmfile] @closeall");
                break;
            case "@wins":
                // @wins — état objectif des fenêtres visibles (couche, rang, recouvrement).
                WindowProbe.Dump();
                break;
            case "@closewin":
                // @closewin <frag> — masque la première fenêtre Control
                // visible dont le nom contient le fragment (dialogue
                // resté ouvert après @dlg, fenêtres de test…).
                foreach (var w in GetTree().Root
                        .FindChildren("*", "Control", true, false)
                        .OfType<Control>())
                    if (w.IsVisibleInTree() &&
                        w.Name.ToString().Contains(p[1]))
                    {
                        w.Visible = false;
                        GD.Print($"[gmfile] @closewin {w.GetPath()}");
                        break;
                    }
                break;
            case "@key":
                var kc = OS.FindKeycodeFromString(p[1]);
                if (kc == Key.None) return;
                var uni = p[1].Length == 1 ? p[1][0] : '\0';
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = kc, PhysicalKeycode = kc, Unicode = uni, Pressed = true,
                });
                GetTree().CreateTimer(0.08).Timeout += () =>
                    Input.ParseInputEvent(new InputEventKey
                    {
                        Keycode = kc, PhysicalKeycode = kc, Pressed = false,
                    });
                GD.Print($"[gmfile] @key {kc}");
                break;
            case "@type":
                var ti = 0;
                foreach (var ch in p[1])
                {
                    var c = ch;
                    var ck = OS.FindKeycodeFromString(c.ToString());
                    GetTree().CreateTimer(0.06 * ti++).Timeout += () =>
                    {
                        Input.ParseInputEvent(new InputEventKey
                        {
                            PhysicalKeycode = ck, Unicode = c,
                            Pressed = true,
                        });
                        Input.ParseInputEvent(new InputEventKey
                        {
                            PhysicalKeycode = ck, Pressed = false,
                        });
                    };
                }
                GD.Print($"[gmfile] @type {p[1]} ({p[1].Length} chars)");
                break;
        }
    }
}
