using Eden.Core.Game.Work;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partiel pipeline — install atomique sur la boucle :
/// copie en bloc des champs chargés (la SEULE mutation de
/// l'EntryState live) puis effets liés à la boucle (BindCharacter,
/// timers works, spawns de summons).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Commande d'install — s'exécute sur la boucle. Si la
    /// session a été remplacée entre-temps (déco + reconnexion),
    /// l'état cible n'est plus celui du pipeline : l'install est
    /// abandonnée (le chargement mort est simplement jeté).</summary>
    private async Task InstallEntryAsync(NetSession session,
        EntryState st, EntryBundle bundle, CancellationToken token)
    {
        if (!_states.TryGetValue(session.SessionId, out var cur)
            || !ReferenceEquals(cur, st))
            return;
        if (bundle.Loaded is { } sc)
        {
            AbsorbEntryFields(st, sc);
            // BindCharacter (et pas la seule propriété) : enregistre
            // la session dans _byCharacter — requis pour router les
            // réponses MissionServer. Fait ici, atomiquement avec la
            // pose de CharId (avant : tout paquet routé par charId
            // loupait la session).
            _services.Sessions.BindCharacter(session, (int)st.CharId);
        }
        st.Entry = bundle;
        // Timers works armés sur la boucle — Schedule + WorkCts sont
        // liés à l'état live ; WorkNum armé ⇔ toujours Running.
        foreach (var num in bundle.WorkTimers)
            if (st.Works.ElementAtOrDefault(num) is { } w && w.Running)
                ArmWorkTimer(session, st, w);
        // Summons matérialisés pendant l'absence — BornMonster natif :
        // spawn registry + aggro sur le fil de la boucle, puis 300 +
        // 435 (même corps que l'ancien MaterializeSummonAsync).
        foreach (var pending in bundle.PendingSummons)
            await InstallSummonAsync(session, st, pending, token);
        // Sorts octroyés hors-boucle : le cache lazy SpellLevels de
        // l'état live est invalidé. On NE passe PAS par le callback
        // SpellLevelsDirty — il marque aussi EntryDirty (pensé pour
        // les mutations mi-session), or le bundle vient d'être
        // construit APRÈS l'upsert : EquipSpellBody est déjà frais.
        if (bundle.SpellLevelsDirty)
            st.SpellLevels = null;
        EdenLog.Write($"[Zone-Entry] Ticket OK char=#{st.CharId} " +
            $"'{st.CharName}' node={st.NodeId}");
    }

    /// <summary>Spawn différé d'un ordre d'invocation — rejoue le
    /// BornMonster natif préparé hors-boucle : `count` monstres
    /// hostiles autour du perso (décalage ±2u, aggro immédiat),
    /// puis le 300 de l'enchant works et le 435 de clôture.</summary>
    private async Task InstallSummonAsync(NetSession session,
        EntryState st, (int WorkNum, int MobId, int Count) pending,
        CancellationToken token)
    {
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        for (var i = 0; i < pending.Count; i++)
        {
            var mx = st.PosX +
                (float)(Random.Shared.NextDouble() * 4.0 - 2.0);
            var mz = st.PosZ +
                (float)(Random.Shared.NextDouble() * 4.0 - 2.0);
            var mob = _services.Live.SpawnHostileMob(st.NodeId,
                st.InstanceGroup, pending.MobId, (int)st.CharId,
                mx, mz, _services.Live.GroundHeightAt(scene, mx, mz),
                st.Facing);
            EdenLog.Write($"[Zone] Summon work char={st.CharId} " +
                $"num={pending.WorkNum} → mob {pending.MobId} " +
                $"handle={mob.Handle:X8}");
        }
        await RemoveWorkEnchantAsync(session, st, pending.WorkNum,
            token);
        // 435 ClientStrengthenItemResult [s16 0xe][s16 1][s16 0×4] —
        // corps natif du NotifyClient d'EndSummon.
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
        { Field1 = (short)WorkTypes.Summon, Field2 = 1 }, token);
    }

    /// <summary>Copie en bloc des champs chargés du scratch vers
    /// l'état live — exhaustive sur les champs écrits par les
    /// chargeurs d'entrée (l'ancien ticket + les états MigrationDone
    /// préchargés). Les collections readonly sont copiées par
    /// contenu ; les objets complets (Works, FastDress, lottery…)
    /// sont transférés par référence — le scratch est mort ensuite.</summary>
    internal static void AbsorbEntryFields(EntryState st, EntryState sc)
    {
        // --- Identité / session / position ---
        st.CharId = sc.CharId;
        st.CharName = sc.CharName;
        st.Session = sc.Session;
        st.NodeId = sc.NodeId;
        st.PosX = sc.PosX;
        st.PosZ = sc.PosZ;
        st.Facing = sc.Facing;
        st.RaceId = sc.RaceId;
        st.GenderId = sc.GenderId;
        st.ClassId = sc.ClassId;
        st.Level = sc.Level;
        st.ClassLevel = sc.ClassLevel;
        st.ActiveTitle = sc.ActiveTitle;
        st.Experience = sc.Experience;
        st.CreateFlag = sc.CreateFlag;
        st.Gold = sc.Gold;
        st.Privilege = sc.Privilege;
        st.AccountId = sc.AccountId;
        st.InstanceGroup = sc.InstanceGroup;
        st.VisitedFamilyInstanceId = sc.VisitedFamilyInstanceId;
        st.CrossZoneNode = sc.CrossZoneNode;
        st.CrossZoneX = sc.CrossZoneX;
        st.CrossZoneY = sc.CrossZoneY;
        st.ReviveAreaId = sc.ReviveAreaId;
        st.LastNormalAreaId = sc.LastNormalAreaId;
        st.Appearance = sc.Appearance;
        // --- Vitals ---
        st.PlayerHp = sc.PlayerHp;
        st.PlayerMp = sc.PlayerMp;
        st.PlayerMaxHp = sc.PlayerMaxHp;
        st.PlayerMaxMp = sc.PlayerMaxMp;
        // --- Arme / combat résolus ---
        st.WeaponItemId = sc.WeaponItemId;
        st.WeaponKind = sc.WeaponKind;
        st.WeaponOffKind = sc.WeaponOffKind;
        st.WeaponKfIds = sc.WeaponKfIds;
        st.AttackRange = sc.AttackRange;
        st.ProcEnchantIds = sc.ProcEnchantIds;
        st.Combat = sc.Combat;
        st.WeaponResolved = sc.WeaponResolved;
        // --- Séquence d'actions consommée par les emits hors-boucle —
        // le prochain NextAction() live continue où le scratch
        // s'est arrêté (ordre natif des séries 299/300).
        st.ActCounter = sc.ActCounter;
        // --- Garde-robe ---
        st.ClosetIndex = sc.ClosetIndex;
        st.HelmetColor = sc.HelmetColor;
        st.ClothesColor = sc.ClothesColor;
        st.CloakColor = sc.CloakColor;
        st.Closets = sc.Closets;
        // --- Collection ---
        foreach (var id in sc.CollectedItemIds)
            st.CollectedItemIds.Add(id);
        st.CollectionPoints = sc.CollectionPoints;
        foreach (var kv in sc.CollectionGroupPoints)
            st.CollectionGroupPoints[kv.Key] = kv.Value;
        // --- Calendrier de connexion ---
        st.DailyDayIndex = sc.DailyDayIndex;
        st.DailyLoginTime = sc.DailyLoginTime;
        st.DailyCooldown = sc.DailyCooldown;
        st.DailyAwardDays = sc.DailyAwardDays;
        st.DailyAwardRecords = sc.DailyAwardRecords;
        st.DailyLoginStatus = sc.DailyLoginStatus;
        st.DailySessionDelta = sc.DailySessionDelta;
        st.DailySessionFlag = sc.DailySessionFlag;
        st.DailyLoaded = sc.DailyLoaded;
        st.DailyEnabled = sc.DailyEnabled;
        // --- Métiers ---
        st.LifeSkill = sc.LifeSkill;
        foreach (var kv in sc.LifeSkillLearned)
            st.LifeSkillLearned[kv.Key] = kv.Value;
        // --- Ordres de travail ---
        st.Works = sc.Works;
        st.WorkOpStarted = sc.WorkOpStarted;
        foreach (var kv in sc.WorkDeferredLocs)
            st.WorkDeferredLocs[kv.Key] = kv.Value;
        // --- Destin ---
        st.Destiny = sc.Destiny;
        st.DestinyVit = sc.DestinyVit;
        st.DestinyVitRegen = sc.DestinyVitRegen;
        // --- Divers chargés ---
        st.FastDress = sc.FastDress;
        foreach (var kv in sc.FortuneCounters)
            st.FortuneCounters[kv.Key] = kv.Value;
        st.CustomClassSlots.Clear();
        st.CustomClassSlots.AddRange(sc.CustomClassSlots);
        st.ActiveCCIndex = sc.ActiveCCIndex;
        st.Vis4ad = sc.Vis4ad;
        st.LotteryNormal = sc.LotteryNormal;
        st.LotteryInGame = sc.LotteryInGame;
        st.LotteryDiamond = sc.LotteryDiamond;
        st.LotteryWeek = sc.LotteryWeek;
        // --- Lifelong ---
        foreach (var kv in sc.Lifelong) st.Lifelong[kv.Key] = kv.Value;
        st.LifelongGold = sc.LifelongGold;
        st.LifelongDay = sc.LifelongDay;
        // --- Recommandées ---
        st.RecommendTime = sc.RecommendTime;
        foreach (var kv in sc.RecommendProgress)
            st.RecommendProgress[kv.Key] = kv.Value;
        foreach (var kv in sc.RecommendFinished)
            st.RecommendFinished[kv.Key] = kv.Value;
        st.RecommendTracing.Clear();
        st.RecommendTracing.AddRange(sc.RecommendTracing);
        // --- Succès ---
        st.Achievements = sc.Achievements;
        st.AchievementTotalPoint = sc.AchievementTotalPoint;
        // --- États lazy préchargés — les objets complets sont
        // transférés par référence (le scratch est mort ensuite,
        // même convention que Works/FastDress) ; le guide tuto est
        // copié par contenu (dictionnaire readonly).
        st.BattlefieldStats = sc.BattlefieldStats;
        st.NodeTimes = sc.NodeTimes;
        foreach (var kv in sc.NewPlayerGuide)
            st.NewPlayerGuide[kv.Key] = kv.Value;
        st.NewPlayerGuideLoaded = sc.NewPlayerGuideLoaded;
    }

    // ============================================================
    //  Émissions différées — rejouées à MigrationDone au même point
    //  du flux que les requêtes/purges d'origine (ordre wire natif).
    // ============================================================

    /// <summary>2032 Limitless depuis les valeurs lues hors-boucle
    /// (+ 2033 des paliers, en mémoire comme avant — même position
    /// dans le flux MigrationDone). Zéros si le service est absent,
    /// comme l'ancien EmitAsync.</summary>
    private async Task EmitLimitlessEntryAsync(NetSession session,
        EntryState st, EntryBundle entry, CancellationToken token)
    {
        await LimitlessEmitter.EmitAsync(session, (int)st.CharId,
            entry.Limitless.Level, entry.Limitless.Exp,
            entry.Limitless.PerkCount, entry.Limitless.NextExp, token);
        await LimitlessEmitter.EmitPassAsync(session,
            _services.Limitless, (int)st.CharId, token);
    }

    /// <summary>Notifications d'expiration rejouées au point natif —
    /// les DELETEs ont été faits hors-boucle ; 316 (nom) + 275 par
    /// item, puis refresh stats si un équipement a sauté.</summary>
    private async Task EmitExpiredNoticesAsync(NetSession session,
        EntryState st, EntryBundle entry, CancellationToken token)
    {
        foreach (var (item, flag) in entry.ExpiredNotices)
            await NotifyExpiredItemAsync(session, st, item, flag,
                token);
        // Un équipement expiré change le profil — RefreshEQsAttr +
        // NotifyRefreshAttr natifs : rejoué avec l'équipement
        // post-purge préchargé (pas de requête).
        if (entry.EquipPurged)
            await ProfileStatEmitter.SendAsync(session, _services,
                (int)st.CharId, st.RaceId, st.ClassId, st.Level,
                entry.EquippedAfterPurge, entry.StageContribution,
                token);
    }

    /// <summary>Rejoue les enchants persistés filtrés hors-boucle —
    /// même AddPlayerEnchantAsync que l'ancien restore (Statuses,
    /// SessionEnchants, 299 — seed counter/stack, hidden pour
    /// rest_time -9).</summary>
    private async Task ApplyRestoredEnchantsAsync(NetSession session,
        EntryState st, EntryBundle entry, CancellationToken token)
    {
        foreach (var r in entry.RestoreEnchants)
        {
            if (!await FilterRideRestoreAsync((int)st.CharId, r.EffectId))
                continue;
            await AddPlayerEnchantAsync(session, st, r.CasterId,
                r.EffectId, r.RestTime, r.Level, token,
                refreshStats: false, hidden: r.RestTime == -9,
                seedCounter: r.Counter, seedStack: r.StackCount);
        }
        if (entry.RestoreEnchants.Count > 0)
            EdenLog.Write($"[Status] char={st.CharId} " +
                $"{st.Statuses.Count} enchant(s) restauré(s)");
    }

    /// <summary>554 [groupe][3][0] des resets lazy lifelong —
    /// émis au même point que l'ancien CheckLifelongDayAsync.</summary>
    private static async Task EmitLifelongResetsAsync(
        NetSession session, EntryBundle entry, CancellationToken token)
    {
        foreach (var group in entry.LifelongResetGroups)
            await session.SendAsync(
                new CNC_CZ_ClientUpdateLifelongPoint
                {
                    Field1 = group,  // s16 : groupe waken
                    Field2 = 3,      // s16 : type 3 = daily_monster_kill
                    Field3 = 0,      // i32 : compteur remis à zéro
                }, token);
    }

    /// <summary>296/492/493 depuis les données d'entrée — équipement
    /// post-purge + contribution du StatPipeline résolue hors-boucle ;
    /// zéro requête sur la boucle.</summary>
    private async Task SendStatBlocksEntryAsync(NetSession session,
        EntryState st, EntryBundle entry, CancellationToken token)
    {
        await ProfileStatEmitter.SendAsync(session, _services,
            (int)st.CharId, st.RaceId, st.ClassId, st.Level,
            entry.EquippedAfterPurge, entry.StageContribution, token);
    }
}
