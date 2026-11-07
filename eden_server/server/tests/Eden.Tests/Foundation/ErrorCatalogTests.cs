using System.Reflection;
using Eden.Core.Game.Foundation;

namespace Eden.Tests.Foundation;

public class ErrorCatalogTests
{
    private static readonly ErrorCatalog Catalog = ErrorCatalog.LoadEmbedded();

    [Fact]
    public void EveryGameErrorsConstant_HasFrenchText()
    {
        var codes = typeof(GameErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        foreach (var code in codes)
        {
            Assert.True(Catalog.Has(code), $"texte FR manquant pour {code}");
            var fr = Catalog.Format(GameError.Of(code));
            Assert.NotEqual(code, fr);
            Assert.False(string.IsNullOrWhiteSpace(fr));
        }
    }

    [Fact]
    public void EveryCatalogCode_HasConstant() =>
        Assert.All(Catalog.Codes, c => Assert.Contains(c,
            typeof(GameErrors).GetFields().Select(f => f.GetRawConstantValue())));

    [Fact]
    public void English_FallsBackToFrench_WhenMissing()
    {
        var cat = new ErrorCatalog(new[] { "X_ONLY_FR\tBonjour" });
        Assert.Equal("Bonjour", cat.Format(GameError.Of("X_ONLY_FR"), GameLanguage.En));
    }

    [Fact]
    public void Args_AreSubstituted() =>
        Assert.Equal("Valeur de configuration invalide : Foo.",
            Catalog.Format(GameError.Of(GameErrors.ConfigInvalidValue, "Foo")));

    [Fact]
    public void UnknownCode_ReturnsCode() =>
        Assert.Equal("NOPE", Catalog.Format(GameError.Of("NOPE")));

    [Fact]
    public void Result_OkAndFail()
    {
        var ok = Result<int>.Ok(3);
        Assert.True(ok.IsOk); Assert.Equal(3, ok.Value);
        var ko = Result<int>.Fail(GameErrors.GatherOutOfArea);
        Assert.False(ko.IsOk);
        Assert.Equal(GameErrors.GatherOutOfArea, ko.Error.Code);
        Assert.Throws<InvalidOperationException>(() => ko.Value);
    }
}
