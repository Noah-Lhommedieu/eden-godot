using System;
using System.Collections.Concurrent;
using System.Linq;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Panneau d'équipement porté (lot S2) — affiche les locs de
/// l'équipement du personnage (tête/torse/jambes/mains/pieds/dos,
/// arme/off-hand/distance, accessoires) alimenté par
/// <see cref="PlayerEquipment"/> (311 entrée, 282 complet, 276 delta).
///
/// Interactions :
///   - double-clic sur un slot → déséquipe (NCID 7 (1,loc,0)→(0,−1,−1)
///     puis NCID 8 mask → réponse 282, flux natif documenté) ;
///   - drop d'un item draggé depuis l'inventaire sur le slot → équipe ;
///   - <see cref="PlayerEquipment.Changed"/> → réaffiche les icônes ET
///     applique le visuel d'équipement au corps 3D
///     (<see cref="CharVisual.ApplyEquipment"/> — meshes tenue
///     4pequip.ini + armes GLB sur Bip01 Prop1/Prop2).
///
/// Envoi réseau : binding paresseux sur <see cref="SessionCarry.Session"/>
/// (même pattern que <see cref="HudNetBridge"/>).
/// </summary>
public partial class EquipSlotsPanel : Control
{
    /// <summary>Locs affichés (ordre visuel, nom EN).</summary>
    private static readonly (int Loc, string Label)[] SlotDefs =
    {
        (1, "Head"), (2, "Body"), (3, "Legs"),
        (4, "Hands"), (5, "Feet"), (6, UiTextEn.Translate("Back")),
        (7, "Weapon"), (8, "Off-hand"), (10, "Ranged"),
        (11, "Necklace"), (12, "Ring"), (13, "Ring"),
        (14, "Trinket"), (15, "Trinket"),
    };

    private const int CellSize = 36;

    private Panel? _window;
    /// <summary>(conteneur équipement, loc) → cellule. Conteneur 0 =
    /// tenue portée ; 2/3/4 = pages d'ensemble cloth/light/heavy
    /// (fast-dress natif, <see cref="PlayerEquipment.CostumeAt"/>).</summary>
    private readonly Dictionary<(int Container, int Loc), EquipCell>
        _cells = new();
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    private bool _equipmentHooked;
    private Node3D? _playerBody;
    private string? _kusoPending = KusoArg();

    /// <summary>--kuso=&lt;suffix&gt;:&lt;0|1&gt; ligne de commande.</summary>
    private static string? KusoArg()
    {
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--kuso=", StringComparison.Ordinal))
                return a["--kuso=".Length..];
        return null;
    }

    /// <summary>Construit le panneau (fenêtre flottante stylée —
    /// montée comme enfant de l'InventoryWindow qui pilote sa
    /// visibilité).</summary>
    public void Build(HudRoot hud) => Build(hud, embedded: false);

    /// <summary><paramref name="embedded"/> : intégré à une autre
    /// fenêtre (Profil, ref profile.png) — pas de titre ni de barre de
    /// drag, fond transparent, position fixée par l'hôte.</summary>
    public void Build(HudRoot hud, bool embedded)
    {
        _window = new Panel();
        // Position par défaut : à droite de l'inventaire (épinglé
        // 455..966 × 260..611) — sous le bandeau tracker (fin ~y 316),
        // au-dessus du strip menu (y ≥ ~642) et du chip or. Taille =
        // contenu exact : titre 30 + 5 rangées de (36+12) px +
        // séparations 8 px.
        _window.Position = new Vector2(976, 320);
        _window.Size = new Vector2(3 * (CellSize + 12) + 24,
            5 * (CellSize + 12) + 4 * 8 + 40);
        _window.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.92f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var title = new Label { Text = UiTextEn.Translate("Equipment"),
            Position = new Vector2(10, 6) };
        title.AddThemeFontSizeOverride("font_size", 12);
        _window.AddChild(title);

        var grid = new GridContainer { Columns = 3,
            Position = new Vector2(12, 32) };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        var slotTex = hud.Region("main", "FunctionSlot");
        foreach (var (loc, label) in SlotDefs)
        {
            var cell = new EquipCell(loc, 0, label, slotTex,
                EeSkin.EmptySlot(hud))
            {
                CustomMinimumSize = new Vector2(CellSize, CellSize + 12),
            };
            cell.UnequipRequested += OnUnequip;
            cell.ItemDropped += OnItemDropped;
            _cells[(0, loc)] = cell;
            grid.AddChild(cell);
        }
        _window.AddChild(grid);
        AddChild(_window);
        if (embedded)
        {
            _window.Position = Vector2.Zero;
            _window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            title.Visible = false;
            grid.Position = new Vector2(4, 4);
        }
        else
        {
            DragHelper.EnsureTitleBar(_window, UiTextEn.Translate("Equipment"));
            DragHelper.Persist(_window, "equipment");
        }
        RefreshIcons();
    }

    /// <summary>Profil natif (EquipmentWnd_new2) : pas de fenêtre
    /// propre, les cellules sont posées par <see cref="Attach"/> sur
    /// les SlotImage du layout.</summary>
    public void BuildHeadless(HudRoot hud)
    {
        _headless = true;
        _hud = hud;
    }

    private bool _headless;
    private HudRoot? _hud;

    /// <summary>Pose une cellule de loc d'équipement dans le slot natif
    /// <paramref name="host"/> (plein cadre, fond transparent).
    /// <paramref name="container"/> : conteneur d'équipement natif —
    /// 0 = tenue portée (défaut), 2/3/4 = page d'ensemble sauvegardée
    /// (rangées Cloth/Leather/Armor du profil natif). Un appel sur un
    /// host déjà occupé remplace l'ancienne cellule (réassignation lors
    /// d'un changement de classe : la rangée « current gear » suit la
    /// licence d'armure).</summary>
    public void Attach(int loc, Control host, string label,
        int container = 0)
    {
        foreach (var c in host.GetChildren())
            if (c is EquipCell old)
            {
                _cells.Remove((old.Container, old.Loc));
                host.RemoveChild(old);
                old.QueueFree();
            }
        var cell = new EquipCell(loc, container, label, null, null,
            bare: true);
        cell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cell.UnequipRequested += OnUnequip;
        cell.ItemDropped += OnItemDropped;
        _cells[(container, loc)] = cell;
        host.AddChild(cell);
        RefreshIcons();
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            _session = s;
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[equip-panel] dispatch : {ex}");
            }
        }
        if (!_equipmentHooked)
        {
            _equipmentHooked = true;
            PlayerEquipment.Changed += OnEquipmentChanged;
            // Changement de classe / de style : la tenue de base change (c_class garde-robe).
            PlayerVitals.ClassChanged += () => Callable.From(ApplyBodyEquipment).CallDeferred();
            // 489 revient à chaque kill (XP de classe) : on ne refait le corps que si (classe, style) a bougé.
            ClassChangeDialog.ClassesChanged += () =>
            {
                if (CharVisual.LocalClass() != _bodyClassKey)
                    Callable.From(ApplyBodyEquipment).CallDeferred();
            };
            // État déjà connu à la pose du hook (311 arrivé avant la
            // construction du panneau).
            OnEquipmentChanged();
        }
        // --kuso=<suffix>:<0|1> — rejoue un clic de case ShowKuso*
        // du profil (banc visuel : toggle sans cliquer dans l'UI).
        if (_kusoPending is { } kuso)
        {
            _kusoPending = null;
            var parts = kuso.Split(':');
            var on = parts.Length < 2 || parts[1] != "0";
            HudPrefs.SetKusoVisible(parts[0], on);
            PlayerEquipment.RaiseChanged();
            GD.Print($"[kuso] {parts[0]} → {(on ? "visible" : "caché")}");
        }
    }

    public override void _ExitTree() =>
        PlayerEquipment.Changed -= OnEquipmentChanged;

    /// <summary>État porté changé (main thread — les Apply* de
    /// PlayerEquipment tournent dans la file _pending de GameScene) :
    /// icônes + visuel 3D.</summary>
    private void OnEquipmentChanged()
    {
        RefreshIcons();
        ApplyBodyEquipment();
    }

    /// <summary>Rafraîchit icônes/tooltips : conteneur 0 →
    /// <see cref="PlayerEquipment.Worn"/>, pages d'ensemble →
    /// <see cref="PlayerEquipment.CostumeAt"/>.</summary>
    private void RefreshIcons()
    {
        EnsureTable();
        foreach (var ((container, loc), cell) in _cells)
        {
            var itemId = container == 0
                ? (PlayerEquipment.Worn.TryGetValue(loc, out var id)
                    ? id : 0)
                : PlayerEquipment.CostumeAt(container, loc);
            cell.SetItem(itemId, itemId != 0 ? LookupDef(itemId) : null);
        }
    }

    // ---------- actions ----------

    /// <summary>Double-clic slot → déséquipe : NCID 7 (1,loc,page)→
    /// (0,−1,−1) ; NCID 8 mask seulement pour la tenue portée (le 282
    /// ne renvoie que le conteneur 0 — natif
    /// <c>BroadcastEquipmentAsync</c> filtre <c>BagId == 0</c>).</summary>
    private void OnUnequip(int loc, int container)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        _ = Send(s, ZoneItemActions.UnequipRequest(loc, container));
        if (container == 0)
            _ = Send(s, new ZoneUpdateEquipment
            {
                UpdateFlags = 1u << loc,
            });
    }

    /// <summary>Drop d'un item d'inventaire sur un slot équipe →
    /// NCID 7 (0,slot,sac)→(1,loc,page) + NCID 8 mask (conteneur 0
    /// seulement — le gate page est côté serveur : kind % 3 = poids
    /// d'armure de la page).</summary>
    private void OnItemDropped(int loc, int srcBag, int srcSlot,
        int container)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        _ = Send(s, new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = (short)srcSlot, SrcIndex = (short)srcBag,
            DstType = 1, DstSlot = (short)loc, DstIndex = (short)container,
        });
        if (container != 0) return;
        var def = InventoryWindow.LookupDef(
            InventoryWindow.ItemAt(srcBag, srcSlot)?.ItemId ?? 0);
        _ = Send(s, new ZoneUpdateEquipment
        {
            UpdateFlags = def != null
                ? ZoneItemActions.LocMask(def.EquipLoc) : (1u << loc),
        });
    }

    private static Task Send(EdenSession s, NetMessage msg) =>
        s.SendZoneAsync(msg, CancellationToken.None).ContinueWith(
            t => GD.PrintErr($"[equip-panel] {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);

    // ---------- visuel 3D ----------

    /// <summary>Applique l'équipement courant au corps du joueur
    /// (recherche paresseuse du nœud « Player » → enfant « Model » →
    /// GLB corps). Sans corps GLB (capsule repli) : no-op.</summary>
    private (int Class, int Style) _bodyClassKey;

    private void ApplyBodyEquipment()
    {
        if (_playerBody == null || !GodotObject.IsInstanceValid(_playerBody))
        {
            _playerBody = FindPlayerBody();
            if (_playerBody == null) return;
        }
        EnsureTable();
        var letter = CharVisual.BodyLetterOf(_playerBody);
        var (lcls, lsty) = CharVisual.LocalClass();
        _bodyClassKey = (lcls, lsty);
        // Loc 9 : aucun slot UI natif, aucun routage serveur
        // (ResolveEquipLocAsync) ni AllowedLocations client ne la
        // produit — mais CharVisual la projette sur « Bip01 Prop1 »
        // comme le loc 7. Une ligne fantôme y peignait une 2e arme
        // dans la main droite (loc 7 + 8 + 9 = trois armes visibles).
        // On la masque du visuel ; l'état _worn n'est pas touché.
        var shown = new Dictionary<int, int>(PlayerEquipment.VisualWorn(
            id => LookupDef(id)?.Kind ?? -1));
        shown.Remove(9);
        // Cases « ShowKuso* » du profil (préférence locale — le natif
        // les écrit dans options.ini sans paquet serveur) : seule la
        // case pilote l'affichage de la tête d'armure / cape / tenue.
        CharVisual.ApplyEquipment(_playerBody, letter, shown,
            LookupModel, lcls, lsty,
            attachVisible: HudPrefs.AttachVisibleForLoc,
            hideOutfit: !HudPrefs.KusoClothesVisible);
    }

    /// <summary>Corps GLB du joueur local (enfant Node3D sous
    /// Player/Model contenant des MeshInstance3D — la capsule de
    /// repli n'a pas de squelette/meshes #B).</summary>
    private Node3D? FindPlayerBody()
    {
        var player = GetTree().Root.FindChild("Player", true, false);
        var model = player?.FindChild("Model", true, false);
        if (model == null) return null;
        foreach (var c in model.GetChildren())
        {
            if (c is Node3D n && n
                    .FindChildren("*", "MeshInstance3D", true, false)
                    .Count > 0)
                return n;
        }
        return null;
    }

    /// <summary>Résolution itemId → (Code, ModelCode) pour la table
    /// 4pequip (Code prioritaire, ModelCode en repli — les deux clés
    /// cohabitent dans les lignes natives).</summary>
    private static (string Code, string ModelCode)? LookupModel(int itemId)
    {
        var def = LookupDef(itemId);
        return def == null ? null : (def.Code, def.ModelCode);
    }

    // ---------- table c_item partagée ----------

    private static void EnsureTable() => InventoryWindow.EnsureItemTable();
    private static ItemTable.ItemDef? LookupDef(int itemId) =>
        InventoryWindow.LookupDef(itemId);

    /// <summary>Une cellule de loc d'équipement : icône + libellé +
    /// cible de drop + double-clic déséquipe + tooltip riche.</summary>
    private sealed partial class EquipCell : Panel
    {
        private readonly int _loc;
        private readonly int _container;
        private readonly TextureRect _icon;
        private int _itemId;
        private ItemTable.ItemDef? _def;

        /// <summary>Loc d'équipement natif de la cellule.</summary>
        public int Loc => _loc;
        /// <summary>Conteneur d'équipement (0 = porté, 2-4 = pages).</summary>
        public int Container => _container;

        /// <summary>Double-clic / clic droit → déséquipe (loc, conteneur).</summary>
        public event Action<int, int>? UnequipRequested;
        /// <summary>Drop inventaire → (loc, sac, slot, conteneur).</summary>
        public event Action<int, int, int, int>? ItemDropped;

        public EquipCell(int loc, int container, string label,
            Texture2D? slotTex, StyleBox? emptySlot, bool bare = false)
        {
            _loc = loc;
            _container = container;
            AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            // Case fleur-de-lys native (36×36) au-dessus du libellé.
            var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            bg.OffsetBottom = -12;
            if (bare)
                bg.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            else if (emptySlot != null)
                bg.AddThemeStyleboxOverride("panel", emptySlot);
            else if (slotTex != null)
                bg.AddThemeStyleboxOverride("panel",
                    new StyleBoxTexture { Texture = slotTex });
            AddChild(bg);
            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _icon.OffsetBottom = -12;
            AddChild(_icon);
            var lbl = new Label
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            lbl.AddThemeFontSizeOverride("font_size", 8);
            lbl.AddThemeColorOverride("font_color",
                new Color(0.7f, 0.75f, 0.85f));
            lbl.Visible = !bare;
            if (bare)
            {
                bg.OffsetBottom = 0;
                _icon.OffsetBottom = 0;
            }
            AddChild(lbl);
            TooltipText = _label = label;
        }

        /// <summary>ItemId porté (0 = vide) + définition.</summary>
        private string _label = "";

        public void SetItem(int itemId, ItemTable.ItemDef? def)
        {
            _itemId = itemId;
            _def = def;
            Texture2D? tex = null;
            if (def != null && def.Code.Length > 0)
                tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
            _icon.Texture = tex;
            TooltipText = itemId != 0 ? "" : _label;
            ItemTooltip.Bind(this, () => _itemId == 0 ? null
                : new InventorySlot(0, _loc, _itemId, 0, 0, 0));
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton mb) return;
            // Clic gauche avec un objet en main = équiper dessus.
            if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (InventoryWindow.TakeHeld() is { } h)
                    ItemDropped?.Invoke(_loc, h.Bag, h.Slot, _container);
                AcceptEvent();
            }
            // Clic droit = déséquiper (le double-clic ne fait plus rien).
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.Right
                     && _itemId != 0)
            {
                UnequipRequested?.Invoke(_loc, _container);
                AcceptEvent();
            }
        }

        /// <summary>Cible de drop : accepte le payload item des
        /// cellules d'inventaire.</summary>
        public override bool _CanDropData(Vector2 atPosition,
            Variant data)
        {
            return data.VariantType == Variant.Type.Dictionary;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            if (d.TryGetValue("bag", out var bag) &&
                d.TryGetValue("slot", out var slot))
            {
                ItemDropped?.Invoke(_loc, bag.AsInt32(),
                    slot.AsInt32(), _container);
            }
        }

    }
}
