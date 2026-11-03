using Xunit;
using Eden.World.Handlers;

namespace Eden.Tests;

/// <summary>
/// Tests du filtre LFG monde — sémantique
/// <c>CGlobalInfo::LoadLFGCharacters</c> @ world 0x8050ea0 :
/// [s16 node][s16 lvlMin][s16 lvlMax][s16 cls1][s16 cls2] ; node==0 =
/// toutes zones, niveau dans [min,max], classe : cls2==0 → branche
/// [cls1,cls1+100], sinon cls ∈ {cls1,cls2} ; tri par char_id.
/// </summary>
public sealed class LfgFilterTests : IDisposable
{
    public LfgFilterTests()
    {
        MiscHandlers.LfgCharacters[101] = new MiscHandlers.LfgEntry(
            101, "Alice", 40, 10101, 1, 5);    // classe 5 (branche guerrier)
        MiscHandlers.LfgCharacters[102] = new MiscHandlers.LfgEntry(
            102, "Bob", 55, 10201, 2, 105);    // classe 105 (autre branche)
        MiscHandlers.LfgCharacters[103] = new MiscHandlers.LfgEntry(
            103, "Carol", 60, 10101, 1, 8);
    }

    public void Dispose() => MiscHandlers.LfgCharacters.Clear();

    [Fact]
    public void LevelRangeOnly_AllMatching_Returned()
    {
        var r = MiscHandlers.FilterLfg(0, 30, 70, 0, 0).ToList();
        Assert.Equal(new[] { 101, 102, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void LevelRange_ExcludesOutOfRange()
    {
        var r = MiscHandlers.FilterLfg(0, 50, 70, 0, 0).ToList();
        Assert.Equal(new[] { 102, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void NodeFilter_MatchesExactNode()
    {
        var r = MiscHandlers.FilterLfg(10101, 0, 100, 0, 0).ToList();
        Assert.Equal(new[] { 101, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void SingleClass_ExpandsToBranchRange()
    {
        // cls1=5, cls2=0 → classes [5,105] : 5, 105 et 8 passent.
        var r = MiscHandlers.FilterLfg(0, 0, 100, 5, 0).ToList();
        Assert.Equal(new[] { 101, 102, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void SingleClass_ExcludesBelowBranchBase()
    {
        // cls1=100, cls2=0 → classes [100,200] : seul 105 passe.
        var r = MiscHandlers.FilterLfg(0, 0, 100, 100, 0).ToList();
        Assert.Equal(new[] { 102 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void TwoClasses_MatchesEitherExact()
    {
        // cls1=5, cls2=8 → match exact 5 ou 8.
        var r = MiscHandlers.FilterLfg(0, 0, 100, 5, 8).ToList();
        Assert.Equal(new[] { 101, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void SecondClassAlone_MatchesExact()
    {
        // cls1=0, cls2=105 → cls==0||cls==105 (natif) → seul 105.
        var r = MiscHandlers.FilterLfg(0, 0, 100, 0, 105).ToList();
        Assert.Equal(new[] { 102 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void CombinedNodeAndClass_BothApply()
    {
        var r = MiscHandlers.FilterLfg(10101, 0, 100, 5, 8).ToList();
        Assert.Equal(new[] { 101, 103 }, r.Select(e => e.CharId));
    }

    [Fact]
    public void Results_SortedByCharId()
    {
        MiscHandlers.LfgCharacters[50] = new MiscHandlers.LfgEntry(
            50, "Zed", 10, 1, 1, 1);
        var r = MiscHandlers.FilterLfg(0, 0, 100, 0, 0).ToList();
        Assert.Equal(r.Select(e => e.CharId).OrderBy(x => x),
            r.Select(e => e.CharId));
    }
}
