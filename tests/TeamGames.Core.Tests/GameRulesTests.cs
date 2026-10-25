using TeamGames.Core.Games;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Tests;

public sealed class GameRulesTests
{
    private sealed class First : IGameRandom { public int Next(int count) => 0; }
    private readonly GameModules games = new([new ThisOrThat(new First())]);
    private readonly Room room = new() { Id = Guid.NewGuid(), Code = "ABC234" };
    private readonly Membership host;
    private readonly Membership player;
    public GameRulesTests()
    {
        room.PromptPool = TeamGames.Core.Prompts.PromptLibrary.All.Take(12).ToArray();
        host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20);
        player = RoomRules.Join(room, Guid.NewGuid(), "Player", 20);
        room.FacilitatorId = host.Id;
    }
    private GameView View(Membership? viewer = null) => games.Project(room, viewer ?? host)!;
    private void Start() => games.Apply(room, host, new(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version), 10);
    private GameCommand Command(string action, Membership? actor = null, string? choice = null)
    {
        var view = View(actor);
        return new(Guid.NewGuid(), action, view.SessionId, view.RoundId, view.StageRevision, view.ResponseRevision, choice);
    }
    private void Apply(string action, Membership? actor = null, string? choice = null) => games.Apply(room, actor ?? host, Command(action, actor, choice), 20);

    [Fact]
    public void StartFreezesRosterAndProjectsOnlyTheViewersChoice()
    {
        Start();
        Apply("answer", player, "b");
        Assert.Null(View().OwnChoice);
        Assert.Equal("b", View(player).OwnChoice);
        Assert.Empty(View().Results);
        Assert.Equal(1, View().ResponseCount);
        Assert.Equal(2, View().EligibleCount);
        Assert.DoesNotContain("reveal", View(player).AllowedActions);
    }
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("pass")]
    public void ResponseStatusRevealsParticipationOnlyAndResetsWithTheFrozenRoster(string choice)
    {
        Start();
        Assert.All(View().Responses!, response => Assert.False(response.HasResponded));
        Apply("answer", player, choice);
        var late = RoomRules.Join(room, Guid.NewGuid(), "Late", 20);
        Assert.Equal(new ResponseStatusView(player.Id, true), View().Responses!.Single(r => r.ParticipantId == player.Id));
        Assert.Equal(View().Responses, View(player).Responses);
        Assert.Null(View().OwnChoice);
        Assert.DoesNotContain(View(late).Responses!, r => r.ParticipantId == late.Id);
        Apply("answer", player, choice == "pass" ? "a" : "pass");
        Assert.Single(View().Responses!, r => r.HasResponded);
        player.Removed = true;
        Assert.Single(View().Responses!);
        Assert.Equal(0, View().ResponseCount);
        Apply("reveal");
        Apply("next");
        Assert.Contains(View().Responses!, r => r.ParticipantId == late.Id);
        Assert.All(View().Responses!, response => Assert.False(response.HasResponded));
    }
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("pass")]
    public void ResponsesCanBeChangedWithoutAdvancingTheStage(string choice)
    {
        Start();
        var revision = View().StageRevision;
        Apply("answer", player, "a");
        Apply("answer", player, choice);
        Assert.Equal(choice, View(player).OwnChoice);
        Assert.Equal(2, View(player).ResponseRevision);
        Assert.Equal(revision, View().StageRevision);
        Assert.Equal(1, View().ResponseCount);
        Apply("reveal");
        var result = Assert.Single(View().Results);
        Assert.Equal(choice == "pass" ? 1 : 0, result.Passes);
        Assert.Equal(1, result.Missing);
        Assert.Equal(choice, result.Choices!.Single(c => c.ParticipantId == player.Id).Choice);
        Assert.Equal("Player", result.Choices!.Single(c => c.ParticipantId == player.Id).Name);
        Assert.Equal("missing", result.Choices!.Single(c => c.ParticipantId == host.Id).Choice);
        Assert.Equal(choice == "a" ? 100 : 0, result.PercentA);
        Assert.Equal(choice == "b" ? 100 : 0, result.PercentB);
    }
    [Fact]
    public void ConcurrentPlayersUseSeparateResponseRevisionsButDuplicateTabsConflict()
    {
        Start();
        var hostAnswer = Command("answer", host, "a");
        var playerAnswer = Command("answer", player, "b");
        games.Apply(room, host, hostAnswer, 20);
        games.Apply(room, player, playerAnswer, 20);
        Assert.Equal(2, View().ResponseCount);
        Assert.Equal("response_changed", Assert.Throws<RoomException>(() => games.Apply(room, player, playerAnswer, 20)).Code);
        Apply("reveal");
        Assert.Equal(50, Assert.Single(View().Results).PercentA);
    }
    [Theory]
    [InlineData("reveal")]
    [InlineData("skip")]
    [InlineData("finish")]
    public void FacilitatorCanMoveOnWithZeroAnswersAndStaleAnswersFail(string action)
    {
        Start();
        var stale = Command("answer", player, "a");
        Apply(action);
        Assert.Equal("stage_changed", Assert.Throws<RoomException>(() => games.Apply(room, player, stale, 20)).Code);
        var result = Assert.Single(View().Results);
        Assert.Equal(0, result.A + result.B + result.Passes);
        Assert.Equal(2, result.Missing);
        Assert.Equal(0, result.PercentA + result.PercentB);
    }
    [Fact]
    public void LateJoinWaitsUntilNextRoundAndRemovalDoesNotChangeFinalizedResults()
    {
        Start();
        var late = RoomRules.Join(room, Guid.NewGuid(), "Late", 20);
        Assert.False(View(late).Eligible);
        Assert.Throws<RoomException>(() => Apply("answer", late, "a"));
        Apply("answer", player, "a");
        player.Removed = true;
        Apply("reveal");
        var finalized = Assert.Single(View().Results);
        Assert.Equal(0, finalized.A);
        host.Removed = true;
        var preserved = Assert.Single(View().Results);
        Assert.Equal(finalized.A, preserved.A);
        Assert.Equal(finalized.Missing, preserved.Missing);
        Assert.All(preserved.Choices!, c => Assert.Equal("Removed player", c.Name));
        host.Removed = false;
        Apply("next");
        Assert.True(View(late).Eligible);
        Assert.Equal(2, View().RoundNumber);
        Assert.Equal(0, View().ResponseCount);
    }
    [Fact]
    public void FinishSkipsUnrevealedChoicesAndReturnToLobbyAllowsANewSession()
    {
        Start();
        var first = View().SessionId;
        Apply("answer", player, "a");
        Apply("finish");
        Assert.True(Assert.Single(View().Results).Skipped);
        Assert.Empty(View().Results[0].Choices!);
        Assert.Equal(0, View().Results[0].A);
        Apply("lobby");
        Assert.Equal("lobby", room.Status);
        Assert.Null(games.Project(room, host));
        Start();
        Assert.NotEqual(first, View().SessionId);
        Assert.Contains(first.ToString(), room.GameState!); // Previous sessions remain durable when starting another game.
    }
    [Fact]
    public void SeedDoesNotRepeatAndExhaustionStillAllowsFinishing()
    {
        Start();
        var options = new HashSet<string>();
        for (var i = 0; i < 12; i++)
        {
            Assert.True(options.Add(View().OptionA));
            Apply("skip");
            if (i < 11) Apply("next");
        }
        Assert.DoesNotContain("next", View().AllowedActions);
        Assert.Throws<RoomException>(() => Apply("next"));
        Apply("finish");
        Assert.Equal(12, View().Results.Count);
    }
    [Theory]
    [InlineData("reveal")]
    [InlineData("skip")]
    [InlineData("next")]
    [InlineData("finish")]
    [InlineData("lobby")]
    public void PlayersCannotControlStages(string action)
    {
        Start();
        Assert.Equal(403, Assert.Throws<RoomException>(() => Apply(action, player)).Status);
    }
    [Fact]
    public void InvalidChoiceStageSessionAndGameAreRejected()
    {
        Assert.Throws<RoomException>(() => games.Apply(room, player, new(Guid.NewGuid(), "start", GameId: "this-or-that"), 0));
        Assert.Throws<RoomException>(() => games.Apply(room, host, new(Guid.NewGuid(), "start", GameId: "missing", ExpectedVersion: room.Version), 0));
        Start();
        Assert.Throws<RoomException>(() => Apply("answer", player, "secret"));
        Assert.Throws<RoomException>(() => Apply("next"));
        Assert.Throws<RoomException>(() => games.Apply(room, host, Command("reveal") with { SessionId = Guid.NewGuid() }, 0));
        Assert.Throws<RoomException>(() => games.Apply(room, host, Command("reveal") with { RoundId = Guid.NewGuid() }, 0));
        Apply("reveal");
        Assert.Throws<RoomException>(() => Apply("answer", player, "b"));
        Apply("finish");
        Assert.Throws<RoomException>(() => Apply("reveal"));
    }
    [Fact]
    public void LegacyAggregateStateRemainsReadableAndUpgradesOnNextCommand()
    {
        Start();
        Apply("reveal");
        var saved = System.Text.Json.Nodes.JsonNode.Parse(room.GameState!)!;
        saved["SchemaVersion"] = 1;
        var session = saved["Sessions"]![0]!;
        var state = System.Text.Json.Nodes.JsonNode.Parse(session["State"]!.GetValue<string>())!;
        foreach (var result in state["Results"]!.AsArray()) result!.AsObject().Remove("Choices");
        session["State"] = state.ToJsonString();
        room.GameState = saved.ToJsonString();
        Assert.Empty(Assert.Single(View().Results).Choices);
        Apply("next");
        Assert.Equal(3, System.Text.Json.Nodes.JsonNode.Parse(room.GameState!)!["SchemaVersion"]!.GetValue<int>());
        Apply("reveal");
        Assert.Equal(2, View().Results[1].Choices.Count);
    }
    private sealed class OtherModule : IGameModule
    {
        public string Id => "other";
        public string Start(Room room, GameCommand command) => "started";
        public string Apply(string state, Room room, Membership actor, GameCommand command) => "finished";
        public GameView Project(GameSession session, Room room, Membership viewer) =>
            new(session.Id, Id, room.Id, 1, 1, session.State, "Prompt", "A", "B", 1, 0, true, null, 0, [], []);
    }
    [Fact]
    public void RegistrySupportsAnotherModuleWithoutRoomOrTransportBranches()
    {
        var registry = new GameModules([new OtherModule()]);
        registry.Apply(room, host, new(Guid.NewGuid(), "start", GameId: "other", ExpectedVersion: room.Version), 10);
        var view = registry.Project(room, host)!;
        Assert.Equal("other", view.GameId);
        Assert.Equal("started", view.Stage);
        registry.Apply(room, host, new(Guid.NewGuid(), "finish", view.SessionId), 20);
        Assert.Equal("finished", registry.Project(room, host)!.Stage);
    }
    [Fact]
    public void RoomControlsAndExpiryRemainAvailableDuringPlay()
    {
        Start();
        RoomRules.Apply(room, host, new(Guid.NewGuid(), room.Version, "transfer", player.Id), 30);
        Assert.DoesNotContain("reveal", View().AllowedActions);
        Assert.Contains("reveal", View(player).AllowedActions);
        Assert.Contains("close", RoomRules.Actions(room, player));
        Assert.True(RoomRules.IsExpired(room, 30 + TimeSpan.FromHours(25).Ticks, new()));
    }
}
