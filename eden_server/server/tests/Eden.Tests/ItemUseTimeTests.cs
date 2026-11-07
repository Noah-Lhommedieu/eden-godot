using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Xunit;

namespace Eden.Tests;

/// <summary>Canalisation d'usage d'item (407 ClientUseItemTime /
/// 408 ClientUseItemTimeBreak — branche temporisée de
/// lapis::CItemFactory::DoUseItem @0x80b8a10 + CEventUseItemTime
/// @0x819cb30). Le use-time vit en S_Item <b>col20</b>
/// (→ CItemData+0x54, décisecondes) : ≠0 → le serveur émet 407,
/// garde l'item en char+0x90 (EntryState.PendingItemUse) et
/// l'usage réel n'arrive qu'à l'échéance ; interruption → 408.</summary>
public class ItemUseTimeTests
{
    private static readonly string DbDir =
        TestPaths.ServerUsData + "/db_decoded";

    [Fact]
    public void Parser_Col20_IsUseTime()
    {
        var items = ItemParser.Parse(DbDir + "/S_Item.ini");
        var byId = items.ToDictionary(i => i.Id);

        // Feu d'artifice (consommable UseType 2) — durée 10 ds = 1 s,
        // recoupé avec c_item col20 du client.
        Assert.True(byId.TryGetValue(34848, out var firework));
        Assert.Equal(10, firework!.UseTimeDs);
        Assert.Equal(2, firework.UseType);

        // Les items temporisés sont des consommables UseType 2
        // (UseItemParam ne crée un control que pour 1/2/3/5/6 et les
        // data ne temporisent que des type 2) — 2228 rows en
        // db_decoded, durées 3-100 ds (outils, passeports, feux).
        var timed = items.Where(i => i.UseTimeDs > 0).ToList();
        Assert.InRange(timed.Count(i => i.UseType == 2), 2000, 2400);
        Assert.All(timed, i => Assert.InRange(i.UseTimeDs, 1, 6000));

        // Un consommable instantané ordinaire → 0.
        var instant = items.First(i => i.UseType == 2 && i.UseTimeDs == 0);
        Assert.Equal(0, instant.UseTimeDs);
    }

    [Fact]
    public void Wire_407_Layout_Is_Item_Container_Slot()
    {
        // Ctor natif DoUseItem @0x80b8a10 :
        // [u32 item_id][s16 GetContainerIndex][s16 slot].
        var outp = new OutBinStream();
        new CNC_CZ_ClientUseItemTime
        { Field1 = 34848u, Field2 = 0, Field3 = 7 }.Serialize(outp);
        var body = outp.ToArray();
        Assert.Equal(8, body.Length);
        Assert.Equal(34848, BitConverter.ToInt32(body, 0));
        Assert.Equal((short)0, BitConverter.ToInt16(body, 4));
        Assert.Equal((short)7, BitConverter.ToInt16(body, 6));

        var back = new CNC_CZ_ClientUseItemTime();
        back.Deserialize(new InBinStream(body));
        Assert.Equal(34848u, back.Field1);
        Assert.Equal((short)0, back.Field2);
        Assert.Equal((short)7, back.Field3);
    }

    [Fact]
    public void Wire_408_HasEmptyBody()
    {
        // ClientUseItemTimeBreak — aucun champ (CheckCastBreak natif
        // envoie la commande nue ; distinct du break de sort 287 qui
        // porte caster/spell/reason).
        var outp = new OutBinStream();
        new CNC_CZ_ClientUseItemTimeBreak().Serialize(outp);
        Assert.Empty(outp.ToArray());
        new CNC_CZ_ClientUseItemTimeBreak()
            .Deserialize(new InBinStream(Array.Empty<byte>()));
    }
}
