using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Tutoriel nouveau joueur (NCID 218 NewPlayerGuide) et
/// flags bot client (164 UpdateBotData). Références natives :
/// <c>CNE_CZ_ZoneServerNewPlayerGuide::Execute</c> @0x826aed0,
/// <c>TriggerNewPlayerGuide</c> @0x8422fe0, <c>CloseNewPlayerGuide</c>
/// @0x84240b0 — map char+0xc18 {id → (trigger, closed)} persistée
/// dans <c>new_player_guide</c> ; <c>UpdateBotData::Execute</c>
/// @0x829a730 — char+0xaa8/0xaa9 = deux u8.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>218 <c>[s16 op][u16 guideId]</c> — op 1 = trigger
    /// (count = max+1, skip si déjà déclenché), op 0 = close
    /// (requis : triggeré et pas clos), op 2 = skip (ferme tout ce
    /// qui est ouvert). Aucune émission client : le natif ne fait
    /// que compter et persister via la commande DBA.</summary>
    private async Task HandleNewPlayerGuideAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0) return;
        var op = BitConverter.ToInt16(payload, 0);      // s16 : op
        var guideId = BitConverter.ToUInt16(payload, 2); // u16 : id

        if (!st.NewPlayerGuideLoaded)
        {
            var (triggers, closed) = await _services.GuideRepo
                .LoadAsync((int)st.CharId);
            foreach (var kv in triggers)
                st.NewPlayerGuide[kv.Key] =
                    (kv.Value, closed.GetValueOrDefault(kv.Key));
            foreach (var kv in closed)
                if (!st.NewPlayerGuide.ContainsKey(kv.Key))
                    st.NewPlayerGuide[kv.Key] = (0, kv.Value);
            st.NewPlayerGuideLoaded = true;
        }

        switch (op)
        {
            case 1: // TriggerNewPlayerGuide
                if (st.NewPlayerGuide.TryGetValue(guideId, out var g)
                    && g.Trigger != 0)
                    return;              // « already triggered » natif
                var next = st.NewPlayerGuide.Count == 0
                    ? (ushort)1
                    : (ushort)(st.NewPlayerGuide.Values
                          .Max(v => v.Trigger) + 1);
                st.NewPlayerGuide[guideId] =
                    (next, st.NewPlayerGuide
                        .GetValueOrDefault(guideId).Closed);
                await _services.GuideRepo.SetFieldAsync(
                    (int)st.CharId, guideId, false, next);
                break;

            case 0: // CloseNewPlayerGuide — doit être triggeré et
                    // pas encore clos (sinon retour silencieux).
                if (!st.NewPlayerGuide.TryGetValue(guideId, out var c)
                    || c.Trigger == 0 || c.Closed != 0)
                    return;
                var nextClosed = st.NewPlayerGuide.Count == 0
                    ? (ushort)1
                    : (ushort)(st.NewPlayerGuide.Values
                          .Max(v => v.Closed) + 1);
                st.NewPlayerGuide[guideId] = (c.Trigger, nextClosed);
                await _services.GuideRepo.SetFieldAsync(
                    (int)st.CharId, guideId, true, nextClosed);
                break;

            case 2: // SkipNewPlayerGuide — ferme toutes les entrées
                    // triggerées non closes.
                var maxClosed = st.NewPlayerGuide.Count == 0
                    ? (ushort)0
                    : st.NewPlayerGuide.Values.Max(v => v.Closed);
                foreach (var id in st.NewPlayerGuide
                             .Where(kv => kv.Value.Trigger != 0
                                      && kv.Value.Closed == 0)
                             .Select(kv => kv.Key).ToList())
                {
                    maxClosed++;
                    st.NewPlayerGuide[id] =
                        (st.NewPlayerGuide[id].Trigger, maxClosed);
                    await _services.GuideRepo.SetFieldAsync(
                        (int)st.CharId, id, true, maxClosed);
                }
                break;
        }
    }

    /// <summary>164 UpdateBotData <c>[u8][u8]</c> — deux booléens
    /// d'auto-jeu persistés en char+0xaa8/0xaa9 natifs (usage
    /// ultérieur : auto-combat du client).</summary>
    private Task HandleUpdateBotDataAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2 || st.CharId == 0)
            return Task.CompletedTask;
        st.BotFlag1 = payload[0] != 0;
        st.BotFlag2 = payload[1] != 0;
        return Task.CompletedTask;
    }

    /// <summary>162 QueryBattlefieldData [] → 482
    /// <c>CNC_CZ_ClientUpdateBFData</c> — Execute natif @0x8274d80 :
    /// sérialise le bloc char+0x97c[26] en <c>map&lt;char,int&gt;</c>
    /// ordonné → <c>[u16 count][count × {u8 idx][i32 val}]</c>.
    /// Indices 0-11 = battlefield_month, 12-23 = battlefield_career
    /// (même ordre de colonnes), 24-25 inutilisés natifs.</summary>
    private async Task HandleQueryBattlefieldDataAsync(
        NetSession session, EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        st.BattlefieldStats ??= await _services.BattlefieldRepo
            .LoadStatsAsync((int)st.CharId, st.CharName,
                (short)st.Level, (short)st.ClassId);
        var body = new Eden.Core.Networking.OutBinStream();
        body.WriteShort((short)st.BattlefieldStats.Length); // u16 n
        for (var i = 0; i < st.BattlefieldStats.Length; i++)
        {
            body.WriteByte((byte)i);                    // u8 : index
            body.WriteInt(st.BattlefieldStats[i]);      // i32 : valeur
        }
        await session.SendPacketAsync(482, body.ToArray(), token);
    }
}
