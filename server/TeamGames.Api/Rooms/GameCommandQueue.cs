using TeamGames.Core.Games;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Rooms;

public sealed class GameCommandQueue(IServiceScopeFactory scopes)
{
    private readonly CommandBatcher<RoomStore.QueuedGame, RoomStore.GameReply> batcher = new(async (roomId, requests) =>
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RoomStore>().PlayBatchAsync(roomId, requests);
    }, () => Task.Delay(5));
    public async Task<CommandResult> PlayAsync(Guid roomId, Guid guestId, GameCommand command)
    {
        var reply = await batcher.Enqueue(roomId, new(guestId, command));
        return reply.Result ?? throw reply.Error!;
    }
}
