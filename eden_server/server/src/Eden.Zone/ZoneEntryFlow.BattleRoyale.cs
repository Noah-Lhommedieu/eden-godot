using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Battle Royale — NCID 241
/// CNE_CZ_ZoneServerBattleRoyaleJoinRequest [i32 join][i32 leave].
///
/// Execute natif @0x0823da10 :
/// <list type="bullet">
///   <item>en BR (scene_type==0xf) + v2≠0 →
///   <c>CBattleRoyaleFactory::LeaveBattleRoyale</c> (br+4 &lt; 3 =
///   encore actif → retour à l'arène ; sinon sortie).</item>
///   <item>v1==0 et char+0xbb4≠0 → texte 0x18c4.</item>
///   <item>v1≠0 → gates ordonnés : level&lt;20 → 0x18bf ;
///   +0xbb0/+0xbb4 → 0x18c3 ; +0x3b&amp;0x40 → 0x18c2 ; en versus →
///   0x1d98 ; types de scène {4-9,0xa,0xc,0xe-0x13} → 0x18c1 ;
///   enchant -0x2b2a (54486) → 0x18c0 ; puis JoinWait mission (BR
///   fermé → 0x18c5 ; déjà inscrit → 0x18c3).</item>
///   <item>30 inscrits (factory+4=0x1e) → CreateNewBattleRoyale +
///   NotifyEnterBattleRoyale → 591 [0][u16 0][10][0…] + teleport
///   node 309 (Mission_TC_EnterBattleRoyale @0x083b6030).</item>
/// </list>
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>241 — gates natives puis file/matchmaking locaux
    /// (<see cref="BattleRoyaleService"/>).</summary>
    private async Task OnBattleRoyaleJoinAsync(NetSession session,
        EntryState st, int v1, int v2, CancellationToken token)
    {
        var join = v1 > 0;
        var leave = v2 > 0;
        var sceneType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;

        // En BR + leave → LeaveBattleRoyale natif.
        if (leave && sceneType == 0xf)
        {
            await LeaveBattleRoyaleAsync(session, st, token);
            return;
        }
        if (!join)
        {
            // char+0xbb4 (in-BR) ≠0 sans demande de sortie → 0x18c4.
            if (st.BattleRoyaleId != 0)
            {
                await session.SendAsync(
                    new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x18c4, Field2 = "" }, token);
                return;
            }
            _services.BattleRoyale.Leave((int)st.CharId);
            return;
        }

        // Gates join dans l'ordre natif :
        // 1) level_data < 0x14 (20) → 0x18bf.
        if (st.Level < 20)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18bf, Field2 = "" }, token);
            return;
        }
        // 2) char+0xbb0 (versus/BR pending) || +0xbb4 (in-BR) → 0x18c3.
        if (st.VersusPending || st.VersusInstanceId != 0
            || st.BattleRoyaleId != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c3, Field2 = "" }, token);
            return;
        }
        // 3) char+0x3b bit0x40 (verrouillé — mort) → 0x18c2.
        if (st.Dead)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c2, Field2 = "" }, token);
            return;
        }
        // 4) IsInVersusInstance → 0x1d98.
        if (sceneType == 0x11)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d98, Field2 = "" }, token);
            return;
        }
        // 5) types de scène exclus {4-9,0xa,0xc,0xe-0x13} → 0x18c1.
        if ((sceneType >= 4 && sceneType <= 0xa) || sceneType == 0xc
            || (sceneType >= 0xe && sceneType <= 0x13))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c1, Field2 = "" }, token);
            return;
        }
        // 6) enchant -0x2b2a (54486, exclusion PvP) → 0x18c0.
        if (st.Statuses.Keys.Contains(VersusService.ExclusionEffectId))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c0, Field2 = "" }, token);
            return;
        }
        // 7) JoinWait : BR fermé (factory+0x40 event off) → 0x18c5 ;
        //    déjà inscrit → 0x18c3.
        if (!BattleRoyaleService.IsOpen(_services.GameData.Schedules,
                DateTimeOffset.UtcNow))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c5, Field2 = "" }, token);
            return;
        }
        if (_services.BattleRoyale.InQueue((int)st.CharId))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x18c3, Field2 = "" }, token);
            return;
        }

        var br = _services.BattleRoyale.Join((int)st.CharId);
        if (br != null)
            await StartBattleRoyaleAsync(br.Value.Id, br.Value.Members,
                token);
    }

    /// <summary>BR formé — recode de
    /// <c>Mission_TC_EnterBattleRoyale</c> @0x083b6030 : 591
    /// BattleRoyaleInfo [i32 0][u16 0][i32 10][0…] par membre +
    /// char+0xd90=brId, puis transport vers l'arène 309
    /// (CEventEnterColosseum natif → migration immédiate).</summary>
    private async Task StartBattleRoyaleAsync(int brId,
        IReadOnlyList<int> members, CancellationToken token)
    {
        var scene = _services.Scenes.GetScene(
            $"S{BattleRoyaleService.ArenaNodeId:D3}");
        var (brOp, brPayload) = PreparePacket(
            new CNC_CZ_ClientBattleRoyaleInfo
            {
                Field1 = 0, Field2 = 0, Field3 = 10,
            });
        foreach (var charId in members)
        {
            var mst = _states.Values.FirstOrDefault(
                s => s.CharId == (uint)charId);
            var sess = _services.Sessions.FindByCharacter(charId);
            if (mst == null || sess == null) continue;
            mst.BattleRoyaleId = brId;
            await SendPreparedToAsync(sess, brOp, brPayload, token);
            var (ex, ey, ez) = ResolveInstanceEntryPos(scene,
                Random.Shared);
            await MigrateToNodeAsync(sess, mst,
                (short)BattleRoyaleService.ArenaNodeId, ex, ey, ez,
                token);
        }
        EdenLog.Write($"[Zone] BR {brId} lancé node=309 " +
            $"{members.Count} joueurs");
    }

    /// <summary><c>LeaveBattleRoyale</c> natif : sortie d'état,
    /// 591 reset, enchant d'exclusion 54486 (même mécanisme que la
    /// sortie versus), retour cross-zone.</summary>
    private async Task LeaveBattleRoyaleAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var brId = st.BattleRoyaleId;
        _services.BattleRoyale.LeaveInstance((int)st.CharId);
        st.BattleRoyaleId = 0;
        if (st.Dead)
        {
            st.Dead = false;
            st.PlayerHp = st.PlayerMaxHp;
            st.PlayerMp = st.PlayerMaxMp;
        }
        await session.SendAsync(new CNC_CZ_ClientBattleRoyaleInfo(),
            token);
        await AddPlayerEnchantAsync(session, st, (int)st.CharId,
            VersusService.ExclusionEffectId,
            VersusService.ExclusionDurationSec, 1, token);
        // CBattleRoyaleFactory::Restore natif @0x804cxx : 273
        // ClientRestore [u32 charId] aux observateurs — le perso
        // quitte l'état d'instance avant le retour cross-zone.
        await BroadcastToNodePeersAsync(session, st,
            new CNC_CZ_ClientRestore { Value = st.CharId });
        EdenLog.Write($"[Zone] BR leave char={st.CharId} br={brId}");
        var backNode = st.CrossZoneNode != 0 ? st.CrossZoneNode : 1;
        var backScene = _services.Scenes.GetScene($"S{backNode:D3}");
        var (bx, by, bz) = st.CrossZoneNode != 0
            ? ((float)st.CrossZoneX, (float)st.CrossZoneY, 0f)
            : ResolveInstanceEntryPos(backScene, Random.Shared);
        await MigrateToNodeAsync(session, st, (short)backNode,
            bx, by, bz, token);
    }
}
