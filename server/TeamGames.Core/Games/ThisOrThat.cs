using System.Text.Json;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;

namespace TeamGames.Core.Games;

public sealed class ThisOrThat(IGameRandom random) : IGameModule
{
    public string Id => "this-or-that";
    private sealed class Answer
    {
        public string Choice { get; set; } = "";
        public long Revision { get; set; }
    }
    private sealed record SavedRound(Guid Id, string OptionA, string OptionB, IReadOnlyList<Guid> Roster, IReadOnlyDictionary<Guid, Answer> Answers);
    private sealed class State
    {
        public int RoundNumber { get; set; }
        public string Prompt { get; set; } = "Which would you choose?";
        public List<string> UsedPromptIds { get; set; } = [];
        public string Category { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Search { get; set; } = "";
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
    public string Start(Room room, GameCommand command)
    {
        var state = new State();
        Next(state, room, command);
        return JsonSerializer.Serialize(state);
    }
    private static Prompt[] Pool(State state, Room room) => room.PromptPool.Where(p => PromptRules.Matches(p, "this-or-that", state.Category, state.Tag, state.Search)).ToArray();
    private void Next(State state, Room room, GameCommand command)
    {
        if ((command.Category?.Length ?? 0) > 48 || (command.Tag?.Length ?? 0) > 32 || (command.Search?.Length ?? 0) > 120)
            throw new RoomException("invalid_filter", "Shorten the prompt filters");
        state.Category = command.Category?.Trim() ?? state.Category;
        state.Tag = command.Tag?.Trim() ?? state.Tag;
        state.Search = command.Search?.Trim() ?? state.Search;
        var pool = Pool(state, room);
        var available = pool.Where(p => !state.UsedPromptIds.Contains(p.Id)).ToArray();
        if (command.ResetPool)
        {
            if (pool.Length == 0 || available.Length != 0) throw new RoomException("reset_unavailable", "Reset is available when the selected pool is exhausted", 409);
            state.UsedPromptIds.RemoveAll(id => pool.Any(p => p.Id == id));
            available = pool;
        }
        if (available.Length == 0) throw new RoomException("prompts_exhausted", "This prompt pool is exhausted; reset it or change your filters", 409);
        var prompt = command.PromptId is null ? available[random.Next(available.Length)]
            : available.FirstOrDefault(p => p.Id == command.PromptId) ?? throw new RoomException("prompt_unavailable", "Choose an unused, enabled prompt from this pool", 409);
        state.UsedPromptIds.Add(prompt.Id);
        state.Prompt = prompt.Text;
        state.OptionA = prompt.OptionA;
        state.OptionB = prompt.OptionB;
        state.RoundNumber++;
        state.RoundId = Guid.NewGuid();
        state.Roster = room.Members.Where(m => !m.Removed).Select(m => m.Id).ToList();
        state.Answers = [];
        state.Stage = "responding";
    }
    private static State Read(string json)
    {
        var state = JsonSerializer.Deserialize<State>(json) ?? throw new InvalidOperationException("Missing round state");
        // M2 stored indices into its twelve-pair deck. Their stable seed IDs preserve usage.
        if (state.Used.Count > 0)
        {
            state.UsedPromptIds.AddRange(state.Used.Select(index => $"tot-{index + 1:000}"));
            if (state.RoundNumber == 0) state.RoundNumber = state.Used.Count;
            state.Used = [];
        }
        return state;
    }
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
        state.Results.Add(new(state.RoundId, state.RoundNumber, state.Prompt, state.OptionA, state.OptionB,
            skipped, a, b, passes, eligible.Length - a - b - passes, percent, a + b == 0 ? 0 : 100 - percent,
            skipped ? [] : eligible.Select(id => new PlayerChoiceView(id, room.Members.Single(m => m.Id == id).Name,
                state.Answers.GetValueOrDefault(id)?.Choice ?? "missing")).ToArray()));
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
                case "next" when state.Stage == "revealed": Next(state, room, command); break;
                case "reset" when state.Stage == "revealed": Next(state, room, command with { ResetPool = true }); break;
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
            if (state.Stage == "revealed")
            {
                var pool = Pool(state, room);
                if (pool.Any(p => !state.UsedPromptIds.Contains(p.Id))) actions.Add("next");
                else if (pool.Length > 0) actions.Add("reset");
            }
            actions.Add(state.Stage == "finished" ? "lobby" : "finish");
        }
        return new(session.Id, Id, state.RoundId, state.RoundNumber, state.Revision, state.Stage,
            state.Prompt, state.OptionA, state.OptionB, eligible.Length, eligible.Count(state.Answers.ContainsKey),
            eligible.Contains(viewer.Id), own?.Choice, own?.Revision ?? 0, actions,
            state.Results.Select(result => result with
            {
                Choices = (result.Choices ?? []).Select(choice => room.Members.Any(m => m.Id == choice.ParticipantId && !m.Removed)
                    ? choice : choice with { Name = "Removed player" }).ToArray()
            }).ToArray(), Pool(state, room).Count(p => !state.UsedPromptIds.Contains(p.Id)), Pool(state, room).Length,
            state.Category, state.Tag, state.Search, viewer.Id == room.FacilitatorId ? state.UsedPromptIds.ToArray() : [],
            eligible.Select(id => new ResponseStatusView(id, state.Answers.ContainsKey(id))).ToArray());
    }
}
