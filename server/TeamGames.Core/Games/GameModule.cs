using System.Security.Cryptography;
using System.Text.Json;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Games;

public sealed record GameCommand(Guid CommandId, string Action, Guid? SessionId = null, Guid? RoundId = null,
    long StageRevision = 0, long ResponseRevision = 0, string? Choice = null, string? GameId = null, long ExpectedVersion = 0, string? PromptId = null, string? Category = null, string? Tag = null, string? Search = null, bool ResetPool = false);
public sealed record PlayerChoiceView(Guid ParticipantId, string Name, string Choice);
public sealed record ResponseStatusView(Guid ParticipantId, bool HasResponded);
public sealed record RoundResult(Guid RoundId, int Number, string Prompt, string OptionA, string OptionB,
    bool Skipped, int A, int B, int Passes, int Missing, int PercentA, int PercentB, IReadOnlyList<PlayerChoiceView> Choices);
public sealed record GameView(Guid SessionId, string GameId, Guid RoundId, int RoundNumber, long StageRevision,
    string Stage, string Prompt, string OptionA, string OptionB, int EligibleCount, int ResponseCount,
    bool Eligible, string? OwnChoice, long ResponseRevision, IReadOnlyList<string> AllowedActions,
    IReadOnlyList<RoundResult> Results, int RemainingPrompts = 0, int PoolSize = 0, string Category = "", string Tag = "", string Search = "", IReadOnlyList<string>? UsedPromptIds = null, IReadOnlyList<ResponseStatusView>? Responses = null);
public sealed class GameSession
{
    public Guid Id { get; set; }
    public string GameId { get; set; } = "";
    public string State { get; set; } = "";
}

public interface IGameRandom { int Next(int count); }
public sealed class GameRandom : IGameRandom { public int Next(int count) => RandomNumberGenerator.GetInt32(count); }

// Only modules interpret their serialized state. Transport and room storage use this contract.
public interface IGameModule
{
    string Id { get; }
    string Start(Room room, GameCommand command);
    string Apply(string state, Room room, Membership actor, GameCommand command);
    GameView Project(GameSession session, Room room, Membership viewer);
}

public sealed class GameModules(IEnumerable<IGameModule> modules)
{
    private sealed class SavedGames
    {
        public int SchemaVersion { get; set; } = 3;
        public List<GameSession> Sessions { get; set; } = [];
    }
    private readonly IReadOnlyDictionary<string, IGameModule> registered = modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
    public IGameModule Get(string id) => registered.GetValueOrDefault(id)
        ?? throw new RoomException("game_unavailable", "That game is not available yet");
    public GameView? Project(Room room, Membership viewer)
    {
        if (room.GameState is null || room.Status != "playing") return null;
        var session = Read(room.GameState).Sessions[^1];
        return Get(session.GameId).Project(session, room, viewer);
    }
    public void Apply(Room room, Membership actor, GameCommand command, long now)
    {
        if (room.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
        if (command.Action == "start")
        {
            Facilitator(room, actor);
            if (room.Status != "lobby" || command.ExpectedVersion != room.Version)
                throw new RoomException("stage_changed", "The room changed; review it and try again", 409);
            var module = Get(command.GameId ?? "");
            var saved = room.GameState is null ? new SavedGames() : Read(room.GameState);
            saved.Sessions.Add(new GameSession { Id = Guid.NewGuid(), GameId = module.Id, State = module.Start(room, command) });
            room.GameState = JsonSerializer.Serialize(saved);
            room.Status = "playing";
        }
        else
        {
            if (room.Status != "playing" || room.GameState is null) throw new RoomException("stage_changed", "There is no active game", 409);
            var saved = Read(room.GameState);
            var session = saved.Sessions[^1];
            if (session.Id != command.SessionId) throw new RoomException("stage_changed", "That game has ended", 409);
            session.State = Get(session.GameId).Apply(session.State, room, actor, command);
            room.GameState = JsonSerializer.Serialize(saved);
            if (command.Action == "lobby") room.Status = "lobby";
        }
        room.Version++;
        room.LastActivityTicks = now;
    }
    public static void Facilitator(Room room, Membership actor)
    {
        if (actor.Id != room.FacilitatorId) throw new RoomException("facilitator_required", "Only the facilitator can do that", 403);
    }
    private static SavedGames Read(string json)
    {
        var session = JsonSerializer.Deserialize<SavedGames>(json) ?? throw new InvalidOperationException("Missing game state");
        if (session.SchemaVersion is not (1 or 2 or 3)) throw new InvalidOperationException("Unsupported game state version");
        // Modules migrate legacy aggregate results and twelve-item prompt usage on read.
        session.SchemaVersion = 3;
        return session;
    }
}
