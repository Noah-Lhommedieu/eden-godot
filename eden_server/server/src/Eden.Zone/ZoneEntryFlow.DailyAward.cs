using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

/// <summary>Calendrier de connexion quotidien — NCID 258
/// (DailyAwardOP → 615 ClientUpdateDailyAwards). Références
/// natives : <c>Execute</c> @0x8246930, <c>DayOut</c> @0x8411360,
/// <c>DailyUpdate</c> @0x8420970, <c>DailyLoginBegin</c> @0x8420e10,
/// <c>GetDailyAward</c> @0x8420f50, <c>UpdateDailyAwardToDB</c>
/// @0x8420610. Persisté dans <c>daily_awards</c> (id = player_id) ;
/// activé par <c>daily_awards_set</c> tag=-2 flags=3.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Chargement à l'entrée — row daily_awards + flags,
    /// puis <c>DailyLoginBegin</c> natif : nouveau jour → status=0,
    /// cooldown=0, avance day_index si le bit courant est posé et
    /// qu'un def jour+2 existe (borne 28) ; login_time=now + save.</summary>
    private async Task LoadDailyAwardAsync(EntryState st,
        CancellationToken token)
    {
        st.DailyEnabled = await _services.DailyAwardRepo
            .IsEnabledAsync();
        var row = await _services.DailyAwardRepo
            .GetAsync((int)st.CharId);
        st.DailyDayIndex = (short)row.DayIndex;
        st.DailyLoginTime = row.LoginTime;
        st.DailyCooldown = row.CooldownTime;
        st.DailyAwardDays = (ulong)row.AwardDays;
        st.DailyAwardRecords = (ulong)row.AwardRecords;
        st.DailyLoginStatus = (byte)row.LoginStatus;
        st.DailySessionDelta = 0;
        st.DailySessionFlag = true;          // +0xdf4 session en cours
        st.DailyLoaded = true;
        if (!st.DailyEnabled) return;

        if (DayOut(st.DailyLoginTime))
        {
            st.DailyLoginStatus = 0;
            st.DailyCooldown = 0;
            // Même avance que la bascule DailyUpdate : bit du jour
            // posé + def jour+2 existant + borne 0x1c.
            var idx = st.DailyDayIndex;
            if (((st.DailyAwardDays >> (idx & 0x3f)) & 1) != 0
                && _services.GameData.DailyAwards.ContainsKey(idx + 2)
                && idx + 1 < 0x1c)
                st.DailyDayIndex = (short)(idx + 1);
        }
        st.DailyLoginTime = NowUnix();
        await SaveDailyAwardAsync(st);
    }

    /// <summary>258 DailyAwardOP <c>[s16 op][i32 param]</c> —
    /// désactivé → 615 type 4 ; op 0 = check de jour (param∈{1,2} →
    /// DailyUpdate(false) accumule, param==2 → réponse type 1) ;
    /// op 1 = requête → type 1 ; op 2 = claim du jour
    /// <paramref name="param"/> (1..32) → type 2.</summary>
    private async Task HandleDailyAwardOpAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6 || st.CharId == 0) return;
        var op = BitConverter.ToInt16(payload, 0);    // s16 : op
        var param = BitConverter.ToInt32(payload, 2); // i32 : param

        if (!st.DailyLoaded)
            await LoadDailyAwardAsync(st, token);
        if (!st.DailyEnabled)
        {
            await session.SendPacketAsync(615,
                DailyAwardPackets.Build(0, 0, 0, 0, 4), token);
            return;
        }

        switch (op)
        {
            case 0: // check de jour — Execute natif : DayOut puis
                    // DailyUpdate(true) INCONDITIONNEL, 615 toujours
                    // émis (type 5 si param==2, sinon 0) avec le
                    // champ +0x1e = local_26.
            {
                short local26;
                if (!DayOut(st.DailyLoginTime))
                {
                    // Même jour : flag = status≠0 (1) sauf
                    // status==0 → 0 via LAB_08246af8.
                    local26 = (short)(st.DailyLoginStatus != 0 ? 1 : 0);
                }
                else
                {
                    // Nouveau jour : param∈{1,2} → DailyUpdate(false),
                    // status=0, flag=0.
                    if ((uint)(param - 1) < 2)
                        DailyUpdate(st, false);
                    st.DailyLoginStatus = 0;
                    local26 = 0;
                }
                DailyUpdate(st, true);
                await SendDailyStateAsync(session, st,
                    (short)(param == 2 ? 5 : 0), local26, token);
                break;
            }
            case 1: // requête d'état — DailyUpdate(true) → type 1
                DailyUpdate(st, true);
                await SendDailyStateAsync(session, st, 1, 1, token);
                break;
            case 2: // claim du jour `param` → type 2 si accordé
                if (await ClaimDailyAwardAsync(session, st, (short)param,
                        token))
                    await SendDailyStateAsync(session, st, 2, 1, token);
                break;
        }
    }

    /// <summary>615 — corps natif [day_index][online_s][award_days]
    /// [award_records][type][flag] (online = cooldown+delta session).</summary>
    private Task SendDailyStateAsync(NetSession session, EntryState st,
        short type, short flag, CancellationToken token) =>
        session.SendPacketAsync(615, DailyAwardPackets.Build(
            st.DailyDayIndex,
            st.DailyCooldown + st.DailySessionDelta,
            st.DailyAwardDays, st.DailyAwardRecords, type, flag),
            token);

    /// <summary><c>DayOut</c> natif : vrai si le minuit local est
    /// passé entre login_time et maintenant (les deux comparent leur
    /// position dans la journée — secondes depuis minuit).</summary>
    private static bool DayOut(int loginTime)
    {
        var now = NowUnix();
        var nowMidnight = now - SecOfDay(now);
        var loginMidnight = loginTime - SecOfDay(loginTime);
        return loginMidnight <= nowMidnight
            && nowMidnight - loginMidnight > 0x1517f;   // > 86399 s
    }

    /// <summary><c>CCharacter::DailyUpdate</c> natif @0x8420970.
    /// Branche « accumuler » — même jour ET flag session consommé,
    /// ou <paramref name="rollover"/> faux : status=1 + login=now à
    /// la première passe, sinon delta = now−login ; puis si le def
    /// S_DailyAward[day+1] existe, jour non complété, non réclamé et
    /// required ≤ cooldown+delta+1 → bit award_days posé (SetBonusDay
    /// natif, bonus zone ignoré — cosmétique) et compteurs remis à
    /// zéro ; save (avec delta si jour incomplet, delta remis à 0
    /// sinon). Branche « bascule » sinon : compteurs remis à zéro,
    /// status = 0 si flag session sinon 1, login=now, day_index
    /// avance si le bit courant est posé et qu'un def jour+2 existe
    /// (borne jour+1 &lt; 0x1c), flag session consommé. Retourne 1 si
    /// bascule (valeur de retour native).</summary>
    private int DailyUpdate(EntryState st, bool rollover)
    {
        var now = NowUnix();
        var nowMidnight = now - SecOfDay(now);
        var loginMidnight = st.DailyLoginTime
            - SecOfDay(st.DailyLoginTime);
        var sameDay = nowMidnight < loginMidnight
            || nowMidnight - loginMidnight < 0x15180;

        if ((sameDay && !st.DailySessionFlag) || !rollover)
        {
            if (st.DailyLoginStatus == 0)
            {
                st.DailyLoginStatus = 1;
                st.DailySessionDelta = 0;
                st.DailyLoginTime = now;
            }
            else
                st.DailySessionDelta = now - st.DailyLoginTime;

            var day = st.DailyDayIndex;
            if (_services.GameData.DailyAwards.TryGetValue(day + 1,
                    out var def))
            {
                var done = Bit(st.DailyAwardDays, day);
                if (!done)
                {
                    if (!Bit(st.DailyAwardRecords, day)
                        && def.RequiredSeconds
                            <= st.DailyCooldown + st.DailySessionDelta + 1)
                    {
                        st.DailyAwardDays |= 1UL << (day & 0x3f);
                        st.DailyCooldown = 0;
                        st.DailySessionDelta = 0;
                        done = true;
                    }
                }
                if (!done)
                {
                    // UpdateDailyAwardToDB(delta) — persiste
                    // cooldown+delta SANS remettre delta à zéro.
                    _ = SaveDailyAwardAsync(st);
                    return 0;
                }
            }
            _ = SaveDailyAwardAsync(st);
            st.DailySessionDelta = 0;
            return 0;
        }

        st.DailyCooldown = 0;
        st.DailyLoginStatus = (byte)(st.DailySessionFlag ? 0 : 1);
        st.DailySessionDelta = 0;
        st.DailyLoginTime = now;
        var idx = st.DailyDayIndex;
        if (Bit(st.DailyAwardDays, idx)
            && _services.GameData.DailyAwards.ContainsKey(idx + 2)
            && idx + 1 < 0x1c)
            st.DailyDayIndex = (short)(idx + 1);
        _ = SaveDailyAwardAsync(st);
        st.DailySessionFlag = false;
        return 1;
    }

    private static bool Bit(ulong mask, int day) =>
        ((mask >> (day & 0x3f)) & 1) != 0;

    /// <summary><c>GetDailyAward</c> natif : jour 1..32 ; bit
    /// award_days requis, bit award_records interdit ; item du def
    /// S_DailyAward → slot libre (0x4f9 si plein) ; bit records
    /// posé + save.</summary>
    private async Task<bool> ClaimDailyAwardAsync(NetSession session,
        EntryState st, short day, CancellationToken token)
    {
        if (day is < 1 or > 32) return false;
        if (!_services.GameData.DailyAwards.TryGetValue(day,
                out var def))
            return false;
        var bit = 1UL << ((day - 1) & 0x3f);
        if ((st.DailyAwardDays & bit) == 0) return false;
        if ((st.DailyAwardRecords & bit) != 0) return false;
        // Gate natif : item non-stackable avec amount>1 → refus
        // (DebugLog "It's not stack item"). MaxStack ≤ 1 = non
        // stackable pour le natif (item +0xf0 & 1).
        if (def.Amount > 1
            && _services.Inventory.MaxStackOf(def.ItemId) <= 1)
            return false;

        // AddItemFromDailyAward natif : stack auto puis slot libre —
        // AddItemAsync couvre les deux et notifie (274/277) via
        // `touched` + NotifyItemAddedAsync.
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var res = await _services.Inventory.AddItemAsync(
            (int)st.CharId, def.ItemId, def.Amount, touched: touched);
        if (!res.Success)
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return false;
        }
        await NotifyItemAddedAsync(session, st, def.ItemId, touched,
            token);
        st.DailyAwardRecords |= bit;
        await SaveDailyAwardAsync(st);
        return true;
    }

    /// <summary>`UpdateDailyAwardToDB` natif — UPDATE complet de
    /// daily_awards (le cooldown persisté = cooldown+delta).</summary>
    private Task SaveDailyAwardAsync(EntryState st) =>
        _services.DailyAwardRepo.SaveAsync((int)st.CharId,
            st.DailyDayIndex, st.DailyLoginTime,
            st.DailyCooldown + st.DailySessionDelta,
            (long)st.DailyAwardDays, (long)st.DailyAwardRecords,
            st.DailyLoginStatus);

    private static int NowUnix() =>
        (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static int SecOfDay(int unix) =>
        (int)((unix % 86400 + 86400) % 86400);
}
