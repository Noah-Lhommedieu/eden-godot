using Godot;
using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Player;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// S→C 9063 « état de récolte » (paquet custom Reborn, voir
/// <c>Eden.Zone/Features/Gather/GatherPackets.cs</c>) : affiche le statut
/// « En cours de minage… » avec le délai avant le prochain tirage, joue
/// l'animation de récolte (<c>collect01</c> / <c>collect02</c> de
/// <c>motion.ini</c>) en boucle et écrit la ressource obtenue dans le chat.
/// Aucune règle de jeu ici : le serveur décide de tout.
/// </summary>
public static class GatherUi
{
    public const int Ncid = 9063;

    /// <summary>Clip d'animation par type de récolte. Association pioche → collect01,
    /// gants / boule → collect02 = HYPOTHÈSE à valider visuellement (PLAN-ACTION-3 A3).</summary>
    private static string ClipFor(int kind) => kind == 1 ? "collect01" : "collect02";

    private static string StatusText(int kind) => kind switch
    {
        1 => "En cours de minage…",
        2 => "En cours de cueillette…",
        _ => "En cours de divination…",
    };

    private static GatherBanner? _banner;
    private static string _savedIdle = "stand01";
    private static bool _animating;

    public static void Bind(EdenSession s, Action<Action> post) =>
        s.OnZone(Ncid, r =>
        {
            var state = r.ReadByte(); var kind = r.ReadByte();
            var tick = r.ReadUShort(); var tool = r.ReadInt();
            var item = r.ReadInt(); var qty = r.ReadUShort(); var reason = r.ReadByte();
            post(() => Handle(state, kind, tick, tool, item, qty, reason));
        });

    private static void Handle(int state, int kind, int tickSeconds, int tool,
        int item, int qty, int reason)
    {
        GD.Print($"[gather] état={state} kind={kind} tick={tickSeconds}s outil={tool} " +
                 $"item={item} qté={qty} raison={reason}");
        switch (state)
        {
            case 1: Begin(kind, tickSeconds); break;
            case 2: Yield(kind, tickSeconds, item, qty); break;
            case 3: Notice(reason); break;
            default: End(); break;
        }
    }

    /// <summary>Textes des notices serveur (GatherRules.NoticeIdFor). Source de référence :
    /// <c>Eden.Core/Game/Foundation/errors.tsv</c> — dette : remplacée par la synchro de
    /// contenu (PLAN-ACTION-3 B6) pour n'avoir qu'une seule source.</summary>
    private static readonly Dictionary<int, string> Notices = new()
    {
        [1] = "Vous devez vous trouver dans une zone de récolte adaptée à cet outil.",
        [2] = "Vous avez bougé : la récolte est annulée.",
        [3] = "Votre outil est cassé.",
        [4] = "Votre sac est plein : la récolte s'arrête.",
        [5] = "Vous êtes déjà en train de récolter.",
    };

    private static void Notice(int id)
    {
        if (!Notices.TryGetValue(id, out var text)) return;
        HudExtensions.FindHud()?.Chat?.AddSystem(UiTextEn.Translate(text));
    }

    private static void Begin(int kind, int tickSeconds)
    {
        Banner()?.Show(UiTextEn.Translate(StatusText(kind)), tickSeconds);
        var anim = Player()?.Animator;
        if (anim == null) return;
        var clip = ClipFor(kind);
        if (!anim.Has(clip)) { GD.PrintErr($"[gather] clip {clip} absent du modèle"); return; }
        if (!_animating) _savedIdle = anim.IdleAnim;
        anim.IdleAnim = clip;
        anim.PlayLoop(clip);
        _animating = true;
        GD.Print($"[gather] animation={anim.Current} (demandée {clip})");
    }

    private static void Yield(int kind, int tickSeconds, int item, int qty)
    {
        Banner()?.Show(UiTextEn.Translate(StatusText(kind)), tickSeconds);
        GD.Print($"[gather] animation en cours={Player()?.Animator?.Current}");
        if (item <= 0 || qty <= 0) return;
        var name = TextResolver.ItemName(item) ?? $"#{item}";
        HudExtensions.FindHud()?.Chat?.AddSystem(
            $"{UiTextEn.Translate("Récolte :")} {name} x{qty}");
    }

    private static void End()
    {
        Banner()?.Hide();
        if (!_animating) return;
        _animating = false;
        if (Player()?.Animator is { } a)
        {
            a.IdleAnim = _savedIdle;
            a.PlayLoop(_savedIdle);
        }
    }

    private static PlayerController? Player() =>
        (Engine.GetMainLoop() as SceneTree)?.Root.FindChild("Player", true, false)
            as PlayerController;

    private static GatherBanner? Banner()
    {
        if (_banner != null && GodotObject.IsInstanceValid(_banner)) return _banner;
        var hud = HudExtensions.FindHud();
        if (hud == null) return null;
        _banner = new GatherBanner { Name = "GatherBanner" };
        hud.AddChild(_banner);
        return _banner;
    }
}

/// <summary>Bandeau de statut de récolte : texte + barre jusqu'au prochain tirage.</summary>
public partial class GatherBanner : PanelContainer
{
    private readonly Label _label = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar _bar = new()
    {
        MinValue = 0, MaxValue = 1, ShowPercentage = false,
        CustomMinimumSize = new Vector2(220, 8),
    };
    private double _start, _duration = 1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(_label);
        box.AddChild(_bar);
        AddChild(box);
        Visible = false;
    }

    public void Show(string text, double seconds)
    {
        _label.Text = text;
        _start = Time.GetTicksMsec() / 1000.0;
        _duration = Math.Max(1, seconds);
        Visible = true;
    }

    public new void Hide() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible) return;
        // Centré en haut de l'écran (parent = CanvasLayer : pas d'ancres relatives).
        Position = new Vector2((GetViewportRect().Size.X - Size.X) / 2f, 96);
        var t = (Time.GetTicksMsec() / 1000.0 - _start) / _duration;
        _bar.Value = Math.Clamp(t, 0, 1);
    }
}
