using System.Linq;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.Hud;

/// <summary>Sondes L-items, montées par <see cref="InventoryWindow.Build"/>
/// quand la ligne de commande les demande :
/// <c>--tip-test=id,id,…[,--tip-shot=png]</c> pose les vrais tooltips
/// (<see cref="ItemTooltip.BuildForId"/>) côte à côte, logue leur texte
/// ligne à ligne et capture ;
/// <c>--bag-test=&lt;png&gt;</c> (session live) clone un sac, le
/// « double-clique » (chemin réel de l'inventaire), puis logue capacité
/// avant/après et capture l'inventaire.</summary>
public sealed partial class BagTipProbe : Node
{
    private readonly HudRoot _hud;
    private readonly int[] _tipIds;
    private readonly string _tipShot;
    private readonly string _bagShot;
    private int _frame;
    private int _bagItem = 33734;   // --bag-item=<id> (Bagage du Voyageur)
    private int _capBefore, _capAfter;

    public BagTipProbe(HudRoot hud, int[] tipIds, string tipShot,
        string bagShot)
    {
        _hud = hud;
        _tipIds = tipIds;
        _tipShot = tipShot;
        _bagShot = bagShot;
        Name = "__bagtip_probe__";
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--bag-item=") &&
                int.TryParse(a[11..], out var b)) _bagItem = b;
    }

    private InventoryWindow? Inv => _hud.Inventory as InventoryWindow;

    private static void Texts(Node n, List<string> into)
    {
        if (n is Label l && l.Text.Length > 0) into.Add(l.Text);
        foreach (var c in n.GetChildren()) Texts(c, into);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_tipIds.Length > 0) TipStep();
        if (_bagShot.Length > 0) BagStep();
    }

    private void TipStep()
    {
        if (_frame == 60)
        {
            var layer = new Control { Name = "__tips__" };
            layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var bg = new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f) };
            bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(bg);
            var x = 6f;
            foreach (var id in _tipIds)
            {
                var tip = ItemTooltip.BuildForId(id);
                tip.Position = new Vector2(x, 6);
                layer.AddChild(tip);
                x += 316;
                var lines = new List<string>();
                Texts(tip, lines);
                GD.Print($"[tip-test] {id} {TextResolver.ItemName(id)} -> " +
                         string.Join(" // ", lines.Select(l =>
                             l.Replace("\n", " "))));
            }
            _hud.AddChild(layer);
        }
        if (_frame == 120 && _tipShot.Length > 0)
        {
            Snap(_tipShot, "tip-test");
            GetTree().Quit();
        }
    }

    private void BagStep()
    {
        var inv = Inv;
        if (inv == null) return;
        switch (_frame)
        {
            case 120:
                _hud.OpenWindow("inventory");
                _capBefore = TotalCapacity();
                GD.Print($"[bag-test] avant : cases={_capBefore} " +
                         $"sacs équipés={EquippedDump()}");
                inv.SendProbe(new EdenClient.Protocol.ZoneTextCommand
                { Text = $"//clone_item {_bagItem}" });
                break;
            case 300:
                var it = inv.KnownSlots.Values.FirstOrDefault(
                    i => i.ItemId == _bagItem && i.Bag == 0);
                GD.Print($"[bag-test] sac {_bagItem} dans l'inventaire : " +
                         (it == null ? "ABSENT" : $"slot {it.Slot}"));
                if (it != null) inv.OnCellDoubleClick(it.Bag, it.Slot);
                break;
            case 480:
                _capAfter = TotalCapacity();
                GD.Print($"[bag-test] après : cases={_capAfter} " +
                         $"sacs équipés={EquippedDump()} " +
                         $"(+{_capAfter - _capBefore})");
                break;
            case 540:
                Snap(_bagShot, "bag-test");
                break;
        }
    }

    private static int TotalCapacity()
    {
        lock (EdenClient.Protocol.NativeInventoryParser.Capacities)
            return EdenClient.Protocol.NativeInventoryParser.Capacities
                .Values.Sum();
    }

    private static string EquippedDump()
    {
        lock (EdenClient.Protocol.NativeInventoryParser.EquippedBags)
            return string.Join(",", EdenClient.Protocol.NativeInventoryParser
                .EquippedBags.Select(k =>
                    $"{k.Key}:{k.Value.ItemId}/{k.Value.Capacity}"));
    }

    private void Snap(string path, string tag)
    {
        RenderingServer.ForceDraw();
        var img = GetViewport().GetTexture()?.GetImage();
        img?.SavePng(path);
        GD.Print($"[{tag}] shot {path}");
    }
}
