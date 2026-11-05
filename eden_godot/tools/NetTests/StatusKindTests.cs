using EdenClient.Data.Tables;
using Xunit;
using Xunit.Abstractions;

namespace EdenClient.NetTests;

/// <summary>Catégories de status (buff / nerf / état / aucun) sur les données
/// réelles du corpus FR — définitions dans docs/knowledge/definitions.md.</summary>
public class StatusKindTests(ITestOutputHelper output)
{
    private static string CorpusFile(string name)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.GetEnvironmentVariable("EDEN_ROOT") ?? "" })
            for (var d = root.Length > 0 ? new DirectoryInfo(root) : null; d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "corpus", "fr-full", "data", "db", name);
                if (File.Exists(p)) return p;
            }
        throw new FileNotFoundException(name);
    }

    private static readonly Dictionary<int, EnchantTable.EnchantDef> Defs = LoadAll();

    private static Dictionary<int, EnchantTable.EnchantDef> LoadAll()
    {
        var defs = EnchantTable.Load(CorpusFile("c_enchant.ini"));
        foreach (var kv in EnchantTable.Load(CorpusFile("c_itemmallenchant.ini")))
            defs.TryAdd(kv.Key, kv.Value);
        return defs;
    }

    private static StatusKind Kind(int id) =>
        StatusKinds.Classify(id, EnchantTable.CommandsOf(Defs[id]), Defs[id].IsBuff);

    [Fact]
    public void Guerison_InstantHeal_IsNotAStatus() =>
        Assert.Equal(StatusKind.None, Kind(52028));   // dispel 1824 seul

    [Fact]
    public void FreePortalStone_IsAState() =>
        Assert.Equal(StatusKind.State, Kind(55869));  // 2047|1, S_ItemMallEnchant

    [Fact]
    public void ItemMallRows_UseFrSchema_CommandsAreRead()
    {
        // 62 cellules (c_itemmallenchant |V.2|60|) : sans normalisation la
        // commande se lisait en 1 et l'état disparaissait de la liste.
        var cmds = EnchantTable.CommandsOf(Defs[55869]);
        Assert.Contains(cmds, c => c.Id == 2047);
    }

    [Theory]
    [InlineData(52507, StatusKind.Buff)]   // certificat « Toutes les DÉF +5 % »
    public void KnownStatEnchants(int id, StatusKind want) => Assert.Equal(want, Kind(id));

    [Fact]
    public void SweepAllEnchants_EveryKindIsRepresented_AndStatsDecideBuffNerf()
    {
        var counts = new Dictionary<StatusKind, int>();
        foreach (var id in Defs.Keys)
        {
            var k = Kind(id);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        output.WriteLine(string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
        foreach (var k in Enum.GetValues<StatusKind>()) Assert.True(counts.GetValueOrDefault(k) > 0, k.ToString());
        // Un enchant à commandes 100 % instantanées n'est jamais un status.
        foreach (var d in Defs.Values)
        {
            var cmds = EnchantTable.CommandsOf(d);
            if (cmds.Count > 0 && cmds.All(c => StatusKinds.InstantCommands.Contains(c.Id)))
                Assert.Equal(StatusKind.None, Kind(d.Id));
        }
    }

    // ---- provenances d'une statistique (StatSources) ---------------------------

    [Fact]
    public void Sources_CertificateDefPercent_FromRealEnchant()
    {
        var acc = new List<StatSource>();
        StatSources.AddCommands(acc, "def", SourceGroup.Certificate, "Armure en Fer I",
            EnchantTable.CommandsOf(Defs[52507]));
        var s = Assert.Single(acc);
        Assert.Equal((SourceGroup.Certificate, 5.0, true), (s.Group, s.Value, s.Percent));
        Assert.Equal("+5 %", StatSources.Format(s));
    }

    [Fact]
    public void Sources_NativeSpeedSign_NegativeIsFaster()
    {
        var eff = StatSources.Effects(2052, new[] { -8.0, 0, 0, 0, 0, 0 });
        Assert.Contains(eff, e => e.Stat == "aspd" && e.Value == 8 && e.Percent);
        var cast = StatSources.Effects(2053, new[] { -5.0, 0, 0, 0, 0, 0 });
        Assert.Contains(cast, e => e.Stat == "cspd" && e.Value == 5);
    }

    [Fact]
    public void Sources_StackMultipliesBuffValue()
    {
        var acc = new List<StatSource>();
        StatSources.AddCommands(acc, "patk", SourceGroup.Buff, "Buff",
            new List<(int, double[])> { (2083, new[] { 4.0, 0, 0, 0, 0, 0 }) }, stacks: 3);
        Assert.Equal(12, Assert.Single(acc).Value);
    }

    [Fact]
    public void Sources_ResistCommands_UseElementOrder_IceBeforeLightning()
    {
        // Commande 2095 : arg i → élément natif 4+i (LicenseService
        // AddResist(4+i) — ordre c_item : fire, ICE, LIGHTNING,
        // nature, holy, dark). Les clés res_* doivent suivre cet ordre
        // élément, pas l'ordre d'affichage (lightning avant ice).
        var e = StatSources.Effects(2095,
            new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 });
        Assert.Equal(
            new[] { "res_fire", "res_ice", "res_lightning",
                    "res_nature", "res_holy", "res_dark" },
            e.Select(x => x.Stat).ToArray());
        // 2101 : arg i → élément 1+i (slash, strike, pierce).
        Assert.Equal(new[] { "res_slash", "res_strike", "res_pierce" },
            StatSources.Effects(2101, new[] { 7.0, 8.0, 9.0 })
                .Select(x => x.Stat).ToArray());
    }

    [Fact]
    public void Sources_ItemResists_IceBeforeLightning()
    {
        // c_item cols 63-71 : index 4 = col 67 « Ice Gem »,
        // index 5 = col 68 « Thunder Gem » (ItemTipFormat).
        var d = new ItemTable.ItemDef
        {
            Resistances = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
        };
        var acc = new List<StatSource>();
        StatSources.AddItem(acc, "res_ice", "gantelet", d);
        StatSources.AddItem(acc, "res_lightning", "gantelet", d);
        Assert.Equal(new[] { 5.0, 6.0 },
            acc.Select(s => s.Value).ToArray());
    }

    [Fact]
    public void EveryEnchantName_NeverContainsAnIdentifier()
    {
        // Libellé de repli générique : jamais « enchant #123 » à l'écran.
        Assert.DoesNotContain("#", EnchantTable.NameOf(-424242));
    }
}
