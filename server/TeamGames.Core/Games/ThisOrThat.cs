using System.Text.Json;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Games;

public sealed class ThisOrThat(IGameRandom random) : IGameModule
{
    public string Id => "this-or-that";
    private sealed record Pair(string A, string B);
    private static readonly Pair[] Prompts =
    [
        new("Mountains", "Seaside"), new("Tea", "Coffee"), new("Early start", "Late start"),
        new("Board games", "Video games"), new("Cook together", "Eat out together"),
        new("A good book", "A good film"), new("Train journey", "Road trip"),
        new("Quiet workspace", "Background music"), new("Plan ahead", "Be spontaneous"),
        new("City break", "Countryside escape"), new("Learn a language", "Learn an instrument"),
        new("Sweet snack", "Savoury snack")
    ];
    private sealed class Answer
    {
        public string Choice { get; set; } = "";
        public long Revision { get; set; }
    }
    private sealed record SavedRound(Guid Id, string OptionA, string OptionB, IReadOnlyList<Guid> Roster, IReadOnlyDictionary<Guid, Answer> Answers);
    private sealed class State
    {
        public Guid RoundId { get; set; }
        public string OptionA { get; set; } = "";
        public string OptionB { get; set; } = "";
        public long Revision { get; set; } = 1;
        public string Stage { get; set; } = "responding";
        public List<int> Used { get; set; } = [];
        public List<Guid> Roster { get; set; } = [];
        public Dictionary<Guid, Answer> Answers { get; set; } = [];
        public List<RoundResult> Results { get; set; } = [];
        public List<SavedRound> CompletedRounds { get; set; } = [];
    }
    public string Start(Room room)
    {
        var state = new State();
        Next(state, room);
        return JsonSerializer.Serialize(state);
    }
    private void Next(State state, Room room)
    {
        var available = Enumerable.Range(0, Prompts.Length).Except(state.Used).ToArray();
        if (available.Length == 0) throw new RoomException("prompts_exhausted", "The seed prompts are finished; finish this game", 409);
        state.Used.Add(available[random.Next(available.Length)]);
        var pair = Prompts[state.Used[^1]];
        state.OptionA = pair.A;
        state.OptionB = pair.B;
        state.RoundId = Guid.NewGuid();
        state.Roster = room.Members.Where(m => !m.Removed).Select(m => m.Id).ToList();
        state.Answers = [];
        state.Stage = "responding";
    }
    private static State Read(string json) => JsonSerializer.Deserialize<State>(json) ?? throw new InvalidOperationException("Missing round state");
    private static Guid[] Eligible(State state, Room room) => state.Roster.Where(id => room.Members.Any(m => m.Id == id && !m.Removed)).ToArray();
    private static void Finalize(State state, Room room, bool skipped)
    {
        state.CompletedRounds.Add(new(state.RoundId, state.OptionA, state.OptionB, state.Roster.ToArray(), new Dictionary<Guid, Answer>(state.Answers)));
        var eligible = Eligible(state, room);
        var answers = eligible.Where(state.Answers.ContainsKey).Select(id => state.Answers[id].Choice).ToArray();
        var a = skipped ? 0 : answers.Count(c => c == "a");
        var b = skipped ? 0 : answers.Count(c => c == "b");
        var passes = skipped ? 0 : answers.Count(c => c == "pass");
        var percent = a + b == 0 ? 0 : (int)Math.Round(100.0 * a / (a + b), MidpointRounding.AwayFromZero);
        state.Results.Add(new(state.RoundId, state.Used.Count, "Which would you choose?", state.OptionA, state.OptionB,
            skipped, a, b, passes, eligible.Length - a - b - passes, percent, a + b == 0 ? 0 : 100 - percent));
    }
    public string Apply(string json, Room room, Membership actor, GameCommand command)
    {
        var state = Read(json);
        if (command.Action != "answer") GameModules.Facilitator(room, actor);
        if (command.RoundId != state.RoundId || command.StageRevision != state.Revision)
            throw new RoomException("stage_changed", "The round moved on; your change was not applied", 409);
        if (command.Action == "answer")
        {
            if (state.Stage != "responding") throw new RoomException("stage_changed", "Answers are closed for this round", 409);
            if (!Eligible(state, room).Contains(actor.Id)) throw new RoomException("not_eligible", "You can play from the next round", 403);
            if (command.Choice is not ("a" or "b" or "pass")) throw new RoomException("invalid_choice", "Choose either option or pass");
            var revision = state.Answers.GetValueOrDefault(actor.Id)?.Revision ?? 0;
            if (command.ResponseRevision != revision) throw new RoomException("response_changed", "Your answer changed in another tab; review it and try again", 409);
            state.Answers[actor.Id] = new() { Choice = command.Choice, Revision = revision + 1 };
        }
        else
        {
            switch (command.Action)
            {
                case "reveal" when state.Stage == "responding":
                    Finalize(state, room, false);
                    state.Stage = "revealed";
                    break;
                case "skip" when state.Stage == "responding":
                    Finalize(state, room, true);
                    state.Stage = "revealed";
                    break;
                case "next" when state.Stage == "revealed": Next(state, room); break;
                case "finish" when state.Stage != "finished":
                    if (state.Stage == "responding") Finalize(state, room, true);
                    state.Stage = "finished";
                    break;
                case "lobby" when state.Stage == "finished": break;
                default: throw new RoomException("invalid_stage", "That action is not available at this stage", 409);
            }
            state.Revision++;
        }
        return JsonSerializer.Serialize(state);
    }
    public GameView Project(GameSession session, Room room, Membership viewer)
    {
        var state = Read(session.State);
        var eligible = Eligible(state, room);
        var own = state.Answers.GetValueOrDefault(viewer.Id);
        var actions = new List<string>();
        if (state.Stage == "responding" && eligible.Contains(viewer.Id)) actions.Add("answer");
        if (viewer.Id == room.FacilitatorId)
        {
            if (state.Stage == "responding") actions.AddRange(["reveal", "skip"]);
            if (state.Stage == "revealed" && state.Used.Count < Prompts.Length) actions.Add("next");
            actions.Add(state.Stage == "finished" ? "lobby" : "finish");
        }
        return new(session.Id, Id, state.RoundId, state.Used.Count, state.Revision, state.Stage,
            "Which would you choose?", state.OptionA, state.OptionB, eligible.Length, eligible.Count(state.Answers.ContainsKey),
            eligible.Contains(viewer.Id), own?.Choice, own?.Revision ?? 0, actions, state.Results.ToArray());
    }
}
