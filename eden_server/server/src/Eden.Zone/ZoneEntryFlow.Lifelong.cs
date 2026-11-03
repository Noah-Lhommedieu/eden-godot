using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

// ─── Class lifelong (compteurs par classe + médailles Crystal Cross) ───
//
// Spécification native (zone/decompiled.c) :
// - char+0xc58 = map<s16 groupe, SClassLifelong> — la clé est le
//   « groupe waken » CClassData+8 = S_Class col2, PAS le class_id
//   (GetWakenGroup(class,race) via IGameData vtable +0x1d8). La classe
//   native lue est char+0x47c, la race char+0x4aa>>4.
// - char+0xc54 = lifelong_gold (player_characters.lifelong_gold) —
//   médailles « Crystal Cross Medal » (S_TextIndex 4524), cap 9999999.
// - SClassLifelong : +0x14 territory_kill (type 0), +0x18 colosseum_win
//   (type 1), +0x1c rare_monster_kill (type 2), +0x20 daily_monster_kill
//   (type 3), +0x24 pending daily s16 (flush >9 ou à la sortie).
// - CCharacter::AddLifelongPointAndGold @0x084192b0 (types 0-2) : le
//   palier d'or est choisi sur l'ANCIEN compteur (S_Parameter
//   419-439 : seuils LV1-3 puis récompenses LV1-4), le compteur
//   += delta, l'or += palier (cap 9999999), puis 555 [i32 or] et
//   554 [s16 groupe][s16 type][i32 compteur] ; l'appelant émet enfin le
//   texte 0x1100 « Obtain %s1 unit(s) of %s2 » avec « or,nom ».
// - CCharacter::AddLifelongPoint @0x084198c0 (type 3 seul) : daily++,
//   pending++, 554 [groupe][3][daily] ; si pending>9 → mission
//   « ClassLifelongAdd class type add_point gold » et pending=0.
// - Persistances : deltas « ClassLifelongAdd » par batch (type0 ≥5
//   kills du territoire, type3 >9) ou immédiat (types 1-2), absolu
//   « ClassLifelongUpdate » à la sortie (LifelongUpdateAllToMission
//   @0x084236e0). Chez nous la zone est la seule écrivaine : deltas
//   UPSERT directs + flush pending à la persistance du perso.
// - Reset daily : mission compare exec+0x760 (jour persisté dans le
//   blob flags %zX:LastDailyLifelongResetDay) au jour courant →
//   daily/pending = 0 + 554 [groupe][3][0] par noeud
//   (CNE_ZMis_ZoneServerResetDailyMonsterKill @0x08333ad0).
public sealed partial class ZoneEntryFlow
{
    /// <summary>Cap natif de l'or lifelong
    /// (<c>char+0xc54</c> borné à 9999999 dans
    /// <c>AddLifelongPointAndGold</c> @0x084192b0).</summary>
    private const int LifelongGoldCap = 9999999;
    /// <summary>T_TextIndex 4352 « Obtain %s1 unit(s) of %s2 » —
    /// vtable+0x58 de CCharacter (TextOutputIndex), canal -105.</summary>
    private const int LifelongTextObtain = 0x1100;
    /// <summary>S_TextIndex 4524 « Crystal Cross Medal » — le nom passé
    /// en %s2 est résolu serveur-side (IGameData+0x114).</summary>
    private const int LifelongMedalTextId = 4524;

    // S_Parameter : seuils LV1-3 puis récompenses LV1-4 par type —
    // offsets natifs /4 → id (0x68c/4=419 … 0x6dc/4=439).
    private static readonly int[] LifelongThresholds = { 419, 426, 433 };
    private static readonly int[] LifelongGoldIds = { 422, 429, 436 };

    /// <summary>Groupe waken du perso — S_Class col2
    /// (<c>ClassDefinition.DisplayIndex</c>) ; col2 est identique pour
    /// toutes les races d'une même classe de base (21/121/…→1).</summary>
    private short LifelongGroup(EntryState st)
        => LifelongGroup(_services.GameData.Classes, st.ClassId);

    /// <summary>Groupe waken d'un class_id — retour class_id à défaut
    /// (classes hors S_Class). <c>&amp; 0xff</c> comme le natif
    /// (<c>uVar7 &amp; 0xff</c> dans AddLifelongPointAndGold).</summary>
    internal static short LifelongGroup(
        IReadOnlyList<ClassStatsParser.ClassDefinition> defs,
        int classId)
    {
        var def = defs.FirstOrDefault(c => c.BaseClassIndex == classId);
        return (short)((def?.DisplayIndex ?? classId) & 0xff);
    }

    /// <summary>Noeud de compteurs d'un groupe — l'opérateur [] de la
    /// map native crée l'entrée vide au besoin.</summary>
    private static LifelongCounters LifelongNode(EntryState st,
        short group)
    {
        if (!st.Lifelong.TryGetValue(group, out var e))
            st.Lifelong[group] = e = new LifelongCounters();
        return e;
    }

    private static int LifelongCounter(LifelongCounters e, int type)
        => type switch
        {
            0 => e.TerritoryKill,
            1 => e.ColosseumWin,
            2 => e.RareMonsterKill,
            _ => e.DailyMonsterKill,
        };

    private static void LifelongAdd(LifelongCounters e, int type,
        int delta)
    {
        switch (type)
        {
            case 0: e.TerritoryKill += delta; break;
            case 1: e.ColosseumWin += delta; break;
            case 2: e.RareMonsterKill += delta; break;
            default: e.DailyMonsterKill += delta; break;
        }
    }

    /// <summary>Palier d'or natif : le palier est choisi sur l'ANCIEN
    /// compteur avant incrément — `seuil[i] &lt;= ancien` en cascade
    /// (AddLifelongPointAndGold @0x084192b0) : &lt;LV1 → or LV1,
    /// [LV1,LV2) → or LV2, [LV2,LV3) → or LV3, ≥LV3 → or LV4 (0).</summary>
    private int LifelongGoldTier(int type, int oldCount)
        => LifelongGoldTier(_services.GameData.Parameters, type,
            oldCount);

    /// <summary>Palier sur paramètres injectés — testable sans
    /// services. type ∈ {0 territoire, 1 colosseum, 2 rare}.</summary>
    internal static int LifelongGoldTier(
        ParameterParser.ParameterSet p, int type, int oldCount)
    {
        var tBase = LifelongThresholds[type];
        var gBase = LifelongGoldIds[type];
        int tier;
        if ((int)p.Get(tBase + 2) <= oldCount) tier = 3;
        else if ((int)p.Get(tBase + 1) <= oldCount) tier = 2;
        else if ((int)p.Get(tBase) <= oldCount) tier = 1;
        else tier = 0;
        return (int)p.Get(gBase + tier);
    }

    /// <summary>Chargement à l'entrée — copie exec→zone native :
    /// map <c>class_lifelong</c> + or + jour du dernier reset persisté
    /// dans <c>flags</c>. Si le jour diffère → reset lazy
    /// (ResetDailyMonsterKill natif).</summary>
    private async Task LoadLifelongAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var map = await _services.LifelongRepo
            .LoadAsync((int)st.CharId);
        foreach (var kv in map)
            st.Lifelong[kv.Key] = kv.Value;
        st.LifelongGold = await _services.LifelongRepo
            .GetGoldAsync((int)st.CharId);
        st.LifelongDay = await _services.LifelongRepo
            .GetResetDayAsync((int)st.CharId);
        await CheckLifelongDayAsync(session, st, token);
    }

    /// <summary>Reset quotidien lazy — jour UTC ≠ jour persisté :
    /// daily/pending à 0 dans tous les noeuds + UPDATE, 554
    /// [groupe][3][0] par noeud chargé, puis mémorisation du jour
    /// dans <c>flags</c> (équivalent de ResetDailyMonsterKill +
    /// LastDailyLifelongResetDay natifs).</summary>
    private async Task CheckLifelongDayAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var today = DateTime.UtcNow.Day;
        if (st.LifelongDay == today) return;
        st.LifelongDay = today;
        await _services.LifelongRepo.SetResetDayAsync(
            (int)st.CharId, today);
        if (st.Lifelong.Count == 0) return;
        await _services.LifelongRepo.ResetDailyAsync((int)st.CharId);
        foreach (var (group, e) in st.Lifelong)
        {
            e.DailyMonsterKill = 0;
            e.PendingDaily = 0;
            await session.SendAsync(new CNC_CZ_ClientUpdateLifelongPoint
            {
                Field1 = group,   // s16 : groupe waken
                Field2 = 3,       // s16 : type 3 = daily_monster_kill
                Field3 = 0,       // i32 : compteur remis à zéro
            }, token);
        }
    }

    /// <summary>Type 3 — tout kill à exp&gt;0 par receveur
    /// (<c>AddLifelongPoint</c> @0x084198c0 dans GiveExp/GiveTeamExp) :
    /// daily++/pending++, 554 [groupe][3][daily] ; pending&gt;9 →
    /// UPSERT daily += pending puis pending=0 (ClassLifelongAdd
    /// batched natif).</summary>
    private async Task AddLifelongDailyPointAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await CheckLifelongDayAsync(session, st, token);
        var group = LifelongGroup(st);
        var e = LifelongNode(st, group);
        e.DailyMonsterKill++;
        e.PendingDaily++;
        await session.SendAsync(new CNC_CZ_ClientUpdateLifelongPoint
        {
            Field1 = group,                  // s16 : groupe waken
            Field2 = 3,                      // s16 : type 3 daily
            Field3 = e.DailyMonsterKill,     // i32 : nouveau compteur
        }, token);
        if (e.PendingDaily <= 9) return;
        await _services.LifelongRepo.AddPointAsync((int)st.CharId,
            group, 3, e.PendingDaily);
        e.PendingDaily = 0;
    }

    /// <summary>Types 0-2 — <c>AddLifelongPointAndGold</c>
    /// @0x084192b0 : compteur += delta, or += palier (cap), 555 puis
    /// 554, puis le texte 0x1100 « Obtain N Crystal Cross Medal » et
    /// la persistance immédiate (ClassLifelongAdd + or absolu).
    /// Le type 2 est appelé par kill d'un mob dont le succès
    /// (achId == mobId, S_Achievement.Type==1) porte un Point&gt;0 ;
    /// type 1 par victoire colosseum ; type 0 par kill unique en
    /// guerre de territoire (hook prêt — le combat de territoire
    /// n'est pas encore porté).</summary>
    private async Task AddLifelongPointAndGoldAsync(NetSession session,
        EntryState st, int type, int delta, CancellationToken token)
    {
        if (type is < 0 or > 2 || delta <= 0) return;
        var group = LifelongGroup(st);
        var e = LifelongNode(st, group);
        var gold = LifelongGoldTier(type, LifelongCounter(e, type));
        LifelongAdd(e, type, delta);
        var oldGold = st.LifelongGold;
        st.LifelongGold = await _services.LifelongRepo.AddGoldAsync(
            (int)st.CharId, gold);
        var gained = st.LifelongGold - oldGold;
        await _services.LifelongRepo.AddPointAsync((int)st.CharId,
            group, type, delta);
        // 555 ClientUpdateLifelongGold [i32] : nouvel or total.
        await session.SendAsync(new CNC_CZ_ClientUpdateLifelongGold
        { Value = st.LifelongGold }, token);
        // 554 [s16 groupe][s16 type][i32 compteur].
        await session.SendAsync(new CNC_CZ_ClientUpdateLifelongPoint
        {
            Field1 = group,
            Field2 = (short)type,
            Field3 = LifelongCounter(e, type),
        }, token);
        // Texte natif vtable+0x58 : « %d,%s » → « or,nom médaille » —
        // émis même à 0 (gain cappé) comme l'appelant natif.
        var medal = _services.GameData.Texts
            .TryGetValue(LifelongMedalTextId, out var n)
            ? n : "Crystal Cross Medal";
        await SendTextIndexArgsAsync(session, LifelongTextObtain,
            $"{gained},{medal}", token);
        // HandleRASpecialPoint(char,2,delta) @0x0840c2b0 : les
        // activités EventKind 9/EventParam 2 progressent de l'or gagné.
        if (gained > 0)
            await BumpRecommendLifelongGoldAsync(session, st, gained,
                token);
    }

    /// <summary>Progression des activités recommandées de type
    /// « gain d'or lifelong » — EventKind 9 / EventParam 2
    /// (HandleRASpecialPoint param=2), capée à la cible, puis
    /// FinishedProcess quand la cible est atteinte.</summary>
    private Task BumpRecommendLifelongGoldAsync(
        NetSession session, EntryState st, int delta,
        CancellationToken token) =>
        BumpRecommendSpecialPointAsync(session, st, 2, delta, token);

    /// <summary>HandleRASpecialPoint générique — EventKind 9 /
    /// EventParam <paramref name="eventParam"/> (2 = or lifelong,
    /// 3 = or colosseum — boucle char+0xc90..c98 de
    /// GainColosseumGold @0x8418f30), capée à la cible, puis
    /// FinishedProcess quand la cible est atteinte.</summary>
    private async Task BumpRecommendSpecialPointAsync(
        NetSession session, EntryState st, int eventParam, int delta,
        CancellationToken token)
    {
        foreach (var (id, progress) in st.RecommendProgress.ToList())
        {
            var def = _services.GameData.GetRecommendActivity(id);
            if (def is not { EventKind: 9 } ||
                def.EventParam != eventParam) continue;
            if (progress >= def.Target) continue;
            var next = Math.Min(progress + (uint)delta,
                (uint)def.Target);
            st.RecommendProgress[id] = next;
            if (next < (uint)def.Target ||
                st.RecommendFinished.ContainsKey(id))
                continue;
            st.RecommendFinished[id] = 0;
            if (_services.Mission is { } ml)
                await ml.SendAgentDbaCommandAsync(st.CharId,
                    $"Update RecommendActivity ActivityID = {id} " +
                    $"Parameter = {next} Finished = 0", token);
            await session.SendAsync(
                new CNC_CZ_ClientUpdateRecommendActivities
                { Field1 = id, Field2 = next, Field3 = 0 }, token);
        }
    }

    /// <summary>Flush du pending daily à la sortie/migration —
    /// <c>LifelongUpdateAllToMission</c> @0x084236e0 + la boucle
    /// +0x24 du départ de scène : pending&gt;0 → UPSERT daily.</summary>
    private async Task FlushLifelongPendingAsync(EntryState st)
    {
        foreach (var (group, e) in st.Lifelong)
        {
            if (e.PendingDaily <= 0) continue;
            await _services.LifelongRepo.AddPointAsync((int)st.CharId,
                group, 3, e.PendingDaily);
            e.PendingDaily = 0;
        }
    }
}
