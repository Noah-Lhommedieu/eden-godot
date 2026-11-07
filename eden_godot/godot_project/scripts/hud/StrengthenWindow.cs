using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre d'enclume / renforcement — layout natif
/// <c>strengthenwnd_na.layout</c> (variante Aeria : 3 slots en
/// bandeau + MCL des paliers + grand %, lot B4 du plan craft).
///
/// Wire (preuves : désasm DoStrengthenItem @0x80a2b00, porté dans
/// <c>eden_server/ZoneEntryFlow.Strengthen.cs</c>) :
///   - Ouverture : PlayerAction 13 (utiliser un parchemin) ou
///     dialogue NPC type 8 → S→C <b>432</b> ClientEnterStrengthen
///     <c>[i32 npcUid][s16 9][u16][s16 -1][s16]</c>. Field2 = type
///     de fenêtre : 9 = renfort (10 combine, 0x68 métier, 0x1c cube,
///     0x1f boîte magique — autres fenêtres).
///   - <b>119</b> C→S StrengthenItem <c>[s16 0][i32 combineId]
///     [s16 sacCible][s16 slotCible][s16 sacPierre][s16 slotPierre]
///     [s16 sacChance][s16 slotChance][s16 rapide]</c> — combineId =
///     recette S_FamilyCombine 1-10 (armes) / 11-20 (armures),
///     résolu depuis la pierre déposée (liste SStuff col18+).
///   - <b>433</b> S→C cast (2 s rapide / 4 s — barre CraftWindow).
///   - <b>434</b> S→C cancel <c>[s16 9]</c> — cast interrompu.
///   - <b>435</b> S→C résultat <c>[s16 9][s16 r][4×0]</c> :
///     0 = échec+destruction, 1 = succès, 2 = échec protégé.
///   - <b>436</b> S→C niveau <c>[s16 contType][s16 slot][s16 niveau]
///     [s16 contIdx][s16 prot]</c> → PlayerBag + rafraîchissement.
///   - <b>120</b> C→S StrengthenClose <c>[s16 9]</c> à la fermeture.
///
/// Taux affiché (ChanceText, même formule que le serveur) :
///   niveau 0 → 100 % ; 1-9 → taux recette / 2^niveau ;
///   10-13 → S_Parameter 517+lvl%10 (armes) / 521+lvl%10 (armures).
///   La pitié (failCount × EquipAddition) est côté serveur/mission —
///   non affichée (WarningText garde le texte natif).
/// Coût : repairPrice × 0.1 + 10 (S_Item col76).
///
/// Slots natifs : <c>ItemBorder/Item</c> (équipement),
/// <c>ItemBorder1/Scroll</c> (parchemin kind 46 / EquipLoc 46),
/// <c>ItemBorder11/ItemMall</c> (item de chance kind 8002 — aucun
/// dans les données EDENFR, le serveur les rejette tous).
/// </summary>
public partial class StrengthenWindow : Control
{
    private Control? _window;
    private ItemSlotCell? _item, _scroll, _chance;
    private MclList? _levels;

    // Sélections déposées (bag/slot dans l'inventaire).
    private int _tBag = -1, _tSlot = -1, _tItem;
    private int _mBag = -1, _mSlot = -1, _mItem;
    private int _cBag = -1, _cSlot = -1;
    private int _combineId = -1;
    private bool _opOpen, _casting;

    // Forge auto (strengthenfactory.layout) : répète le 119 jusqu'au
    // palier demandé ou à l'épuisement des pierres.
    private Control? _factory;
    private LineEdit? _timesIn, _levelIn;
    private int _autoTimes, _autoLevel;

    private static Dictionary<int, CraftTable.CombineRecipe>? _combine;
    private static Dictionary<int, ItemTable.ItemDef>? _items;
    private static Dictionary<int, float>? _params;

    private bool _native, _building;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        EnsureWindow();
        Visible = false;
        MiscPackets.Received += OnPacket;
        PlayerBag.Changed += OnBagChanged;
    }

    /// <summary>Le ctx uidata n'est pas prêt au _Ready du game.tscn —
    /// on retente le layout natif à chaque ouverture tant que la
    /// fenêtre affichée est le repli code.</summary>
    private void EnsureWindow()
    {
        if (_native) return;
        _building = true;
        var hud = HudExtensions.FindHud();
        var w = hud?.Build("strengthenwnd_na.layout");
        if (w == null)
        {
            if (_window == null) BuildFallback();
            _building = false;
            return;
        }
        _window?.QueueFree();
        _window = w;
        _native = true;
        AddChild(_window);
        HudExtensions.CenterNative(_window, new Vector2(450, 570));
        BindNative(_window);
        _window.Visible = false;
        // Forge auto (strengthenfactory.layout) — même problème de
        // timing que la fenêtre : construite ici, pas au _Ready.
        _factory ??= hud?.Build("strengthenfactory.layout");
        if (_factory != null)
        {
            AddChild(_factory);
            HudExtensions.CenterNative(_factory,
                new Vector2(450, 570));
            _timesIn = FindLeaf<LineEdit>(_factory, "TimesInput");
            _levelIn = FindLeaf<LineEdit>(_factory, "LevelInput");
            var start = FindLeaf<BaseButton>(_factory, "StartBtn");
            if (start != null) start.Pressed += StartFactory;
            var cancel = FindLeaf<BaseButton>(_factory, "CancelBtn");
            if (cancel != null) cancel.Pressed += () =>
                _factory.Visible = false;
            _factory.Visible = false;
        }
        _building = false;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        PlayerBag.Changed -= OnBagChanged;
        base._ExitTree();
    }

    /// <summary>Montre la fenêtre hors session (capture --hud-open).
    /// Sans op serveur → la fermeture n'émet pas le 120.</summary>
    public void ShowDemo()
    {
        EnsureWindow();
        if (_window != null) { _window.Visible = true; HudWindows.Front(_window); }
        Visible = true;
    }

    // ------------------------- paquets -------------------------

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterStrengthen e when e.Field2 == 9:
                _opOpen = true;
                EnsureWindow();
                EnsureTables();
                FillLevels();
                if (_window != null) { _window.Visible = true; HudWindows.Front(_window); }
                Visible = true;
                GD.Print("[verify] strengthen : 432 op=9 — enclume");
                break;
            case ZoneClientStrengthenItemCancel c when c.Value == 9:
                _casting = false;
                _autoTimes = 0;
                break;
            case ZoneClientStrengthenItemResult r when r.Field1 == 9:
                OnResult(r.Field2);
                break;
            case ZoneClientModifyItemStrengthen u:
                // [contType][slot][niveau][contIdx][prot]
                if (u.Field1 == 0)
                    PlayerBag.ApplyStrengthenData(u.Field4, u.Field2,
                        u.Field3);
                if (u.Field4 == _tBag && u.Field2 == _tSlot)
                    RefreshState();
                break;
        }
    }

    /// <summary>435 [9][résultat] — succès / échec (destruction) /
    /// échec protégé.</summary>
    private void OnResult(int result)
    {
        _casting = false;
        var hud = GetParent() as HudRoot;
        switch (result)
        {
            case 1:
                hud?.AddChat(7, "",
                    UiTextEn.Translate("Fortification succeeded."));
                break;
            case 2:
                hud?.AddChat(7, "",
                    UiTextEn.Translate(
                        "Fortification failed (protected)."));
                break;
            default:
                hud?.AddChat(7, "",
                    UiTextEn.Translate(
                        "Fortification failed — the item was destroyed."));
                // Cible détruite : le 275 vide le sac ; le slot UI
                // se rafraîchit via PlayerBag.Changed.
                break;
        }
        if (result != 1) _autoTimes = 0;   // échec → forge auto stop
        RefreshState();
        MaybeContinueFactory();
    }

    // ------------------------- envois -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[strengthen] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[strengthen] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>StrengthenBtn → 119. rapide = 0 (cast 4 s natif).</summary>
    private void OnStrengthen()
    {
        if (_casting || _tBag < 0 || _mBag < 0 || _combineId <= 0)
            return;
        _casting = true;
        Send(new ZoneServerStrengthenItem
        {
            Field1 = 0,
            Field2 = _combineId,
            Field3 = (short)_tBag,
            Field4 = (short)_tSlot,
            Field5 = (short)_mBag,
            Field6 = (short)_mSlot,
            Field7 = (short)_cBag,
            Field8 = (short)_cSlot,
            Field9 = 0,
        });
    }

    private void OnVisibilityChanged()
    {
        // Visible=false pendant la construction ne compte pas ; et
        // c'est la fenêtre native qui compte (this reste visible).
        if (_building || !_opOpen) return;
        if (_window == null || _window.Visible) return;
        _opOpen = false;
        Send(new ZoneServerStrengthenClose { Value = 9 });
    }

    // ------------------------- UI native -------------------------

    private void BindNative(Control w)
    {
        DragHelper.EnsureTitleBar(w, UiTextEn.Translate("Strengthen"));
        DragHelper.Persist(w, "strengthen");
        w.VisibilityChanged += OnVisibilityChanged;
        _item = ItemSlotCell.Find(w, "Root/Strengthen/ItemBorder/Item");
        _scroll = ItemSlotCell.Find(w,
            "Root/Strengthen/ItemBorder1/Scroll");
        _chance = ItemSlotCell.Find(w,
            "Root/Strengthen/ItemBorder11/ItemMall");
        _levels = FindLeaf<MclList>(w, "StrengthenLevel");
        if (_item != null) _item.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            _tBag = d["bag"].AsInt32();
            _tSlot = d["slot"].AsInt32();
            _tItem = d.ContainsKey("itemId")
                ? d["itemId"].AsInt32() : 0;
            var it = FindBag(_tBag, _tSlot);
            if (it != null) _item.SetItem(it);
            RefreshState();
        };
        if (_scroll != null) _scroll.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            _mBag = d["bag"].AsInt32();
            _mSlot = d["slot"].AsInt32();
            _mItem = d.ContainsKey("itemId")
                ? d["itemId"].AsInt32() : 0;
            var it = FindBag(_mBag, _mSlot);
            if (it != null) _scroll.SetItem(it);
            RefreshState();
        };
        if (_chance != null) _chance.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            _cBag = d["bag"].AsInt32();
            _cSlot = d["slot"].AsInt32();
            var it = FindBag(_cBag, _cSlot);
            if (it != null) _chance.SetItem(it);
            RefreshState();
        };
        var btn = FindLeaf<BaseButton>(w, "StrengthenBtn");
        if (btn != null) btn.Pressed += OnStrengthen;
        // Boutons sans Text dans le layout natif : libellés posés ici.
        LabelButton(w, "StrengthenBtn", "Strengthen");
        LabelButton(w, "CloseBtn", "Close");
        LabelButton(w, "StrengthenFactory", "Strengthen workshop");
        var factory = FindLeaf<BaseButton>(w, "StrengthenFactory");
        if (factory != null && _factory != null)
            factory.Pressed += () => _factory.Visible = true;
        else if (factory != null)
            factory.Visible = false;
    }

    /// <summary>Slot déposé toujours présent dans le sac ? Sinon on
    /// le vide (item déplacé/consommé/détruit).</summary>
    private void OnBagChanged()
    {
        if (_tBag >= 0 && FindBag(_tBag, _tSlot) == null)
        { _tBag = _tSlot = -1; _item?.Clear(); }
        if (_mBag >= 0 && FindBag(_mBag, _mSlot) == null)
        { _mBag = _mSlot = -1; _scroll?.Clear(); }
        if (_cBag >= 0 && FindBag(_cBag, _cSlot) == null)
        { _cBag = _cSlot = -1; _chance?.Clear(); }
        RefreshState();
    }

    private static InventorySlot? FindBag(int bag, int slot)
    {
        foreach (var it in PlayerBag.Items)
            if (it.Bag == bag && it.Slot == slot) return it;
        return null;
    }

    /// <summary>Recalcule combineId, taux et coût à chaque changement
    /// de slot / niveau.</summary>
    private void RefreshState()
    {
        _combineId = ResolveCombineId();
        var def = _items != null &&
            _items.TryGetValue(_tItem, out var d) ? d : null;
        var level = FindBag(_tBag, _tSlot)?.Strengthen ?? 0;

        // Taux : formule du serveur (sans pitié — inconnue client).
        var rate = ComputeRate(level, def?.Kind ?? 0);
        SetText("ChanceText",
            rate < 0 ? "--%" : $"{Math.Min(rate, 100f):0.0}%");

        // Coût = repairPrice × 0.1 + 10 (or+argent, convention cuivre).
        var cost = def != null ? (int)(def.RepairPrice * 0.1 + 10) : 0;
        SetText("GoldText",
            cost / 1000 > 0 ? $"{cost / 1000}" : "0");
        SetText("SilverText", $"{cost % 1000}");

        // Éligibilité : pierre ↔ recette + classe d'item.
        var scrollOk = _mBag < 0 || _combineId > 0;
        var warn = "";
        if (_mBag >= 0 && _combineId <= 0)
            warn = UiTextEn.Translate(
                "This scroll cannot be used on this item.");
        else if (level >= 14)
            warn = UiTextEn.Translate("Maximum level reached.");
        SetText("WarningText", warn);
        var btn = FindLeaf<BaseButton>(_window!, "StrengthenBtn");
        if (btn != null)
            btn.Disabled = !scrollOk || _tBag < 0 || level >= 14
                || _casting;
    }

    /// <summary>Pierre → recette S_FamilyCombine 1-20, filtrée par la
    /// classe de la cible (1-10 armes/bouclier, 11-20 armures).</summary>
    private int ResolveCombineId()
    {
        if (_mItem <= 0 || _combine == null) return -1;
        var def = _items != null &&
            _items.TryGetValue(_tItem, out var d) ? d : null;
        var weaponClass = _tBag < 0 || def == null ||
            def.Kind is >= 0x11 and <= 0x21 or 0x22 or 34;
        foreach (var r in _combine.Values)
        {
            if (r.Id is < 1 or > 20) continue;
            if (weaponClass != r.Id <= 10) continue;
            foreach (var m in r.Materials)
                if (m.ItemId == _mItem) return r.Id;
        }
        return -1;
    }

    /// <summary>Taux de réussite natif — miroir de DoStrengthenItem
    /// (level 0 → 100 ; 1-9 → recette/2^lvl ; 10-13 → S_Parameter).
    /// Sans la pitié (compteur de fails non communiqué).</summary>
    private float ComputeRate(int level, int kind)
    {
        if (level == 0) return 100f;
        if (_combineId <= 0) return -1;
        var recipe = _combine!.GetValueOrDefault(_combineId);
        if (recipe == null) return -1;
        if (level is >= 10 and <= 13)
        {
            var weapon = kind is >= 0x11 and <= 0x21;
            var paramId = weapon ? 517 + level % 10
                : kind < 0x11 || kind == 0x22 ? 521 + level % 10 : -1;
            return paramId >= 0 &&
                _params != null && _params.TryGetValue(paramId,
                    out var p) ? p : -1;
        }
        return recipe.SuccessRate / (1 << level);
    }

    /// <summary>MCL « paliers » : 14 lignes (col 0 = palier). Les
    /// stats par palier ne sont pas dans les données localisées —
    /// gap EDENFR noté ; la ligne courante est mise en valeur.</summary>
    private void FillLevels()
    {
        if (_levels == null) return;
        _levels.ClearRows();
        var level = FindBag(_tBag, _tSlot)?.Strengthen ?? 0;
        for (var i = 1; i <= 14; i++)
        {
            var row = _levels.AddRow(new[] { $"+{i}", "", "", "", "", "" });
            if (i == level + 1)
                _levels.SetCellColor(row, 0,
                    new Color(1f, 0.85f, 0.4f));
        }
    }

    // ------------------------- forge auto -------------------------

    /// <summary>StartBtn du dialogue forge : N tentatives ou jusqu'au
    /// palier demandé — chaîne un 119 après chaque 435 succès.</summary>
    private void StartFactory()
    {
        _autoTimes = int.TryParse(_timesIn?.Text, out var t)
            ? Math.Clamp(t, 1, 999) : 1;
        _autoLevel = int.TryParse(_levelIn?.Text, out var l)
            ? Math.Clamp(l, 1, 14) : 14;
        if (_factory != null) _factory.Visible = false;
        MaybeContinueFactory();
    }

    /// <summary>Relance un 119 si la forge auto est armée et qu'il
    /// reste des pierres / du niveau à gagner.</summary>
    private void MaybeContinueFactory()
    {
        if (_autoTimes <= 0) return;
        var level = FindBag(_tBag, _tSlot)?.Strengthen ?? 0;
        var scrollLeft = FindBag(_mBag, _mSlot)?.Amount ?? 0;
        if (level >= _autoLevel || scrollLeft <= 0)
        {
            _autoTimes = 0;
            return;
        }
        _autoTimes--;
        OnStrengthen();
    }

    // ------------------------- tables / helpers -------------------------

    private static void EnsureTables()
    {
        if (_combine != null) return;
        var p = AssetCatalog.ResolveRawAsset("data/db/c_familycombine.ini");
        _combine = p != null
            ? CraftTable.LoadCombine(p) : new();
        var ip = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        _items = ip != null
            ? ItemTable.Load(ip) : new();
        // Parchemins de fortif (40002+) : table mall, fusionnée.
        var mp = AssetCatalog.ResolveRawAsset("data/db/c_itemmall.ini");
        if (mp != null)
            foreach (var (id, def) in ItemTable.Load(mp))
                _items.TryAdd(id, def);
        _params = LoadParams();
    }

    /// <summary>c_parameter.ini <c>id|valeur|desc|</c> — seuls les
    /// taux 517-524 (fortif +11..+14) nous intéressent.</summary>
    private static Dictionary<int, float> LoadParams()
    {
        var dict = new Dictionary<int, float>();
        try
        {
            foreach (var line in Vfs.EdenVfs.ReadLines(
                         "data/db/c_parameter.ini", EdenEncoding.Big5))
            {
                var c = line.Split('|');
                if (c.Length < 2 || !int.TryParse(c[0], out var id)
                    || id is < 517 or > 524) continue;
                if (float.TryParse(c[1],
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var v))
                    dict[id] = v;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[strengthen] c_parameter : {e.Message}");
        }
        return dict;
    }

    private static T? FindLeaf<T>(Node root, string leaf) where T : class
    {
        foreach (var n in root.FindChildren("*", typeof(T).Name,
                     true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as T;
        return null;
    }

    private static void LabelButton(Control w, string leaf, string en)
    {
        if (FindLeaf<Button>(w, leaf) is { } b)
            b.Text = UiTextEn.Translate(en);
    }

    private void SetText(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Repli sans uidata : panneau minimal (slots code).</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "StrengthenWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-224, -282);
        p.Size = new Vector2(448, 564);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        p.VisibilityChanged += OnVisibilityChanged;
        DragHelper.EnsureTitleBar(p, UiTextEn.Translate("Strengthen"));
        DragHelper.Persist(p, "strengthen");
        var v = new VBoxContainer { Name = "__v" };
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetTop = 30;
        p.AddChild(v);
        _item = new ItemSlotCell();
        _scroll = new ItemSlotCell();
        _chance = new ItemSlotCell();
        var row = new HBoxContainer();
        row.AddChild(_item);
        row.AddChild(_scroll);
        row.AddChild(_chance);
        v.AddChild(row);
        _item.ItemDropped += (_, d) =>
        {
            _tBag = d["bag"].AsInt32();
            _tSlot = d["slot"].AsInt32();
            _tItem = d["itemId"].AsInt32();
            var it = FindBag(_tBag, _tSlot);
            if (it != null) _item.SetItem(it);
            RefreshState();
        };
        _scroll.ItemDropped += (_, d) =>
        {
            _mBag = d["bag"].AsInt32();
            _mSlot = d["slot"].AsInt32();
            _mItem = d["itemId"].AsInt32();
            var it = FindBag(_mBag, _mSlot);
            if (it != null) _scroll.SetItem(it);
            RefreshState();
        };
        _chance.ItemDropped += (_, d) =>
        {
            _cBag = d["bag"].AsInt32();
            _cSlot = d["slot"].AsInt32();
            var it = FindBag(_cBag, _cSlot);
            if (it != null) _chance.SetItem(it);
            RefreshState();
        };
        var btn = new Button
        {
            Name = "StrengthenBtn",
            Text = UiTextEn.Translate("Strengthen"),
        };
        btn.Pressed += OnStrengthen;
        v.AddChild(btn);
    }
}
