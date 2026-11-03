using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

// Fenêtres légères des systèmes couverts par la vague complétude —
// chaque fenêtre lit les paquets parsés via MiscPackets.Received et
// envoie les senders prouvés quand la sémantique est connue.

/// <summary>Base commune : panneau centré déplaçable + titre +
/// zone de texte + bouton fermer.</summary>
public abstract partial class ServicePanel : Control
{
    protected Control? Panel;
    protected Label? Body;

    /// <summary>Construit le panneau standard (à appeler dans
    /// _Ready). Si <paramref name="layout"/> est fourni et que le
    /// builder CEGUI le résout, la fenêtre native (chrome + titre +
    /// bouton × + drag) remplace le panneau générique — repli
    /// identique à avant sinon.</summary>
    protected void BuildPanel(string name, string title, int w = 420,
        int h = 260, string? layout = null)
    {
        var built = layout == null ? null
            : HudExtensions.BuildNativeWindow(this, layout, name,
                new Vector2(w, h));
        if (built != null)
        {
            Panel = built;
            built.Name = name;
        }
        else
        {
            var p = new Panel { Name = name };
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            p.Position = new Vector2(-w / 2, -h / 2);
            p.Size = new Vector2(w, h);
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.07f, 0.06f, 0.09f, 0.97f),
                BorderColor = new Color(0.537f, 0.517f, 0.46f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            AddChild(p);
            Panel = p;
            DragHelper.EnsureTitleBar(p, title);
            DragHelper.Persist(p, name);
            var close = new Button { Text = UiTextEn.Translate("Close") };
            close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            close.Position = new Vector2(-45, -34);
            close.Size = new Vector2(90, 26);
            close.Pressed += () => Visible = false;
            p.AddChild(close);
        }

        Body = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        Body.Position = new Vector2(14, 40);
        Body.Size = new Vector2(
            Mathf.Max(Panel!.Size.X - 28, 60),
            Mathf.Max(Panel.Size.Y - 90, 30));
        Panel.AddChild(Body);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    protected abstract void OnPacket(NetMessage m);

    protected void Show(string text)
    {
        if (Body != null) Body.Text = text;
        Visible = true;
    }

    protected void Chat(string t) =>
        (GetParent() as HudRoot)?.AddChat(7, "", t);

    protected static void Send(NetMessage m)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(m,
                System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[svc] send : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}

/// <summary>Territoires de famille — 515 query, 516 info ;
/// rejoindre via C→S 190 <see cref="ZoneServerTerritoryJoin"/>.</summary>
public partial class TerritoryWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__territory", UiTextEn.Translate("Territories"),
            layout: "TerritoryMainWnd.layout");

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session) : les infos vivent sinon dans les 515/516.</summary>
    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Territory war\n\nAurea — held by Reborn\n") +
            UiTextEn.Translate("Crystal Utopia — contested (Sat 20:00)\n\n") +
            UiTextEn.Translate("Join via the family administrator."));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientTerritoryQuery q:
                Show(UiTextEn.Translate($"Territories: {q.Field1} {q.Field2} ") +
                    $"{q.Field3} {q.Field4}");
                break;
            case ZoneClientTerritoryInfo i:
                Show(UiTextEn.Translate($"Info : {i.Field1} {i.Field2} {i.Field3} ") +
                    $"{i.Field4}");
                break;
            case ZoneClientTerritoryEnd:
                Chat(UiTextEn.Translate("Territory finished."));
                break;
            case ZoneClientTerritoryKilledInfo k:
                Chat($"Frag territoire : {k.Field1}");
                break;
        }
    }
}

/// <summary>Colosseum / gladiateurs — 527 entrée, 528 résultat join,
/// 529 team info, 530 leave/join, 450-452 équipe gladiateur ;
/// entrée via C→S 199 <see cref="ZoneServerColosseumJoinRequest"/>.</summary>
public partial class ColosseumWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__colosseum", UiTextEn.Translate("Colosseum"),
            layout: "ArenaMain.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Gladiator arena\n\nMap : Arena of Souls\n") +
            UiTextEn.Translate("Queue : 12 waiting — 3v3\n\n") +
            UiTextEn.Translate("Join the queue to enter."));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterColosseum e:
                Show(UiTextEn.Translate($"Colosseum entry ({e.Value}) — join the queue."));
                break;
            case ZoneClientColosseumJoinResult j:
                Chat(UiTextEn.Translate($"Colosseum join: {j.Value}"));
                break;
            case ZoneClientColosseumTeamInfo t:
                Show(UiTextEn.Translate($"Team: {t.Field1} {t.Field2} {t.Field3}"));
                break;
            case ZoneClientGladiatorTeamInfo g:
                Show(UiTextEn.Translate($"Gladiators: {g.Field1} {g.Field2} {g.Field3}"));
                break;
            case ZoneClientDisbandGladiatorTeam:
                Chat(UiTextEn.Translate("Gladiator team disbanded."));
                break;
        }
    }
}

/// <summary>Recrutement d'instance — 553 résultat, 564 dialog ;
/// candidature via C→S 220 <see cref="ZoneServerRecruitInstance"/>.</summary>
public partial class RecruitWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__recruit", "Recrutement d'instance",
            layout: "Recruit.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Instance recruitment\n\n") +
            UiTextEn.Translate("Purpose : dungeon run\nDemand : lv 40+\n\n") +
            UiTextEn.Translate("Auto-join / start recruit via the checkboxes."));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientOpenInstanceDialog d:
                Show(UiTextEn.Translate($"Instance creation: {d.Field1}/{d.Field2}"));
                break;
            case ZoneClientRecruitInstanceResult r:
                Show($"Recrutement : {r.Field1}/{r.Field2}");
                break;
        }
    }
}

/// <summary>Fast dress (tenues rapides) — 594 update ; applique via
/// C→S 244 <see cref="ZoneServerApplyFastDressData"/>.</summary>
public partial class FastDressWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__fastdress", "Tenues rapides",
            layout: "DressRoomUI.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Fast dress\n\nSave and apply outfit presets ") +
            UiTextEn.Translate("from the dressing room."));

    protected override void OnPacket(NetMessage m)
    {
        if (m is ZoneClientUpdateFastDressData d)
            Show($"Tenue {d.Field1}/{d.Field2} — {d.Field3} {d.Field4}");
    }
}

/// <summary>Classements — 385 open exploit rank, 521 rank info,
/// 562 killed rank, monde 59.</summary>
public partial class RankWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__rank", UiTextEn.Translate("Rankings"),
            layout: "RankMainWnd.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Rankings\n\n1. Player — 9 850 pts\n") +
            UiTextEn.Translate("2. Hero — 8 120 pts\n3. Knight — 7 400 pts"));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientOpenExploitRank:
                Show("Classement des exploits.");
                break;
            case ZoneClientRankInfo r:
                Show($"Rang : {r.Field2} — {r.Field1}/{r.Field3} " +
                    $"{r.Field4}");
                break;
            case ZoneClientFamilyBattleKilledRank k:
                var lines = new System.Text.StringBuilder(
                    "Frags famille :\n");
                foreach (var e in k.Entries)
                    lines.AppendLine(
                        $"{e.Rank}. {e.PlayerName} ({e.FamilyName}) " +
                        $"— {e.Kills}");
                Show(lines.ToString());
                break;
            case WorldClientQueryExploitRank w:
                Show(UiTextEn.Translate($"Exploit rank : {w.Field1} {w.Field2} {w.Field3}"));
                break;
        }
    }
}
