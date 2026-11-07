using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Boîte magique (conversion d'énergie + scouring) — lot D2. Layout
/// natif <c>magicboxwnd.layout</c> (« Root/MagicBox », 2 onglets) :
///   - <b>Tab1 conversion</b> : <c>First01/UseItem01</c> = item à
///     charger, <c>Second01-08</c> = sacrifices, étoiles
///     <c>Star01-05</c>, barre <c>Energy01/MpBar</c>, combos
///     <c>Power01-03</c>, catalyseur <c>First02/UseItem01</c> ;
///   - <b>Tab2 scouring</b> : même géométrie (combos rerollés,
///     sélection par clic sur Power01-03), catalyseur
///     <c>First02/UseItem02</c>.
///
/// Wire (preuves : <c>ZoneEntryFlow.MagicBox.cs</c>, désasm
/// <c>CItemFactory::InitMagicBox/DoMagicBox</c>, codec serveur
/// <c>MagicBoxPackets</c>) :
///   - Ouverture : service NPC type 31 → <b>612</b> (corps vide,
///     op interne 31/0x58).
///   - Exécution : <b>257</b> <c>[u16 type][u16 n][s16 sacs×n]
///     [u16 n][s16 slots×n][s16 mainBag][s16 mainSlot][u16 n]
///     [s16 combos×n][s16 mallBag][s16 mallSlot]</c> — type 1 =
///     conversion (sacrifices → énergie/étoile/combo), type 2 =
///     scouring (reroll des combos sélectionnés).
///   - <b>433</b> <c>[0x1f][15×100ms][0]</c> cast ~1,5 s ; <b>434</b>
///     <c>[0x1f]</c> refus/annulation ; <b>613</b> résultat
///     (<see cref="ZoneClientMagicBoxResult"/>).
///   - Fermeture : <b>120</b> StrengthenClose <c>[s16 31]</c>.
/// </summary>
public partial class MagicBoxWindow : Control
{
    /// <summary>Subtype natif de l'op boîte magique (cast 433).</summary>
    private const short BoxType = 0x1f;

    private Control? _window;
    private bool _open;

    /// <summary>Slots par onglet : item principal, catalyseur,
    /// 8 sacrifices.</summary>
    private sealed class Tab
    {
        public ItemSlotCell? Main, Mall;
        public readonly List<ItemSlotCell> Sacrifices = new();
        public readonly List<Control> Stars = new();
        public readonly List<Control> Powers = new();
        public ProgressBar? Energy;
        public readonly HashSet<int> SelectedCombos = new();
    }

    private readonly Tab[] _tabs = { new(), new() };
    private TabContainer? _tabCtl;
    private Label? _status;

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
            case ZoneClientOpenMagicBoxDialog:
                Open();
                break;
            case ZoneStrengthenCasting c when c.WorkType == BoxType:
                SetStatus(UiTextEn.Translate("Working…"));
                break;
            case ZoneClientStrengthenItemCancel c
                when c.Value == BoxType:
                SetStatus(UiTextEn.Translate("Cancelled."));
                break;
            case ZoneClientMagicBoxResult r:
                ApplyResult(r);
                break;
        }
    }

    private void ApplyResult(ZoneClientMagicBoxResult r)
    {
        var t = _tabs[Math.Clamp(r.Type - 1, 0, 1)];
        for (var i = 0; i < t.Stars.Count; i++)
            t.Stars[i].Visible = i < r.StarLevel;
        if (t.Energy != null) t.Energy.Value = r.Energy;
        for (var i = 0; i < t.Powers.Count; i++)
            SetLeafText(t.Powers[i],
                i < r.Combos.Count && r.Combos[i] > 0
                    ? $"{r.Combos[i]}" : "—");
        foreach (var cell in t.Sacrifices) cell.Clear();
        t.Mall?.Clear();
        t.SelectedCombos.Clear();
        SetStatus(UiTextEn.Translate("Done."));
    }

    // ------------------------- réseau -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[magicbox] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[magicbox] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------- fenêtre -------------------------

    public void Open()
    {
        if (_window == null) BuildWindow();
        _open = true;
        _window!.Visible = true;
        Visible = true;
        SetStatus("");
    }

    private void Close()
    {
        if (_open)
        {
            _open = false;
            Send(new ZoneServerStrengthenClose { Value = 31 });
        }
        if (_window != null) _window.Visible = false;
        Visible = false;
    }

    private void BuildWindow()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("magicboxwnd.layout");
        if (_window == null)
        {
            _window = new PanelContainer
            { CustomMinimumSize = new Vector2(400, 260) };
            ((PanelContainer)_window).AddChild(new Label
            { Text = "magicboxwnd.layout introuvable" });
        }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(520, 420));
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Magic Box"));
        DragHelper.Persist(_window, "magicbox");
        _window.VisibilityChanged += () =>
        { if (!_window.Visible && _open) Close(); };
        _tabCtl = FindLeaf<TabContainer>(_window, "TabControl");

        for (var ti = 0; ti < 2; ti++)
        {
            var tab = _tabs[ti];
            var root = FindLeaf<Control>(_window, $"Tab{ti + 1}");
            if (root == null) continue;
            tab.Main = Cell(
                FindLeaf<Control>(root, "First01") ?? root, "UseItem01");
            var f2 = FindLeaf<Control>(root, "First02") ?? root;
            tab.Mall = Cell(f2,
                ti == 0 ? "UseItem01" : "UseItem02");
            for (var i = 1; i <= 8; i++)
            {
                var c = Cell(root, $"Second{i:D2}");
                if (c != null) tab.Sacrifices.Add(c);
            }
            for (var i = 1; i <= 5; i++)
            {
                var s = FindLeaf<Control>(root, $"Star{i:D2}");
                if (s != null) { s.Visible = false; tab.Stars.Add(s); }
            }
            var powerRoot = ti == 0 ? "Power01" : "PowerBG";
            for (var i = 1; i <= 3; i++)
            {
                var p = FindLeaf<Control>(root,
                    $"{powerRoot}/Image{i:D2}");
                if (p != null)
                {
                    var idx = i - 1; var tb = tab;
                    p.GuiInput += e =>
                    {
                        if (e is InputEventMouseButton
                            { Pressed: true, ButtonIndex:
                                MouseButton.Left })
                            ToggleCombo(tb, idx);
                    };
                    tab.Powers.Add(p);
                }
            }
            tab.Energy = FindLeaf<ProgressBar>(root, "MpBar");
            var ok = FindLeaf<BaseButton>(root, "OkButton");
            if (ok != null)
            {
                var type = ti + 1;
                ok.Pressed += () => Execute(type);
            }
            var close = FindLeaf<BaseButton>(root, "CloseButton");
            if (close != null) close.Pressed += Close;
            // Accepte le drop de sac sur main/mall/sacrifices.
            foreach (var cell in new[] { tab.Main, tab.Mall }
                     .Concat(tab.Sacrifices))
            {
                if (cell == null) continue;
                cell.AcceptDrop = StorageItemCell.IsBagPayload;
                cell.ItemDropped += (_, d) =>
                {
                    var it = new InventorySlot(
                        d["bag"].AsInt32(), d["slot"].AsInt32(),
                        d["itemId"].AsInt32(), 0, 0, 0);
                    GD.Print($"[magicbox] drop {cell.Name} " +
                        $"bag={it.Bag} slot={it.Slot} id={it.ItemId}");
                    cell.SetItem(it);
                };
                cell.RightClicked += _ => cell.Clear();
            }
            // Valeurs de maquette (100/100, x1111111…) vidées ; noms de
            // nœuds préfixés → on se fonde sur le texte, pas sur le nom.
            CleanTexts(root);
            Callable.From(() => CleanTexts(root)).CallDeferred();
            if (FindLeaf<Button>(root, "OkButton") is { } okb)
                okb.Text = ti == 0 ? "Transmuter" : "Raffiner";
            if (FindLeaf<Button>(root, "CloseButton") is { } clb)
                clb.Text = "Fermer";
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Mock =
        new(@"^(x?\d{5,}|\d+/\d+)$");

    /// <summary>« Text01 » du panneau gauche (x需求裝備:) : libellé de la
    /// ligne masquée ci-dessus.</summary>
    private static bool IsReqLine(Node n) =>
        n.Name.ToString().EndsWith("BackgroundLeft02/Text01")
        || (n.Name.ToString().EndsWith("Text01")
            && n.GetParent()?.Name.ToString().EndsWith("BackgroundLeft02") == true);

    private static void CleanTexts(Control root)
    {
        foreach (var n in root.FindChildren("*", "Control", true, false))
        {
            string? t = n is Label l ? l.Text
                : n is RichTextLabel r ? r.Text : null;
            if (t == null) continue;
            // Texte clair sur fond clair : violet foncé (cf. AuctionWindow).
            var dark = new Color(0.16f, 0.10f, 0.26f);
            if (n is Label dl) dl.AddThemeColorOverride("font_color", dark);
            else if (n is RichTextLabel dr)
                dr.AddThemeColorOverride("default_color", dark);
            // Ligne « Équipement requis » de maquette (jamais pilotée par
            // le code) : superposée aux titres, masquée.
            var nm = n.Name.ToString();
            if (nm.EndsWith("EquipText01") || nm.EndsWith("ColorText01")
                || nm.EndsWith("LeftText01") || IsReqLine(n))
            { if (n is CanvasItem ci) ci.Visible = false; continue; }
            t = t.Trim();
            var fr = t switch
            {
                "Cube Power" => "Puissance du cube",
                "Cube Energy" => "Énergie du cube",
                _ => Mock.IsMatch(t) ? "" : null,
            };
            if (fr == null) continue;
            if (n is Label l2) l2.Text = fr;
            else if (n is RichTextLabel r2) r2.Text = fr;
        }
    }

    private void ToggleCombo(Tab t, int idx)
    {
        if (!t.SelectedCombos.Add(idx)) t.SelectedCombos.Remove(idx);
        // Marque visuelle : modulate sur l'image.
        if (idx < t.Powers.Count)
            t.Powers[idx].Modulate = t.SelectedCombos.Contains(idx)
                ? new Color(1f, 0.85f, 0.4f) : Colors.White;
    }

    private void Execute(int type)
    {
        var t = _tabs[type - 1];
        var msg = new ZoneServerExecuteMagicBox { Type = (ushort)type };
        foreach (var c in t.Sacrifices)
            if (c.Item is { } it)
                msg.Sacrifices.Add(((short)it.Bag, (short)it.Slot));
        if (t.Main?.Item is { } m)
        { msg.MainBag = (short)m.Bag; msg.MainSlot = (short)m.Slot; }
        if (type == 2)
            msg.SelectedCombos.AddRange(
                t.SelectedCombos.OrderBy(i => i).Select(i => (short)i));
        if (t.Mall?.Item is { } mall)
        { msg.MallBag = (short)mall.Bag; msg.MallSlot = (short)mall.Slot; }
        Send(msg);
        SetStatus("");
    }

    private void SetStatus(string text)
    {
        if (_status == null && _window != null)
        {
            _status = new Label { Position = new Vector2(20, -26) };
            _status.AddThemeColorOverride("font_color",
                new Color(0.9f, 0.8f, 0.4f));
            _window.AddChild(_status);
        }
        if (_status != null) _status.Text = text;
    }

    private static void SetLeafText(Control root, string text)
    {
        foreach (var n in root.GetChildren())
            if (n is Label l) { l.Text = text; return; }
            else if (n is RichTextLabel r) { r.Text = text; return; }
        // descend d'un niveau (Image01/Power01)
        foreach (var n in root.GetChildren())
            if (n is Control c) SetLeafText(c, text);
    }

    /// <summary>Slot natif par suffixe de feuille CEGUI.</summary>
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
