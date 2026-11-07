using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Slot item natif des layouts CEGUI (<c>DejaVu/SlotImage*</c> et
/// <c>CEGUI/SlotImageItem</c>) — instancié par
/// <c>CeguiToGodot</c> quand le builder rencontre un slot craft
/// (strengthen/collect/combine/magicbox/cube/lifeskill…).
///
/// Trois usages :
///   - item d'inventaire (<see cref="SetItem"/>) : icône + qté +
///     tooltip riche + payload drag « bag » compatible
///     <see cref="StorageItemCell"/> ;
///   - item par id (<see cref="SetItemId"/>) : recettes, boutique,
///     résultats — pas de drag (pas de loc réel) ;
///   - cible de drop : <see cref="AcceptDrop"/> filtre, l'événement
///     <see cref="ItemDropped"/> est routé par la fenêtre hôte.
/// </summary>
public sealed partial class ItemSlotCell : TextureRect
{
    /// <summary>Taille de case quand le layout n'en impose pas.</summary>
    public const int DefaultSize = 34;

    private readonly TextureRect _icon;
    private readonly Label _qty;
    private InventorySlot? _item;
    private int _itemId;
    private int _displayAmount = 1;

    /// <summary>Kind du payload drag émis (« bag » par défaut —
    /// même convention que <see cref="StorageItemCell"/>).</summary>
    public string PayloadKind = "bag";
    /// <summary>Filtre d'acceptation de drop — null = toute payload
    /// porteuse d'un slot est acceptée.</summary>
    public Func<Godot.Collections.Dictionary, bool>? AcceptDrop;

    /// <summary>Clic gauche simple.</summary>
    public event Action<ItemSlotCell>? Clicked;
    /// <summary>Double-clic gauche.</summary>
    public event Action<ItemSlotCell>? DoubleClicked;
    /// <summary>Clic droit.</summary>
    public event Action<ItemSlotCell>? RightClicked;
    /// <summary>Drop d'une payload item sur le slot.</summary>
    public event Action<ItemSlotCell, Godot.Collections.Dictionary>?
        ItemDropped;

    /// <summary>Requis par <c>Node.Duplicate()</c> sur les templates de
    /// lignes contenant un slot (shopitem.layout, lifeskill…). Les
    /// enfants dupliqués en surnombre sont vides (icône null, label
    /// vide) — aucun effet visuel.</summary>
    public ItemSlotCell() : this(null) { }

    /// <param name="slotTex">Image de fond du slot (région d'imageset
    /// du layout — cadre de case) ; null = fond discret.</param>
    public ItemSlotCell(Texture2D? slotTex = null)
    {
        // TextureRect (pas Panel) : les fenêtres historiques font
        // Find<TextureRect> sur les SlotImage (ItemMall, BuffBar,
        // étal) — la case reste compatible avec ce lookup.
        Texture = slotTex;
        CustomMinimumSize = new Vector2(DefaultSize, DefaultSize);
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _icon.OffsetLeft = _icon.OffsetTop = 2;
        _icon.OffsetRight = _icon.OffsetBottom = -2;
        AddChild(_icon);
        _qty = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _qty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _qty.OffsetRight = -2;
        _qty.OffsetBottom = -1;
        _qty.AddThemeFontSizeOverride("font_size", 9);
        _qty.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
        _qty.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _qty.AddThemeConstantOverride("outline_size", 3);
        AddChild(_qty);
        RefreshTooltip();
    }

    /// <summary>Item d'inventaire affiché (null = vide ou id brut).</summary>
    public InventorySlot? Item => _item;
    /// <summary>Id affiché (item ou id brut) ; 0 = slot vide.</summary>
    public int ItemId => _item?.ItemId ?? _itemId;

    /// <summary>Peuple le slot avec un item d'inventaire.</summary>
    public void SetItem(InventorySlot? it)
    {
        _item = it;
        _itemId = it?.ItemId ?? 0;
        _displayAmount = 1;
        var tex = IconFor(_itemId);
        int n = 0;
        if (it != null && it.ItemId != 0)
            n = StorageItemCell.StackCount(it, ItemTableCache.Get(it.ItemId));
        _qty.Text = n > 1 ? n.ToString() : "";
        _icon.Texture = tex;
        RefreshTooltip();
    }

    /// <summary>Peuple le slot avec un id d'item hors inventaire
    /// (recette, boutique, produit de craft) — tooltip par id.</summary>
    public void SetItemId(int itemId, int amount = 1)
    {
        _item = null;
        _itemId = itemId;
        _displayAmount = amount;
        _icon.Texture = IconFor(itemId);
        _qty.Text = amount > 1 ? amount.ToString() : "";
        RefreshTooltip();
    }

    /// <summary>Vide le slot.</summary>
    public void Clear() => SetItemId(0, 1);

    private static Texture2D? IconFor(int itemId)
    {
        if (itemId == 0) return null;
        var def = ItemTableCache.Get(itemId);
        return def is { Code.Length: > 0 }
            ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
            : null;
    }

    private void RefreshTooltip()
    {
        if (_item is { } it && it.ItemId != 0)
            ItemTooltip.Bind(this, () => _item);
        else if (_itemId != 0)
            ItemTooltip.BindId(this, () => _itemId, _displayAmount);
        else
            ItemTooltip.BindCustom(this, () => null);
    }

    /// <summary>Tous les slots natifs d'un arbre de layout — sert aux
    /// fenêtres pour retrouver leurs cases par nom après le build. Le
    /// filtre de type C# est fait en managed : FindChildren par nom de
    /// classe ne connaît pas les types sans [GlobalClass].</summary>
    public static List<ItemSlotCell> AllIn(Control root) =>
        root.FindChildren("*", "Control", true, false)
            .OfType<ItemSlotCell>().ToList();

    /// <summary>Slot natif d'un arbre par son nom CEGUI (sanitizé —
    /// « Root/Wnd/Slot1 » → « Root_Wnd_Slot1 »).</summary>
    public static ItemSlotCell? Find(Control root, string ceguiName) =>
        root.FindChild(ceguiName.Replace('/', '_'), true, false)
            as ItemSlotCell;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.DoubleClick)
                    DoubleClicked?.Invoke(this);
                else
                    Clicked?.Invoke(this);
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                RightClicked?.Invoke(this);
                AcceptEvent();
            }
        }
    }

    /// <summary>Source de drag : seuls les items d'inventaire partent
    /// (un slot recette n'a pas de loc réel à déplacer).</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_item == null || _item.ItemId == 0) return default;
        var it = _item;
        var d = new Godot.Collections.Dictionary
        {
            ["kind"] = PayloadKind,
            ["cont"] = it.Bag,
            ["slot"] = it.Slot,
            ["bag"] = it.Bag,
            ["itemId"] = it.ItemId,
        };
        if (_icon.Texture != null)
        {
            var preview = new TextureRect
            {
                Texture = _icon.Texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(DefaultSize, DefaultSize),
            };
            SetDragPreview(preview);
        }
        return d;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("slot")) return false;
        return AcceptDrop?.Invoke(d) ?? true;
    }

    public override void _DropData(Vector2 atPosition, Variant data) =>
        ItemDropped?.Invoke(this, data.AsGodotDictionary());

    /// <summary>Dépôt programmatique (modèle « objet en main »).</summary>
    public void EmitDrop(Godot.Collections.Dictionary d) =>
        ItemDropped?.Invoke(this, d);
}
