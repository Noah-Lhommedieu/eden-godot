using System.Buffers.Binary;
using Eden.Zone.Codecs;

namespace Eden.Tests;

/// <summary>
/// Tests de régression des builders familiers (elf pets) — formats
/// vérifiés sur les <c>Serialize</c> désassemblés du serveur natif :
/// 412 = [u16 n][n × 44 champs][s16], 513 = [u8 flag][s16 loc],
/// 418 = [s16 loc][s16 type][i32 valeur][str u16-len], 414 = [s16][s16].
/// </summary>
public sealed class ElfCodecTests
{
    /// <summary>Record 412 minimal reproductible — 1 elf nommé "Lap".</summary>
    private static NativeElfCodec.ElfInfoRecord Sample() => new()
    {
        Loc = 3,
        ElfId = 42,
        OwnerCharId = 777,
        TemplateId = 23993,
        Status = 2,
        Exp = 1234,
        Hp = 77,
        Quality = 5,
        Level = 9,
        Name = "Lap",
        Grow = { [0] = 1, [1] = 2, [2] = 3, [3] = 4 },
        MixStar = 2,
        Battle = { [0] = 58016 },
        Integrate = { [0] = 51001 },
    };

    /// <summary>Le 412 doit ouvrir sur le count u16 puis émettre 43
    /// champs par record et un s16 terminal — un elf vide produit
    /// exactement 2+2 octets (aucun record, jamais de record brut
    /// sans count — bug historique).</summary>
    [Fact]
    public void EnterNodeElfInfo_Empty_IsCountPlusTail()
    {
        var body = NativeElfCodec.BuildEnterNodeElfInfo(
            Array.Empty<NativeElfCodec.ElfInfoRecord>());
        Assert.Equal(4, body.Length);
        Assert.Equal(0, BinaryPrimitives
            .ReadUInt16LittleEndian(body));
    }

    /// <summary>Un record = 44 champs dont 1 str u16-len + 17 champs
    /// loterie/mix à zéro ; la queue s16 suit le dernier record.</summary>
    [Fact]
    public void EnterNodeElfInfo_OneRecord_FieldOrderAndTail()
    {
        var e = Sample();
        var body = NativeElfCodec.BuildEnterNodeElfInfo(new[] { e });
        // u16 count + loc s16 + 3 i32 + s16 + i32 + i32 + s16×3 +
        // str(u16+3) + s16×4 + s16 + s16×5×2 + s16×17 + s16 tail
        // (Serialize natif : 17 champs +0x158..+0x228 après integ —
        // un 16ᵉ seul = 2 octets trop courts = crash client).
        var expected = 2 + 2 + 12 + 2 + 8 + 6 + (2 + 3) + 8 + 2 + 20
            + 34 + 2;
        Assert.Equal(expected, body.Length);
        Assert.Equal(1, BinaryPrimitives
            .ReadUInt16LittleEndian(body));               // count
        Assert.Equal(3, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(2)));      // loc
        Assert.Equal(42, BinaryPrimitives
            .ReadInt32LittleEndian(body.AsSpan(4)));      // elf_id
        Assert.Equal(777, BinaryPrimitives
            .ReadInt32LittleEndian(body.AsSpan(8)));      // owner
        Assert.Equal(23993, BinaryPrimitives
            .ReadInt32LittleEndian(body.AsSpan(12)));     // template
        // Ordre natif corrigé (ctor @0x824d5f0) : pos5 = level,
        // pos8 = 0 (local_18 non init natif), pos9 = +0x58 (status),
        // pos10 = +0x24 (quality/growtype).
        Assert.Equal(9, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(16)));     // level
        Assert.Equal(0, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(26)));     // garbage
        Assert.Equal(2, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(28)));     // +0x58
        Assert.Equal(5, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(30)));     // quality
        Assert.Equal(0, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(body.Length - 2)));
    }

    /// <summary>413 : vecteurs à compteurs u16 — grow/battle/integrate
    /// doivent être préfixés (u16 n) puis n × s16.</summary>
    [Fact]
    public void ElfAdd_VectorsAreCounted()
    {
        var e = Sample();
        var body = NativeElfCodec.BuildElfAdd(e);
        var expected = 2 + 4 + 4 + 2 + 4 + 2 + (2 + 3) + 2 + 2 + 8
            + 2 + 2 + 10 + 2 + 10;
        Assert.Equal(expected, body.Length);
        Assert.Equal(3, BinaryPrimitives
            .ReadInt16LittleEndian(body));                // loc
        Assert.Equal(42, BinaryPrimitives
            .ReadInt32LittleEndian(body.AsSpan(2)));      // elf_id
    }

    /// <summary>513 PetShortCut : [u8 flag][s16 loc] — 1 invoqué,
    /// 0 renvoyé (CallElf/CallElfBack natifs).</summary>
    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)0)]
    public void PetShortCut_IsFlagPlusLoc(byte flag)
    {
        var body = NativeElfCodec.BuildPetShortCut(flag, 4);
        Assert.Equal(new byte[] { flag, 4, 0 }, body);
    }

    /// <summary>418 : [s16 loc][s16 type][i32 valeur][str] — CallElf
    /// émet type=2 avec le HP courant et une chaîne vide (u16=0).</summary>
    [Fact]
    public void ElfUpdateAttr_HpPush_HasEmptyString()
    {
        var body = NativeElfCodec.BuildElfUpdateAttr(2, 2, 100);
        Assert.Equal(10, body.Length);
        Assert.Equal(2, BinaryPrimitives
            .ReadInt16LittleEndian(body));                // loc
        Assert.Equal(2, BinaryPrimitives
            .ReadInt16LittleEndian(body.AsSpan(2)));      // type
        Assert.Equal(100, BinaryPrimitives
            .ReadInt32LittleEndian(body.AsSpan(4)));      // valeur
        Assert.Equal(0, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(8)));     // str vide
    }

    /// <summary>414 ElfReturnOK : [s16 loc][s16 flag] — 4 octets.</summary>
    [Fact]
    public void ElfReturnOK_IsLocPlusFlag()
    {
        Assert.Equal(new byte[] { 5, 0, 0, 0 },
            NativeElfCodec.BuildElfReturnOK(5));
    }

    /// <summary>588 ClientUpdateElfSpell : `[s16 loc][u16 n]
    /// [n×s16 battle][u16 n][n×s16 integrate]` — format variable
    /// vérifié sur le ctor natif @0x8455b30 dans LearnSpell (les
    /// vecteurs s16 sont copiés par <c>vector::operator=</c>, sérialisés
    /// count-prefixed).</summary>
    [Fact]
    public void UpdateElfSpell_IsLocPlusTwoVectors()
    {
        var body = NativeElfCodec.BuildUpdateElfSpell(
            3, new[] { 58016, 58017 }, new[] { 51001 });
        // s16 loc + u16 n + 2×s16 + u16 n + s16 = 2+2+4+2+2 = 12.
        Assert.Equal(12, body.Length);
        Assert.Equal(3, BinaryPrimitives
            .ReadInt16LittleEndian(body));                // loc
        Assert.Equal(2, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(2)));     // n battle
        // Ids de sorts elf >32767 : le natif écrit des shorts signés
        // (58659 → -6877) — octets identiques en lecture u16, c'est
        // la sémantique côté client.
        Assert.Equal(58016, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(4)));
        Assert.Equal(58017, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(6)));
        Assert.Equal(1, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(8)));     // n integrate
        Assert.Equal(51001, BinaryPrimitives
            .ReadUInt16LittleEndian(body.AsSpan(10)));
    }

    /// <summary>Les vecteurs vides produisent `[loc][u16 0][u16 0]` —
    /// jamais de fields positionnels fixes (la classe générée aplatie
    /// à 5 shorts n'est PAS le format natif).</summary>
    [Fact]
    public void UpdateElfSpell_EmptyVectors_AreZeroCounts()
    {
        var body = NativeElfCodec.BuildUpdateElfSpell(
            1, Array.Empty<int>(), Array.Empty<int>());
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0 }, body);
    }

    /// <summary>Données réelles : l'item 873 (技能書(三)．連擊)
    /// porte UseType 6 et l'enchant 61539 qui décode en commande 6035
    /// ChangePetBattleSpell avec les 6 vars positionnelles natives
    /// [slot=3, mode=1, spell=0, min=58659, max=58663, needStar=4] —
    /// mode 1 = tirage dans la plage (preuve : Execute @0x851b060 →
    /// LearnSpell(vec=0, slot-1, mode, spell, min, max)).</summary>
    [Fact]
    public void PetBookEnchant_CommandsDecodeNativeArgOrder()
    {
        var gd = new Eden.Core.Game.Services.GameDataService(
            TestPaths.RepoRoot + "/server_us/Data/db");
        gd.LoadAll();
        var es = new Eden.Core.Game.Services.EnchantService(gd.Enchants);
        var book = gd.Items.FirstOrDefault(i => i.Id == 873);
        Assert.NotNull(book);
        Assert.Equal(6, book!.UseType);
        Assert.Equal(61539, book.UseEnchantId);
        var cmds = es.GetUseCommands(book.UseEnchantId);
        var cmd = Assert.Single(cmds, c => c.CommandId == 6035);
        Assert.Equal(new double[] { 3, 1, 0, 58659, 58663, 4 },
            cmd.Args);
    }

    /// <summary>ElfPetEnchantGroupMap native (S_Enchant col6) : les
    /// groupes intégrés 6-11 (祈願 I-III) résolvent des ids
    /// d'enchant — les livres 6036/6038 stockent ces ids dans les
    /// slots integrate_spell_*.</summary>
    [Fact]
    public void ElfEnchantGroups_IntegrateGroupsResolve()
    {
        var gd = new Eden.Core.Game.Services.GameDataService(
            TestPaths.RepoRoot + "/server_us/Data/db");
        gd.LoadAll();
        var es = new Eden.Core.Game.Services.EnchantService(gd.Enchants);
        for (var g = 6; g <= 11; g++)
            Assert.NotEmpty(es.GetElfEnchantGroup(g));
    }

    /// <summary>ElfPetSpellGroupMap native (S_Spell col34, famille 8)
    /// : le groupe test 999 porte les sorts battle 58668-58698.</summary>
    [Fact]
    public void ElfSpellGroup_SpellParserReadsCol34()
    {
        var gd = new Eden.Core.Game.Services.GameDataService(
            TestPaths.RepoRoot + "/server_us/Data/db");
        gd.LoadAll();
        var group999 = gd.Spells
            .Where(s => s.Family == 8 && s.ElfGroup == 999)
            .Select(s => s.Id).ToList();
        Assert.Contains(58668, group999);
    }
}
