using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Limitless;
using Eden.Core.Game.Lottery;
using Eden.Core.Game.Stats;
using Eden.Core.Game.Work;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  Phase 2 — chargement d'entrée hors-boucle.
//
//  Avant : HandleTicketAsync exécutait RPC ticket + ~30-50 opérations SQL
//  séquentielles dans UNE commande de la boucle → ticks retardés en rafale.
//  Après : la commande NCID 0 pose st.EntryLoad (Task du pipeline créée
//  ici) et rend la main. Le pipeline fait tout le travail DB/RPC
//  hors-boucle dans un EntryState SCRATCH (jamais l'état live), puis
//  reposte UNE commande d'install qui copie les champs et joue les effets
//  liés à la boucle (timers works, spawns de summons). EntryLoad se
//  termine APRÈS l'install : la passerelle de TryHandleAsync garantit
//  qu'aucun handler ne voit un état à moitié chargé.
// -----------------------------------------------------------------------------
public sealed partial class ZoneEntryFlow
{
    /// <summary>Résultats des lectures parallèles du pipeline — une
    /// opération par connexion de pool, tous indépendants.</summary>
    private sealed class EntryPrefetch
    {
        public IReadOnlyList<ItemEntity> AllEquip = [];
        public IReadOnlyList<ItemEntity> Bags = [];
        public SortedDictionary<byte, List<(ulong Helmet, ulong Clothes,
            ulong Cloak)>> Closets = new();
        public IReadOnlyList<(long RowId, int ItemId)> Cards = [];
        public bool DailyEnabled;
        public DailyAwardRepository.DailyAwardRow? DailyRow;
        public List<PlayerLifeSkillEntity> LifeRows = [];
        public int DestinyVit;
        public DestinyRoadRow? DestinyRow;
        public List<Eden.Core.Game.Inventory.FastDressRecord>
            FastDressRows = [];
        public Dictionary<ushort, short> FortuneRows = new();
        public List<Eden.Core.Game.Classes.CustomClassSlot> CcSlots = [];
        public int CcIndex;
        public IReadOnlyList<ClassProgressEntity> Classes = [];
        public IReadOnlyList<(short Spell, short Level)> Spells = [];
        public Dictionary<short, short> AaEffects = new();
        public IReadOnlyList<PlayerEnchantEntity> Enchants = [];
        public Dictionary<int, (int SuccessNum, int Point)>
            Achievements = new();
        public Dictionary<short, LifelongCounters> Lifelong = new();
        public int LifelongGold;
        public int LifelongDay = -1;
        public PlayerLotteryState? LotNormal;
        public PlayerLotteryState? LotInGame;
        public PlayerLotteryState? LotDiamond;
        public LotteryWeekPoints? LotWeek;
        public int RecommendTime;
        public IReadOnlyList<(ushort Id, uint Progress, int Finished,
            int Tracing)> RecommendRows = [];
        public LimitlessService.Progress? LimitlessProgress;
        public IReadOnlyList<LimitlessService.Perk>? LimitlessPerks;
        // --- Données « premier paquet » — consommées par les
        // handlers post-entrée via le bundle ou l'état absorbé ;
        // null = lecture échouée/impossible → requête live à la
        // demande (comportement d'origine).
        /// <summary>Lignes `elf` — le natif charge la table au login
        /// (CExecutionerDBO remplit char+0x8ac avant l'entrée).</summary>
        public IReadOnlyList<ElfRepository.ElfRow>? ElfRows;
        /// <summary>Registre FMS décodé (final_mission_state_v2).</summary>
        public Dictionary<int, int>? MissionFms;
        /// <summary>Lignes personal_mission_states (index, state).</summary>
        public List<(int Index, int State)>? MissionPms;
        /// <summary>Les 10 slots de quêtes trackées (mission_trace).</summary>
        public int[]? MissionTrace;
        /// <summary>Les 26 compteurs battlefield (month+career) —
        /// l'INSERT-if-absent du repo tourne ici hors-boucle.</summary>
        public int[]? BattlefieldStats;
        /// <summary>Compteurs tutoriel new_player_guide (trigger /
        /// closed par guide id 1-20).</summary>
        public (SortedDictionary<ushort, ushort> Triggers,
            SortedDictionary<ushort, ushort> Closed)? Guide;
        /// <summary>Compteurs d'entrées journalières par nœud
        /// (player_node_times — char+0xcd0 natif).</summary>
        public Dictionary<int, int>? NodeTimes;
    }

    /// <summary>Données d'entrée produites hors-boucle — figées avant
    /// l'install. <see cref="Loaded"/> est l'état scratch peuplé par
    /// les chargeurs ; le reste alimente les patches 310/311 et les
    /// étapes différées de MigrationDone sans re-requêter la base.
    /// <c>internal</c> pour les tests d'install (Eden.Tests).</summary>
    internal sealed class EntryBundle
    {
        /// <summary>Scratch EntryState peuplé hors-boucle — copié en
        /// bloc sur l'état live par l'install.</summary>
        public EntryState? Loaded;
        /// <summary>Ligne player_characters — vitals persistés de
        /// fallback si ComputeVitals défaille.</summary>
        public PlayerCharacterEntity? Character;
        /// <summary>Progression de la classe active (player_classlist)
        /// — patch 310 (@85 exp, @105 CP) + overlay AA (attr 0x31).</summary>
        public ClassProgressEntity? ClassRow;
        /// <summary>Corps complet du 311 EnterNodeEquipAndSpell —
        /// construit APRÈS normalisation des slots et octroi des
        /// sorts, AVANT purge des expirés (ordre du flux d'origine).</summary>
        public byte[]? EquipSpellBody;
        /// <summary>Items expirés purgés en DB — (item, flag 0 sac /
        /// 1 équipement) ; notices 316+275 émises au point natif de
        /// MigrationDone.</summary>
        public List<(ItemEntity Item, short Flag)> ExpiredNotices = new();
        /// <summary>Un équipement expiré a été purgé — MigrationDone
        /// réémet les blocs de stats (RefreshEQsAttr natif).</summary>
        public bool EquipPurged;
        /// <summary>Lignes player_enchants à rejouer (rest_time != 0,
        /// hors talents AA) — filtrées et purgées hors-boucle ;
        /// AddPlayerEnchantAsync reste à sa place de MigrationDone.</summary>
        public List<PlayerEnchantEntity> RestoreEnchants = new();
        /// <summary>work_num des ordres encore en cours — timers armés
        /// à l'install (Schedule + WorkCts sont liés à la boucle).</summary>
        public List<int> WorkTimers = new();
        /// <summary>Summons matérialisés pendant l'absence — (work_num,
        /// mobId, qté) ; spawn registry + emits joués à l'install.</summary>
        public List<(int WorkNum, int MobId, int Count)> PendingSummons
            = new();
        /// <summary>Groupes lifelong dont le compteur daily a été
        /// remis à zéro — les 554 sont émis au point natif de
        /// MigrationDone.</summary>
        public List<short> LifelongResetGroups = new();
        /// <summary>Sorts octroyés à l'entrée — invalidation du cache
        /// SpellLevels à l'install (après pose de CharId).</summary>
        public bool SpellLevelsDirty;
        /// <summary>État Limitless lu hors-boucle pour le 2032.</summary>
        public (int Level, long Exp, int PerkCount, long NextExp)
            Limitless;
        /// <summary>Contribution du StatPipeline résolue hors-boucle
        /// sur les lignes préchargées — alimente 296/492/493.</summary>
        public StatContribution? StageContribution;
        /// <summary>Entités équipées page 0 APRÈS normalisation et
        /// purge — base des blocs de stats et du refresh « équipement
        /// expiré ».</summary>
        public List<ItemEntity> EquippedAfterPurge = new();
        /// <summary>Émissions différées de MigrationDone jouées —
        /// anti-rejeu : un EntryState survit aux portails et le même
        /// bundle resterait posé ; sans ce drapeau chaque re-migration
        /// réémettrait notices d'expiration, restores d'enchants et
        /// resets 554 (doublons wire + doubles statuts).</summary>
        public bool Emitted;
        /// <summary>Lignes `elf` préchargées — servies par
        /// <c>EnsureElfsLoadedAsync</c> au premier paquet elf de la
        /// session (110/109/108/…) au lieu de re-requêter la table ;
        /// le snapshot vaut jusqu'au premier usage (toutes les
        /// mutations elf passent par cette même porte).</summary>
        public IReadOnlyList<ElfRepository.ElfRow>? ElfRows;
        /// <summary>Registre FMS préchargé — servi au premier 348
        /// (NCID 50) tant que <see cref="Emitted"/> est faux ; la
        /// mission tient l'état après la déclaration du perso.</summary>
        public Dictionary<int, int>? MissionFms;
        /// <summary>Lignes personal_mission_states préchargées —
        /// même fenêtre de validité que <see cref="MissionFms"/>.</summary>
        public List<(int Index, int State)>? MissionPms;
        /// <summary>Slots mission_trace préchargés — même fenêtre de
        /// validité que <see cref="MissionFms"/>.</summary>
        public int[]? MissionTrace;
        /// <summary>Lignes player_classlist préchargées — servies au
        /// premier 489 (NCID 166) post-entrée ; <c>EntryDirty</c> les
        /// invalide (changement/octroi de classe possible en session).</summary>
        public IReadOnlyList<ClassProgressEntity>? ClassRows;
        /// <summary>Map advanced_ability préchargée (effectId →
        /// points) — servie par le 471 et le replay des mods
        /// AttrSpell* du premier 489 post-entrée ; les mutations AA
        /// (SetAAPoint, reset Memory Tome) restent en requête live.</summary>
        public Dictionary<short, short>? AaEffects;
    }

    /// <summary>Démarre le pipeline hors-boucle — appelé par la
    /// commande NCID 0 sur la boucle ; la Task est posée dans
    /// <see cref="EntryState.EntryLoad"/> sans être awaitée ici.</summary>
    private Task StartEntryLoad(NetSession session, EntryState st,
        byte[] payload)
        => Task.Run(() => RunEntryPipelineAsync(session, st, payload),
            CancellationToken.None);

    /// <summary>Corps du pipeline : 473 → validation ticket →
    /// chargement déterministe → install postée sur la boucle et
    /// attendue (fin de EntryLoad = install exécutée). Aucune
    /// mutation de l'EntryState live : tout le chargement écrit dans
    /// le scratch <see cref="EntryBundle.Loaded"/>.</summary>
    private async Task RunEntryPipelineAsync(NetSession session,
        EntryState st, byte[] payload)
    {
        var bundle = new EntryBundle();
        try
        {
            using (EdenMetrics.BeginDbOperation("zone_entry_ticket"))
            {
                // NCID 473 ClientHello — émis en premier, avant la
                // validation ticket (ordre natif prouvé, capture
                // zsniff_cs_entry_success).
                await session.SendAsync(new CNC_CZ_ClientHello
                {
                    Value = BitConverter.Int32BitsToSingle(0x43207852),
                }, CancellationToken.None);

                // Ticket natif = 22 octets — payload court = refus
                // silencieux (comportement de l'ancien handler).
                if (payload.Length < 22) return;
                var ticket = payload[..22];
                var charId = BitConverter.ToUInt32(ticket, 0);
                // Validation T_ : ServerUseTicket → ClientUseTicket.
                // EnsureConnectedAsync dans RoundtripAsync gère la
                // reconnexion paresseuse (pas de garde-fou Connected).
                var ok = await _ticket.UseTicketAsync(charId, ticket,
                    CancellationToken.None);
                if (!ok)
                {
                    EdenLog.Write($"[Zone-Entry] Ticket rejeté " +
                        $"char=#{charId} (T_ ko)");
                    return;
                }
                var sc = new EntryState { CharId = charId };
                bundle.Loaded = sc;
                await LoadEntryDataAsync(session, sc, bundle, charId);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Entry] pipeline load " +
                $"session={session.SessionId} : {ex}");
            return;
        }
        // Install atomique — la SEULE mutation de l'EntryState live ;
        // attendue ici (hors-boucle) pour que la passerelle libère les
        // paquets suivants seulement quand l'état est complet.
        try
        {
            await RunOnLoopAsync(ct => InstallEntryAsync(session, st,
                bundle, ct));
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Entry] install " +
                $"session={session.SessionId} : {ex}");
        }
    }

    /// <summary>Lecture parallèle du pipeline — une requête par
    /// connexion de pool. Le scratch reçoit identité/position AVANT
    /// le batch (les tâches de compte ont besoin d'AccountId).</summary>
    private async Task LoadEntryDataAsync(NetSession session,
        EntryState sc, EntryBundle bundle, uint charId)
    {
        var cid = (int)charId;
        sc.Session = session;
        var chr = await _characters.GetCharacterByIdAsync(cid);
        bundle.Character = chr;
        if (chr == null) return; // install posera identité+session seules
        ApplyEntryIdentity(sc, chr);
        var pf = await PrefetchEntryAsync(sc, chr);
        bundle.ClassRow = pf.Classes
            .FirstOrDefault(c => c.ClassId == sc.ClassId);
        await WriteEntryPhaseAsync(session, sc, bundle, chr, pf);
    }

    /// <summary>Le batch de lectures indépendantes — équipement tous
    /// conteneurs (couvre pages 0-5 du 311, vitals, arme,
    /// normalisation, purge), sacs, classes, sorts, enchants,
    /// works/destiny/lifelong/lottery/recommend/limitless…</summary>
    private async Task<EntryPrefetch> PrefetchEntryAsync(
        EntryState sc, PlayerCharacterEntity chr)
    {
        var cid = (int)sc.CharId;
        var equipTask = _services.InventoryRepo
            .GetAllEquipmentEntitiesAsync(cid);
        var bagsTask = _services.InventoryRepo
            .GetItemsByCharacterIdAsync(cid);
        var closetsTask = _services.Classes.GetClosetsAsync(cid);
        var collTask = _services.InventoryRepo.GetCollectionItemsAsync(cid);
        var licenseTask = _services.Licenses.LoadAsync(cid,
            CancellationToken.None);
        var dailyEnabledTask = _services.DailyAwardRepo.IsEnabledAsync();
        var dailyTask = _services.DailyAwardRepo.GetAsync(cid);
        var lifeTask = _services.LifeSkills.GetByPlayerAsync(cid);
        var worksEnsureTask = _services.WorksRepo.EnsureRowsAsync(cid);
        var destinyVitTask = _services.DestinyRepo.GetVitalityAsync(cid);
        var destinyTask = _services.DestinyRepo.GetAsync(cid);
        var fastDressTask = _services.FastDressRepo.LoadAsync(cid,
            CancellationToken.None);
        var fortuneTask = _services.FortuneRepo
            .GetCountersAsync(chr.AccountId);
        var ccSlotsTask = _services.CustomClassRepo.LoadSlotsAsync(cid);
        var ccIndexTask = _services.CustomClassRepo.GetActiveIndexAsync(cid);
        var classesTask = _services.Classes
            .GetClassesByCharacterIdAsync(cid);
        var spellsTask = _services.Classes.GetPlayerSpellsAsync(cid);
        var aaTask = _services.AdvancedAbilities.GetEffectsAsync(cid);
        var enchantsTask = _services.PlayerEnchants.GetEnchantsAsync(cid);
        var achTask = _services.AchievementRepo.LoadAsync(cid);
        var lifelongTask = _services.LifelongRepo.LoadAsync(cid);
        var lifelongGoldTask = _services.LifelongRepo.GetGoldAsync(cid);
        var lifelongDayTask = _services.LifelongRepo.GetResetDayAsync(cid);
        var lotNormalTask = _services.LotteryRepo.GetStateAsync(cid,
            AltarKind.Normal);
        var lotInTask = _services.LotteryRepo.GetStateAsync(cid,
            AltarKind.InGame);
        var lotDiaTask = _services.LotteryRepo.GetStateAsync(cid,
            AltarKind.Diamond);
        var lotWeekTask = _services.LotteryRepo
            .GetWeekPointsAsync(chr.AccountId);
        var recTimeTask = _characters
            .GetRecommendActivitiesTimeAsync(cid);
        var recRowsTask = _characters.GetRecommendActivitiesAsync(cid);
        var llProgTask = PrefetchOrNull("limitless_prog", cid,
            _services.Limitless?.GetProgressAsync(cid));
        var llPerksTask = PrefetchOrNull("limitless_perks", cid,
            _services.Limitless?.GetActivePerksAsync(cid));
        // Lectures « premier paquet » — données connues dès l'entrée
        // (charId seul) servies ensuite par les handlers depuis le
        // bundle / l'état absorbé au lieu de re-requêter sur la
        // boucle. Enveloppées : une table en échec dégrade en null
        // (requête live à la demande) au lieu d'avorter l'entrée.
        var elfTask = PrefetchOrNull("elf", cid,
            _services.ElfRepo?.GetElfsAsync(cid));
        var fmsTask = PrefetchOrNull("mission_fms", cid,
            _services.MissionRepo.GetFMSAsync(cid,
                CancellationToken.None));
        var pmsTask = PrefetchOrNull("mission_pms", cid,
            _services.MissionRepo.GetPersonalMissionStatesAsync(cid,
                CancellationToken.None));
        var traceTask = PrefetchOrNull("mission_trace", cid,
            _services.MissionRepo.GetMissionTraceAsync(cid,
                CancellationToken.None));
        var bfTask = PrefetchOrNull("battlefield", cid,
            _services.BattlefieldRepo.LoadStatsAsync(cid, sc.CharName,
                (short)sc.Level, (short)sc.ClassId));
        var guideTask = PrefetchOrNull("new_player_guide", cid,
            _services.GuideRepo.LoadAsync(cid));
        var nodeTimesTask = PrefetchOrNull("node_times", cid,
            _services.EvaluateRepo.GetNodeTimesAsync(cid));
        var tasks = new List<Task>
        {
            equipTask, bagsTask, closetsTask, collTask, licenseTask,
            dailyEnabledTask, dailyTask, lifeTask, worksEnsureTask,
            destinyVitTask, destinyTask, fastDressTask, fortuneTask,
            ccSlotsTask, ccIndexTask, classesTask, spellsTask, aaTask,
            enchantsTask, achTask, lifelongTask, lifelongGoldTask,
            lifelongDayTask, lotNormalTask, lotInTask, lotDiaTask,
            lotWeekTask, recTimeTask, recRowsTask,
            elfTask, fmsTask, pmsTask, traceTask, bfTask, guideTask,
            nodeTimesTask,
        };
        if (llProgTask != null) tasks.Add(llProgTask);
        if (llPerksTask != null) tasks.Add(llPerksTask);
        await Task.WhenAll(tasks);
        return new EntryPrefetch
        {
            AllEquip = equipTask.Result,
            Bags = bagsTask.Result,
            Closets = closetsTask.Result,
            Cards = collTask.Result,
            DailyEnabled = dailyEnabledTask.Result,
            DailyRow = dailyTask.Result,
            LifeRows = lifeTask.Result,
            DestinyVit = destinyVitTask.Result,
            DestinyRow = destinyTask.Result,
            FastDressRows = fastDressTask.Result,
            FortuneRows = fortuneTask.Result,
            CcSlots = ccSlotsTask.Result,
            CcIndex = ccIndexTask.Result,
            Classes = classesTask.Result,
            Spells = spellsTask.Result,
            AaEffects = aaTask.Result,
            Enchants = enchantsTask.Result,
            Achievements = achTask.Result,
            Lifelong = lifelongTask.Result,
            LifelongGold = lifelongGoldTask.Result,
            LifelongDay = lifelongDayTask.Result,
            LotNormal = lotNormalTask.Result,
            LotInGame = lotInTask.Result,
            LotDiamond = lotDiaTask.Result,
            LotWeek = lotWeekTask.Result,
            RecommendTime = recTimeTask.Result,
            RecommendRows = recRowsTask.Result,
            LimitlessProgress = llProgTask?.Result,
            LimitlessPerks = llPerksTask?.Result,
            ElfRows = elfTask.Result,
            MissionFms = fmsTask.Result,
            MissionPms = pmsTask.Result,
            MissionTrace = traceTask.Result,
            BattlefieldStats = bfTask.Result,
            Guide = guideTask.Result,
            NodeTimes = nodeTimesTask.Result,
        };
    }

    /// <summary>Enveloppe d'une lecture optionnelle du prefetch — une
    /// table absente/cassée dégrade en <c>default</c> (le handler
    /// retombera sur la requête live à la demande, comportement
    /// d'origine) au lieu d'avorter tout le pipeline.</summary>
    private static async Task<T?> PrefetchOrNull<T>(string label,
        int charId, Task<T>? task)
    {
        if (task == null) return default;
        try { return await task; }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Entry] prefetch {label} " +
                $"char={charId} : {ex.Message}");
            return default;
        }
    }

    /// <summary>Identité + position du perso sur le scratch — reprend
    /// la logique du ticket d'origine : redirection d'instance au
    /// login, fallback born/event-area sur position absente ou
    /// corrompue, champs player_characters.</summary>
    private void ApplyEntryIdentity(EntryState sc,
        PlayerCharacterEntity chr)
    {
        sc.CharName = chr.GivenName ?? "";
        sc.NodeId = (short)chr.NodeId;
        sc.PosX = (float)chr.PositionX;
        sc.PosZ = (float)chr.PositionY; // (x,y) = plan au sol
        var savedType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == sc.NodeId)?.Type ?? 0;
        if (InstanceNodeTypes.Contains(savedType))
            ApplyInstanceLoginRedirect(sc, chr, savedType);
        if (!float.IsFinite(sc.PosX) || !float.IsFinite(sc.PosZ)
            || Math.Abs(sc.PosX) > 20_000f || Math.Abs(sc.PosZ) > 20_000f
            || (sc.PosX == 0 && sc.PosZ == 0))
        {
            var scene = _services.Scenes.GetScene($"S{sc.NodeId:D3}");
            var born = scene?.EventAreas
                .FirstOrDefault(a => a.Type == "BORN");
            if (born != null)
            {
                sc.PosX = born.X; sc.PosZ = born.Y;
                sc.Facing = born.Facing;
            }
            else if (scene?.Positions.Count > 0)
            {
                var sp = scene.Positions[0];
                sc.PosX = sp.X; sc.PosZ = sp.Y;
            }
            else
            {
                var spawn = EntryReplayData.Map[57]
                    .Select(h => Convert.FromHexString(h))
                    .FirstOrDefault(b =>
                        BitConverter.ToUInt16(b, 0) == 313);
                if (spawn is { Length: >= 17 })
                {
                    sc.PosX = BitConverter.ToSingle(spawn, 5);
                    sc.PosZ = BitConverter.ToSingle(spawn, 9);
                    sc.Facing = BitConverter.ToSingle(spawn, 13);
                }
            }
        }
        sc.RaceId = chr.RaceId;
        sc.GenderId = chr.GenderId;
        sc.ClassId = chr.ClassId;
        sc.Level = Math.Max(chr.Level, 1);
        sc.ClassLevel = Math.Max(chr.ClassLevel, 1);
        sc.ActiveTitle = chr.Appellation;
        sc.Experience = chr.Experience;
        sc.CreateFlag = chr.CreationFlag;
        sc.Gold = chr.Gold;
        sc.ReviveAreaId = chr.ReviveAreaId;
        sc.LastNormalAreaId = chr.LastNormalAreaId;
        sc.VisitedFamilyInstanceId = chr.VisitFamilyId;
        sc.CrossZoneNode = chr.CrossZoneNode;
        sc.CrossZoneX = chr.CrossZoneX;
        sc.CrossZoneY = chr.CrossZoneY;
        sc.Privilege = chr.Privilege;
        sc.ClosetIndex = (byte)(chr.Style & 0x3f);
        sc.HelmetColor = (ulong)chr.HelmetColor;
        sc.ClothesColor = (ulong)chr.ClothesColor;
        sc.CloakColor = (ulong)chr.CloakColor;
        sc.AccountId = chr.AccountId;
        sc.LifeSkill = chr.LifeSkill;
    }

    /// <summary>Phase séquentielle hors-boucle — même ordre logique
    /// que l'ancien chemin : vitals sur équipement brut, seeds et
    /// consommations works, puis octroi des sorts, normalisation des
    /// slots, corps 311, purge expirés et contribution stats.</summary>
    private async Task WriteEntryPhaseAsync(NetSession session,
        EntryState sc, EntryBundle bundle, PlayerCharacterEntity chr,
        EntryPrefetch pf)
    {
        var cid = (int)sc.CharId;
        var allEquip = pf.AllEquip.ToList();
        var equipped0 = allEquip.Where(i => i.BagId == 0).ToList();
        // Node sans scène → repli zone de spawn ; groupe d'instance.
        // Résolu EN TÊTE : les broadcasts hors-boucle (enchants de
        // works 299/300) ciblent node+groupe — l'ancien flux les
        // connaissait déjà à ce point.
        if (_services.Scenes.GetScene($"S{sc.NodeId:D3}") == null)
        {
            EdenLog.Write($"[Zone-Entry] node {sc.NodeId} sans " +
                "scène — fallback node 2");
            sc.NodeId = 2;
            sc.PosX = 0; sc.PosZ = 0;
        }
        sc.InstanceGroup = ResolveInstanceGroup(sc, sc.NodeId);
        // Vitals — agrégat équipement porté BRUT (avant
        // normalisation/purge, comme l'ancien ticket) + perk
        // Limitless max_hp calculé sur la liste préchargée.
        ApplyEntryVitals(sc, chr, equipped0,
            PerkValue(pf.LimitlessPerks, "max_hp"));
        if (pf.LimitlessProgress is { } lp)
            bundle.Limitless = (lp.Level, lp.Exp,
                pf.LimitlessPerks?.Count ?? 0, lp.NextExp);
        sc.Closets = pf.Closets;
        foreach (var (_, itemId) in pf.Cards)
        {
            sc.CollectedItemIds.Add(itemId);
            if (_services.GameData.Collections.TryGetValue(itemId,
                    out var def))
            {
                sc.CollectionPoints += def.Points;
                AddCollectionGroupPoints(sc, (short)def.Group,
                    def.Points);
                AddCollectionGroupPoints(sc,
                    (short)(def.Special ? 1001 : 1000), def.Points);
            }
        }
        // Calendrier + métiers (seeds batch InsertMany — 1 requête).
        await ApplyDailyAwardEntryAsync(sc, pf.DailyEnabled,
            pf.DailyRow!);
        await ApplyLifeSkillsAsync(sc, pf.LifeRows);
        // Ordres de travail : consommations + tirages + saves
        // hors-boucle ; armements et summons différés à l'install.
        await LoadWorksEntryAsync(session, sc, bundle,
            CancellationToken.None);
        ApplyDestiny(sc, pf.DestinyVit, pf.DestinyRow);
        ApplyFastDress(sc, pf.FastDressRows);
        foreach (var (bagId, cnt) in pf.FortuneRows)
            sc.FortuneCounters[bagId] = cnt;
        sc.CustomClassSlots.AddRange(pf.CcSlots);
        sc.ActiveCCIndex = pf.CcIndex;
        if (sc.ActiveCCIndex < sc.CustomClassSlots.Count)
            sc.Vis4ad = sc.Vis4ad & ~4 |
                (sc.CustomClassSlots[sc.ActiveCCIndex].WeaponType
                 & 1) << 2;
        // Arme + snapshot combat sur l'équipement BRUT (la
        // normalisation n'arrivait qu'au 311 dans l'ancien flux).
        await ResolveWeaponAsync(sc, equipped0
            .Select(i => (i.Slot, i.ItemId)).ToList());
        if (chr.AppearanceData is { Length: > 0 } ad)
            Array.Copy(ad, sc.Appearance, Math.Min(32, ad.Length));
        NormalizeAppearance(sc);
        // Sorts octroyés — batch unnest au lieu de N LearnSpellAsync ;
        // la liste finale du 311 est reconstruite en mémoire.
        var finalSpells = await EnsureEntrySpellsAsync(sc, bundle, pf);
        ApplyLotteryEntry(sc, pf);
        // Succès — la map préchargée évite la requête d'Ensure*.
        sc.Achievements = pf.Achievements;
        sc.AchievementTotalPoint =
            pf.Achievements.Values.Sum(v => (long)v.Point);
        // Données « premier paquet » — posées sur le bundle pour que
        // les handlers post-entrée les servent au lieu de requêter
        // sur la boucle (fallback live inchangé quand le champ est
        // null ou périmé — cf. Emitted/EntryDirty).
        bundle.ElfRows = pf.ElfRows;
        if (pf.MissionFms != null && pf.MissionPms != null
            && pf.MissionTrace != null)
        {
            bundle.MissionFms = pf.MissionFms;
            bundle.MissionPms = pf.MissionPms;
            // Une trace ne doit pointer que sur une quête EN COURS (FMS 3/4) :
            // les traces périmées (quête terminée/abandonnée, jamais purgée)
            // réapparaissaient dans le tracker.
            bundle.MissionTrace = pf.MissionTrace
                .Select(m => m != 0 &&
                    pf.MissionFms.GetValueOrDefault(m) is 3 or 4 ? m : 0)
                .ToArray();
        }
        bundle.ClassRows = pf.Classes;
        bundle.AaEffects = pf.AaEffects;
        // États lazy déjà lus — les gates ??=/Loaded des handlers
        // (162 stats battlefield, 218 tutoriel, CheckNodeTimes)
        // sautent alors leur requête d'origine.
        sc.BattlefieldStats = pf.BattlefieldStats;
        sc.NodeTimes = pf.NodeTimes;
        if (pf.Guide is { } guide)
        {
            foreach (var kv in guide.Triggers)
                sc.NewPlayerGuide[kv.Key] =
                    (kv.Value, guide.Closed.GetValueOrDefault(kv.Key));
            foreach (var kv in guide.Closed)
                if (!sc.NewPlayerGuide.ContainsKey(kv.Key))
                    sc.NewPlayerGuide[kv.Key] = (0, kv.Value);
            sc.NewPlayerGuideLoaded = true;
        }
        await ApplyLifelongEntryAsync(sc, bundle, pf);
        await InitRecommendEntryAsync(session, sc, pf,
            CancellationToken.None);
        await PrepareEnchantRestoreAsync(cid, pf, bundle);
        // Normalisation des slots équipés (écritures) puis corps 311
        // — construit AVANT la purge des expirés, comme l'ancien flux.
        await NormalizeEquipmentListAsync(cid, allEquip,
            CancellationToken.None);
        bundle.EquipSpellBody = NativeItemCodec
            .BuildEnterNodeEquipAndSpell(allEquip, finalSpells);
        await PrepareExpiredPurgeAsync(cid, pf, allEquip, bundle);
        await ResolveEntryStatsAsync(sc, bundle, chr, pf, allEquip);
    }

    /// <summary>Valeur agrégée d'un perk sur une liste préchargée —
    /// même règle que <c>LimitlessService.GetPerkValueAsync</c>
    /// (mult = produit, flat/percent = somme).</summary>
    private static double PerkValue(
        IReadOnlyList<LimitlessService.Perk>? perks, string code)
    {
        if (perks == null) return 0;
        var sel = perks.Where(x => x.Code == code).ToList();
        if (sel.Count == 0) return 0;
        return sel.Any(x => x.ValueKind == "mult")
            ? sel.Where(x => x.ValueKind == "mult")
                 .Aggregate(1.0, (a, x) => a * x.Value)
            : sel.Sum(x => x.Value);
    }
}
