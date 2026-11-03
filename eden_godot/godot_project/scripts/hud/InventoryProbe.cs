using System.Linq;
using Godot;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Sonde <c>--inv-test</c> : exerce move (7→277), split (84),
/// tri (ArrangeItem→620) et équipement (7+8→282) avec les vrais paquets
/// serveur, logue l'état avant/après. Le jeu reste ouvert (capture
/// Flameshot ensuite).</summary>
public sealed partial class InventoryProbe : Node
{
    private readonly HudRoot _hud;
    private int _frame;
    private int _a, _empty, _stack, _gearSlot;
    private int _mountItem = 20051;   // --inv-mount=<id> : autre monture

    public InventoryProbe(HudRoot hud)
    {
        _hud = hud;
        Name = "__inv_probe__";
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--inv-mount=") &&
                int.TryParse(a[12..], out var m)) _mountItem = m;
    }

    private InventoryWindow? Inv => _hud.Inventory as InventoryWindow;

    private string Dump() => string.Join(" ", Inv!.KnownSlots
        .Where(k => k.Key.Bag == 0).OrderBy(k => k.Key.Slot).Take(14)
        .Select(k => $"{k.Key.Slot}:{k.Value.ItemId}x{k.Value.Amount}/{k.Value.Durability}"));

    /// <summary>Double-clic sur le 1er exemplaire de l'item (tous sacs).</summary>
    private void UseFirst(int itemId, string what)
    {
        var it = Inv!.KnownSlots.Values.FirstOrDefault(i => i.ItemId == itemId);
        GD.Print($"[inv-test] utilise {what} item={itemId} -> {(it == null ? "ABSENT" : $"sac{it.Bag} slot{it.Slot} x{it.Amount}/{it.Durability}")}");
        if (it != null) Inv.OnCellDoubleClick(it.Bag, it.Slot);
    }

    public override void _Process(double delta)
    {
        _frame++;
        var inv = Inv;
        if (inv == null) return;
        switch (_frame)
        {
            case 90:
                _hud.OpenWindow("inventory");
                var bag0 = inv.KnownSlots.Where(k => k.Key.Bag == 0)
                    .Select(k => k.Value).OrderBy(i => i.Slot).ToList();
                GD.Print($"[inv-test] parse312 echec={EdenClient.Protocol.NativeInventoryParser.LastFailure ?? "aucun"}");
                GD.Print($"[inv-test] par sac: {string.Join(" ", inv.KnownSlots.Values.GroupBy(i => i.Bag).Select(g => $"sac{g.Key}={g.Count()}"))}");
                GD.Print($"[inv-test] items={bag0.Count} {Dump()}");
                if (bag0.Count == 0) { SetProcess(false); return; }
                GD.Print("[inv-test] 38572: " + string.Join(" ", bag0.Where(i => i.ItemId == 38572)));
                _a = bag0[0].Slot;
                _empty = Enumerable.Range(0, 200).First(i =>
                    !inv.KnownSlots.ContainsKey((0, i)));
                _stack = bag0.FirstOrDefault(i => i.Amount > 1 || i.ItemId == 193 && i.Durability > 1)?.Slot ?? -1;
                if (_stack >= 0) GD.Print($"[inv-test] stack candidat {inv.KnownSlots[(0, _stack)]}");
                _gearSlot = bag0.FirstOrDefault(i =>
                    InventoryWindow.LookupDef(i.ItemId) is { } d &&
                    EdenClient.Protocol.ZoneItemActions.IsPlayerEquippableKind(d.Kind) &&
                    EdenClient.Protocol.ZoneItemActions.AllowedLocations(d.EquipLoc).Length > 0)?.Slot ?? -1;
                GD.Print($"[inv-test] move {_a}->{_empty}");
                inv.OnCellDrop(0, _a, 0, _empty, false);
                break;
            case 150:
                GD.Print($"[inv-test] after move: a={inv.KnownSlots.ContainsKey((0, _a))} dst={inv.KnownSlots.ContainsKey((0, _empty))}");
                _stack = inv.KnownSlots.Values.Where(i => i.Bag == 0 && i.ItemId == 193).Select(i => i.Slot).DefaultIfEmpty(-1).First();
                if (_stack >= 0)
                {
                    var amt = Math.Max(inv.KnownSlots[(0, _stack)].Amount, inv.KnownSlots[(0, _stack)].Durability);
                    var dst = Enumerable.Range(0, 200).First(i =>
                        !inv.KnownSlots.ContainsKey((0, i)));
                    GD.Print($"[inv-test] split slot {_stack} x{amt} -> {dst} x1");
                    inv.SplitDirect(0, _stack, dst, 1);
                }
                else GD.Print("[inv-test] split : aucun stack >1");
                break;
            case 210:
                GD.Print($"[inv-test] after split: {Dump()}");
                GD.Print("[inv-test] tri");
                inv.SortBag();
                break;
            case 270:
                GD.Print($"[inv-test] after sort: {Dump()}");
                if (_gearSlot >= 0)
                {
                    var g = Enumerable.Range(0, 200).Select(i => inv.KnownSlots.GetValueOrDefault((0, i)))
                        .FirstOrDefault(i => i != null && InventoryWindow.LookupDef(i.ItemId) is { } d &&
                            EdenClient.Protocol.ZoneItemActions.IsPlayerEquippableKind(d.Kind) &&
                            EdenClient.Protocol.ZoneItemActions.AllowedLocations(d.EquipLoc).Length > 0);
                    if (g != null)
                    {
                        _gearSlot = g.Slot;
                        GD.Print($"[inv-test] avant équipe n24170={inv.KnownSlots.Values.Count(i => i.ItemId == 24170)}"); GD.Print($"[inv-test] équipe item {g.ItemId} slot {g.Slot}");
                        inv.OnCellDoubleClick(0, g.Slot);
                    }
                }
                break;
            case 330:
                GD.Print($"[inv-test] after equip: n24170={inv.KnownSlots.Values.Count(i => i.ItemId == 24170)}");
                GD.Print("[inv-test] déséquipe loc 7");
                inv.UnequipDirect(7);
                break;
            case 100:
                // Vrai clic droit sur la potion (utiliser) + capture cooldown.
                var pot = inv.KnownSlots.Values.FirstOrDefault(i => i.ItemId == 32631 && i.Bag == 0);
                if (pot != null && inv.CellCenter(0, pot.Slot) is { } pos)
                {
                    GD.Print($"[inv-test] clic droit réel potion slot {pot.Slot} à {pos}");
                    var mm = new InputEventMouseMotion { Position = pos, GlobalPosition = pos };
                    Input.ParseInputEvent(mm);
                    Input.ParseInputEvent(new InputEventMouseButton
                    { Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Right, Pressed = true });
                    Input.ParseInputEvent(new InputEventMouseButton
                    { Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Right, Pressed = false });
                }
                else GD.Print($"[inv-test] potion non visible pot={pot} ");
                break;
            case 130:
                Shot("t2_cooldown_live");
                break;
            case 400:
                GD.Print($"[inv-test] after unequip: n24170={inv.KnownSlots.Values.Count(i => i.ItemId == 24170)}");
                break;
            case 480:
                UseFirst(_mountItem, "monture (monter)");
                break;
            case 850:
                _t0 = Time.GetTicksMsec();
                _step = 1;
                break;
        }
        if (_step > 0) Phase2();
    }

    /// <summary>Capture Flameshot de l'écran réel (godot-shots/, non commité).</summary>
    private static void Shot(string name)
    {
        var dir = ProjectSettings.GlobalizePath("res://../godot-shots");
        OS.Execute("bash", new[] { "-c", $"flameshot full --raw > '{dir}/{name}.png'" });
        GD.Print($"[inv-test] flameshot {name}");
    }

    private ulong _t0;
    private int _step;

    /// <summary>Statuts de contrôle (temps réel : le serveur tick à 1 s,
    /// le client tourne à des centaines de fps).</summary>
    private void Phase2()
    {
        var t = Time.GetTicksMsec() - _t0;
        string L() => EdenClient.Combat.PlayerStateLocks.Describe();
        switch (_step)
        {
            case 1 when t > 3000:
                _hud.OpenWindow("inventory");   // ferme : vue dégagée du perso
                _step = 12; break;
            case 12 when t > 12000:
                GD.Print($"[inv-test] barre sorts monture 545: [{string.Join(",", ZoneClientRideSpell.Latest)}]");
                UseFirst(_mountItem, "monture (descendre)");
                _step = 10; break;
            case 10 when t > 15000:
                Shot("t2_mounted_live");
                _t0 = Time.GetTicksMsec();
                _step = 11; break;
            case 11:
                GD.Print("[inv-test] statut 10041 (2055: move/atk/magic/kungfu) 6 s");
                _ = _hud.SendZoneAsync?.Invoke(new ZoneTextCommand
                { Text = "//self_use_effect 10041 6" });
                _step = 2; break;
            case 2 when t > 2500:
                GD.Print($"[inv-test] verrous actifs: {L()}");
                Shot("u8_stun_live");
                UseFirst(32631, "potion (item non verrouillé par 10041)");
                _step = 3; break;
            case 3 when t > 9500:
                GD.Print($"[inv-test] verrous après expiration 10041: {L()}");
                _ = _hud.SendZoneAsync?.Invoke(new ZoneTextCommand
                { Text = "//self_use_effect 10055 6" });
                _step = 4; break;
            case 4 when t > 12000:
                GD.Print($"[inv-test] verrous 10055 (NoUseItem): {L()}");
                UseFirst(32631, "potion pendant NoUseItem (doit être bloquée)");
                _step = 5; break;
            case 5 when t > 19000:
                GD.Print($"[inv-test] verrous après expiration 10055: {L()}");
                GD.Print("[inv-test] done");
                SetProcess(false);
                _step = 0; break;
        }
    }
}
