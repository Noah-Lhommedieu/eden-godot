using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Services;
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

    /// <summary>Le certificat EVA de test3 alimente le bonus du profil
    /// depuis le texte d'effet natif T_Enchant.</summary>
    [Fact]
    public async Task EquippedEvaCertificate_ContributesToProfileStats()
    {
        var repo = new MemLicenseRepo();
        await repo.AddLicenseAsync(200000004, unchecked((short)33281), 1);
        var service = CreateService(repo, GameData);
        await service.LoadAsync(200000004);

        Assert.Equal(4, service.ProvideStats(200000004).DodgePercent);
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

        Assert.Equal(4, service.ProvideStats(200000004).DodgePercent);
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
}
