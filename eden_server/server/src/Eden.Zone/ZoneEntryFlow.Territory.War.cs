using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Cycle de guerre de territoire / family battle — émissions
/// natives 516/532/547/562/566. Preuves désasm zone (détail dans
/// docs/temp-plans/2026-10-29-territory-family-battle.md) :
/// <c>CCharacter::SendTerritoryData</c> @0x08424ab0,
/// <c>CTerritoryWarFactory::TerritoryWarStart</c> @0x08158c90,
/// <c>CTerritoryWarFactory::TerritoryWarFinishByNewEnding</c>
/// @0x081597e0, <c>CEventTerritoryKilledInfo::Execute</c>
/// @0x0866e6e0, <c>CEventTerritoryClose::Execute</c> @0x0819c240.
/// Approximation documentée : le natif arme les événements
/// temps-réel (start/close/killed-info) ; ici les transitions sont
/// détectées au tick 1 s des persos inscrits (RefreshPhases).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary><c>CCharacter::SendTerritoryData</c> natif — émis au
    /// login et après chaque <c>OnMigrationDone</c> quand
    /// char+0xb70 ≠ 0. 516 <c>[s16 type][i32 remaining][i32 own_fam]
    /// [u16 side]</c> : phase prépare → type 0 + compte à rebours,
    /// guerre → type 1 + temps restant, close → type 2 + 0.</summary>
    private async Task SendTerritoryDataAsync(NetSession session,
        EntryState st, CancellationToken ct)
    {
        if (st.TerritoryId == 0) return;
        _services.Territory.RefreshPhases(
            _services.GameData.Schedules, DateTimeOffset.UtcNow);
        var terr = _services.Territory.GetTerritory(st.TerritoryId);
        if (terr == null) { ClearTerritory(st); return; }
        await session.SendAsync(
            BuildTerritoryInfo(terr, st), ct);
        st.TerritoryPhaseSeen = terr.Phase;
    }

    /// <summary>516 pour un territoire+perso — mapping phase natif :
    /// prépare (terr+8=0 ≡ notre Phase 1) → type 0 + compte à rebours ;
    /// guerre (+8=1 ≡ Phase 2) → type 1 + temps restant ; sinon type 2
    /// + 0 (forme émise par FinishByNewEnding).</summary>
    private static CNC_CZ_ClientTerritoryInfo BuildTerritoryInfo(
        TerritoryWarService.TerritoryState terr, EntryState st)
    {
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pkt = new CNC_CZ_ClientTerritoryInfo
        {
            Field3 = terr.OwnFamilyId,      // i32 : own_family_id (terr+0x14)
            Field4 = (short)st.TerritoryCamp, // u16 : camp char+0xb74
        };
        if (terr.Phase == 1)
        {
            // terr+8==0 natif : prépare — remaining = PrepareDuration -
            // (now - announce) ≡ Begin-now chez nous (fenêtre de 15 min
            // avant le début du créneau S_Schedule).
            pkt.Field1 = 0;                          // s16 : type 0
            pkt.Field2 = Math.Max(0, terr.BeginEpoch - now);
        }
        else if (terr.Phase == 2)
        {
            // terr+8==1 natif : guerre — remaining = durée - écoulé
            // (terr+0x1c - (now - terr+0x20)) ≡ End-now.
            pkt.Field1 = 1;                          // s16 : type 1
            pkt.Field2 = Math.Max(0, terr.EndEpoch - now);
        }
        else
        {
            // terr+8==2 natif : closing — type 2, remaining 0
            // (FinishByNewEnding émet la même forme).
            pkt.Field1 = 2;                          // s16 : type 2
            pkt.Field2 = 0;
        }
        return pkt;
    }

    /// <summary>Tick 1 s — détection des transitions de guerre pour un
    /// perso inscrit : fin détectée → séquence de close native
    /// (516-type2 + 566 + 562 + 547 + rapatriement +
    /// persistance du vainqueur). Le premier observateur traite le
    /// node entier ; <see cref="TerritoryWarService.FinalizeClose"/>
    /// consomme le flag.</summary>
    private async Task TerritoryWarTickAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.TerritoryId == 0) return;
        _services.Territory.RefreshPhases(
            _services.GameData.Schedules, DateTimeOffset.UtcNow);
        var terr = _services.Territory.GetTerritory(st.TerritoryId);
        if (terr == null) { ClearTerritory(st); return; }
        if (terr.ClosePending)
        {
            await CloseTerritoryWarAsync(terr, token);
            return;
        }
        // Transition de phase pendant la guerre — le natif broadcast
        // un 516 à chaque changement (TerritoryWarStart type 1, fin
        // type 2) : on n'émet que si la phase vue a changé.
        if (terr.Phase != st.TerritoryPhaseSeen)
        {
            await session.SendAsync(
                BuildTerritoryInfo(terr, st), token);
            st.TerritoryPhaseSeen = terr.Phase;
        }
    }

    /// <summary>Séquence de fin — <c>FinishByNewEnding</c> +
    /// <c>CEventTerritoryClose</c> natifs : chaque perso du territoire
    /// reçoit 516 type 2, les 566 par famille, le 562 rank, puis le
    /// 547 vide ; camps remis à 0xffff et transport retour au node
    /// sauvegardé (char+0xa98 ≡ CrossZoneNode). Vainqueur persisté
    /// dans <c>territory_war</c>.</summary>
    private async Task CloseTerritoryWarAsync(
        TerritoryWarService.TerritoryState terr,
        CancellationToken token)
    {
        var winner = _services.Territory.ResolveWinnerFamily(terr);
        var ranks = _services.Territory.BuildKillRank(terr);
        var scores = terr.FamilyScores.ToList();
        EdenLog.Write($"[Territory] close tid={terr.Id} " +
            $"winnerFam={winner} def={terr.DefenderKills} " +
            $"atk={terr.AttackerKills}");
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.TerritoryId != terr.Id || !ost.Migrated) continue;
            try
            {
                await SendTerritoryClosePacketsAsync(s, ost, terr,
                    scores, ranks, token);
                // char+0xb70=0 / +0xb74=0xffff puis
                // TransportCharacterToNode(char+0xa98) natifs.
                ClearTerritory(ost);
                var back = ost.CrossZoneNode == 0 ? 1 : ost.CrossZoneNode;
                var scene = _services.Scenes.GetScene($"S{back:D3}");
                var (ex, ey, ez) = ResolveInstanceEntryPos(scene,
                    Random.Shared);
                await MigrateToNodeAsync(s, ost, back, ex, ey, ez,
                    token);
            }
            catch { /* session morte — ignorée comme le natif */ }
        }
        if (winner != 0 && _services.Social != null)
        {
            try
            {
                await _services.Social.UpdateTerritoryWarAsync(terr.Id,
                    winner, terr.OwnTimes + 1);
                terr.OwnFamilyId = winner;
                terr.OwnTimes++;
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Territory] persist winner " +
                    $"tid={terr.Id} : {ex.Message}");
            }
        }
        _services.Territory.FinalizeClose(terr);
    }

    /// <summary>Emissions de fin à un perso — ordre natif
    /// <c>FinishByNewEnding</c> : 566 (par famille) → 516 type 2 →
    /// 562 rank → 547 vide (CEventTerritoryClose).</summary>
    private static async Task SendTerritoryClosePacketsAsync(
        NetSession s, EntryState ost,
        TerritoryWarService.TerritoryState terr,
        List<KeyValuePair<int, (string Name, int Side, int Score)>>
            scores,
        List<(string Player, string Family, short Kills, short Rank)>
            ranks,
        CancellationToken token)
    {
        short idx = 0;
        foreach (var fs in scores.OrderByDescending(
            f => f.Value.Score))
        {
            // 566 ClientTerritoryFamilyScore
            // [s16 index][u16 camp][str famName][u16 0][i32 score] —
            // index séquentiel natif (uVar21) par entrée du vecteur.
            await s.SendAsync(new CNC_CZ_ClientTerritoryFamilyScore
            {
                Field1 = idx++,                    // s16 : index entrée
                Field2 = (ushort)fs.Value.Side,    // u16 : camp
                Field3 = fs.Value.Name,            // str : nom famille
                Field4 = 0,                        // u16 : champ record
                Field5 = fs.Value.Score,           // i32 : score
            }, token);
        }
        // 516 type 2 — closing (piVar12[1]=2, remaining=0 natif).
        await s.SendAsync(new CNC_CZ_ClientTerritoryInfo
        {
            Field1 = 2,                            // s16 : type 2 (fin)
            Field2 = 0,                            // i32 : remaining
            Field3 = terr.OwnFamilyId,             // i32 : own_family
            Field4 = (short)ost.TerritoryCamp,     // u16 : camp
        }, token);
        // 562 ClientFamilyBattleKilledRank [s16 n][entries] —
        // émis même pour les guerres non-FB chez le natif.
        var rankPkt = new CNC_CZ_ClientFamilyBattleKilledRank();
        foreach (var r in ranks)
            rankPkt.Entries.Add(
                new CNC_CZ_ClientFamilyBattleKilledRank.Entry
                {
                    PlayerName = r.Player,
                    FamilyName = r.Family,
                    Kills = r.Kills,
                    Rank = r.Rank,
                });
        await s.SendAsync(rankPkt, token);
        // 547 ClientTerritoryEnd — corps vide.
        await s.SendAsync(new CNC_CZ_ClientTerritoryEnd(), token);
    }

    /// <summary>Kill joueur→joueur dans une bataille — incrémente les
    /// compteurs natifs terr+0x30/+0x34 (camps) et char+0xb9c (kills
    /// perso), puis broadcast 532 <c>[i32 killsA][i32 killsB][s16 0]
    /// [s16 0][s16 ownKills]</c> à tous les persos du node —
    /// équivalent du changement détecté par
    /// <c>CEventTerritoryKilledInfo</c> (les 2 s16 centraux sont un
    /// record de dernier kill non reproduit — approximation
    /// documentée).</summary>
    /// <summary>Relaie un kill PvP de match battlefield à la
    /// mission — op154 `[s16 bfId][i32 killer][i32 dead]` ; la
    /// mission incrémente le score du camp du tueur et diffuse
    /// l'UpdateScore (466) aux zones.</summary>
    private async Task RelayBattlefieldKillAsync(EntryState killer,
        EntryState dead)
    {
        var mission = _services.Mission;
        if (mission is not { Connected: true }) return;
        var b = new byte[10];
        BitConverter.GetBytes((short)killer.BattlefieldId)
            .CopyTo(b, 0);                       // bfId
        BitConverter.GetBytes((int)killer.CharId).CopyTo(b, 2);
        BitConverter.GetBytes((int)dead.CharId).CopyTo(b, 6);
        await mission.SendAsync(154, b, CancellationToken.None);
    }

    /// <summary>Relaie un kill PvP de croisade à la mission via la
    /// commande DBA « CrusadeKill crusade_id= %d killer= %d victim= %d »
    /// — la mission crédite le score de la guilde du tueur
    /// (CBattleCrusade, kill-info natif 532).</summary>
    private async Task RelayCrusadeKillAsync(EntryState killer,
        EntryState dead)
    {
        var mission = _services.Mission;
        if (mission is not { Connected: true }) return;
        await mission.SendAgentDbaCommandAsync(0,
            $"CrusadeKill crusade_id= {killer.CrusadeId} " +
            $"killer= {killer.CharId} victim= {dead.CharId}",
            CancellationToken.None);
    }

    private async Task RecordTerritoryKillAsync(EntryState killer,
        EntryState victim, CancellationToken token)
    {
        var terr = _services.Territory.GetTerritory(killer.TerritoryId);
        if (terr == null || killer.TerritoryCamp == 0xffff) return;
        _services.Territory.RecordKill(terr, (int)killer.CharId,
            killer.TerritoryCamp);
        killer.TerritoryKills++;
        EdenLog.Write($"[Territory] kill tid={terr.Id} " +
            $"killer={killer.CharId} victim={victim.CharId} " +
            $"def={terr.DefenderKills} atk={terr.AttackerKills}");
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.TerritoryId != terr.Id || !ost.Migrated) continue;
            try
            {
                await s.SendAsync(
                    new CNC_CZ_ClientTerritoryKilledInfo
                    {
                        Field1 = terr.DefenderKills,   // i32 : camp A
                        Field2 = terr.AttackerKills,   // i32 : camp B
                        Field3 = 0,   // s16 : record dernier kill +4
                        Field4 = 0,   // s16 : record dernier kill +2
                        Field5 = (short)ost.TerritoryKills, // char+0xb9c
                    }, token);
            }
            catch { }
        }
    }
}
