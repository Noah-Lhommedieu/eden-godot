using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtres « ordres de travail » (métiers NPC 12-17) sur layouts
/// natifs — lot B2/B3 du plan craft.
///
/// Layouts corpus (`corpus/fr-full/uidata/layouts/`) :
///   - <c>worklistwnd.layout</c> (Root/Work → Static/Border+TotalText+
///     ScrollabelPane) : liste des commissions disponibles + ordres en
///     cours/terminés. Le gabarit de ligne WorkListItem est ABSENT du
///     corpus EDENFR → lignes construites en code (icône + texte).
///   - <c>collectwnd.layout</c> (récolte, workType 12-14) : UseItemSlot
///     (catalyseur/outil), ItemmallSlot (charm optionnel), 4 slots +
///     textes produits possibles, NeedTimeText, Gold/SliverText,
///     WorkLevelText, CommissionBtn → 170.
///   - <c>combinewnd.layout</c> (fusion/plans, workType 15-17) : même
///     gabarit + StuffItemSlot0-3 (matériaux, compteurs possédé/requis
///     colorés) + ResultItem (produit) + SuccessRate dans ResultText.
///   - <c>workresultwnd.layout</c> : popup résultat (498) — 4 slots
///     produits, ErrText échec, EventItem1-4 bonus, OKBtn → 171.
///
/// Wire (zone/serveur) :
///   S→C 496 EnterWorks (service NPC 12-17) → ouvre la worklist,
///     workType mémorisé (l'op works côté serveur).
///   S→C 497 PlayerWorkUpdate → ordres en cours (slot/type/id/nom).
///   S→C 498 PlayerWorkResult → ordre terminé non réclamé (produits,
///     workId &lt; 0 = échec) → popup workresultwnd.
///   S→C 500 PlayerWorkResultEnd → slot vidé après claim.
///   S→C 433 StrengthenCasting → barre de progression (durée ×100 ms).
///   C→S 170 PlayerWork [u32 workId][s16 catBag][s16 catSlot]
///     [s16 boostBag][s16 boostSlot] — catalyseur localisé dans
///     l'inventaire (cache du 312) ou déposé dans UseItemSlot ; charm
///     optionnel déposé dans ItemmallSlot.
///   C→S 171 PlayerWorkResultOK [s16 workNum] — « Récupérer » (OKBtn).
///
/// Montage : nœud <c>ZoneWindows/CraftWindow</c> de
/// <c>scenes/game.tscn</c> — binding paresseux de
/// <see cref="SessionCarry.Session"/> dans <see cref="_Process"/>.
///
/// Recettes : <see cref="CraftTable"/> (C_FamilyCollect/c_familycombine,
/// layouts = S_ serveur). Filtre par type : collect col1 = kind-0x32
/// (prouvé données : 0→type13, 1→type12, 2→type14) ;
/// combine col1 ∈{3,4,5} ↔ 15/16/17 (repli : toutes).
/// </summary>
public partial class CraftWindow : Control
{
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();

    // --- état works ---
    private int _workType = -1;
    private int _npcUid;
    private readonly Dictionary<int, ZonePlayerWorkUpdate.WorkOrder>
        _running = new();
    private readonly Dictionary<int, ZonePlayerWorkResult>
        _finished = new();

    // --- UI ---
    private Control? _workWnd;          // worklistwnd
    private VBoxContainer? _orderRows;  // lignes dans ScrollabelPane
    private Label? _totalText;
    private Control? _detailWnd;        // collectwnd ou combinewnd
    private Control? _resultWnd;        // workresultwnd
    private int _resultWorkNum = -1;
    private ProgressBar? _castBar;
    private double _castStart;           // s — Time.GetTicksMsec()/1000
    private double _castEnd;             // s — échéance du cast

    // --- détail commission courante ---
    private int _detailWorkId = -1;
    private int _boostBag = -1, _boostSlot = -1;   // charm déposé
    private int _catBag = -1, _catSlot = -1;       // catalyseur déposé

    private static Dictionary<int, CraftTable.CollectRecipe>? _collect;
    private static Dictionary<int, CraftTable.CombineRecipe>? _combine;
    private static Dictionary<int, ItemTable.ItemDef>? _items;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;   // passe-partout hors panneau
        BuildWindows();
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            { GD.PrintErr($"[craft] dispatch : {ex}"); }
        }
        // Barre de cast — progression locale jusqu'à l'échéance
        // (durée du 433 en unités de 100 ms).
        if (_castBar != null && _castBar.Visible)
        {
            var now = Time.GetTicksMsec() / 1000.0;
            if (now >= _castEnd) _castBar.Visible = false;
            else _castBar.Value = 100.0 * (now - _castStart)
                / Math.Max(_castEnd - _castStart, 0.001);
        }
    }

    // ------------------------- réseau -------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(496, r =>
        {
            var m = new ZoneEnterWorks();
            m.Deserialize(r);
            _pending.Enqueue(() => OpenWorks(m));
        });
        s.OnZone(497, r =>
        {
            var m = new ZonePlayerWorkUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyWorkUpdate(m));
        });
        s.OnZone(498, r =>
        {
            var m = new ZonePlayerWorkResult();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyWorkResult(m));
        });
        s.OnZone(500, r =>
        {
            var m = new ZonePlayerWorkResultEnd();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _running.Remove(m.WorkNum);
                _finished.Remove(m.WorkNum);
                if (_resultWorkNum == m.WorkNum &&
                    _resultWnd != null)
                    _resultWnd.Visible = false;
                RefreshOrders();
            });
        });
        s.OnZone(433, r =>
        {
            var m = new ZoneStrengthenCasting();
            m.Deserialize(r);
            _pending.Enqueue(() => StartCastBar(m));
        });
        GD.Print("[craft] binds 496/497/498/500/433 posés");
    }

    /// <summary>496 : natif (FUN_0070fe30 @0x0070fe30, client_game) —
    /// workType 12-14 → CollectWnd, 15-17 → CombineWnd, fenêtre vide
    /// (aucun catalogue) ; la recette vient de la formule déposée dans
    /// UseItemSlot. La worklist ne s'ouvre que sur le 497.</summary>
    private void OpenWorks(ZoneEnterWorks m)
    {
        _workType = m.WorkType;
        _npcUid = m.NpcUid;
        _bldLevel = m.Field4;
        if (_workWnd == null) BuildWindows();
        EnsureTables();
        ShowDetail();
        GD.Print($"[verify] craft : EnterWorks 496 type={m.WorkType} " +
                 $"npc={m.NpcUid} bld={m.Field4} — fenêtre " +
                 (CraftTable.WorkTypes.IsCollect(m.WorkType)
                     ? "CollectWnd" : "CombineWnd"));
    }

    private int _bldLevel;

    private void ApplyWorkUpdate(ZonePlayerWorkUpdate m)
    {
        _running.Clear();
        foreach (var o in m.Orders)
            _running[o.WorkNum] = o;
        RefreshOrders();
        // Natif (FUN_0070ff50) : le 497 ouvre WorkListWnd.
        if (m.Orders.Count > 0 && _workWnd != null)
            _workWnd.Visible = true;
    }

    /// <summary>498 : ordre terminé → popup workresultwnd natif.</summary>
    private void ApplyWorkResult(ZonePlayerWorkResult m)
    {
        _running.Remove(m.WorkNum);
        _finished[m.WorkNum] = m;
        RefreshOrders();
        ShowResult(m);
    }

    private void StartCastBar(ZoneStrengthenCasting m)
    {
        if (_castBar == null) return;
        _castStart = Time.GetTicksMsec() / 1000.0;
        _castEnd = _castStart + m.DurationUnits / 10.0;
        _castBar.Visible = true;
    }

    // ------------------------- envois -------------------------

    private void Send(NetMessage msg)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t =>
                GD.PrintErr($"[craft] send {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>CommissionBtn : 170 avec le catalyseur localisé (drop
    /// UseItemSlot ou recherche sac) + le charm déposé le cas échéant.</summary>
    private void OnCommission()
    {
        var workId = _detailWorkId;
        if (workId < 0) return;
        var (bag, slot) = (_catBag, _catSlot);
        if (bag < 0)
        {
            GD.Print($"[craft] aucune formule déposée work={workId}");
            return;
        }
        // Collect : la map CCollectData native est cléée par l'item id
        // du catalyseur (S_FamilyCollect col2) — le client envoie
        // l'item id dans le 170, pas le work_id col0 (désasm
        // CCollectQuery::Deserialize @0x085cd020). Combine : work_id.
        var wireId = workId;
        if (CraftTable.WorkTypes.IsCollect(_workType) &&
            _collect != null && _collect.TryGetValue(workId, out var cd))
            wireId = cd.CatalystItemId;
        Send(new ZonePlayerWork
        {
            WorkId = (uint)wireId,
            CatalystBag = (short)bag,
            CatalystSlot = (short)slot,
            BoostBag = (short)_boostBag,
            BoostSlot = (short)_boostSlot,
        });
        GD.Print($"[craft] ordre {workId} envoyé (cat {bag}:{slot}, " +
                 $"boost {_boostBag}:{_boostSlot})");
    }

    /// <summary>Localise le catalyseur : collect → CatalystItemId ;
    /// combine → 1er matériau dont le kind correspond au type ouvert
    /// (HYPOTHESIS : repli 1er matériau si aucun kind ne colle).</summary>
    private (int Bag, int Slot) FindCatalyst(int workId)
    {
        int wanted = 0;
        if (CraftTable.WorkTypes.IsCollect(_workType) &&
            _collect != null && _collect.TryGetValue(workId, out var c))
        {
            wanted = c.CatalystItemId;
        }
        else if (CraftTable.WorkTypes.IsCombine(_workType) &&
                 _combine != null &&
                 _combine.TryGetValue(workId, out var cb))
        {
            var kind = CraftTable.WorkTypes.CatalystKind(_workType);
            foreach (var m in cb.Materials)
                if (kind != 0 && _items != null &&
                    _items.TryGetValue(m.ItemId, out var d) &&
                    d.Kind == kind)
                { wanted = m.ItemId; break; }
            if (wanted == 0 && cb.Materials.Count > 0)
                wanted = cb.Materials[0].ItemId;   // parchemin
        }
        if (wanted == 0) return (-1, -1);
        foreach (var it in PlayerBag.Items)
            if (it.ItemId == wanted && it.Bag is >= 0 and <= 4)
                return (it.Bag, it.Slot);
        return (-1, -1);
    }

    /// <summary>Quantité possédée d'un itemId dans les sacs 0-4
    /// (compteurs possédé/requis des matériaux).</summary>
    private int Owned(int itemId)
    {
        var n = 0;
        foreach (var it in PlayerBag.Items)
            if (it.ItemId == itemId && it.Bag is >= 0 and <= 4)
                n += StorageItemCell.StackCount(it, ItemTableCache.Get(it.ItemId));
        return n;
    }

    private void OnClaimPressed(int workNum) =>
        Send(new ZonePlayerWorkResultOK { WorkNum = (short)workNum });

    // ------------------------- tables -------------------------

    private static void EnsureTables()
    {
        if (_collect != null) return;
        _collect = LoadOr("data/db/C_FamilyCollect.ini",
            CraftTable.LoadCollect);
        _combine = LoadOr("data/db/c_familycombine.ini",
            CraftTable.LoadCombine);
        var ipath = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        _items = ipath != null
            ? ItemTable.Load(ipath)
            : new Dictionary<int, ItemTable.ItemDef>();
    }

    private static Dictionary<int, T> LoadOr<T>(string rel,
        Func<string, Dictionary<int, T>> loader)
    {
        var p = AssetCatalog.ResolveRawAsset(rel);
        try
        { return p != null ? loader(p) : new(); }
        catch (Exception e)
        {
            GD.PrintErr($"[craft] {rel} : {e.Message}");
            return new();
        }
    }

    // ========================= UI native =========================

    /// <summary>Construit les 3 fenêtres natives (worklist, détail,
    /// résultat) ; panneau de repli si les layouts manquent.</summary>
    private void BuildWindows()
    {
        var hud = HudExtensions.FindHud();
        _workWnd ??= hud?.Build("worklistwnd.layout");
        _resultWnd ??= hud?.Build("workresultwnd.layout");
        if (_workWnd != null)
        {
            // Le repli du premier _Ready (ctx CEGUI pas encore prêt)
            // devient inutile — la fenêtre native le remplace.
            _panel?.QueueFree();
            _panel = null;
            AddChild(_workWnd);
            // Taille native 292×306 (worklistwnd), centrée — jamais
            // collée en haut-gauche sur le portrait/HP.
            HudExtensions.CenterNative(_workWnd, new Vector2(292, 306));
            var scroll = FindByLeaf<ScrollContainer>(_workWnd,
                "ScrollabelPane");
            _orderRows = new VBoxContainer { Name = "__rows" };
            _orderRows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            (scroll ?? _workWnd).AddChild(_orderRows);
            _totalText = FindByLeaf<Label>(_workWnd, "TotalText");
            if (_totalText != null)
            {
                _totalText.ClipText = false;
                _totalText.TextOverrunBehavior =
                    TextServer.OverrunBehavior.NoTrimming;
                _totalText.AddThemeColorOverride("font_color",
                    new Color(0.2f, 0.12f, 0.3f));
            }
            var close = FindByLeaf<BaseButton>(_workWnd, "CloseBtn");
            if (close != null) close.Pressed += () =>
                _workWnd.Visible = false;
            DragHelper.EnsureTitleBar(_workWnd,
                "Ordres de travail");
            DragHelper.Persist(_workWnd, "worklist");
            _workWnd.Visible = false;
        }
        if (_resultWnd != null)
        {
            AddChild(_resultWnd);
            HudExtensions.CenterNative(_resultWnd,
                new Vector2(300, 260));
            DragHelper.EnsureTitleBar(_resultWnd,
                "Résultat du travail");
            DragHelper.Persist(_resultWnd, "workresult");
            SetExact(_resultWnd, "OKBtn", "Récupérer");
            SetExact(_resultWnd, "CloseBtn", "Fermer");
            var ok = FindByLeaf<BaseButton>(_resultWnd, "OKBtn");
            if (ok != null) ok.Pressed += () =>
            {
                if (_resultWorkNum >= 0)
                    OnClaimPressed(_resultWorkNum);
                _resultWnd.Visible = false;
            };
            var close = FindByLeaf<BaseButton>(_resultWnd, "CloseBtn");
            if (close != null) close.Pressed += () =>
                _resultWnd.Visible = false;
            _resultWnd.Visible = false;
        }
        if (_workWnd == null && _panel == null) BuildFallback();
        // Barre de cast (433) — bas-centre de l'écran, visible même
        // worklist fermée (le cast continue en arrière-plan).
        if (_castBar != null) return;
        _castBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 0,
            Visible = false, ShowPercentage = false,
            CustomMinimumSize = new Vector2(220, 10),
        };
        _castBar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom,
            LayoutPresetMode.KeepSize, 120);
        AddChild(_castBar);
    }

    /// <summary>Premier enfant de type T dont le nom se termine par
    /// <paramref name="leaf"/> — les noms CEGUI sont sanitizés en
    /// « Root/…/leaf » → « Root_…_leaf ».</summary>
    private static T? FindByLeaf<T>(Node root, string leaf) where T : class
    {
        foreach (var n in root.FindChildren("*", typeof(T).Name,
                     true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as T;
        return null;
    }

    /// <summary>Ordres en cours / terminés à la suite des recettes.</summary>
    private void RefreshOrders()
    {
        if (_orderRows == null) return;
        foreach (var c in _orderRows.GetChildren())
            if (c.Name.ToString().StartsWith("__ord"))
                c.QueueFree();
        var n = 0;
        foreach (var o in _running.Values.OrderBy(o => o.WorkNum))
        {
            _orderRows.AddChild(OrderRow(
                $"#{o.WorkNum} " +
                "en cours" +
                $" — {o.FmName} (work {o.WorkId})", null));
            n++;
        }
        foreach (var kv in _finished.OrderBy(kv => kv.Key))
        {
            var m = kv.Value;
            var label = m.Failed
                ? string.Format("#{0} " + "ÉCHEC",
                    m.WorkNum)
                : $"#{m.WorkNum} " + "terminé — " +
                  string.Join(", ", m.Products.Select(
                      p => $"{ItemName(p.ItemId)}×{p.Amount}"));
            var row = OrderRow(label, m.Failed ? null : kv.Key);
            _orderRows.AddChild(row);
            n++;
        }
        if (_totalText != null)
            _totalText.Text =
                $"{n} ordre(s)";
    }

    private Control OrderRow(string text, int? claimWorkNum)
    {
        var row = new HBoxContainer
        {
            Name = "__ord",
            CustomMinimumSize = new Vector2(0, 24),
        };
        row.MouseFilter = MouseFilterEnum.Stop;
        var lab = new Label { Text = text };
        lab.AddThemeFontSizeOverride("font_size", 11);
        lab.AddThemeColorOverride("font_color",
            new Color(0.12f, 0.07f, 0.22f));
        if (claimWorkNum is { } w)
        {
            lab.AddThemeColorOverride("font_color",
                new Color(0.05f, 0.45f, 0.15f));
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left } mb &&
                    mb.DoubleClick)
                {
                    OnClaimPressed(w);
                    row.AcceptEvent();
                }
            };
        }
        row.AddChild(lab);
        return row;
    }

    // ------------------------- détail commission -------------------------

    private static readonly string[] StationNames =
    {
        "Mine", "Ferme", "Invocation", "Laboratoire alchimique",
        "Fonderie", "Mare magique",
    };

    private string StationName() =>
        _workType is >= 12 and <= 17
            ? StationNames[_workType - 12] : "Artisanat";

    /// <summary>Recette liée à une formule déposée : collect → col2
    /// (item catalyseur) ; combine → 1er matériau (la formule).
    /// Autorisation par PNJ = Kind de l'item (CatalystKind du type).</summary>
    private object? RecipeForItem(int itemId, out string err)
    {
        err = "";
        var kind = CraftTable.WorkTypes.CatalystKind(_workType);
        if (_items == null || !_items.TryGetValue(itemId, out var d)
            || d.Kind != kind)
        {
            err = "Ce PNJ ne peut pas fabriquer cette formule.";
            return null;
        }
        if (CraftTable.WorkTypes.IsCollect(_workType))
            return _collect?.Values.Where(r => r.CatalystItemId == itemId)
                .OrderBy(r => r.Id).FirstOrDefault();
        return _combine?.Values.Where(r => r.Materials.Count > 0 &&
            r.Materials[0].ItemId == itemId)
            .OrderBy(r => r.Id).FirstOrDefault();
    }

    private static readonly Color Ink = new(0.16f, 0.10f, 0.26f);

    /// <summary>Ouvre collectwnd/combinewnd natif, vide : titre = station,
    /// slot Formule déposable depuis le sac, Commander → 170.</summary>
    private void ShowDetail()
    {
        _detailWnd?.QueueFree();
        _detailWorkId = -1;
        _boostBag = _boostSlot = -1;
        _catBag = _catSlot = -1;
        var hud = HudExtensions.FindHud();
        var collect = CraftTable.WorkTypes.IsCollect(_workType);
        var file = collect ? "collectwnd.layout" : "combinewnd.layout";
        _detailWnd = hud?.Build(file);
        if (_detailWnd == null)
        {
            GD.PrintErr($"[craft] {file} indisponible");
            return;
        }
        AddChild(_detailWnd);
        HudExtensions.CenterNative(_detailWnd, new Vector2(300, 330));
        DragHelper.EnsureTitleBar(_detailWnd, StationName());
        PolishDetailStatics(_detailWnd, collect);
        var close = FindByLeaf<BaseButton>(_detailWnd, "CloseBtn");
        if (close != null) close.Pressed += () =>
            _detailWnd.Visible = false;
        var commission =
            FindByLeaf<BaseButton>(_detailWnd, "CommissionBtn");
        if (commission != null) commission.Pressed += OnCommission;
        var root = collect ? "Root/CollectWnd" : "Root/CombineWnd";
        var charm = FindSlot(_detailWnd,
            root + "/Background/ItemmallSlot");
        GD.Print($"[craft] slots : use={(FindSlot(_detailWnd, root + "/Background/UseItemSlot") != null)} " +
                 $"charm={charm != null}");
        if (charm != null)
            charm.Clicked += c => TakeHeldInto(c);
        if (charm != null)
            charm.ItemDropped += (_, d) =>
            {
                GD.Print($"[craft] drop charme {string.Join(",", d.Keys)}");
                _boostBag = d.ContainsKey("bag")
                    ? d["bag"].AsInt32() : -1;
                _boostSlot = d.ContainsKey("slot")
                    ? d["slot"].AsInt32() : -1;
                if (d.ContainsKey("itemId"))
                    charm.SetItemId(d["itemId"].AsInt32(), 1);
                if (_detailWorkId >= 0) RefreshRate();
            };
        var use = FindSlot(_detailWnd,
            root + "/Background/UseItemSlot");
        if (use != null)
            use.Clicked += c => TakeHeldInto(c);
        if (use != null)
            use.ItemDropped += (_, d) =>
            {
                var id = d.ContainsKey("itemId")
                    ? d["itemId"].AsInt32() : 0;
                var recipe = RecipeForItem(id, out var err);
                GD.Print($"[craft] drop formule item={id} type={_workType} " +
                    $"recette={(recipe == null ? "AUCUNE (" + err + ")" : "ok")}");
                if (recipe == null)
                {
                    if (err == "") err = "Aucune recette pour cette formule.";
                    PolishDetailStatics(_detailWnd!, collect);
                    SetText(_detailWnd!, "WorkLevelText", err);
                    return;
                }
                _catBag = d.ContainsKey("bag") ? d["bag"].AsInt32() : -1;
                _catSlot = d.ContainsKey("slot")
                    ? d["slot"].AsInt32() : -1;
                use.SetItemId(id, 1);
                if (recipe is CraftTable.CollectRecipe c)
                { _detailWorkId = c.Id; FillCollectDetail(_detailWnd!, c); }
                else if (recipe is CraftTable.CombineRecipe cb)
                { _detailWorkId = cb.Id; FillCombineDetail(_detailWnd!, cb); }
            };
        _detailWnd.Visible = true;
    }

    /// <summary>Slot par nom CEGUI complet, repli par feuille (le
    /// builder aplatit/renomme : CombineWindow cherche par feuille).</summary>
    private static ItemSlotCell? FindSlot(Control root, string cegui)
    {
        var s = ItemSlotCell.Find(root, cegui);
        if (s != null) return s;
        var leaf = cegui[(cegui.LastIndexOf('/') + 1)..];
        foreach (var n in root.FindChildren("*", "Control", true, false))
            if (n is ItemSlotCell c && n.Name.ToString().EndsWith(leaf))
                return c;
        GD.PrintErr($"[craft] slot introuvable : {cegui}");
        return null;
    }

    /// <summary>Modèle natif « objet en main » : clic gauche sur l'objet du
    /// sac (InventoryWindow.Held) puis clic sur le slot cible. Rejoue le
    /// chemin du drop (ItemDropped) avec {bag, slot, itemId}.</summary>
    private static void TakeHeldInto(ItemSlotCell cell)
    {
        if (InventoryWindow.TakeHeld() is not { } h) return;
        var it = InventoryWindow.ItemAt(h.Bag, h.Slot);
        if (it == null || it.ItemId == 0) return;
        GD.Print($"[craft] objet en main → {cell.Name} item={it.ItemId} " +
                 $"({h.Bag},{h.Slot})");
        cell.EmitDrop(new Godot.Collections.Dictionary
        {
            ["bag"] = h.Bag, ["slot"] = h.Slot, ["itemId"] = it.ItemId,
        });
    }

    private void RefreshRate()
    {
        if (_detailWnd != null && _combine != null &&
            _combine.TryGetValue(_detailWorkId, out var cb))
            FillCombineDetail(_detailWnd, cb);
    }

    /// <summary>collectwnd : formule + 4 produits possibles + coût
    /// + durée + niveau de travail.</summary>
    private void FillCollectDetail(Control w, CraftTable.CollectRecipe r)
    {
        for (var i = 0; i < 4; i++)
        {
            (int ItemId, string Name, int Qty, float Rate) p =
                i < r.Products.Count ? r.Products[i] : default;
            SetSlot(w, $"Root/CollectWnd/Background/ResultItemSlot{i}",
                p.ItemId, p.Qty);
            SetText(w, $"ResultItemText{i}",
                p.ItemId > 0 ? ItemName(p.ItemId) : "");
            SetText(w, $"ResultItemNumText{i}",
                p.ItemId > 0 ? $"{p.Qty} ({p.Rate:0}%)" : "");
        }
        SetText(w, "NeedTimeText", TimeText(r.Instant, r.DurationMin));
        var (g, s) = SplitMoney(r.Gold);
        SetText(w, "GoldText", g);
        SetText(w, "SliverText", s);
        SetText(w, "WorkLevelText", $"{r.CatalystQty}× " +
            ItemName(r.CatalystItemId) + " requis");
    }

    /// <summary>combinewnd : formule (UseItemSlot) + matériaux
    /// possédé/requis colorés + produit + taux + coût.</summary>
    private void FillCombineDetail(Control w, CraftTable.CombineRecipe r)
    {
        for (var i = 0; i < 4; i++)
        {
            (int ItemId, string Name, int Qty) m =
                i + 1 < r.Materials.Count ? r.Materials[i + 1] : default;
            SetSlot(w, "Root/CombineWnd/Background/Border/" +
                $"StuffItemSlot{i}", m.ItemId, m.Qty);
            SetText(w, $"StuffItemText{i}",
                m.ItemId > 0 ? ItemName(m.ItemId) : "");
            var num = FindByLeaf<Label>(w, $"StuffItemNumText{i}")
                as Control ?? FindByLeaf<RichTextLabel>(w,
                    $"StuffItemNumText{i}");
            if (num != null)
            {
                var owned = Owned(m.ItemId);
                SetText(w, $"StuffItemNumText{i}",
                    m.ItemId > 0 ? $"{owned}/{m.Qty}" : "");
                num.AddThemeColorOverride("font_color",
                    owned >= m.Qty ? Ink : new Color(0.75f, 0.1f, 0.1f));
            }
        }
        SetSlot(w, "Root/CombineWnd/Background/Border1/ResultItem",
            r.ProductItemId, r.ProductQty);
        SetText(w, "ResultText", ItemName(r.ProductItemId) +
            (r.ProductQty > 1 ? $" ×{r.ProductQty}" : ""));
        SetText(w, "Text7", "Taux de réussite : " + r.SuccessRate +
            "%" + (_boostSlot >= 0 ? " + aide" : ""));
        SetText(w, "NeedTimeText", TimeText(r.Instant, r.DurationMin));
        var (g, s) = SplitMoney(r.Gold);
        SetText(w, "GoldText", g);
        SetText(w, "SliverText", s);
        SetText(w, "WorkLevelText", _bldLevel > 0
            ? $"Commissions de niveau de travail {_bldLevel} et moins"
            : $"Niveau de travail requis : {r.BuildingLevel}");
    }

    /// <summary>Textes statiques des maquettes collectwnd/combinewnd
    /// (« Test… », CN) → français, par feuille. Les entrées « Test… »
    /// de ui_text_fr.tsv donnent de l'anglais : on passe par les clés
    /// CN sans préfixe.</summary>
    internal static void PolishDetailStatics(Control w, bool collect)
    {
        SetExact(w, "Text1", "Objet à utiliser");
        SetExact(w, "Text2", "Charme");
        SetExact(w, "Text3", "Durée");
        SetExact(w, "Text4", "Coût");
        SetExact(w, "Text5", collect ? "Peut obtenir :"
            : "Matériaux requis :");
        SetExact(w, "Text6", "Produit :");
        SetExact(w, "Text7", "");
        SetExact(w, "CloseBtn", "Fermer");
        SetExact(w, "CommissionBtn", "Commander");
        for (var i = 0; i < 4; i++)
        {
            SetText(w, $"StuffItemText{i}", "");
            SetText(w, $"StuffItemNumText{i}", "");
            SetText(w, $"ResultItemNumText{i}", "");
            SetText(w, $"ResultItemText{i}", "");
        }
        SetText(w, "ResultText", "");
        SetText(w, "WorkLevelText", "Déposez une formule dans l'emplacement");
        SetText(w, "NeedTimeText", "");
        SetText(w, "GoldText", "");
        SetText(w, "SliverText", "");
    }

    /// <summary>Comme SetText mais nom de feuille exact (« Text1 » ne
    /// doit pas viser « StuffItemText1 »).</summary>
    private static void SetExact(Control root, string leaf, string text)
    {
        foreach (var n in root.FindChildren("*", "", true, false))
        {
            var nm = n.Name.ToString();
            if (nm != leaf && !nm.EndsWith("_" + leaf) &&
                !nm.EndsWith("/" + leaf)) continue;
            if (n is Label l) { l.Text = text; return; }
            if (n is RichTextLabel r) { r.Text = text; return; }
            if (n is Button b) { b.Text = text; return; }
        }
    }

    /// <summary>workresultwnd (498) : 4 slots produits + textes +
    /// ErrText si échec ; OKBtn claim (171).</summary>
    private void ShowResult(ZonePlayerWorkResult m)
    {
        if (_resultWnd == null) return;
        _resultWorkNum = m.WorkNum;
        for (var i = 0; i < 4; i++)
        {
            var p = i < m.Products.Count ? m.Products[i] : default;
            SetSlot(_resultWnd,
                $"Root/WorkResult/Static/Border/ResultItem{i + 1}",
                i < m.Products.Count ? p.ItemId : 0,
                i < m.Products.Count ? p.Amount : 0);
            var ev = FindByLeaf<Control>(_resultWnd,
                $"EventItem{i + 1}");
            if (ev != null) ev.Visible = false;
        }
        // Maquette CN de Text1/Text2 (« 恭喜您… ») → FR dynamique ;
        // EventTitle/EventContent (événements) masqués, ils chevauchent.
        SetExact(_resultWnd, "Text1", (m.Failed ? "Commande échouée" : "Commande terminée !"));
        SetExact(_resultWnd, "Text2", m.Failed ? "" : string.Join("\n",
            m.Products.Select(p => $"{ItemName(p.ItemId)} ×{p.Amount}"))
            + (m.Failed ? "" : "\n" +
               "Récupérez votre commande."));
        // Encre sombre sur fond pâle, retour à la ligne propre.
        foreach (var leaf in new[] { "Text1", "Text2" })
            if (FindByLeaf<Label>(_resultWnd, leaf) is { } tl)
            {
                tl.AddThemeColorOverride("font_color",
                    new Color(0.16f, 0.10f, 0.26f));
                tl.AddThemeFontSizeOverride("font_size",
                    leaf == "Text1" ? 14 : 11);
                tl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                tl.ClipText = true;
                tl.HorizontalAlignment = HorizontalAlignment.Center;
            }
        SetText(_resultWnd, "ErrText", "");
        foreach (var leaf in new[] { "EventTitle", "EventContent" })
            if (FindByLeaf<Control>(_resultWnd, leaf) is { } ec)
                ec.Visible = false;
        for (var i = 1; i <= 4; i++)
            if (FindSlot(_resultWnd,
                    $"Root/WorkResult/Static/Border/ResultItem{i}")
                is { } rs)
                rs.CustomMinimumSize = new Vector2(34, 34);
        _resultWnd.Visible = true;
    }

    /// <summary>Slot natif → id d'item (0 = masqué/vide).</summary>
    private static void SetSlot(Control root, string cegui,
        int itemId, int amount)
    {
        var slot = FindSlot(root, cegui);
        if (slot == null) return;
        if (itemId <= 0) slot.Clear();
        else slot.SetItemId(itemId, amount);
    }

    private static readonly string[] LightLeaves =
        { "NeedTimeText", "GoldText", "SliverText", "WorkLevelText",
          "Text7" };
    private static readonly Color Cream = new(0.97f, 0.94f, 0.85f);

    private static void SetText(Control root, string leaf, string text)
    {
        foreach (var n in root.FindChildren("*", "Control", true, false))
        {
            if (!n.Name.ToString().EndsWith(leaf) || n is not Control c)
                continue;
            if (n is Label l) l.Text = text;
            else if (n is RichTextLabel r) r.Text = text;
            else continue;
            // Fond violet foncé : texte clair ; fond clair (matériaux,
            // produit) : encre sombre.
            var col = Array.IndexOf(LightLeaves, leaf) >= 0 ? Cream : Ink;
            c.AddThemeColorOverride("font_color", col);
            c.AddThemeColorOverride("default_color", col);
            return;
        }
    }

    /// <summary>Durée formatée (« 99分99秒 » natif → min/sec).</summary>
    private static string TimeText(bool instant, int durationMin) =>
        instant ? "instantané"
                : $"{durationMin} min";

    /// <summary>Prix en copper → (or, argent) affichés natif.</summary>
    private static (string Gold, string Silver) SplitMoney(int copper) =>
        (copper / 1000 > 0 ? $"{copper / 1000}" : "",
         $"{copper % 1000}");

    private string ItemName(int itemId) =>
        TextResolver.ItemName(itemId)
        ?? (_items != null && _items.TryGetValue(itemId, out var d) &&
            d.Name.Length > 0 ? d.Name : $"item #{itemId}");

    // ------------------------- repli -------------------------

    private Panel? _panel;

    /// <summary>Repli sans uidata : panneau minimal indiquant le
    /// workType + ordres (aucune recette — layouts requis).</summary>
    private void BuildFallback()
    {
        _panel = new Panel { Name = "CraftPanel" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.Position = new Vector2(-330, -250);
        _panel.Size = new Vector2(660, 500);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "Craft");
        DragHelper.Persist(_panel, "craft");
        _orderRows = new VBoxContainer
        {
            Name = "__rows",
            Position = new Vector2(10, 40),
            Size = new Vector2(640, 400),
        };
        _panel.AddChild(_orderRows);
        _panel.Visible = false;
    }
}
