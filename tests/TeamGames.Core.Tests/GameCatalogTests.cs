using TeamGames.Core.Games;

namespace TeamGames.Core.Tests;

public sealed class GameCatalogTests
{
    private readonly GameCatalog catalog = new();

    [Theory]
    [InlineData("this-or-that", 1)]
    [InlineData("two-truths-and-a-lie", 2)]
    [InlineData("never-have-i-ever", 1)]
    [InlineData("would-you-rather", 1)]
    [InlineData("most-likely-to", 2)]
    public void EveryPlannedGameHasUsableDiscoveryInformation(string id, int minimumPlayers)
    {
        var game = Assert.IsType<GameSummary>(catalog.Find(id));
        Assert.Equal(minimumPlayers, game.MinimumPlayers);
        Assert.False(string.IsNullOrWhiteSpace(game.Instructions));
        Assert.False(string.IsNullOrWhiteSpace(game.Description));
        Assert.InRange(game.SuggestedMinutes, 1, 60);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("This-Or-That")]
    public void UnknownOrNonCanonicalIdentifiersDoNotSelectAGame(string id) => Assert.Null(catalog.Find(id));

    [Fact]
    public void GameRoutesAreUnique() => Assert.Equal(catalog.Games.Count, catalog.Games.Select(game => game.Id).Distinct().Count());
}
