using Godot;
using EdenClient.Data;
using EdenClient.Combat;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Barre d'actions du familier invoqué — <c>PetFunctionWnd5p.layout</c>
/// natif (6 slots + case PetMode + cadenas + ×), affichée uniquement
/// quand un familier est invoqué (<see cref="ElfBook.Summoned"/>).
///
/// Sémantique native prouvée (rf_game/decompiled.c
/// <c>FUN_0088f0b0</c> — handler de clic des slots) :
///   - Slot1 → <see cref="ZoneSendPetCmd"/> instr 1 (ordre d'attaque
///     sur la cible courante) ;
///   - Slot2..Slot6 → instrs 101..105 = CastSpell(battle[i-1], cible) ;
///   - PetMode (ImageOnlyCheckbox) → <see cref="ZonePetModeChange"/>
///     187 : coché (PetAttackMode) = mode 1 combat, décoché
///     (PetCollectMode) = mode 2 passif/loot — cf.
///     <c>HandlePetModeChangeAsync</c> serveur (textes 0xb30/0xb31).
/// LockBtn et CloseBtn sont locaux (verrou de position / masquage).
/// </summary>
public partial class PetFunctionBar : Control
{
    /// <summary>Action remontée au bridge (« petcmd », instr en
    /// arg ; « passive », mode en arg).</summary>
    public event Action<string, int, string>? ActionRequested;

    private Control? _window;
    private ElfBook? _book;
    private AtlasTexture? _attackTex;
    private readonly List<Control> _slots = new();
    private CheckBox? _modeBox;

    /// <summary>Attache le livre + montre la barre si un pet est
    /// invoqué (appelé par <see cref="ElfBridge"/>).</summary>
    public void SetBook(ElfBook book)
    {
        if (_book != null) _book.Changed -= Refresh;
        _book = book;
        book.Changed += Refresh;
        Refresh();
    }

    public void Build(HudRoot hud)
    {
        _attackTex = hud.Region("main", "AttackIcon");
        _window = hud.Build("PetFunctionWnd5p.layout")
                  ?? hud.Build("PetFunctionWnd.layout");
        if (_window != null)
        {
            AddChild(_window);
            // UARECT natif {{0.37875,0},{1,-180},{0.37875,265},
            // {1,-135}} — barre 265×45 au-dessus de la hotbar.
            HudRoot.Pin(_window, 0.37875f, 1f, 0.37875f, 1f,
                0, -222, 265, -177);   // B6 : au-dessus de la castbar (-170..-152)
            WireNative();
        }
        else
        {
            BuildFallback();
        }
        Visible = false;
    }

    // -------------------- rendu -----------------------------------

    /// <summary>Re-peint les slots depuis le familier invoqué et
    /// bascule la visibilité (513 → ElfBook.Summoned).</summary>
    public void Refresh()
    {
        var pet = _book?.Summoned;
        Visible = pet != null;
        if (_window == null) return;
        var spells = pet?.Battle;
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            foreach (var c in slot.GetChildren()
                         .Where(n => n.Name.ToString().StartsWith("_pf")))
            {
                // Libère le nom AVANT le free — sinon le nœud mort
                // conserve « _pfb0 » et Godot renomme le remplaçant
                // en @Button@<id> (bouton intraçable au dump).
                c.Name = $"_pf_dead{c.GetInstanceId()}";
                c.QueueFree();
            }
            if (pet == null) continue;
            if (i == 0)
            {
                // Slot1 = ordre d'attaque (instr 1) — icône
                // AttackIcon de l'atlas main.
                slot.TooltipText =
                    UiTextEn.Translate("Attack — order the familiar to hit the target");
                if (_attackTex != null)
                    AddIcon(slot, _attackTex, i);
                AddClick(slot, i, 1);
                continue;
            }
            var id = spells != null && i - 1 < spells.Length
                ? spells[i - 1] : 0;
            if (id <= 0) continue;
            var name = SpellCatalog.Name(id);
            slot.TooltipText = UiTextEn.Translate($"{name} — cast on current target");
            var tex = SpellCatalog.Icon(id);
            if (tex != null) AddIcon(slot, tex, i);
            AddClick(slot, i, 101 + i - 1);
        }
    }

    private void AddIcon(Control slot, Texture2D tex, int i)
    {
        var icon = new TextureRect
        {
            Name = $"_pf{i}", Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        slot.AddChild(icon);
    }

    private void AddClick(Control slot, int i, int instr)
    {
        var btn = new Button
        {
            Name = $"_pfb{i}", Text = "", Flat = true,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = slot.TooltipText,
        };
        btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        btn.Pressed += () =>
            ActionRequested?.Invoke("petcmd", -1, instr.ToString());
        slot.AddChild(btn);
    }

    // -------------------- construction -----------------------------

    private void WireNative()
    {
        for (var i = 1; i <= 6; i++)
        {
            var s = TopLeftVitals.Find<Control>(_window, $"Slot{i}");
            if (s != null) _slots.Add(s);
        }
        _modeBox = TopLeftVitals.Find<CheckBox>(_window, "PetMode");
        if (_modeBox != null)
        {
            // Pushed = PetAttackMode (combat → 1) ; normal =
            // PetCollectMode (loot/passif → 2). L'invocation native
            // démarre en mode 2 (pet+0x3ec initial).
            _modeBox.ButtonPressed = false;
            _modeBox.TooltipText = UiTextEn.Translate("Unchecked: loot/passive — ") +
                UiTextEn.Translate("checked: combat mode");
            _modeBox.Toggled += on =>
                ActionRequested?.Invoke("petmode", -1,
                    on ? "1" : "2");
        }
        var lockBtn = TopLeftVitals.Find<BaseButton>(_window, "LockBtn");
        if (lockBtn != null)
            lockBtn.TooltipText = UiTextEn.Translate("Lock pet bar position");
        var close = TopLeftVitals.Find<BaseButton>(_window, "CloseBtn");
        if (close != null)
            close.Pressed += () => Visible = false;
    }

    /// <summary>Repli sans uidata : strip sombre de 6 slots +
    /// checkbox mode au-dessus de la hotbar.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "PetFunctionWnd" };
        HudRoot.Pin(p, 0.5f, 1f, 0.5f, 1f, -132, -222, 132, -177);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.9f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 34; row.OffsetRight = -34;
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddThemeConstantOverride("separation", 4);
        p.AddChild(row);
        for (var i = 0; i < 6; i++)
        {
            var cell = new Panel
            { CustomMinimumSize = new Vector2(32, 32) };
            cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            { BgColor = new Color(0.116f, 0.112f, 0.1f, 0.5f) });
            row.AddChild(cell);
            _slots.Add(cell);
        }
        _modeBox = new CheckBox
        {
            TooltipText = UiTextEn.Translate("Unchecked: loot/passive — checked: combat"),
        };
        _modeBox.Toggled += on =>
            ActionRequested?.Invoke("petmode", -1, on ? "1" : "2");
        p.AddChild(_modeBox);
        _modeBox.Position = new Vector2(4, 6);
        var close = new Button { Text = "×" };
        close.Position = new Vector2(246, 4);
        close.Size = new Vector2(16, 18);
        close.AddThemeFontSizeOverride("font_size", 10);
        close.Pressed += () => Visible = false;
        p.AddChild(close);
        AddChild(p);
        _window = p;
    }
}
