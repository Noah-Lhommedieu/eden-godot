using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Chargeur central des donnees de jeu — charge toutes les tables decodees
/// au demarrage du serveur. Equivalent du GameData::ReadINIFile natif.
/// </summary>
public sealed class GameDataService
{
    public string DataDir { get; }

    // Catalogues de donnees
    public List<ClassStatsParser.ClassDefinition> Classes { get; private set; } = new();
    public List<ItemParser.ItemDef> Items { get; private set; } = new();
    public List<MonsterParser.MonsterDef> Monsters { get; private set; } = new();
    public List<SpellParser.SpellDef> Spells { get; private set; } = new();
    public List<MissionParser.MissionDef> Missions { get; private set; } = new();
    public List<EnchantParser.EnchantDef> Enchants { get; private set; } = new();
    /// <summary>Lignes S_EquipSet — sets d'équipement ET combos de
    /// certificats (`CEquipSetQuery::Deserialize` @0x85b2xxx).</summary>
    public List<EquipSetParser.EquipSetDef> EquipSets { get; private set; } = new();
    /// <summary>Pool natif `EquipSetPool` — set trié d'ids →
    /// (item récompense, enchant). Miroir de `InitEquipSetID`
    /// (@0x859abb0) : utilisé par les combos de certificats (183)
    /// et les bonus de sets d'équipement.</summary>
    public EquipSetParser.EquipSetPool? EquipSetPool { get; private set; }
    public List<CraftRecipeParser.ElfCombineRecipe> ElfRecipes { get; private set; } = new();
    public List<CraftRecipeParser.FamilyCombineRecipe> FamilyRecipes { get; private set; } = new();
    public List<CraftRecipeParser.ItemCombo> ItemCombos { get; private set; } = new();
    /// <summary>Recettes métiers (S_LifeSkill.ini — CLifeSkillQuery
    /// natif) : analyse joaillerie (group 1) et production (2-5).
    /// Index auxiliaire id→recette et itemId→recette d'analyse.</summary>
    public List<LifeSkillParser.Recipe> LifeSkillRecipes { get; private set; } = new();
    public Dictionary<int, LifeSkillParser.Recipe> LifeSkillById { get; private set; } = new();
    /// <summary>itemId → recette dont c'est l'item source (col2) —
        /// équivalent du lien ItemDef+0x178 posé par InitLifeSkillItem
        /// natif (AnalysisCheck : le slot analysé mène à la recette) —
        /// groupes 1 uniquement : pour les groupes 2-5, col2 est la
        /// SORTIE, pas une source d'analyse.</summary>
    public Dictionary<int, LifeSkillParser.Recipe> LifeSkillByItemId { get; private set; } = new();
    public List<ElfParser.ElfDef> Elfs { get; private set; } = new();
    public List<BattlefieldParser.BattlefieldDef> Battlefields { get; private set; } = new();
    public List<GameDataParsers.ClassDropTable> ClassDrops { get; private set; } = new();
    public List<DropAndSpeedParsers.DropTable> DropTables { get; private set; } = new();
    public List<DropAndSpeedParsers.SpeedDef> MonsterSpeeds { get; private set; } = new();
    public List<DropAndSpeedParsers.SpeedDef> RideSpeeds { get; private set; } = new();
    public List<GameDataParsers.TalentTree> Talents { get; private set; } = new();
    /// <summary>Bonus cumulatifs de classes montées (Class Ability
    /// Bonus) — RefreshJPAward natif : rang floor(classLv×0.2) →
    /// enchant permanent du palier, sommé sur TOUTES les classes du
    /// personnage.</summary>
    public List<ShopAndMiscParsers.ClassRankBuffDef> ClassRankBuffs { get; private set; } = new();
    /// <summary>Progression Mana Infusion par niveau — S_EquipTrain.ini
    /// (`CEquipTrainQuery` natif : seuil d'EXP par train_level ET les
    /// 15 coefficients indexés par le required level de l'item).</summary>
    public Dictionary<int, EquipTrainParser.EquipTrainRow> EquipTrain
        { get; private set; } = new();
    public List<GameDataParsers.TitleDef> Titles { get; private set; } = new();
    public List<GameDataParsers.AchievementDef> Achievements { get; private set; } = new();
    public List<GameDataParsers.NodeDef> Nodes { get; private set; } = new();
    public List<ShopAndMiscParsers.StoreDef> Stores { get; private set; } = new();
    public List<WorldParsers.NpcDef> Npcs { get; private set; } = new();
    public List<WorldParsers.TransportRoute> TransportRoutes { get; private set; } = new();
    public List<ProgressionParsers.LevelRow> Levels { get; private set; } = new();
    /// <summary>Activités recommandées hebdo (S_RecommendActivity.ini) —
    /// éligibilité niveau, cible et récompenses par id.</summary>
    public List<ProgressionParsers.RecommendActivityDef> RecommendActivities { get; private set; } = new();
    /// <summary>Lookup par id (QueryRecommendActivityData natif).</summary>
    public ProgressionParsers.RecommendActivityDef? GetRecommendActivity(int id) =>
        RecommendActivities.FirstOrDefault(a => a.Id == id);
    /// <summary>Routes du chemin d'éveil (S_DestinyRoad.ini) —
    /// CDestinyRoadData natif : 39 cases de groupes d'events.</summary>
    public List<DestinyDataParser.DestinyRoadDef> DestinyRoads { get; private set; } = new();
    /// <summary>Events de case (S_DestinyEvent.ini) — type/valeur/param2.</summary>
    public List<DestinyDataParser.DestinyEventDef> DestinyEvents { get; private set; } = new();
    /// <summary>`DestinyRoadLevelMap` natif : tier (level/10) → ids.</summary>
    public SortedDictionary<int, List<int>> DestinyRoadLevelMap { get; private set; } = new();
    /// <summary>`DestinyEventGroupMap` natif : groupe → ids d'events.</summary>
    public Dictionary<int, List<int>> DestinyEventGroupMap { get; private set; } = new();
    /// <summary>Lookup route par id (GameData+0x194 natif).</summary>
    public DestinyDataParser.DestinyRoadDef? GetDestinyRoad(int id) =>
        DestinyRoads.FirstOrDefault(r => r.Id == id);
    /// <summary>Lookup event par id (GameData+0x198 natif).</summary>
    public DestinyDataParser.DestinyEventDef? GetDestinyEvent(int id) =>
        DestinyEvents.FirstOrDefault(e => e.Id == id);
    /// <summary>Noms localisés des sorts (T_Spell.ini : `id|name|desc`).
    /// Utilisés par le 316 `[u16 0x05f4][u16 len][str]` que le natif
    /// envoie après chaque 286 ClientGetSpell (capture 17:22:35.926 —
    /// "Resonating Blow", "Quick Means", "Ambush").</summary>
    public Dictionary<int, string> SpellNames { get; private set; } = new();
    /// <summary>Noms anglais des items (T_Item.ini : `id|name|desc`).
    /// S_Item.ini porte les noms Big5 — les commandes GM en anglais
    /// (« Adventurer Alpaca ») matchent ici.</summary>
    public Dictionary<int, string> ItemNames { get; private set; } = new();
    /// <summary>Descriptions anglaises des enchants (T_Enchant.ini) —
    /// métadonnées d'affichage des effets de certificats de classe.</summary>
    public Dictionary<int, string> EnchantDescriptions
        { get; private set; } = new();
    /// <summary>EquipType.ini (CSV) : kind d'item → loc d'équipement
    /// (col 3). Preuve : 1..6 = armure tête→cape, 7/9/11 = 1H→loc 7,
    /// 8/10/12/16 = 2H→loc 9, 13/14 = distance→loc 10, 17 = bouclier→8,
    /// 18 = accessoire→11, 19/20 = munition→13.</summary>
    public Dictionary<int, int> EquipLocs { get; private set; } = new();
    /// <summary>Ordres de collecte par work_id — S_FamilyCollect.ini
    /// (CCollectData natif, types 12 mining / 13 planting / 14 summon).
    /// </summary>
    public Dictionary<int, WorkOrderParser.WorkCollectDef> WorkCollectById
        { get; private set; } = new();
    /// <summary>Ordres de combinaison par work_id — S_FamilyCombine.ini
    /// (CCombineData natif, types 15 alchemy / 16 casting /
    /// 17 enchanting).</summary>
    public Dictionary<int, WorkOrderParser.WorkCombineDef> WorkCombineById
        { get; private set; } = new();
    /// <summary>kfIds d'attaque par préfixe de modèle (Motion.ini) —
    /// pool pioché par CMonsterKfm::GetAttackID pour le field5 du 272.
    /// ex. "m025" -> [1021, 1022, 1023] (attack01/02/03).</summary>
    public Dictionary<string, int[]> AttackKfIds { get; private set; } = new();
    /// <summary>Timings des motions `kfm -> kfId -> (hitDs, endDs)`
    /// (Motion.ini, phases `hit`/`end` en décisecondes — conversion
    /// native `round(sec*100)/10` de ParseMotionIni @0x080e1cd0).
    /// Source du délai swing→impact (CKfm::GetMotionTime
    /// @0x080e0b00) pour les casts et la mêlée.</summary>
    public Dictionary<string, Dictionary<int, MotionParser.MotionTiming>>
        MotionTimings { get; private set; } = new();
    /// <summary>S_Parameter.ini : les coefficients de formules natifs
    /// (AllFormulaParas du serveur natif — 678 floats indexés :
    /// StrToAttack=5, IntToMagicAttack=7, ConToDefense=8, AgiToDodge=10,
    /// AgiToCrit=11…). Puisés par CCharFormula pour dégâts/défense/
    /// vitesses — aucun de ces nombres ne doit être en dur dans le
    /// code.</summary>
    public ParameterParser.ParameterSet Parameters { get; private set; }
        = new ParameterParser.ParameterSet(new(), new());
    /// <summary>Table de textes résolus serveur (T_TextIndex puis
    /// S_TextIndex en secours) — `GameData::GetTextIndex` natif :
    /// utilisée pour les chaînes émises telles quelles au client
    /// (bulletins lottery → ScreenMessage 474).</summary>
    public Dictionary<int, string> Texts { get; private set; } = new();
    /// <summary>Template de bâtiment de ville de famille
    /// (<c>GameData::CBuildingData</c> natif, chargé par
    /// <c>CBuildingQuery::Deserialize</c> mission @0x82aca20).</summary>
    /// <param name="Type">col3 → +0xc : 4-9 = bâtiments de production
    /// (ferme/mine/autel/alchimie/forge/piscine), 2 = maison,
    /// 10 = commerce.</param>
    /// <param name="RepairCostPerPoint">col7 → +0x1c : coût de
    /// réparation par point de durabilité manquant
    /// (<c>TC_FamilyBuildingRepair</c> mission @0x81ef3d0).</param>
    /// <param name="MaxDurability">col8 → +0x20 : durabilité max
    /// (borne de <c>CFamily::UpdateBuildingDurability</c>).</param>
    /// <param name="Code">col1 → +4 : code du modèle de maison
    /// (ex. <c>H00101</c>) — clé de <see cref="HouseLayouts"/>
    /// (4PHouse.ini) pour les offsets de slots NPC.</param>
    /// <param name="Level">col4 → niveau de famille requis du
    /// template.</param>
    /// <param name="PrevTid">col5 → template du niveau précédent
    /// (chaîne d'upgrade ; +0x30 natif = tid suivant).</param>
    /// <param name="NpcItemTid">col6 → +0x18 : item NPC attaché au
    /// bâtiment (housekeeper) — sert dans
    /// <c>CheckFamilyItemDelete</c>/<c>CheckNpcSizeLimit</c> et à
    /// résoudre le NPC via la col35 de S_Item.</param>
    public sealed record BuildingDef(short Type, int RepairCostPerPoint,
        int MaxDurability, string Code = "", int Level = 0,
        int PrevTid = 0, int NpcItemTid = 0);

    /// <summary>Templates de bâtiments de ville de famille
    /// (`S_Buliding.ini`, faute de frappe native) — `tid → def`.</summary>
    public Dictionary<int, BuildingDef> Buildings { get; private set; }
        = new();

    /// <summary>Offsets de slots NPC par modèle de maison —
    /// <c>4PHouse.ini</c> `[TableHouse]` : `DB<n>=code,modèle,modèle2,
    /// 8 triplets "dx dy dfacing"`. Équivaut à la map native
    /// <c>CZoneServer::Ins+0x2e8</c> (nom de modèle → CHouseData)
    /// lue par <c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0.</summary>
    public Dictionary<string, IReadOnlyList<(float Dx, float Dy,
        float Df)>> HouseLayouts { get; private set; } = new();

    /// <summary>Vue `tid → type` de <see cref="Buildings"/> — la
    /// recherche de ville 174 traduit le type en bit via
    /// <c>CFamilyItem::GetBuildingValue</c> (zone @0x8464870 :
    /// 4→1, 5→2, 6→4, 7→8, 8→0x10, 9→0x20).</summary>
    public IReadOnlyDictionary<int, short> BuildingTypes
        => _buildingTypes ??= Buildings.ToDictionary(
            kv => kv.Key, kv => kv.Value.Type);
    private Dictionary<int, short>? _buildingTypes;

    /// <summary>Lien « niveau suivant » de la chaîne S_Buliding —
    /// inverse de <c>PrevTid</c> (col5). Équivaut au champ +0x30 du
    /// CBuildingData natif remonté par le walk de
    /// <c>FamilyBuildingPutItemFromInventroy</c> @0x808a770 : pour les
    /// types 1/2/10 la pose sélectionne l'échelon de la chaîne dont le
    /// niveau égale le niveau de la famille. Chaînes linéaires en
    /// EDENFR → un seul next par tid.</summary>
    public int NextBuildingTid(int tid)
    {
        _buildingNext ??= BuildBuildingNext();
        return _buildingNext.TryGetValue(tid, out var n) ? n : 0;
    }
    private Dictionary<int, int>? _buildingNext;
    private Dictionary<int, int> BuildBuildingNext()
    {
        var next = new Dictionary<int, int>();
        foreach (var (tid, def) in Buildings)
            if (def.PrevTid != 0) next[def.PrevTid] = tid;
        return next;
    }

    /// <summary>Définition d'un territoire issue de
    /// <c>S_Territory.ini</c> (<c>CTerritoryData</c> natif) —
    /// <c>Type</c> = col4 → +0x10 : 1-15 = guerres de territoire (→
    /// type de schedule type-1 via <c>CGlobalInfo::
    /// GetScheduleTypeByTerritoryType</c> @0x8071ec0),
    /// 0x12d-0x13b (301-315) = <c>CFamilyBattle</c> (éliminations de
    /// familles, records AC023), 601-603 = instances versus.
    /// <c>AttachedNodeId</c> = col3 : node rattaché (map monde 2-11
    /// pour les guerres, 407 pour les versus 27-29).</summary>
    public sealed record TerritoryDef(int Id, string Name, int Type,
        int AttachedNodeId);

    /// <summary>Territoires `id → def` — <c>S_Territory.ini</c> est un
    /// format à records multi-lignes `id|nom|group|col4|type|desc…` ;
    /// seule la ligne d'ouverture (commençant par `chiffre|`) porte
    /// id/nom/type.</summary>
    public Dictionary<int, TerritoryDef> Territories { get; private set; }
        = new();

    /// <summary>Entrée d'horaire <c>S_Schedule.ini</c> :
    /// `row|schedType|entryId|beginHHMM|endHHMM|` —
    /// <paramref name="DayId"/> = jour de la semaine (1-7) ou index
    /// d'entrée ; heures en HHMM (ex. 2130 = 21h30).</summary>
    public sealed record ScheduleEntry(int DayId, int BeginHhmm,
        int EndHhmm);

    /// <summary>Calendrier des événements `schedType → entrées` —
    /// <c>S_Schedule.ini</c> chargé par <c>CMissionDBO::
    /// InitScheduleEvent</c> @0x818f5a0 dans <c>CGlobalInfo::
    /// AllSchedules</c> (map&lt;short, CSchedule*&gt;). Types : 1-10
    /// guerres de territoire, 200-203 ?, 301-315 rounds family battle,
    /// 501 Greedy Island, 502 Battle Royale, 601-603 instances
    /// versus.</summary>
    public Dictionary<int, List<ScheduleEntry>> Schedules
        { get; private set; } = new();

    /// <summary>Cartes collectables indexées par item_id
    /// (`S_Collection.ini`).</summary>
    public Dictionary<int, CollectionParser.CollectionDef> Collections
        { get; private set; } = new();

    /// <summary>Récompenses de points de collection
    /// (`S_CollectionPoint.ini`).</summary>
    public List<CollectionParser.CollectionPointAward>
        CollectionPointAwards { get; private set; } = new();

    /// <summary>Événements « pay » (échanges d'items) indexés par id
    /// (`S_PaySystem.ini`) — chaque def porte son
    /// <see cref="PayEventCmd"/> précompilé comme le
    /// <c>AllPayEventCmd</c> natif rempli au boot par
    /// <c>LoadAllPayEventCmd</c>.</summary>
    public Dictionary<int, (PaySystemParser.PayEventDef Def,
        PayEventCmd Cmd)> PayEvents { get; private set; } = new();

    /// <summary>Récompenses du calendrier de connexion par index de
    /// jour (`S_DailyAward.ini`).</summary>
    public Dictionary<int, DailyAwardParser.DailyAwardDef> DailyAwards
        { get; private set; } = new();

    /// <summary>Defs d'évaluation « gladiator trial » indexées par
    /// nodeTid (`S_RewardScore.ini` — <c>CEvaluateList</c> natif,
    /// nœuds type 18 = 655-674 ; NCID 255 + paquets 607/608).</summary>
    public Dictionary<int, RewardScoreParser.EvaluateDef> Evaluates
        { get; private set; } = new();

    public GameDataService(string dataDir) => DataDir = dataDir;

    /// <summary>Charge toutes les tables (tolere les fichiers absents).</summary>
    public void LoadAll()
    {
        Load("S_Class.ini", p => Classes = ClassStatsParser.ParseClassDefinitions(p));
        Load("S_Item.ini", p => Items = ItemParser.Parse(p));
        // S_ItemMall.ini : même schéma 78 colonnes que S_Item — les
        // items "spéciaux" (Blessed Crystal 49004, cadeaux de niveau,
        // boutique) n'existent QUE là : sans cette fusion tout usage
        // tombait dans la branche "hors S_Item — ack sans effet".
        Load("S_ItemMall.ini", p =>
            Items.AddRange(ItemParser.Parse(p)
                .Where(m => Items.All(i => i.Id != m.Id))));
        // S_ItemExtra.ini : ajouts propres au portage vivant dans le
        // db client overlay (corpus/fr-full — l'oracle S_* est en
        // lecture seule). Même schéma S_Item, prioritaire — ex.
        // monture EDENFR 1290 « Kitsune de l'Aube » absente de S_Item,
        // ou 48813 « Renard Blanc du blizzard » dont l'id n'est qu'un
        // placeholder « 測試用編號 » dans S_ItemMall US.
        var itemExtra = ReforgedDataLocator.FindFile("S_ItemExtra.ini",
            DataDir, AppContext.BaseDirectory);
        if (itemExtra != null)
            MergeItemExtra(Items, ItemParser.Parse(itemExtra));
        Load("S_Monster.ini", p => Monsters = MonsterParser.Parse(p));
        Load("S_Spell.ini", p => Spells = SpellParser.Parse(p));
        Load("S_Mission.ini", p => Missions = MissionParser.Parse(p));
        Load("S_Enchant.ini", p => Enchants = EnchantParser.Parse(p));
        // S_ItemMallEnchant.ini : les enchants référencés par les items
        // mall (monture 2134 → enchant de ride 55850, col23) n'existent
        // QUE là — sans cette fusion GetRideInfo retournait null et
        // l'item tombait en consommable (mangé au clic).
        Load("S_ItemMallEnchant.ini", p =>
            Enchants.AddRange(EnchantParser.Parse(p)
                .Where(e => Enchants.All(x => x.Id != e.Id))));
        if (Directory.Exists(DataDir))
            MergeFrMounts(ReforgedDataLocator.FindDbDir(DataDir,
                AppContext.BaseDirectory));
        Load("S_EquipSet.ini", p =>
        {
            EquipSets = EquipSetParser.Parse(p);
            // Le pool a besoin de S_Item (col23 = enchant de
            // l'item récompense) — construit APRÈS Items si
            // disponible, sinon au premier accès ci-dessous.
            if (Items.Count > 0)
                EquipSetPool = EquipSetParser.BuildPool(EquipSets,
                    Items.ToDictionary(i => i.Id,
                        i => i.UseEnchantId));
        });
        Load("S_ElfCombine.ini", p => ElfRecipes = CraftRecipeParser.ParseElfCombine(p));
        Load("S_FamilyCombine.ini", p => FamilyRecipes = CraftRecipeParser.ParseFamilyCombine(p));
        Load("S_ItemCombo.ini", p => ItemCombos = CraftRecipeParser.ParseItemCombo(p));
        // S_ItemComboExtra.ini : combos EDENFR absents de l'oracle US
        // (même convention que S_ItemExtra — priorité aux lignes extra).
        var comboExtra = ReforgedDataLocator.FindFile("S_ItemComboExtra.ini",
            DataDir, AppContext.BaseDirectory);
        if (comboExtra != null)
            foreach (var m in CraftRecipeParser.ParseItemCombo(comboExtra))
            {
                ItemCombos.RemoveAll(i => i.Id == m.Id);
                ItemCombos.Add(m);
            }
        Load("S_Elf.ini", p => Elfs = ElfParser.Parse(p));
        Load("S_Battlefield.ini", p => Battlefields = BattlefieldParser.Parse(p));
        Load("S_ClassDropItem.ini", p => ClassDrops = GameDataParsers.ParseClassDropItem(p));
        Load("S_DropItem.ini", p => DropTables = DropAndSpeedParsers.ParseDropItem(p));
        Load("S_MonsterStep.ini", p => MonsterSpeeds = DropAndSpeedParsers.ParseSpeedTable(p));
        Load("S_RideStep.ini", p => RideSpeeds = DropAndSpeedParsers.ParseSpeedTable(p));
        Load("S_Talent.ini", p => Talents = GameDataParsers.ParseTalents(p));
        Load("S_ClassRankBuff.ini", p => ClassRankBuffs = ShopAndMiscParsers.ParseClassRankBuffs(p));
        Load("S_EquipTrain.ini", p => EquipTrain =
            EquipTrainParser.Parse(p).ToDictionary(r => r.Level));
        Load("S_Title.ini", p => Titles = GameDataParsers.ParseTitles(p));
        Load("S_Achievement.ini", p => Achievements = GameDataParsers.ParseAchievements(p));
        Load("S_Node.ini", p =>
        {
            Nodes = GameDataParsers.ParseNodes(p);
            EnsureFamilyInstanceNodes(Nodes);
        });
        Load("S_Store.ini", p => Stores = ShopAndMiscParsers.ParseStores(p));
        Load("S_Level.ini", p => Levels = ProgressionParsers.ParseLevels(p));
        // S_RecommendActivity.ini : activités hebdomadaires recommandées
        // (22 colonnes — mapping Deserialize @0x8620880, voir le parser).
        Load("S_RecommendActivity.ini", p => RecommendActivities =
            ProgressionParsers.ParseRecommendActivities(p));
        Load("S_Npc.ini", p => Npcs = WorldParsers.ParseNpcs(p));
        Load("S_Transport.ini", p => TransportRoutes = WorldParsers.ParseTransport(p));
        Load("T_Spell.ini", p => SpellNames = ParseSpellNames(p));
        // T_Item.ini : `id|nom anglais|desc` — même structure que
        // T_Spell (les lignes de continuation commencent par '$').
        Load("T_Item.ini", p => ItemNames = ParseSpellNames(p));
        // T_ItemMall.ini : noms anglais des items boutique (montures
        // Nine Tail 43774/43801…) absents de T_Item — Items inclut
        // déjà S_ItemMall, sans cette fusion find_item ne les
        // trouvait pas. Les entrées « Testing ID » de T_Item sont
        // remplacées par le vrai nom mall quand il existe.
        Load("T_ItemMall.ini", p =>
        {
            foreach (var kv in ParseSpellNames(p))
                if (!ItemNames.TryGetValue(kv.Key, out var cur) ||
                    cur.Length == 0 ||
                    cur.StartsWith("Testing ID",
                        StringComparison.OrdinalIgnoreCase))
                    ItemNames[kv.Key] = kv.Value;
        });
        Load("T_Enchant.ini", p =>
            EnchantDescriptions = ParseEnchantDescriptions(p));
        Load("Motion.ini", p => AttackKfIds = MotionParser.ParseAttackIds(p));
        Load("Motion.ini", p => MotionTimings = MotionParser.ParseTimings(p));
        Load("EquipType.ini", p => EquipLocs = ParseEquipLocs(p));
        Load("S_Parameter.ini", p => Parameters = ParameterParser.Parse(p));
        // Chemin d'éveil : routes par tier (InitDestinyRoadMap natif
        // @0x080daf00) + events regroupés pour le tirage aléatoire.
        Load("S_DestinyRoad.ini", p =>
        {
            DestinyRoads = DestinyDataParser.ParseRoads(p);
            DestinyRoadLevelMap = DestinyDataParser.BuildRoadLevelMap(DestinyRoads);
        });
        Load("S_DestinyEvent.ini", p =>
        {
            DestinyEvents = DestinyDataParser.ParseEvents(p);
            DestinyEventGroupMap = DestinyDataParser.BuildEventGroupMap(DestinyEvents);
        });
        // T_TextIndex.ini : table de textes localisée du serveur US
        // (annonces lottery 0xa2d/0x1c3e/0x1f54, textes cristaux,
        // marquee). Format `id|flag|texte` — utilisée par
        // GameData::GetTextIndex natif pour résoudre les chaînes
        // émises telles quelles (ScreenMessage 474).
        Load("T_TextIndex.ini", p => Texts = ParseTextIndex(p));
        // S_TextIndex.ini : la table serveur Big5 en secours — les
        // ids absents de T_ (p. ex. noms de cristaux 2616-2619).
        Load("S_TextIndex.ini", p =>
        {
            foreach (var kv in ParseTextIndex(p))
                Texts.TryAdd(kv.Key, kv.Value);
        });
        // S_FamilyCollect/S_FamilyCombine : tables des ordres de
        // travail CWorkManager (services NPC 12-17). Le combine partage
        // le fichier avec FamilyRecipes (recettes cube) — les parsers
        // lisent des sous-ensembles de colonnes différents.
        // La map CCollectData native est cléée par l'ID DE L'ITEM
        // CATALYSEUR (S_FamilyCollect col2), pas par le work_id col0 :
        // CCollectQuery::Deserialize @0x085cd020 lit col0 sans la
        // stocker puis fait `*param_2 = token2` (col2 → +0x08/+0x00/
        // clé). Le client envoie l'item id dans le 170 (ex. 36226 =
        // carte de mine, recette col0=193) — une clé col0 renvoyait
        // « Item does not exist » (textId 4).
        Load("S_FamilyCollect.ini", p => WorkCollectById =
            WorkOrderParser.ParseCollect(p)
                .GroupBy(d => d.CatalystItemId)
                .ToDictionary(g => g.Key, g => g.First()));
        Load("S_FamilyCombine.ini", p => WorkCombineById =
            WorkOrderParser.ParseCombine(p)
                .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First()));
        // S_Buliding.ini : templates de bâtiments du hall de famille
        // (CBuildingQuery natif) — type pour le masque de la recherche
        // de ville 174 (GetBuildingValue zone @0x8464870) +
        // coût/durabilité pour la réparation 180.
        Load("S_Buliding.ini", p => Buildings = ParseBuildings(p));
        // 4PHouse.ini : offsets de slots NPC par modèle de maison —
        // map native Ins+0x2e8 (SFamily::GetNpcPositionByNpcLoc).
        Load("4PHouse.ini", p => HouseLayouts = ParseHouseLayouts(p));
        // S_Territory.ini : définitions des territoires (guerres +
        // éliminations de familles) — `CTerritoryQuery::Init` natif
        // mission @0x8326e10.
        Load("S_Territory.ini", p => Territories = ParseTerritories(p));
        // S_Schedule.ini : horaires d'événements `row|type|jour|début|
        // fin` — `CMissionDBO::InitScheduleEvent` @0x818f5a0 ; les types
        // 501/502/6xx alimentent la map du paquet territoire 515.
        Load("S_Schedule.ini", p => Schedules = ParseSchedules(p));
        // S_Collection.ini : cartes de monstres collectables, indexées
        // par item_id (`CCollectionQuery::Deserialize` natif zone
        // @0x0860xxxx — id, flag, locate, points, groupe).
        Load("S_Collection.ini", p => Collections =
            CollectionParser.ParseCollection(p));
        // S_CollectionPoint.ini : enchants accordés par seuil de
        // points de collection (`RefreshCollectionPointAward` natif
        // — EffectToLife permanent -2 quand les conds groupes sont
        // atteintes).
        Load("S_CollectionPoint.ini", p => CollectionPointAwards =
            CollectionParser.ParseCollectionPoint(p));
        // S_DailyAward.ini : récompenses du calendrier de connexion
        // (CDailyAwardData — item, quantité, secondes en ligne
        // requises par index de jour, NCID 258).
        Load("S_DailyAward.ini", p => DailyAwards =
            DailyAwardParser.Parse(p));
        // S_RewardScore.ini : trials d'évaluation indexés par nœud
        // (DSL KillMonster/TeamDieNum/PassTime/TotalScore → grades
        // et pool de loterie). NCID 255.
        Load("S_RewardScore.ini", p => Evaluates =
            RewardScoreParser.Parse(p));
        // S_PaySystem.ini : événements d'échange « pay » — defs +
        // cmd compilé (ConvertCmd natif : cond col4 → vecteurs
        // conditions, award col17 → vecteurs récompenses). NCID 253.
        Load("S_PaySystem.ini", p =>
        {
            var defs = PaySystemParser.Parse(p);
            PayEvents = defs.ToDictionary(kv => kv.Key,
                kv => (kv.Value, PayEventCmdParser.Build(
                    kv.Value.CondCmd, kv.Value.AwardCmd)));
        });
        Load("S_LifeSkill.ini", p =>
        {
            LifeSkillRecipes = LifeSkillParser.Parse(p);
            LifeSkillById = LifeSkillRecipes.ToDictionary(r => r.Id);
            LifeSkillByItemId = LifeSkillRecipes
                .Where(r => r.Group == 1)
                .GroupBy(r => r.ItemId)
                .ToDictionary(g => g.Key, g => g.First());
        });
    }

    /// <summary>Nodes 501/502 — nœuds d'instance de hall de famille.
    /// S_Node.ini ne les contient PAS (1-37 seulement) : le natif les
    /// connaît via les données de nœud du serveur d'instance —
    /// <c>CFamily::InstanceCreate</c> mission @0x81bf660 choisit
    /// 0x1f5 (type 1, forêt → S501) ou 0x1f6 (type 0, montagne → S502)
    /// comme <c>family.house_open</c>. Le gate
    /// <c>CCharacter::IsInFamilyInstance</c> (zone @0x84098d0) exige
    /// <b>type == 4</b>. Sans ces définitions, la zone ne reconnaît
    /// jamais le perso comme « dans un hall » : spawn des NPCs de
    /// hall, GetData 132 et la sortie 176 -1 étaient tous muets.</summary>
    internal static void EnsureFamilyInstanceNodes(
        List<GameDataParsers.NodeDef> nodes)
    {
        if (nodes.All(n => n.Id != 501))
            nodes.Add(new GameDataParsers.NodeDef
            {
                Id = 501, Type = 4, Name = "FamilyTown",
                MapCode = "S501", MapCodeM = "S501M",
                MapCodeS = "S501S",
            });
        if (nodes.All(n => n.Id != 502))
            nodes.Add(new GameDataParsers.NodeDef
            {
                Id = 502, Type = 4, Name = "FamilyTown",
                MapCode = "S502", MapCodeM = "S502M",
                MapCodeS = "S502S",
            });
    }

    /// <summary>[ST]_TextIndex.ini : S_ = `id|flag|texte` (3 col.) et
    /// T_ = `id|texte` (2 col., UTF-8) — les deux formes coexistent
    /// (S_TextIndex @2399 `id|1|初階…` vs T_TextIndex @2399
    /// `id|Novice Trial Medal|`). Le texte peut contenir des
    /// `%s1/%s2/%d` — formats ReplaceOrderParm natifs.</summary>
    private static Dictionary<int, string> ParseTextIndex(string path)
    {
        var dict = new Dictionary<int, string>();
        string[] encodings = { "utf-8", "big5" };
        foreach (var enc in encodings)
        {
            try
            {
                var ok = 0;
                foreach (var line in File.ReadLines(path,
                             Encoding.GetEncoding(enc)))
                {
                    var sep = line.IndexOf('|');
                    if (sep <= 0) continue;
                    var rest = line[(sep + 1)..];
                    var sep2 = rest.IndexOf('|');
                    string text;
                    if (sep2 < 0)
                    {
                        // T_ sans pipe final : `id|texte` tout court —
                        // on ne prend le reste que s'il n'est pas un
                        // flag numérique S_ tronqué.
                        text = rest.TrimEnd('\r', '\n', '|');
                        if (text.Length == 0 ||
                            int.TryParse(text, out _)) continue;
                    }
                    else
                    {
                        text = rest[(sep2 + 1)..].TrimEnd('\r', '\n', '|');
                        // T_ (2 col.) : la 3e "colonne" est vide — le
                        // vrai texte est le champ du milieu (bug
                        // historique : toutes les entrées T_ étaient "").
                        if (text.Length == 0)
                            text = rest[..sep2].TrimEnd();
                    }
                    if (!int.TryParse(line[..sep].Trim(), out var id))
                        continue;
                    if (!dict.ContainsKey(id))
                    {
                        dict[id] = text;
                        ok++;
                    }
                }
                if (ok > 0) return dict;
                dict.Clear();
            }
            catch { /* encodage incompatible — tentative suivante */ }
        }
        return dict;
    }

    /// <summary>T_Spell.ini : `id|nom|desc|icon` — le nom est col 1
    /// (ASCII). Les lignes de continuation de description commencent
    /// par '$' et sont ignorées. Fichier ASCII/UTF-8.</summary>
    private static Dictionary<int, string> ParseSpellNames(string path)
    {
        var dict = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(path))
        {
            var cols = line.Split('|');
            if (cols.Length >= 2 && int.TryParse(cols[0], out var id)
                && cols[1].Length > 0 && !cols[1].StartsWith('$'))
                dict[id] = cols[1];
        }
        return dict;
    }

    /// <summary>Extrait les descriptions du texte anglais d'enchant —
    /// format T_Enchant `id|name|description|...`.</summary>
    private static Dictionary<int, string> ParseEnchantDescriptions(
        string path)
    {
        var dict = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(path))
        {
            var cols = line.Split('|');
            if (cols.Length > 2 && int.TryParse(cols[0], out var id)
                && cols[2].Length > 0 && !cols[2].StartsWith('$'))
                dict[id] = cols[2].Trim();
        }
        return dict;
    }

    /// <summary>EquipType.ini — CSV `kind,nom,?,loc,?,nom` ;
    /// `loc` = champ 3 (−1 = non équipable / dummy −1 filtré).</summary>
    private static Dictionary<int, int> ParseEquipLocs(string path)
    {
        var dict = new Dictionary<int, int>();
        foreach (var line in File.ReadLines(path))
        {
            var cols = line.Split(',');
            if (cols.Length > 3 && int.TryParse(cols[0], out var kind)
                && kind >= 0 && int.TryParse(cols[3], out var loc)
                && loc > 0)
                dict[kind] = loc;
        }
        return dict;
    }

    /// <summary>S_Buliding.ini — lignes `id|code|nom|type|…`
    /// (<c>CBuildingQuery::Deserialize</c> mission @0x82aca20 :
    /// col0→+0 id, col1→+4 code, col2→+8 nom, col3→+0xc type,
    /// col7→+0x1c coût/point de réparation, col8→+0x20 durabilité
    /// max).</summary>
    private static Dictionary<int, BuildingDef> ParseBuildings(
        string path)
    {
        var dict = new Dictionary<int, BuildingDef>();
        foreach (var line in File.ReadLines(path))
        {
            var cols = line.Split('|');
            if (cols.Length > 8 && int.TryParse(cols[0], out var tid)
                && short.TryParse(cols[3], out var type))
            {
                int.TryParse(cols[7], out var cost);
                int.TryParse(cols[8], out var maxDur);
                int.TryParse(cols[4], out var level);
                int.TryParse(cols[5], out var prev);
                int.TryParse(cols[6], out var npcItem);
                dict[tid] = new BuildingDef(type, cost, maxDur,
                    cols[1], level, prev, npcItem);
            }
        }
        return dict;
    }

    /// <summary>4PHouse.ini `[TableHouse]` — lignes
    /// <c>DB&lt;n&gt;=code,modèle,modèle2,dx dy df,…</c> (8 triplets
    /// d'offsets de slots NPC, indexes 1-8 de la formule
    /// <c>GetNpcPositionByNpcLoc</c> @0x84662b0). La clé est le code
    /// col0 (ex. <c>H00101</c>, = S_Buliding col1).</summary>
    private static Dictionary<string, IReadOnlyList<(float, float,
        float)>> ParseHouseLayouts(string path)
    {
        var dict = new Dictionary<string, IReadOnlyList<(float, float,
            float)>>(StringComparer.OrdinalIgnoreCase);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in File.ReadLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB")) continue;
            var cols = line[(eq + 1)..].TrimEnd(',', '\r', '\n')
                .Split(',');
            if (cols.Length < 4) continue;
            var slots = new List<(float, float, float)>();
            foreach (var c in cols.Skip(3))
            {
                var p = c.Trim().Split(' ',
                    StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                slots.Add((float.Parse(p[0], inv),
                    float.Parse(p[1], inv), float.Parse(p[2], inv)));
            }
            if (slots.Count > 0) dict[cols[0].Trim()] = slots;
        }
        return dict;
    }

    /// <summary>S_Territory.ini — records multi-lignes
    /// `id|nom|group|col4|type|desc§récompenses…|` : seule la ligne
    /// d'ouverture (`^\d+|`) est lue (id→+4, nom→+0x18, col5 type→
    /// +0x10 d'après <c>CTerritoryQuery::Init</c> @0x8326e10).</summary>
    private static Dictionary<int, TerritoryDef> ParseTerritories(
        string path)
    {
        var dict = new Dictionary<int, TerritoryDef>();
        foreach (var raw in File.ReadLines(path, EdenEncoding.Big5))
        {
            var line = raw.TrimEnd('\r');
            var cols = line.Split('|');
            if (cols.Length > 5 && int.TryParse(cols[0], out var id)
                && id > 0 && int.TryParse(cols[4], out var type)
                && type > 0)
            {
                int.TryParse(cols[3], out var node);
                dict[id] = new TerritoryDef(id, cols[1], type, node);
            }
        }
        return dict;
    }

    /// <summary>S_Schedule.ini — lignes `row|schedType|entryId|
    /// beginHHMM|endHHMM|` → <c>schedType → entrées</c> (dayId =
    /// entryId : les schedules de guerre l'indexent comme jour de
    /// semaine — prouvé par les requêtes BEGIN_WEEK*/DAY* natives,
    /// `SCHEDULE` = HHMM heure de début, END_HOUR = fin).</summary>
    private static Dictionary<int, List<ScheduleEntry>>
        ParseSchedules(string path)
    {
        var dict = new Dictionary<int, List<ScheduleEntry>>();
        foreach (var line in File.ReadLines(path))
        {
            var cols = line.Split('|');
            if (cols.Length > 4 && int.TryParse(cols[1], out var type)
                && type > 0
                && int.TryParse(cols[2], out var day)
                && int.TryParse(cols[3], out var begin)
                && int.TryParse(cols[4], out var end))
            {
                if (!dict.TryGetValue(type, out var list))
                    dict[type] = list = new List<ScheduleEntry>();
                list.Add(new ScheduleEntry(day, begin, end));
            }
        }
        return dict;
    }

    /// <summary>Fichiers .ini attendus mais absents de
    /// <see cref="DataDir"/> — alimenté par <see cref="LoadAll"/>.
    /// Historiquement un serveur lancé sans DATA_DIR chargeait
    /// silencieusement 0 table (bug docker : aucun volume data).</summary>
    public List<string> MissingFiles { get; } = new();

    /// <summary>Échoue au démarrage si les tables cœur sont vides —
    /// un serveur sans items/monstres/sorts/nodes est cassé de toute
    /// façon, autant le dire au boot plutôt qu'en jeu.</summary>
    public void RequireLoaded()
    {
        if (Items.Count == 0 || Monsters.Count == 0 || Spells.Count == 0
            || Nodes.Count == 0)
            throw new InvalidOperationException(
                $"Gamedata vide — DATA_DIR='{DataDir}' ne contient pas " +
                $"les tables natives ({MissingFiles.Count} fichiers " +
                $"absents, dont : {string.Join(", ", MissingFiles.Take(6))})");
    }

    private void Load(string fileName, Action<string> loader)
    {
        var path = Path.Combine(DataDir, fileName);
        if (File.Exists(path)) loader(path);
        else MissingFiles.Add(fileName);
    }

    /// <summary>Montures EDENFR absentes de l'oracle US : ajoute depuis
    /// le db client (<c>EDEN_REFORGED_DB</c> = corpus/fr-full) les items
    /// (c_item.ini) et enchants (c_enchant.ini, c_itemmallenchant.ini)
    /// dont l'id manque (ou dont la def US est un placeholder sans enchant
    /// de monture) — sans quoi 53 montures « Hors archive »
    /// (Kitsune de l'Aube 1290, Renard blanc 48813…) n'existent pas côté
    /// serveur et ne peuvent pas se monter. Les ids déjà présents ne sont
    /// jamais remplacés. Sans db client : no-op.</summary>
    /// <summary>Stats des monstres de donjon depuis le db EDENFR
    /// (<c>c_monster.ini</c>) : S_Monster (oracle US) diverge sur 126 des
    /// 2 062 ids posés dans les scènes de donjon (ex. boss S804 58543 HP
    /// 28 915 US contre 57 830 FR). Remplace la ligne S_Monster des ids
    /// donnés ; retourne le nombre de lignes changées.</summary>
    public int OverlayFrDungeonMonsters(string? frDbDir,
        IReadOnlySet<int> dungeonMonsterIds)
    {
        if (frDbDir == null) return 0;
        var file = Directory.EnumerateFiles(frDbDir).FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), "c_monster.ini",
                StringComparison.OrdinalIgnoreCase));
        if (file == null) return 0;
        var fr = MonsterParser.Parse(file)
            .Where(m => dungeonMonsterIds.Contains(m.Id))
            .ToDictionary(m => m.Id);
        var changed = 0;
        for (var i = 0; i < Monsters.Count; i++)
            if (fr.TryGetValue(Monsters[i].Id, out var f) &&
                (Monsters[i].HP != f.HP ||
                 Monsters[i].AttackPower != f.AttackPower ||
                 Monsters[i].ExpReward != f.ExpReward ||
                 Monsters[i].Level != f.Level))
            {
                Monsters[i] = f;
                changed++;
            }
        return changed;
    }

    /// <summary>Ordres de travail (métiers 12-17) depuis le db client
    /// EDENFR (<c>c_familycollect.ini</c> / <c>c_familycombine.ini</c>) :
    /// le client affiche et planifie avec SES tables, l'oracle US
    /// S_FamilyCollect diverge sur 100 durées (col10 : 50 US contre 25
    /// FR, ex. work_id 111-118) et S_FamilyCombine sur 130 catégories
    /// (col1 5 US / 3 FR). Les lignes FR remplacent celles de même clé
    /// (collect : id du catalyseur, combine : work_id) ; les lignes
    /// US-only restent (aucun plan FR ne les référence). Sans db
    /// client : no-op. Retourne le nombre de recettes dont un champ de
    /// logique (niveau bâtiment, durée, or, usure, instantané) change.
    /// </summary>
    public int OverlayFrWorkOrders(string? frDbDir)
    {
        if (frDbDir == null) return 0;
        string? Find(string name) => Directory.EnumerateFiles(frDbDir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name,
                StringComparison.OrdinalIgnoreCase));
        var changed = 0;
        if (Find("c_familycollect.ini") is { } cc)
            foreach (var d in WorkOrderParser.ParseCollect(cc)
                         .GroupBy(x => x.CatalystItemId)
                         .Select(g => g.First()))
            {
                if (WorkCollectById.TryGetValue(d.CatalystItemId, out var o)
                    && (o.BuildingLevel != d.BuildingLevel
                        || o.Duration != d.Duration
                        || o.Gold != d.Gold || o.Instant != d.Instant
                        || o.BuildingDamage != d.BuildingDamage
                        || o.BuildingDamageChance != d.BuildingDamageChance))
                    changed++;
                WorkCollectById[d.CatalystItemId] = d;
            }
        if (Find("c_familycombine.ini") is { } cb)
            foreach (var d in WorkOrderParser.ParseCombine(cb)
                         .GroupBy(x => x.Id).Select(g => g.First()))
            {
                if (WorkCombineById.TryGetValue(d.Id, out var o)
                    && (o.BuildingLevel != d.BuildingLevel
                        || o.Duration != d.Duration
                        || o.Gold != d.Gold || o.Instant != d.Instant
                        || o.Category != d.Category
                        || o.BuildingDamage != d.BuildingDamage
                        || o.BuildingDamageChance != d.BuildingDamageChance))
                    changed++;
                WorkCombineById[d.Id] = d;
            }
        return changed;
    }

    /// <summary>Boutiques PNJ depuis le db client EDENFR
    /// (<c>c_store.ini</c>) : le client résout les articles via SA
    /// table (le 304 ne porte que le storeId) — indexer S_Store (oracle
    /// US, qui préfixe deux scrolls itemmall au store 3) désynchronisait
    /// les slots : clic sur la potion = achat d'un parchemin à 100 000
    /// or (bug live). Sans db client : no-op, S_Store reste la
    /// référence. Retourne le nombre de stores chargés (0 = pas
    /// d'overlay).</summary>
    public int OverlayFrStores(string? frDbDir)
    {
        if (frDbDir == null) return 0;
        var file = Directory.EnumerateFiles(frDbDir).FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), "c_store.ini",
                StringComparison.OrdinalIgnoreCase));
        if (file == null) return 0;
        var fr = ShopAndMiscParsers.ParseStores(file);
        if (fr.Count == 0) return 0;
        Stores = fr;
        return fr.Count;
    }

    internal void MergeFrMounts(string? frDbDir)
    {
        if (frDbDir == null) return;
        string? Find(string name) => Directory.EnumerateFiles(frDbDir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name,
                StringComparison.OrdinalIgnoreCase));
        var haveE = Enchants.Select(e => e.Id).ToHashSet();
        foreach (var name in new[] { "c_enchant.ini",
                     "c_itemmallenchant.ini" })
        {
            var f = Find(name);
            if (f == null) continue;
            foreach (var e in EnchantParser.Parse(f))
            {
                if (haveE.Add(e.Id)) { Enchants.Add(e); continue; }
                // Id réutilisé par l'oracle US pour un autre effet
                // (ex. 58996 Tank de Combat) : le ride EDENFR l'emporte.
                var cur = Enchants.First(x => x.Id == e.Id);
                if (e.Raw.Contains("2134") && !cur.Raw.Contains("2134"))
                {
                    Enchants[Enchants.IndexOf(cur)] = e;
                    continue;
                }
                // Kind 8002 « pierre de chance » : les lignes US de
                // S_ItemMallEnchant sont tronquées (pas de borne ni de
                // flag bind) — la ligne EDENFR est la référence.
                if (e.TriggerParams[0] == 0x1f42)
                    Enchants[Enchants.IndexOf(cur)] = e;
            }
        }
        var rideEnchants = Enchants
            .Where(e => e.Raw.Contains("2134")).Select(e => e.Id)
            .ToHashSet();
        var byId = Items.ToDictionary(i => i.Id);
        foreach (var itemFile in new[] { "c_item.ini", "c_itemmall.ini" })
        {
            var itemFr = Find(itemFile);
            if (itemFr == null) continue;
            foreach (var fr in ItemParser.Parse(itemFr))
            {
                if (!byId.TryGetValue(fr.Id, out var cur))
                {
                    Items.Add(fr);
                    byId[fr.Id] = fr;
                }
                // Placeholder US (ex. 48813 « 測試用編號 », sans enchant) :
                // la def EDENFR d'une monture remplace celle de l'oracle.
                else if (cur.UseEnchantId != fr.UseEnchantId &&
                         rideEnchants.Contains(fr.UseEnchantId))
                {
                    Items[Items.IndexOf(cur)] = fr;
                    byId[fr.Id] = fr;
                }
            }
        }
    }

    /// <summary>Fusionne les defs <c>S_ItemExtra.ini</c> — prioritaires :
    /// un id existant (placeholder US comme 48813 « 測試用編號 ») est
    /// remplacé par la def EDENFR, sinon ajouté.</summary>
    internal static void MergeItemExtra(List<ItemParser.ItemDef> items,
        IEnumerable<ItemParser.ItemDef> extra)
    {
        foreach (var m in extra)
        {
            items.RemoveAll(i => i.Id == m.Id);
            items.Add(m);
        }
    }
}
