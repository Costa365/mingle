namespace TeamGames.Core.Games;

public sealed class GameCatalog
{
    public IReadOnlyList<GameSummary> Games { get; } = Array.AsReadOnly<GameSummary>(
    [
        new("this-or-that", "This or That", "Two choices. A whole lot to talk about.",
            "Pick a side, discover what you have in common, and make a case for your choice.",
            "Pick a side", 1, 10),
        new("two-truths-and-a-lie", "Two Truths and a Lie", "Everyone has a story. One of these isn't true.",
            "Share two true stories and one convincing invention. Your teammates guess which is the lie.",
            "Find the bluff", 2, 15),
        new("never-have-i-ever", "Never Have I Ever", "Find the little adventures you share.",
            "Respond to a lighthearted statement, see the group's answers, and share a story if you like.",
            "Swap stories", 1, 10),
        new("would-you-rather", "Would You Rather?", "Delightfully difficult decisions, together.",
            "Choose between two unexpected possibilities. Compare the results and explain your reasoning.",
            "What if?", 1, 10),
        new("most-likely-to", "Most Likely To", "Celebrate the characters on your team.",
            "Nominate a teammate for a playful prompt and discover who the group has in mind.",
            "Meet your team", 2, 10)
    ]);

    public GameSummary? Find(string id) =>
        Games.FirstOrDefault(game => string.Equals(game.Id, id, StringComparison.Ordinal));
}
