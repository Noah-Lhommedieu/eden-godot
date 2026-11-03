using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Combat;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Point d'entrée des fenêtres/composants ajoutés SANS toucher
/// <see cref="HudRoot"/> ni <c>GameScene</c> (lots parallèles S1/S4/S7).
///
/// Montage : ce nœud est un <b>autoload</b> (<c>project.godot</c> —
/// <c>HudExtensions="*res://scripts/hud/HudExtensions.cs"</c>) qui
/// surveille la scène courante ; dès que le HUD de jeu
/// (<see cref="HudRoot"/>, nœud « Hud » de <c>scenes/hud.tscn</c>) est
/// construit, les composants du lot sont instanciés et montés :
///
///   - fenêtres/réhabs 2D (<see cref="SkillBarWindow"/>,
///     <see cref="ShopWindow"/>, <see cref="BankWindow"/>,
///     <see cref="DeathWindow"/>,
///     <see cref="CastBar"/>) → enfants du HudRoot via
///     <see cref="Mount"/> (FullRect + MouseFilter Ignore, même
///     convention que <c>HudRoot.Mount</c>) ;
///   - nœuds monde/logique (<see cref="SkillCast"/>,
///     <see cref="DamageFloaters"/>, <see cref="TreasurePickup"/>) →
///     enfants de la scène courante (ils accèdent à « Player » et
///     « NetEntities » par FindChild).
///
/// <see cref="Mount(Control)"/> est aussi l'API publique pour tout
/// agent parallèle : trouve le HudRoot de la scène (ou le root
/// Control, à défaut pose un CanvasLayer dédié) et AddChild.
///
/// Ce nœud joue en outre le rôle de pont réseau paresseux partagé
/// (pattern <see cref="HudNetBridge"/>) : il lie les NCIDs sac
/// (312/274/275/291) pour alimenter <see cref="PlayerBag"/>, l'état
/// commun des fenêtres vente/dépôt — la session est découverte via
/// <see cref="SessionCarry.Session"/> (flux écrans ; en --live direct
/// la session est privée à GameScene → binds absents, même limite
/// documentée que HudNetBridge/AutoAttack).
/// </summary>
public partial class HudExtensions : Node
{
    /// <summary>Singleton de l'autoload (posé dans _Ready).</summary>
    public static HudExtensions? Instance { get; private set; }

    private readonly ConcurrentQueue<Action> _pending = new();
    private EdenSession? _session;
    private bool _mounted;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Monte <paramref name="c"/> sur le HUD de la scène en
    /// cours : HudRoot (canvas de jeu) si trouvé, sinon le root
    /// Control de la scène, sinon un CanvasLayer dédié ajouté à la
    /// scène courante. Thread principal uniquement.</summary>
    public static void Mount(Control c)
    {
        var hud = FindHud();
        if (hud != null)
        {
            hud.AddChild(c);
            c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            return;
        }
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (tree.CurrentScene is Control root)
        {
            root.AddChild(c);
            return;
        }
        if (tree.CurrentScene != null)
        {
            var layer = new CanvasLayer { Name = "ExtHud", Layer = 10 };
            tree.CurrentScene.AddChild(layer);
            layer.AddChild(c);
            c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
    }

    /// <summary>Construit le layout natif <paramref name="layout"/>
    /// sous <paramref name="host"/> (chrome + titre + drag + bouton ×
    /// natifs) : centré écran, taille native conservée, position
    /// persistée sous <paramref name="persistKey"/>. Null si uidata
    /// absent → l'appelant garde son panneau de repli.</summary>
    public static Control? BuildNativeWindow(Control host,
        string layout, string persistKey, Vector2 fallbackSize)
    {
        var built = FindHud()?.Build(layout)
            ?? (host.GetParent() as HudRoot)?.Build(layout);
        if (built == null) return null;
        host.AddChild(built);
        var sz = built.Size;
        if (sz.X < 60 || sz.Y < 60) sz = fallbackSize;
        HudRoot.Pin(built, 0.5f, 0.5f, 0.5f, 0.5f,
            -sz.X / 2, -sz.Y / 2, sz.X / 2, sz.Y / 2);
        built.CustomMinimumSize = sz;
        DragHelper.Persist(built, persistKey);
        return built;
    }

    /// <summary>Trouve le <see cref="HudRoot"/> de la scène courante
    /// (recherche par type puis par nom « Hud »). Null hors game.tscn
    /// ou avant son montage.</summary>
    public static HudRoot? FindHud()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;
        var stack = new Stack<Node>();
        stack.Push(tree.Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is HudRoot h) return h;
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
        return null;
    }

    /// <summary>Trouve la scène de jeu courante (nœud « GameScene » —
    /// Node3D racine de game.tscn).</summary>
    public static Node? FindGameScene()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;
        return tree.CurrentScene;
    }

    // ------------------------------------------------------------------
    // Boot paresseux : on attend que le HUD existe puis on monte tout.
    // ------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[hud-ext] dispatch : {ex}");
            }
        }
        if (_mounted) return;
        var hud = FindHud();
        var scene = FindGameScene();
        // La scène de jeu est un Node3D nommé « GameScene » ; le HUD
        // doit être construit (Vitals non null = BuildAll terminé, le
        // contexte CEGUI est alors chargé → hud.Build() fonctionnel).
        if (hud == null || hud.Vitals == null ||
            scene is not Node3D || scene.Name != "GameScene")
            return;
        _mounted = true;
        MountAll(hud, scene);
    }

    /// <summary>Instancie tous les composants du lot S1+S4+S7.</summary>
    private void MountAll(HudRoot hud, Node scene)
    {
        // Nœuds monde/logique (ordre : SkillCast et CastBar avant la
        // barre de skills qui les consomme via leurs Instance).
        var skillCast = new SkillCast { Name = "SkillCast" };
        scene.AddChild(skillCast);
        scene.AddChild(new SpellFx { Name = "SpellFx" });
        var floaters = new DamageFloaters { Name = "DamageFloaters" };
        scene.AddChild(floaters);
        var loot = new TreasurePickup { Name = "TreasurePickup" };
        scene.AddChild(loot);

        // Fenêtres 2D sur le canvas HUD.
        Mount(new CastBar { Name = "CastBar" });
        Mount(new SkillBarWindow { Name = "SkillBar" });
        Mount(new ShopWindow { Name = "ShopWnd" });
        Mount(new BankWindow { Name = "BankWnd" });
        // Vague 2 : coffre de famille (service NPC 19 → 507/508,
        // deltas 504/505 + journal 506 — binds GameScene).
        Mount(new FamilyStorageWindow { Name = "FamilyStorageWnd" });
        Mount(new DeathWindow { Name = "DeathWnd" });
        // Vague 1-C : courrier (399→400…) et hôtel des ventes (430→425…).
        Mount(new MailWindow { Name = "MailWnd" });
        Mount(new AuctionWindow { Name = "AuctionWnd" });
        // Vague 1-D : succès (480/396-398) + calendrier de connexion
        // (258/615) — binds posés par les fenêtres elles-mêmes.
        Mount(new AchievementWindow { Name = "AchievementWindow" });
        Mount(new DailyRewardWindow { Name = "DailyRewardWindow" });
        // Vague Reforged : item mall (canal monde 15..25/56..79,
        // binds posés par la fenêtre elle-même via OnWorld) et
        // neobank (même protocole 357/358/60/61/62 que la banque —
        // vue étendue rf_neobank, bascule via le bouton « Neo+ »
        // de BankWindow).
        Mount(new ItemMallWindow { Name = "ItemMallWnd" });
        Mount(new NeobankWindow { Name = "NeobankWnd" });

        // Vague 2 : buffs/statuts (299/300/301/315/372/445/446 +
        // C→S 18/85/127/128), chemin d'éveil + fortune
        // (534/535/589/590/557 + C→S 203/204/223) et couple/mariage
        // (475-479/597/599 + C→S 154-157/249/251). Les trois ponts
        // font leur binding SessionCarry paresseux comme
        // HudNetBridge.
        var buffBar = new BuffBar { Name = "BuffBar" };
        buffBar.Build(hud);
        Mount(buffBar);
        var nerfBar = new NerfBar { Name = "NerfBar" };
        nerfBar.Build(hud);
        Mount(nerfBar);
        var destiny = new DestinyWindow { Name = "DestinyWindow" };
        destiny.Build(hud);
        Mount(destiny);
        var couple = new CoupleWindow { Name = "CoupleWindow" };
        couple.Build(hud);
        Mount(couple);
        hud.AddChild(new BuffBridge(hud));
        hud.AddChild(new DestinyBridge());
        hud.AddChild(new LoverBridge());

        // Vague 2 : échange J↔J (trade 331-340/353/394/395) et étal
        // joueur (vending 375-379) — binds posés par les fenêtres
        // elles-mêmes (pattern PartyHud).
        Mount(new TradeWindow { Name = "TradeWnd" });
        Mount(new VendingWindow { Name = "VendingWnd" });
        // Vague complétude : fenêtres des NCIDs fraîchement parsés —
        // duel 1v1 (451-454 + C→S 135/136), panneau d'affichage
        // (455-459 + C→S 138-142) et dialogs de service (magicbox,
        // loterie, battlefield, instance, hôtel, église, boutique…).
        Mount(new DuelDialog { Name = "DuelDialog" });
        Mount(new MessageBoardWindow { Name = "MessageBoardWnd" });
        Mount(new FeatureDialogWindow { Name = "FeatureDialogWnd" });
        Mount(new LotteryWindow { Name = "LotteryWnd" });
        Mount(new ArchiveWindow { Name = "ArchiveWnd" });
        Mount(new CostumeWindow { Name = "CostumeWnd" });
        Mount(new ItemPreviewWindow { Name = "ItemPreviewWnd" });
        Mount(new AltarWindow
        {
            Name = "AltarStarWnd", Title = UiTextEn.Translate("Star Diamond / Eden Crystal Altar"),
            Currencies = new[] { "Star Diamond", "Eden Crystal" },
            Description = UiTextEn.Translate("Offer Star Diamonds or Eden Crystals to draw ") +
                UiTextEn.Translate("rewards from the altar."),
        });
        Mount(new AltarWindow
        {
            Name = "AltarGemWnd", Title = UiTextEn.Translate("Eden Gem Altar"),
            Currencies = new[] { "Eden Gem" },
            Description = UiTextEn.Translate("Offer Eden Gems to draw rewards from the altar."),
        });
        Mount(new BattlefieldWindow { Name = "BattlefieldWnd" });
        Mount(new HotelWindow { Name = "HotelWnd" });
        Mount(new TerritoryWindow { Name = "TerritoryWnd" });
        Mount(new ColosseumWindow { Name = "ColosseumWnd" });
        Mount(new RecruitWindow { Name = "RecruitWnd" });
        Mount(new FastDressWindow { Name = "FastDressWnd" });
        Mount(new RankWindow { Name = "RankWnd" });
        Mount(new RollTreasureDialog { Name = "RollTreasureDlg" });
        Mount(new InspectWindow { Name = "InspectWnd" });

        GD.Print("[hud-ext] S1/S4/S7+1C+1D+V2 montés (skillbar, " +
            "castbar, floaters, loot, shop, bank, familystorage, " +
            "gold, death, mail, auction, achievements, daily, " +
            "itemmall, neobank, buffbar, destiny, couple, " +
            "trade, vending, duel, messageboard, featuredlg, " +
            "lottery, magicbox, battlefield, hotel, territory, " +
            "colosseum, recruit, fastdress, rank, roll, inspect)");
    }

    // ------------------------------------------------------------------
    // Pont réseau partagé : sac joueur (312/274/275/291) → PlayerBag.
    // Les handlers parsent sur le thread réseau puis postent ici.
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        // Parseurs « complétude » : tout NCID S→C émis par le serveur
        // est désérialisé (plus de « non géré ») — MiscPackets.Latest +
        // event Received pour les états/UX branchés dessus.
        MiscPackets.BindZone(s, _pending.Enqueue);
        MiscPackets.BindWorld(s, _pending.Enqueue);
        // Classes existantes jusque-là non bindées — même traitement.
        foreach (var proto in new NetMessage[]
        {
            new ZoneClientHello(), new ZoneClientSpellCast(),
            new ZoneClientSpellBlastOK(), new ZoneClientSpellBlastToLife(),
        })
            s.OnZone(proto.Opcode, r =>
            {
                try { proto.Deserialize(r); }
                catch (Exception ex)
                {
                    GD.PrintErr($"[misc] parse {proto.Name} : {ex.Message}");
                }
            });
        // Miroir sac : la donnée est appliquée ICI (thread réseau,
        // store verrouillé) — la file UI peut rester bloquée ~10 s
        // pendant une migration et un lecteur verrait un sac vide
        // alors que le serveur l'a rempli (constat --vending-test).
        // Seule la notification Changed est postée sur le main thread.
        s.OnZone(312, r =>
        {
            // 312 EnterNodeInventory — codec prouvé NativeItemCodec.
            var body = r.ReadBytes(r.Remaining);
            var items = NativeInventoryParser.Parse(body);
            PlayerBag.ApplyEnterNodeData(items);
            _pending.Enqueue(PlayerBag.RaiseChanged);
        });
        s.OnZone(274, r =>
        {
            var m = new ZonePutItem();
            m.Deserialize(r);
            PlayerBag.ApplyPutItemData(m);
            _pending.Enqueue(PlayerBag.RaiseChanged);
        });
        s.OnZone(275, r =>
        {
            var m = new ZoneDropItem();
            m.Deserialize(r);
            PlayerBag.ApplyDropItemData(m);
            _pending.Enqueue(PlayerBag.RaiseChanged);
        });
        s.OnZone(291, r =>
        {
            var m = new ZoneModifyItemSlot();
            m.Deserialize(r);
            PlayerBag.ApplyModifySlotData(m);
            _pending.Enqueue(PlayerBag.RaiseChanged);
        });
        // Étal joueur (375-378) : mêmes raisons que le sac — l'état
        // VendingState est appliqué sur le thread réseau (binds
        // précoces, posés dès la session) ; les binds de VendingWindow
        // n'ont plus que les messages chat — sinon un 377 [1][2]
        // arrivé avant le montage de la fenêtre était perdu et le
        // vendeur restait « non vendeur » (constat --vending-test).
        // SelfCharId est posé ici aussi (la fenêtre le faisait au
        // montage — trop tard pour classer le 378 vendeur).
        VendingState.SelfCharId =
            (uint)(SessionCarry.Character?.Id ?? 0);
        s.OnZone(375, r =>
        {
            var m = new ZoneClientVendorClose();
            m.Deserialize(r);
            VendingState.ApplyCloseData(m);
            _pending.Enqueue(VendingState.RaiseChanged);
        });
        s.OnZone(376, r =>
        {
            var m = new ZoneClientVendorCloseView();
            m.Deserialize(r);
            VendingState.ApplyCloseViewData(m);
            _pending.Enqueue(VendingState.RaiseChanged);
        });
        s.OnZone(377, r =>
        {
            var m = new ZoneClientVendorStatus();
            m.Deserialize(r);
            VendingState.ApplyStatusData(m);
            _pending.Enqueue(VendingState.RaiseChanged);
        });
        s.OnZone(378, r =>
        {
            var m = new ZoneClientVendorUpdateView();
            m.Deserialize(r);
            VendingState.ApplyUpdateViewData(m);
            _pending.Enqueue(VendingState.RaiseChanged);
        });
        // Vague 1-D : succès (480), titres (396/397/398), calendrier
        // de connexion (615) — codecs dédiés ZoneAchievementMessages ;
        // la mutation store/UI reste sur le thread principal via
        // _pending. Le C→S 93 (titre) est routé par
        // PlayerTitles.SetTitleSender ; le 258 op0 (check de jour)
        // est déjà dans le burst d'entrée d'EdenSession.
        s.OnZone(480, r =>
        {
            var m = new ZoneAchievementUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() =>
                AchievementWindow.OnAchievementUpdate(m));
        });
        s.OnZone(397, r =>
        {
            var m = new ZoneUpdateAppellation();
            m.Deserialize(r);
            _pending.Enqueue(() => PlayerTitles.ApplyUpdate(m.TitleId));
        });
        s.OnZone(398, r =>
        {
            var m = new ZoneAddAppellation();
            m.Deserialize(r);
            _pending.Enqueue(() =>
                AchievementWindow.OnTitleAdded(m.TitleId));
        });
        s.OnZone(396, r =>
        {
            var m = new ZoneChangeAppellation();
            m.Deserialize(r);
            _pending.Enqueue(() => PlayerTitles.ApplyChange(
                m.CharId, m.TitleId, m.Seq, (int)s.SelectedCharId));
        });
        s.OnZone(615, r =>
        {
            var m = new ZoneUpdateDailyAwards();
            m.Deserialize(r);
            _pending.Enqueue(() => DailyRewardWindow.OnDailyAwards(m));
        });
        // Sélecteur de titre du profil → C→S 93 ([u16 titleId],
        // format prouvé HandleSetAppellationAsync zone @0x080f2400).
        PlayerTitles.SetTitleSender = id =>
            s.SendZoneAsync(new ZoneSetAppellation
            { TitleId = (ushort)Math.Clamp(id, 0, 0xffff) },
                CancellationToken.None);
        PlayerTitles.Log = msg => GD.PrintErr(msg);
        BindEntryData(s);
        GD.Print("[hud-ext] binds sac 312/274/275/291 + " +
            "1D 480/396/397/398/615 posés");
    }

    /// <summary>Binds Vague 4 — les blocs EnterNode*/mises à jour de
    /// profil arrivent dans le burst d'entrée AVANT le montage des
    /// fenêtres : ils mutent <see cref="PlayerProfileState"/> sur le
    /// thread réseau (donnée dispo immédiate) et ré-émettent
    /// <c>Changed</c> en file principale. Sans ces binds précoces les
    /// réponses aux requêtes d'entrée étaient loguées « non géré » et
    /// perdues (même maladie que vending 377/378 et le sac 312).</summary>
    private void BindEntryData(EdenSession s)
    {
        PlayerProfileState.Reset();
        void Bind<T>(int ncid, Action<T> apply,
            string section) where T : NetMessage, new()
        {
            s.OnZone(ncid, r =>
            {
                var m = new T();
                m.Deserialize(r);
                apply(m);
                _pending.Enqueue(
                    () => PlayerProfileState.RaiseChanged(section));
            });
        }
        Bind<ZoneEnterNodeShortcut>(351,
            PlayerProfileState.Apply, "shortcuts");
        Bind<ZoneEnterNodePlayerRoom>(523,
            PlayerProfileState.Apply, "playerroom");
        Bind<ZoneEnterNodeFastDress>(593,
            PlayerProfileState.Apply, "fastdress");
        Bind<ZoneEnterStarWayData>(609,
            PlayerProfileState.Apply, "starway");
        Bind<ZoneEnterNodePlayerCard>(490,
            PlayerProfileState.Apply, "playercard");
        Bind<ZoneUpdateCollection>(472,
            PlayerProfileState.Apply, "collection");
        Bind<ZoneUpdateBFData>(482,
            PlayerProfileState.Apply, "bfdata");
        Bind<ZonePlayerClassCustomData>(592,
            PlayerProfileState.Apply, "classcustom");
        Bind<ZoneUpdateAdvancedAbility>(471,
            PlayerProfileState.Apply, "advability");
        Bind<ZonePlayerClosets>(488,
            PlayerProfileState.Apply, "closets");
        Bind<ZonePlayerLicense>(509,
            PlayerProfileState.Apply, "license");
        Bind<ZonePlayerLifeSkill>(543,
            PlayerProfileState.Apply, "lifeskill");
        Bind<ZoneRecommendActivities>(559,
            PlayerProfileState.Apply, "activities");
        Bind<ZoneUpdateSystemVar>(344,
            PlayerProfileState.Apply, "sysvar");
        Bind<ZoneUpdateCoolDown>(368,
            PlayerProfileState.Apply, "cooldown");
        Bind<ZoneClientNewPlayerGuide>(551,
            PlayerProfileState.Apply, "guide");
        Bind<ZoneReviveInfo>(531,
            PlayerProfileState.Apply, "revive");
        Bind<ZoneClientMigrationDone>(382,
            PlayerProfileState.Apply, "migration");
        Bind<ZoneUpdateNodeTimes>(563,
            PlayerProfileState.Apply, "nodetimes");
        Bind<ZoneCommandFailReturn>(410,
            PlayerProfileState.Apply, "cmdfail");
        Bind<ZoneFlagModify>(302,
            PlayerProfileState.Apply, "flags");
        // Destin/fortune (534/535/589/590/557) : mutations data sur
        // le thread réseau dans DestinyState.Shared — les trames du
        // burst d'entrée arrivaient avant le montage de la fenêtre et
        // partaient en « non géré » (constat live 557). La fenêtre
        // consomme le même état partagé à son montage.
        var ds = DestinyState.Shared;
        s.OnZone(534, r =>
        {
            var m = new ZonePlayerDestinyRoad();
            m.Deserialize(r);
            ds.ApplyData(m);
            _pending.Enqueue(ds.RaiseChanged);
        });
        s.OnZone(535, r =>
        {
            var m = new ZonePlayerDestinyRoadEvent();
            m.Deserialize(r);
            ds.ApplyData(m);
            _pending.Enqueue(() =>
            {
                ds.RaiseEventReceived(m.EventId);
                ds.RaiseChanged();
            });
        });
        s.OnZone(589, r =>
        {
            var m = new ZoneUpdateDestinyRoad();
            m.Deserialize(r);
            ds.ApplyData(m);
            _pending.Enqueue(ds.RaiseChanged);
        });
        s.OnZone(590, r =>
        {
            var m = new ZoneDestinyDiceFailed();
            m.Deserialize(r);
            _pending.Enqueue(() => ds.RaiseOpFailed(m.Op));
        });
        s.OnZone(557, r =>
        {
            var m = new ZoneUpdateFortuneBagCounter();
            m.Deserialize(r);
            ds.ApplyData(m);
            _pending.Enqueue(ds.RaiseChanged);
        });
        Bind<ZoneLimitlessState>(2032,
            PlayerProfileState.Apply, "limitless");
        Bind<ZoneLimitlessPass>(2033,
            PlayerProfileState.Apply, "limitlesspass");
        // 299/301 : BuffBridge les rebindera pour l'UI — ces binds
        // précoces alimentent le store pour ne plus perdre les buffs
        // posés pendant le chargement.
        Bind<ZoneEnchantAdd>(299,
            PlayerProfileState.Apply, "enchant");
        Bind<ZoneEnchantUpdate>(301,
            PlayerProfileState.Apply, "enchant");
        // Recrutements d'équipe — résumé texte pour le chat/fenêtre.
        s.OnZone(519, r =>
        {
            var m = new ZoneTeamRecruitMsg();
            m.Deserialize(r);
            PlayerProfileState.ApplyTeamRecruit(m.Summary);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("recruit"));
        });
        s.OnZone(583, r =>
        {
            var m = new ZoneTeamRecruitMsgV2();
            m.Deserialize(r);
            PlayerProfileState.ApplyTeamRecruit(m.Summary);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("recruit"));
        });
        // Monde : reset/prestige/coins + famille — posés dès la
        // session (les fenêtres concernées arrivent après).
        s.OnWorld(40, r =>
        {
            var m = new WorldResetTime();
            m.Deserialize(r);
            PlayerProfileState.Apply(m);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("worldtime"));
        });
        s.OnWorld(60, r =>
        {
            var m = new WorldUpdatePrestigeData();
            m.Deserialize(r);
            PlayerProfileState.Apply(m);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("prestige"));
        });
        s.OnWorld(78, r =>
        {
            var m = new WorldUpdateSpecialCoin();
            m.Deserialize(r);
            PlayerProfileState.Apply(m);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("coin"));
        });
        s.OnWorld(46, r =>
        {
            var m = new WorldUpdateFamilyMember();
            m.Deserialize(r);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("family"));
        });
        s.OnWorld(48, r =>
        {
            var m = new WorldFamilyInvitation();
            m.Deserialize(r);
            PlayerProfileState.Apply(m);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("familyinvite"));
        });
        s.OnWorld(54, r =>
        {
            var m = new WorldRepeatedLogin();
            m.Deserialize(r);
            PlayerProfileState.Apply(m);
            _pending.Enqueue(
                () => PlayerProfileState.RaiseChanged("relogin"));
        });
        // Item mall : mutation immédiate (Apply*Data, thread-safe)
        // + Changed posté — les binds de la fenêtre arrivaient après
        // le premier lot 62 (« non géré »).
        s.OnWorld(56, r =>
        {
            var m = new WorldItemMallAnything();
            m.Deserialize(r);
            ItemMallState.ApplyAnythingData(m.Anything);
            // Natif : 56 vrai → le client demande la liste (17).
            if (m.Anything)
                _ = s.SendWorldAsync(new WorldItemMallReceivableQuery(),
                    CancellationToken.None);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(57, r =>
        {
            var m = new WorldItemMallReceivableList();
            m.Deserialize(r);
            ItemMallState.ApplyReceiptsData(m);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(58, r =>
        {
            var m = new WorldItemMallReceiveResult();
            m.Deserialize(r);
            ItemMallState.ApplyReceiveResultData(m);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(62, r =>
        {
            var m = new WorldItemMallList();
            m.Deserialize(r);
            ItemMallState.ApplyListData(m);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(63, r =>
        {
            var m = new WorldItemMallBuyResult();
            m.Deserialize(r);
            ItemMallState.ApplyBuyResultData(m);
            // Achat réussi → re-demande des reçus (livraison immédiate).
            if (m.Result == ItemMallResults.Ok)
                _ = s.SendWorldAsync(new WorldItemMallReceivableQuery(),
                    CancellationToken.None);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(64, r =>
        {
            var m = new WorldItemMallPointInfo();
            m.Deserialize(r);
            ItemMallState.ApplyPointsData(m.Field1, m.Field2);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
        s.OnWorld(79, r =>
        {
            var m = new WorldItemMallLimit();
            m.Deserialize(r);
            ItemMallState.ApplyLimitData(m);
            _pending.Enqueue(ItemMallState.RaiseChanged);
        });
    }
}
