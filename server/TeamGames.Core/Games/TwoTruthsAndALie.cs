using System.Text.Json;
using TeamGames.Core.Rooms;
using TeamGames.Core.Administration;

namespace TeamGames.Core.Games;

public sealed class TwoTruthsAndALie : IGameModule
{
    public string Id => "two-truths-and-a-lie";
    private sealed record Entry(StatementSubmission? Submission, bool Passed, long Revision);
    private sealed record Vote(string Choice, long Revision);
    private sealed class State
    {
        public Guid RoundId { get; set; } = Guid.NewGuid();
        public int Number { get; set; } = 1;
        public long Revision { get; set; } = 1;
        public string Stage { get; set; } = "collection";
        public List<Guid> CollectionRoster { get; set; } = [];
        public Dictionary<Guid, Entry> Entries { get; set; } = [];
        public List<Guid> Queue { get; set; } = [];
        public int Cursor { get; set; }
        public Guid? Presenter { get; set; }
        public List<Guid> Voters { get; set; } = [];
        public Dictionary<Guid, Vote> Votes { get; set; } = [];
        public List<RoundResult> Results { get; set; } = [];
        public Dictionary<Guid, Guid> ResultPresenters { get; set; } = [];
    }
    private static bool Active(Room room, Guid id) => room.Members.Any(m => m.Id == id && !m.Removed);
    private static State Read(string json) => JsonSerializer.Deserialize<State>(json) ?? throw new InvalidOperationException("Missing statement game");
    public string Start(Room room, GameCommand command) => JsonSerializer.Serialize(new State { CollectionRoster = room.Members.Where(m => !m.Removed).Select(m => m.Id).ToList() });
    private static Guid[] Roster(State state, Room room) => (state.Stage is "collection" or "ready" ? state.CollectionRoster : state.Voters).Where(id => Active(room, id)).ToArray();
    private static StatementSubmission Validate(StatementSubmission? submission)
    {
        if (submission?.Statements is null || submission.Statements.Count != 3 || submission.LieIndex is < 0 or > 2)
            throw new RoomException("invalid_statements", "Write three statements and select exactly one lie");
        var statements = submission.Statements.Select(s => (s ?? "").Trim()).ToArray();
        if (statements.Any(s => s.Length is < 1 or > 200 || s.Any(char.IsControl)) || statements.Select(s => s.ToUpperInvariant()).Distinct().Count() != 3)
            throw new RoomException("invalid_statements", "Use three different statements of 1–200 characters without control characters");
        return new(statements, submission.LieIndex);
    }
    private static bool HasNext(State state, Room room) => state.Queue.Skip(state.Cursor).Any(id => Active(room, id));
    private static void Next(State state, Room room)
    {
        while (state.Cursor < state.Queue.Count)
        {
            var candidate = state.Queue[state.Cursor++];
            if (!Active(room, candidate)) continue;
            state.Presenter = candidate;
            state.Voters = room.Members.Where(m => !m.Removed && m.Id != candidate).Select(m => m.Id).ToList();
            state.Votes = [];
            state.Number++;
            state.RoundId = Guid.NewGuid();
            state.Stage = "responding";
            return;
        }
        throw new RoomException("presenters_exhausted", "Everyone ready has presented; finish this game", 409);
    }
    private static void Finalize(State state, Room room, bool skipped)
    {
        var presenter = state.Presenter!.Value;
        var submission = state.Entries[presenter].Submission!;
        var voters = Roster(state, room);
        var votes = voters.Where(state.Votes.ContainsKey).Select(id => state.Votes[id].Choice).ToArray();
        var passes = skipped ? 0 : votes.Count(v => v == "pass");
        var total = skipped ? 0 : votes.Count(v => v != "pass");
        var totals = skipped ? null : submission.Statements.Select((text, index) =>
        {
            var count = votes.Count(v => v == (index + 1).ToString());
            return new VoteTotal((index + 1).ToString(), text, count, total == 0 ? 0 : (int)Math.Round(100.0 * count / total, MidpointRounding.AwayFromZero), false);
        }).ToArray();
        state.Results.Add(new(state.RoundId, state.Number, "Which statement is the lie?", "Statements", "Guesses", skipped,
            0, 0, passes, voters.Length - total - passes, 0, 0,
            skipped ? [] : voters.Select(id => new PlayerChoiceView(id, room.Members.Single(m => m.Id == id).Name,
                state.Votes.GetValueOrDefault(id)?.Choice ?? "missing")).ToArray(), totals, skipped ? null : submission.LieIndex,
            room.Members.Single(m => m.Id == presenter).Name));
        state.ResultPresenters[state.RoundId] = presenter;
    }
    public string Apply(string json, Room room, Membership actor, GameCommand command)
    {
        var state = Read(json);
        if (command.Action is not ("submit" or "answer")) GameModules.Facilitator(room, actor);
        if (command.RoundId != state.RoundId || command.StageRevision != state.Revision)
            throw new RoomException("stage_changed", "The round moved on; review it and try again", 409);
        if (command.Action is "submit" or "answer")
        {
            if (!Roster(state, room).Contains(actor.Id)) throw new RoomException("not_eligible", "You cannot respond at this stage", 403);
            if (state.Stage == "collection")
            {
                var previous = state.Entries.GetValueOrDefault(actor.Id);
                if ((previous?.Revision ?? 0) != command.ResponseRevision) throw new RoomException("response_changed", "Your statements changed in another tab", 409);
                if (command.Action == "submit") state.Entries[actor.Id] = new(Validate(command.Submission), false, command.ResponseRevision + 1);
                else if (command.Choice == "pass") state.Entries[actor.Id] = new(null, true, command.ResponseRevision + 1);
                else throw new RoomException("invalid_choice", "Save your statements or sit this game out");
            }
            else if (state.Stage == "responding" && command.Action == "answer")
            {
                if (state.Presenter is not { } presenter || !Active(room, presenter)) throw new RoomException("presenter_unavailable", "The presenter left; skip this round or finish", 409);
                if (command.Choice is not ("1" or "2" or "3" or "pass")) throw new RoomException("invalid_choice", "Choose a statement or pass");
                if ((state.Votes.GetValueOrDefault(actor.Id)?.Revision ?? 0) != command.ResponseRevision) throw new RoomException("response_changed", "Your guess changed in another tab", 409);
                state.Votes[actor.Id] = new(command.Choice, command.ResponseRevision + 1);
            }
            else throw new RoomException("invalid_stage", "Responses are closed", 409);
        }
        else
        {
            switch (command.Action)
            {
                case "close-collection" when state.Stage == "collection":
                    state.Queue = state.CollectionRoster.Where(id => Active(room, id) && state.Entries.GetValueOrDefault(id)?.Submission is not null).ToList();
                    state.Cursor = 0;
                    if (state.Queue.Count == 0) state.Stage = "ready";
                    else { state.Number = 0; Next(state, room); }
                    break;
                case "reopen" when state.Stage == "ready":
                    state.CollectionRoster = room.Members.Where(m => !m.Removed).Select(m => m.Id).ToList();
                    state.Stage = "collection";
                    state.RoundId = Guid.NewGuid();
                    break;
                case "reveal" when state.Stage == "responding" && state.Presenter is { } presenter && Active(room, presenter) && Roster(state, room).Length > 0:
                    Finalize(state, room, false); state.Stage = "revealed"; break;
                case "skip" when state.Stage == "responding": Finalize(state, room, true); state.Stage = "revealed"; break;
                case "next" when state.Stage == "revealed": Next(state, room); break;
                case "finish" when state.Stage != "finished":
                    if (state.Stage == "responding") Finalize(state, room, true);
                    state.Stage = "finished"; break;
                case "lobby" when state.Stage == "finished": break;
                default: throw new RoomException("invalid_stage", "That action is not available now", 409);
            }
            state.Revision++;
        }
        return JsonSerializer.Serialize(state);
    }
    public AdminGameView Describe(GameSession session)
    {
        var state = Read(session.State);
        return new(session.Id, session.GameId, state.Stage, state.Number);
    }
    public string Finish(string json, Room room)
    {
        var state = Read(json);
        if (state.Stage == "finished") throw new RoomException("invalid_stage", "This game has already finished", 409);
        if (state.Stage == "responding") Finalize(state, room, true);
        state.Stage = "finished";
        state.Revision++;
        return JsonSerializer.Serialize(state);
    }
    public GameView Project(GameSession session, Room room, Membership viewer)
    {
        var state = Read(session.State);
        var roster = Roster(state, room);
        var collecting = state.Stage == "collection";
        var presenterActive = state.Presenter is { } presenter && Active(room, presenter);
        var eligible = roster.Contains(viewer.Id) && (collecting || state.Stage == "responding" && presenterActive);
        var entry = state.Entries.GetValueOrDefault(viewer.Id);
        var vote = state.Votes.GetValueOrDefault(viewer.Id);
        var actions = new List<string>();
        if (eligible) { actions.Add("answer"); if (collecting) actions.Add("submit"); }
        if (viewer.Id == room.FacilitatorId)
        {
            if (collecting) actions.Add("close-collection");
            if (state.Stage == "ready") actions.Add("reopen");
            if (state.Stage == "responding") { actions.Add("skip"); if (presenterActive && roster.Length > 0) actions.Add("reveal"); }
            if (state.Stage == "revealed" && HasNext(state, room)) actions.Add("next");
            actions.Add(state.Stage == "finished" ? "lobby" : "finish");
        }
        var responses = roster.Select(id => new ResponseStatusView(id, collecting || state.Stage == "ready" ? state.Entries.ContainsKey(id) : state.Votes.ContainsKey(id))).ToArray();
        var publicPresenter = presenterActive && state.Stage is "responding" or "revealed" ? new PresenterView(state.Presenter!.Value, room.Members.Single(m => m.Id == state.Presenter).Name, state.Entries[state.Presenter.Value].Submission!.Statements) : null;
        var results = state.Results.Select(r => r with
        {
            PresenterName = Active(room, state.ResultPresenters[r.RoundId]) ? r.PresenterName : "Removed player",
            Choices = (r.Choices ?? []).Select(choice => Active(room, choice.ParticipantId)
                ? choice : choice with { Name = "Removed player" }).ToArray()
        }).ToArray();
        return new(session.Id, Id, state.RoundId, state.Number, state.Revision, state.Stage, collecting ? "Write your two truths and one lie" : "Which statement is the lie?", "Statements", "Guesses",
            roster.Length, responses.Count(r => r.HasResponded), eligible, collecting ? entry?.Passed == true ? "pass" : null : state.Stage == "responding" && presenterActive ? vote?.Choice : null,
            collecting ? entry?.Revision ?? 0 : vote?.Revision ?? 0, actions, results, Responses: responses,
            Options: publicPresenter?.Statements.Select((text, index) => new GameOption((index + 1).ToString(), text)).ToArray() ?? [],
            OwnSubmission: entry?.Submission, Presenter: publicPresenter,
            Notice: state.Stage == "ready" ? "No one is ready to present. Reopen collection or finish the game." : state.Stage == "responding" && (!presenterActive || roster.Length == 0) ? "This round needs an active presenter and at least one other player. Skip or finish." : null);
    }
}
