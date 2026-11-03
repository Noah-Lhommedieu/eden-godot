using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// Calendrier de connexion quotidien — lot Vague 1-D (Phase 6),
/// modèle <c>LoginReward.layout</c> / shot <c>ref-shots/daily
/// reward.png</c> : grille de cases jour (Block1..28 + milestones
/// 29-32), icône d'item, bouton « 領取 » (claim).
///
/// Wire (zone/serveur — ZoneEntryFlow.DailyAward.cs +
/// Codecs/DailyAwardPackets.cs, GOLDEN pour le 258) :
///   C→S 258 DailyAwardOP <c>[s16 op][i32 param]</c> :
///     op 0 = check de jour (accumule le temps en ligne ; envoyé une
///     fois à l'entrée — le serveur fait DailyUpdate à chaque op) ;
///     op 1 = requête d'état (à l'ouverture de la fenêtre) ;
///     op 2 = claim du jour param (1..32).
///   S→C 615 ClientUpdateDailyAwards (26 octets) :
///     [s16 dayIndex][i32 onlineS][i64 awardDays][i64 awardRecords]
///     [s16 type][s16 flag] — type 4 = système désactivé.
///
/// Définitions : <see cref="DailyAwardTable"/> (C_DailyAward.ini) —
/// item + quantité + temps en ligne requis par jour.
/// </summary>
public partial class DailyRewardWindow : Control
{
    /// <summary>Instance montée (game.tscn ZoneWindows) — pour le
    /// bouton « Daily Reward » d'AchievementWindow.</summary>
    public static DailyRewardWindow? Instance { get; private set; }

    // --- état 615 ---
    private int _dayIndex;            // jour courant (0-based serveur)
    private int _onlineSeconds;
    private ulong _awardDays;         // bit day-1 = jour complété
    private ulong _awardRecords;      // bit day-1 = jour réclamé
    private bool _enabled = true;

    // --- UI ---
    private Panel? _panel;
    private GridContainer? _grid;
    private Label? _status, _timer;

    private static Dictionary<int, DailyAwardTable.DailyAwardDef>?
        _defs;
    private static Dictionary<int, ItemTable.ItemDef>? _items;

    /// <summary>Bascule la fenêtre (bouton / --hud-open).</summary>
    public static void ToggleStatic()
    {
        if (Instance == null) return;
        if (Instance._panel != null && Instance._panel.Visible)
            Instance._panel.Visible = false;
        else
            Instance.Open();
    }

    public override void _Ready()
    {
        Instance = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildPanel();
        Visible = true;
        if (_panel != null) _panel.Visible = false;
        // Trame 615 du burst d'entrée reçue avant le montage.
        if (_stashed is { } f) { _stashed = null; ApplyState(f); }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------- réseau -------------------------
    // Le bind 615 est posé par HudExtensions.BindSession (pont
    // partagé éprouvé en live) ; le check de jour 258 op0 est déjà
    // dans le burst d'entrée d'EdenSession (Field2=2 → réponse type
    // 5) — pas de renvoi ici. Les trames arrivent désérialisées sur
    // le thread principal via <see cref="OnDailyAwards"/>.

    /// <summary>Trame 615 reçue avant le montage (le burst d'entrée
    /// arrive tôt) — appliquée à la construction de la fenêtre.</summary>
    private static ZoneUpdateDailyAwards? _stashed;

    /// <summary>Applique une trame 615 (pont, thread principal) :
    /// met à jour l'état du calendrier et ouvre la fenêtre si le
    /// check de jour révèle une récompense à prendre (popup natif).
    /// Hors montage, la trame est mise de côté pour le _Ready.</summary>
    public static void OnDailyAwards(ZoneUpdateDailyAwards m)
    {
        if (Instance == null) { _stashed = m; return; }
        Instance.ApplyState(m);
    }

    private void ApplyState(ZoneUpdateDailyAwards m)
    {
        _dayIndex = m.DayIndex;
        _onlineSeconds = m.OnlineSeconds;
        _awardDays = (ulong)m.AwardDays;
        _awardRecords = (ulong)m.AwardRecords;
        _enabled = m.Kind != 4;
        Refresh();
        // Popup de login natif : la fenêtre s'ouvre d'elle-même quand
        // le check du jour (type 0/5) révèle une récompense à prendre
        // (cf. « daily reward.png » — calendrier au login).
        if ((m.Kind == 0 || m.Kind == 5) && HasClaimable())
            Open();
    }

    /// <summary>Un jour est réclamable : complété (awardDays) et pas
    /// encore réclamé (awardRecords).</summary>
    private bool HasClaimable()
    {
        EnsureTables();
        if (_defs == null) return (_awardDays & ~_awardRecords) != 0;
        foreach (var day in _defs.Keys)
            if (Claimable(day)) return true;
        return false;
    }

    private bool Claimable(int day)
    {
        var bit = 1UL << ((day - 1) & 0x3f);
        return (_awardDays & bit) != 0 && (_awardRecords & bit) == 0;
    }

    private bool Claimed(int day) =>
        (_awardRecords & (1UL << ((day - 1) & 0x3f))) != 0;

    private bool Completed(int day) =>
        (_awardDays & (1UL << ((day - 1) & 0x3f))) != 0;

    /// <summary>Ouvre la fenêtre + requête d'état fraîche (258 op1).
    /// (<c>Open</c> : <c>Show</c> est pris par CanvasItem.)</summary>
    public void Open()
    {
        if (_panel == null) return;
        EnsureTables();
        Refresh();
        _panel.Visible = true;
        Send(new ZoneDailyAwardOp { Field1 = 1, Field2 = 0 });
    }

    public void Toggle() => ToggleStatic();

    /// <summary>Données de démo hors session (<c>--hud-open=daily</c>)
    /// — une trame 615 de type 1 synthétique passée par le vrai
    /// <see cref="ApplyState"/> (jours 1-4 complétés, 1-2 réclamés,
    /// jour courant = 5). En live, une requête d'état 258 op1 est
    /// aussi émise : la vraie trame remplace la démo.</summary>
    public void EnsureDemo()
    {
        EnsureTables();
        if (_awardDays == 0)
            ApplyState(new ZoneUpdateDailyAwards
            {
                DayIndex = 4,
                OnlineSeconds = 3600,
                AwardDays = 0b1111,
                AwardRecords = 0b0011,
                Kind = 1,
            });
        Refresh();
        // Live : l'état réel écrase la démo dès la réponse 615.
        Send(new ZoneDailyAwardOp { Field1 = 1, Field2 = 0 });
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t =>
                GD.PrintErr($"[daily] send {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnClaim(int day) =>
        Send(new ZoneDailyAwardOp { Field1 = 2, Field2 = (uint)day });

    // ------------------------- tables -------------------------

    private static void EnsureTables()
    {
        if (_defs != null) return;
        var p = AssetCatalog.ResolveRawAsset("data/db/C_DailyAward.ini");
        try
        {
            _defs = p != null ? DailyAwardTable.Load(p) : new();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[daily] C_DailyAward.ini : {e.Message}");
            _defs = new();
        }
        var ipath = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        _items = ipath != null
            ? ItemTable.Load(ipath)
            : new Dictionary<int, ItemTable.ItemDef>();
        // Les récompenses quotidiennes sont des items MALL
        // (51464-51467 gift boxes, 49704-49707 Generalist Caches) —
        // absents de c_item.ini, présents dans c_itemmall.ini
        // (même format |V.11|78| ; les noms EN passent déjà par
        // TextResolver.ItemName qui lit t_itemmall.ini).
        var mpath = AssetCatalog.ResolveRawAsset(
            "data/db/c_itemmall.ini");
        if (mpath != null)
            foreach (var (id, def) in ItemTable.Load(mpath))
                _items.TryAdd(id, def);
    }

    private static Texture2D? ItemIcon(int itemId)
    {
        if (_items != null && _items.TryGetValue(itemId, out var d) &&
            d.Code.Length > 0)
            return AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png");
        return null;
    }

    private static string ItemName(int itemId) =>
        TextResolver.ItemName(itemId)
        ?? (_items != null && _items.TryGetValue(itemId, out var d) &&
            d.Name.Length > 0 ? d.Name : $"#{itemId}");

    // ------------------------- UI -------------------------

    private void Refresh()
    {
        if (_grid == null || _defs == null) return;
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        var days = _defs.Values.OrderBy(d => d.Day).ToList();
        foreach (var def in days)
            _grid.AddChild(MakeCell(def));
        if (_status != null)
        {
            _status.Text = !_enabled
                ? UiTextEn.Translate("System disabled on the server.")
                : UiTextEn.Translate($"Day {_dayIndex + 1} — ") +
                  $"{days.Count(d => Completed(d.Day))}/{days.Count} " +
                  UiTextEn.Translate("completed");
        }
        if (_timer != null)
        {
            var next = _defs.TryGetValue(_dayIndex + 1, out var nd)
                ? nd.RequiredSeconds : 0;
            _timer.Text = _enabled
                ? UiTextEn.Translate($"Online: {_onlineSeconds / 60} min") +
                  (next > 0 ? UiTextEn.Translate($" — next day at {next / 60} min") : "")
                : "";
        }
    }

    private Control MakeCell(DailyAwardTable.DailyAwardDef def)
    {
        var claimed = Claimed(def.Day);
        var claimable = Claimable(def.Day);
        var completed = Completed(def.Day);
        var current = def.Day == _dayIndex + 1;

        var cell = new PanelContainer
        {
            CustomMinimumSize = new Vector2(96, 112),
        };
        cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = claimed
                ? new Color(0.09f, 0.13f, 0.09f, 0.85f)
                : claimable
                    ? new Color(0.16f, 0.13f, 0.05f, 0.95f)
                    : current
                        ? new Color(0.09f, 0.10f, 0.16f, 0.9f)
                        : new Color(0.06f, 0.07f, 0.10f, 0.7f),
            BorderColor = claimable
                ? new Color(0.9f, 0.75f, 0.25f)
                : claimed
                    ? new Color(0.4f, 0.6f, 0.35f)
                    : current
                        ? new Color(0.45f, 0.5f, 0.75f)
                        : new Color(0.24f, 0.26f, 0.34f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });

        var v = new VBoxContainer { Alignment =
            BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 2);
        cell.AddChild(v);

        var dayLabel = new Label
        {
            Text = UiTextEn.Translate($"Day {def.Day}"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        dayLabel.AddThemeFontSizeOverride("font_size", 10);
        if (current)
            dayLabel.AddThemeColorOverride("font_color",
                new Color(0.7f, 0.8f, 1f));
        v.AddChild(dayLabel);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(40, 40),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = ItemIcon(def.ItemId) ?? AssetCatalog.White,
            TooltipText = $"{ItemName(def.ItemId)} ×{def.Amount}\n" +
                $"{def.RequiredSeconds / 60} min en ligne requises",
        };
        if (!completed && !claimable)
            icon.Modulate = new Color(1, 1, 1, 0.3f);
        v.AddChild(icon);

        var qty = new Label
        {
            Text = def.Amount > 1 ? $"×{def.Amount}" : " ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        qty.AddThemeFontSizeOverride("font_size", 9);
        v.AddChild(qty);

        if (claimed)
        {
            var done = new Label
            {
                Text = "✓",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            done.AddThemeColorOverride("font_color",
                new Color(0.5f, 0.9f, 0.45f));
            done.AddThemeFontSizeOverride("font_size", 12);
            v.AddChild(done);
        }
        else if (claimable)
        {
            var btn = new Button { Text = UiTextEn.Translate("Claim") };
            btn.AddThemeFontSizeOverride("font_size", 10);
            var day = def.Day;
            btn.Pressed += () => OnClaim(day);
            v.AddChild(btn);
        }
        else
        {
            var lockLabel = new Label
            {
                Text = current ? UiTextEn.Translate("in progress") : "·",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            lockLabel.AddThemeFontSizeOverride("font_size", 9);
            lockLabel.AddThemeColorOverride("font_color",
                new Color(0.5f, 0.5f, 0.6f));
            v.AddChild(lockLabel);
        }
        return cell;
    }

    private void BuildPanel()
    {
        _panel = new Panel { Name = "DailyRewardPanel" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        // Décalé à gauche : la fenêtre succès (centrée-droite) reste
        // visible quand les deux sont ouvertes.
        _panel.Position = new Vector2(-640, -255);
        _panel.Size = new Vector2(650, 510);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, UiTextEn.Translate("Daily Reward"));
        DragHelper.Persist(_panel, "daily_reward");

        _status = new Label
        {
            Position = new Vector2(12, 30),
            Size = new Vector2(420, 20),
            Text = UiTextEn.Translate("Login calendar"),
        };
        _status.AddThemeFontSizeOverride("font_size", 11);
        _panel.AddChild(_status);

        _timer = new Label
        {
            Position = new Vector2(430, 30),
            Size = new Vector2(210, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _timer.AddThemeFontSizeOverride("font_size", 10);
        _panel.AddChild(_timer);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(10, 54),
            Size = new Vector2(630, 444),
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        _grid = new GridContainer
        {
            Columns = 6,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);
        _panel.AddChild(scroll);
    }
}
