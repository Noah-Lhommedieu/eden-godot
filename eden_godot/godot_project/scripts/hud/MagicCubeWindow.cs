using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Cube magique (fusion d'objets) — lot D1. Layout natif
/// <c>newmagiccubewnd.layout</c> (« Root/NewMagicCubeWnd », génération
/// Brandt — la plus riche de fr-full, cf. D4-layouts-craft-fusion) :
/// grille <c>StuffSlot01-16</c> (items placés), <c>Main01-08</c> /
/// <c>Second01-04</c> (matériaux de la recette), <c>Complete01-08</c>
/// (produits), <c>SpecialSlot</c> (sac de chance), coûts or/argent,
/// % de réussite, <c>StartButton</c>/<c>ClearButton</c>/
/// <c>CompleteListButton</c> (liste des recettes).
///
/// Wire (preuves : <c>ZoneEntryFlow.Cube.cs</c>, désasm
/// <c>CNE_CZ_ZoneServerVerifyCubeCosts::Execute @0x829f0d0</c>,
/// <c>CCubeOp @0x8570xxx</c>) :
///   - Ouverture : PlayerAction 101 ('e') → <b>432</b>
///     <c>[i32 npc][s16 0x1c]…</c> (op interne 28).
///   - Lancement : <b>232</b> <c>[u32 recipeId][u16 n][s16 sacs×n]
///     [u16 n][s16 slots×n][s16 luckyBag][s16 luckySlot]</c>.
///   - <b>433</b> <c>[0x1c][15×100ms][0]</c> cast ~1,5 s ; <b>434</b>
///     échec ; <b>435</b> <c>[0x1c][1]</c> succès ; <b>572</b> produits
///     (3 vecteurs sacs/slots/qtés → lus dans le sac rafraîchi par
///     les 274/275).
///   - Fermeture : <b>120</b> StrengthenClose <c>[s16 28]</c>.
/// Recettes : <c>data/db/c_itemchest.ini</c> (même table que
/// S_ItemChest serveur — <c>ItemChestDef</c>, 32 colonnes).
/// </summary>
public partial class MagicCubeWindow : Control
{
    /// <summary>Type du 432 ouvrant le cube + subtype op interne.</summary>
    private const short CubeType = 0x1c;

    /// <summary>Une recette c_itemchest (colonnes serveur).</summary>
    private sealed class Recipe
    {
        public int Id, ItemId, MainAmount, Gold, SuccessRate,
            ProductKey, RequiredLevel, ProductAmount;
        public string Name = "", ProductName = "";
        public readonly List<(int Id, int Qty)> Subs = new();
    }

    private Control? _window;
    private bool _open;
    private readonly List<Recipe> _recipes = new();
    private readonly Dictionary<int, ItemTable.ItemDef> _items = new();
    private Recipe? _recipe;

    /// <summary>Items placés : cellule → (bag, slot).</summary>
    private readonly List<(ItemSlotCell Cell, short Bag, short Slot,
        int ItemId)> _placed = new();
    private ItemSlotCell? _lucky;
    private Control? _recipeWnd;
    private ItemList? _recipeList;
    private Label? _going;

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
            case ZoneClientEnterStrengthen e when e.Field2 == CubeType:
                Open();
                break;
            case ZoneStrengthenCasting c when c.WorkType == CubeType:
                if (_going != null)
                    _going.Text = UiTextEn.Translate("Fusing…");
                break;
            case ZoneClientStrengthenItemCancel c
                when c.Value == CubeType:
                if (_going != null)
                    _going.Text = UiTextEn.Translate("Failed.");
                break;
            case ZoneClientStrengthenItemResult r
                when r.Field1 == CubeType:
                if (_going != null)
                    _going.Text = r.Field2 == 1
                        ? UiTextEn.Translate("Success!")
                        : UiTextEn.Translate("Failed.");
                ClearPlaced();
                break;
            case ZoneClientLoadCubeItems l:
                FillProducts(l);
                break;
        }
    }

    private void FillProducts(ZoneClientLoadCubeItems l)
    {
        // Les produits sont déjà dans le sac (274/275) — les vecteurs
        // pointent les (bag,slot) → icône dans Complete01-08.
        var i = 1;
        foreach (var (bag, slot) in l.ProdBags.Zip(l.ProdSlots))
        {
            var cell = Cell(_window!, $"Complete{i:D2}");
            var it = PlayerBag.Items.FirstOrDefault(x =>
                x.Bag == bag && x.Slot == slot);
            if (cell != null && it != null)
                cell.SetItemId(it.ItemId, it.Amount);
            i++;
        }
    }

    // ------------------------- réseau -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[cube] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[cube] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------- fenêtre -------------------------

    public void Open()
    {
        EnsureTables();
        if (_window == null) BuildWindow();
        _open = true;
        _window!.Visible = true;
        Visible = true;
        if (_going != null) _going.Text = "";
    }

    private void Close()
    {
        if (_open)
        {
            _open = false;
            Send(new ZoneServerStrengthenClose { Value = 28 });
        }
        if (_window != null) _window.Visible = false;
        Visible = false;
    }

    private void BuildWindow()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("newmagiccubewnd.layout");
        if (_window == null) { BuildFallback(); return; }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(520, 420));
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Magic Cube"));
        DragHelper.Persist(_window, "magiccube");
        _window.VisibilityChanged += () =>
        { if (!_window.Visible && _open) Close(); };

        // Grille d'items placés : drop sac → mémorise (bag,slot).
        for (var i = 1; i <= 16; i++)
        {
            var cell = Cell(_window, $"StuffSlot{i:D2}");
            if (cell == null) continue;
            var idx = _placed.Count;
            _placed.Add((cell, -1, -1, 0));
            cell.PayloadKind = "cube";
            cell.AcceptDrop = StorageItemCell.IsBagPayload;
            var n = idx;
            cell.ItemDropped += (_, d) =>
            {
                _placed[n] = (cell,
                    (short)d["bag"].AsInt32(),
                    (short)d["slot"].AsInt32(),
                    d["itemId"].AsInt32());
                if (_recipe == null) MatchRecipe(d["itemId"].AsInt32());
            };
            cell.RightClicked += _ =>
            {
                _placed[n] = (cell, -1, -1, 0);
                cell.Clear();
            };
        }
        // Sac de chance.
        _lucky = Cell(_window, "SpecialSlot");
        if (_lucky != null)
        {
            _lucky.PayloadKind = "cubelucky";
            _lucky.AcceptDrop = StorageItemCell.IsBagPayload;
        }
        _going = FindLeaf<Label>(_window, "Going");
        var start = FindLeaf<BaseButton>(_window, "StartButton");
        if (start != null) start.Pressed += Start;
        var clear = FindLeaf<BaseButton>(_window, "ClearButton");
        if (clear != null) clear.Pressed += ClearPlaced;
        var list = FindLeaf<BaseButton>(_window, "CompleteListButton");
        if (list != null) list.Pressed += ShowRecipes;
        var close = FindLeaf<BaseButton>(_window, "CloseButton");
        if (close != null) close.Pressed += Close;
        ResetPlaceholders();
    }

    /// <summary>Fenêtre neuve = neutre : valeurs de maquette du layout
    /// (x 25, 99 %, 9999, xLoading...) retirées ; libellés en français.</summary>
    private void ResetPlaceholders()
    {
        if (_window == null) return;
        foreach (var n in new[] { "Probability", "Gold", "Silver",
            "MainMaterial", "SecondaryMaterial", "numtext" })
            SetAll<Label>(n, "");
        SetAll<RichTextLabel>("numtext", "");
        // Pastilles de quantité (flèches grises) + écran de chargement.
        foreach (var n in _window.FindChildren("*", "Control", true,
                     false).OfType<Control>())
        {
            var name = n.Name.ToString();
            if (name.EndsWith("countnum1") || name.EndsWith("countnum2")
                || name.EndsWith("countnum3") || name.EndsWith("countnum4")
                || name.EndsWith("countnum5") || name.EndsWith("Message")
                || name.EndsWith("LoadingText"))
                n.Visible = false;
        }
        SetAll<Label>("Text1", "Aperçu des formules");
        SetAll<Label>("Text2", "Zone des matériaux");
        SetAll<Label>("Text3", "Contenu de la formule");
        SetAll<Label>("SpecialItem", "Objet spécial");
        SetAll<Label>("Going", "");
        SetAll<Label>("Times", "fois");
        SetAll<Label>("Complete", "Produits");
        SetBtn("StartButton", "Lancer");
        SetBtn("ClearButton", "Vider le cube");
        SetBtn("CloseButton", "Fermer");
        SetBtn("NeedItemListButton", "Voir");
        SetBtn("CompleteListButton", "Voir");
    }

    private void SetAll<T>(string leaf, string text) where T : Control
    {
        foreach (var n in _window!.FindChildren("*", "Control", true,
                     false))
            if (n.Name.ToString().EndsWith(leaf))
            {
                if (n is Label l) l.Text = text;
                else if (n is RichTextLabel r) r.Text = text;
            }
    }

    private void SetBtn(string leaf, string text)
    {
        if (FindLeaf<Button>(_window!, leaf) is { } b) b.Text = text;
    }

    private void BuildFallback()
    {
        var p = new Panel
        { CustomMinimumSize = new Vector2(400, 260) };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        var l = new Label
        { Text = "newmagiccubewnd.layout introuvable" };
        p.AddChild(l);
        var c = new Button
        { Text = UiTextEn.Translate("Close"), Position = new(10, 220) };
        c.Pressed += Close;
        p.AddChild(c);
        _window = p;
        AddChild(p);
    }

    // ------------------------- recettes -------------------------

    private void MatchRecipe(int itemId)
    {
        _recipe = _recipes.FirstOrDefault(r =>
            r.ItemId == itemId && r.RequiredLevel is > 0 and < 999);
        RenderRecipe();
    }

    private void ShowRecipes()
    {
        if (_recipeWnd == null)
        {
            _recipeWnd = HudExtensions.FindHud()
                ?.Build("newmagiccuberecipewnd.layout");
            if (_recipeWnd == null) return;
            HudExtensions.CenterNative(_recipeWnd,
                new Vector2(340, 300));
            _window?.AddChild(_recipeWnd);
            DragHelper.EnsureTitleBar(_recipeWnd,
                UiTextEn.Translate("Cube recipes"));
            var border = FindLeaf<Control>(_recipeWnd, "Border");
            if (border != null)
            {
                _recipeList = new ItemList
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                };
                _recipeList.SetAnchorsAndOffsetsPreset(
                    LayoutPreset.FullRect);
                border.AddChild(_recipeList);
                _recipeList.ItemActivated += i => PickRecipe((int)i);
            }
        }
        _recipeList?.Clear();
        foreach (var r in _recipes)
        {
            if (r.RequiredLevel is <= 0 or >= 999) continue;
            _recipeList?.AddItem(
                $"{r.Name} → {r.ProductName} ({r.SuccessRate}%)");
        }
        _recipeWnd.Visible = true;
    }

    private void PickRecipe(int i)
    {
        var list = _recipes.Where(r =>
            r.RequiredLevel is > 0 and < 999).ToList();
        if (i < 0 || i >= list.Count) return;
        _recipe = list[i];
        RenderRecipe();
        if (_recipeWnd != null) _recipeWnd.Visible = false;
    }

    private void RenderRecipe()
    {
        var r = _recipe;
        if (_window == null || r == null) return;
        SetText("MainMaterial",
            $"{ItemName(r.ItemId)} ×{r.MainAmount}");
        SetText("SecondaryMaterial", string.Join(", ",
            r.Subs.Select(s => $"{ItemName(s.Id)} ×{s.Qty}")));
        SetText("Probability", $"{r.SuccessRate}%");
        SetText("Gold", $"{r.Gold}");
        Cell(_window, "Main01")?.SetItemId(r.ItemId, r.MainAmount);
        for (var i = 0; i < 4; i++)
        {
            var c = Cell(_window, $"Second{i + 1:D2}");
            if (i < r.Subs.Count) c?.SetItemId(r.Subs[i].Id, r.Subs[i].Qty);
            else c?.Clear();
        }
        // Produit : clé directe <100000 = item ; groupe = 1er id du
        // set applicable ; 0 = 1er auxiliaire.
        var prod = r.ProductKey is > 0 and < 100000 ? r.ProductKey
            : r.ProductKey == 0 && r.Subs.Count > 0 ? r.Subs[0].Id : 0;
        Cell(_window, "Complete01")
            ?.SetItemId(prod, Math.Max(r.ProductAmount, 1));
    }

    private void Start()
    {
        if (_recipe == null) return;
        var bags = _placed.Where(p => p.Bag >= 0)
            .Select(p => p.Bag).ToArray();
        var slots = _placed.Where(p => p.Bag >= 0)
            .Select(p => p.Slot).ToArray();
        short lb = -1, ls = -1;
        if (_lucky?.Item is { } li)
        { lb = (short)li.Bag; ls = (short)li.Slot; }
        Send(new ZoneServerVerifyCubeCosts
        {
            RecipeId = _recipe.Id,
            Bags = bags, Slots = slots,
            LuckyBag = lb, LuckySlot = ls,
        });
        if (_going != null) _going.Text = "";
    }

    private void ClearPlaced()
    {
        for (var i = 0; i < _placed.Count; i++)
        {
            _placed[i].Cell.Clear();
            _placed[i] = (_placed[i].Cell, -1, -1, 0);
        }
        _lucky?.Clear();
    }

    // ------------------------- tables -------------------------

    private void EnsureTables()
    {
        if (_recipes.Count > 0) return;
        var ip = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        if (ip != null)
            foreach (var (id, def) in ItemTable.Load(ip))
                _items[id] = def;
        var mp = AssetCatalog.ResolveRawAsset("data/db/c_itemmall.ini");
        if (mp != null)
            foreach (var (id, def) in ItemTable.Load(mp))
                _items.TryAdd(id, def);
        var cp = AssetCatalog.ResolveRawAsset("data/db/c_itemchest.ini");
        if (cp == null) return;
        foreach (var row in IniTable.ParseFile(cp, EdenEncoding.Big5)
                .Rows)
        {
            if (row.Length < 25 || !int.TryParse(row[0], out var id))
                continue;
            var r = new Recipe
            {
                Id = id,
                ItemId = Int(row, 1),
                Name = IniTable.GetStr(row, 4),
                MainAmount = Int(row, 6),
                Gold = Int(row, 19),
                SuccessRate = Int(row, 20),
                ProductKey = Int(row, 21),
                RequiredLevel = Int(row, 22),
                ProductName = IniTable.GetStr(row, 23),
                ProductAmount = Int(row, 24),
            };
            for (var i = 0; i < 4; i++)
            {
                var sub = Int(row, 7 + i * 3);
                if (sub != 0)
                    r.Subs.Add((sub, Int(row, 9 + i * 3)));
            }
            _recipes.Add(r);
        }
        GD.Print($"[verify] cube : {_recipes.Count} recettes " +
            "c_itemchest chargées");
    }

    private static int Int(string[] row, int i) =>
        i < row.Length && int.TryParse(row[i], out var v) ? v : 0;

    private string ItemName(int id) =>
        _items.TryGetValue(id, out var d) && d.Name.Length > 0
            ? d.Name : $"#{id}";

    private void SetText(string leaf, string text)
    {
        if (FindLeaf<Label>(_window!, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window!, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Slot natif par suffixe de feuille CEGUI
    /// (« StuffSlot01 » → « Root_NewMagicCubeWnd_…_StuffSlot01 »).</summary>
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
