using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du système de certificats (« License Knowledge ») — lot Q.
/// Couvre le parsing S_EquipSet (combos), le pool de lookup natif
/// (`InitEquipSetID`), le service LicenseService (slots, capacité,
/// combos, remplacement) et les entités de persistance — tout sur
/// les données natives réelles de server_us.
/// </summary>
public class LicenseTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    /// <summary>Repository `player_license` en mémoire.</summary>
    private sealed class MemLicenseRepo : ILicenseRepository
    {
        public readonly List<LicenseEntity> Rows = new();

        public Task<IReadOnlyList<LicenseEntity>> GetLicensesAsync(
            int playerId)
            => Task.FromResult<IReadOnlyList<LicenseEntity>>(
                Rows.Where(r => r.PlayerId == playerId).ToList());

        public Task AddLicenseAsync(int playerId, short itemId,
            short loc)
        {
            if (Rows.All(r => r.PlayerId != playerId ||
                r.ItemId != itemId))
                Rows.Add(new LicenseEntity
                { PlayerId = playerId, ItemId = itemId, Loc = loc });
            return Task.CompletedTask;
        }

        public Task SetLicenseLocAsync(int playerId, short itemId,
            short loc)
        {
            var r = Rows.First(x => x.PlayerId == playerId &&
                x.ItemId == itemId);
            r.Loc = loc;
            return Task.CompletedTask;
        }

        public Task RemoveLicenseAsync(int playerId, short itemId)
        {
            Rows.RemoveAll(r => r.PlayerId == playerId &&
                r.ItemId == itemId);
            return Task.CompletedTask;
        }
    }

    private static readonly GameDataService Data = LoadData();

    private static GameDataService LoadData()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        return gd;
    }

    private static GameDataService GameData => Data;

    private static LicenseService CreateService(
        MemLicenseRepo repo, GameDataService gd)
        => new(repo, gd.Items.ToDictionary(i => i.Id),
            gd.EquipSetPool!, new EnchantService(gd.Enchants),
            gd.EnchantDescriptions);

    /// <summary>Le certificat « Agilité I » (33281 → enchant 52515
    /// `2088||4` = AttrDodgeRateRate +4 %) alimente le canal
    /// multiplicatif d'esquive — fidélité native
    /// (`CEC_AttrDodgeRateRate::Execute` + `PhysicalDodgeRate` :
    /// facteur (1+pct) sur le numérateur, PAS +4 pp additif).</summary>
    [Fact]
    public async Task EquippedEvaCertificate_ContributesToProfileStats()
    {
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(200000004, unchecked((short)33281), 1);
        var service = CreateService(repo, GameData);
        await service.LoadAsync(200000004);

        var stats = service.ProvideStats(200000004);
        Assert.Equal(4, stats.DodgeRatePercent);
        Assert.Equal(0, stats.DodgePercent);
    }

    /// <summary>Deux certificats qui référencent le même enchant ne le
    /// cumulent pas deux fois, comme `FindEnchant` natif.</summary>
    [Fact]
    public async Task DuplicateActiveEnchant_IsCountedOnceLikeNativeFindEnchant()
    {
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(200000004, 1, 1);
        await repo.AddLicenseAsync(200000004, 2, 2);
        var defs = new Dictionary<int, ItemParser.ItemDef>
        {
            [1] = new() { Id = 1, UseEnchantId = 52515 },
            [2] = new() { Id = 2, UseEnchantId = 52515 },
        };
        var service = new LicenseService(repo, defs,
            GameData.EquipSetPool!, new EnchantService(GameData.Enchants),
            GameData.EnchantDescriptions);
        await service.LoadAsync(200000004);

        Assert.Equal(4, service.ProvideStats(200000004).DodgeRatePercent);
    }

    /// <summary>Contribution d'un certificat posé (helper —
    /// `player_license.loc` 1..7 → slot actif).</summary>
    private static async Task<StatContribution> StatsOfEquipped(
        int itemId)
    {
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(200000004, unchecked((short)itemId), 1);
        var service = CreateService(repo, GameData);
        await service.LoadAsync(200000004);
        return service.ProvideStats(200000004);
    }

    /// <summary>Chaque type de bonus passif de certificat alimente le
    /// canal natif attendu — données EDENFR réelles (c_class 22-29 →
    /// c_item col23 → c_enchant cmd col9+7k).</summary>
    [Theory]
    // 2122|±N = AttrHateRate — aggro générée.
    [InlineData(33272, "hate", 5)]          // Raillerie I (Guerrier)
    [InlineData(34170, "hate", 8)]          // Raillerie II
    [InlineData(33308, "hate", -5)]         // Colère Tranquille I (Magicien)
    [InlineData(34206, "hate", -8)]         // Colère Tranquille II
    // 2083|a1 = AttrDefenceRate — « Toutes les DÉF +N % ».
    [InlineData(33273, "defPct", 5)]        // Armure en Fer I
    [InlineData(34171, "defPct", 10)]       // Armure en Fer II
    [InlineData(33286, "defPct", 5)]        // Énergie Défensive I (Assassin)
    // 2083|a2 = AttrMagicDamageRate — « ATQ MAG +N % ».
    [InlineData(24236, "matkPct", 5)]       // Détermination Chaotique 1
    [InlineData(24239, "matkPct", 10)]      // Détermination Chaotique 2
    // 2040 : a0→crit phys %, a1→crit mag %.
    [InlineData(33289, "critRatePct", 5)]   // Maître de Danse I (Danselame)
    [InlineData(34187, "critRatePct", 10)]  // Maître de Danse II
    [InlineData(33294, "critRatePct", 5)]   // Dynamique des Turbines I (Ingénieur)
    [InlineData(34192, "critRatePct", 10)]  // Dynamique des Turbines II
    [InlineData(33309, "mcritRatePct", 5)]  // Fusion des Éléments I
    [InlineData(34207, "mcritRatePct", 10)] // Fusion des Éléments II
    [InlineData(33302, "mcritRatePct", 5)]  // Esprit du Rock I (Barde)
    // 2043 : a0→crit dmg phys, a1→crit dmg mag.
    [InlineData(33290, "critDmg", 10)]      // Cherche Faiblesse I (Chasseur)
    [InlineData(34188, "critDmg", 20)]      // Cherche Faiblesse II
    [InlineData(33306, "mcritDmg", 10)]     // Totem des Esprits I (Shaman)
    [InlineData(34204, "mcritDmg", 20)]     // Totem des Esprits II
    [InlineData(37021, "critDmg", 10)]      // Stabilité I (Samouraï)
    // 2044 : a1→crit dmg mag (donnée négative — fidèle au dispatch).
    [InlineData(20806, "mcritDmg", -10)]    // Réconciliation Esprit Saint I (Sage)
    [InlineData(20809, "mcritDmg", -20)]    // Réconciliation Esprit Saint II
    // 2085 : a0→Str, a1→Agi, a2→Int, a3→Wis, a4→Lck en %.
    [InlineData(33284, "strPct", 10)]       // Puissance Musculaire I (Assassin)
    [InlineData(34182, "strPct", 15)]       // Puissance Musculaire II
    [InlineData(33283, "agiPct", 10)]       // Reflet de l'Orage I (Voleur)
    [InlineData(34181, "agiPct", 15)]       // Reflet de l'Orage II
    [InlineData(33310, "intPct", 10)]       // Connaissances en Sorcellerie I
    [InlineData(34208, "intPct", 15)]       // Connaissances en Sorcellerie II
    [InlineData(33299, "wisPct", 10)]       // Forte Croyance I (Clerc)
    [InlineData(34197, "wisPct", 15)]       // Forte Croyance II
    [InlineData(33292, "lckPct", 10)]       // Chasse au Trésor I (Chasseur)
    [InlineData(34190, "lckPct", 15)]       // Chasse au Trésor II
    // 2088 : a0→AttrHitRateRate, a1→AttrDodgeRateRate.
    [InlineData(33281, "dodgeRatePct", 4)]  // Agilité I (Voleur)
    [InlineData(34179, "dodgeRatePct", 8)]  // Agilité II
    [InlineData(33293, "hitRatePct", 10)]   // Lentilles de Contact I (Ingénieur)
    [InlineData(34191, "hitRatePct", 15)]   // Lentilles de Contact II
    [InlineData(33315, "hitRatePct", 10)]   // Œil du Démon I (Démoniste)
    // 2002|a0 = AttrMaxHPRate — « PV MAX +N % ».
    [InlineData(33285, "hpPct", 5)]         // Endurance I
    [InlineData(34183, "hpPct", 10)]        // Endurance II
    // 2013|a0 = AttrMaxMPRate — « PM +N % ».
    [InlineData(33301, "mpPct", 5)]         // Gentillesse Divine I (Clerc)
    [InlineData(34199, "mpPct", 10)]        // Gentillesse Divine II
    // 2015|a0 = CostMPPlus flat (négatif = réduction).
    [InlineData(33303, "mpCost", -2)]       // Accordeur I (Barde)
    [InlineData(34201, "mpCost", -5)]       // Accordeur II
    [InlineData(33313, "mpCost", -2)]       // Contrôle de l'Illusion I
    // 2051|a0 = AttrMovement — « VIT DÉP +N % ».
    [InlineData(33297, "movePct", 5)]       // Passe Rapide I (Corsaire)
    [InlineData(34195, "movePct", 10)]      // Passe Rapide II
    // 2052|a0 = AttrWeaponAttackSpeedLevel cat0 (négatif = +rapide).
    [InlineData(33296, "atkSpeedPct", 8)]   // Objectif Rapide I (Corsaire)
    [InlineData(34194, "atkSpeedPct", 15)]  // Objectif Rapide II
    // 2053|a0 = AttrCastTime (négatif = cast plus court → +vitesse).
    [InlineData(33312, "castSpeed", 5)]     // Joue avec le Temps I (Illusionniste)
    [InlineData(34210, "castSpeed", 10)]    // Joue avec le Temps II
    [InlineData(33307, "castSpeed", 5)]     // (Shaman, 52541)
    // 2108|a0 = AttrShieldBlock — taux de blocage (rapproché incond.).
    [InlineData(33278, "blockPct", 3)]      // Défense Bouclier Massif I (Templier)
    [InlineData(34176, "blockPct", 5)]      // Défense Bouclier Massif II
    // 2109|a0 = AttrWeaponParryRate — parade (rapproché incond.).
    [InlineData(33276, "parry", 4)]         // Maître de Défense I (Chevalier)
    [InlineData(34174, "parry", 7)]         // Maître de Défense II
    [InlineData(33288, "parry", 4)]         // Corps Dynamique I (Danselame)
    [InlineData(34186, "parry", 7)]         // Corps Dynamique II
    [InlineData(37022, "parry", 4)]         // Croyance Loyale I (Samouraï)
    public async Task EquippedCertificate_AppliesNativeStatChannel(
        int itemId, string channel, double expected)
    {
        var stats = await StatsOfEquipped(itemId);
        var actual = channel switch
        {
            "hate" => stats.HateRatePercent,
            "defPct" => stats.DefenceRatePercent,
            "matkPct" => stats.MagicAttackRatePercent,
            "critRatePct" => stats.CritRatePercent,
            "mcritRatePct" => stats.MagicCritRatePercent,
            "critDmg" => stats.CritDamageAdd,
            "mcritDmg" => stats.MagicCritDamageAdd,
            "strPct" => stats.Pct(StatIndex.Str),
            "agiPct" => stats.Pct(StatIndex.Agi),
            "intPct" => stats.Pct(StatIndex.Int),
            "wisPct" => stats.Pct(StatIndex.Wis),
            "lckPct" => stats.Pct(StatIndex.Lck),
            "dodgeRatePct" => stats.DodgeRatePercent,
            "hitRatePct" => stats.HitRatePercent,
            "hpPct" => stats.Pct(StatIndex.Hp),
            "mpPct" => stats.Pct(StatIndex.Mp),
            "mpCost" => stats.MpCostFlat,
            "movePct" => stats.MoveSpeedPercent,
            "atkSpeedPct" => stats.AttackSpeedRatePercent,
            "castSpeed" => stats.CastSpeedAdd,
            "blockPct" => stats.BlockPercentAdd,
            "parry" => stats.ParryAdd,
            _ => throw new ArgumentOutOfRangeException(channel),
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>Résistances : 2095 → éléments 4-9 (AttrResist),
    /// 2101 → physiques 1-3 — « Résistances à tous les éléments ».</summary>
    [Fact]
    public async Task ResistCertificates_FillElementChannels()
    {
        // Cœur Sacré I (Templier) — 52539 `2095|3|3|3|3|3|3`.
        var stats = await StatsOfEquipped(33279);
        for (var i = 3; i < 9; i++)
            Assert.Equal(3, stats.ResistAdds[i]);

        // Pas Peur de la Mort I (Samouraï) — 52973 `2101|3|3|3`.
        stats = await StatsOfEquipped(37023);
        Assert.Equal(3, stats.ResistAdds[0]);
        Assert.Equal(3, stats.ResistAdds[1]);
        Assert.Equal(3, stats.ResistAdds[2]);

        // Bénédiction du Roi Sage I (Sage) — 50602 `2101|3|3|3`.
        stats = await StatsOfEquipped(20805);
        Assert.Equal(3, stats.ResistAdds[0]);
    }

    /// <summary>Les blocs transférés vers le set déclenché (col40)
    /// ne comptent PAS en passif — natif `CEnchantQuery::Deserialize`
    /// (`*slotPtr = 0` après transfert). Sans cette exclusion, les
    /// certificats à proc octroyaient leurs bonus en permanence.</summary>
    [Theory]
    // 24235 → 46304 `1014|5|5` slots=1 evt1 — proc « 10 % → +5 % PM ».
    [InlineData(24235)]
    // 24238 → 46305 `1014|10|10` slots=1 — idem palier 2.
    [InlineData(24238)]
    // 28048 → 41709 `2047|1`+`1004|3|3` slots=1;2 — heal/HP de proc.
    [InlineData(28048)]
    // 28051 → 41712 `2047|1`+`1004|5|5` slots=1;2 — idem palier 2.
    [InlineData(28051)]
    // 28049 → 41710 `2083|5||5` slots=1 — proc « +5 % ATQ ».
    [InlineData(28049)]
    // 28052 → 41713 `2083|10||10` slots=1 — idem palier 2.
    [InlineData(28052)]
    // 21913 → 56112 `2061|||||100` slots=1 evt4 — proc « Trivial ».
    [InlineData(21913)]
    // 33295 → 52529 `2055|…` slots=1 evt2 — proc stun.
    [InlineData(33295)]
    // 28050 → 41711 `2055|…` slots=1 evt2 — proc stun (Juge).
    [InlineData(28050)]
    public async Task TriggeredBlocks_DoNotCountAsPassiveStats(int itemId)
    {
        var stats = await StatsOfEquipped(itemId);
        // Aucun canal ne doit bouger : tout le bonus vit dans le proc.
        Assert.Equal(0, stats.HateRatePercent);
        Assert.Equal(0, stats.DefenceRatePercent);
        Assert.Equal(0, stats.PhysAttackRatePercent);
        Assert.Equal(0, stats.MagicAttackRatePercent);
        Assert.Equal(0, stats.DodgeRatePercent);
        Assert.Equal(0, stats.HitRatePercent);
        Assert.Equal(0, stats.HealGivenFlat);
        Assert.Equal(0, stats.HealTakenFlat);
        Assert.Equal(0, stats.Pct(StatIndex.Hp));
        Assert.Equal(0, stats.Pct(StatIndex.Mp));
    }

    /// <summary>Certificat MultiStroke (33282 → 52516 `2031|1|50` +
    /// `2032|1|50`) : pas de canal stats — les commandes restent
    /// lisibles par <see cref="EnchantService.GetAttackCommands"/>
    /// pour le dispatch de procs.</summary>
    [Fact]
    public async Task MultiStrokeCertificate_NoStat_ButAttackCommands()
    {
        var stats = await StatsOfEquipped(33282);
        Assert.Equal(0, stats.CritRatePercent);
        Assert.Equal(0, stats.DodgeRatePercent);

        var enchants = new EnchantService(GameData.Enchants);
        var cmds = enchants.GetAttackCommands(52516);
        Assert.Equal(2, cmds.Count);
        Assert.Equal(2031, cmds[0].CommandId);
        Assert.Equal(50, cmds[0].Args[1]);
    }

    /// <summary>Un certificat possédé mais non posé (loc=0) ne doit
    /// rien contribuer — miroir du vecteur +0xafc natif.</summary>
    [Fact]
    public async Task OwnedButUnslottedCertificate_ContributesNothing()
    {
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(200000004, unchecked((short)33272), 0);
        var service = CreateService(repo, GameData);
        await service.LoadAsync(200000004);
        var c = service.ProvideStats(200000004);
        Assert.Equal(0, c.HateRatePercent);
        Assert.Equal(0, c.DefenceRatePercent);
    }

    [Fact]
    public void EquipSetPool_FourSlotCombo_ResolvesEnchant()
    {
        var gd = GameData;
        // Set 79 « 盾牌大師 » : 4 listes identiques de certificats,
        // récompense index 4-2=2 → item 38157 (enchant +0x60).
        var def = gd.EquipSets.First(d => d.Id == 79);
        var ids = def.PositionLists.Take(4)
            .Select((l, i) => l[i]).ToArray();  // 4 ids distincts

        var item = gd.EquipSetPool!.QueryItem(ids);
        Assert.Equal(38157, item);
        var expected = gd.Items.First(i => i.Id == 38157).UseEnchantId;
        Assert.Equal(expected, gd.EquipSetPool.QueryEnchant(ids));
        Assert.NotEqual(0, expected);
    }

    [Fact]
    public void EquipSetPool_ThreeSlotCombo_UsesIndexOne()
    {
        var gd = GameData;
        // Set 84 « 戰鬥之心 » : 3 listes → récompense index 3-2=1
        // (col15) = 38162 — vérifie l'indexation taille-2.
        var def = gd.EquipSets.First(d => d.Id == 84);
        var ids = def.PositionLists.Take(3)
            .Select((l, i) => l[i]).ToArray();

        Assert.Equal(38162, gd.EquipSetPool!.QueryItem(ids));
    }

    [Fact]
    public void EquipSetPool_WrongSizeOrMember_ReturnsZero()
    {
        var gd = GameData;
        var def = gd.EquipSets.First(d => d.Id == 79); // 4 listes
        var ids = def.PositionLists.Take(4)
            .Select((l, i) => l[i]).ToArray();
        // Un id étranger au set → aucune ligne ne matche → 0.
        var foreign = ids.Skip(1).Append((ushort)1).ToArray();
        Assert.Equal(0, gd.EquipSetPool!.QueryEnchant(foreign));
        // Doublon → pas un set natif (std::set déduplique — un tuple
        // avec doublon retombe sur une taille plus petite sans
        // récompense index 4 pour ce set).
        var dup = new[] { ids[0], ids[0], ids[1], ids[2] };
        Assert.Equal(0, gd.EquipSetPool.QueryEnchant(dup));
    }

    [Fact]
    public void LicenseService_Capacity_UsesCharLevel()
    {
        // KB-18 : règle native `lv/10 + 1` (SetCurrentClassLicense
        // @0x082714f0 : refus si lv/10+1 <= occupés), plafonnée à 7 ;
        // le `lv/10 + 2` du texte 0x325 n'est que l'affichage.
        Assert.Equal(1, LicenseService.CapacityFor(9));
        Assert.Equal(2, LicenseService.CapacityFor(10));
        Assert.Equal(7, LicenseService.CapacityFor(70));
        Assert.Equal(2, LicenseService.DisplayedCapacityFor(9));
        Assert.Equal(3, LicenseService.DisplayedCapacityFor(10));
    }

    [Fact]
    public async Task SetCurrentClassLicense_SlotsAndConflicts()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);
        // Deux certs réels du guerrier (enchant ≠ 0).
        await repo.AddLicenseAsync(1, unchecked((short)33272), 0);
        await repo.AddLicenseAsync(1, unchecked((short)33273), 0);
        await svc.LoadAsync(1);

        // Niveau 10 → capacité 2 : les deux posent.
        var (err1, changed1) = await svc.SetCurrentClassLicenseAsync(1,
            10, new[] { (unchecked((short)33272), (byte)1),
                        (unchecked((short)33273), (byte)2) });
        Assert.Equal(0, err1);
        Assert.Equal(2, changed1.Count);
        var st = svc.StateOf(1);
        Assert.Equal(unchecked((short)33272), st.SlotLicense[1]);
        Assert.NotEqual(0, st.SlotEnchant[1]);   // 52506 appliqué

        // Conflit : déplacer 33273 vers le slot 1 → 33272 repart
        // en loc=0 (comportement natif LAB_082715a7).
        var (err2, changed2) = await svc.SetCurrentClassLicenseAsync(1,
            10, new[] { (unchecked((short)33273), (byte)1) });
        Assert.Equal(0, err2);
        Assert.Contains(changed2, c =>
            c.ItemId == unchecked((short)33272) && c.Loc == 0);
        Assert.Equal(unchecked((short)33273), st.SlotLicense[1]);
        Assert.Equal(0, st.SlotLicense[2]);
        // Persisté : 33272 → loc 0, 33273 → loc 1.
        Assert.Equal(0, repo.Rows.First(r =>
            r.ItemId == unchecked((short)33272)).Loc);
        Assert.Equal(1, repo.Rows.First(r =>
            r.ItemId == unchecked((short)33273)).Loc);
    }

    [Fact]
    public async Task SetCurrentClassLicense_OverCapacity_Skips()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);
        await repo.AddLicenseAsync(1, unchecked((short)33272), 0);
        await repo.AddLicenseAsync(1, unchecked((short)33273), 0);
        await repo.AddLicenseAsync(1, unchecked((short)33274), 0);
        await svc.LoadAsync(1);

        // Niveau 19 → capacité 2 : la 3e sélection est rejetée (0x325)
        // MAIS la boucle continue (natif : erreur par sélection).
        var (err, changed) = await svc.SetCurrentClassLicenseAsync(1,
            19, new[] { (unchecked((short)33272), (byte)1),
                       (unchecked((short)33273), (byte)2),
                       (unchecked((short)33274), (byte)3) });
        Assert.Equal(LicenseService.ErrorOverCapacity, err);
        Assert.Equal(2, changed.Count);            // seuls les 2 premiers
        Assert.Equal(unchecked((short)33272), svc.StateOf(1).SlotLicense[1]);
        Assert.Equal(0, svc.StateOf(1).SlotLicense[3]);
    }

    [Fact]
    public async Task InvokeSpellStonesSet_AppliesHalfCombo()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);

        // 4 certs distincts du set 79 (listes identiques → prendre
        // les 4 premiers de la liste 1).
        var def = gd.EquipSets.First(d => d.Id == 79);
        var certs = def.PositionLists[0].Take(4).ToArray();
        foreach (var c in certs)
            await repo.AddLicenseAsync(1, (short)c, 0);
        await svc.LoadAsync(1);

        // Les poser en slots 1-4 (niveau 40 → cap 5).
        var sels = certs.Select((c, i) =>
            ((short)c, (byte)(i + 1))).ToList();
        await svc.SetCurrentClassLicenseAsync(1, 40, sels);

        // NCID 183 : ids + flag 0 (moitié slots 1-4).
        var req = certs.Append((ushort)0).ToList();
        var enchant = svc.InvokeSpellStonesSet(1, req);
        Assert.Equal(
            (short)gd.Items.First(i => i.Id == 38157).UseEnchantId,
            enchant);
        Assert.Equal(enchant, svc.StateOf(1).SetEnchant[0]);

        // Rejouer le même set sur la moitié 1 → refus (ids non
        // posés en slots 4-7 → validation échoue).
        var reqBad = certs.Append((ushort)1).ToList();
        Assert.Equal(0, svc.InvokeSpellStonesSet(1, reqBad));
    }

    [Fact]
    public async Task InvokeSpellStonesSet_RequiresSlottedIds()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);
        var def = gd.EquipSets.First(d => d.Id == 79);
        var certs = def.PositionLists[0].Take(4).ToArray();
        // Aucun cert posé → la validation « ids ⊆ slots de la
        // moitié » échoue avant même le lookup.
        await svc.LoadAsync(1);
        var req = certs.Append((ushort)0).ToList();
        Assert.Equal(0, svc.InvokeSpellStonesSet(1, req));
        Assert.Equal(0, svc.StateOf(1).SetEnchant[0]);
    }

    [Fact]
    public async Task GrantLicense_ReplacesLowerTier()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);

        // Tier inférieur posé en slot 1.
        await repo.AddLicenseAsync(1, unchecked((short)33272), 1);
        await svc.LoadAsync(1);
        Assert.Equal(unchecked((short)33272), svc.StateOf(1).SlotLicense[1]);

        // Octroi du tier supérieur 34170 (c35/itemdata+0x8c =
        // « remplace 33272 ») — comportement natif GetLicense.
        await svc.GrantLicenseAsync(1, unchecked((short)34170));

        Assert.DoesNotContain(repo.Rows, r => r.ItemId == unchecked((short)33272));
        Assert.Contains(repo.Rows, r => r.ItemId == unchecked((short)34170));
        Assert.Equal(0, svc.StateOf(1).SlotLicense[1]);  // slot vidé
        Assert.Equal(0, svc.StateOf(1).SlotEnchant[1]);
    }

    [Fact]
    public async Task GrantLicense_NoDoubleGrant()
    {
        var gd = GameData;
        var repo = new MemLicenseRepo();
        var svc = CreateService(repo, gd);
        await svc.LoadAsync(1);
        await svc.GrantLicenseAsync(1, unchecked((short)33272));
        await svc.GrantLicenseAsync(1, unchecked((short)33272));
        Assert.Single(repo.Rows, r => r.ItemId == unchecked((short)33272));
    }

    /// <summary>Le prédicat partagé de la liste de procs classe les
    /// enchants de certificats comme le useEnchant d'équipement :
    /// déclenché col40/41 → vrai, MultiStroke passif → vrai, passif
    /// stats pur → faux (il ne doit jamais entrer dans
    /// ProcEnchantIds).</summary>
    [Fact]
    public void IsProcEnchant_ClassifiesLicenseEnchants()
    {
        var enchants = new EnchantService(GameData.Enchants);
        // 52529 `2055` slots=1 evt2 — proc stun déclenché (GetProc).
        Assert.True(ZoneEntryFlow.IsProcEnchant(enchants, 52529));
        // 52516 `2031|1|50`+`2032|1|50` — MultiStroke passif non
        // déclenché (GetAttackCommands).
        Assert.True(ZoneEntryFlow.IsProcEnchant(enchants, 52516));
        // 52515 `2088||4` — « ESQ +4 % » passif pur.
        Assert.False(ZoneEntryFlow.IsProcEnchant(enchants, 52515));
    }

    /// <summary>Preuve du bug corrigé : la reconstruction de
    /// <see cref="ZoneEntryFlow.EntryState.ProcEnchantIds"/> par
    /// <see cref="ZoneEntryFlow.RebuildProcEnchantIds"/> (appelée à
    /// chaque <c>ResolveWeaponAsync</c>) refusionne les enchants à
    /// proc des licences posées — un changement d'équipement
    /// (Invalidation → re-resolve) ne les perd plus, alors qu'avant
    /// la liste reconstruite depuis l'équipement seul les tuait
    /// jusqu'à la prochaine opération licence 181/182/183.</summary>
    [Fact]
    public async Task RebuildProcEnchantIds_LicenseProcsSurviveEquipChange()
    {
        const int charId = 200000004;
        var repo = new MemLicenseRepo();
        // 33295 → enchant 52529 (proc stun `2055` evt2, GetProc).
        await repo.AddLicenseAsync(charId, unchecked((short)33295), 1);
        // 33282 → enchant 52516 (MultiStroke passif, GetAttackCommands).
        await repo.AddLicenseAsync(charId, unchecked((short)33282), 2);
        // 33281 → enchant 52515 (passif pur — exclu de la liste).
        await repo.AddLicenseAsync(charId, unchecked((short)33281), 3);
        var svc = CreateService(repo, GameData);
        await svc.LoadAsync(charId);
        var enchants = new EnchantService(GameData.Enchants);
        var st = new ZoneEntryFlow.EntryState { CharId = charId };

        // Resolve initial : équipement porteur d'un proc (41710) +
        // les deux licences à proc fusionnées dans l'ordre des slots.
        ZoneEntryFlow.RebuildProcEnchantIds(st, new[] { 41710 },
            svc, enchants);
        Assert.Equal(new[] { 41710, 52529, 52516 },
            st.ProcEnchantIds!);

        // « Changement d'équipement » → WeaponResolved=false →
        // ResolveWeaponAsync reconstruit : l'id équipement remplacé
        // disparaît, les procs de certificats survivent.
        ZoneEntryFlow.RebuildProcEnchantIds(st, new[] { 56110 },
            svc, enchants);
        Assert.Equal(new[] { 56110, 52529, 52516 },
            st.ProcEnchantIds!);

        // Rebuild désarmé — il ne reste que les licences.
        ZoneEntryFlow.RebuildProcEnchantIds(st,
            Enumerable.Empty<int>(), svc, enchants);
        Assert.Equal(new[] { 52529, 52516 }, st.ProcEnchantIds!);
        Assert.DoesNotContain(52515, st.ProcEnchantIds!);
    }

    /// <summary>La dépose d'un certificat (182 → loc 0) retire son
    /// enchant de la reconstruction suivante — l'id ne survit pas
    /// indéfiniment dans la liste.</summary>
    [Fact]
    public async Task RebuildProcEnchantIds_UnslottedLicenseProcDropped()
    {
        const int charId = 200000004;
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(charId, unchecked((short)33295), 1);
        var svc = CreateService(repo, GameData);
        await svc.LoadAsync(charId);
        var enchants = new EnchantService(GameData.Enchants);
        var st = new ZoneEntryFlow.EntryState { CharId = charId };

        ZoneEntryFlow.RebuildProcEnchantIds(st,
            Enumerable.Empty<int>(), svc, enchants);
        Assert.Equal(new[] { 52529 }, st.ProcEnchantIds!);

        // Dépose du certificat → l'enchant sort du merge au rebuild.
        await svc.SetCurrentClassLicenseAsync(charId, 30,
            new[] { (unchecked((short)33295), (byte)0) });
        ZoneEntryFlow.RebuildProcEnchantIds(st,
            Enumerable.Empty<int>(), svc, enchants);
        Assert.Null(st.ProcEnchantIds);
    }

    /// <summary>Dedup par effect_id (miroir du <c>seen</c> de
    /// ProcSources et du FindEnchant natif) : le même enchant porté
    /// par l'équipement ET par deux licences n'apparaît qu'une fois.</summary>
    [Fact]
    public async Task RebuildProcEnchantIds_SameEnchantTwice_MergedOnce()
    {
        const int charId = 1;
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(charId, 10, 1);
        await repo.AddLicenseAsync(charId, 11, 2);
        var defs = new Dictionary<int, ItemParser.ItemDef>
        {
            [10] = new() { Id = 10, UseEnchantId = 52529 },
            [11] = new() { Id = 11, UseEnchantId = 52529 },
        };
        var svc = new LicenseService(repo, defs, GameData.EquipSetPool!,
            new EnchantService(GameData.Enchants),
            GameData.EnchantDescriptions);
        await svc.LoadAsync(charId);
        var enchants = new EnchantService(GameData.Enchants);
        var st = new ZoneEntryFlow.EntryState { CharId = charId };
        ZoneEntryFlow.RebuildProcEnchantIds(st, new[] { 52529, 52529 },
            svc, enchants);
        Assert.Equal(new[] { 52529 }, st.ProcEnchantIds!);
    }
}
