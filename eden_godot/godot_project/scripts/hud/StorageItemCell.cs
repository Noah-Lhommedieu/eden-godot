using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Cellule d'item partagée banque / coffre famille / panneau sac —
/// icône itemicon + quantité + tooltip riche (<see cref="ItemTooltip"/>)
/// + source/cible de drag avec payload identifiant le conteneur
/// d'origine.
///
/// Payload de drag (Godot Dictionary) :
///   - <c>kind</c> : "bag" (sac joueur), "bank" (banque perso),
///     "fchest" (coffre famille) ;
///   - <c>slot</c> : loc de l'item dans son conteneur ;
///   - <c>cont</c> : index du conteneur (onglet banque / coffre
///     0-2 / n° de sac pour "bag") ;
///   - <c>bag</c> : n° de sac (redondant avec cont pour "bag" — gardé
///     pour compatibilité avec le payload <c>{bag,slot}</c> émis par
///     la fenêtre inventaire : un dict SANS "kind" est lu comme un
///     item de sac).
///
/// Les cellules exposent deux événements ; c'est la fenêtre hôte qui
/// route vers le bon NCID (7 SwitchItem type 4 pour la banque,
/// 177/178 pour le coffre famille).
/// </summary>
public sealed partial class StorageItemCell : Panel
{
    /// <summary>Taille de cellule (même gabarit que l'inventaire).</summary>
    public const int CellSize = 40;

    /// <summary>Type de conteneur représenté ("bank"/"fchest"/"bag").</summary>
    public string Kind { get; }
    /// <summary>Index du conteneur (onglet banque / coffre / sac).</summary>
    public int ContIndex { get; }
    /// <summary>Loc que la cellule représente dans son conteneur.</summary>
    public int Loc { get; }

    /// <summary>Double-clic sur un item présent.</summary>
    public event Action<StorageItemCell>? DoubleClicked;
    /// <summary>Drop d'un payload d'item sur la cellule.</summary>
    public event Action<StorageItemCell, Godot.Collections.Dictionary>?
        ItemDropped;

    private readonly TextureRect _icon;
    private readonly Label _qty;
    private InventorySlot? _item;

    public StorageItemCell(string kind, int contIndex, int loc,
        Texture2D? slotTex)
    {
        Kind = kind;
        ContIndex = contIndex;
        Loc = loc;
        CustomMinimumSize = new Vector2(CellSize, CellSize);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            // Case lisible même sans texture « FunctionSlot » : fond violet
            // sombre + liseré clair (cases vides repérables).
            BgColor = new Color(0.18f, 0.12f, 0.27f, 0.9f),
            BorderColor = new Color(0.62f, 0.52f, 0.82f, 0.9f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
        });
        if (slotTex != null)
        {
            var bg = new TextureRect
            {
                Texture = slotTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);
        }
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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
        _qty.AddThemeColorOverride("font_color",
            new Color(1f, 0.95f, 0.6f));
        _qty.AddThemeColorOverride("font_outline_color",
            new Color(0f, 0f, 0f));
        _qty.AddThemeConstantOverride("outline_size", 3);
        AddChild(_qty);
    }

    /// <summary>Item affiché (null = cellule vide).</summary>
    public InventorySlot? Item => _item;

    /// <summary>Quantité de stack d'un item — la convention serveur
    /// range le count dans <c>durability</c> pour les stackables
    /// (MaxStack&gt;1 et non équipable — <c>BankService.IsStackable</c>) ;
    /// les items 274 portent aussi le count dans <c>Amount</c>.</summary>
    public static int StackCount(InventorySlot it, ItemTable.ItemDef? def)
    {
        var stackable = def is { MaxStack: > 1, EquipLoc: <= 0 };
        var n = stackable
            ? Math.Max(it.Durability, it.Amount)
            : Math.Max(it.Amount, 0);
        return Math.Max(n, 1);
    }

    /// <summary>Règle « stackable » miroir serveur (MaxStack&gt;1 &amp;&amp;
    /// non équipable).</summary>
    public static bool IsStackable(ItemTable.ItemDef? def) =>
        def is { MaxStack: > 1, EquipLoc: <= 0 };

    /// <summary>Peuple la cellule (icône, quantité, tooltip).</summary>
    public void SetItem(InventorySlot? it)
    {
        _item = it;
        Texture2D? tex = null;
        if (it != null && it.ItemId != 0)
        {
            var def = ItemTableCache.Get(it.ItemId);
            if (def != null && def.Code.Length > 0)
                tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
            var n = StackCount(it, def);
            _qty.Text = n > 1 ? n.ToString() : "";
        }
        else
        {
            _qty.Text = "";
        }
        _icon.Texture = tex;
        ItemTooltip.Bind(this, () => _item);
    }

    /// <summary>Payload de drag d'un item sac (source extérieure —
    /// fenêtre inventaire) lu comme équivalent à kind="bag".</summary>
    public static bool IsBagPayload(
        Godot.Collections.Dictionary d) =>
        !d.ContainsKey("kind") ||
        d["kind"].AsString() == "bag";

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton
            {
                Pressed: true, DoubleClick: true,
                ButtonIndex: MouseButton.Left,
            } && _item != null)
        {
            DoubleClicked?.Invoke(this);
            AcceptEvent();
        }
    }

    /// <summary>Source de drag : payload conteneur + aperçu = icône.</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_item == null || _item.ItemId == 0) return default;
        var it = _item;
        var d = new Godot.Collections.Dictionary
        {
            ["kind"] = Kind,
            ["cont"] = ContIndex,
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
                CustomMinimumSize = new Vector2(CellSize, CellSize),
            };
            SetDragPreview(preview);
        }
        return d;
    }

    /// <summary>Toute payload porteuse d'un slot est déposable — le
    /// filtrage fin (refus coffre→coffre…) est fait par l'hôte dans
    /// <see cref="ItemDropped"/>.</summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        return data.AsGodotDictionary().ContainsKey("slot");
    }

    public override void _DropData(Vector2 atPosition, Variant data) =>
        ItemDropped?.Invoke(this, data.AsGodotDictionary());

}
