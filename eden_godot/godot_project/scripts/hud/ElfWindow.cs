using Godot;
using EdenClient.Data;
using EdenClient.Combat;
using EdenClient.Game;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « Familier » (PetWndV3 natif) — portrait, nom, niveau,
/// HP (« vitalité »), exp, stats de croissance, sorts de combat
/// (FightSKill) et d'intégration (PraySkill), slots d'équipement,
/// liste des elfs possédés et boutons d'action.
///
/// Actions C→S câblées (toutes prouvées côté serveur) :
///   - <b>Battle</b> (BtnBattle) : invoquer ↔ renvoyer —
///     <see cref="ZoneElfWork"/> 107 [loc][2] / [loc][0] ;
///   - <b>Pray</b> (BtnPray) : intégrer (mix) — 107 [loc][1] ;
///   - <b>Release</b> (BtnRelease) : libérer — 108 [loc][0][-1][-1] ;
///   - <b>Seal</b> (BtnSeal) : sceller en œuf (10k or) —
///     108 [loc][1][-1][-1] ;
///   - <b>Rename</b> (BtnChangeName) : 228 [loc][nom] (parchemin
///     40056/40156 exigé côté serveur) ;
///   - <b>Feed</b> (BtnFeed) : 229 ElfRevive — chemin serveur assumé
///     (mort en natif) ; <b>Caress</b> : 137 ElfTalk (idem) ;
///   - <b>Passive</b> : 187 PetModeChange [2] (passif) / [0] (assiste) ;
///   - <b>Attack</b> / clic sur sort de combat : 188 SendPetCmd
///     instr 1 / 101..105 sur la cible courante.
///
/// Faim/humeur : le snapshot 412 natif ne transporte aucun champ
/// d'humeur/satiété — la fenêtre n'en affiche donc pas (zone
/// « ItemMall » native présentée mais le bouton feed suit le chemin
/// serveur documenté).
/// </summary>
public partial class ElfWindow : Control
{
    /// <summary>Action demandée par un bouton — remontée au bridge.
    /// (action, loc, valeur texte pour rename).</summary>
    public event Action<string, int, string>? ActionRequested;

    private Control? _window;
    private ElfBook? _book;
    private int _selectedLoc = -1;
    private AtlasTexture? _slotTex;

    // Widgets natifs résolus (feuilles du PetWndV3).
    private Control? _iconBox;
    private Label? _nameLab, _levelLab, _hpLab;
    private Label? _attrTextLab, _attrValueLab;
    private Label? _amountLab;
    private ProgressBar? _expBar;
    private readonly List<Control> _fightSlots = new();
    private readonly List<Control> _praySlots = new();
    private readonly List<Control> _equipSlots = new();
    private VBoxContainer? _listBox;
    private Label? _statusLab;
    private LineEdit? _renameEdit;
    private Control? _renamePop;

    /// <summary>Attache le modèle et redessine.</summary>
    public void SetBook(ElfBook book)
    {
        _book = book;
        if (_selectedLoc < 0 && book.Elfs.Count > 0)
            _selectedLoc = book.Elfs[0].Loc;
        book.Changed += Refresh;
        Refresh();
    }

    public void Build(HudRoot hud)
    {
        _slotTex = hud.Region("main", "FunctionSlot");
        _window = hud.Build("PetWndV3.layout");
        if (_window != null)
        {
            AddChild(_window);
            // FrameWindow 386×~500 native ancrée en (0,0) sur sa
            // résolution d'origine — taille fixe centrée.
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -384, -283, 384, 283);
            DragHelper.EnsureTitleBar(_window, "Familiar");
            RenameNativeLabels();
            WireWidgets();
        }
        EnsureFallbackChrome();
        DragHelper.Persist(_window ?? this, "elf");
        Visible = false;
    }

    /// <summary>Vrai si le layout natif PetWndV3 a été instancié.</summary>
    public bool HasNativeLayout => _window != null &&
        _window.HasMeta("cegui_target");

    // -------------------- rendu -----------------------------------

    /// <summary>Redessine la fenêtre depuis <see cref="_book"/>.</summary>
    public void Refresh()
    {
        if (_book == null || _window == null) return;
        var book = _book;
        // Auto-sélection : elf invoqué > 1er de la liste.
        if (_selectedLoc < 0 || book.ByLoc(_selectedLoc) == null)
            _selectedLoc = book.Summoned?.Loc
                ?? (book.Elfs.Count > 0 ? book.Elfs[0].Loc : -1);
        var sel = book.ByLoc(_selectedLoc);

        if (_amountLab != null)
            _amountLab.Text = $"{book.Elfs.Count}/10";
        if (_statusLab != null)
            _statusLab.Text = sel == null
                ? UiTextEn.Translate("No familiar — use an egg item (kind 60) to add one.")
                : StatusText(sel);
        RenderList(book);
        RenderDetail(sel, book);
    }

    private void RenderList(ElfBook book)
    {
        if (_listBox == null) return;
        foreach (var c in _listBox.GetChildren()) c.QueueFree();
        foreach (var e in book.Elfs)
        {
            var loc = e.Loc;
            var b = new Button
            {
                Text = UiTextEn.Translate($"  {DisplayName(e)}  Lv{e.Level}") +
                       StatusSuffix(e),
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                ButtonPressed = loc == _selectedLoc,
            };
            b.AddThemeFontSizeOverride("font_size", 11);
            if (PartnerTable.Icon(e.TemplateId) is { } ic)
            {
                // L'icône s'affiche via un petit TextureRect accolé.
                b.Icon = ic;
                b.ExpandIcon = false;
            }
            b.Pressed += () =>
            {
                _selectedLoc = loc;
                Refresh();
            };
            _listBox.AddChild(b);
        }
    }

    private void RenderDetail(ElfRecord? sel, ElfBook book)
    {
        // Portrait / nom / niveau / vitalité / exp.
        if (_iconBox != null)
        {
            foreach (var c in _iconBox.GetChildren()
                         .Where(n => n.Name == "_icon"))
                c.QueueFree();
            var tex = sel != null
                ? PartnerTable.Icon(sel.TemplateId) : null;
            if (tex != null)
            {
                var icon = new TextureRect
                {
                    Name = "_icon", Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode =
                        TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                _iconBox.AddChild(icon);
            }
        }
        if (_nameLab != null)
            _nameLab.Text = sel == null ? "—" : DisplayName(sel);
        if (_levelLab != null)
            _levelLab.Text = sel == null ? "" : UiTextEn.Translate($"Lv {sel.Level}");
        if (_hpLab != null)
        {
            var maxHp = sel != null
                ? Math.Max(PartnerTable.Get(sel.TemplateId)?.MaxHp ?? 0,
                           sel.Hp) : 0;
            _hpLab.Text = sel == null
                ? "" : UiTextEn.Translate($"Vitality {sel.Hp}/{maxHp}");
        }
        if (_expBar != null && sel != null)
        {
            // Pas de seuil petExp côté client — on borne visuellement
            // l'exp sur 32 bits signé (le serveur la borne au seuil).
            _expBar.MinValue = 0;
            _expBar.MaxValue = Math.Max(1, sel.Exp + sel.Exp / 10 + 1);
            _expBar.Value = sel.Exp;
            _expBar.TooltipText = UiTextEn.Translate($"Exp {sel.Exp}");
        }
        // Stats de croissance (Grow[0..3] = atk/hit/crit/atkspd index)
        // + valeurs de combat actualisées par les 418 types 8-11.
        var statLabels = new[] { UiTextEn.Translate("ATK"), UiTextEn.Translate("ACC"), UiTextEn.Translate("CRIT"), UiTextEn.Translate("SPD") };
        for (var i = 0; i < 4; i++)
        {
            var v = TopLeftVitals.Find<Label>(_window, $"N{i + 1}");
            var g = TopLeftVitals.Find<Control>(_window, $"G{i + 1}");
            if (v != null)
                v.Text = sel == null ? "" :
                    PetClientRules.GradeLetter(sel.Grow[i]) +
                    (book.CombatStats[i] > 0 ? $" ({book.CombatStats[i]})" : "");
            if (g != null) g.Visible = sel != null && sel.Grow[i] > 0;
            var t = TopLeftVitals.Find<Label>(_window, $"T{i + 1}");
            if (t != null) t.Text = statLabels[i];
        }
        if (_attrTextLab != null) _attrTextLab.Text = UiTextEn.Translate("Mix★");
        if (_attrValueLab != null)
            _attrValueLab.Text = sel == null ? "" : $"{sel.MixStar}";

        FillSkills(_fightSlots, sel?.Battle, book, true, sel);
        FillSkills(_praySlots, sel?.Integrate, book, false, sel);
        FillEquip(sel);
    }

    private void FillSkills(List<Control> slots, int[]? ids,
        ElfBook book, bool clickable, ElfRecord? sel)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            foreach (var c in slot.GetChildren()
                         .Where(n => n.Name.ToString().StartsWith("_sk")))
                c.QueueFree();
            var id = ids != null && i < ids.Length ? ids[i] : 0;
            if (id <= 0) { slot.TooltipText = ""; continue; }
            var name = SpellCatalog.Name(id);
            slot.TooltipText = clickable
                ? $"{name} — clic : ordre de lancer (cible = mob visé)"
                : name;
            var tex = SpellCatalog.Icon(id);
            if (tex != null)
            {
                var icon = new TextureRect
                {
                    Name = $"_sk{i}", Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode =
                        TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                slot.AddChild(icon);
            }
            if (clickable && sel != null && sel.Status == 2)
            {
                var btn = new Button
                {
                    Name = $"_skb{i}", Text = "",
                    Flat = true, MouseFilter = MouseFilterEnum.Stop,
                    TooltipText = slot.TooltipText,
                };
                btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                var instr = 101 + i;
                var loc = sel.Loc;
                btn.Pressed += () =>
                    ActionRequested?.Invoke("petcmd", loc,
                        instr.ToString());
                slot.AddChild(btn);
            }
        }
    }

    private void FillEquip(ElfRecord? sel)
    {
        for (var i = 0; i < _equipSlots.Count; i++)
        {
            var slot = _equipSlots[i];
            foreach (var c in slot.GetChildren()
                         .Where(n => n.Name.ToString().StartsWith("_eq")))
                c.QueueFree();
            slot.TooltipText = "";
            var it = sel == null ? null
                : _book?.EquipOf(sel.Loc)
                     .FirstOrDefault(x => x.Slot == i);
            if (it == null) continue;
            var def = InventoryWindow.LookupDef(it.ItemId);
            if (def?.IconCode is { Length: > 0 } code)
            {
                var tex = AssetCatalog.GetTexture($"UI/itemicon/{code}.png");
                if (tex != null)
                {
                    var icon = new TextureRect
                    {
                        Name = "_eq", Texture = tex,
                        ExpandMode =
                            TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum
                            .KeepAspectCentered,
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    icon.SetAnchorsAndOffsetsPreset(
                        LayoutPreset.FullRect);
                    slot.AddChild(icon);
                }
            }
            var elfId = it.ItemId;
            ItemTooltip.BindId(slot, () => elfId);
        }
    }

    // -------------------- helpers ---------------------------------

    /// <summary>Nom affiché : nom donné sinon t_partner/col3.</summary>
    public static string DisplayName(ElfRecord e) =>
        e.Name.Length > 0 ? e.Name : PartnerTable.NameOf(e.TemplateId);

    private static string StatusSuffix(ElfRecord e) => e.Status switch
    {
        2 => "   [summoned]",
        1 => "   [praying]",
        >= 7 and <= 9 => "   [lottery]",
        _ => "",
    };

    private static string StatusText(ElfRecord e) => e.Status switch
    {
        2 => "Summoned — fighting beside you.",
        1 => "Praying (integration slot).",
        >= 7 and <= 9 => "In a lottery.",
        _ => "Resting.",
    };

    // -------------------- construction ----------------------------

    /// <summary>Relie les widgets natifs + injecte la liste d'elfs.</summary>
    private void WireWidgets()
    {
        _iconBox = TopLeftVitals.Find<Control>(_window, "Icon");
        _nameLab = TopLeftVitals.Find<Label>(_window, "Name");
        _levelLab = TopLeftVitals.Find<Label>(_window, "FloatLevelText");
        _hpLab = TopLeftVitals.Find<Label>(_window, "HpText");
        _attrTextLab = TopLeftVitals.Find<Label>(_window, "AttrText");
        _attrValueLab = TopLeftVitals.Find<Label>(_window, "AttrValue");
        _amountLab = TopLeftVitals.Find<Label>(_window, "Amount");
        _expBar = TopLeftVitals.Find<ProgressBar>(_window, "ExpBar");

        for (var i = 1; i <= 5; i++)
        {
            var fight = FindSlot($"FightSKill/S{i}") ??
                FindNth(_window, $"S{i}", "FightSKill");
            if (fight != null) _fightSlots.Add(fight);
            var pray = FindSlot($"PraySkill/S{i}") ??
                FindNth(_window, $"S{i}", "PraySkill");
            if (pray != null) _praySlots.Add(pray);
        }
        for (var i = 1; i <= 4; i++)
        {
            var eq = FindSlot($"Equipment/Slot{i}");
            if (eq != null)
            {
                _equipSlots.Add(eq);
                WireEquipSlot(eq, i - 1);
            }
        }

        // Liste des elfs : ScrollablePane natif → VBox de boutons.
        var pane = _window?
            .FindChildren("*", "ScrollContainer", true, false)
            .Cast<ScrollContainer>()
            .FirstOrDefault(s => s.Name.ToString()
                .EndsWith("ScrollablePane"));
        if (pane != null)
        {
            _listBox = new VBoxContainer();
            _listBox.AddThemeConstantOverride("separation", 2);
            _listBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            pane.AddChild(_listBox);
        }

        // Boutons natifs → actions.
        WireButton("BtnBattle", () => SendAction("battle"));
        WireButton("BtnPray", () => SendAction("pray"));
        WireButton("BtnRelease", () => SendAction("release"));
        WireButton("BtnSeal", () => SendAction("seal"));
        WireButton("BtnFeed", () => SendAction("feed"));
        WireButton("BtnUpgrade", ShowFusePopup);
        WireButton("BtnChangeName", ShowRenamePopup);
        WireButton("LotteryBtn", () => { }); // loterie non portée
        WireButton("HelpBtn", () => { });
        // Boutons ajoutés : passif, caresse, attaque (le layout natif
        // n'a pas de widget dédié — PetFunctionWnd les porte sur le
        // menu contextuel du pet).
        var aux = new HBoxContainer { Name = "_aux" };
        aux.AddThemeConstantOverride("separation", 4);
        _window?.AddChild(aux);
        var where = FindDataAnchor();
        aux.Position = where;
        aux.AddChild(MakeAuxButton(UiTextEn.Translate("Passive"), () => SendAction("passive")));
        aux.AddChild(MakeAuxButton(UiTextEn.Translate("Caress"), () => SendAction("caress")));
        aux.AddChild(MakeAuxButton(UiTextEn.Translate("Attack"), () => SendAction("attack")));
        _statusLab = new Label { Name = "_status", Text = "" };
        _statusLab.AddThemeFontSizeOverride("font_size", 10);
        _statusLab.Position = where + new Vector2(0, 26);
        _statusLab.Size = new Vector2(200, 20);
        _window?.AddChild(_statusLab);
    }

    /// <summary>Slot de gear pet : cible de drop d'un item de sac
    /// (kind 81, paire d'EquipLoc — règle serveur ElfEquip) ou d'un
    /// autre slot gear (swap adjacent), clic droit = déséquiper.
    /// Envoie SwitchItemLocation dstType 6 via le pont.</summary>
    private void WireEquipSlot(Control slot, int elfSlot)
    {
        slot.MouseFilter = MouseFilterEnum.Stop;
        var canDrop = Callable.From((Vector2 _, Variant data) =>
            CanDropOnGear(data, elfSlot));
        var drop = Callable.From((Vector2 _, Variant data) =>
            DropOnGear(data, elfSlot));
        var drag = Callable.From((Vector2 _) => DragGear(slot, elfSlot));
        slot.SetDragForwarding(drag, canDrop, drop);
        slot.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Right } &&
                _book?.EquipOf(_selectedLoc)
                    .Any(x => x.Slot == elfSlot) == true)
            {
                ActionRequested?.Invoke("petunequip", _selectedLoc,
                    elfSlot.ToString());
                slot.AcceptEvent();
            }
        };
    }

    private Variant DragGear(Control slot, int elfSlot)
    {
        var it = _book?.EquipOf(_selectedLoc)
            .FirstOrDefault(x => x.Slot == elfSlot);
        if (it == null) return default;
        var d = new Godot.Collections.Dictionary
        {
            ["src"] = "petgear", ["elfSlot"] = elfSlot,
            ["elfLoc"] = _selectedLoc, ["itemId"] = it.ItemId,
        };
        var def = InventoryWindow.LookupDef(it.ItemId);
        if (def?.IconCode is { Length: > 0 } code &&
            AssetCatalog.GetTexture($"UI/itemicon/{code}.png") is { } tex)
            slot.SetDragPreview(new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(32, 32),
            });
        return d;
    }

    private bool CanDropOnGear(Variant data, int elfSlot)
    {
        if (_selectedLoc < 0 ||
            data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.TryGetValue("src", out var src) &&
            src.AsString() == "petgear")
        {
            var from = d["elfSlot"].AsInt32();
            return d["elfLoc"].AsInt32() == _selectedLoc && from != elfSlot
                && (from ^ 1) == elfSlot;   // 0↔1 / 2↔3
        }
        if (!d.ContainsKey("bag") || !d.ContainsKey("slot") ||
            !d.ContainsKey("itemId")) return false;
        var def = InventoryWindow.LookupDef(d["itemId"].AsInt32());
        return def != null && PetClientRules.CanEquip(def.Kind,
            def.EquipLoc, elfSlot);
    }

    private void DropOnGear(Variant data, int elfSlot)
    {
        var d = data.AsGodotDictionary();
        if (d.TryGetValue("src", out var src) &&
            src.AsString() == "petgear")
        {
            ActionRequested?.Invoke("petswap", _selectedLoc,
                $"{d["elfSlot"].AsInt32()},{elfSlot}");
            return;
        }
        ActionRequested?.Invoke("petequip", _selectedLoc,
            $"{d["bag"].AsInt32()},{d["slot"].AsInt32()},{elfSlot}");
    }

    /// <summary>Position en bas du panneau Data (là où le natif met
    /// ses boutons) — depuis le bouton BtnRelease s'il existe.</summary>
    private Vector2 FindDataAnchor()
    {
        var b = TopLeftVitals.Find<Control>(_window, "BtnChangeName");
        if (b != null)
            return b.Position + new Vector2(0, b.Size.Y + 6);
        return new Vector2(8, 470);
    }

    private Button MakeAuxButton(string text, Action act)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", 10);
        b.Pressed += () => act();
        return b;
    }

    private void WireButton(string leaf, Action act)
    {
        var b = TopLeftVitals.Find<BaseButton>(_window, leaf);
        if (b == null) return;
        b.Pressed += () => act();
    }

    /// <summary>SlotImage sous un parent précis (les « S1..S5 » se
    /// répètent entre FightSKill et PraySkill — FindChild seul
    /// renverrait le premier trouvé).</summary>
    private Control? FindSlot(string path)
    {
        var leaf = path[(path.LastIndexOf('/') + 1)..];
        var parentPath = path[..path.LastIndexOf('/')];
        var parentLeaf = parentPath[(parentPath.LastIndexOf('/') + 1)..];
        var parent = TopLeftVitals.Find<Control>(_window, parentLeaf);
        return parent?.FindChildren("*", "Control", true, false)
            .Cast<Control>()
            .FirstOrDefault(c =>
            {
                var n = c.Name.ToString();
                return n == leaf || n.EndsWith("_" + leaf);
            });
    }

    /// <summary>Repli : trouve le n-ième contrôle « leaf » dans la
    /// zone du parent nommé.</summary>
    private Control? FindNth(Control? root, string leaf, string parentLeaf)
    {
        var parent = TopLeftVitals.Find<Control>(root, parentLeaf);
        return parent?.FindChildren("*", "Control", true, false)
            .Cast<Control>()
            .FirstOrDefault(c =>
            {
                var n = c.Name.ToString();
                return n == leaf || n.EndsWith("_" + leaf);
            });
    }

    /// <summary>Traduit les libellés Big5 du layout en anglais
    /// (même convention que ClassWindow.RenameLabels). Les feuilles
    /// passées à <see cref="TopLeftVitals.Find{T}"/> utilisent le nom
    /// sanitisé ('_' — CeguiBuilder.SanitizeName), ex.
    /// « FightSKill_Title » matche « Root_Pet_…_FightSKill_Title ».
    /// </summary>
    private void RenameNativeLabels()
    {
        void SetText(string leaf, string text)
        {
            var l = TopLeftVitals.Find<Label>(_window, leaf);
            if (l != null) l.Text = text;
        }
        void SetBtn(string leaf, string text)
        {
            var b = TopLeftVitals.Find<BaseButton>(_window, leaf);
            if (b is Button btn) btn.Text = text;
        }
        SetText("Data_Title", UiTextEn.Translate("Pet Info"));
        SetText("List_Title", UiTextEn.Translate("Pet List"));
        SetText("FightSKill_Title", UiTextEn.Translate("Battle"));
        SetText("PraySkill_Title", UiTextEn.Translate("Pray"));
        SetText("Equipment_Title", UiTextEn.Translate("Pet Gear"));
        SetText("ItemMall_Title", UiTextEn.Translate("Pet Lunch Box"));
        SetText("Value_N1", UiTextEn.Translate("ATK/M-ATK")); SetText("Value_N2", UiTextEn.Translate("ACC"));
        SetText("Value_N3", UiTextEn.Translate("CRIT/M-CRIT")); SetText("Value_N4", UiTextEn.Translate("ATK SPD"));
        SetText("AttrText", UiTextEn.Translate("Attack Attribute"));
        SetBtn("BtnBattle", UiTextEn.Translate("Battle"));
        SetBtn("BtnPray", UiTextEn.Translate("Pray"));
        SetBtn("BtnUpgrade", UiTextEn.Translate("Advanced"));
        SetBtn("BtnFeed", UiTextEn.Translate("Feed"));
        SetBtn("BtnChangeName", UiTextEn.Translate("Rename"));
        SetBtn("BtnRelease", UiTextEn.Translate("Release"));
        SetBtn("BtnSeal", UiTextEn.Translate("Seal"));
        SetBtn("LotteryBtn", UiTextEn.Translate("Lottery"));
        SetText("HpText", "");
        // Libellés sur parchemin : encre sombre (ref PetsPanel).
        var ink = new Color(0.12f, 0.11f, 0.1f);
        foreach (var leaf in new[] { "Data_Title", "List_Title",
                     "FightSKill_Title", "PraySkill_Title", "Equipment_Title",
                     "Value_N1", "Value_N2", "Value_N3", "Value_N4",
                     "Value_T1", "Value_T2", "Value_T3", "Value_T4",
                     "AttrText", "AttrValue", "FloatLevelText" })
            if (TopLeftVitals.Find<Label>(_window, leaf) is { } l)
                l.AddThemeColorOverride("font_color", ink);
    }

    /// <summary>Popup de fusion (227 ElfMix) : l'elf sélectionné
    /// reçoit la fusion ; l'utilisateur choisit l'elf consommé dans
    /// la liste. Pas de catalyseur (−1/−1) — le serveur accepte la
    /// voie sans item (CPetUpgradeOp natif).</summary>
    private void ShowFusePopup()
    {
        var sel = _book?.ByLoc(_selectedLoc);
        var others = _book?.Elfs
            .Where(e => e.Loc != _selectedLoc && e.Status is 0 or 1)
            .ToList() ?? new List<ElfRecord>();
        if (sel == null || others.Count == 0)
        {
            if (_statusLab != null)
                _statusLab.Text = sel == null
                    ? UiTextEn.Translate("No familiar selected.")
                    : UiTextEn.Translate("No other resting familiar to fuse into it.");
            return;
        }
        var pop = new Panel { Name = "_fuse" };
        pop.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        pop.Position = new Vector2(-120, -50);
        pop.Size = new Vector2(240, 60 + others.Count * 26);
        pop.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.98f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var lab = new Label
        {
            Text = UiTextEn.Translate($"Fuse into {DisplayName(sel)} (consumed):"),
            Position = new Vector2(8, 6),
        };
        lab.AddThemeFontSizeOverride("font_size", 10);
        pop.AddChild(lab);
        var y = 30;
        foreach (var e in others)
        {
            var loc = e.Loc;
            var b = new Button
            {
                Text = UiTextEn.Translate($"{DisplayName(e)} Lv{e.Level} (loc {loc})"),
                Position = new Vector2(8, y),
                Size = new Vector2(224, 22),
            };
            b.AddThemeFontSizeOverride("font_size", 10);
            b.Pressed += () =>
            {
                SendAction("fuse", loc.ToString());
                pop.QueueFree();
            };
            pop.AddChild(b);
            y += 26;
        }
        var cancel = new Button { Text = UiTextEn.Translate("Cancel"),
            Position = new Vector2(8, y), Size = new Vector2(224, 20) };
        cancel.AddThemeFontSizeOverride("font_size", 10);
        cancel.Pressed += () => pop.QueueFree();
        pop.AddChild(cancel);
        _window?.AddChild(pop);
    }

    /// <summary>Popup de renommage (228 ElfChangeName).</summary>
    private void ShowRenamePopup()
    {
        if (_renamePop != null)
        {
            _renamePop.Visible = true;
            _renameEdit?.GrabFocus();
            return;
        }
        var pop = new Panel { Name = "_rename" };
        pop.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        pop.Position = new Vector2(-110, -40);
        pop.Size = new Vector2(220, 80);
        pop.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.98f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var lab = new Label { Text = UiTextEn.Translate("New name:"), Position = new Vector2(8, 6) };
        pop.AddChild(lab);
        _renameEdit = new LineEdit
        {
            Position = new Vector2(8, 26),
            Size = new Vector2(204, 24),
            PlaceholderText = UiTextEn.Translate("name…"), MaxLength = 24,
        };
        pop.AddChild(_renameEdit);
        var ok = new Button { Text = UiTextEn.Translate("OK"),
            Position = new Vector2(56, 54), Size = new Vector2(50, 22) };
        ok.Pressed += () =>
        {
            if (_renameEdit != null && _renameEdit.Text.Trim().Length > 0)
                SendAction("rename", _renameEdit.Text.Trim());
            pop.Visible = false;
        };
        pop.AddChild(ok);
        var cancel = new Button { Text = UiTextEn.Translate("Cancel"),
            Position = new Vector2(114, 54), Size = new Vector2(56, 22) };
        cancel.Pressed += () => pop.Visible = false;
        pop.AddChild(cancel);
        _window?.AddChild(pop);
        _renamePop = pop;
        _renameEdit.GrabFocus();
    }

    private void SendAction(string action, string arg = "")
    {
        var sel = _book?.ByLoc(_selectedLoc);
        var loc = sel?.Loc ?? _selectedLoc;
        ActionRequested?.Invoke(action, loc, arg);
    }

    /// <summary>Chrome de repli quand la fenêtre native n'a pas été
    /// construite (panneau sombre + barre).</summary>
    private void EnsureFallbackChrome()
    {
        if (_window != null) return;
        var p = new Panel { Name = "PetWndV3" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-220, -280);
        p.Size = new Vector2(440, 560);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        DragHelper.EnsureTitleBar(p, "Familiar");
        AddChild(p);
        _window = p;
        BuildFallbackBody(p);
    }

    /// <summary>Corps de repli sans uidata : liste + détail + boutons.</summary>
    private void BuildFallbackBody(Control root)
    {
        var split = new HBoxContainer { Name = "_split" };
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        split.OffsetTop = 28;
        split.OffsetLeft = 8; split.OffsetRight = -8;
        split.OffsetBottom = -8;
        root.AddChild(split);

        _listBox = new VBoxContainer
        {
            Name = "_list",
            CustomMinimumSize = new Vector2(150, 0),
        };
        _listBox.AddThemeConstantOverride("separation", 2);
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(_listBox);
        split.AddChild(scroll);

        var right = new VBoxContainer { Name = "_detail" };
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", 4);
        split.AddChild(right);

        var top = new HBoxContainer();
        _iconBox = new Panel
        {
            CustomMinimumSize = new Vector2(48, 48),
        };
        _iconBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        { BgColor = new Color(0.116f, 0.112f, 0.1f, 0.5f) });
        top.AddChild(_iconBox);
        var nv = new VBoxContainer();
        _nameLab = new Label { Text = "—" };
        _levelLab = new Label { Text = "" };
        _hpLab = new Label { Text = "" };
        nv.AddChild(_nameLab); nv.AddChild(_levelLab); nv.AddChild(_hpLab);
        top.AddChild(nv);
        right.AddChild(top);
        _expBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 10) };
        right.AddChild(_expBar);

        var statsGrid = new GridContainer { Columns = 4 };
        foreach (var t in new[] { UiTextEn.Translate("ATK"), "HIT", UiTextEn.Translate("CRIT"), "SPD" })
        {
            var cell = new VBoxContainer();
            var lab = new Label { Text = t, HorizontalAlignment =
                HorizontalAlignment.Center };
            lab.AddThemeFontSizeOverride("font_size", 9);
            cell.AddChild(lab);
            statsGrid.AddChild(cell);
        }
        right.AddChild(statsGrid);
        var statVals = new GridContainer { Columns = 4 };
        for (var i = 1; i <= 4; i++)
        {
            var v = new Label { Name = $"_fN{i}", HorizontalAlignment =
                HorizontalAlignment.Center };
            statVals.AddChild(v);
            // Pont nommé N1..N4 pour RenderDetail (fallback : on pose
            // des labels N1..N4 dans l'arbre via un conteneur caché).
            v.Name = $"N{i}_x";
        }
        right.AddChild(statVals);

        var fTitle = new Label { Text = UiTextEn.Translate("Battle Skills") };
        right.AddChild(fTitle);
        var fightGrid = new GridContainer { Columns = 5 };
        for (var i = 0; i < 5; i++)
        {
            var s = MakeSlot();
            _fightSlots.Add(s); fightGrid.AddChild(s);
        }
        right.AddChild(fightGrid);
        var pTitle = new Label { Text = UiTextEn.Translate("Pray Skills") };
        right.AddChild(pTitle);
        var prayGrid = new GridContainer { Columns = 5 };
        for (var i = 0; i < 5; i++)
        {
            var s = MakeSlot();
            _praySlots.Add(s); prayGrid.AddChild(s);
        }
        right.AddChild(prayGrid);
        var eTitle = new Label { Text = UiTextEn.Translate("Equipment") };
        right.AddChild(eTitle);
        var eqGrid = new GridContainer { Columns = 4 };
        for (var i = 0; i < 4; i++)
        {
            var s = MakeSlot();
            _equipSlots.Add(s); eqGrid.AddChild(s);
        }
        right.AddChild(eqGrid);
        _statusLab = new Label { Text = "" };
        _statusLab.AddThemeFontSizeOverride("font_size", 10);
        right.AddChild(_statusLab);

        var btnRow = new GridContainer { Columns = 4 };
        foreach (var (label, act) in new (string, string)[]
        {
            ("Summon", "battle"), (UiTextEn.Translate("Pray"), "pray"),
            (UiTextEn.Translate("Release"), "release"), (UiTextEn.Translate("Seal"), "seal"),
            (UiTextEn.Translate("Rename"), "rename"), (UiTextEn.Translate("Feed"), "feed"),
            (UiTextEn.Translate("Caress"), "caress"), (UiTextEn.Translate("Attack"), "attack"),
            (UiTextEn.Translate("Passive"), "passive"), ("Fuse", "fuse"),
        })
        {
            var b = new Button { Text = label };
            b.AddThemeFontSizeOverride("font_size", 10);
            var a = act;
            if (a == "rename")
                b.Pressed += ShowRenamePopup;
            else if (a == "fuse")
                b.Pressed += ShowFusePopup;
            else
                b.Pressed += () => SendAction(a);
            btnRow.AddChild(b);
        }
        right.AddChild(btnRow);
        _amountLab = new Label { Text = "" };
        right.AddChild(_amountLab);
    }

    private Panel MakeSlot()
    {
        var cell = new Panel { CustomMinimumSize = new Vector2(36, 36) };
        cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        { BgColor = new Color(0.116f, 0.112f, 0.1f, 0.5f) });
        if (_slotTex != null)
        {
            var bg = new TextureRect
            {
                Texture = _slotTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            cell.AddChild(bg);
        }
        return cell;
    }
}
