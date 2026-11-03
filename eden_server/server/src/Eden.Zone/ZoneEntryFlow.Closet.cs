using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Garde-robe costumes — NCIDs 165 (QueryClosets → 488) et
/// 167 (ClosetSet). Référence native : Execute @0x8243b10 —
/// sélection d'un set de couleurs de costume parmi les slots définis
/// par la classe (<c>SClassCloset</c>, vecteur S_Class+0x74, ≤10)
/// et enregistrés dans <c>player_closet</c>.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>165 QueryClosets — relit la garde-robe courante et
    /// renvoie le 488 (même corps que l'émission d'entrée).</summary>
    private Task HandleQueryClosetsAsync(NetSession session,
        EntryState st, CancellationToken token)
        => SendPlayerClosetsAsync(session, st, token);

    /// <summary>167 ClosetSet <c>[u8 idx]</c> — sélection d'un slot
    /// de costume. Gates natifs (désasm Execute @0x8243b10) :
    /// <c>idx &lt; |SClassCloset|</c>, niveau requis du slot ≠ 0,
    /// <c>level ≤ class_level</c> (char+0x47e), <c>idx ≠ closet
    /// courant</c> (char+0x4a6&amp;0x3f). Puis : copie des 3 couleurs
    /// du slot (zéros si absent), save DB, 283 mask 0x1e00 self +
    /// CanSee, réémission 489.</summary>
    private async Task HandleClosetSetAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 1 || st.CharId == 0) return;
        var idx = payload[0];                         // u8 : slot choisi

        // --- Gates natifs ---
        var classDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, st.RaceId, st.ClassId);
        var defs = classDef?.LevelProgression;        // = SClassCloset
        if (defs == null || idx >= defs.Count) return;
        var req = defs[idx].Level;
        if (req == 0 || req > st.ClassLevel) return;
        if (idx == st.ClosetIndex) return;

        // --- Application (char+0x4a6 + char+0x48e..0x4a2 natifs) ---
        st.ClosetIndex = idx;
        if (st.Closets.TryGetValue((byte)st.ClassId, out var slots)
            && idx < slots.Count)
        {
            var (helmet, clothes, cloak) = slots[idx];
            st.HelmetColor = helmet;
            st.ClothesColor = clothes;
            st.CloakColor = cloak;
        }
        else
        {
            st.HelmetColor = st.ClothesColor = st.CloakColor = 0;
        }

        // --- Persistance : SavePlayerCharacter "style helmet_color
        // clothes_color cloak_color" + classlist.style natifs ---
        await _services.Classes.SaveClosetSelectionAsync(
            (int)st.CharId, st.ClassId, idx,
            st.HelmetColor, st.ClothesColor, st.CloakColor);

        // --- 283 ClientModelModify mask 0x1e00 : 7 u32 = index +
        // 3 couleurs u64 (low/high) — self puis broadcast CanSee ---
        var body = BuildClosetModelModifyBody(st);
        await session.SendPacketAsync(283, body, token);
        await BroadcastToNodeAsync(session, st, 283, body, token);

        // --- 489 PlayerClassList — style de la classe maj ---
        await HandleQueryClassListAsync(session, st, token);
    }

    /// <summary>Corps du 283 mask 0x1e00 émis par ClosetSet natif :
    /// <c>[i32 handle][s8 charByte][i32 mask][u16 7][u32 idx][u64×3
    /// couleurs][u16 1][u32 handle]</c> — le char+0x4dc natif est
    /// incrémenté avant envoi ; 0x0a est la valeur constante utilisée
    /// par les autres ModelModify du flux.</summary>
    private static byte[] BuildClosetModelModifyBody(EntryState st)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(st.CharId));     // i32 handle
        body.Add(0x0a);                                      // s8 char byte
        body.AddRange(BitConverter.GetBytes(0x1e00));        // i32 mask
        body.AddRange(BitConverter.GetBytes((short)7));      // u16 : 7 u32
        body.AddRange(BitConverter.GetBytes((uint)st.ClosetIndex));
        foreach (var color in new[]
                 { st.HelmetColor, st.ClothesColor, st.CloakColor })
        {
            body.AddRange(BitConverter.GetBytes((uint)color));        // lo
            body.AddRange(BitConverter.GetBytes((uint)(color >> 32)));// hi
        }
        body.AddRange(BitConverter.GetBytes((short)1));      // u16 : vec
        body.AddRange(BitConverter.GetBytes(st.CharId));     // u32 handle
        return body.ToArray();
    }

    /// <summary>Commande texte <c>update_closet &lt;idx&gt;</c>
    /// (TC_UpdateCloset @0x83d2620 — S_RootCmds lv4) : enregistre les
    /// couleurs équipées dans le slot de la classe courante
    /// (DELETE+INSERT player_closet) puis réémet le 488.</summary>
    private async Task HandleUpdateClosetCommandAsync(
        NetSession session, EntryState st, int idx,
        CancellationToken token)
    {
        var classId = (byte)st.ClassId;
        if (!st.Closets.TryGetValue(classId, out var slots))
            slots = st.Closets[classId] = new();
        while (slots.Count <= idx)
            slots.Add((0UL, 0UL, 0UL));
        slots[idx] = (st.HelmetColor, st.ClothesColor, st.CloakColor);
        await _services.Classes.UpsertClosetSlotAsync(
            (int)st.CharId, st.ClassId, idx,
            st.HelmetColor, st.ClothesColor, st.CloakColor);
        await SendPlayerClosetsAsync(session, st, token);
    }
}
