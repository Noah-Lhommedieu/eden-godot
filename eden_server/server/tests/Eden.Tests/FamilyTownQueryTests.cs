using Eden.Core.Data.Parsers;
using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests des codecs dédiés et de la sélection de la recherche de
/// ville de famille — formats prouvés par désassemblage :
/// <list type="bullet">
/// <item>502 <c>CNC_CZ_ClientFamilyTownQuery</c> — 17 vecteurs
/// parallèles <c>[u16 count][elems]</c> (ctor @zone:0x8148b90 +
/// Serialize zone ; la classe générée aplatit le format).</item>
/// <item>503 <c>CNC_CZ_ClientFamilyTownMemberQuery</c> — 6 vecteurs
/// (<c>TC_FamilyTownMemberQuery</c> mission @0x820a670).</item>
/// <item>Filtres 174 — <c>CPlayerFactory::FamilyTownQuery</c>
/// zone @0x8110830 : gate conteneur 0, niveau inclusif, sous-chaîne
/// de nom d'item NPC (Ins+0x490 / InsertTownNpcMap @0x8178260),
/// masque = GetBuildingValue @0x8464870.</item>
/// </list>
/// </summary>
public class FamilyTownQueryTests
{
    private static FamilyPackets.FamilyTownQueryEntry Entry(
        int id = 7, params FamilyBuildingItem[] buildings) => new(
        id, Level: 5, Name: "Fam", LeaderName: "Chef",
        Exp: 2500, BusinessTax: 7, MemberCount: 12, HouseOpen: 501,
        Buildings: buildings);

    private static FamilyBuildingItem Bld(short loc = 1,
        short itemTid = 5010, int tid = 101, short itemType = 0x29)
        => new()
        {
            ItemTid = itemTid, Tid = tid, ItemType = itemType,
            Durability = 100, ContainerIndex = 0, Loc = loc,
            Level = 2, Exp = 40, DueDate = 1700000000,
        };

    // ---------- 502 — CNC_CZ_ClientFamilyTownQuery ----------

    /// <summary>Paquet vide = 17 compteurs u16 à zéro (34 octets) —
    /// le paquet final est TOUJOURS émis, même vide.</summary>
    [Fact]
    public void TownQuery_Empty_Is17ZeroCounts()
    {
        var b = FamilyPackets.FamilyTownQuery([]);
        Assert.Equal(34, b.Length);
        Assert.All(b, x => Assert.Equal(0, x));
    }

    /// <summary>Une famille + 2 bâtiments : relecture champ par champ
    /// dans l'ordre des 17 vecteurs natifs.</summary>
    [Fact]
    public void TownQuery_Vectors_InNativeOrder()
    {
        var b = FamilyPackets.FamilyTownQuery([Entry(7,
            Bld(loc: 3, itemTid: 5001, tid: 101),
            Bld(loc: 9, itemTid: unchecked((short)0x8001), tid: 102))]);
        var r = new InBinStream(b);
        // vec0 i32 ids / vec1 s16 niveaux / vec2 str noms
        Assert.Equal(1, r.ReadShort()); Assert.Equal(7, r.ReadInt());
        Assert.Equal(1, r.ReadShort()); Assert.Equal(5, r.ReadShort());
        Assert.Equal(1, r.ReadShort()); Assert.Equal("Fam", r.ReadString());
        // vec3 str chefs / vec4 u32 exp / vec5 s16 business_tax
        Assert.Equal(1, r.ReadShort()); Assert.Equal("Chef", r.ReadString());
        Assert.Equal(1, r.ReadShort()); Assert.Equal(2500, r.ReadInt());
        Assert.Equal(1, r.ReadShort()); Assert.Equal(7, r.ReadShort());
        // vec6 u32 membres / vec7 s16 house_open / vec8 u32 nb bât.
        Assert.Equal(1, r.ReadShort()); Assert.Equal(12, r.ReadInt());
        Assert.Equal(1, r.ReadShort()); Assert.Equal(501, r.ReadShort());
        Assert.Equal(1, r.ReadShort()); Assert.Equal(2, r.ReadInt());
        // vec9 s16 locs — ordre des items tel que passé (map par loc)
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(3, r.ReadShort()); Assert.Equal(9, r.ReadShort());
        // vec10 i32 item_tids — élargissement NON signé (+0x84 u16)
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(5001, r.ReadInt()); Assert.Equal(0x8001, r.ReadInt());
        // vec11 i32 tids / vec12 s16 item_types / vec13 s16 durabilités
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(101, r.ReadInt()); Assert.Equal(102, r.ReadInt());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(0x29, r.ReadShort()); Assert.Equal(0x29, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(100, r.ReadShort()); Assert.Equal(100, r.ReadShort());
        // vec14 s16 niveaux / vec15 u32 exps / vec16 u32 due_dates
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(2, r.ReadShort()); Assert.Equal(2, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(40, r.ReadInt()); Assert.Equal(40, r.ReadInt());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(1700000000, r.ReadInt());
        Assert.Equal(1700000000, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    // ---------- 503 — CNC_CZ_ClientFamilyTownMemberQuery ----------

    /// <summary>6 vecteurs natifs : ids i32, noms str, niveaux s16,
    /// rangs u8, node_ids s16, puis les titres DE LA FAMILLE (pas
    /// par membre — copie entière de fam+0x124).</summary>
    [Fact]
    public void MemberQuery_Vectors_InNativeOrder()
    {
        var members = new[]
        {
            new FamilyPackets.FamilyTownMemberEntry(
                42, "Chef", 60, 1, 501),
            new FamilyPackets.FamilyTownMemberEntry(
                43, "Membre", 30, 0, 0),
        };
        var titles = Enumerable.Repeat("T", 9).ToArray();
        var r = new InBinStream(
            FamilyPackets.FamilyTownMemberQuery(members, titles));
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(42, r.ReadInt()); Assert.Equal(43, r.ReadInt());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal("Chef", r.ReadString());
        Assert.Equal("Membre", r.ReadString());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(60, r.ReadShort()); Assert.Equal(30, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(1, r.ReadByte()); Assert.Equal(0, r.ReadByte());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(501, r.ReadShort()); Assert.Equal(0, r.ReadShort());
        // vec5 = titres famille — 9 entrées même avec 2 membres.
        Assert.Equal(9, r.ReadShort());
        for (var i = 0; i < 9; i++) Assert.Equal("T", r.ReadString());
        Assert.Equal(0, r.Remaining);
    }

    /// <summary>Famille absente côté mission = relais jamais émis ;
    /// le codec gère quand même le cas vide proprement.</summary>
    [Fact]
    public void MemberQuery_Empty_Is6Counts()
    {
        var b = FamilyPackets.FamilyTownMemberQuery([], []);
        Assert.Equal(12, b.Length);
        Assert.All(b, x => Assert.Equal(0, x));
    }

    // ---------- Filtres 174 — CPlayerFactory::FamilyTownQuery ----------

    private static GuildService.Guild Fam(int id, int level,
        short houseOpen = 501) => new()
    {
        Id = id, Name = $"Fam{id}", Level = level,
        LeaderCharacterId = 1000 + id, LeaderName = "Chef",
        Experience = 100, BusinessTax = 3, HouseOpen = houseOpen,
    };

    private static readonly ItemParser.ItemDef NpcItem = new()
        { Id = 5001, Name = "Guild Steward", Kind = 0x2d };
    private static readonly ItemParser.ItemDef[] Items = [NpcItem];
    private static readonly Dictionary<int, short> BldTypes =
        new() { [101] = 4, [102] = 7 }; // ferme→bit1, autel→bit8

    /// <summary>Gate du ctor 502 : famille candidate mais SANS
    /// conteneur 0 (fam+0x7c sans clé 0) → exclue des résultats.</summary>
    [Fact]
    public void Select_FamilyWithoutContainer0_Excluded()
    {
        var r = ZoneEntryFlow.SelectTownQueryEntries(
            [Fam(7, 5)],
            new Dictionary<int, List<FamilyBuildingItem>>(),
            Items, BldTypes, 0, 100, 0, "");
        Assert.Empty(r);
    }

    /// <summary>Niveau INCLUSIF [min,max] sur (char)fam+0x18.</summary>
    [Fact]
    public void Select_LevelBounds_Inclusive()
    {
        var fams = new[] { Fam(1, 5), Fam(2, 10), Fam(3, 15) };
        var blds = fams.ToDictionary(f => f.Id,
            _ => new List<FamilyBuildingItem> { Bld() });
        var r = ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 5, 10, 0, "");
        Assert.Equal([1, 2], r.Select(e => e.FamilyId));
    }

    /// <summary>Filtre nom = sous-chaîne (string::find) sur les noms
    /// des items NPC item_type 0x2a du conteneur 0 ; les items dont
    /// le subtype vaut 0x16 sont exclus de l'index natif.</summary>
    [Fact]
    public void Select_NameFilter_NpcItemSubstring()
    {
        var npcBld = Bld(loc: 4, itemTid: 5001, itemType: 0x2a);
        var fams = new[] { Fam(1, 5), Fam(2, 5) };
        var blds = new Dictionary<int, List<FamilyBuildingItem>>
        {
            [1] = [npcBld, Bld()], [2] = [Bld()],
        };
        Assert.Equal([1], ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 0, 100, 0, "Steward")
            .Select(e => e.FamilyId));
        // Un item 0x2a subtype 0x16 n'est PAS indexé (InsertTownNpcMap).
        var excluded = new[] { new ItemParser.ItemDef
            { Id = 5002, Name = "Steward X", Kind = 0x16 } };
        var b2 = new Dictionary<int, List<FamilyBuildingItem>>
        {
            [2] = [Bld(loc: 4, itemTid: 5002, itemType: 0x2a)],
        };
        Assert.Empty(ZoneEntryFlow.SelectTownQueryEntries(
            [Fam(2, 5)], b2, excluded, BldTypes, 0, 100, 0, "Steward"));
    }

    /// <summary>Masque bâtiment : mask≠0 exige un item_type 0x29 dont
    /// le type S_Buliding & mask ≠ 0 (GetBuildingValue : 4→1, 7→8).</summary>
    [Fact]
    public void Select_BuildingMask_GetBuildingValueBits()
    {
        var fams = new[] { Fam(1, 5), Fam(2, 5) };
        var blds = new Dictionary<int, List<FamilyBuildingItem>>
        {
            [1] = [Bld(tid: 101)],           // type 4 → bit 1
            [2] = [Bld(tid: 102)],           // type 7 → bit 8
        };
        // mask 0 → tout passe ; mask 1 → fam 1 ; mask 8 → fam 2 ;
        // mask 4 (alchimie) → personne.
        Assert.Equal([1, 2], ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 0, 100, 0, "")
            .Select(e => e.FamilyId));
        Assert.Equal([1], ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 0, 100, 1, "")
            .Select(e => e.FamilyId));
        Assert.Equal([2], ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 0, 100, 8, "")
            .Select(e => e.FamilyId));
        Assert.Empty(ZoneEntryFlow.SelectTownQueryEntries(
            fams, blds, Items, BldTypes, 0, 100, 4, ""));
    }
}
