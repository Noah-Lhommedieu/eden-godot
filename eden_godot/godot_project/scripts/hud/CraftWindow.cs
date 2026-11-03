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
/// Fenêtre « ordres de travail » (métiers NPC 12-17) — lot S8.
///
/// Montage : nœud <c>ZoneWindows/CraftWindow</c> de
/// <c>scenes/game.tscn</c> (HudRoot.cs/GameScene.cs intouchés) —
/// binding paresseux de <see cref="SessionCarry.Session"/> dans
/// <see cref="_Process"/>, même pattern que <see cref="HudNetBridge"/>.
///
/// Wire (zone/serveur) :
///   S→C 496 EnterWorks (service NPC 12-17) → ouvre la fenêtre,
///     workType mémorisé (l'op works côté serveur).
///   S→C 497 PlayerWorkUpdate → ordres en cours (slot/type/id/nom).
///   S→C 498 PlayerWorkResult → ordre terminé non réclamé (produits,
///     workId &lt; 0 = échec).
///   S→C 500 PlayerWorkResultEnd → slot vidé après claim.
///   S→C 433 StrengthenCasting → barre de progression (durée ×100 ms).
///   C→S 170 PlayerWork [u32 workId][s16 catBag][s16 catSlot]
///     [s16 -1][s16 -1] — catalyseur localisé dans l'inventaire
///     (cache du 312 EnterNodeInventory) par itemId/kind.
///   C→S 171 PlayerWorkResultOK [s16 workNum] — « Récupérer ».
///
/// Recettes : <see cref="CraftTable"/> (C_FamilyCollect/c_familycombine,
/// layouts = S_ serveur). Filtre par type : HYPOTHESIS documentée
/// (collect col1 = workType-12 ; combine col1 ∈{3,4,5} ↔ 15/16/17 —
/// les autres catégories sont listées dans un onglet « Autre »).
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
    private List<InventorySlot> _inventory = new();

    // --- UI ---
    private Panel? _panel;
    private ItemList? _recipeList;
    private ItemList? _ordersList;
    private Label? _detail;
    private Label? _status;
    private Button? _craftBtn;
    private ProgressBar? _castBar;
    private double _castStart;           // s — Time.GetTicksMsec()/1000
    private double _castEnd;             // s — échéance du cast

    private static Dictionary<int, CraftTable.CollectRecipe>? _collect;
    private static Dictionary<int, CraftTable.CombineRecipe>? _combine;
    private static Dictionary<int, ItemTable.ItemDef>? _items;
    private readonly List<int> _recipeIds = new();   // liste affichée

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;   // passe-partout hors panneau
        BuildPanel();
        Visible = true;   // le Control racine reste actif ; le panneau
        if (_panel != null) _panel.Visible = false;  // est masqué.
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
        // (durée du 433 en unités de 100 ms, serveur plafonné dev).
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
                RefreshOrders();
            });
        });
        s.OnZone(433, r =>
        {
            var m = new ZoneStrengthenCasting();
            m.Deserialize(r);
            _pending.Enqueue(() => StartCastBar(m));
        });
        // Cache d'inventaire (312) — localisation du catalyseur pour
        // le 170. Handler supplémentaire : GameScene en pose déjà un.
        s.OnZone(312, r =>
        {
            var items = NativeInventoryParser.Parse(
                r.ReadBytes(r.Remaining));
            _pending.Enqueue(() => _inventory = items);
        });
        GD.Print("[craft] binds 496/497/498/500/433/312 posés");
    }

    /// <summary>496 : ouvre la fenêtre works pour ce type de métier.</summary>
    private void OpenWorks(ZoneEnterWorks m)
    {
        _workType = m.WorkType;
        _npcUid = m.NpcUid;
        EnsureTables();
        FillRecipes();
        RefreshOrders();
        if (_panel != null) _panel.Visible = true;
        Status(string.Format(UiTextEn.Translate("Work opened — type {0}"), m.WorkType));
        GD.Print($"[verify] craft : EnterWorks 496 type={m.WorkType} " +
                 $"npc={m.NpcUid} — fenêtre ouverte");
    }

    private void ApplyWorkUpdate(ZonePlayerWorkUpdate m)
    {
        _running.Clear();
        foreach (var o in m.Orders)
            _running[o.WorkNum] = o;
        RefreshOrders();
    }

    private void ApplyWorkResult(ZonePlayerWorkResult m)
    {
        _running.Remove(m.WorkNum);
        _finished[m.WorkNum] = m;
        RefreshOrders();
        Status(m.Failed
            ? UiTextEn.Translate($"Order #{m.WorkNum} failed ({-m.WorkId})")
            : UiTextEn.Translate($"Order #{m.WorkNum} finished — {m.Products.Count} product(s)"));
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
        if (s?.Zone?.IsConnected != true)
        {
            Status(UiTextEn.Translate("offline — not sent"), true);
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t =>
                GD.PrintErr($"[craft] send {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnCraftPressed()
    {
        var sel = _recipeList?.GetSelectedItems();
        if (sel == null || sel.Length == 0 ||
            sel[0] >= _recipeIds.Count)
        {
            Status("choisis une recette", true);
            return;
        }
        var workId = _recipeIds[sel[0]];
        // Catalyseur : item de la recette localisé (sac 0-4) — pour les
        // combines, le parchemin est un matériau (kind 0x35-0x37 par
        // type ; on prend le 1er matériau présent en sac).
        var (bag, slot) = FindCatalyst(workId);
        if (bag < 0)
        {
            Status("catalyseur introuvable dans l'inventaire", true);
            return;
        }
        Send(new ZonePlayerWork
        {
            WorkId = (uint)workId,
            CatalystBag = (short)bag,
            CatalystSlot = (short)slot,
            BoostBag = -1,
            BoostSlot = -1,
        });
        Status(string.Format(UiTextEn.Translate("Order {0} sent (catalyst {1}:{2})"), workId, bag, slot));
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
        foreach (var it in _inventory)
            if (it.ItemId == wanted && it.Bag is >= 0 and <= 4)
                return (it.Bag, it.Slot);
        return (-1, -1);
    }

    private void OnClaimPressed(int workNum) =>
        Send(new ZonePlayerWorkResultOK { WorkNum = (short)workNum });

    // ------------------------- tables + UI -------------------------

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

    /// <summary>Recettes du type ouvert — collect par catégorie
    /// workType-12 ; combine catégorie = workType-12 si des recettes
    /// matchent, sinon toutes (catégories 6/7/20 non triées).</summary>
    private void FillRecipes()
    {
        _recipeList?.Clear();
        _recipeIds.Clear();
        if (_recipeList == null) return;
        if (CraftTable.WorkTypes.IsCollect(_workType) &&
            _collect != null)
        {
            var cat = _workType - 12;
            foreach (var r in _collect.Values
                         .Where(r => r.Category == cat)
                         .OrderBy(r => r.Id))
                AddRecipe(r.Id, $"{r.Name}  ({r.CatalystQty}× " +
                    $"{ItemName(r.CatalystItemId)}, {r.Gold}g)");
            if (_recipeIds.Count == 0)   // repli : catégorie inconnue
                foreach (var r in _collect.Values.OrderBy(r => r.Id))
                    AddRecipe(r.Id, $"[cat{r.Category}] {r.Name}");
        }
        else if (CraftTable.WorkTypes.IsCombine(_workType) &&
                 _combine != null)
        {
            var cat = _workType - 12;
            var matched = _combine.Values
                .Where(r => r.Category == cat).ToList();
            var src = matched.Count > 0 ? matched
                : _combine.Values.ToList();
            foreach (var r in src.OrderBy(r => r.Id))
                AddRecipe(r.Id, $"{ItemName(r.ProductItemId)} ×" +
                    $"{r.ProductQty}  ({r.Gold}g, {r.SuccessRate}%)");
        }
        if (_recipeIds.Count == 0)
            _recipeList.AddItem(UiTextEn.Translate("(no recipe for this type)"));
        if (_detail != null) _detail.Text = "";
    }

    private void AddRecipe(int id, string label)
    {
        _recipeIds.Add(id);
        _recipeList?.AddItem(label);
    }

    private void OnRecipeSelected(long index)
    {
        if (_detail == null || index >= _recipeIds.Count) return;
        var id = _recipeIds[(int)index];
        var sb = new System.Text.StringBuilder();
        if (_collect != null &&
            _collect.TryGetValue(id, out var c))
        {
            sb.AppendLine($"{c.Name} — #{id}");
            sb.AppendLine($"Catalyseur : {c.CatalystQty}× " +
                ItemName(c.CatalystItemId));
            sb.AppendLine(UiTextEn.Translate($"Gold: {c.Gold}  Duration: ") +
                (c.Instant ? "instantanée" : UiTextEn.Translate($"{c.DurationMin} min")) +
                UiTextEn.Translate($"  Exp : {c.Exp}"));
            sb.AppendLine("Produits :");
            foreach (var p in c.Products)
                sb.AppendLine($"  {ItemName(p.ItemId)} ×{p.Qty} " +
                    $"({p.Rate:0}%)");
        }
        else if (_combine != null &&
                 _combine.TryGetValue(id, out var cb))
        {
            sb.AppendLine($"{ItemName(cb.ProductItemId)} ×" +
                $"{cb.ProductQty} — #{id}");
            sb.AppendLine(UiTextEn.Translate($"Success {cb.SuccessRate}%  Gold: {cb.Gold}  ") +
                (cb.Instant ? "instantané" : UiTextEn.Translate($"{cb.DurationMin} min")));
            sb.AppendLine(UiTextEn.Translate("Materials:"));
            foreach (var m in cb.Materials)
                sb.AppendLine($"  {ItemName(m.ItemId)} ×{m.Qty}");
        }
        _detail.Text = sb.ToString();
        var (b, sl) = FindCatalyst(id);
        if (_craftBtn != null)
            _craftBtn.Disabled = b < 0 ||
                _session?.Zone?.IsConnected != true;
    }

    private void RefreshOrders()
    {
        _ordersList?.Clear();
        if (_ordersList == null) return;
        foreach (var o in _running.Values.OrderBy(o => o.WorkNum))
            _ordersList.AddItem(
                $"#{o.WorkNum} en cours — work {o.WorkId} " +
                UiTextEn.Translate($"(type {o.WorkType}) {o.FmName}"));
        foreach (var kv in _finished.OrderBy(kv => kv.Key))
        {
            var m = kv.Value;
            var label = m.Failed
                ? string.Format(UiTextEn.Translate("#{0} FAILED — work {1}"), m.WorkNum, -m.WorkId)
                : string.Format(UiTextEn.Translate("#{0} done — "), m.WorkNum) + string.Join(", ",
                    m.Products.Select(p =>
                        $"{ItemName(p.ItemId)}×{p.Amount}"));
            var idx = _ordersList.AddItem(label);
            _ordersList.SetItemMetadata(idx, m.WorkNum);
        }
    }

    private string ItemName(int itemId) =>
        TextResolver.ItemName(itemId)
        ?? (_items != null && _items.TryGetValue(itemId, out var d) &&
            d.Name.Length > 0 ? d.Name : $"item #{itemId}");

    private void Status(string t, bool err = false)
    {
        if (_status == null) return;
        _status.Text = t;
        _status.AddThemeColorOverride("font_color",
            err ? new Color(1f, 0.45f, 0.4f) : new Color(0.6f, 0.9f, 0.6f));
        GD.Print($"[craft] {t}");
    }

    /// <summary>Panneau de repli autonome (le HUD CEGUI n'est pas
    /// accessible sans HudRoot — style sombre cohérent).</summary>
    private void BuildPanel()
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
        // Fenêtre déplaçable : barre de titre + position persistée —
        // l'ancien titre manuel est décalé sous la barre.
        DragHelper.EnsureTitleBar(_panel, "Craft");
        DragHelper.Persist(_panel, "craft");

        var title = new Label { Text = UiTextEn.Translate("Work orders"),
            Position = new Vector2(10, 30) };
        _panel.AddChild(title);
        // Le bouton × custom faisait doublon avec celui de la barre
        // de titre (même coin) — retiré.

        var leftLbl = new Label { Text = UiTextEn.Translate("Recipes"),
            Position = new Vector2(10, 34) };
        _panel.AddChild(leftLbl);
        _recipeList = new ItemList
        {
            Position = new Vector2(10, 54),
            Size = new Vector2(300, 240),
        };
        _recipeList.ItemSelected += OnRecipeSelected;
        _panel.AddChild(_recipeList);

        _detail = new Label
        {
            Position = new Vector2(320, 54),
            Size = new Vector2(330, 240),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _detail.AddThemeFontSizeOverride("font_size", 11);
        _panel.AddChild(_detail);

        _castBar = new ProgressBar
        {
            Position = new Vector2(10, 300),
            Size = new Vector2(300, 14),
            MinValue = 0, MaxValue = 100, Value = 0,
            Visible = false, ShowPercentage = false,
        };
        _panel.AddChild(_castBar);

        _craftBtn = new Button
        {
            Text = UiTextEn.Translate("Craft"),
            Position = new Vector2(10, 322),
            Size = new Vector2(140, 28),
        };
        _craftBtn.Pressed += OnCraftPressed;
        _panel.AddChild(_craftBtn);

        var ordersLbl = new Label { Text = UiTextEn.Translate(
            "Orders (double-click = collect)"), Position = new Vector2(10, 356) };
        ordersLbl.AddThemeFontSizeOverride("font_size", 11);
        _panel.AddChild(ordersLbl);
        _ordersList = new ItemList
        {
            Position = new Vector2(10, 376),
            Size = new Vector2(640, 86),
        };
        _ordersList.ItemActivated += idx =>
        {
            var meta = _ordersList.GetItemMetadata((int)idx);
            if (meta.VariantType == Variant.Type.Int)
                OnClaimPressed((int)meta);
        };
        _panel.AddChild(_ordersList);

        _status = new Label { Position = new Vector2(10, 468),
            Size = new Vector2(640, 24) };
        _status.AddThemeFontSizeOverride("font_size", 11);
        _panel.AddChild(_status);
    }
}
