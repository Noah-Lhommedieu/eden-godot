using System.Text;
using Eden.Core.Game.Calendar;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Modifiers;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Session;
using Eden.Zone.Handlers;

namespace Eden.Zone.Features.Modifiers;

/// <summary>Opcode 9066 C→S (custom Reborn, sans corps) : « donne-moi les 21 prochains jours de l'éphéméride ».</summary>
public sealed class CNE_CZ_ZoneServerAlmanax : NetCommand
{
    public override int Opcode => 9066;
    public override string Name => "CNE_CZ_ZoneServerAlmanax";
    public override void Serialize(OutBinStream stream) { }
    public override void Deserialize(InBinStream stream) { }
}

/// <summary>
/// Éphéméride affichée au joueur. S→C 9066 : <c>[i32 jourDeResetAujourdhui][u8 n]</c> puis n × <c>[i32 jour][i32 modèle][u8 forcé]
/// [str nom][str détail]</c> (str = <c>[u16 len][UTF-8]</c>). Aucune logique côté client : tout est calculé ici.
/// </summary>
public static class AlmanaxHandler
{
    public const ushort ResponseNcid = 9066;

    public static void Register(PacketDispatcher d, ZoneServices s) =>
        d.RegisterChar(() => new CNE_CZ_ZoneServerAlmanax(),
            async (session, msg, charId, token) =>
                await session.SendPacketAsync(ResponseNcid, Build(AlmanaxProvider.Calendar, GameTime.Clock.ResetDayNow()), token));

    public static byte[] Build(AlmanaxCalendar calendar, int today)
    {
        var days = calendar.Window(today);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(today); w.Write((byte)days.Count);
        foreach (var d in days)
        {
            w.Write(d.ResetDay); w.Write(d.Template.Id); w.Write((byte)(d.Forced ? 1 : 0));
            WriteStr(w, d.Template.NameFr); WriteStr(w, Detail(d.Template));
        }
        return ms.ToArray();
    }

    /// <summary>Effet chiffré en clair : « +25 % EXP de personnage (Monde ouvert) ; ×0,8 durée de craft ».</summary>
    public static string Detail(AlmanaxTemplate t) => string.Join(" ; ", t.Rules.Select(r =>
    {
        var label = ModifierKeys.All.FirstOrDefault(k => k.Id == r.KeyId)?.Description ?? r.KeyId;
        var v = r.Mode switch
        {
            ModifierMode.AddPercent => $"{r.Value:+0.##;-0.##} %",
            ModifierMode.AddFlat => $"{r.Value:+0.##;-0.##}",
            ModifierMode.Multiply => $"×{r.Value:0.##}",
            _ => $"= {r.Value:0.##}",
        };
        return $"{v} {label}{(r.When != null ? $" [{r.When}]" : "")}";
    }));

    private static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s); w.Write((ushort)b.Length); w.Write(b);
    }
}

