using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Quête terminée ≠ en cours (bug « Nv. 1 Votre première Quête »
/// visible alors que faite : slot mission_trace périmé).</summary>
public class QuestStateLogicTests
{
    // État réel test123 : trace=[1009], FMS {1010:1, 5xx:5}, 1009 absent.
    private static readonly Dictionary<int, int> Fms = new()
        { [1010] = 1, [1295] = 5, [1012] = 3, [1013] = 4 };

    [Fact]
    public void StaleTraceOfFinishedQuestIsHidden()
    {
        var v = QuestStateLogic.Visible(new[] { 1009, 0, 0 },
            Array.Empty<int>(), Fms);
        Assert.Empty(v);
    }

    [Theory]
    [InlineData(1010)] // faite (FMS 1)
    [InlineData(1295)] // checked (FMS 5)
    [InlineData(1009)] // absente / reset (FMS 0)
    public void NonOngoingStatesNeverShown(int mid) =>
        Assert.Empty(QuestStateLogic.Visible(new[] { mid },
            Array.Empty<int>(), Fms));

    [Fact]
    public void OngoingQuestsShownTraceFirst()
    {
        Assert.Equal(new[] { 1013 },
            QuestStateLogic.Visible(new[] { 1013, 1010, 1009 },
                new[] { 1012, 1013 }, Fms));
    }

    [Fact]
    public void FallbackListsOngoingOnly()
    {
        var v = QuestStateLogic.Visible(new[] { 1009 },
            new[] { 1012, 1013, 1010 }, Fms);
        Assert.Equal(new[] { 1012, 1013 }, v.OrderBy(x => x));
    }

    [Fact]
    public void InProgressWithoutRegistryEntryIsLive() =>
        Assert.Equal(new[] { 77 }, QuestStateLogic.Visible(
            Array.Empty<int>(), new[] { 77 }, new Dictionary<int, int>()));
}
