namespace TeamGames.Core.Games;

public sealed record GameSummary(
    string Id,
    string Name,
    string Description,
    string Instructions,
    string Category,
    int MinimumPlayers,
    int SuggestedMinutes);
