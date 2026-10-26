using System.Text.Json;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;

namespace TeamGames.Core.Games;

public sealed class ThisOrThat(IGameRandom random) : PromptGame(random, "this-or-that", "binary", true);
public sealed class WouldYouRather(IGameRandom random) : PromptGame(random, "would-you-rather", "binary");
public sealed class NeverHaveIEver(IGameRandom random) : PromptGame(random, "never-have-i-ever", "experience");
public sealed class MostLikelyTo(IGameRandom random) : PromptGame(random, "most-likely-to", "nomination");

public class PromptGame(IGameRandom random, string id, string kind, bool namedReveal = false) : IGameModule
{
    public string Id => id;
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
    private Prompt[] Pool(State state, Room room) => room.PromptPool.Where(p => PromptRules.Matches(p, Id, state.Category, state.Tag, state.Search)).ToArray();
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
        state.OptionA = kind == "experience" ? "I have" : kind == "nomination" ? "Nominate a teammate" : prompt.OptionA;
        state.OptionB = kind == "experience" ? "I haven't" : kind == "nomination" ? "Pass" : prompt.OptionB;
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
    private void Finalize(State state, Room room, bool skipped)
    {
        state.CompletedRounds.Add(new(state.RoundId, state.OptionA, state.OptionB, state.Roster.ToArray(), new Dictionary<Guid, Answer>(state.Answers)));
        var eligible = Eligible(state, room);
        var answers = eligible.Where(state.Answers.ContainsKey).Select(id => state.Answers[id].Choice).ToArray();
        var a = skipped ? 0 : answers.Count(c => c == "a");
        var b = skipped ? 0 : answers.Count(c => c == "b");
        var passes = skipped ? 0 : answers.Count(c => c == "pass");
        VoteTotal[]? totals = null;
        var validNominations = 0;
        if (kind == "nomination" && !skipped)
        {
            var counts = eligible.Select(target => (Id: target, Count: eligible.Count(voter => voter != target && state.Answers.GetValueOrDefault(voter)?.Choice == target.ToString()))).ToArray();
            validNominations = counts.Sum(c => c.Count);
            var maximum = counts.Length == 0 ? 0 : counts.Max(c => c.Count);
            totals = counts.Select(c => new VoteTotal(c.Id.ToString(), room.Members.Single(m => m.Id == c.Id).Name, c.Count,
                validNominations == 0 ? 0 : (int)Math.Round(100.0 * c.Count / validNominations, MidpointRounding.AwayFromZero), maximum > 0 && c.Count == maximum)).ToArray();
        }
        var percent = a + b == 0 ? 0 : (int)Math.Round(100.0 * a / (a + b), MidpointRounding.AwayFromZero);
        state.Results.Add(new(state.RoundId, state.RoundNumber, state.Prompt, state.OptionA, state.OptionB,
            skipped, a, b, passes, eligible.Length - (kind == "nomination" ? validNominations : a + b) - passes, percent, a + b == 0 ? 0 : 100 - percent,
            skipped || !namedReveal ? [] : eligible.Select(id => new PlayerChoiceView(id, room.Members.Single(m => m.Id == id).Name,
                state.Answers.GetValueOrDefault(id)?.Choice ?? "missing")).ToArray(), totals));
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
            if (kind == "nomination")
            {
                var eligible = Eligible(state, room);
                if (eligible.Length < 2) throw new RoomException("insufficient_players", "This round needs at least two players", 409);
                if (command.Choice != "pass" && (!Guid.TryParse(command.Choice, out var target) || target == actor.Id || !eligible.Contains(target)))
                    throw new RoomException("invalid_choice", "Nominate another eligible teammate or pass");
            }
            else if (command.Choice is not ("a" or "b" or "pass")) throw new RoomException("invalid_choice", "Choose either option or pass");
            var revision = state.Answers.GetValueOrDefault(actor.Id)?.Revision ?? 0;
            if (command.ResponseRevision != revision) throw new RoomException("response_changed", "Your answer changed in another tab; review it and try again", 409);
            state.Answers[actor.Id] = new() { Choice = kind == "nomination" && command.Choice != "pass" ? Guid.Parse(command.Choice!).ToString() : command.Choice, Revision = revision + 1 };
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
        if (state.Stage == "responding" && eligible.Contains(viewer.Id) && (kind != "nomination" || eligible.Length >= 2)) actions.Add("answer");
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
            eligible.Contains(viewer.Id) && (kind != "nomination" || eligible.Length >= 2),
            kind == "nomination" && own?.Choice != "pass" && !eligible.Any(id => id != viewer.Id && id.ToString() == own?.Choice) ? null : own?.Choice, own?.Revision ?? 0, actions,
            state.Results.Select(result => result with
            {
                Choices = (result.Choices ?? []).Select(choice => room.Members.Any(m => m.Id == choice.ParticipantId && !m.Removed)
                    ? choice : choice with { Name = "Removed player" }).ToArray(),
                Totals = result.Totals?.Select(total => room.Members.Any(m => !m.Removed && m.Id.ToString() == total.Value) ? total : total with { Label = "Removed player" }).ToArray()
            }).ToArray(), Pool(state, room).Count(p => !state.UsedPromptIds.Contains(p.Id)), Pool(state, room).Length,
            state.Category, state.Tag, state.Search, viewer.Id == room.FacilitatorId ? state.UsedPromptIds.ToArray() : [],
            eligible.Select(id => new ResponseStatusView(id, state.Answers.ContainsKey(id))).ToArray(),
            kind == "nomination" ? eligible.Where(id => id != viewer.Id).Select(id => new GameOption(id.ToString(), room.Members.Single(m => m.Id == id).Name)).ToArray() : null,
            Notice: kind == "nomination" && eligible.Length < 2 ? "This round needs two players. Skip this round or finish the game." : kind == "nomination" && state.Stage == "responding" && own is not null && own.Choice != "pass" && !eligible.Any(id => id != viewer.Id && id.ToString() == own.Choice) ? "Your previous nominee is no longer eligible. Choose another teammate or pass." : null);
    }
}
