using Eden.Core.Data.Parsers;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Actions de scène « niveau node » (plan futur-feature/02) : celles qui
/// n'ont pas besoin d'un joueur déclencheur — NPC né/tué/marchant,
/// motion + bulle de monstre, son, message de canal — partagées par le
/// moteur d'événements (<c>RunSceneActionAsync</c>) et par le pont texte
/// mission→zone (<c>CNE_MW_WorldTextCommand</c>, NCID 20 —
/// <see cref="HandleSceneTextCommandAsync"/>).
///
/// Preuves : <c>CZoneServer::MonsterActionByHandle</c> @0x08170a90 →
/// <c>CAI::SetEventAction</c> @0x084d5120 (verrou d'IA « Time », 495 puis
/// <c>MonsterSay</c> 268) ; <c>CNode::PlaySound</c> mission @0x081d53e0
/// → texte « PlaySound NodeID= FileName= PositionID= » →
/// <c>Mission_TC_PlaySound</c> zone @0x083b0880 → 494 ;
/// <c>TC_Event_ChannelMsg</c> mission @0x080ed350 (texte T_TextIndex,
/// « %s » = nom de l'exécutant, diffusé monde, canal 3).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Event_BornNpc / BornNpcByTID : fait naître le NPC
    /// <paramref name="npcId"/> à chaque position de groupe
    /// <paramref name="group"/> (col7 de [position]) — repli sur
    /// (<paramref name="fx"/>,<paramref name="fy"/>) sans groupe. Le NPC
    /// né est déjà un NPC ordinaire (dialogable via son template) : pas
    /// de verbe « transform » natif.</summary>
    private async Task BornSceneNpcAsync(int nodeId, int inst,
        SceneParser.Scene scene, int npcId, int group, float fx, float fy,
        CancellationToken token)
    {
        // Le template est filtré au chargement natif (GetAllNPCTemplate —
        // masque Locate) : un NPC régional ne peut pas être né par event.
        if (npcId <= 0 || !_services.Live.NpcSpawnable(npcId)) return;
        if (scene.PositionGroups.TryGetValue(group, out var ps) &&
            ps.Count > 0)
            foreach (var p in ps)
                await BroadcastSeeMobAsync(nodeId, inst,
                    _services.Live.SpawnNpc(nodeId, inst, npcId,
                        p.X, p.Y, p.Z, p.Facing), token);
        else
            await BroadcastSeeMobAsync(nodeId, inst,
                _services.Live.SpawnNpc(nodeId, inst, npcId,
                    fx, fy, 0, 0), token);
    }

    /// <summary>Event_KillNpc / KillNpcByTID : retire les NPC du
    /// template (269 aux sessions du node).</summary>
    private async Task KillSceneNpcAsync(int nodeId, int inst, int npcId,
        CancellationToken token)
    {
        foreach (var h in _services.Live.DespawnNpcs(nodeId, inst, npcId))
            await BroadcastUnsightAsync(nodeId, inst, h, token);
    }

    /// <summary>Durée de motion connue (Motion.ini, fin en
    /// décisecondes) pour (modèle du mob, kfId) — 0 si inconnue.</summary>
    private int MotionEndDs(ZoneLiveWorld.LiveMob mob, int kfId)
    {
        if (mob.Def == null || kfId <= 0) return 0;
        var prefix = MotionParser.ModelPrefix(mob.Def.ModelCode);
        return _services.GameData.MotionTimings.TryGetValue(prefix,
                out var byKf) && byKf.TryGetValue(kfId, out var t)
            ? t.EndDs : 0;
    }

    /// <summary>Event_MonsterAction / MonsterActionByHandle :
    /// <c>CAI::SetEventAction</c> sur chaque monstre vivant du groupe —
    /// ignoré si un verrou court déjà (natif : <c>CAI+0xec != 0</c>),
    /// sinon IA figée <see cref="SceneEventRules.ActionLockSeconds"/>,
    /// arrêt de marche (BroadCastMove = 326 immobile), 495 de la motion
    /// et bulle 268 (MonsterSay) du dialogue. Retourne le nombre de
    /// monstres touchés.</summary>
    private async Task<int> SceneMonsterActionAsync(int nodeId, int inst,
        int groupId, int kfId, int dlgId, int timeParam,
        CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var hit = 0;
        var text = dlgId > 0 ? DialogueText(dlgId) : "";
        if (text.StartsWith('#')) text = "";   // id inconnu : pas de bulle
        foreach (var mob in _services.Live.GroupMobs(nodeId, inst, groupId)
            .Where(m => m.Alive).ToList())
        {
            if (mob.EventActionLocked(now)) continue;
            var lockSec = SceneEventRules.ActionLockSeconds(timeParam,
                MotionEndDs(mob, kfId));
            if (lockSec > 0)
                mob.EventActionUntil = now.AddSeconds(lockSec);
            hit++;
            await BroadcastMobStopAsync(nodeId, inst, mob, token);
            if (kfId > 0)
                await BroadcastMonsterActionAsync(mob.Handle, nodeId,
                    mob.InstanceKey, (short)kfId);
            if (text.Length > 0)
                await BroadcastMonsterSayAsync(nodeId, inst, mob.Handle,
                    text, token, dlgId);
        }
        return hit;
    }

    /// <summary>326 immobile (cur = dst) du mob — équivalent du
    /// <c>BroadCastMove</c> de <c>CAI::SetEventAction</c> : le client
    /// coupe la marche en cours.</summary>
    private async Task BroadcastMobStopAsync(int nodeId, int inst,
        ZoneLiveWorld.LiveMob mob, CancellationToken token)
    {
        var (op, payload) = PreparePacket(BuildMovePrediction(mob.Handle,
            mob.X, mob.Y, mob.X, mob.Y));
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Sent.ContainsKey(mob.Handle)) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Marche scriptée d'un NPC de scène (extension Eden —
    /// aucun verbe natif ne déplace un NPC) : l'entité de sight est
    /// replacée à destination (borne <see cref="SceneEventRules.ClampWalk"/>)
    /// et un 326 cur→dst est diffusé (wire prouvé par capture : même
    /// paquet que les mobs). Le client anime la marche (prédiction
    /// <c>HandleMovePrediction</c>). Retourne le nombre de NPC mus.</summary>
    internal async Task<int> WalkSceneNpcAsync(int nodeId, int inst,
        int npcId, float dstX, float dstY, CancellationToken token)
    {
        var moved = 0;
        foreach (var e in _services.Live.NpcsOfTemplate(nodeId, inst, npcId))
        {
            var (x, y) = SceneEventRules.ClampWalk(e.X, e.Y, dstX, dstY);
            var facing = NativeDir(x - e.X, y - e.Y);
            if (_services.Live.MoveNpc(nodeId, inst, e.Handle, x, y,
                    facing) is not { } old) continue;
            moved++;
            var (op, payload) = PreparePacket(BuildMovePrediction(
                e.Handle, old.X, old.Y, x, y));
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                    !ost.Sent.ContainsKey(e.Handle)) continue;
                ost.EntityPos[e.Handle] = (x, y);
                await SendPreparedToAsync(s, op, payload, token);
            }
        }
        return moved;
    }

    /// <summary>Event_PlaySound / PlaySound : 494 ClientPlaySound
    /// <c>[str fichier][f32 x][f32 y][f32 z]</c> (désasm
    /// <c>CNC_CZ_ClientPlaySound</c>) aux sessions du node. La position
    /// native vient de la map CPositionData (node&lt;&lt;16|positionId) :
    /// l'id est la clé col7 d'un groupe [position] (vérifié S176 :
    /// « 54 = 第三道門擊破特效指定點 »), repli sur la position du
    /// déclencheur (<paramref name="fx"/>, <paramref name="fy"/>) — le
    /// client Godot n'a pas de moteur audio (journal seulement).</summary>
    private async Task ScenePlaySoundAsync(int nodeId, int inst,
        SceneParser.Scene scene, string file, int positionId,
        float fx, float fy, CancellationToken token)
    {
        if (string.IsNullOrEmpty(file)) return;
        var (x, y, z) = ScenePoint(scene, positionId, fx, fy);
        var (op, payload) = PreparePacket(new CNC_CZ_ClientPlaySound
        {
            Field1 = file,          // str : nom de fichier son
            Field2 = x,             // f32 : x
            Field3 = y,             // f32 : y (plan EDEN)
            Field4 = z,             // f32 : z (hauteur)
        });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Point (x,y,z) d'un positionId de scène — premier point
    /// du groupe [position] col7 ; repli (fx,fy,0).</summary>
    internal static (float X, float Y, float Z) ScenePoint(
        SceneParser.Scene scene, int positionId, float fx, float fy)
        => scene.PositionGroups.TryGetValue(positionId, out var ps) &&
           ps.Count > 0 ? (ps[0].X, ps[0].Y, ps[0].Z) : (fx, fy, 0f);

    /// <summary>Event_PlayEffect <c>effectId fileNo positionId</c> :
    /// <c>CNode::AddEffect</c> zone @0x084ab100 diffuse un 383
    /// <c>ClientPlayEffect(x,y,z, 0,0,0f, "s&lt;fileNo&gt;",
    /// node&lt;&lt;16|effectId, 0)</c> à tout le node (la mission écrit
    /// le fichier « s%d » — TC_Event_PlayEffect @0x080ecd20). Le natif
    /// mémorise l'effet pour les arrivants tardifs : non porté.</summary>
    private async Task ScenePlayEffectAsync(int nodeId, int inst,
        SceneParser.Scene scene, int effectId, int fileNo, int positionId,
        float fx, float fy, CancellationToken token)
    {
        var (x, y, z) = ScenePoint(scene, positionId, fx, fy);
        var (op, payload) = PreparePacket(new CNC_CZ_ClientPlayEffect
        {
            Field1 = x, Field2 = y, Field3 = z,   // f32 : coordonnée
            Field4 = 0,                           // i32 : cible (aucune)
            Field5 = 0,                           // i32 : réservé
            Field6 = 0f,                          // f32 : échelle
            Field7 = $"s{fileNo}",                // str : fichier effet
            Field8 = nodeId << 16 | (effectId & 0xFFFF), // i32 : clé
            Field9 = 0,                           // u8  : flag
        });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Event_ChannelMsg : annonce monde du texte
    /// <c>T_TextIndex[id]</c> (natif : « %s » = nom de l'exécutant,
    /// <c>CNC_DBA_ClientChannelMsg</c> type 3 émetteur vide,
    /// <c>BroadCastToWorldServer</c>). Écart assumé : on diffuse un 316
    /// <c>ClientTextOutputIndex [index][args]</c> à toutes les sessions
    /// zone — le client résout l'index dans SA langue (FR/EN) au lieu de
    /// recevoir un texte serveur figé ; un index sans texte serveur est
    /// ignoré comme le natif.</summary>
    private async Task SceneChannelMsgAsync(EntryState st, int textId,
        CancellationToken token)
    {
        if (textId <= 0 || textId > short.MaxValue ||
            !_services.GameData.Texts.ContainsKey(textId)) return;
        await _services.Sessions.BroadcastAllAsync(
            new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = (short)textId,     // s16 : index T_TextIndex
                Field2 = st.CharName,       // str : argument « %s » (nom)
            }, token);
    }

    /// <summary>Pont texte mission→zone (NCID 20) pour les verbes de
    /// scène : <c>BornNpcByTID</c>, <c>KillNpcByTID</c>,
    /// <c>MonsterActionByHandle</c>, <c>PlaySound</c> (+ extension Eden
    /// <c>NpcWalkToByTID NpcTID= NodeID= X= Y=</c>). Formats natifs :
    /// zone @0x0887de18..0x0887df0c. Exécution sur la scène partagée
    /// (instance 0) — la mission ne connaît pas les instances d'équipe.
    /// Retourne true si le verbe a été reconnu.</summary>
    internal async Task<bool> HandleSceneTextCommandAsync(string cmd,
        CancellationToken token = default)
    {
        var c = SceneEventRules.ParseTextCommand(cmd);
        if (c == null) return false;
        var node = c.Int("NodeID");
        var scene = _services.Scenes.GetScene($"S{node:D3}");
        switch (c.Verb)
        {
            case "BornNpcByTID":
                if (scene == null) return true;
                await BornSceneNpcAsync(node, 0, scene, c.Int("NpcTID"),
                    c.Int("PositionID"), 0, 0, token);
                return true;
            case "KillNpcByTID":
                await KillSceneNpcAsync(node, 0, c.Int("NpcTID"), token);
                return true;
            case "MonsterActionByHandle":
                // « Handle » = id de groupe reborn (monster+0x360).
                await SceneMonsterActionAsync(node, 0, c.Int("Handle"),
                    c.Int("ActionID"), c.Int("DlgID"), c.Int("Time"),
                    token);
                return true;
            case "PlaySound":
                if (scene == null) return true;
                await ScenePlaySoundAsync(node, 0, scene,
                    c.Str("FileName"), c.Int("PositionID"), 0, 0, token);
                return true;
            case "PlayEffect":
                if (scene == null) return true;
                await ScenePlayEffectAsync(node, 0, scene,
                    c.Int("EffectID"), SceneEventRules.EffectFileNo(
                        c.Str("FileName")), c.Int("PositionID"), 0, 0,
                    token);
                return true;
            case "NpcWalkToByTID":      // extension Eden (non native)
                await WalkSceneNpcAsync(node, 0, c.Int("NpcTID"),
                    c.Float("X"), c.Float("Y"), token);
                return true;
            default:
                return false;
        }
    }
}
