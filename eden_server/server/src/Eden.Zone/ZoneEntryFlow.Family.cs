using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone;

/// <summary>
/// Pushes famille monde→zone — ZW 31/32/33 (natifs
/// <c>CNE_ZW_ZoneServer{Change,New,Remove}Family</c> — exécutés
/// nativement sur la zone @0x82ad060/0x82ad140/0x82ad270).
/// La zone applique au registre <see cref="Core.Game.Services.GuildService"/>
/// (l'équivalent de la map <c>SFamily</c> de <c>CZoneServer+0x45c</c>)
/// puis émet <c>CNC_CZ_ClientChangeFamily</c> (346) au(x) perso(s)
/// concerné(s) — <c>CPlayerFactory::NewFamily</c> natif.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Types de push famille ZW (nos NCIDs compacts
    /// 31..33 + 35 données non-membre).</summary>
    public enum FamilyPushKind { Change = 31, New = 32, Remove = 33,
        DataUpdate = 35 }

    /// <summary>Point d'entrée — routé sur la boucle par Program.cs
    /// (mutation de <c>Guilds</c> + envois 346).</summary>
    public Task HandleFamilyPushAsync(FamilyPushKind kind, byte[] body)
        => RunOnLoopAsync(async _ =>
        {
            var s = new InBinStream(body);
            switch (kind)
            {
                case FamilyPushKind.New:
                    await ApplyNewFamilyAsync(s);
                    break;
                case FamilyPushKind.Change:
                    await ApplyChangeFamilyAsync(s);
                    break;
                case FamilyPushKind.Remove:
                    await ApplyRemoveFamilyAsync(s);
                    break;
                case FamilyPushKind.DataUpdate:
                    ApplyFamilyDataUpdate(s);
                    break;
            }
        });

    /// <summary>ZW 32 NewFamily — [i32 charId][str charName]
    /// [s16 node][i32 familyId][str familyName]. familyId&lt;0 :
    /// refus natif (-1 nom pris → texte 0x913, -2 autre) relayé
    /// tel quel au client.</summary>
    private async Task ApplyNewFamilyAsync(InBinStream s)
    {
        var charId = s.ReadInt();      // i32 — charId créateur
        var charName = s.ReadString(); // str — nom du perso
        s.ReadShort();                 // s16 — node_id (ignoré)
        var familyId = s.ReadInt();    // i32 — family_id (>0) ou erreur (<0)
        var familyName = s.ReadString(); // str — nom de famille
        if (familyId < 0)
        {
            var session = _services.Sessions.FindByCharacter(charId);
            if (session != null)
                // -1 = nom déjà pris → texte 0x913 "%s" (natif
                // CPlayerFactory::NewFamily); -2 = refus générique.
                await SendTextIndexAsync(session,
                    familyId == -1 ? 0x913 : 0x184e,
                    CancellationToken.None, familyName);
            return;
        }
        _services.Guilds.ApplyNewFamily(familyId, familyName,
            charId, charName);
        // Coût de création natif : 50 000 or prélevés par
        // CPlayerFactory::NewFamily quand le push réussit.
        await DebitFamilyCreationGoldAsync(charId);
        await EmitChangeFamilyAsync(charId);
        EdenLog.Write($"[Zone] NewFamily #{familyId} '{familyName}' " +
            $"pour char={charId}");
    }

    /// <summary>ZW 31 ChangeFamily — [i32 charId][s16 node]
    /// [i32 familyId][u8 flag][i32 rank][s16 emblem][s16 0]
    /// (<c>Deserialize @0x82aae70</c>). familyId=0 = départ.</summary>
    private async Task ApplyChangeFamilyAsync(InBinStream s)
    {
        var charId = s.ReadInt();      // i32 — charId ciblé
        s.ReadShort();                 // s16 — node_id (ignoré)
        var familyId = s.ReadInt();    // i32 — family_id (0 = départ)
        s.ReadByte();                  // u8 — flag famille (fam+0x18 natif)
        var rank = s.ReadInt();        // i32 — rang (fam+0x1c natif)
        var emblem = s.ReadShort();    // s16 — emblem (fam+0x20 natif)
        s.ReadShort();                 // s16 — réservé
        _services.Guilds.ApplyChangeFamily(charId, string.Empty,
            familyId, rank);
        if (familyId > 0
            && _services.Guilds.GetGuild(familyId) is { } g
            && emblem != 0)
            g.Emblem = emblem;
        await EmitChangeFamilyAsync(charId);
    }

    /// <summary>ZW 33 RemoveFamily — [i32 familyId] : dissolution,
    /// 346 famille=0 à chaque ex-membre en ligne.</summary>
    private async Task ApplyRemoveFamilyAsync(InBinStream s)
    {
        var familyId = s.ReadInt();    // i32 — family_id supprimée
        foreach (var id in _services.Guilds.ApplyRemoveFamily(familyId))
            await EmitChangeFamilyAsync(id);
    }

    /// <summary>ZW 35 FamilyDataUpdate — [i32 familyId]
    /// [s16 house_open] : met à jour le node de hall dans le cache
    /// famille zone (équivalent de la mutation <c>SFamily+0x3c</c>
    /// faite par le broadcast natif <c>CNC_DBA_ClientChangeFamily</c>
    /// après <c>CFamily::InstanceCreate</c>).</summary>
    private void ApplyFamilyDataUpdate(InBinStream s)
    {
        var familyId = s.ReadInt();    // i32 — family_id
        var houseOpen = s.ReadShort(); // s16 — house_open (0/501/502)
        if (_services.Guilds.GetGuild(familyId) is { } g)
            g.HouseOpen = houseOpen;
        else
            EdenLog.Write($"[Zone] FamilyDataUpdate famille inconnue " +
                $"#{familyId} (house_open={houseOpen})");
    }

    /// <summary>Émet 346 au perso s'il est en ligne — même trame que
    /// le handler UpdateFamily (81) : [u32 char][i32 family][u8 flag]
    /// [str nom][i32 rang][s16 emblem][u8 révision].</summary>
    private async Task EmitChangeFamilyAsync(int charId)
    {
        var session = _services.Sessions.FindByCharacter(charId);
        if (session is null) return;
        var guild = _services.Guilds.GetGuildOf(charId);
        var member = _services.Guilds.GetMember(charId);
        await session.SendAsync(new CNC_CZ_ClientChangeFamily
        {
            Field1 = charId,                              // u32 charId
            Field2 = guild?.Id ?? 0,                      // i32 family_id
            Field3 = 0,                                   // u8 flag
            Field4 = guild?.Name ?? string.Empty,         // str nom
            Field5 = member != null ? member.FamilyRank : -1, // i32 rang
            Field6 = unchecked((short)(guild?.Emblem ?? -1)), // s16 emblem
            Field7 = (byte)_services.Guilds.GetFamilyRevision(charId), // u8 révision
        });
        await EmitR25UpdateFamilyAsync(charId, guild?.Name ?? "");
    }

    /// <summary>565 `ClientR25UpdateFamily` `[s16 memberIndex]
    /// [str familyName]` — <c>CForceTeam::R25UpdateFamily</c> natif
    /// (zone @0x0846d160) : broadcast aux membres de la team du perso
    /// dont la famille vient de changer (join/quit/kick/disband via
    /// le push W→Z ChangeFamily) — l'index est la position du membre
    /// dans la team, la string le nouveau nom de famille.</summary>
    private async Task EmitR25UpdateFamilyAsync(int charId,
        string familyName)
    {
        var team = _services.Teams.GetTeamOf(charId);
        if (team == null) return;
        var idx = team.Members.FindIndex(m => m.CharacterId == charId);
        if (idx < 0) return;
        var pkt = new CNC_CZ_ClientR25UpdateFamily
        {
            Field1 = (short)idx,           // s16 : slot du membre
            Field2 = familyName,           // str : nouveau nom famille
        };
        var (op, payload) = PreparePacket(pkt);
        foreach (var m in team.Members)
        {
            var s = _services.Sessions.FindByCharacter(m.CharacterId);
            if (s == null) continue;
            await SendPreparedToAsync(s, op, payload,
                CancellationToken.None);
        }
    }

    /// <summary>Prélève le coût de création natif (50 000 or —
    /// <c>GainGold(-50000)</c> dans <c>CPlayerFactory::NewFamily</c>
    /// quand <c>char+0x4b0</c> &gt; 49 999).</summary>
    private async Task DebitFamilyCreationGoldAsync(int charId)
    {
        const long cost = 50_000;
        var gold = await _services.Characters.GetGoldAsync(charId);
        if (gold < cost)
        {
            // Le monde a déjà créé la famille — cas limite : le natif
            // vérifiait l'or côté zone à la réception du push ; on log
            // simplement (le client verra la famille sans le débit).
            EdenLog.Write($"[Zone] char={charId} or insuffisant pour " +
                "le coût de création de famille");
            return;
        }
        await _services.Characters.AddGoldAsync(charId, -cost);
    }
}
