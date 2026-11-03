using System.Collections.Concurrent;
using Godot;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Logique de changement de classe — lot S6 (sans rendu : la fenêtre
/// visible est <see cref="ClassWindow"/>, ref ClassePanel.png).
///
/// Montage : nœud <c>ZoneWindows/ClassChangeDialog</c> de
/// <c>scenes/game.tscn</c> ; binding paresseux de
/// <see cref="SessionCarry.Session"/> (même pattern que
/// <see cref="HudNetBridge"/>).
///
/// Wire (GOLDEN/DESASM serveur) :
///   S→C 489 PlayerClassList — toutes les classes accessibles
///     ({1..5,19} + possédées), level/exp/points/aa par classe.
///     Reçue à l'entrée et réémise après chaque changement.
///   C→S 168 ChangeClass [u8 classId] — « Activate » / « Transform! » ;
///     le serveur répond par le burst complet (489 + attr 0x2c + 510).
///   S→C 510 ChangeClassShortcut — parsé/logué (la hotbar est
///     re-remplie par le serveur).
///
/// Hotkey <c>P</c> : ouvre la fenêtre Classe (comme K et l'icône du
/// menu).
/// </summary>
public partial class ClassChangeDialog : Control
{
    private static ClassChangeDialog? _instance;
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    private static readonly List<ZonePlayerClassList.Entry> _classes = new();

    /// <summary>Classes accessibles (dernier 489).</summary>
    public static IReadOnlyList<ZonePlayerClassList.Entry> Classes => _classes;
    /// <summary>Émis (thread principal) à chaque 489.</summary>
    public static event Action? ClassesChanged;

    public override void _Ready()
    {
        _instance = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            { GD.PrintErr($"[class] dispatch : {ex}"); }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k &&
            k.Keycode == Key.P &&
            GetViewport().GuiGetFocusOwner() is not LineEdit)
            HudExtensions.FindHud()?.ToggleSkills();
    }

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(489, r =>
        {
            var m = new ZonePlayerClassList();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _classes.Clear();
                _classes.AddRange(m.Entries);
                GD.Print($"[verify] classe : 489 reçu — " +
                    $"{m.Entries.Count} classes listées");
                ClassesChanged?.Invoke();
            });
        });
        s.OnZone(510, r =>
        {
            var m = new ZoneChangeClassShortcut();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PlayerProfileState.Apply(m);
                GD.Print(
                    $"[class] 510 shortcuts : {m.Bars.Count} barres, " +
                    $"{m.Extras.Count} extras");
            });
        });
        GD.Print("[class] binds 489/510 posés");
    }

    /// <summary>C→S 168 ChangeClass — false si hors ligne / déjà active.</summary>
    public static bool RequestChange(int classId)
    {
        var s = _instance?._session;
        if (s?.Zone?.IsConnected != true) return false;
        var cur = PlayerVitals.ActiveClass > 0 ? PlayerVitals.ActiveClass
            : SessionCarry.Character?.ClassId ?? -1;
        if (classId == cur) return false;
        _ = s.SendZoneAsync(new ZoneChangeClass
                { ClassId = (byte)classId }, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[class] ChangeClass : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        GD.Print($"[class] switch to class {classId} requested");
        return true;
    }
}
