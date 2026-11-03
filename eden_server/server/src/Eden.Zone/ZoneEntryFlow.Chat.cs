using System.Text;
using Eden.Core.Data;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Chat zone — NCID 34 `CNE_CZ_ZoneServerChannelMsg`.
/// Wire natif (Deserialize @0x82415xx + Execute @0x82412xx) :
/// `[u8 channel][str target][str msg][s16][s16][u32 blobLen][blob]`.
///
/// Routage natif (CPlayerFactory::ChannelMsg @0x81290xx) :
/// - channel 0 (say) → 268 `CNC_CZ_ClientSay` broadcast can-see + écho ;
/// - channel 1/3/4/5 → relayé au MissionServer (whisper/party/guild) ;
/// - autres → relayé au WorldServer (CNC_ZW_WorldServerChannelMsg).
/// En mono-zone on émet l'écho client directement : 268 pour say,
/// 327 `CNC_CZ_ClientChannelMsg` `[u8 ch][str sender][str msg][u32 len][blob]`
/// pour les autres canaux.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>HandleChannelMsgAsync — parse le 34, route say en
    /// broadcast node et écho les autres canaux (mono-joueur).</summary>
    private async Task HandleChannelMsgAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || !st.Migrated) return;
        if (payload.Length < 5) return;

        var o = 0;
        var channel = payload[o]; o += 1;                    // u8 : canal
        var target = ReadStr(payload, ref o);                // str : cible
        var msg = ReadStr(payload, ref o);                   // str : message
        if (o + 8 > payload.Length) return;
        o += 2; o += 2;                                      // s16 + s16 (usage inconnu — transmis au natif)
        var blobLen = BitConverter.ToUInt32(payload, o); o += 4; // u32 : taille blob
        if (o + blobLen > payload.Length) return;
        var blob = new byte[blobLen];
        Array.Copy(payload, o, blob, 0, blob.Length);        // blob : données liées (item link…)

        var name = st.CharName ?? "";
        if (channel == 0)
        {
            // say → 268 ClientSay [u32 charUid][str msg][u32 len][blob]
            var (sayOp, sayPayload) = PreparePacket(new CNC_CZ_ClientSay
            {
                Field1 = st.CharId, Field2 = msg,
                Field3 = blobLen, Blob = blob,
            });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    !ost.Migrated || ost.NodeId != st.NodeId ||
                    ost.InstanceGroup != st.InstanceGroup) continue;
                await SendPreparedToAsync(s, sayOp, sayPayload, token);
            }
        }
        else if (channel == 3
                 && _services.Guilds.GetGuildOf((int)st.CharId)
                     is { } guild)
        {
            // Canal guilde — natif : la zone relaie au MissionServer
            // (TC_ChannelGuild → CPlayerFactory::ChannelMsg ch=3
            // @0x83c9210), qui rediffuse aux membres en ligne de la
            // famille (SendCommandToFamilyMember). Mono-zone : on
            // émet 327 directement à chaque membre migré — l'auteur
            // compris (il fait partie des membres natifs).
            var (pktOp, pktPayload) = PreparePacket(
                new CNC_CZ_ClientChannelMsg
                {
                    Field1 = channel, Field2 = name,
                    Field3 = msg, Blob = blob,
                });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost)
                    || !ost.Migrated) continue;
                if (_services.Guilds.GetGuildOf((int)ost.CharId)?.Id
                    != guild.Id) continue;
                await SendPreparedToAsync(s, pktOp, pktPayload, token);
            }
        }
        else
        {
            // autres canaux → 327 ClientChannelMsg — écho.
            await session.SendAsync(new CNC_CZ_ClientChannelMsg
            {
                Field1 = channel, Field2 = name,
                Field3 = msg, Blob = blob,
            }, token);
        }
        EdenLog.Write($"[Zone-Entry] CHAT ch={channel} " +
            $"{name}: {msg}");
    }

    /// <summary>Lit une `str` protocole : [u16 len][bytes Big5].</summary>
    private static string ReadStr(byte[] b, ref int o)
    {
        if (o + 2 > b.Length) return "";
        var len = BitConverter.ToUInt16(b, o); o += 2;
        if (o + len > b.Length) { o = b.Length; return ""; }
        var s = EdenEncoding.Big5.GetString(b, o, len); o += len;
        return s;
    }

    /// <summary>Écrit une `str` protocole : [u16 len][bytes Big5].</summary>
    private static void WriteStr(List<byte> body, string s)
    {
        var bytes = EdenEncoding.Big5.GetBytes(s);
        body.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
        body.AddRange(bytes);
    }
}
