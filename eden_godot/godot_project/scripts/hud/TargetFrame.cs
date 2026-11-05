using Godot;
using EdenClient.Data;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Cadre de cible haut-centre — instancie le layout natif
/// <c>TargetInfoWnd.layout</c> (DefaultWindow : portrait
/// <c>Face</c>/<c>ClickBoard</c>, <c>NameText</c>/<c>LVText</c>/
/// <c>JobLvText</c>, <c>HpBar</c>/<c>MpBar</c> sur
/// <c>hpBorder</c>, rangée d'étoiles mob et icônes buff).
///
/// Remplie par <see cref="HudNetBridge"/> à la réception du NCID 444
/// <c>CNC_CZ_ClientGetSelectLifeTarget</c> ([i32 handle][u8][i32] —
  /// corps 9B prouvé capture, WireRegressionTests serveur) : handle ≠ 0
/// → nom/niveau/type/HP résolus dans <see cref="EntityRegistry"/> ;
/// handle = 0 → masquée (déselection / sortie de combat / délock).
/// Cachée aussi quand l'entité ciblée disparaît du registre (despawn
/// 269/294) — vérifié chaque frame.
/// </summary>
public partial class TargetFrame : Control
{
    /// <summary>Dernière instance construite (bridges Vague 2).</summary>
    public static TargetFrame? Instance { get; private set; }

    /// <summary>Handle de la cible affichée (0 = aucune).</summary>
    public uint TargetHandle { get; private set; }
    /// <summary>Kind natif de la cible (0 joueur, 8 NPC, 12 mob).</summary>
    public byte TargetKind { get; private set; }
    /// <summary>Nom résolu de la cible (sans préfixe « LvN »).</summary>
    public string TargetName { get; private set; } = "";
    /// <summary>Vrai si la cible est un joueur (kind 0 — son handle
    /// wire est alors son charId, cf. EntityRegistry).</summary>
    public bool TargetIsPlayer => TargetHandle != 0 && TargetKind == 0;
    /// <summary>charId de la cible joueur (0 si mob/NPC — leurs
    /// handles sont des sight-handles, pas des charIds).</summary>
    public uint TargetCharId => TargetIsPlayer ? TargetHandle : 0;

    private Control? _window;
    private Label? _name, _lv, _jobLv;
    private ProgressBar? _hp, _mp;
    private Label? _hpText, _mpText;
    // « Cible de la cible » (TargetBg natif : mini-cadre à droite
    // du cadre principal dans le ref).
    private Control? _totBg;
    private Label? _totName, _totLv, _totTitle;
    private ProgressBar? _totHp, _totMp;
    private HpLossAnim _loss = new();
    private GhostBar? _ghost, _totGhost;
    private HpLossAnim _totLoss = new();
    // ---- Break Point (plan 13) : barre violette, 9 icônes de faiblesse,
    // étoiles de rang — nœuds natifs TargetInfoWnd.layout.
    private HudRoot? _hudRoot;
    private Control? _bpBg;
    private ProgressBar? _bpBar;
    private Label? _bpText;
    private TextureRect? _frame;
    private Control? _hpBorder;
    private Control? _starBorder;
    private readonly Control?[] _weak = new Control?[9];
    private readonly Control?[] _stars = new Control?[5];
    private readonly TextureRect?[] _buffIcons = new TextureRect?[4];
    private readonly Control?[] _buffHosts = new Control?[4];

    public void Build(HudRoot hud)
    {
        _hudRoot = hud;
        _window = hud.Build("TargetInfoWnd.layout");
        if (_window == null)
        {
            BuildFallback();
        }
        else
        {
            AddChild(_window);
            // DefaultWindow sérialisé avec ancres partiellement
            // scale sur une racine plein écran : épinglé explicitement
            // haut-centre (sous le bandeau d'événement, à gauche de
            // l'horloge radar) — la position native EE.
            // Ref TimedCastSkill.png / shot_0037 : fenêtre native posée
            // à droite des vitals (portrait x≈262, y≈76).
            HudRoot.Pin(_window, 0, 0, 0, 0, 217, 74, 217 + 449, 74 + 120);
            _name = TopLeftVitals.Find<Label>(_window, "NameText");
            _lv = TopLeftVitals.Find<Label>(_window, "LVText");
            _jobLv = TopLeftVitals.Find<Label>(_window, "JobLvText");
            _hp = TopLeftVitals.Find<ProgressBar>(_window, "HpBar");
            _mp = TopLeftVitals.Find<ProgressBar>(_window, "MpBar");
            _hpText = TopLeftVitals.Find<Label>(_hp, "text");
            _mpText = TopLeftVitals.Find<Label>(_mp, "text");
            if (_hp != null)
                _hp.AddThemeStyleboxOverride("fill", EeSkin.HpFill);
            if (_mp != null)
                _mp.AddThemeStyleboxOverride("fill", EeSkin.MpFill);
            foreach (var t in new[] { _hpText, _mpText })
                if (t != null) EeSkin.Shadowed(t, 11);
            if (_hp != null)
            {
                _ghost = new GhostBar();
                _hp.AddChild(_ghost);
                // sous le texte de PV
                if (_hpText != null) _hp.MoveChild(_ghost, _hpText.GetIndex());
            }
            // Ref : nom du mob jaune, niveau blanc.
            if (_name != null)
            {
                EeSkin.Shadowed(_name, 12, Color.Color8(235, 235, 90));
                _name.HorizontalAlignment = HorizontalAlignment.Center;
            }
            // Ref : niveau gris discret à gauche du bandeau.
            if (_lv != null)
            {
                EeSkin.Shadowed(_lv, 12, Color.Color8(150, 150, 150));
                _lv.HorizontalAlignment = HorizontalAlignment.Left;
            }
            // Éléments sans données : étoiles de rang mob, icônes de
            // classe/élément. Les 4 slots buff restent cachés tant
            // que la cible n'a pas d'enchant (SetBuffs les montre).
            foreach (var leaf in new[] { "CharIcon", "JobIcon" })
            {
                var n = TopLeftVitals.Find<Control>(_window, leaf);
                if (n != null) n.Visible = false;
            }
            _frame = TopLeftVitals.Find<TextureRect>(_window, "ClickBoard");
            _hpBorder = TopLeftVitals.Find<Control>(_window, "hpBorder");
            BuildBreakPoint(hud);
            // Slots buff1-4 natifs (SlotImage) — Vague 2 : nourris
            // par EntityBuffs via SetBuffs (301 de la cible + le
            // broadcast 299 des joueurs vus).
            for (var i = 0; i < 4; i++)
            {
                var host = TopLeftVitals.Find<Control>(
                    _window, $"buff{i + 1}");
                _buffHosts[i] = host;
                if (host == null) continue;
                host.Visible = false;
                var icon = host as TextureRect;
                if (icon == null)
                {
                    icon = new TextureRect
                    {
                        ExpandMode = TextureRect.ExpandModeEnum
                            .IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum
                            .KeepAspectCentered,
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    icon.SetAnchorsAndOffsetsPreset(
                        LayoutPreset.FullRect);
                    host.AddChild(icon);
                }
                _buffIcons[i] = icon;
            }
            // MP des mobs absente du 298 : la ref montre la barre MP
            // pleine sans chiffres — affichée pleine, texte vide.
            if (_mp != null) _mp.Value = 1;
            if (_mpText != null) _mpText.Text = "";
            // Target's Target : caché tant qu'aucune cible de la
            // cible n'est connue (272 observe l'aggro du mob).
            _totBg = TopLeftVitals.Find<Control>(_window, "TargetBg");
            if (_totBg != null)
            {
                _totName = TopLeftVitals.Find<Label>(_totBg, "Name");
                _totLv = TopLeftVitals.Find<Label>(_totBg, "Level");
                _totTitle = TopLeftVitals.Find<Label>(_totBg, "Title");
                _totHp = TopLeftVitals.Find<ProgressBar>(_totBg, "HpBar");
                _totMp = TopLeftVitals.Find<ProgressBar>(_totBg, "MpBar");
                if (_totMp != null) _totMp.Value = 1;
                if (_totHp != null)
                {
                    _totHp.AddThemeStyleboxOverride("fill", EeSkin.HpFill);
                    _totGhost = new GhostBar();
                    _totHp.AddChild(_totGhost);
                }
                if (_totMp != null)
                    _totMp.AddThemeStyleboxOverride("fill", EeSkin.MpFill);
                if (_totName != null) EeSkin.Shadowed(_totName, 11);
                if (_totLv != null) EeSkin.Shadowed(_totLv, 11);
                if (_totTitle != null)
                    EeSkin.Shadowed(_totTitle, 11, Color.Color8(225, 225, 225));
                _totBg.Visible = false;
            }
        }
        // Bouton « ? » natif (MonsterInfoBtn) = inspecter : C→S 143
        // LookEquip [i32 charId] pour un joueur (réponse 460 →
        // InspectWindow) ; fiche mob (stats/drops/skills) à venir.
        var inspect = TopLeftVitals.Find<BaseButton>(_window, "MonsterInfoBtn");
        if (inspect != null)
        {
            inspect.TooltipText = UiTextEn.Translate("Inspect");
            inspect.Pressed += Inspect;
        }
        Visible = false;
        Instance = this;
    }

    /// <summary>Câble les nœuds natifs Break Point : <c>BPBackground</c>
    /// (+<c>BPIcon</c>/<c>Bar</c>/<c>Bar/Text</c>), <c>Weak0..8</c>
    /// (image par attribut) et <c>MonsterStartBorder/star1..5</c> ; tout
    /// reste caché jusqu'à <see cref="SetBreak"/>.</summary>
    private void BuildBreakPoint(HudRoot hud)
    {
        _bpBg = TopLeftVitals.Find<Control>(_window, "BPBackground");
        if (_bpBg != null)
        {
            _bpBg.Visible = false;
            _bpBar = TopLeftVitals.Find<ProgressBar>(_bpBg, "Bar");
            _bpText = TopLeftVitals.Find<Label>(_bpBg, "Text");
            if (_bpBar != null)
            {
                _bpBar.AddThemeStyleboxOverride("fill", EeSkin.BpFill);
                _bpBar.TooltipText = UiTextEn.Translate("Break Point");
            }
            if (_bpText != null) EeSkin.Shadowed(_bpText, 10);
        }
        for (var i = 0; i < 9; i++)
        {
            var w = TopLeftVitals.Find<Control>(_window, $"Weak{i}");
            _weak[i] = w;
            if (w == null) continue;
            w.Visible = false;
            w.MouseFilter = MouseFilterEnum.Pass;
            w.TooltipText = BreakPointUi.WeakTip(i);
            if (w is TextureRect tr &&
                hud.Region("main", BreakPointUi.IconImages[i]) is { } tex)
                tr.Texture = tex;
        }
        _starBorder = TopLeftVitals.Find<Control>(_window,
            "MonsterStartBorder");
        if (_starBorder != null)
        {
            _starBorder.Visible = false;
            // Ref : les étoiles suivent le niveau (« 81 ★★ ») — le
            // layout les pose sur le texte du niveau : décalage.
            _starBorder.OffsetLeft += 20f;
            _starBorder.OffsetRight += 20f;
        }
        for (var i = 0; i < 5; i++)
        {
            _stars[i] = TopLeftVitals.Find<Control>(_window, $"star{i + 1}");
            if (_stars[i] != null) _stars[i]!.Visible = false;
        }
    }

    private Vector2[]? _weakHome;      // positions natives (arc complet)
    private int[]? _weakArc;           // slots triés le long de l'arc

    /// <summary>Resserre les icônes de faiblesse : le layout natif répartit
    /// les 9 éléments sur un arc autour du portrait (un slot par élément) ;
    /// avec peu de faiblesses elles restaient espacées. Elles occupent
    /// maintenant des positions d'arc consécutives, centrées sur l'arc.</summary>
    private void PackWeakIcons(int weakMask)
    {
        if (_weakHome == null)
        {
            _weakHome = new Vector2[9];
            for (var i = 0; i < 9; i++)
                if (_weak[i] != null) _weakHome[i] = _weak[i]!.Position;
            // Ordre le long de l'arc = tri par ordonnée native.
            _weakArc = Enumerable.Range(0, 9)
                .OrderBy(i => _weakHome[i].Y).ToArray();
        }
        var on = BreakPointUi.WeakSlots(weakMask);
        if (on.Count == 0) return;
        // Éléments gardés dans leur ordre d'arc d'origine.
        var ordered = _weakArc!.Where(on.Contains).ToList();
        var start = (9 - ordered.Count) / 2;
        for (var k = 0; k < ordered.Count; k++)
            if (_weak[ordered[k]] is { } w)
                w.Position = _weakHome[_weakArc![start + k]];
    }

    /// <summary>Met à jour l'affichage Break Point de la cible : étoiles
    /// de rang (type de monstre), icônes de faiblesse (masque) et barre
    /// violette (patience courante/max). Tout est masqué si la cible n'a
    /// pas de faiblesse (joueur, PNJ, mob normal).</summary>
    public void SetBreak(int monsterType, int weakMask, int cur, int max)
    {
        var stars = BreakPointUi.Stars(monsterType);
        if (_starBorder != null)
        {
            _starBorder.Visible = stars > 0;
            _starBorder.TooltipText = UiTextEn.Translate(
                BreakPointUi.RankName(monsterType));
            _starBorder.MouseFilter = MouseFilterEnum.Pass;
        }
        for (var i = 0; i < _stars.Length; i++)
            if (_stars[i] != null) _stars[i]!.Visible = i < stars;
        var show = BreakPointUi.ShowBar(weakMask, max);
        for (var i = 0; i < _weak.Length; i++)
            if (_weak[i] != null)
                _weak[i]!.Visible = show && BreakPointUi.IsWeak(weakMask, i);
        PackWeakIcons(show ? weakMask : 0);
        if (_bpBg != null) _bpBg.Visible = show;
        if (!show) return;
        if (_bpBar != null)
            _bpBar.Value = BreakPointUi.Fraction(cur, max);
        if (_bpText != null) _bpText.Text = BreakPointUi.BarText(cur, max);
    }

    /// <summary>Médaillon du portrait (<c>ClickBoard</c> natif =
    /// <c>faceFrame</c>) : image Target* de l'imageset main selon le
    /// type de cible (ref « targetframe (Elite mob) » / TailleMob).</summary>
    private void SetBadge(string image)
    {
        if (_frame == null || _hudRoot == null) return;
        if (_hudRoot.Region("main", image) is { } tex)
            _frame.Texture = tex;
    }

    /// <summary>Id de gabarit c_monster de la cible quand c'est un mob (0
    /// sinon) — posé par <c>HudNetBridge</c> avec <see cref="SetTarget"/>.</summary>
    public int TargetTemplateId { get; set; }

    /// <summary>Déclenche le bouton « ? » (QA <c>--hud-open=target-inspect</c>).</summary>
    public void InvokeInspect() => Inspect();

    private void Inspect()
    {
        if (TargetCharId == 0)
        {
            // Mob : fiche à 3 onglets (Stats / Drops / Skills).
            if (TargetTemplateId > 0 && MobInfoWindow.Instance != null)
                MobInfoWindow.Instance.Open(TargetTemplateId);
            else
                GD.Print($"[target] inspect 0x{TargetHandle:x8} : pas un mob");
            return;
        }
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var task = s.SendZoneAsync(new ZoneServerLookEquip
            { Value = (int)TargetCharId },
            System.Threading.CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[inspect] : {t.Exception}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Affiche la cible. <paramref name="displayName"/> est le
    /// label du registre (« Lv12 Rose Knight » — le niveau est extrait
    /// vers <c>LVText</c>, le reste va à <c>NameText</c>).</summary>
    public void SetTarget(uint handle, string displayName, byte kind,
        long hp, long maxHp, int family = 0)
    {
        TargetHandle = handle;
        TargetKind = kind;
        TargetTemplateId = 0;
        SetBadge(BreakPointUi.TargetBadge(kind, family));
        // « <Titre>\nLvN Nom » / « LvN Nom » / « Nom » (titre non affiché
        // dans le cadre natif — seulement le nom, ref Elite mob).
        var (_, level, name) = BreakPointUi.SplitLabel(displayName);
        name = TextResolver.NameOnly(name);
        TargetName = name;
        SetBreak(0, 0, 0, 0);       // nouvelle cible : pas de BP hérité
        _loss = new HpLossAnim();   // nouvelle cible : pas d'animation héritée
        if (_name != null) FitLabel(_name, name, 12);
        if (_lv != null) _lv.Text = level > 0 ? $"{level}" : "";
        // Ref : pas de libellé de type à côté du niveau.
        if (_jobLv != null) _jobLv.Text = "";
        SetVitals(hp, maxHp);
        ClearTargetOfTarget();
        Visible = true;
    }

    /// <summary>« Target's Target » natif : nom + niveau + HP de la
    /// cible de notre cible (résolu depuis le dernier 272 observé).
    /// <paramref name="displayName"/> = label registre « LvN … ».</summary>
    public void SetTargetOfTarget(string displayName, long hp,
        long maxHp)
    {
        if (_totBg == null) return;
        var (_, level, name) = BreakPointUi.SplitLabel(displayName);
        name = TextResolver.NameOnly(name);
        if (_totName != null) FitLabel(_totName, name, 11);
        if (_totLv != null)
            _totLv.Text = level > 0 ? $"{level}" : "";
        if (_totTitle != null) _totTitle.Text = UiTextEn.Translate("Target's Target");
        if (_totHp != null)
        {
            _totHp.Visible = maxHp > 0;
            var f = maxHp > 0 ? Math.Clamp((float)hp / maxHp, 0f, 1f) : 1f;
            _totHp.Value = f;
            _totLoss.Set(f);
        }
        _totBg.Visible = true;
    }

    /// <summary>Masque le cadre « cible de la cible ».</summary>
    public void ClearTargetOfTarget()
    {
        if (_totBg != null) _totBg.Visible = false;
    }

    /// <summary>HP courante/max de la cible (444 initial puis 280
    /// UpdateAttr attrId=0 pendant le combat).</summary>
    public void SetVitals(long hp, long maxHp)
    {
        // PNJ / entités sans PV (maxHp 0) : barre cachée au lieu d'un
        // cadre vide « 0 PV » (3.3.0).
        var hasHp = maxHp > 0;
        // PNJ : pas de barres PV/MP du tout (cadre natif sans vitaux).
        if (_hpBorder != null) _hpBorder.Visible = TargetKind != 8;
        if (_hp != null) _hp.Visible = hasHp;
        if (_mp != null && TargetKind == 8) _mp.Visible = false;
        else if (_mp != null) _mp.Visible = true;
        var f = hasHp ? Math.Clamp((float)hp / maxHp, 0f, 1f) : 1f;
        if (_hp != null) _hp.Value = f;
        _loss.Set(f);
        // Valeurs numériques : pref « Cible » (rien / PV / PM / PV+PM).
        // PM : connus seulement pour soi-même (le 298/444 ne les porte pas).
        _lastHp = hp; _lastMax = maxHp;
        var mode = HudPrefs.TargetValues;
        var showHp = mode is HudPrefs.ValuesMode.Hp or HudPrefs.ValuesMode.Both;
        var showMp = mode is HudPrefs.ValuesMode.Mp or HudPrefs.ValuesMode.Both;
        if (_hpText != null)
            _hpText.Text = hasHp && showHp
                ? HudPrefs.FormatValue(UiTextEn.Translate("HP"), hp, maxHp)
                : "";
        var selfId = (uint)(SessionCarry.Character?.Id ?? 0);
        if (_mpText != null)
            _mpText.Text = showMp && TargetHandle != 0 &&
                TargetHandle == selfId && HudPrefs.SelfMaxMp > 0
                ? HudPrefs.FormatValue(UiTextEn.Translate("MP"),
                    HudPrefs.SelfMp, HudPrefs.SelfMaxMp)
                : "";
        if (_mp != null)
            _mp.Value = TargetHandle != 0 && TargetHandle == selfId &&
                HudPrefs.SelfMaxMp > 0
                ? Math.Clamp((double)HudPrefs.SelfMp / HudPrefs.SelfMaxMp, 0, 1)
                : 1;
        UpdateGhosts();
    }

    private long _lastHp, _lastMax;

    public override void _EnterTree()
    {
        HudPrefs.Changed += RefreshValues;
        HudPrefs.SelfMpChanged += RefreshValues;
    }

    public override void _ExitTree()
    {
        HudPrefs.Changed -= RefreshValues;
        HudPrefs.SelfMpChanged -= RefreshValues;
    }

    private void RefreshValues()
    {
        if (TargetHandle != 0) SetVitals(_lastHp, _lastMax);
    }

    private void UpdateGhosts()
    {
        if (_ghost != null)
        {
            _ghost.Lo = _loss.Frac; _ghost.Hi = _loss.GhostTop();
            _ghost.QueueRedraw();
        }
        if (_totGhost != null)
        {
            _totGhost.Lo = _totLoss.Frac; _totGhost.Hi = _totLoss.GhostTop();
            _totGhost.QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_loss.Active || _totLoss.Active || (_ghost?.Hi ?? 0) > (_ghost?.Lo ?? 0) + 1e-4f)
            UpdateGhosts();
    }

    /// <summary>Nom/titre long : réduit la police jusqu'à 8 px puis
    /// tronque avec « … » pour rester dans le cadre (3.3.0).</summary>
    private static void FitLabel(Label l, string text, int baseSize)
    {
        l.Text = text;
        l.ClipText = true;
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var w = l.Size.X > 8 ? l.Size.X : 200f;
        var font = l.GetThemeFont("font");
        var size = baseSize;
        while (size > 8 && font != null &&
               font.GetStringSize(text, HorizontalAlignment.Left, -1,
                   size).X > w)
            size--;
        l.AddThemeFontSizeOverride("font_size", size);
    }

    /// <summary>Déselection (444 handle=0) ou despawn de la cible.</summary>
    public void Clear()
    {
        TargetHandle = 0;
        TargetKind = 0;
        TargetName = "";
        SetBuffs(new List<EntityBuffs.BuffInst>());
        SetBreak(0, 0, 0, 0);
        Visible = false;
    }

    /// <summary>Remplit les 4 slots natifs buff1-4 depuis le snapshot
    /// <see cref="EntityBuffs"/> de la cible (appelé par BuffBridge
    /// sur changement de cible et sur chaque update du handle).
    /// Tooltip = nom EN + durée + effect id ; cadre rouge = debuff.
    /// </summary>
    public void SetBuffs(List<EntityBuffs.BuffInst> buffs)
    {
        buffs = buffs.Where(x => BuffBar.Displayable(x.EffectId)).ToList();
        for (var i = 0; i < 4; i++)
        {
            var host = _buffHosts[i];
            if (host == null) continue;
            var b = i < buffs.Count ? buffs[i] : null;
            host.Visible = b != null;
            var icon = _buffIcons[i];
            if (icon == null) continue;
            if (b == null)
            {
                icon.Texture = null;
                host.TooltipText = "";
                continue;
            }
            icon.Texture = BuffIcon(b.EffectId);
            icon.Modulate = Data.Tables.EnchantTable.Get(b.EffectId)
                is { IsBuff: false }
                ? new Color(1f, 0.55f, 0.55f) : Colors.White;
            var rem = b.Remaining;
            var dur = rem < 0 ? "permanent"
                : $"{(int)rem / 60}:{(int)rem % 60:D2}";
            host.TooltipText =
                $"{Data.Tables.EnchantTable.NameOf(b.EffectId)}\n" +
                UiTextEn.Translate($"Lv{b.Level}  ·  {dur}\n#{b.EffectId}");
        }
    }

    /// <summary>Icône d'un enchant cible — même convention que
    /// <see cref="BuffBar"/> (code c_enchant col1 → skillicon puis
    /// itemicon/uiicon).</summary>
    private static Texture2D? BuffIcon(int effectId)
    {
        var code = Data.Tables.EnchantTable.Get(effectId)?.Code;
        if (code == null || code.Length == 0) return null;
        return AssetCatalog.GetTexture($"UI/skillicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/itemicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/uiicon/{code}.png");
    }

    /// <summary>Cadre de repli sans uidata : nom + niveau + barre HP.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "TargetInfoWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        p.Position = new Vector2(-190, 6);
        p.Size = new Vector2(380, 96);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.85f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        _name = new Label { Position = new Vector2(70, 6) };
        _lv = new Label { Position = new Vector2(16, 6) };
        _jobLv = new Label { Position = new Vector2(300, 6) };
        _hp = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, ShowPercentage = false,
            Position = new Vector2(70, 34), Size = new Vector2(280, 14),
        };
        _hp.AddThemeStyleboxOverride("fill", new StyleBoxFlat
        {
            BgColor = new Color(0.8f, 0.15f, 0.15f),
        });
        _hpText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hpText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hpText.AddThemeFontSizeOverride("font_size", 9);
        _hp.AddChild(_hpText);
        p.AddChild(_name); p.AddChild(_lv); p.AddChild(_jobLv);
        p.AddChild(_hp);
        AddChild(p);
        _window = p;
    }
}
