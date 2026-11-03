using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Instance versus (arène PvP par file) — NCID 252
/// CNE_CZ_ZoneServerVersusInstanceJoinRequest [i32 v1][i32 v2].
///
/// Preuves désasm :
/// <list type="bullet">
///   <item>Execute zone @0x82a0670 — v2≠0 + IsInVersusInstance
///   (scene_type==0x11) → CVersusInstanceFactory::LeaveVersusInstance ;
///   sinon validations ordonnées puis JoinWait mission.</item>
///   <item><c>CVersusInstanceFactory::JoinWait</c> mission
///   @0x080d8ad0 : file FIFO ; à 10 → CreateVersusInstance →
///   StartVersusInstance + « EnterVersusInstance id node_tid camp
///   area_id » par membre.</item>
///   <item><c>Mission_TC_EnterVersusInstance</c> zone @0x083b5e70 :
///   char+0xdb0=camp, +0xdb4=area ; 601 VersusInstanceInfo
///   [0][0][10][0…] au membre puis event de transport.</item>
///   <item><c>LeaveVersusInstance</c> @0x0815e0d0 : status=2, revive
///   si mort, LeaveFromTeam, 356 CanLogout, retour cross-zone ;
///   le handler 601 de sortie (@0x0815e4xx) émet [0][-1][0][0…] +
///   enchant d'exclusion 54486 (EffectToLife, 9000 ds).</item>
/// </list>
/// Notre zone absorbe le rôle mission (archi fusionnée) : la file et
/// le matchmaking vivent dans <see cref="VersusService"/>.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Traite le 252 — validations de l'Execute natif dans
    /// l'ordre exact, puis file/matchmaking locaux.</summary>
    private async Task OnVersusJoinRequestAsync(NetSession session,
        EntryState st, int v1, int v2, CancellationToken token)
    {
        // Natif : les deux i32 du wire sont « booléifiés » (0<x → 1).
        var join = v1 > 0;
        var leave = v2 > 0;

        // Scene type = NodeDef.Type (S_Node col 3) — le natif lit
        // *(char+0x1c)+0x28+0x2c ; 0x11 = instance versus.
        var sceneType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;

        // v2≠0 + dans l'instance → LeaveVersusInstance zone-side.
        if (leave && sceneType == 0x11)
        {
            await LeaveVersusInstanceAsync(session, st, token);
            return;
        }

        if (!join)
        {
            // flag=0 : en file (exec+0x684) → texte 0x1d8c ; sinon
            // LeaveWait (retrait silencieux de la file).
            if (st.VersusPending)
            {
                await session.SendAsync(
                    new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x1d8c, Field2 = "" }, token);
                return;
            }
            _services.Versus.Leave((int)st.CharId);
            return;
        }

        // flag=1 (join) — validations dans l'ordre natif :
        // 1) level_data < AllFormulaParas+0x964 (S_Parameter 601 = 85)
        //    → 0x1d8e. char+0x470 = level_data id = st.Level.
        var minLevel = _services.GameData.Parameters.Get(601, 85f);
        if (st.Level < minLevel)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d8e, Field2 = "" }, token);
            return;
        }
        // 2) char+0xbb0 (in-versus) || exec+0x684 (en file) → 0x1d91.
        if (st.VersusPending || st.VersusInstanceId != 0
            || sceneType == 0x11)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d91, Field2 = "" }, token);
            return;
        }
        // 3) char+0x3b bit0x40 (état « action verrouillée » — mort/
        //    étourdi chez nous) → 0x1d90.
        if (st.Dead)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d90, Field2 = "" }, token);
            return;
        }
        // 4) type de scène exclu {4-10,12,14-19} → 0x1d8f.
        if ((sceneType >= 4 && sceneType <= 10) || sceneType == 12
            || (sceneType >= 14 && sceneType <= 19))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d8f, Field2 = "" }, token);
            return;
        }
        // 5) enchant actif dont data+0x08 s16 == -0x2b2a (effect_id
        //    54486 — buff d'exclusion versus) → 0x1d8d.
        if (st.Statuses.Keys.Contains(VersusService.ExclusionEffectId))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d8d, Field2 = "" }, token);
            return;
        }

        // 6) JoinWait natif : versus fermé (factory+0x10, ouverture
        //    sched 602) → texte 0x1d89.
        if (!VersusService.IsOpen(_services.GameData.Schedules,
                DateTimeOffset.UtcNow))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1d89, Field2 = "" }, token);
            return;
        }

        // JoinWait natif : flag exec+0x684 puis file FIFO ; le match
        // se forme à 10 (factory+4).
        st.VersusPending = true;
        var match = _services.Versus.Join((int)st.CharId);
        if (match != null)
            await StartVersusMatchAsync(match, token);
    }

    /// <summary>Match formé — recode de
    /// <c>Mission_TC_EnterVersusInstance</c> @0x083b5e70 : pour chaque
    /// membre, camp/area sur l'état, 601 VersusInstanceInfo
    /// [0][0][10][0…] puis transport vers l'arène (le natif passe par
    /// CEventEnterColosseum — délai court, ramené à une migration
    /// immédiate comme le colosseum).</summary>
    private async Task StartVersusMatchAsync(
        VersusService.VersusMatch match, CancellationToken token)
    {
        var scene = _services.Scenes.GetScene(
            $"S{match.NodeId:D3}");
        var normals = scene?.EventAreas
            .Where(a => a.Type == "NORMAL").ToList();
        // Spawn de camp : AllSpawnAreaIDs[char+0xdb4] natif — les
        // aires NORMAL de S406/S407 sont réparties ouest (x petit)
        // /est : camp 0 → moitié basse en X, camp 1 → moitié haute.
        var midX = normals is { Count: > 0 }
            ? (normals.Min(a => a.X) + normals.Max(a => a.X)) / 2f
            : 0f;
        // 601 : forme « entrée » native — champs objets
        // [1]=0 [2]=0 [3]=10, tout le reste à 0 (ctor dans
        // Mission_TC_EnterVersusInstance).
        var (vsOp, vsPayload) = PreparePacket(
            new CNC_CZ_ClientVersusInstanceInfo
            {
                Field1 = 0, Field2 = 0, Field3 = 10,
            });
        foreach (var (camp, charId) in match.Members())
        {
            var mst = _states.Values.FirstOrDefault(
                s => s.CharId == (uint)charId);
            var sess = _services.Sessions.FindByCharacter(charId);
            if (mst == null || sess == null) continue;
            mst.VersusPending = false;
            mst.VersusInstanceId = match.Id;
            mst.VersusCamp = camp;
            await SendPreparedToAsync(sess, vsOp, vsPayload, token);
            var area = normals is { Count: > 0 }
                ? normals.Where(a => camp == 0 ? a.X < midX
                        : a.X >= midX)
                    .OrderBy(_ => Random.Shared.Next())
                    .FirstOrDefault() ?? normals[0]
                : null;
            var (ex, ey, ez) = area != null
                ? (area.X, area.Y, area.Z)
                : ResolveInstanceEntryPos(scene, Random.Shared);
            await MigrateToNodeAsync(sess, mst, (short)match.NodeId,
                ex, ey, ez, token);
        }
        EdenLog.Write($"[Zone] Versus instance {match.Id} lancée " +
            $"node={match.NodeId} " +
            $"A={match.CampA.Count} B={match.CampB.Count}");
    }

    /// <summary><c>LeaveVersusInstance</c> natif @0x0815e0d0 :
    /// revive si mort (flag char+0x3a bit1), 356 CanLogout, reset
    /// 601 [0][-1][0][0…], enchant d'exclusion 54486 (900 s natif),
    /// retour aux coords cross-zone (char+0xa98/+0xa9c/+0xaa0 →
    /// CrossZoneNode/X/Y chez nous).</summary>
    private async Task LeaveVersusInstanceAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var instId = st.VersusInstanceId;
        _services.Versus.LeaveInstance((int)st.CharId);
        st.VersusInstanceId = 0;
        st.VersusCamp = 0;
        st.VersusPending = false;
        if (st.Dead)
        {
            st.Dead = false;
            st.PlayerHp = st.PlayerMaxHp;
            st.PlayerMp = st.PlayerMaxMp;
        }
        await session.SendAsync(new CNC_CZ_ClientCanLogout(), token);
        // 601 forme « sortie » native : [1]=0 [2]=-1 [3]=0.
        await session.SendAsync(new CNC_CZ_ClientVersusInstanceInfo
        {
            Field1 = 0, Field2 = -1, Field3 = 0,
        }, token);
        // EffectToLife(char,char,-0x2b2a,9000,0x20) — exclusion
        // versus 900 s quand camp<3 (notre camp est 0/1 → toujours).
        await AddPlayerEnchantAsync(session, st, (int)st.CharId,
            VersusService.ExclusionEffectId,
            VersusService.ExclusionDurationSec, 1, token);
        // VersusInstanceFactory::Restore natif : 273 ClientRestore
        // [u32 charId] aux observateurs — sortie de l'état
        // d'instance avant le retour cross-zone.
        await BroadcastToNodePeersAsync(session, st,
            new CNC_CZ_ClientRestore { Value = st.CharId });
        EdenLog.Write($"[Zone] Versus leave char={st.CharId} " +
            $"inst={instId}");
        // Retour au point mémorisé à l'entrée (CrossZone* natifs).
        var backNode = st.CrossZoneNode != 0 ? st.CrossZoneNode : 1;
        var backScene = _services.Scenes.GetScene($"S{backNode:D3}");
        var (bx, by, bz) = st.CrossZoneNode != 0
            ? ((float)st.CrossZoneX, (float)st.CrossZoneY, 0f)
            : ResolveInstanceEntryPos(backScene, Random.Shared);
        await MigrateToNodeAsync(session, st, (short)backNode,
            bx, by, bz, token);
    }
}
