using System.Text.Json.Nodes;
using TeamGames.Core.Games;
using TeamGames.Core.Prompts;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Tests;

public sealed class PromptRulesTests
{
    private static PromptInput Input => new("this-or-that", "Food", ["relaxed"], "Choose", "Tea", "Coffee");
    private sealed class First : IGameRandom { public int Next(int count) => 0; }
    [Fact]
    public void LaunchLibraryHasValidStableUniqueShapesCountsAndCategories()
    {
        Assert.Equal(350, PromptLibrary.All.Count);
        Assert.Equal(200, PromptLibrary.All.Count(p => p.GameId == "this-or-that"));
        Assert.Equal(8, PromptLibrary.All.Where(p => p.GameId == "this-or-that").Select(p => p.Category).Distinct().Count());
        Assert.All(PromptRules.GameIds.Skip(1), id => Assert.Equal(50, PromptLibrary.All.Count(p => p.GameId == id)));
        Assert.Equal(350, PromptLibrary.All.Select(p => p.NormalizedKey).Distinct().Count());
        Assert.Equal("Mountains", PromptLibrary.All.Single(p => p.Id == "tot-001").OptionA);
        Assert.Equal("Sweet snack", PromptLibrary.All.Single(p => p.Id == "tot-012").OptionA);
    }
    [Fact]
    public void NormalizedDuplicatesIncludeReversedPairsAndUnicodeWhitespace()
    {
        var first = PromptRules.Create(Input, "one", null);
        var reversed = PromptRules.Create(Input with { OptionA = "  Ｃoffee  ", OptionB = "Ｔea" }, "two", null);
        Assert.Equal(first.NormalizedKey, reversed.NormalizedKey);
        Assert.True(PromptRules.SeedConflicts(reversed, [first]));
        Assert.False(PromptRules.SeedConflicts(first, [first]));
        Assert.Equal("A B", PromptRules.Normalize(" a   b "));
    }
    [Fact]
    public void SeedGuardRecognizesManualChangesAndDisabling()
    {
        var prompt = PromptRules.Create(Input, "id", null);
        prompt.SeedFingerprint = PromptRules.Fingerprint(prompt);
        Assert.True(PromptRules.CanUpdateSeed(prompt));
        prompt.Enabled = false;
        Assert.False(PromptRules.CanUpdateSeed(prompt));
        prompt.Enabled = true;
        prompt.Source = "administrator";
        Assert.False(PromptRules.CanUpdateSeed(prompt));
        prompt.Source = "built-in";
        prompt.Text = "An editorial change";
        Assert.False(PromptRules.CanUpdateSeed(prompt));
    }
    [Theory]
    [InlineData("game")]
    [InlineData("blank")]
    [InlineData("same")]
    [InlineData("long")]
    [InlineData("controls")]
    [InlineData("shape")]
    [InlineData("tags")]
    public void InvalidPromptContentFailsClearly(string kind)
    {
        var input = kind switch
        {
            "game" => Input with { GameId = "missing" },
            "blank" => Input with { OptionA = " " },
            "same" => Input with { OptionB = "tea" },
            "long" => Input with { Text = new string('x', 241) },
            "controls" => Input with { Category = "a\nb" },
            "shape" => Input with { GameId = "never-have-i-ever" },
            _ => Input with { Tags = ["tag", "TAG"] }
        };
        Assert.Equal("invalid_prompt", Assert.Throws<RoomException>(() => PromptRules.Create(input, "id", null)).Code);
    }
    [Fact]
    public void SearchCategoryAndTagFiltersComposeAndDisabledPromptsAreExcluded()
    {
        var prompt = PromptRules.Create(Input, "id", null);
        Assert.True(PromptRules.Matches(prompt, "this-or-that", "food", "RELAXED", "coffee"));
        Assert.False(PromptRules.Matches(prompt, "this-or-that", "Travel", null, null));
        Assert.False(PromptRules.Matches(prompt, "this-or-that", null, "other", null));
        Assert.False(PromptRules.Matches(prompt, "would-you-rather", null, null, null));
        prompt.Enabled = false;
        Assert.False(PromptRules.Matches(prompt, "this-or-that", null, null, null));
    }
    [Fact]
    public void SelectionSnapshotsTextAvoidsRepeatsAndRequiresExplicitExhaustionReset()
    {
        var room = new Room { Id = Guid.NewGuid(), Code = "ABC234" };
        var host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20);
        room.FacilitatorId = host.Id;
        var prompt = PromptRules.Create(Input, "custom", room.Id);
        Assert.Equal(room.Id, prompt.RoomId);
        Assert.Equal("room", prompt.Source);
        room.PromptPool = [prompt];
        var games = new GameModules([new ThisOrThat(new First())]);
        games.Apply(room, host, new(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: 1, PromptId: "custom", Category: "Food"), 10);
        var view = games.Project(room, host)!;
        prompt.OptionA = "Changed";
        Assert.Equal("Tea", games.Project(room, host)!.OptionA);
        GameCommand Command(string action) => new(Guid.NewGuid(), action, view.SessionId, view.RoundId, view.StageRevision);
        games.Apply(room, host, Command("reveal"), 20);
        view = games.Project(room, host)!;
        Assert.Equal(0, view.RemainingPrompts);
        Assert.Contains("reset", view.AllowedActions);
        Assert.Throws<RoomException>(() => games.Apply(room, host, Command("next"), 20));
        games.Apply(room, host, Command("reset"), 30);
        view = games.Project(room, host)!;
        Assert.Equal(2, view.RoundNumber);
        Assert.Equal("Changed", view.OptionA);
        Assert.Equal("Tea", view.Results[0].OptionA);
        Assert.DoesNotContain(games.Project(room, host)!.Results[0].Choices, c => c.Choice is "a" or "b");
    }
    [Fact]
    public void M2IndexUsageMigratesToStablePromptIdsWithoutRepeating()
    {
        var room = new Room { Id = Guid.NewGuid(), Code = "ABC234", PromptPool = PromptLibrary.All };
        var host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20);
        room.FacilitatorId = host.Id;
        var module = new ThisOrThat(new First());
        var state = JsonNode.Parse(module.Start(room, new(Guid.NewGuid(), "start")))!;
        state.AsObject().Remove("UsedPromptIds"); state.AsObject().Remove("RoundNumber");
        state["Used"] = new JsonArray(0, 1, 2);
        var session = new GameSession { Id = Guid.NewGuid(), GameId = module.Id, State = state.ToJsonString() };
        var view = module.Project(session, room, host);
        Assert.Equal(3, view.RoundNumber);
        Assert.Contains("tot-001", view.UsedPromptIds!);
        Assert.Equal(197, view.RemainingPrompts);
    }
}
