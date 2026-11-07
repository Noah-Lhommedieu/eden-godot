using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.Vfs;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre métier racial (LifeSkill) — lots B5 du plan craft.
/// Deux layouts natifs selon le groupe de métier (543 Field1) :
///   - groupe 1 (joaillerie/analyse, Humain) →
///     <c>jewelrystudy.layout</c> (zone Study : slot cible +
///     StudyBtn = 214 op0 avec sac/slot ; zone Create comme les
///     autres groupes) ;
///   - groupes 2-5 (Zumi/Anuran/Ursun/Halfkin) →
///     <c>engineeringstudy.layout</c> (liste ScrollPane de lignes
///     <c>lifeskillitem.layout</c> : icône, nom, ExpBar, ExpText,
///     FullExpText « 已熟練 », ForgetBtn « 遺忘 », LvUpBtn « 領悟 »).
///
/// Wire (preuves : <c>eden_server/ZoneEntryFlow.LifeSkill.cs</c>,
/// désasm CLifeSkillFactory) :
///   - Ouverture : PlayerAction 16 (icône LS du sac) ou dialogue →
///     432 ClientEnterStrengthen <c>[uid][s16 0x68][…]</c> ; à
///     l'ouverture le client envoie <b>213</b> QueryLifeSkill →
///     543 liste apprise (déjà routée vers PlayerProfileState).
///   - <b>214</b> C→S <c>[u8 op][u16 recipeId][s16 sac][s16 slot]</c> :
///     0 = analyse (joaillerie, sac+slot), 1/2 = production
///     (2 = avec gain d'exp), 3 = oubli, 4 = apperception
///     (maîtrise → recette produit aléatoire), 5 = level-up
///     (maîtrise → recette de base du palier suivant).
///   - <b>544</b> S→C <c>[u8 op][u16 id][u32 val]</c> : 0 = exp,
///     1 = apprise, 2 = oubliée, 3 = remplacée → état local patché.
///   - 433 cast 5 s (0x68 production / 0x69 analyse), 434 cancel
///     [0x68], 435 résultat [0x68][r], 316 textes d'erreur natifs.
///   - Fermeture → <b>120</b> StrengthenClose <c>[s16 22]</c>
///     (op interne métier, pas le code fenêtre 0x68).
///
/// Schéma recette <c>c_lifeskill.ini</c> (miroir LifeSkillParser
/// serveur) : id|groupe|itemId|nom|qté|nivReq|expBase|expVar|
/// expCap|or|nextId|prevId|≤3 triplets matériaux (id,nom,qté) dès
/// col12|triplets résultats (id,nom,poids) dès col21.
/// </summary>
public partial class LifeSkillWindow : Control
{
    private Control? _window;        // engineeringstudy ou jewelrystudy
    private Control? _rowProto;      // lifeskillitem.layout
    private VBoxContainer? _rows;
    private int _selected = -1;      // recipeId sélectionnée
    private int _group;
    private bool _opOpen;
    private readonly Dictionary<int, (int Exp, int Due)> _learned =
        new();
    // Analyse joaillerie : slot cible déposé.
    private int _aBag = -1, _aSlot = -1, _aItem;

    private static Dictionary<int, Recipe>? _recipes;
    private static Dictionary<int, ItemTable.ItemDef>? _items;

    /// <summary>Recette c_lifeskill (subset pour l'UI).</summary>
    private sealed class Recipe
    {
        public int Id, Group, ItemId, Count, ReqLevel, ExpCap, Gold,
            NextId, PrevId;
        public string Name = "";
        public readonly List<(int ItemId, int Qty)> Materials = new();
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var hud = HudExtensions.FindHud();
        _rowProto = hud?.Build("lifeskillitem.layout");
        Visible = false;
        MiscPackets.Received += OnPacket;
        PlayerProfileState.Changed += OnProfileChanged;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        PlayerProfileState.Changed -= OnProfileChanged;
        base._ExitTree();
    }

    /// <summary>543 reçu après l'ouverture → resynchronise.</summary>
    private void OnProfileChanged(string section)
    {
        if (section != "lifeskill" || _window == null) return;
        SyncLearned();
        CallDeferred(nameof(FillRows));   // 543 arrive du thread réseau
    }

    // ------------------------- paquets -------------------------

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterStrengthen e when e.Field2 == 0x68:
                _opOpen = true;
                EnsureTables();
                SyncLearned();
                BuildWindow();
                Send(new ZoneServerQueryLifeSkill());
                GD.Print("[verify] lifeskill : 432 op=0x68 — métier " +
                         $"groupe {_group}");
                break;
            case ZoneClientLifeSkillUpdate u:
                ApplyUpdate(u);
                break;
            case ZoneClientStrengthenItemResult r when r.Field1 == 0x68:
                if (r.Field2 == 1)
                    (GetParent() as HudRoot)?.AddChat(7, "",
                        UiTextEn.Translate("Production done."));
                break;
            case ZoneClientStrengthenItemCancel c
                when c.Value == 0x68:
                break;   // cast annulé — la barre se ferme seule
        }
    }

    /// <summary>544 — op 0 exp / 1 apprise / 2 oubliée /
    /// 3 remplacée.</summary>
    private void ApplyUpdate(ZoneClientLifeSkillUpdate u)
    {
        switch (u.Field1)
        {
            case 0:
                if (_learned.ContainsKey(u.Field2))
                    _learned[u.Field2] = ((int)u.Field3,
                        _learned[u.Field2].Due);
                break;
            case 1:
                _learned[u.Field2] = (0, 0);
                break;
            case 2:
                _learned.Remove(u.Field2);
                if (_selected == u.Field2) _selected = -1;
                break;
            case 3:
                _learned.Remove(u.Field2);
                _learned[(int)u.Field3] = (0, 0);
                break;
        }
        CallDeferred(nameof(FillRows));   // 544 arrive du thread réseau
    }

    /// <summary>543 déjà routé vers PlayerProfileState — recopie la
    /// liste apprise dans l'état local.</summary>
    private void SyncLearned()
    {
        _group = PlayerProfileState.LifeSkillField;
        _learned.Clear();
        var ids = PlayerProfileState.LifeSkillRecipes;
        var exps = PlayerProfileState.LifeSkillExps;
        var dues = PlayerProfileState.LifeSkillDues;
        for (var i = 0; i < ids.Count; i++)
            _learned[ids[i]] = (
                i < exps.Count ? exps[i] : 0,
                i < dues.Count ? dues[i] : 0);
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[lifeskill] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[lifeskill] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>214 — op + recette (+ sac/slot pour l'analyse).</summary>
    private void SendOp(byte op, int recipeId, int bag = -1,
        int slot = -1) =>
        Send(new ZoneServerLifeSkill
        {
            Field1 = op, Field2 = (ushort)recipeId,
            Field3 = (short)bag, Field4 = (short)slot,
        });

    // ------------------------- fenêtre -------------------------

    /// <summary>Construit la fenêtre adaptée au groupe (groupe 1 =
    /// joaillerie avec zone d'analyse, autres = production).</summary>
    /// <summary>Montre la fenêtre hors session (capture --hud-open) —
    /// groupe ingénierie par défaut. Sans op serveur la fermeture
    /// n'émet pas le 120.</summary>
    public void ShowDemo() { _group = 0; BuildWindow(); }

    private void BuildWindow()
    {
        _window?.QueueFree();
        var hud = HudExtensions.FindHud();
        // Le proto de ligne ne peut pas être construit dans _Ready :
        // le nœud vit dans game.tscn, le contexte CEGUI n'existe pas
        // encore à ce moment (hud.Build → null en silence).
        _rowProto ??= hud?.Build("lifeskillitem.layout");
        var file = _group == 1 ? "jewelrystudy.layout"
            : "engineeringstudy.layout";
        _window = hud?.Build(file);
        if (_window == null) { BuildFallback(); return; }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(420, 650));
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Racial craft"));
        DragHelper.Persist(_window, "lifeskill");
        _window.VisibilityChanged += OnWindowHidden;
        var scroll = FindLeaf<ScrollContainer>(_window, "ScrollPane");
        _rows = new VBoxContainer { Name = "__rows" };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        (scroll ?? (Control)_window).AddChild(_rows);
        var create = FindLeaf<BaseButton>(_window, "CreateBtn");
        if (create != null) create.Pressed += () =>
        {
            GD.Print($"[lifeskill] create ← sel={_selected}");
            if (_selected > 0) SendOp(2, _selected);
        };
        var study = FindLeaf<BaseButton>(_window, "StudyBtn");
        if (study != null) study.Pressed += OnStudy;
        // Zone d'analyse (groupe 1) : drop d'une gemme à analyser.
        var slot = ItemSlotCell.Find(_window,
            "Root/JewlryStudy/Background/BG/Slot");
        if (slot != null) slot.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            _aBag = d["bag"].AsInt32();
            _aSlot = d["slot"].AsInt32();
            _aItem = d.ContainsKey("itemId")
                ? d["itemId"].AsInt32() : 0;
            GD.Print($"[lifeskill] analyse slot ← bag={_aBag} " +
                     $"slot={_aSlot} item={_aItem}");
            var it = PlayerBag.Items.FirstOrDefault(
                i => i.Bag == _aBag && i.Slot == _aSlot);
            if (it != null) slot.SetItem(it);
            var num = FindLeaf<Label>(_window, "AnalysisItemNum");
            if (num != null)
                num.Text = it != null ? $"{Math.Max(it.Amount, 1)}"
                                      : "";
        };
        ResetPlaceholders();
        HudWindows.Open(_window);
        HudWindows.Front(_window);
        Visible = true;
        FillRows();
    }

    /// <summary>Fenêtre neuve = neutre : les valeurs de maquette du
    /// layout (99, */99, 9999999, texte chinois) sont vidées ; libellés
    /// fixes posés en français.</summary>
    private void SetExact(string leaf, string text)
    {
        var c = _window == null ? null : RowLeaf(_window, leaf);
        if (c is Label l) l.Text = text;
        else if (c is RichTextLabel r) r.Text = text;
    }

    private void ResetPlaceholders()
    {
        if (_window == null) return;
        foreach (var n in new[] { "InfoText", "InfoText1", "GoldText",
            "SilverText", "LevelText", "AnalysisItemNum",
            "ItemText0", "ItemText1", "ItemText2",
            "ItemNumText0", "ItemNumText1", "ItemNumText2" })
            SetExact(n, "");
        SetButton("StudyBtn", "Analyser");
        SetButton("CreateBtn", _group == 1 ? "Façonner" : "Fabriquer");
        SetExact("Text1", "Résultat");
        SetExact("DemendInfo", "Matériaux requis");
        SetExact("DemendInfo1", "Matériaux requis");
        SetExact("ManufactureExpense", "Coût :");
        SetExact("LevelInfo", "Niveau requis :");
        SetExact("StudyEditText", "fois");
        SetExact("CreateEditText", "fois");
        SetExact("ManufactureExpense2", "fois");
        foreach (var n in new[] { "StudyEditBox", "CreateEditBox",
            "Editbox" })
            if (FindLeaf<LineEdit>(_window, n) is { } e) e.Text = "1";
    }

    private void SetButton(string leaf, string text)
    {
        if (_window != null && FindLeaf<Button>(_window, leaf) is { } b)
            b.Text = text;
    }

    /// <summary>StudyBtn (groupe 1) — 214 op0 : la recette se résout
    /// depuis l'item déposé (lien item→recette natif).</summary>
    private void OnStudy()
    {
        var recipeId = 0;
        if (_aBag >= 0 && _recipes != null)
            foreach (var r in _recipes.Values)
                if (r.Group == 1 && r.ItemId == _aItem)
                { recipeId = r.Id; break; }
        GD.Print($"[lifeskill] study ← aBag={_aBag} aItem={_aItem} " +
                 $"recipe={recipeId} recs={_recipes?.Count ?? -1}");
        if (recipeId <= 0) return;
        SendOp(0, recipeId, _aBag, _aSlot);
    }

    private void OnWindowHidden()
    {
        if (_window == null || _window.Visible || !_opOpen) return;
        _opOpen = false;
        // 120 : l'op interne métier = 22 (pas le code fenêtre 0x68).
        Send(new ZoneServerStrengthenClose { Value = 22 });
    }

    // ------------------------- lignes apprises -------------------------

    /// <summary>Une ligne <c>lifeskillitem</c> par recette apprise :
    /// icône produit, nom, barre d'exp / cap, 已熟練 au cap,
    /// ForgetBtn → op3, LvUpBtn → op4 (groupes 2-5) / op5 (groupe 1).</summary>
    private void FillRows()
    {
        if (_rows == null) return;
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        if (_recipes == null) return;
        foreach (var (id, kv) in _learned.OrderBy(k => k.Key))
        {
            if (!_recipes.TryGetValue(id, out var r)) continue;
            var row = MakeRow(r, kv.Exp);
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left })
                {
                    _selected = id;
                    ShowDetail(r);
                    GD.Print($"[lifeskill] ligne ← recette {id}");
                    row.AcceptEvent();
                }
            };
            _rows.AddChild(row);
        }
        if (_learned.Count == 0)
            _rows.AddChild(new Label
                { Text = UiTextEn.Translate("(no recipe learned)") });
    }

    /// <summary>Clone du template de ligne natif.</summary>
    private Control MakeRow(Recipe r, int exp)
    {
        Control row = _rowProto != null
            ? (Control)_rowProto.Duplicate() : new HBoxContainer();
        row.CustomMinimumSize = new Vector2(0, 44);
        row.MouseFilter = MouseFilterEnum.Stop;
        // La ligne entière sélectionne la recette : tout descendant
        // non-bouton passe en Pass (le clic atteint row.GuiInput ;
        // tooltips conservés). Les boutons gardent Stop.
        foreach (var ch in row.FindChildren("*", "Control", true, false)
                .OfType<Control>())
            if (ch is not BaseButton)
                ch.MouseFilter = MouseFilterEnum.Pass;
        ItemSlotCell.Find(row, "LifeSkillItem/Item/ItemSlot")
            ?.SetItemId(r.ItemId, Math.Max(r.Count, 1));
        SetRowText(row, "ItemDeatilText", ItemName(r.ItemId));
        var bar = FindLeaf<ProgressBar>(row, "ExpBar");
        if (bar != null)
        {
            bar.MaxValue = Math.Max(r.ExpCap, 1);
            bar.Value = Math.Min(exp, r.ExpCap);
        }
        var hasCap = r.ExpCap > 0;
        SetRowText(row, "ExpText", hasCap ? $"{exp}/{r.ExpCap}" : "");
        if (bar != null) bar.Visible = hasCap;
        if (FindLeaf<Button>(row, "ForgetBtn") is { } fb)
            fb.Text = "Oublier";
        if (FindLeaf<Button>(row, "LvUpBtn") is { } lb)
            lb.Text = "Progresser";
        SetRowText(row, "FullExpText", "Maîtrisé");
        var full = RowLeaf(row, "FullExpText");
        if (full != null) full.Visible = hasCap && exp >= r.ExpCap;
        var forget = FindLeaf<BaseButton>(row, "ForgetBtn");
        if (forget != null)
        {
            forget.Visible = r.PrevId >= 2;
            forget.Pressed += () => SendOp(3, r.Id);
        }
        var lvup = FindLeaf<BaseButton>(row, "LvUpBtn");
        if (lvup != null)
        {
            lvup.Visible = hasCap && exp >= r.ExpCap;
            // Groupe 1 : LevelUp (recette suivante) ; groupes 2-5 :
            // Apperception (recette produit aléatoire).
            var op = (byte)(_group == 1 ? 5 : 4);
            lvup.Pressed += () => SendOp(op, r.Id);
        }
        return row;
    }

    /// <summary>Détail de la recette sélectionnée : produit,
    /// matériaux (possédé/requis), coût, niveau requis.</summary>
    private void ShowDetail(Recipe r)
    {
        if (_window == null) return;
        var prod = ItemSlotCell.Find(_window,
            "Root/EngineeringStudy/Background/BG/Slot")
            ?? ItemSlotCell.Find(_window,
                "Root/JewlryStudy/Background/BG/Slot2");
        prod?.SetItemId(r.ItemId, Math.Max(r.Count, 1));
        SetText("InfoText", $"{ItemName(r.ItemId)}" +
            (r.Count > 1 ? $" ×{r.Count}" : ""));
        for (var i = 0; i < 3; i++)
        {
            var m = i < r.Materials.Count ? r.Materials[i]
                : (ItemId: 0, Qty: 0);
            var s = ItemSlotCell.Find(_window,
                $"Root/EngineeringStudy/Background/Border/ItemSlot{i}")
                ?? ItemSlotCell.Find(_window,
                    $"Root/JewlryStudy/Background/Border/ItemSlot{i}");
            if (s != null)
            {
                if (m.ItemId > 0) s.SetItemId(m.ItemId, m.Qty);
                else s.Clear();
            }
            SetText($"ItemText{i}",
                m.ItemId > 0 ? ItemName(m.ItemId) : "");
            var owned = Owned(m.ItemId);
            SetText($"ItemNumText{i}",
                m.ItemId > 0 ? $"{owned}/{m.Qty}" : "");
        }
        var (g, sv) = (r.Gold / 1000, r.Gold % 1000);
        SetText("GoldText", g > 0 ? $"{g}" : "0");
        SetText("SilverText", $"{sv}");
        SetText("LevelText", $"{r.ReqLevel}");
    }

    private int Owned(int itemId)
    {
        var n = 0;
        foreach (var it in PlayerBag.Items)
            if (it.ItemId == itemId && it.Bag is >= 0 and <= 4)
                n += Math.Max(it.Amount, 1);
        return n;
    }

    // ------------------------- tables -------------------------

    private static void EnsureTables()
    {
        if (_recipes != null) return;
        _recipes = LoadRecipes();
        var ip = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        _items = ip != null ? ItemTable.Load(ip) : new();
        var mp = AssetCatalog.ResolveRawAsset("data/db/c_itemmall.ini");
        if (mp != null)
            foreach (var (id, def) in ItemTable.Load(mp))
                _items.TryAdd(id, def);
    }

    /// <summary>c_lifeskill.ini — miroir LifeSkillParser serveur :
    /// id|grp|item|nom|qté|niv|expBase|expVar|expCap|or|next|prev|
    /// 3 mats (12-20) | résultats (21+).</summary>
    private static Dictionary<int, Recipe> LoadRecipes()
    {
        var dict = new Dictionary<int, Recipe>();
        var path = AssetCatalog.ResolveRawAsset(
            "data/db/c_lifeskill.ini");
        if (path == null) return dict;
        try
        {
            foreach (var line in EdenVfs.ReadLines(
                         path, EdenEncoding.Big5))
            {
                var c = line.Trim().Split('|');
                if (c.Length < 12 ||
                    !int.TryParse(c[0], out var id)) continue;
                var r = new Recipe
                {
                    Id = id,
                    Group = Int(c, 1), ItemId = Int(c, 2),
                    Name = c.Length > 3 ? c[3] : "",
                    Count = Int(c, 4), ReqLevel = Int(c, 5),
                    ExpCap = Int(c, 8), Gold = Int(c, 9),
                    NextId = Int(c, 10), PrevId = Int(c, 11),
                };
                for (var i = 0; i < 3; i++)
                {
                    var b = 12 + i * 3;
                    if (b + 2 >= c.Length) break;
                    var m = Int(c, b);
                    if (m > 0) r.Materials.Add((m, Int(c, b + 2)));
                }
                dict[id] = r;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[lifeskill] c_lifeskill : {e.Message}");
        }
        return dict;
    }

    private static int Int(string[] c, int i) =>
        i < c.Length && int.TryParse(c[i], out var v) ? v : 0;

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        foreach (var n in root.FindChildren("*", typeof(T).Name,
                     true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as T;
        return null;
    }

    private void SetText(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Feuille de ligne par nom exact (dernier segment) :
    /// « ExpText » ne doit pas tomber sur « FullExpText ».</summary>
    private static Control? RowLeaf(Control row, string leaf)
    {
        foreach (var n in row.FindChildren("*", "Control", true, false))
        {
            var nm = n.Name.ToString();
            var cut = nm.LastIndexOfAny(new[] { '/', '_' });
            if ((cut >= 0 ? nm[(cut + 1)..] : nm) == leaf)
                return n as Control;
        }
        return null;
    }

    private static void SetRowText(Control row, string leaf,
        string text)
    {
        var c = RowLeaf(row, leaf);
        if (c is Label l) l.Text = text;
        else if (c is RichTextLabel r) r.Text = text;
    }

    private static string ItemName(int itemId) =>
        TextResolver.ItemName(itemId) ?? $"#{itemId}";

    /// <summary>Repli sans uidata : liste texte des recettes.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "LifeSkillWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-210, -260);
        p.Size = new Vector2(420, 520);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        p.VisibilityChanged += OnWindowHidden;
        DragHelper.EnsureTitleBar(p,
            UiTextEn.Translate("Racial craft"));
        var scroll = new ScrollContainer { Name = "__scroll" };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 30;
        _rows = new VBoxContainer { Name = "__rows" };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        p.AddChild(scroll);
        p.Visible = true;
        Visible = true;
    }
}
