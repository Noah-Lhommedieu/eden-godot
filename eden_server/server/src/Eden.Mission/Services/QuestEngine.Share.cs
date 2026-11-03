using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>Partiel partage de quête — <c>CGlobalInfo::ShareMission</c>
/// @0x0807bfd0 (verdicts : <see cref="QuestShareRules"/>) appelé par
/// <c>TC_MissionServiceOp</c> @0x081f2ec0 cas 6 ; l'acceptation du
/// destinataire (op 7) réutilise <see cref="AcceptMissionAsync"/>
/// (SetFMS(3) natif).</summary>
public sealed partial class QuestEngine
{
    /// <summary>Verdict d'éligibilité d'un destinataire — pur, testable.</summary>
    internal Eden.Core.Game.Services.QuestShareRules.Verdict ShareVerdict(
        CharContext target, MissionParser.MissionDef m) =>
        QuestShareRules.Check(m.Field6, target.Active.Count,
            target.FMS.GetValueOrDefault(m.Id),
            IsOfferable(target, m));

    /// <summary>Approbation d'acceptation (FMSCheckApproval == 6) :
    /// non active, non bloquée, FMS vierge/reprenable, niveau, conditions
    /// — mêmes critères que l'offre de dialogue.</summary>
    private bool IsOfferable(CharContext c, MissionParser.MissionDef m)
    {
        if (c.Active.ContainsKey(m.Id)) return false;
        var offerable = m.CompleteLimit > 0 && IsRepeatable(m)
            ? !DoneBlocked(c, m)
            : !DoneBlocked(c, m) &&
              c.FMS.GetValueOrDefault(m.Id) is 0 or 6;
        var reqLv = Col(m, 8);
        return offerable && (reqLv == 0 || reqLv <= c.Level) &&
               ConditionsPass(c, m);
    }

    /// <summary>MissionServiceOp mid 6 targetUid — le partageur propose
    /// la quête à <paramref name="targetUid"/>. Succès : commande zone
    /// <c>shop mission-share 0 mid</c> au destinataire (ouvre sa fiche
    /// de partage) + textes 0x61c (destinataire, nom du partageur) et
    /// 0xac (partageur, « titre,nom »). Échec : texte du verdict au
    /// partageur + <c>shop mission-share-fail</c> au destinataire.</summary>
    public async Task OnShareAsync(MissionSession s, int sharerUid, int mid,
        int targetUid)
    {
        if (!_chars.TryGetValue(sharerUid, out var sharer) ||
            !_chars.TryGetValue(targetUid, out var target)) return;
        var m = _data.Missions.FirstOrDefault(x => x.Id == mid);
        if (m == null || !sharer.Active.ContainsKey(mid)) return;
        var ts = target.Session ?? s;
        var verdict = ShareVerdict(target, m);
        var targetName = await _db.GetCharNameAsync(targetUid);
        if (verdict != QuestShareRules.Verdict.Ok)
        {
            await SendTextAsync(sharer.Session ?? s, sharerUid,
                QuestShareRules.TextOf(verdict), targetName);
            await SendTransitionAsync(ts, target.NodeId, targetUid,
                $"shop mission-share-fail 0 {mid}");
            EdenLog.Write($"[Mission] share {mid} {sharerUid}→{targetUid} " +
                $"refusé : {verdict}");
            return;
        }
        var sharerName = await _db.GetCharNameAsync(sharerUid);
        var title = _missionTitles.GetValueOrDefault(mid, m.Name);
        await SendTransitionAsync(ts, target.NodeId, targetUid,
            $"shop mission-share 0 {mid}");
        await SendTextAsync(ts, targetUid, QuestShareRules.TextOffered,
            sharerName);
        await SendTextAsync(sharer.Session ?? s, sharerUid,
            QuestShareRules.TextSharedTo, $"{title},{targetName}");
        EdenLog.Write($"[Mission] share {mid} {sharerUid}→{targetUid} proposé");
    }

    /// <summary>66 TextOutputIndex vers la zone du perso (même corps que
    /// MissionServer.SendTextOutputIndexAsync).</summary>
    private static Task SendTextAsync(MissionSession s, int charUid,
        short textId, string param)
    {
        var txt = Eden.Core.Data.EdenEncoding.Big5.GetBytes(param ?? "");
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(charUid));          // i32 : charUid
        b.AddRange(BitConverter.GetBytes((short)0));         // s16 : node
        b.AddRange(BitConverter.GetBytes(textId));           // s16 : textId
        b.AddRange(BitConverter.GetBytes((ushort)txt.Length)); // str : param
        b.AddRange(txt);
        b.Add(0x9b);                                         // u8  : canal
        b.Add(0);                                            // u8  : +0x14
        return s.SendAsync(66, b.ToArray());
    }
}
