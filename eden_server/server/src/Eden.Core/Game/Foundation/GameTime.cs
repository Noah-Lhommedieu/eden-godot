namespace Eden.Core.Game.Foundation;

/// <summary>Horloge de jeu partagée par les services (reset 06:00) ; les tests y posent une <see cref="FakeGameClock"/>.</summary>
public static class GameTime
{
    public static IGameClock Clock { get; set; } = new SystemGameClock();
}
