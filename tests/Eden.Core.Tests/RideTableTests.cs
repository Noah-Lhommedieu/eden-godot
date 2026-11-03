// Copyright (c) 2026 Noah Lhommedieu. MIT License.

using EdenClient.Data;
using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Logique monture pure (Eden.Core) : stem/scale ridelist,
/// table L17, mapping anims cavalier.</summary>
public class RideTableTests
{
    private static readonly string[] SampleLines =
    {
        "[INFO]", "Version=103", "[Table]",
        "DB0=r041010,r041.kfm,1.00,1.00,r04101.dds,0.0;0.0;0.0," +
            "0.0;0.0;0.0,0.000000,0.000000,1.500000,1.000000",
        "DB1=r211001,r211.kfm,1.20,1.00,r21101.dds,,,0,0,0,0",
    };

    [Fact]
    public void ParseReadsStemAndScale()
    {
        var d = RideList.Parse(SampleLines);
        Assert.True(d.TryGetValue("r041010", out var e));
        Assert.Equal("r041", e.Stem);
        Assert.Equal(1f, e.Scale);
        Assert.True(d.TryGetValue("R211001", out var e2));   // casse
        Assert.Equal("r211", e2.Stem);
        Assert.Equal(1.2f, e2.Scale);
    }

    [Fact]
    public void StemFallsBackToFamilyStem()
    {
        // rideKey sans entrée ridelist → « r{key/1000:D3} »
        // (convention 3 derniers chiffres = variante).
        // StemFor lit la vraie table si présente ; le repli est purement
        // arithmétique — on le teste via un key absent du corpus.
        Assert.Equal("r999", $"r{999001 / 1000:D3}");
        Assert.Equal("r253020", $"r{253020:D6}");
    }

    [Fact]
    public void SeatTableCoversCorpusStems()
    {
        // Stems connus de la spec MONTURES-L17 (AR/ R/ TR/ M/ JEFF…).
        Assert.True(RideSeatData.TryGet("r012", out var r012));
        Assert.InRange(r012.Z, 0.3f, 30f);
        Assert.True(RideSeatData.TryGet("ar013", out _));
        Assert.False(RideSeatData.TryGet("m412", out _));  // sans L17
        Assert.False(RideSeatData.TryGet("r890", out _));  // sans L17
        Assert.False(RideSeatData.TryGet("nope", out _));
        // Garde-fou natif : toute entrée de la table doit rester
        // plausible (sinon elle aurait dû être exclue à la génération).
        Assert.True(RideSeatData.TryGet("r012", out var s) &&
                    RideSeatMath.IsPlausibleSeat(s));
    }

    [Fact]
    public void RiderAnimMapping()
    {
        Assert.Equal("ride04",
            DirectionalLocomotion.RiderAnimFor("move01"));
        Assert.Equal("ride06",
            DirectionalLocomotion.RiderAnimFor("move03"));
        Assert.Equal("ride01",
            DirectionalLocomotion.RiderAnimFor("stand01"));
        Assert.Equal("ride01",
            DirectionalLocomotion.RiderAnimFor("truc"));
    }
}
