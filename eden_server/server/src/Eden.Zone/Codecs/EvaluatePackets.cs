using Eden.Core.Game.Evaluate;
using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>Codec du 607 <c>CNC_CZ_ClientEvaluateInfo</c> — corps
/// conditionnel sur le 1er i32 (<c>Serialize</c> natif zone
/// @0x080846b0) : la classe générée aplatie est inutilisable.
/// <list type="bullet">
/// <item><b>type 0</b> — statut/temps : <c>[i32 0][i32 status][i32
/// sec]</c> ; <c>SendTimeInfoToAll</c> @0x845c260 (+0x2c et
/// event−now). Le reset <c>[0][-1][0]</c> est émis à
/// <c>CharacterLeave</c> @0x8082310.</item>
/// <item><b>type 1</b> — compteurs : <c>[i32 1][rawScore][deaths]
/// [mon][elite][boss][maxDeaths][maxMon][maxElite][maxBoss]</c>
/// (<c>SendScoreInfoToAll</c> @0x845c040 — eval +0x44,+0x48,+0x4c,
/// +0x50,+0x54,+0xc,+0x10,+0x14,+0x18).</item>
/// <item><b>type 2</b> — résultat : <c>[i32 2][elapsed][timeBonus]
/// [deaths][mon][elite][boss][maxDeaths][maxMon][maxElite][maxBoss]
/// [dieBonus][monScore][eliteScore][bossScore][rawScore][totalScore]
/// [str grade][opens][buys][cost]</c> — l'ordre du fil est celui du
/// Serialize (this+0xc,+0x44,+0x14…+0x58), pas celui des offsets.
/// (<c>End</c> @0x845d340).</item>
/// <item><b>type 3</b> — loterie : <c>[i32 3][opens][itemId][count]
/// [index]</c> (<c>LotteryOpen</c> @0x845dfd0) + sentinelles
/// [-1]=claim, [-2]=reset, [5|6]=achat.</item>
/// </list></summary>
public static class EvaluatePackets
{
    /// <summary>607 type 0 — <paramref name="status"/> = statut
    /// natif +0x2c (−1 au reset, 1 run, 2 loterie, 3 close) ;
    /// <paramref name="secondsLeft"/> = échéance event − now.</summary>
    public static byte[] BuildStatus(int status, int secondsLeft)
    {
        var s = new OutBinStream();
        s.WriteInt(0);              // i32 : type 0
        s.WriteInt(status);         // i32 : statut (+0x2c)
        s.WriteInt(secondsLeft);    // i32 : secondes restantes
        return s.ToArray();
    }

    /// <summary>607 type 1 — compteurs courants + plafonds DSL.</summary>
    public static byte[] BuildCounters(EvaluateInstance ev)
    {
        var s = new OutBinStream();
        s.WriteInt(1);              // i32 : type 1
        s.WriteInt(ev.RawScore);    // i32 : +0x44 score brut (Σ exp)
        s.WriteInt(ev.Deaths);      // i32 : +0x48 morts d'équipe
        s.WriteInt(ev.MonKills);    // i32 : +0x4c kills normaux
        s.WriteInt(ev.EliteKills);  // i32 : +0x50 kills élite
        s.WriteInt(ev.BossKills);   // i32 : +0x54 kills boss
        s.WriteInt(ev.Def.MaxDeaths); // i32 : +0x0c seuil max morts
        s.WriteInt(ev.Def.MaxMon);    // i32 : +0x10 seuil max mon
        s.WriteInt(ev.Def.MaxElite);  // i32 : +0x14 seuil max élite
        s.WriteInt(ev.Def.MaxBoss);   // i32 : +0x18 seuil max boss
        return s.ToArray();
    }

    /// <summary>607 type 2 — résultat complet émis à End. L'ordre
    /// suit le Serialize natif : elapsed, timeBonus, compteurs
    /// courants, plafonds, scores partiels, rawScore, totalScore,
    /// string grade, params loterie (opens/buys/cost).</summary>
    public static byte[] BuildResult(EvaluateInstance ev)
    {
        var s = new OutBinStream();
        s.WriteInt(2);                  // i32 : type 2
        s.WriteInt(ev.ElapsedSeconds);  // i32 : +0x38 temps écoulé
        s.WriteInt(ev.TimeBonus);       // i32 : +0x3c bonus temps
        s.WriteInt(ev.Deaths);          // i32 : +0x48
        s.WriteInt(ev.MonKills);        // i32 : +0x4c
        s.WriteInt(ev.EliteKills);      // i32 : +0x50
        s.WriteInt(ev.BossKills);       // i32 : +0x54
        s.WriteInt(ev.Def.MaxDeaths);   // i32 : +0x0c
        s.WriteInt(ev.Def.MaxMon);      // i32 : +0x10
        s.WriteInt(ev.Def.MaxElite);    // i32 : +0x14
        s.WriteInt(ev.Def.MaxBoss);     // i32 : +0x18
        s.WriteInt(ev.DieBonus);        // i32 : +0x1c
        s.WriteInt(ev.MonScore);        // i32 : +0x20
        s.WriteInt(ev.EliteScore);      // i32 : +0x24
        s.WriteInt(ev.BossScore);       // i32 : +0x28
        s.WriteInt(ev.RawScore);        // i32 : +0x44
        s.WriteInt(ev.TotalScore);      // i32 : +0x40
        s.WriteString(ev.Grade?.Name ?? ""); // str : +0xc8 grade
        s.WriteInt(ev.Grade?.Opens ?? 0);    // i32 : +0xbc opens
        s.WriteInt(ev.Grade?.Buys ?? 0);     // i32 : +0xc0 buys
        s.WriteInt(ev.Grade?.Cost ?? 0);     // i32 : +0xc4 coût×1000
        return s.ToArray();
    }

    /// <summary>607 type 3 — loterie : pick
    /// <c>[3][opens][itemId][count][index]</c> et sentinelles
    /// (index −1 = claim, −2 = reset, 5/6 = achat avec itemId=0).</summary>
    public static byte[] BuildLottery(int opens, int itemId, int count,
        int index)
    {
        var s = new OutBinStream();
        s.WriteInt(3);        // i32 : type 3
        s.WriteInt(opens);    // i32 : ouvertures restantes
        s.WriteInt(itemId);   // i32 : item gagné (0 sentinelle)
        s.WriteInt(count);    // i32 : quantité (0 sentinelle)
        s.WriteInt(index);    // i32 : index boîte / sentinelle
        return s.ToArray();
    }
}
