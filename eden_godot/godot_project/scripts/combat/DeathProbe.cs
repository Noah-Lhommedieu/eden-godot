using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Session;
using EdenClient.Screens;

namespace EdenClient.Combat;

/// <summary>
/// Banc QA de la mort du joueur (<c>--death-test[=die|here|save|item|life|dgsave|dghere]</c>) : en jeu live, envoie
/// <c>//suicide</c> (GM), attend le 305 self, puis — sauf <c>die</c> — demande le revive du type voulu
/// (NCID 20) et attend le 306. Sortie : lignes <c>[death-test]</c> avec l'état <see cref="PlayerVitals"/>.
/// <c>die</c> laisse le perso mort (test de persistance au relog). Sans effet sans l'argument.
/// </summary>
public partial class DeathProbe : Node
{
    private string _mode = "here";
    private double _t;
    private int _state;      // 0 attente entrée, 1 suicide envoyé, 2 mort vue, 3 revive envoyé, 4 fini
    private double _stateT;
    private bool _moved;

    public static bool Requested(out string mode)
    {
        mode = "here";
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a == "--death-test") return true;
            if (a.StartsWith("--death-test=")) { mode = a[13..]; return true; }
        }
        return false;
    }

    public override void _Ready() => Requested(out _mode);

    public override void _Process(double delta)
    {
        _t += delta; _stateT += delta;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true || _state == 4) return;
        switch (_state)
        {
            case 0 when _t > 12 && _mode.StartsWith("dg") && !_moved:
                // Donjon : se téléporte (GM) dans le node 701 avant de mourir.
                _moved = true;
                Send(new ZoneTextCommand { Text = "//transport_node 701" });
                GD.Print("[death-test] //transport_node 701 envoyé");
                _t = 0;
                break;
            case 0 when _t > 12:
                if (PlayerVitals.IsDead)
                {
                    GD.Print("[death-test] déjà mort à l'entrée (état persistant) — OK");
                    Next(2);
                    break;
                }
                // Feuilles de test : item = Locus 40036, life = Vie 40035.
                if (_mode is "item" or "life")
                    Send(new ZoneTextCommand
                        { Text = _mode == "item" ? "//clone_item 40036" : "//clone_item 40035" });
                Send(new ZoneTextCommand { Text = "//suicide" });
                GD.Print("[death-test] //suicide envoyé");
                Next(1);
                break;
            case 1 when PlayerVitals.IsDead:
                GD.Print("[death-test] 305 reçu : IsDead=true");
                Next(2);
                break;
            case 1 when _stateT > 10:
                GD.Print("[death-test] ÉCHEC : pas de mort après 10 s");
                Next(4);
                break;
            case 2 when _stateT > 6:
                if (_mode == "die") { GD.Print("[death-test] mode die : reste mort"); Next(4); break; }
                var type = _mode switch { "save" or "dgsave" => 0, "item" => 5, "life" => 6, _ => 1 };
                Send(new ZoneReviveRequest { ReviveType = type });
                GD.Print($"[death-test] revive type={type} envoyé");
                Next(3);
                break;
            case 3 when !PlayerVitals.IsDead:
                GD.Print("[death-test] 306 reçu : IsDead=false — OK");
                Next(4);
                break;
            case 3 when _stateT > 8:
                GD.Print($"[death-test] revive sans effet après 8 s (IsDead={PlayerVitals.IsDead}) — refusé par le serveur");
                Next(4);
                break;
        }
    }

    private void Next(int st) { _state = st; _stateT = 0; }

    private static void Send(NetMessage m) =>
        _ = SessionCarry.Session?.SendZoneAsync(m, CancellationToken.None);
}
