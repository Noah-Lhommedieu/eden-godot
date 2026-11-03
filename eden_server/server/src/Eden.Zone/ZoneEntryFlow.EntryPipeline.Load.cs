using Eden.Core.Data.Parsers;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Lottery;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Core.Game.Work;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partiel pipeline — méthodes « Apply » hors-boucle qui
/// peuplent le scratch <c>EntryState</c> depuis les lignes
/// préchargées, et préparations DB (seeds, consommations, purges)
/// autrefois exécutées sur le fil de la boucle.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Vitals d'entrée — même calcul que l'ancien ticket :
    /// agrégat équipement porté + formule CCharFormula (S_Level ×
    /// S_Class%) + perk Limitless max_hp ; vitals persistés en
    /// fallback ; clamps identiques (staleMax, min courant/max).</summary>
    private void ApplyEntryVitals(EntryState sc,
        PlayerCharacterEntity chr, List<ItemEntity> equipped,
        double maxHpPerk)
    {
        var eqV = EquipmentStatService.Sum(equipped,
            _services.GameData.Items, _services.EquipTrain.ItemTrainAdds);
        var (mxHp, mxMp) = ProfileStatEmitter.ComputeVitals(
            _services, sc.RaceId, sc.ClassId, sc.Level,
            eqV.BonusHp, eqV.BonusMp, eqV.Def);
        if (maxHpPerk > 0) mxHp += (int)maxHpPerk;
        if (mxHp > 0 && mxMp > 0)
        {
            sc.PlayerMaxHp = mxHp;
            sc.PlayerMaxMp = mxMp;
        }
        else
        {
            sc.PlayerMaxHp = chr.MaxHp > 0 ? chr.MaxHp : 400;
            sc.PlayerMaxMp = chr.MaxMp > 0 ? chr.MaxMp : 200;
        }
        var staleMax = chr.MaxHp > 0 && chr.MaxHp < sc.PlayerMaxHp;
        sc.PlayerHp = chr.CurrentHp > 0 && !staleMax
            ? Math.Min(chr.CurrentHp, sc.PlayerMaxHp) : sc.PlayerMaxHp;
        staleMax = chr.MaxMp > 0 && chr.MaxMp < sc.PlayerMaxMp;
        sc.PlayerMp = chr.CurrentMp > 0 && !staleMax
            ? Math.Min(chr.CurrentMp, sc.PlayerMaxMp) : sc.PlayerMaxMp;
    }

    /// <summary>DailyLoginBegin natif sur ligne préchargée — même
    /// bascule de jour que <c>LoadDailyAwardAsync</c> + save.
    /// Non fatal : une ligne cassée ne doit pas avorter l'entrée.</summary>
    private async Task ApplyDailyAwardEntryAsync(EntryState sc,
        bool dailyEnabled, DailyAwardRepository.DailyAwardRow row)
    {
        try
        {
            sc.DailyEnabled = dailyEnabled;
            sc.DailyDayIndex = (short)row.DayIndex;
            sc.DailyLoginTime = row.LoginTime;
            sc.DailyCooldown = row.CooldownTime;
            sc.DailyAwardDays = (ulong)row.AwardDays;
            sc.DailyAwardRecords = (ulong)row.AwardRecords;
            sc.DailyLoginStatus = (byte)row.LoginStatus;
            sc.DailySessionDelta = 0;
            sc.DailySessionFlag = true;      // +0xdf4 session en cours
            sc.DailyLoaded = true;
            if (!sc.DailyEnabled) return;
            if (DayOut(sc.DailyLoginTime))
            {
                sc.DailyLoginStatus = 0;
                sc.DailyCooldown = 0;
                // Même avance que la bascule DailyUpdate : bit du jour
                // posé + def jour+2 existant + borne 0x1c.
                var idx = sc.DailyDayIndex;
                if (((sc.DailyAwardDays >> (idx & 0x3f)) & 1) != 0
                    && _services.GameData.DailyAwards.ContainsKey(idx + 2)
                    && idx + 1 < 0x1c)
                    sc.DailyDayIndex = (short)(idx + 1);
            }
            sc.DailyLoginTime = NowUnix();
            await SaveDailyAwardAsync(sc);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Entry] DailyAward load ko " +
                $"char=#{sc.CharId} : {ex.Message}");
        }
    }

    /// <summary>Métiers — player_life_skill sur lignes préchargées :
    /// dérive de groupe si -1 (persistée), puis seed batch des
    /// recettes de base manquantes (col11==1 du groupe — la
    /// restriction « aucune ligne » du groupe 1 est conservée).</summary>
    private async Task ApplyLifeSkillsAsync(EntryState sc,
        List<PlayerLifeSkillEntity> lifeRows)
    {
        var cid = (int)sc.CharId;
        if (sc.LifeSkill < 0)
        {
            sc.LifeSkill = 1;
            await _characters.SetLifeSkillAsync(cid, sc.LifeSkill);
        }
        foreach (var ls in lifeRows)
            sc.LifeSkillLearned[ls.LifeSkillId] = (ls.Exp, ls.DueDate);
        var learned0 = sc.LifeSkillLearned.Count;
        var toLearn = new List<int>();
        foreach (var r in _services.GameData.LifeSkillRecipes)
        {
            if (r.PrevId != 1 || r.Group != sc.LifeSkill
                || sc.LifeSkillLearned.ContainsKey(r.Id)) continue;
            if (r.Group == 1 && learned0 != 0) continue;
            sc.LifeSkillLearned[r.Id] = (0, 0);
            toLearn.Add(r.Id);
        }
        if (toLearn.Count > 0)
            await _services.LifeSkills.InsertManyAsync(cid, toLearn);
    }

    /// <summary>Variante pipeline de <see cref="LoadWorksAsync"/> :
    /// même séquence (échus matérialisés, timers réarmés, 299 du
    /// reste) mais la consommation/tirages/saves se font hors-boucle,
    /// les armements sont collectés pour l'install et les summons
    /// ne spawnent pas (registry lié à la boucle).</summary>
    private async Task LoadWorksEntryAsync(NetSession session,
        EntryState sc, EntryBundle bundle, CancellationToken token)
    {
        try
        {
            // EnsureRows a déjà tourné dans le prefetch parallèle.
            sc.Works = await _services.WorksRepo
                .GetWorksAsync((int)sc.CharId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var work in sc.Works)
            {
                if (work.WorkId == 0) continue;
                if (work.FinishAtMs <= now)
                    await MaterializeWorkEntryAsync(session, sc, bundle,
                        work, token);
                else if (work.Running)
                {
                    // DEV — même plafond qu'à l'armement (10 s) :
                    // dev : remettre 30 min natif plus tard.
                    var devCapMs = (long)DevWorkDurationUnits
                        * WorkOrderService.UnitMs;
                    if (work.FinishAtMs - now > devCapMs)
                    {
                        work.FinishAtMs = now + devCapMs;
                        await _services.WorksRepo.EnsureAndSaveAsync(
                            (int)sc.CharId, work);
                    }
                    bundle.WorkTimers.Add(work.WorkNum);
                    // 299 avec le reste réel — émis hors-boucle dans
                    // la même position relative qu'avant (pendant le
                    // traitement du ticket, avant l'install).
                    var rest = (int)Math.Max(1,
                        (work.FinishAtMs - now)
                        / WorkOrderService.UnitMs);
                    await SendWorkEnchantAsync(session, sc,
                        work.WorkNum, rest, token);
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[Zone] LoadWorks char={sc.CharId} : {ex.Message}");
            sc.Works = Array.Empty<WorkSlot>();
        }
    }

    /// <summary>Variante pipeline de <see cref="MaterializeWorkAsync"/>
    /// (emitResult=false toujours au login) : les ordres « summon » ne
    /// spawnent pas ici — le lancer de dés et le reset DB restent
    /// hors-boucle, le spawn + emits sont collectés pour l'install.</summary>
    private async Task MaterializeWorkEntryAsync(NetSession session,
        EntryState sc, EntryBundle bundle, WorkSlot work,
        CancellationToken token)
    {
        if (!work.Running) return;
        if (sc.WorkDeferredLocs.TryGetValue(work.WorkNum, out var locs)
            && sc.WorkOpStarted)
        {
            var plan = RebuildPlan(work, locs);
            if (plan is { Deferred: true }
                && !await ConsumeWorkInputsAsync(session, sc, plan,
                    locs, deferred: true, token))
                work.WorkId = -Math.Abs(work.WorkId);
        }
        if (work.WorkId > 0)
        {
            if (work.WorkType == WorkTypes.Summon
                && _services.GameData.WorkCollectById
                    .TryGetValue(work.WorkId, out var summon)
                && summon != null)
            {
                await PrepareSummonEntryAsync(sc, bundle, work, summon);
                return;
            }
            if (_services.GameData.WorkCollectById
                    .TryGetValue(work.WorkId, out var collect)
                && collect != null && WorkTypes.IsCollect(work.WorkType))
                _services.Works.RollCollectProducts(collect, work);
            else if (_services.GameData.WorkCombineById
                    .TryGetValue(work.WorkId, out var combine)
                && combine != null && WorkTypes.IsCombine(work.WorkType))
                _services.Works.RollCombineProduct(combine, work);
            if (work.Products.All(p => p.IsEmpty))
                work.WorkId = -Math.Abs(work.WorkId);
        }
        work.Running = false;
        work.FinishAtMs = 0;
        await _services.WorksRepo.EnsureAndSaveAsync(
            (int)sc.CharId, work);
        await RemoveWorkEnchantAsync(session, sc, work.WorkNum, token);
    }

    /// <summary>EndSummon hors-boucle : dé pondéré + reset DB du slot
    /// (mêmes effets persistés que <see cref="MaterializeSummonAsync"/>)
    /// ; le spawn des mobs, le 300 et le 435 sont différés à
    /// l'install (registry + boucle).</summary>
    private async Task PrepareSummonEntryAsync(EntryState sc,
        EntryBundle bundle, WorkSlot work,
        WorkOrderParser.WorkCollectDef def)
    {
        var roll = Random.Shared.Next(100);
        float acc = 0;
        WorkOrderParser.WorkCollectDef.Product? pick = null;
        foreach (var p in def.Products)
        {
            acc += Math.Max(p.Rate, 0f);
            if (roll < acc) { pick = p; break; }
        }
        if (pick != null && pick.ItemId > 0)
            bundle.PendingSummons.Add((work.WorkNum, pick.ItemId,
                Math.Max(1, pick.Quantity + work.BonusItemAmount)));
        else
            EdenLog.Write($"[Zone] Summon work char={sc.CharId} " +
                $"num={work.WorkNum} : dice sans entrée " +
                $"(workId={work.WorkId})");
        sc.WorkDeferredLocs.Remove(work.WorkNum);
        work.Reset();
        await _services.WorksRepo.ResetAsync((int)sc.CharId, work);
    }

    /// <summary>Chemin d'éveil sur lignes préchargées — miroir de
    /// <see cref="LoadDestinyAsync"/>.</summary>
    private static void ApplyDestiny(EntryState sc, int vit,
        DestinyRoadRow? row)
    {
        sc.DestinyVit = vit;
        if (row == null || row.RoadId == 0) return;
        var d = new DestinyState
        {
            RoadId = (short)row.RoadId,
            RoadType = (short)row.RoadType,
            Step = (short)row.Step,
            RollsLeft = (short)row.RollsLeft,
            DpPool = row.DpPool,
            MovePoints = (short)row.MovePoints,
            FunctionState = (short)row.FunctionState,
        };
        for (var i = 0; i < 39; i++) d.Slots[i] = (short)row.Slots[i];
        sc.Destiny = d;
    }

    /// <summary>Fast-dress sur lignes préchargées — miroir de
    /// <see cref="LoadFastDressAsync"/> (seed fait par le repo).</summary>
    private static void ApplyFastDress(EntryState sc,
        List<Eden.Core.Game.Inventory.FastDressRecord> rows)
    {
        var state = Eden.Core.Game.Inventory.FastDressState.Create();
        foreach (var r in rows)
        {
            if (r.Page is < 0 or >= Eden.Core.Game.Inventory
                    .FastDressSlots.PageCount) continue;
            if (state.Saved[r.Page].ContainsKey(r.TarLoc)) continue;
            state.Saved[r.Page][r.TarLoc] = r;
            var copy = Eden.Core.Game.Inventory.FastDressRecord
                .Empty(r.Page, r.TarLoc);
            copy.CopyFrom(r);
            state.Edited[r.Page][r.TarLoc] = copy;
        }
        for (var p = 0; p < Eden.Core.Game.Inventory
                 .FastDressSlots.PageCount; p++)
            foreach (var key in Eden.Core.Game.Inventory
                         .FastDressSlots.EquipLocs)
                if (!state.Edited[p].ContainsKey((int)key))
                    state.Edited[p][(int)key] = Eden.Core.Game.Inventory
                        .FastDressRecord.Empty(p, (int)key);
        sc.FastDress = state;
    }

    /// <summary>Octroi des sorts de classe actifs + généraux sur
    /// lignes préchargées — un upsert batch unnest au lieu de N
    /// <c>LearnSpellAsync</c>. La liste finale (connus + appris
    /// niveau 1) alimente le 311. Retourne les tuples
    /// (spellmaster, level) complets.</summary>
    private async Task<List<(short Spell, short Level)>>
        EnsureEntrySpellsAsync(EntryState sc, EntryBundle bundle,
            EntryPrefetch pf)
    {
        var known = pf.Spells
            .Select(s => unchecked((ushort)s.Spell)).ToHashSet();
        var learn = new List<short>();
        try
        {
            var classLevel = Math.Max(bundle.ClassRow?.ClassLevel
                ?? sc.ClassLevel, 1);
            var classDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, sc.RaceId, sc.ClassId);
            foreach (var sm in ClassSkillTable.GrantableSpells(
                         sc.ClassId, classLevel,
                         _services.GameData.Spells, classDef, sc.RaceId))
                if (known.Add(unchecked((ushort)sm)))
                    learn.Add(unchecked((short)sm));
            foreach (var cls in pf.Classes)
                foreach (var sm in ClassSkillTable
                        .GrantableGeneralSpells(cls.ClassId, sc.Level,
                            _services.GameData.Spells, sc.RaceId))
                    if (known.Add(unchecked((ushort)sm)))
                        learn.Add(unchecked((short)sm));
            if (learn.Count > 0)
            {
                await _services.Classes.LearnSpellsAsync(
                    (int)sc.CharId, learn);
                bundle.SpellLevelsDirty = true;
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Entry] EnsureClassSpells " +
                $"char={sc.CharId}: {ex.Message}");
        }
        var all = pf.Spells.ToList();
        foreach (var s in learn) all.Add((s, 1));
        return all;
    }

    /// <summary>États des 3 autels + points hebdo sur lignes
    /// préchargées — même décroissance paresseuse du plus que
    /// <see cref="LoadLotteryStateAsync"/>.</summary>
    private static void ApplyLotteryEntry(EntryState sc,
        EntryPrefetch pf)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (pf.LotNormal is { } n)
        {
            LotteryService.DecayPlus(n, now);
            sc.LotteryNormal = n;
        }
        if (pf.LotInGame is { } g)
        {
            LotteryService.DecayPlus(g, now);
            sc.LotteryInGame = g;
        }
        if (pf.LotDiamond is { } d)
        {
            LotteryService.DecayPlus(d, now);
            sc.LotteryDiamond = d;
        }
        if (pf.LotWeek != null) sc.LotteryWeek = pf.LotWeek;
    }

    /// <summary>Lifelong sur lignes préchargées — reset lazy du
    /// compteur daily (écritures hors-boucle) ; les 554 de reset sont
    /// collectés pour être émis au point natif de MigrationDone.</summary>
    private async Task ApplyLifelongEntryAsync(EntryState sc,
        EntryBundle bundle, EntryPrefetch pf)
    {
        try
        {
            foreach (var kv in pf.Lifelong) sc.Lifelong[kv.Key] = kv.Value;
            sc.LifelongGold = pf.LifelongGold;
            sc.LifelongDay = pf.LifelongDay;
            var today = DateTime.UtcNow.Day;
            if (sc.LifelongDay == today) return;
            sc.LifelongDay = today;
            await _services.LifelongRepo.SetResetDayAsync(
                (int)sc.CharId, today);
            if (sc.Lifelong.Count == 0) return;
            await _services.LifelongRepo.ResetDailyAsync((int)sc.CharId);
            foreach (var (group, e) in sc.Lifelong)
            {
                e.DailyMonsterKill = 0;
                e.PendingDaily = 0;
                bundle.LifelongResetGroups.Add(group);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Lifelong] init char={sc.CharId} : " +
                ex.Message);
        }
    }

    /// <summary>Activités recommandées sur lignes préchargées — mêmes
    /// règles que <see cref="InitRecommendActivitiesAsync"/> : maps
    /// persistées, pending reconstruit, reset hebdo + push 831
    /// mission (envoi hors-boucle — le lien mission a son propre
    /// verrou d'écriture).</summary>
    private async Task InitRecommendEntryAsync(NetSession session,
        EntryState sc, EntryPrefetch pf, CancellationToken token)
    {
        try
        {
            sc.RecommendTime = pf.RecommendTime;
            foreach (var (id, progress, finished, tracing)
                     in pf.RecommendRows)
            {
                sc.RecommendProgress[id] = progress;
                var def = _services.GameData.GetRecommendActivity(id);
                if (finished != 0)
                    sc.RecommendFinished[id] = 1;
                else if (def != null && progress >= (uint)def.Target)
                    sc.RecommendFinished[id] = 0;
                if (tracing != 0) sc.RecommendTracing.Add(id);
            }
            if (!RecommendResetNeeded(sc)) return;
            sc.RecommendProgress.Clear();
            sc.RecommendFinished.Clear();
            foreach (var def in _services.GameData.RecommendActivities)
            {
                if (def.Flag != 0) continue;
                if (sc.Level < def.MinLevel) continue;
                if (def.MaxLevel != 0 && sc.Level > def.MaxLevel) continue;
                sc.RecommendProgress[(ushort)def.Id] = 0;
            }
            sc.RecommendTime =
                (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_services.Mission is { } ml)
                await ml.SendRefreshRecommendActivitiesAsync(
                sc.CharId, sc.RecommendTime,
                sc.RecommendProgress
                    .Select(kv => (kv.Key, kv.Value,
                        (byte)(sc.RecommendFinished
                            .ContainsKey(kv.Key) ? 1 : 0)))
                    .ToList(),
                sc.RecommendTracing, token);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Recommend] init char={sc.CharId} : " +
                ex.Message);
        }
    }

    /// <summary>Purge hors-boucle des lignes player_enchants mortes
    /// (rest_time == 0) ou persistées par erreur pour un talent AA —
    /// les lignes restantes sont conservées dans le bundle pour le
    /// AddPlayerEnchantAsync de MigrationDone (ordre wire inchangé).</summary>
    private async Task PrepareEnchantRestoreAsync(int cid,
        EntryPrefetch pf, EntryBundle bundle)
    {
        try
        {
            var aaIds = new HashSet<int>();
            foreach (var id in pf.AaEffects.Keys)
                aaIds.Add(id & 0xffff);
            foreach (var r in pf.Enchants)
            {
                if (r.RestTime == 0)
                {
                    await _services.PlayerEnchants
                        .DeleteEnchantAsync(cid, r.EffectId);
                    continue;
                }
                if (aaIds.Contains(r.EffectId & 0xffff))
                {
                    await _services.PlayerEnchants
                        .DeleteEnchantAsync(cid, r.EffectId);
                    EdenLog.Write($"[Status] char={cid} purge " +
                        $"enchant de talent persisté " +
                        $"effect={r.EffectId}");
                    continue;
                }
                bundle.RestoreEnchants.Add(r);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Status] restore char={cid} : " +
                ex.Message);
        }
    }

    /// <summary>Normalisation des locs d'équipement sur la liste
    /// préchargée — mêmes swaps/moves que
    /// <see cref="NormalizeEquipmentSlotsAsync"/> (conteneur 0 seul :
    /// les pages costumes 1-5 ont leurs propres règles de loc) mais
    /// la liste reste cohérente avec la DB (slot mis à jour / entrée
    /// retirée) pour le corps 311 construit juste après.</summary>
    private async Task NormalizeEquipmentListAsync(int cid,
        List<ItemEntity> allEquip, CancellationToken token)
    {
        // Même périmètre que l'original : page équipée (container 0).
        var page0 = allEquip.Where(i => i.BagId == 0).ToList();
        var occupied = page0.Select(i => i.Slot).ToHashSet();
        foreach (var item in page0.OrderBy(i => i.Slot))
        {
            token.ThrowIfCancellationRequested();
            var def = _services.GameData.Items
                .FirstOrDefault(d => d.Id == item.ItemId);
            var allowed = NativeEquipmentSlots.AllowedLocations(
                def?.EquipLoc ?? 0);
            if (allowed.Contains(item.Slot)) continue;
            var target = allowed.FirstOrDefault(
                slot => !occupied.Contains(slot), -1);
            var moved = false;
            if (target >= 0)
            {
                moved = await _services.InventoryRepo
                    .SwapEquipmentAsync(cid, 0, item.Slot, 0, target);
                if (moved)
                {
                    occupied.Remove(item.Slot);
                    occupied.Add(target);
                    item.Slot = target;
                }
            }
            else if (await _services.InventoryRepo
                         .GetAnyAvailableSlotAsync(cid) is { } free)
            {
                moved = await _services.InventoryRepo
                    .MoveToInventoryAsync(cid, 0, item.Slot,
                        free.BagId, free.Slot);
                if (moved)
                {
                    occupied.Remove(item.Slot);
                    // Parti en inventaire : le corps 311 construit
                    // depuis allEquip ne doit plus le voir (l'ancien
                    // chemin re-requêtait la DB après normalisation).
                    allEquip.Remove(item);
                }
            }
            EdenLog.Write(moved
                ? $"[Equipment] char={cid} item={item.ItemId} " +
                  $"loc={item.Slot} -> " +
                  $"{(target >= 0 ? target : "inventory")}"
                : $"[Equipment] invalid legacy row retained char={cid} " +
                  $"item={item.ItemId} loc={item.Slot} equipType=" +
                  $"{def?.EquipLoc ?? 0}; no safe destination");
        }
    }

    /// <summary>CheckItemDueDate hors-boucle — DELETEs des items
    /// expirés (sacs + équipement pages 0-3 depuis les listes
    /// préchargées) ; les notices et le flag « équipement purgé »
    /// restent pour les émissions de MigrationDone.</summary>
    private async Task PrepareExpiredPurgeAsync(int cid,
        EntryPrefetch pf, List<ItemEntity> allEquip,
        EntryBundle bundle)
    {
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var item in pf.Bags)
        {
            if (item.DueDate <= 0 || item.DueDate > now) continue;
            await _services.InventoryRepo.RemoveItemAsync(cid,
                item.BagId, item.Slot);
            bundle.ExpiredNotices.Add((item, 0));
        }
        foreach (var item in allEquip.Where(i => i.BagId is >= 0 and < 4)
                     .ToList())
        {
            if (item.DueDate <= 0 || item.DueDate > now) continue;
            await _services.InventoryRepo.RemoveEquippedItemAsync(cid,
                item.BagId, item.Slot);
            allEquip.Remove(item);
            bundle.ExpiredNotices.Add((item, 1));
            bundle.EquipPurged = true;
        }
        bundle.EquippedAfterPurge =
            allEquip.Where(i => i.BagId == 0).ToList();
    }

    /// <summary>Contribution du StatPipeline résolue hors-boucle —
    /// chaque étape pioche dans le <see cref="StatFetch"/> seedé des
    /// lignes déjà lues (classlist, char, AA, enchants, perks,
    /// appellation) : aucune requête supplémentaire.</summary>
    private async Task ResolveEntryStatsAsync(EntryState sc,
        EntryBundle bundle, PlayerCharacterEntity chr, EntryPrefetch pf,
        List<ItemEntity> allEquip)
    {
        var cid = (int)sc.CharId;
        var fetch = new StatFetch(new Dictionary<string, object?>
        {
            ["classlist"] = pf.Classes,
            ["char"] = chr,
            ["aa"] = pf.AaEffects,
            ["enchants"] = pf.Enchants,
            ["limitless.perks"] = pf.LimitlessPerks,
            ["appellation"] = chr.Appellation,
            ["create_flag"] = chr.CreationFlag,
            ["equipment.all"] = allEquip.ToList(),
        });
        bundle.StageContribution =
            await _services.StatPipeline.ResolveAsync(cid,
                CancellationToken.None, fetch);
    }
}
