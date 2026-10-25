using System.Reflection;
using System.Text;
using System.Text.Json;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;
using TeamGames.Core.Games;

// Generate from the actual public DTOs, never from EF entities or credentials.
Type[] types = [typeof(PromptInput), typeof(PromptCommand), typeof(PromptView), typeof(PromptPage), typeof(PlayerChoiceView), typeof(GameCommand), typeof(RoundResult), typeof(GameView), typeof(ParticipantView), typeof(RoomSnapshot), typeof(EntryRequest), typeof(RoomCommand), typeof(CommandResult), typeof(SessionView), typeof(RoomErrorView)];
var nullable = new NullabilityInfoContext();
var output = new StringBuilder("// Generated from TeamGames.Core room DTOs. Run the contracts tool to update.\n\n");
foreach (var type in types)
{
    var optional = type.GetConstructors().SelectMany(c => c.GetParameters()).Where(p => p.HasDefaultValue)
        .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    output.Append("export interface ").Append(type.Name).Append(" {\n");
    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
    {
        output.Append("  ").Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
        if (optional.Contains(property.Name)) output.Append('?');
        output.Append(": ").Append(Format(nullable.Create(property))).Append("\n");
    }
    output.Append("}\n\n");
}
var target = args.FirstOrDefault(a => a != "--check") ?? "web/src/api/room-contract.generated.ts";
var generated = output.ToString().TrimEnd() + "\n";
if (args.Contains("--check"))
{
    if (!File.Exists(target) || File.ReadAllText(target).Replace("\r\n", "\n") != generated)
    {
        Console.Error.WriteLine("Room contracts are stale. Run dotnet run --project tools/TeamGames.Contracts");
        return 1;
    }
    Console.WriteLine("Generated room HTTP/hub contracts match the server DTOs.");
}
else File.WriteAllText(target, generated);
return 0;

static string Format(NullabilityInfo info)
{
    var type = info.Type;
    var valueType = Nullable.GetUnderlyingType(type);
    if (valueType is not null) return Primitive(valueType) + " | null";
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        return Format(info.GenericTypeArguments[0]) + "[]";
    var text = Primitive(type);
    return info.ReadState == NullabilityState.Nullable ? text + " | null" : text;
}

static string Primitive(Type type) => type == typeof(string) || type == typeof(Guid) ? "string"
    : type == typeof(bool) ? "boolean"
    : type == typeof(int) || type == typeof(long) ? "number"
    : type.Name;
