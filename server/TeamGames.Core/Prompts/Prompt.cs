using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamGames.Core.Rooms;

namespace TeamGames.Core.Prompts;

public sealed class Prompt
{
    public string Id { get; set; } = "";
    public Guid? RoomId { get; set; }
    public string GameId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Text { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string Source { get; set; } = "built-in";
    public bool Enabled { get; set; } = true;
    public long Revision { get; set; } = 1;
    public string NormalizedKey { get; set; } = "";
    public string? SeedFingerprint { get; set; }
}
public sealed record PromptInput(string GameId, string Category, IReadOnlyList<string> Tags, string Text, string OptionA = "", string OptionB = "", bool Enabled = true);
public sealed record PromptCommand(Guid CommandId, long ExpectedVersion, string Action, PromptInput? Input = null, string? PromptId = null, long PromptRevision = 0);
public sealed record PromptView(string Id, string GameId, string Category, IReadOnlyList<string> Tags, string Text,
    string OptionA, string OptionB, string Source, bool Enabled, long Revision, bool Used);
public sealed record PromptPage(long RoomVersion, IReadOnlyList<PromptView> Prompts);

public static class PromptRules
{
    public static readonly string[] GameIds = ["this-or-that", "would-you-rather", "never-have-i-ever", "most-likely-to"];
    public static string Normalize(string text) => string.Join(' ', text.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string Clean(string? text, int maximum, string label, bool optional = false)
    {
        var value = (text ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if ((!optional && value.Length == 0) || value.Length > maximum || value.Any(char.IsControl))
            throw new RoomException("invalid_prompt", $"{label} must contain {(optional ? "0" : "1")}–{maximum} characters without control characters");
        return value;
    }
    public static Prompt Create(PromptInput input, string id, Guid? roomId)
    {
        if (!GameIds.Contains(input.GameId)) throw new RoomException("invalid_prompt", "Choose a supported prompt game");
        if (input.Tags is null || input.Tags.Count > 8) throw new RoomException("invalid_prompt", "Use up to eight tags");
        var tags = input.Tags.Select(t => Clean(t, 32, "Each tag").ToLowerInvariant()).ToArray();
        if (tags.Any(t => t.Contains(',')) || tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Length)
            throw new RoomException("invalid_prompt", "Use distinct tags without commas");
        var binary = input.GameId is "this-or-that" or "would-you-rather";
        var a = Clean(input.OptionA, 120, "Option A", !binary);
        var b = Clean(input.OptionB, 120, "Option B", !binary);
        if (binary && Normalize(a) == Normalize(b)) throw new RoomException("invalid_prompt", "The options must be different");
        if (!binary && (a.Length > 0 || b.Length > 0)) throw new RoomException("invalid_prompt", "This game uses a statement rather than options");
        var prompt = new Prompt
        {
            Id = id,
            RoomId = roomId,
            GameId = input.GameId,
            Category = Clean(input.Category, 48, "Category"),
            Tags = string.Join(',', tags),
            Text = Clean(input.Text, 240, "Prompt text"),
            OptionA = a,
            OptionB = b,
            Source = roomId is null ? "built-in" : "room",
            Enabled = input.Enabled
        };
        var content = binary ? string.Join('|', new[] { Normalize(a), Normalize(b) }.Order(StringComparer.Ordinal)) : Normalize(prompt.Text);
        prompt.NormalizedKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt.GameId + "|" + content)));
        return prompt;
    }
    public static string Fingerprint(Prompt prompt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { prompt.GameId, prompt.Category, prompt.Tags, prompt.Text, prompt.OptionA, prompt.OptionB, prompt.Enabled, prompt.Source }))));
    public static bool SeedConflicts(Prompt seed, IEnumerable<Prompt> existing) => existing.Any(p => p.Id != seed.Id && p.NormalizedKey == seed.NormalizedKey);
    public static bool CanUpdateSeed(Prompt prompt) => prompt.SeedFingerprint == Fingerprint(prompt);
    public static bool Matches(Prompt prompt, string gameId, string? category, string? tag, string? search) =>
        prompt.Enabled && prompt.GameId == gameId &&
        (string.IsNullOrWhiteSpace(category) || Normalize(prompt.Category) == Normalize(category)) &&
        (string.IsNullOrWhiteSpace(tag) || prompt.Tags.Split(',').Any(t => Normalize(t) == Normalize(tag))) &&
        (string.IsNullOrWhiteSpace(search) || Normalize($"{prompt.Text} {prompt.OptionA} {prompt.OptionB} {prompt.Category} {prompt.Tags}").Contains(Normalize(search), StringComparison.Ordinal));
    public static PromptView View(Prompt prompt, bool used) => new(prompt.Id, prompt.GameId, prompt.Category,
        prompt.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries), prompt.Text, prompt.OptionA, prompt.OptionB, prompt.Source, prompt.Enabled, prompt.Revision, used);
}

public static class PromptLibrary
{
    private sealed record Seed(string Id, string GameId, string Category, IReadOnlyList<string> Tags, string Text, string OptionA, string OptionB);
    public static IReadOnlyList<Prompt> All { get; } = Load();
    private static IReadOnlyList<Prompt> Load()
    {
        using var stream = typeof(PromptLibrary).Assembly.GetManifestResourceStream("TeamGames.Core.Prompts.library.json")!;
        var seeds = JsonSerializer.Deserialize<Seed[]>(stream, JsonSerializerOptions.Web)!;
        var prompts = seeds.Select(s => PromptRules.Create(new(s.GameId, s.Category, s.Tags, s.Text, s.OptionA, s.OptionB), s.Id, null)).ToArray();
        if (prompts.Select(p => p.Id).Distinct().Count() != prompts.Length || prompts.Select(p => p.NormalizedKey).Distinct().Count() != prompts.Length)
            throw new InvalidOperationException("Duplicate library prompt");
        if (prompts.Count(p => p.GameId == "this-or-that") < 200 || prompts.Where(p => p.GameId == "this-or-that").Select(p => p.Category).Distinct().Count() < 8 ||
            PromptRules.GameIds.Skip(1).Any(id => prompts.Count(p => p.GameId == id) < 50)) throw new InvalidOperationException("Incomplete launch prompt library");
        return Array.AsReadOnly(prompts);
    }
}
