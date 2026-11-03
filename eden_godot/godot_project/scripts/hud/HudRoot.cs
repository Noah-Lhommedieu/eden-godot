using Godot;
using EdenClient.Data;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Racine du HUD in-game (<c>scenes/hud.tscn</c>) — CanvasLayer posé
/// au-dessus du monde 3D par <see cref="GameScene"/>.
///
/// Montage : le contexte CEGUI (scheme + looknfeel + imagesets, ~1 s de
/// XML) est chargé en tâche de fond <b>une fois</b> (cache statique
/// <see cref="_sharedCtx"/> — survit aux changements de scène), puis
/// chaque panneau instancie son layout natif sur le thread principal.
/// Si uidata est introuvable, les panneaux se construisent quand même
/// en style de repli (StyleBoxFlat sombre) — le HUD ne disparaît jamais.
///
/// API : uniquement thread principal (GameScene draine <c>_pending</c>
/// avant d'appeler). Le chat remonte l'envoi via <see cref="SendChatAsync"/>.
/// </summary>
public partial class HudRoot : CanvasLayer
{
    private static CeguiContext? _sharedCtx;
    private static bool _ctxTried;

    public TopLeftVitals? Vitals { get; private set; }
    public UltimateBar? Ultimate { get; private set; }
    public MinimapPanel? Minimap { get; private set; }
    public QuestTracker? Quests { get; private set; }
    public ChatPanel? Chat { get; private set; }
    public HotbarPanel? Hotbar { get; private set; }
    public MenuIconsPanel? MenuIcons { get; private set; }
    public ZoneBanner? Banner { get; private set; }
    public InventoryWindow? Inventory { get; private set; }
    public ProfileWindow? Profile { get; private set; }
    public TargetFrame? Target { get; private set; }
    public NpcDialogueWindow? Dialogue { get; private set; }
    public WorldMapWindow? WorldMap { get; private set; }
    /// <summary>Menu système « Main Menu » (Échap / icône système).</summary>
    public SystemMenuWindow? SystemMenu { get; private set; }
    /// <summary>Carte régionale (ref carte.png — touche M).</summary>
    public RegionMapWindow? RegionMap { get; private set; }
    public ClassWindow? Skills { get; private set; }
    public QuestWindow? QuestLog { get; private set; }
    public ElfWindow? Elf { get; private set; }
    /// <summary>Barre du familier invoqué (PetFunctionWnd5p).</summary>
    public PetFunctionBar? PetBar { get; private set; }

    /// <summary>Callback d'envoi chat : (canal, cible, texte) — branché
    /// par GameScene sur <c>EdenSession.SendZoneAsync(ZoneChannelMsg)</c>
    /// en live ; en solo l'écho local est fait par <see cref="ChatPanel"/>.</summary>
    public Func<byte, string, string, Task>? SendChatAsync { get; set; }
    /// <summary>Envoi de message zone générique (M6 NCID 160).</summary>
    public Func<EdenClient.Net.NetMessage, Task>? SendZoneAsync { get; set; }

    /// <summary>Émis à la fin de <see cref="BuildAll"/> — GameScene
    /// repousse l'état connu (zone, vitals…) reçu avant la fin du
    /// chargement asynchrone du contexte CEGUI.</summary>
    public event Action? HudReady;

    /// <summary>true tant que la saisie chat a le focus — GameScene
    /// suspend alors le WASD du PlayerController.</summary>
    public bool ChatHasFocus => Chat?.InputFocused ?? false;

    /// <summary>Le HUD consomme-t-il la souris ce frame (info debug).</summary>
    public override void _Ready()
    {
        Layer = 10;                 // au-dessus du monde, sous la DebugUI
        if (_sharedCtx == null && !_ctxTried)
            LoadContextAsync();     // fire-and-forget
        else
            BuildAll();
    }

    /// <summary>Chargement CEGUI hors thread (parsing XML ~1 s) —
    /// continuation await → thread principal pour construire.</summary>
    private async void LoadContextAsync()
    {
        _ctxTried = true;
        var uidata = DataPaths.FindSubdir("uidata");
        CeguiContext? ctx = null;
        if (uidata != null)
        {
            try { ctx = await Task.Run(() => CeguiContext.Load(uidata)); }
            catch (Exception e)
            {
                GD.PrintErr($"[hud] uidata : {e.Message}");
            }
        }
        if (ctx == null)
            GD.PrintErr("[hud] pas de contexte CEGUI — style de repli");
        _sharedCtx = ctx;
        Uid = uidata ?? "";
        if (IsInsideTree()) BuildAll();
    }

    /// <summary>Répertoire uidata (layouts) — vide si absent.</summary>
    public string Uid { get; private set; } = "";

    /// <summary>Contexte CEGUI partagé (null si uidata absent).</summary>
    public CeguiContext? Ctx => _sharedCtx;

    private UiTextureCache? _tex;

    /// <summary>Construit un layout natif → Control, ou null.</summary>
    public Control? Build(string layoutFile)
    {
        if (_sharedCtx == null || _tex == null || Uid.Length == 0)
            return null;
        var path = Path.Combine(Uid, "layouts", layoutFile);
        if (!Vfs.EdenVfs.Exists(path)) return null;
        try
        {
            var c = CeguiToGodot.BuildLayoutFile(path, _sharedCtx, _tex);
            GD.Print($"[hud] {layoutFile} → " +
                     (c != null ? $"{c.Name} ok" : "null"));
            return c;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[hud] layout {layoutFile} : {e.Message}");
            return null;
        }
    }

    /// <summary>Cache textures atlas partagé (null hors ctx).</summary>
    public UiTextureCache? Tex => _tex;

    /// <summary>Région d'atlas « set:X image:Y » → AtlasTexture.</summary>
    public AtlasTexture? Region(string set, string image) =>
        _tex?.GetRegion(new CeguiImageRef(set, image));

    private void DemoTarget(string label, byte kind, int family, int type)
    {
        Target?.SetTarget(0xFFFFA002, label, kind, kind == 8 ? 0 : 900,
            kind == 8 ? 0 : 1200, family);
        if (kind == 12) Target?.SetBreak(type, 0, 0, 0);
    }

    /// <summary>Instancie tous les panneaux (thread principal).</summary>
    private void BuildAll()
    {
        if (_sharedCtx == null || GetChildCount() > 0) return;
        _tex = new UiTextureCache(_sharedCtx.Imagesets);
        _tex.SetTheme(UiTextureCache.SavedTheme());

        Vitals = new TopLeftVitals { Name = "Vitals" };
        Vitals.Build(this);
        Mount(Vitals);

        Ultimate = new UltimateBar { Name = "Ultimate" };
        Ultimate.Build(this);
        Mount(Ultimate);
        // hud-interfaces.md : feature custom différée — barre cachée
        // tant que le portage initial n'est pas terminé.
        Ultimate.Visible = false;
        // Barre native (ref-shots : « ULTIMATE Lv N EXP x/y Perks »
        // haut-centre) alimentée par NCID 2032 → PlayerProfileState.
        Protocol.PlayerProfileState.Changed += section =>
        {
            if (section == "limitless" && Ultimate != null)
            {
                var l = Protocol.PlayerProfileState.Limitless;
                Ultimate.Set(l.Level, l.Exp, l.NextExp, l.Perks);
            }
        };

        Minimap = new MinimapPanel { Name = "Minimap" };
        Minimap.Build(this);
        Mount(Minimap);

        Quests = new QuestTracker { Name = "Quests" };
        Quests.Build(this);
        Quests.Send = m => SendZoneAsync?.Invoke(m) ?? Task.CompletedTask;
        Mount(Quests);
        // hud-interfaces.md : objectif cliqué → auto-follow vers la cible.
        Quests.ObjectiveClicked += (mid, label) =>
            AutoRoute.ToMissionTarget(this, mid, label);

        Chat = new ChatPanel { Name = "Chat" };
        Chat.Build(this);
        Chat.SendChat += (ch, target, text) =>
        {
            if (SendChatAsync != null)
                _ = SendChatAsync(ch, target, text);
            else
                Chat.AddLocalEcho(ch, text);   // solo : écho local
        };
        Mount(Chat);

        Hotbar = new HotbarPanel { Name = "Hotbar" };
        Hotbar.Build(this);
        Mount(Hotbar);

        MenuIcons = new MenuIconsPanel { Name = "MenuIcons" };
        MenuIcons.Build(this);
        MenuIcons.MenuAction += OnMenuAction;
        Mount(MenuIcons);

        Banner = new ZoneBanner { Name = "Banner" };
        Mount(Banner);

        Inventory = new InventoryWindow { Name = "Inventory" };
        Inventory.Build(this);
        Mount(Inventory);

        Profile = new ProfileWindow { Name = "Profile" };
        Profile.Build(this);
        Mount(Profile);

        // Fenêtres C7/C8/C10 — montées sur le même canvas (ordre :
        // au-dessus des panneaux permanents).
        Target = new TargetFrame { Name = "TargetFrame" };
        Target.Build(this);
        Mount(Target);

        Dialogue = new NpcDialogueWindow { Name = "NpcDialogue" };
        Dialogue.Build(this);
        Mount(Dialogue);

        WorldMap = new WorldMapWindow { Name = "WorldMap" };
        WorldMap.Build(this);
        Mount(WorldMap);

        RegionMap = new RegionMapWindow { Name = "RegionMap" };
        RegionMap.Build(this);
        Mount(RegionMap);

        SystemMenu = new SystemMenuWindow { Name = "SystemMenu" };
        SystemMenu.Build(this);
        Mount(SystemMenu);

        Skills = new ClassWindow { Name = "Skills" };
        Skills.Build(this);
        Mount(Skills);

        QuestLog = new QuestWindow { Name = "QuestLog" };
        QuestLog.Build(this);
        QuestLog.Send = m =>
            SendZoneAsync?.Invoke(m) ?? Task.CompletedTask;
        QuestLog.ObjectiveClicked += (mid, label) =>
            AutoRoute.ToMissionTarget(this, mid, label);
        Mount(QuestLog);

        // Fenêtre Familier (PetWndV3 natif — alimentée par le bridge
        // elf monté par HudNetBridge).
        Elf = new ElfWindow { Name = "Elf" };
        Elf.Build(this);
        Mount(Elf);
        // Barre d'actions du familier invoqué (PetFunctionWnd5p —
        // visible seulement quand Summoned != null).
        PetBar = new PetFunctionBar { Name = "PetBar" };
        PetBar.Build(this);
        Mount(PetBar);

        // Pont réseau 444/285/280 → target frame + dialogue NPC
        // (binding paresseux SessionCarry, cf. HudNetBridge).
        AddChild(new AutoTranslate { Name = "AutoTranslate" });
        AddChild(new HudNetBridge(this));
        AddChild(new HudLayout(this));   // HUDs fixes : drag par leur entête

        GD.Print("[hud] monté (vitals, ultimate, minimap, quêtes, " +
            "chat, hotbar, menu, inventaire, profil, target, " +
            "dialogue, map, skills, questlog)");
        // Abonnés non critiques (GameScene, SettingsApplier…) — un
        // handler qui lève ne doit pas empêcher la suite (timers
        // --hud-open/--hud-dump ci-dessous). Les exceptions en
        // async-void sont sinon silencieusement avalées.
        try { HudReady?.Invoke(); }
        catch (Exception ex)
        {
            GD.PrintErr($"[hud] HudReady : {ex}");
        }
        // --hud-dump : rects globaux des panneaux une fois posés
        // (validation visuelle sans spectacle — vérifie le recadrage).
        var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
            .ToArray();
        // Open+Dump partent avant le shot (prochaine expiration du
        // compte à rebours ~60 f après la reprise — ~1 s de temps
        // traité). Timer nœud (pas SceneTreeTimer) : le wrapper C#
        // de CreateTimer peut être collecté sous la pression GC du
        // chargement live → timeout jamais émis (vérifié : le handler
        // ne tournait pas en live).
        if (System.Array.Exists(args, a => a == "--hud-dump"))
        {
            var dt = new Godot.Timer
            {
                WaitTime = 1.2, OneShot = true, Autostart = true,
            };
            AddChild(dt);
            dt.Timeout += () => { DumpRects(); dt.QueueFree(); };
        }
        // --hud-open=inventory,profile : force-ouvre des fenêtres
        // (captures de validation — pas d'injection d'input headless).
        var openKeys = args
            .Where(a => a.StartsWith("--hud-open="))
            .SelectMany(a => a[11..].Split(','))
            .Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
        if (openKeys.Count > 0)
        {
            GD.Print($"[hud] --hud-open planifié " +
                $"({string.Join(",", openKeys)}) hud={GetPath()}");
            var ot = new Godot.Timer
            {
                WaitTime = 1.0, OneShot = true, Autostart = true,
            };
            AddChild(ot);
            ot.Timeout += () =>
            {
                foreach (var w in openKeys) ForceOpen(w);
                ot.QueueFree();
            };
        }
        // --drag-test=<fenêtre> : sonde de drag synthétique
        // (pression + déplacement + relâchement injectés, positions
        // et persistance loguées — preuve H5).
        foreach (var a in args)
            if (a.StartsWith("--drag-test="))
                AddChild(new DragHelper.DragProbe(this, a[12..].Trim()));
        // --esc-test : sonde Échap/croix (ordre de fermeture,
        // Main Menu sur pile vide — preuve H4).
        if (args.Contains("--esc-test"))
            AddChild(new HudWindows.EscProbe(this));
        // --inv-test : sonde inventaire (move/split/tri/équipement).
        if (args.Contains("--inv-test"))
            AddChild(new InventoryProbe(this));
        // --ride-test : monte la monture 20051 (captures cavalier).
        if (args.Contains("--ride-test"))
            AddChild(new RideProbe(this));
        // --loot-test : menu portrait + dialogue loot natif (U1).
        if (args.Contains("--loot-test") && Vitals != null)
            AddChild(new TopLeftVitals.LootProbe(Vitals));
        // --exp-test : double barre EXP + menu précision (U2).
        if (args.Contains("--exp-test") && Hotbar != null)
            AddChild(new HotbarPanel.ExpProbe(Hotbar));
        // --zoom-test : paliers de zoom minimap + icônes natives (U3).
        if (args.Contains("--zoom-test") && Minimap != null)
            AddChild(new MinimapPanel.ZoomProbe(Minimap));
        // --track-test : suivi quête shift+clic → C→S 186 (U4).
        if (args.Contains("--track-test") && QuestLog != null)
            AddChild(new QuestWindow.TrackProbe(QuestLog));
        // --chat-test : préfixe /o → canal Peer + envoi NCID 34 (U5).
        if (args.Contains("--chat-test") && Chat != null)
            AddChild(new ChatPanel.ChatProbe(Chat));
        // --shortcut-test : drop/activation/pages raccourcis (U6).
        if (args.Contains("--shortcut-test") && Hotbar != null)
            AddChild(new HotbarPanel.ShortcutProbe(Hotbar));
        // --menu-test : clic réel de chaque icône menu → fenêtre (U7).
        if (args.Contains("--menu-test") && MenuIcons != null)
            AddChild(new MenuIconsPanel.MenuProbe(MenuIcons, this));
        // --enchant-test : buff+debuff réels → slots → clic droit 85
        // (U8) — la barre est découverte paresseusement par la sonde
        // (montée par HudExtensions après BuildAll).
        if (args.Contains("--enchant-test"))
            AddChild(new BuffBar.BuffProbe(this));
        // --hud-hover=<nom nœud>|<x>,<y> : warp réel du pointeur sur
        // un contrôle (WarpMouse + événement motion réinjecté en
        // coordonnées viewport — insensible au mapping écran OS),
        // puis logue le contrôle effectivement survolé : preuve de
        // tooltip/hover sans dépendre du warping XTEST.
        foreach (var a in args.Where(x => x.StartsWith("--hud-hover=")))
            AddChild(new HoverProbe(this, a[12..].Trim()));
    }

    /// <summary>Ouvre une fenêtre par clé courte — les clés
    /// historiques ont leur toggle dédié ; les autres cherchent le
    /// nœud par nom (fenêtres montées par HudExtensions) et forcent
    /// la visibilité (racine + panneau interne éventuel).</summary>
    public void OpenWindow(string key) => ForceOpen(key);

    private void ForceOpen(string key)
    {
        switch (key)
        {
            case "inventory": ToggleInventory(); return;
            case "profile": ToggleProfile(); return;
            case "skills":
            case "class": ToggleSkills(); return;
            case "quests":
            case "questlog": ToggleQuests(); return;
            // --hud-open=questsheet[1|2|3] : fiche de quête (op 1 offre,
            // 2 rendu, 3 en cours) sur la quête 1488 — captures QA.
            case "questsheet":
            case "questsheet1": Dialogue?.ShowMissionSheet(1, 1488, 50299); return;
            case "questsheet2": Dialogue?.ShowMissionSheet(2, 1488, 50299); return;
            case "questsheet3": Dialogue?.ShowMissionSheet(3, 1488, 50299); return;
            case "elf":
                if (Elf != null) Elf.Visible = true;
                return;
            case "map": ToggleWorldMap(); return;
            case "system":
            case "menu": HudWindows.Toggle(SystemMenu?.Window); return;
            case "dialogue": Dialogue?.ShowMock(); return;
            case "target":
                Target?.SetTarget(0xFFFFA001,
                    "Lv1 Red Cap Mushroid", 12, 850, 1200);
                Target?.SetTargetOfTarget(
                    Screens.SessionCarry.Character?.Name ?? "test1", 1, 1);
                return;
            // QA badges/étoiles du cadre de cible (famille → médaillon,
            // type → étoiles) : target-elite / -boss / -world / -dungeon
            // / -undead / -human / -npc.
            case "target-elite":
                DemoTarget("Lv81 Archangel of Sin", 12, 6, 2); return;
            case "target-boss":
                DemoTarget("Lv60 Demo Boss", 12, 4, 3); return;
            case "target-world":
                DemoTarget("Lv90 Demo World Boss", 12, 5, 4); return;
            case "target-dungeon":
                DemoTarget("Lv90 Demo Dungeon Boss", 12, 6, 5); return;
            case "target-undead":
                DemoTarget("Lv40 Demo Undead", 12, 3, 1); return;
            case "target-human":
                DemoTarget("Lv40 Demo Bandit", 12, 1, 1); return;
            case "target-npc":
                DemoTarget("Tanja", 8, 0, 0); return;
            // Vague 1-C : hors session, peuple des données de démo
            // pour les captures de validation (--hud-open).
            case "mail":
                ForceOpenExt<MailWindow>("MailWnd", w => w.EnsureDemo());
                return;
            case "auction":
                ForceOpenExt<AuctionWindow>("AuctionWnd",
                    w => w.EnsureDemo());
                return;
            // Vague 1-D : succès + calendrier de connexion — démo
            // injectée par les vrais handlers (480/615/398).
            case "achievements":
            case "achievement":
                ForceOpenExt<AchievementWindow>("AchievementWindow",
                    w => w.EnsureDemo());
                return;
            case "transport":
            {
                // Sonde : routes du transporteur d'Aven (C_Transport).
                var m = new Protocol.ZoneEnterTransport { NpcUid = 50360 };
                foreach (var id in TransportZones.All.OrderBy(i => i)
                             .Take(40))
                    m.Routes.Add((short)id);
                Protocol.TransportState.OnEnterTransport(m);
                return;
            }
            case "daily":
            case "loginreward":
                // Vrai toggle : en live la trame 615 du burst d'entrée
                // auto-ouvre la fenêtre — --hud-open=daily doit pouvoir
                // la refermer pour dégager les captures.
                if (GetTree().Root.FindChild("DailyRewardWindow", true, false)
                        is DailyRewardWindow d)
                {
                    d.EnsureDemo();
                    d.Toggle();
                    GD.Print("[hud] --hud-open DailyRewardWindow (toggle)");
                }
                else GD.PrintErr("[hud] --hud-open daily : nœud introuvable");
                return;
            // Vague 1-A : guilde — Open() re-émet la requête 10 (fiche)
            // + zone 81 (self) en plus de montrer le panneau.
            case "guild":
                ForceOpenExt<GuildWindow>("GuildWnd", w => w.Open());
                return;
            // Vague 2 : banque complète + coffre famille — démo
            // factice hors session (grille interactive, envois no-op).
            case "bank":
                ForceOpenExt<BankWindow>("BankWnd", w => w.EnsureDemo());
                return;
            // Vague 2 : échange J↔J + étal joueur — démo factice
            // hors session (même convention que bank).
            case "trade":
                ForceOpenExt<TradeWindow>("TradeWnd",
                    w => w.EnsureDemo());
                return;
            case "vending":
            case "stall":
                ForceOpenExt<VendingWindow>("VendingWnd",
                    w => w.EnsureDemo());
                return;
            case "familystorage":
            case "familybank":
            case "fchest":
                ForceOpenExt<FamilyStorageWindow>("FamilyStorageWnd",
                    w => w.EnsureDemo());
                return;
            // Vague Reforged : item mall (catalogue démo injecté hors
            // session par Open→EnsureDemo) et neobank (conteneurs
            // factices via EnsureDemo — même état storage).
            case "itemmall":
            case "mall":
                ForceOpenExt<ItemMallWindow>("ItemMallWnd",
                    w => w.Open());
                return;
            case "neobank":
                ForceOpenExt<NeobankWindow>("NeobankWnd",
                    w => w.EnsureDemo());
                return;
            // Vague 2 : bandeau buffs self (démo handle −1),
            // chemin d'éveil (route factice) et fenêtre couple
            // (fiche + cérémonie factices + popup d'invite).
            case "buff":
            case "buffs":
            case "buffbar":
                ForceOpenExt<BuffBar>("BuffBar", w => w.EnsureDemo());
                return;
            case "destiny":
            case "fateroad":
                ForceOpenExt<DestinyWindow>("DestinyWindow",
                    w => w.EnsureDemo());
                return;
            case "couple":
            case "lover":
            case "wedding":
                ForceOpenExt<CoupleWindow>("CoupleWindow",
                    w => w.EnsureDemo());
                return;
            // CastBar : 4 s factices pour capture (progression +
            // secondes restantes — Bloc H).
            // Bannières centre : « Entering Combat » + texte d'état.
            case "combat":
                // Durées longues : visibles au --shot de validation.
                Banner?.ShowCombat(entering: true, seconds: 60);
                Banner?.ShowSystemText(
                    "You have acquired <Hard Truth> state", 60);
                return;
            case "cast":
            case "castbar":
                if (GetTree().Root.FindChild("CastBar", true, false)
                        is Combat.CastBar cb)
                    cb.BeginCast(60f);
                return;
            // Bloc I : fenêtres de service converties en layouts
            // natifs + dialogues duel/battlefield/hôtel/panneau/
            // loot/mort — contenu factice EnsureDemo hors session.
            case "territory":
            case "territorywar":
                ForceOpenExt<TerritoryWindow>("TerritoryWnd",
                    w => w.EnsureDemo());
                return;
            case "colosseum":
            case "arena":
                ForceOpenExt<ColosseumWindow>("ColosseumWnd",
                    w => w.EnsureDemo());
                return;
            case "recruit":
                ForceOpenExt<RecruitWindow>("RecruitWnd",
                    w => w.EnsureDemo());
                return;
            case "fastdress":
            case "dress":
                ForceOpenExt<FastDressWindow>("FastDressWnd",
                    w => w.EnsureDemo());
                return;
            case "rank":
            case "rankings":
                ForceOpenExt<RankWindow>("RankWnd",
                    w => w.EnsureDemo());
                return;
            case "duel":
                ForceOpenExt<DuelDialog>("DuelDialog",
                    w => w.EnsureDemo());
                return;
            case "battlefield":
                ForceOpenExt<BattlefieldWindow>("BattlefieldWnd",
                    w => w.EnsureDemo());
                return;
            case "hotel":
                ForceOpenExt<HotelWindow>("HotelWnd",
                    w => w.EnsureDemo());
                return;
            case "msgboard":
            case "messageboard":
                ForceOpenExt<MessageBoardWindow>("MessageBoardWnd",
                    w => w.EnsureDemo());
                return;
            case "roll":
            case "rolltreasure":
                ForceOpenExt<RollTreasureDialog>("RollTreasureDlg",
                    w => w.EnsureDemo());
                return;
            case "death":
                ForceOpenExt<DeathWindow>("DeathWnd",
                    w => w.EnsureDemo());
                return;
            case "featuredlg":
            case "featdlg":
                ForceOpenExt<FeatureDialogWindow>("FeatureDialogWnd",
                    w => w.Visible = true);
                return;
            case "lottery":
            case "altar":
                ForceOpenExt<LotteryWindow>("LotteryWnd",
                    w => w.Visible = true);
                return;
        }
        // archive:<famille> : Archive ouverte sur une catégorie —
        // validation capture sans clic OS (--hud-open=archive:Mounts).
        if (key.StartsWith("archive:"))
        {
            var cat = key[8..];
            ForceOpenExt<ArchiveWindow>("ArchiveWnd",
                w => w.SelectCategory(cat));
            return;
        }
        // Clés génériques → noms de nœuds montés hors HudRoot.
        var nodeNames = key switch
        {
            "shop" => new[] { "ShopWnd" },
            "social" => new[] { "SocialWnd", "SocialPanel" },
            "skillbar" => new[] { "SkillBar" },
            "craft" => new[] { "CraftPanel", "CraftWindow" },
            "transport" => new[] { "Transport" },
            _ => new[] { key },
        };
        foreach (var n in nodeNames)
            if (GetTree().Root.FindChild(n, true, false) is Control c)
            {
                c.Visible = true;
                foreach (var p in c.FindChildren("*", "Panel",
                             true, false).Cast<Control>())
                    p.Visible = true;
                GD.Print($"[hud] --hud-open {key} → {c.GetPath()}");
                return;
            }
        GD.PrintErr($"[hud] --hud-open {key} : nœud introuvable");
    }

    /// <summary>Ouvre une fenêtre montée par HudExtensions par nom de
    /// nœud racine, après l'avoir laissée préparer son état
    /// (données de démo hors-session…).</summary>
    private void ForceOpenExt<T>(string nodeName, Action<T> prepare)
        where T : Control
    {
        if (GetTree().Root.FindChild(nodeName, true, false) is T c)
        {
            prepare(c);
            c.Visible = true;
            foreach (var p in c.FindChildren("*", "Panel", true, false)
                         .Cast<Control>())
                p.Visible = true;
            GD.Print($"[hud] --hud-open {nodeName} → {c.GetPath()}");
            return;
        }
        GD.PrintErr($"[hud] --hud-open {nodeName} : nœud introuvable");
    }

    /// <summary>Imprime le rect global des panneaux clés (debug
    /// recadrage — activé par --hud-dump).</summary>
    public void DumpRects()
    {
        void Dump(Node? n, string name)
        {
            if (n is Control c)
                GD.Print($"[hud-dump] {name,-28} {c.GetGlobalRect()}");
        }
        var vp = GetViewport().GetVisibleRect().Size;
        GD.Print($"[hud-dump] viewport {vp}");
        Dump(Vitals, "Vitals(root)");
        Dump(TopLeftVitals.Find<Control>(Vitals, "MiniInfoWnd"),
            "MiniInfoWnd");
        Dump(Ultimate, "Ultimate(root)");
        Dump(Minimap, "Minimap(root)");
        Dump(TopLeftVitals.Find<Control>(Minimap, "Radar"), "Radar");
        Dump(TopLeftVitals.Find<Control>(Chat, "MainWnd"), "ChatMain");
        Dump(TopLeftVitals.Find<Control>(Chat, "InputWnd"), "ChatInput");
        Dump(TopLeftVitals.Find<Control>(Hotbar, "FastBar1"), "FastBar1");
        Dump(TopLeftVitals.Find<Control>(Hotbar, "FastBar2"), "FastBar2");
        Dump(TopLeftVitals.Find<Control>(Hotbar, "FunctionBarWnd"),
            "FunctionBarWnd");
        Dump(TopLeftVitals.Find<Control>(MenuIcons, "MenuBackgrand"),
            "MenuBackgrand");
        Dump(Quests, "Quests(root)");
        foreach (var name in new[]
                 {
                     "Root_Inventory", "EquipPanel", "Root_CharacterUI",
                     "Root_TargetInfoWnd", "Root_NpcTalkWnd",
                     "Root_ZoneMapUI", "Root_NewSkillList",
                     "Root_Pet", "Root_PetFunctionWnd",
                     "Root/Pet", "Root_Mission", "Root_TransportWnd",
                     "Root_shop", "Root_StorageUI",
                     "Root_FamilyStorage", "SocialPanel",
                     "GuildPanel",
                     "CraftPanel", "ClassChangePanel", "PartyPanel",
                     "MountPanel", "DeathWnd", "SkillBar", "CastBar",
                     "AchievementPanel", "DailyRewardPanel",
                     "ItemMallWnd", "Root_ItemMal",
                     "NeobankWnd",
                     "Root_BuffWnd", "DestinyPanel", "CouplePanel",
                     "LoverInvitePopup",
                 })
            Dump(GetTree().Root.FindChild(name, true, false), name);
    }

    /// <summary>Monte un panneau : plein écran (les ancres « scale »
    /// des layouts CEGUI se résolvent sur la taille du viewport) +
    /// <see cref="Control.MouseFilterEnum.Ignore"/> — le panneau ne
    /// mange pas les clics sol ; ses enfants gardent leur filtre
    /// (boutons/editbox restent cliquables).</summary>
    private void Mount(Control panel)
    {
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(panel);
    }

    /// <summary>Épingle explicitement un Control sur le viewport :
    /// ancres constantes + offsets en pixels
    /// (<c>pixel = anchor × parentSize + offset</c>). Utilisé pour
    /// recadrer les racines/enfants de layouts dont l'UnifiedAreaRect
    /// natif est une position sauvegardée pour une autre résolution
    /// (fenêtres chat/menu débordant l'écran en 720p).</summary>
    internal static void Pin(Control c,
        float anchorLeft, float anchorTop,
        float anchorRight, float anchorBottom,
        float offLeft, float offTop, float offRight, float offBottom)
    {
        c.AnchorLeft = anchorLeft;
        c.AnchorTop = anchorTop;
        c.AnchorRight = anchorRight;
        c.AnchorBottom = anchorBottom;
        c.OffsetLeft = offLeft;
        c.OffsetTop = offTop;
        c.OffsetRight = offRight;
        c.OffsetBottom = offBottom;
    }

    /// <summary>Action d'une icône du menu bas-droite.</summary>
    private void OnMenuAction(string action)
    {
        switch (action)
        {
            case "bag": ToggleInventory(); break;
            case "character": ToggleProfile(); break;
            case "skill": ToggleSkills(); break;
            case "mission": ToggleQuests(); break;
            // CommuBtn natif → fenêtre sociale (O) : toggle du panneau
            // interne SocialPanel monté par MinimapPanel.
            case "community": ToggleByName("SocialPanel"); break;
            // CityBtn natif → fenêtre guilde (G) : GuildPanel.
            case "city": ToggleByName("GuildPanel"); break;
            // PetBtn (variante 5pPet du FunctionMenu) → Familier (P).
            case "pet": ToggleElf(); break;
            // WorkBtn natif → panneau d'étal joueur (StallUI) monté
            // par HudExtensions (VendingWindow) — Vague 2.
            case "work": ToggleByName("StallPanel"); break;
            // SysBtn natif → « Main Menu » (ref SystemPanel.png).
            case "system": HudWindows.Toggle(SystemMenu?.Window); break;
            // ItemMallBtn natif → boutique cash (canal monde).
            case "itemmall":
                if (GetTree().Root.FindChild("ItemMallWnd", true,
                        false) is ItemMallWindow mall)
                    mall.Toggle();
                break;
            default:
                GD.Print($"[hud] menu {action} — window not " +
                    "implemented");
                Chat?.AddSystem(UiTextEn.Translate($"[{action}] not implemented yet."));
                break;
        }
    }

    /// <summary>Toggle le premier panneau visible portant ce nom —
    /// fenêtres social/guilde montées par MinimapPanel (Panel interne,
    /// la racine FullRect reste toujours présente).</summary>
    private void ToggleByName(string panelName)
    {
        if (GetTree().Root.FindChild(panelName, true, false)
            is Control c)
            HudWindows.ToggleIn(c);
    }

    // -------------------- API publique (thread principal) ----------

    /// <summary>Identité self (310 EnterNodePlayerData / sélection).</summary>
    public void SetSelf(uint charId, string name, int level)
    {
        World.MobDanger.PlayerLevel = level;
        Vitals?.SetIdentity(name, level);
        Profile?.SetIdentity(name, level);
        Hotbar?.SetLevels(level, 0);
    }

    /// <summary>Vitaux self : HP/MP courant et max.</summary>
    public void SetVitals(long hp, long maxHp, long mp, long maxMp)
    {
        HudPrefs.SelfHp = hp; HudPrefs.SelfMaxHp = maxHp;
        Vitals?.SetVitals(hp, maxHp, mp, maxMp);
    }

    /// <summary>XP courante (600) — barre bas-gauche du hotbar.</summary>
    public void SetExp(long exp) => Hotbar?.SetExp(exp);

    /// <summary>Zone courante (313) : code map + nom affiché
    /// (null → nom résolu depuis c_node.ini côté minimap).</summary>
    public void SetZone(string mapCode, string? zoneName)
    {
        Minimap?.SetZone(mapCode, zoneName);
        Banner?.ShowZone(zoneName ?? mapCode);
    }

    /// <summary>Position sol Eden du joueur (x,y) + yaw — minimap.</summary>
    public void SetSelfPos(float x, float y, float yaw) =>
        Minimap?.SetSelfPos(x, y, yaw);

    /// <summary>Message de chat entrant (268/327/266/316).</summary>
    public void AddChat(int channel, string sender, string text) =>
        Chat?.AddMessage(channel, sender, text);

    /// <summary>Texte système rouge centré (474 ScreenMessage).</summary>
    public void ShowScreenMessage(string text) =>
        Banner?.ShowSystemText(text);

    /// <summary>Items d'inventaire (corps 312 parsé).</summary>
    public void SetInventory(IReadOnlyList<Protocol.InventorySlot> items) =>
        Inventory?.SetItems(items);

    /// <summary>Données complètes du 310 (parse
    /// <see cref="Protocol.NativePlayerDataParser"/>) — vitals +
    /// profil.</summary>
    public void SetPlayerData(Protocol.PlayerHudData d)
    {
        HudPrefs.SelfHp = d.Hp; HudPrefs.SelfMaxHp = d.MaxHp;
        Vitals?.SetVitals(d.Hp, d.MaxHp, d.Mp, d.MaxMp);
        Profile?.SetPlayerData(d);
    }

    /// <summary>Race du perso (CharacterSummary monde — le champ du
    /// 310 est une HYPOTHESIS, celui-ci est fiable).</summary>
    public void SetRace(int raceId) => Profile?.SetRace(raceId);

    /// <summary>Registre de quêtes (348 ClientMissionStates) —
    /// tracker bord droit + journal détaillé.</summary>
    public void SetMissions(Protocol.ZoneMissionStates m)
    {
        Quests?.SetMissions(m);
        QuestLog?.SetMissions(m);
    }

    /// <summary>Update unitaire de quête (391).</summary>
    public void UpdateMission(Protocol.ZoneUpdateMissionState m)
    {
        Quests?.UpdateMission(m);
        QuestLog?.UpdateMission(m);
    }

    public void ToggleInventory()
    {
        if (Inventory == null) return;
        HudWindows.ToggleIn(Inventory);
    }

    public void ToggleProfile()
    {
        if (Profile == null) return;
        HudWindows.ToggleIn(Profile);
    }

    /// <summary>Hook lot D (paramètres) — scale global UI
    /// (0.5–2.0). Délègue à <see cref="Settings.SettingsApplier"/>
    /// qui applique le content-scale « canvas_items » du viewport
    /// racine : les ancres des panneaux bord-à-bord restent à
    /// l'écran et le 3D n'est pas affecté.</summary>
    public void ApplyUiScale(float s) =>
        Settings.SettingsApplier.ApplyUiScale(GetWindow(), s);

    public void ToggleSkills()
    {
        HudWindows.ToggleIn(Skills);
    }

    /// <summary>Fenêtre familier (PetWndV3 — montée dans BuildAll,
    /// hotkey P).</summary>
    public void ToggleElf()
    {
        HudWindows.ToggleIn(Elf);
    }

    public void ToggleQuests()
    {
        HudWindows.ToggleIn(QuestLog);
    }

    /// <summary>M / icône carte : carte régionale (le bouton « World
    /// Map » de celle-ci ouvre la carte monde).</summary>
    public void ToggleWorldMap() => HudWindows.ToggleIn(RegionMap);

    /// <summary>Fenêtre succès (Vague 1-D — montée par HudExtensions,
    /// action eden_achievements, défaut L). Le panneau interne porte
    /// la visibilité.</summary>
    internal void ToggleAchievements()
    {
        if (GetTree().Root.FindChild("AchievementWindow", true, false)
            is AchievementWindow w)
            w.Toggle();
    }

    /// <summary>Échap (plan v4 H4) : ferme la fenêtre au premier
    /// plan ; si aucune n'est ouverte, bascule le Main Menu.</summary>
    public void HandleEscape()
    {
        GD.Print($"[esc] open=[{HudWindows.DescribeOpen()}]");
        if (HudWindows.CloseTopmost()) return;
        HudWindows.Toggle(SystemMenu?.Window);
    }

    /// <summary>Clic dans le HUD : la fenêtre cliquée passe devant.</summary>
    public override void _Input(InputEvent e)
    {
        // Échap traité avant le GUI : un Control (fenêtre, bouton) qui
        // consomme la touche ne doit pas empêcher la fermeture.
        if (e is InputEventKey { Pressed: true, Echo: false,
                Keycode: Key.Escape })
        {
            if (ChatHasFocus) Chat?.ReleaseInputFocus();
            else HandleEscape();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton { Pressed: true } mb &&
            mb.ButtonIndex is MouseButton.Left or MouseButton.Right &&
            GetViewport().GuiGetHoveredControl() is { } hov)
            HudWindows.OnPress(hov);
    }

    /// <summary>Referme toutes les fenêtres ouvertes.</summary>
    public void CloseAllWindows()
    {
        HudWindows.CloseAll();
        foreach (var w in new Control?[]
                 { Inventory, Profile, Skills, QuestLog, WorldMap, RegionMap,
                   SystemMenu,
                   Dialogue, Elf })
            if (w != null) w.Visible = false;
        // Panneaux internes des fenêtres montées par HudExtensions.
        foreach (var n in new[] { "AchievementPanel", "DailyRewardPanel" })
            if (GetTree().Root.FindChild(n, true, false) is Control c)
                c.Visible = false;
    }

    // -------------------- hotkeys ----------------------------------
    // Toutes les touches fenêtres passent par les actions eden_* des
    // bindings (SettingsApplier.Dispatch) — rebindables, boutons
    // souris compris. Plus de switch Keycode codé en dur ici : il
    // court-circuitait le débinding (une touche retirée restait
    // active) et ne pouvait pas recevoir de bouton souris.

    /// <summary>Sonde --hud-hover : laisse le HUD s'installer (~2 s,
    /// après --hud-open), warp le pointeur réel sur la cible, puis
    /// logue le contrôle survolé et son appartenance à une liaison
    /// tooltip (vérif visuelle + log).</summary>
    public sealed partial class HoverProbe : Node
    {
        private readonly HudRoot _hud;
        private readonly string _target;
        private double _t;

        public HoverProbe(HudRoot hud, string target)
        {
            _hud = hud; _target = target;
        }

        public override void _Process(double delta)
        {
            _t += delta;
            if (_phase == 0)
            {
                if (_t < 2.2) return;
                // Cible paresseuse : la liste se remplit à l'arrivée
                // du 489, après le montage du HUD — réessayer jusqu'à
                // la deadline.
                var p = Resolve();
                if (p == null)
                {
                    if (_t > 15.0) QueueFree();
                    return;
                }
                // WarpMouse déplace le pointeur OS mais ne réinjecte
                // pas toujours un motion dans le GUI Godot (même
                // remarque que --cursor-test).
                Input.WarpMouse(p.Value);
                Input.ParseInputEvent(new InputEventMouseMotion
                { Position = p.Value });
                _at = p.Value; _phase = 1; _t = 0;
                return;
            }
            if (_t < 0.5) return;
            var hov = _hud.GetViewport().GuiGetHoveredControl();
            GD.Print($"[hud-hover] '{_target}' @{_at} → " +
                $"{hov?.GetPath().ToString() ?? "rien"}");
            QueueFree();
        }

        private Vector2? Resolve()
        {
            if (_target.Contains(','))
            {
                var xy = _target.Split(',');
                return new Vector2(
                    float.Parse(xy[0],
                        System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(xy[1],
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            var c = _hud.GetTree().Root
                .FindChildren("*", "Control", true, false)
                .Cast<Control>()
                .Where(x => x.IsVisibleInTree() &&
                       x.GetGlobalRect().Size is { X: > 2f, Y: > 2f })
                .FirstOrDefault(x =>
                    string.Equals(x.Name, _target,
                        StringComparison.OrdinalIgnoreCase));
            // FindChildren manque certains sous-arbres (ScrollContainer)
            // — FindChild direct comme repli fiable.
            if (c == null)
                c = _hud.GetTree().Root
                    .FindChild(_target, true, false) as Control;
            if (c == null && _t > 15.0)
            {
                var siblings = _hud.GetTree().Root
                    .FindChildren("*", "Control", true, false)
                    .Cast<Control>()
                    .Where(x => x.Name.ToString().StartsWith("ClassRow_") ||
                           x.Name.ToString().StartsWith("SkillCell_"))
                    .Select(x => $"{x.Name}({(x.IsVisibleInTree() ? "vis" : "caché")})");
                GD.PrintErr(
                    $"[hud-hover] '{_target}' : nœud introuvable — " +
                    $"présents: {string.Join(", ", siblings)}");
            }
            return c?.GetGlobalRect().GetCenter();
        }

        private Vector2 _at;
        private int _phase;
    }
}
