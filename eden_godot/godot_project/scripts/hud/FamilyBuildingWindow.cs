using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Gestion des bâtiments de la ville de guilde (VDG) — lot C2.
/// Layout natif <c>familybuilding_forest.layout</c>
/// (« Root/FamilyBuildingSetWnd », 726×512) : carte
/// <c>BuildingArea</c> avec 5 aires (<c>Area0-4</c>,
/// <c>Building0-4</c> = bases de groupe) + 8 slots NPC par aire
/// (<c>Npc1-8</c>, <c>Npc11-18</c>, … <c>Npc41-48</c>) + zone relique
/// <c>Building5</c> (loc 0x33) ; panneau droit <c>CollectArea</c> :
/// <c>BC0-15</c> (conteneur 1, bâtiments) et <c>NC0-19</c>
/// (conteneur 2, contrats NPC).
///
/// Wire (preuves : <c>ZoneEntryFlow.FamilyInstance.cs</c>,
/// <c>FamilyPackets.FamilyContainerData</c>, désasm
/// <c>CFamilyBuildingShop::OpenShop @0x8578c90</c>,
/// <c>SFamily::CheckItemLoc @0x8254d40</c>) :
///   - Ouverture : service NPC « family_building » (dialogue type 11)
///     → <b>483</b> <c>[i32 npcUid]</c> puis un <b>484</b> par
///     conteneur non vide (0 = sol VDG, 1-3 = annexes).
///   - <b>132</b> C→S → <b>487</b> données d'instance (nom, niveau,
///     house_open + bâtiments du conteneur 0).
///   - <b>130</b> C→S <c>[s16 mode=0][s16 container][s16 loc]
///     [s16 bag][s16 slot]</c> : déposer un item du sac.
///     Bâtiment (kind 0x29) → base de groupe (0/10/20/30/40) ;
///     NPC (kind 0x2a) → loc base+1..base+8 d'une aire bâtie ;
///     relique (subtype 0x16) → loc 0x33.
///   - <b>131</b> C→S <c>[s16 container][s16 loc][s16 bag][s16 slot]
///     </c> : reprendre un item dans le sac (clic droit).
///   - Fermeture : <b>120</b> StrengthenClose <c>[s16 0x3b]</c>
///     (subtype de l'op CFamilyBuildingOp).
/// </summary>
public partial class FamilyBuildingWindow : Control
{
    /// <summary>Subtype natif de l'op bâtiment famille (0x3b).</summary>
    private const short FamilyBuildingOp = 0x3b;
    /// <summary>Loc de la zone relique (« 聖物祭祀區 »).</summary>
    private const short HolyLoc = 0x33;

    private Control? _window;
    private bool _open;

    /// <summary>conteneur → (loc → item) ; 0 = sol VDG, 1-3 annexes.
    /// </summary>
    private readonly Dictionary<short, Dictionary<short,
        ZoneClientFamilyContainerData.Item>> _containers = new();

    /// <summary>Slots de la grille : cellule + (conteneur, loc).</summary>
    private readonly List<SlotRef> _slots = new();

    /// <summary>Un slot du layout. <c>Loc</c> est mutable : les
    /// grilles du panneau droit (BC/NC) défilent par lignes de 4.</summary>
    private sealed class SlotRef
    {
        public ItemSlotCell Cell = null!;
        public short Cont;
        public short Loc;
        /// <summary>Colonne/ligne dans la grille de défilement (-1 si
        /// le slot ne défile pas).</summary>
        public int GridIndex = -1;
        /// <summary>Aire (0-4) et rang 1-8 pour les slots PNJ, sinon -1.</summary>
        public int Group = -1, Idx = -1;
        public Texture2D? Normal;
        public Label? NpcName;
    }

    /// <summary>Colonnes des grilles BC/NC (layout : 4 colonnes).</summary>
    private const int GridCols = 4;
    /// <summary>Capacité native des conteneurs annexes (SFamily::
    /// IsBuildingStorageFull/IsNpcStorageFull = 40).</summary>
    private const int ContainerCapacity = 40;
    private readonly int[] _scrollRow = new int[3];

    // Caps NPC par niveau de bâtiment-1 — copie des .rodata natifs du
    // serveur (SFamily::CheckNpcSizeLimit @0x8464ea0 : HouseNpcLimit
    // type 2, ProduceNpcLimit types 4-9, MallNpcLimit type 10).
    private static readonly ushort[] HouseNpcLimit =
        { 0, 0, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 };
    private static readonly ushort[] ProduceNpcLimit =
        { 0, 0, 4, 5, 6, 8, 8, 8, 8, 8, 8, 8, 8, 8 };
    private static readonly ushort[] MallNpcLimit =
        { 0, 0, 0, 1, 1, 2, 2, 3, 3, 3, 3, 3, 3, 3 };

    private Texture2D? _lockTex;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterFamilyBuilding:
                Open();
                break;
            case ZoneClientFamilyContainerData d:
                _containers[d.Container] = d.Items
                    .ToDictionary(i => i.Loc, i => i);
                RefreshAll();
                break;
            case ZoneClientFamilyInstanceData d:
                if (d.Container == 0)
                    _containers[0] = d.Buildings.ToDictionary(
                        b => b.Loc,
                        b => new ZoneClientFamilyContainerData.Item(
                            b.Loc, b.ItemTid, b.Tid, b.ItemType,
                            b.Durability, b.Level, b.Exp, 0, 0));
                RefreshAll();
                break;
        }
    }

    // ------------------------- réseau -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning(
                $"[familybuilding] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[familybuilding] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------- fenêtre -------------------------

    /// <summary>Layout bâti : forêt (node 501) ou montagne (502) —
    /// mêmes noms de widgets, seul l'habillage change
    /// (familybuilding_forest/_mountain.layout).</summary>
    private string? _builtLayout;

    private static string WantedLayout() =>
        GuildState.HouseOpen == 502
            ? "familybuilding_mountain.layout"
            : "familybuilding_forest.layout";

    public void Open()
    {
        if (_window != null && _builtLayout != WantedLayout())
        {
            // Ville d'un autre type que celle déjà construite : on
            // reconstruit la fenêtre avec le bon layout.
            _window.QueueFree();
            _window = null;
            _slots.Clear();
        }
        if (_window == null) BuildWindow();
        _containers.Clear();
        _open = true;
        _window!.Visible = true;
        HudWindows.Front(_window);
        Visible = true;
        Send(new ZoneServerFamilyInstanceGetData());   // 132 → 487
        RefreshAll();
    }

    private void Close()
    {
        if (_open)
        {
            _open = false;
            Send(new ZoneServerStrengthenClose
                { Value = FamilyBuildingOp });   // 120 [0x3b]
        }
        if (_window != null) _window.Visible = false;
        Visible = false;
    }

    private void BuildWindow()
    {
        var hud = HudExtensions.FindHud();
        _builtLayout = WantedLayout();
        GD.Print($"[vdg-panel] FamilyBuildingWindow layout {_builtLayout} " +
            $"(houseOpen={GuildState.HouseOpen})");
        _window = hud?.Build(_builtLayout);
        if (_window == null)
        {
            _window = new PanelContainer
            { CustomMinimumSize = new Vector2(726, 512) };
            var lbl = new Label
            { Text = $"{_builtLayout} introuvable" };
            ((PanelContainer)_window).AddChild(lbl);
        }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(640, 480));
        var tb = DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Guild town buildings"));
        // Barre de titre commune : elle recouvrait les 30 px du haut
        // (noms d'aires, 1re carte) — on agrandit vers le haut.
        _window.OffsetTop -= 30;
        // Les enfants ont des ancres d'échelle : agrandir ne les
        // décale pas sous la barre → on les descend de 30 px.
        foreach (var ch in _window.GetChildren())
            if (ch is Control cc && cc.Name.ToString().StartsWith("Root"))
                cc.OffsetTop += 30;
        // Titres de panneau : le CN→FR du corpus laisse de l'anglais.
        SetLabel("BText", UiTextEn.Translate("Building Collection"));
        SetLabel("BText1",
            UiTextEn.Translate("Character Contract Collection"));
        DragHelper.Persist(_window, "familybuilding");
        _window.VisibilityChanged += () =>
        { if (!_window.Visible && _open) Close(); };

        // Slots conteneur 0 : bâtiments aux bases de groupe,
        // NPCs aux locs base+1..base+8, relique à 0x33.
        for (var g = 0; g < 5; g++)
            AddSlot($"Building{g}", 0, (short)(g * 10));
        AddSlot("Building5", 0, HolyLoc);
        // Noms natifs : aire 0 → Npc1-8, aire g → Npc{g}1-8 (Npc11-18,
        // Npc21-28…) — pas « Npc01 ».
        for (var g = 0; g < 5; g++)
            for (var j = 1; j <= 8; j++)
                AddSlot(g == 0 ? $"Npc{j}" : $"Npc{g}{j}", 0,
                    (short)(g * 10 + j));
        // Conteneurs annexes du panneau CollectArea.
        for (var i = 0; i < 16; i++) AddSlot($"BC{i}", 1, (short)i, i);
        for (var i = 0; i < 20; i++) AddSlot($"NC{i}", 2, (short)i, i);
        DecorateAreas();
        WireScroll("BuildingBorder", 1, 4);
        WireScroll("NpcBorder", 2, 5);

        if (FindLeaf<BaseButton>(_window, "CloseBtn") is { } cb)
            cb.Pressed += Close;
    }

    private void AddSlot(string leaf, short cont, short loc,
        int gridIndex = -1)
    {
        var cell = Cell(_window!, leaf);
        if (cell == null) return;
        var sr = new SlotRef
        {
            Cell = cell, Cont = cont, Loc = loc, GridIndex = gridIndex,
        };
        // Le SlotImage natif n'a pas d'image dans le layout (le natif
        // pose « set:main image:FunctionSlot », désasm FUN_00984c80
        // @0x984c80 / chaîne 0xf1e7c8) : on pose la même région.
        cell.Texture ??= HudExtensions.FindHud()
            ?.Region("main", "FunctionSlot")
            ?? ItemSlotCell.AllIn(_window!)
                .FirstOrDefault(x => x.Texture != null)?.Texture;
        cell.Visible = true;
        // Dernier recours (aucune texture résolue) : fond neutre
        // semi-opaque pour que le cadre de l'aire reste visible.
        if (cell.Texture == null)
        {
            var bgp = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            bgp.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            bgp.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.1f, 0.05f, 0.2f, 0.6f),
                BorderColor = new Color(0.7f, 0.65f, 0.9f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            cell.AddChild(bgp);
            cell.MoveChild(bgp, 0);
        }
        sr.Normal = cell.Texture;
        // Slots PNJ : nom FR du PNJ posé (vide tant qu'aucun contrat).
        if (cont == 0 && leaf.StartsWith("Npc"))
        {
            var digits = leaf[3..];
            var n = int.Parse(digits);
            sr.Group = n < 10 ? 0 : n / 10;
            sr.Idx = n < 10 ? n : n % 10;
            var lbl = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                ClipText = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            lbl.AddThemeFontSizeOverride("font_size", 8);
            lbl.AddThemeColorOverride("font_color", Colors.White);
            lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
            lbl.AddThemeConstantOverride("outline_size", 3);
            cell.AddChild(lbl);
            sr.NpcName = lbl;
        }
        cell.PayloadKind = "familybuilding";
        cell.AcceptDrop = StorageItemCell.IsBagPayload;
        cell.ItemDropped += (_, d) =>
        {
            GD.Print($"[familybuilding] drop c={sr.Cont} loc={sr.Loc} " +
                $"bag={d["bag"]} slot={d["slot"]}");
            Send(new ZoneServerFamilyContainerPutItem
            {
                Field1 = 0,                             // mode inventaire
                Field2 = sr.Cont,                       // conteneur
                Field3 = sr.Loc,                        // loc
                Field4 = (short)d["bag"].AsInt32(),     // sac source
                Field5 = (short)d["slot"].AsInt32(),    // slot source
            });
        };
        cell.RightClicked += _ =>
        {
            // Reprendre l'item → sac 1, premier slot libre.
            Send(new ZoneServerFamilyContainerGetItem
            {
                Field1 = sr.Cont, Field2 = sr.Loc, Field3 = 1, Field4 = 0,
            });
        };
        _slots.Add(sr);
    }

    /// <summary>Cadres : le layout natif ne porte aucune image pour
    /// les aires (DefaultWindow) ni pour la carte — sans cadre les
    /// slots flottaient. Cadre parchemin derrière chaque aire/relique,
    /// noms en brun foncé (le layout les donne en noir).</summary>
    private void DecorateAreas()
    {
        var frame = new StyleBoxFlat
        {
            BgColor = new Color(0.86f, 0.78f, 0.60f, 0.78f),
            BorderColor = new Color(0.40f, 0.27f, 0.12f),
            BorderWidthBottom = 2, BorderWidthTop = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        };
        var names = new List<string>();
        for (var g = 0; g < 5; g++) names.Add($"_Area{g}");
        names.Add("_BuildingStatic5");
        foreach (var suffix in names)
        {
            var host = _window!.FindChildren("*", "Control", true, false)
                .OfType<Control>().FirstOrDefault(c =>
                    c.Name.ToString().EndsWith(suffix));
            if (host == null)
            {
                GD.PushWarning($"[familybuilding] aire {suffix} absente");
                continue;
            }
            var p = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            p.AddThemeStyleboxOverride("panel", frame);
            host.AddChild(p);
            host.MoveChild(p, 0);
        }
        var ink = new Color(0.20f, 0.10f, 0.02f);
        for (var g = 0; g <= 5; g++)
            if (FindLeaf<Label>(_window!, $"BuildingName{g}") is { } l)
            {
                l.AddThemeColorOverride("font_color", ink);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.AddThemeFontSizeOverride("font_size", 10);
            }
    }

    // ------------------------- rafraîchissement -------------------------

    private void RefreshAll()
    {
        if (_window == null) return;
        _containers.TryGetValue(0, out var c0);
        _containers.TryGetValue(1, out var c1);
        _containers.TryGetValue(2, out var c2);

        // Slots hors aires PNJ : conteneur + loc courante (défilement).
        foreach (var s in _slots)
        {
            if (s.Group >= 0) continue;
            var items = s.Cont switch { 0 => c0, 1 => c1, 2 => c2, _ => null };
            if (items != null && items.TryGetValue(s.Loc, out var it))
                s.Cell.SetItemId(it.ItemTid, 1);
            else
                s.Cell.Clear();
        }

        // Aires 0-4 (FUN_00984c80 @0x984c80) : sans bâtiment posé à la
        // base, nom vidé et slots PNJ masqués ; avec bâtiment, nom
        // « L<niveau> <nom> » et slots PNJ 1..N (N = 3 hôtel, 8 marché,
        // 3 + 1 si PNJ-concierge pour la production) dont ceux au-delà
        // du cap de niveau sont verrouillés (image DejaVu:invalidItem).
        var diag = new System.Text.StringBuilder();
        for (var g = 0; g < 5; g++)
        {
            var hasB = c0 != null && c0.TryGetValue((short)(g * 10), out _);
            var b = hasB ? c0![(short)(g * 10)] : default;
            int shown = 0, cap = 0;
            if (hasB)
            {
                (shown, cap) = NpcSlots(b.Tid, b.Level);
                SetLabel($"BuildingName{g}",
                    $"L{b.Level} {ItemLabel(b.ItemTid)}");
            }
            else
                SetLabel($"BuildingName{g}", "");
            var occ = 0;
            foreach (var s in _slots.Where(x => x.Group == g))
            {
                ZoneClientFamilyContainerData.Item npc = default;
                var has = hasB && c0!.TryGetValue(
                    (short)(g * 10 + s.Idx), out npc);
                if (has) occ++;
                // Un PNJ réellement posé reste toujours visible.
                s.Cell.Visible = has || (hasB && s.Idx <= shown);
                var locked = hasB && !has && s.Idx > cap;
                s.Cell.Texture = locked ? LockTexture() ?? s.Normal
                    : s.Normal;
                if (has)
                {
                    s.Cell.SetItemId(npc.ItemTid, 1);
                    s.NpcName!.Text = (npc.Tid > 0
                        ? TextResolver.NpcName(npc.Tid) : null)
                        ?? ItemLabel(npc.ItemTid);
                }
                else
                {
                    s.Cell.Clear();
                    s.NpcName!.Text = "";
                }
            }
            diag.Append(hasB
                ? $" A{g}=[tid={b.Tid} item={b.ItemTid} L{b.Level} " +
                  $"slots={shown} cap={cap} pnj={occ}]"
                : $" A{g}=vide");
        }
        // Zone relique (loc 0x33) : le titre natif est statique
        // (« 聖物祭祀區 » → « Objet Sacré »), la relique vit dans le slot.
        SetLabel("BuildingName5", UiTextEn.Translate("聖物祭祀區"));
        GD.Print($"[familybuilding] node={GuildState.HouseOpen}{diag} " +
            $"c0={c0?.Count ?? 0} c1={c1?.Count ?? 0} c2={c2?.Count ?? 0}");
    }

    /// <summary>(slots affichés, cap de niveau) d'une aire.
    /// Natif FUN_00984c80 @0x984c80 : type 2 → 3 slots, type 10 → 8,
    /// types 4-9 → 3 ; cap = table de niveau (+1 si le bâtiment porte
    /// un PNJ-concierge — col6 de c_buliding.ini, déduit du test
    /// <c>def+0x48 != 0</c> natif ; production : +1 slot affiché).
    /// Bâtiment inconnu → 8/8 (aucune invention de verrou).</summary>
    private static (int Shown, int Cap) NpcSlots(int buildingTid,
        int level)
    {
        var def = HouseTable.GetBuilding(buildingTid);
        if (def == null) return (8, 8);
        var l = Math.Clamp(level - 1, 0, 13);
        var housekeeper = def.NpcItemTid != 0 ? 1 : 0;
        switch (def.Type)
        {
            case 2: return (3, HouseNpcLimit[l] + housekeeper);
            case 10: return (8, MallNpcLimit[l] + housekeeper);
            case >= 4 and <= 9:
                return (3 + housekeeper, ProduceNpcLimit[l] + housekeeper);
            default: return (0, 0);   // hall (type 1) : pas de PNJ
        }
    }

    private Texture2D? LockTexture() => _lockTex ??=
        HudExtensions.FindHud()?.Region("DejaVu", "invalidItem");

    /// <summary>Nom FR d'un item (t_item.ini via TextResolver, même
    /// source que l'inventaire) ; repli Big5 c_item seulement si le
    /// texte FR manque.</summary>
    private static string ItemLabel(int itemTid)
    {
        if (TextResolver.ItemName(itemTid) is { Length: > 0 } fr)
            return TextResolver.NameOnly(fr);
        var def = ItemTableCache.Get(itemTid);
        return def != null && def.Name.Length > 0
            ? UiTextEn.Translate(def.Name) : $"#{itemTid}";
    }

    /// <summary>Défilement des grilles BC (conteneur 1) / NC (2) :
    /// 4 colonnes, capacité 40 → 10 lignes, 4/5 visibles.</summary>
    private void WireScroll(string borderLeaf, short cont, int visRows)
    {
        var sb = _window!.FindChildren("*", "VScrollBar", true, false)
            .OfType<VScrollBar>().FirstOrDefault(v =>
                v.Name.ToString().EndsWith($"{borderLeaf}_ScrollBar"));
        if (sb == null)
        {
            GD.PushWarning(
                $"[familybuilding] ScrollBar {borderLeaf} introuvable");
            return;
        }
        var totalRows = ContainerCapacity / GridCols;
        sb.MinValue = 0;
        sb.MaxValue = totalRows;
        sb.Page = visRows;
        sb.Step = 1;
        sb.ValueChanged += v =>
        {
            _scrollRow[cont] = (int)v;
            foreach (var s in _slots.Where(x => x.Cont == cont
                         && x.GridIndex >= 0))
                s.Loc = (short)((_scrollRow[cont] + s.GridIndex
                    / GridCols) * GridCols + s.GridIndex % GridCols);
            RefreshAll();
        };
    }

    private void SetLabel(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Slot natif par suffixe de feuille CEGUI (« Building0 »
    /// → « Root_FamilyBuildingSetWnd_BuildingArea_Building0 »).</summary>
    private static ItemSlotCell? Cell(Control root, string leaf) =>
        ItemSlotCell.AllIn(root).FirstOrDefault(c =>
            c.Name.ToString().EndsWith(leaf));

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        foreach (var n in root.FindChildren("*", typeof(T).Name,
                     true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as T;
        return null;
    }
}
