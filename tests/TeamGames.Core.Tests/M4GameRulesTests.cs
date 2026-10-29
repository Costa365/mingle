using System.Text.Json;
using TeamGames.Core.Games;
using TeamGames.Core.Prompts;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Tests;

public sealed class M4GameRulesTests
{
    private sealed class First : IGameRandom { public int Next(int count) => 0; }
    private readonly Room room = new() { Id = Guid.NewGuid(), Code = "ABC234", PromptPool = PromptLibrary.All };
    private readonly Membership host, one, two;
    private readonly GameModules games = new([new ThisOrThat(new First()), new WouldYouRather(new First()), new NeverHaveIEver(new First()), new MostLikelyTo(new First()), new TwoTruthsAndALie()]);
    public M4GameRulesTests()
    {
        host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20);
        one = RoomRules.Join(room, Guid.NewGuid(), "One", 20);
        two = RoomRules.Join(room, Guid.NewGuid(), "Two", 20);
        room.FacilitatorId = host.Id;
    }
    private GameView View(Membership? viewer = null) => games.Project(room, viewer ?? host)!;
    private void Start(string id) => games.Apply(room, host, new(Guid.NewGuid(), "start", GameId: id, ExpectedVersion: room.Version), 1);
    private GameCommand Command(string action, Membership? actor = null, string? choice = null, StatementSubmission? submission = null)
    {
        var view = View(actor);
        return new(Guid.NewGuid(), action, view.SessionId, view.RoundId, view.StageRevision, view.ResponseRevision, choice, Submission: submission);
    }
    private void Act(string action, Membership? actor = null, string? choice = null, StatementSubmission? submission = null) => games.Apply(room, actor ?? host, Command(action, actor, choice, submission), 2);
    private static StatementSubmission Submission(int lie = 2) => new(["I learned to juggle", "I visited Iceland", "I speak five languages"], lie);

    [Theory]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    public void BinaryGamesHaveIndependentContentPrivateAnswersAndAggregateOnlyReveal(string id)
    {
        Start(id);
        Assert.Equal(id, View().GameId);
        if (id == "never-have-i-ever") Assert.Equal(("I have", "I haven't"), (View().OptionA, View().OptionB));
        Act("answer", one, "a"); Act("answer", two, "pass");
        Assert.Null(View().OwnChoice); Assert.Empty(View().Results);
        Assert.Throws<RoomException>(() => Act("answer", one, "invalid"));
        Assert.Throws<RoomException>(() => Act("reveal", one));
        var stale = Command("answer", one, "b");
        Act("answer", one, "b"); Act("reveal");
        var result = Assert.Single(View().Results);
        Assert.Equal((0, 1, 1, 1), (result.A, result.B, result.Passes, result.Missing));
        Assert.Empty(result.Choices);
        Assert.Throws<RoomException>(() => games.Apply(room, one, stale, 3));
        Act("next"); Assert.Equal(2, View().RoundNumber);
        Assert.All(View().Responses!, r => Assert.False(r.HasResponded));
        Act("finish"); Act("lobby"); Assert.Null(games.Project(room, host));
    }
    [Theory]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("two-truths-and-a-lie")]
    public void FinishedSummaryPublishesFinalizedChoicesOnlyWithFrozenEligibility(string id)
    {
        var missing = RoomRules.Join(room, Guid.NewGuid(), "Missing", 20);
        Start(id);
        if (id == "two-truths-and-a-lie")
        {
            Act("submit", host, submission: Submission());
            Act("submit", two, submission: Submission(0));
            Act("close-collection");
        }
        var choice = id == "two-truths-and-a-lie" ? "3" : "b";
        Act("answer", one, choice); Act("answer", two, "pass");
        var stale = Command("answer", one, choice);
        var late = RoomRules.Join(room, Guid.NewGuid(), "Late", 20);
        Act("reveal");
        Assert.All(new[] { host, one, two, missing, late }, viewer => Assert.Empty(View(viewer).Results[0].Choices));
        Assert.Throws<RoomException>(() => games.Apply(room, one, stale, 3));
        Act("next");
        Assert.Empty(View().Results[0].Choices);
        one.Removed = true;
        Act("finish");
        var result = View(two).Results[0];
        Assert.Equal(choice, result.Choices.Single(c => c.ParticipantId == one.Id).Choice);
        Assert.Equal("Removed player", result.Choices.Single(c => c.ParticipantId == one.Id).Name);
        Assert.Equal("pass", result.Choices.Single(c => c.ParticipantId == two.Id).Choice);
        Assert.Equal("missing", result.Choices.Single(c => c.ParticipantId == missing.Id).Choice);
        Assert.DoesNotContain(result.Choices, c => c.ParticipantId == late.Id);
        if (id == "two-truths-and-a-lie") Assert.DoesNotContain(result.Choices, c => c.ParticipantId == host.Id);
        Assert.True(View().Results[1].Skipped);
        Assert.Empty(View().Results[1].Choices);
    }

    [Theory]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("two-truths-and-a-lie")]
    public void OlderAggregateResultsDoNotInventNamedChoicesAtFinish(string id)
    {
        Start(id);
        if (id == "two-truths-and-a-lie") { Act("submit", host, submission: Submission()); Act("close-collection"); }
        Act("answer", one, id == "two-truths-and-a-lie" ? "1" : "a");
        Act("reveal");
        var saved = System.Text.Json.Nodes.JsonNode.Parse(room.GameState!)!;
        var session = saved["Sessions"]![0]!;
        var state = System.Text.Json.Nodes.JsonNode.Parse(session["State"]!.GetValue<string>())!;
        state["Results"]![0]!.AsObject().Remove("Choices");
        session["State"] = state.ToJsonString(); room.GameState = saved.ToJsonString();
        Act("finish");
        Assert.Empty(View().Results[0].Choices);
        Assert.False(View().Results[0].Skipped);
    }

    [Fact]
    public void NominationsForbidSelfAndUnknownTargetsAndRevealTiesWithoutVoters()
    {
        Start("most-likely-to");
        Assert.DoesNotContain(View().Options!, o => o.Value == host.Id.ToString());
        Assert.Throws<RoomException>(() => Act("answer", host, host.Id.ToString()));
        Assert.Throws<RoomException>(() => Act("answer", host, Guid.NewGuid().ToString()));
        Act("answer", host, one.Id.ToString()); Act("answer", one, two.Id.ToString()); Act("answer", two, "pass");
        Assert.Null(View(one).OwnSubmission); Assert.Empty(View(one).Results);
        Act("reveal"); var result = Assert.Single(View().Results);
        Assert.Empty(result.Choices);
        Assert.Equal(2, result.Totals!.Count(t => t.Winner));
        Assert.Equal(2, result.Totals!.Sum(t => t.Count));
        Assert.Equal(1, result.Passes);
        Act("next"); Act("reveal"); Assert.DoesNotContain(View().Results[^1].Totals!, t => t.Winner);
        Act("finish"); Assert.All(View().Results, r => Assert.Empty(r.Choices));
    }
    [Fact]
    public void RemovedNomineeDoesNotExposeVotersThroughStatusAndSoloCannotVote()
    {
        Start("most-likely-to"); Act("answer", host, one.Id.ToString());
        one.Removed = true;
        Assert.True(View(two).Responses!.Single(r => r.ParticipantId == host.Id).HasResponded);
        Assert.Null(View().OwnChoice);
        Assert.Contains("previous nominee", View().Notice);
        Act("reveal"); Assert.Equal(0, View().Results[0].Totals!.Sum(t => t.Count));
        Assert.Equal(2, View().Results[0].Missing);
        Act("next"); two.Removed = true;
        Assert.DoesNotContain("answer", View().AllowedActions);
        Assert.Throws<RoomException>(() => Act("answer", host, "pass"));
        Act("skip"); Assert.True(View().Results[^1].Skipped);
    }
    [Fact]
    public void StatementsArePrivateUntilTheirTurnAndLieOnlyAppearsOnReveal()
    {
        Start("two-truths-and-a-lie"); Act("submit", host, submission: Submission()); Act("submit", one, submission: new(["Private one", "Private two", "Private three"], 0));
        Assert.Null(View(two).OwnSubmission); Assert.Null(View(two).Presenter);
        Assert.DoesNotContain("Private one", JsonSerializer.Serialize(View(host)));
        Assert.Equal(2, View().ResponseCount);
        Assert.Throws<RoomException>(() => Act("close-collection", one));
        Act("close-collection"); Assert.Equal(host.Id, View(one).Presenter!.ParticipantId);
        Assert.DoesNotContain("answer", View().AllowedActions);
        Assert.Throws<RoomException>(() => Act("answer", host, "3"));
        Assert.Null(View(one).OwnChoice); Assert.Empty(View(one).Results);
        var stale = Command("answer", one, "1");
        Act("answer", one, "3"); Act("answer", two, "pass"); Act("reveal");
        Assert.Equal(2, View(one).Results[0].LieIndex);
        Assert.Null(View(one).OwnChoice);
        Assert.Equal(1, View().Results[0].Totals![2].Count);
        Assert.Empty(View().Results[0].Choices);
        Assert.Throws<RoomException>(() => games.Apply(room, one, stale, 3));
        Act("next"); Assert.Equal(one.Id, View(two).Presenter!.ParticipantId);
        Assert.Equal("Private one", View(two).Presenter!.Statements[0]);
        Act("finish"); Assert.True(View().Results[^1].Skipped); Assert.Null(View().Results[^1].LieIndex);
    }
    [Fact]
    public void MissingOptedOutAndRemovedPresentersCanNeverBlockOrLeakLie()
    {
        Start("two-truths-and-a-lie"); Act("answer", host, "pass"); Act("close-collection");
        Assert.Equal("ready", View().Stage); Assert.Contains("reopen", View().AllowedActions);
        Act("reopen"); Act("submit", one, submission: Submission()); Act("close-collection");
        Assert.Equal(one.Id, View().Presenter!.ParticipantId);
        one.Removed = true;
        Assert.Null(View().Presenter); Assert.DoesNotContain("reveal", View().AllowedActions);
        Act("skip"); Assert.Null(View().Results[0].LieIndex); Assert.Null(View().Results[0].Totals);
        Assert.Equal("Removed player", View().Results[0].PresenterName);
        Act("finish"); Act("lobby");
    }
    [Fact]
    public void SubmissionValidationLateArrivalsAndIndependentRevisionsAreEnforced()
    {
        Start("two-truths-and-a-lie");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StatementSubmission>("{\"Statements\":[\"one\",\"two\",\"three\"]}"));
        foreach (var invalid in new[] { new StatementSubmission(["one", "two"], 0), new StatementSubmission(["same", " SAME ", "other"], 0), new StatementSubmission(["one", "two", "three"], -1), new StatementSubmission([new string('x', 201), "two", "three"], 1) })
            Assert.Throws<RoomException>(() => Act("submit", one, submission: invalid));
        var first = Command("submit", one, submission: Submission());
        games.Apply(room, one, first, 2);
        Assert.Throws<RoomException>(() => games.Apply(room, one, first, 3));
        var late = RoomRules.Join(room, Guid.NewGuid(), "Late", 20);
        Assert.False(View(late).Eligible); Assert.Throws<RoomException>(() => Act("submit", late, submission: Submission()));
        Act("close-collection"); Assert.True(View(late).Eligible);
    }
}
