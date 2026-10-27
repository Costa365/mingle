using TeamGames.Core.Rooms;
namespace TeamGames.Core.Tests;

public sealed class CommandBatcherTests
{
    [Fact]
    public async Task AcknowledgementsWaitForTheWholeBatchCommitAndKeepTheirIndividualResults()
    {
        var collect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var queue = new CommandBatcher<int, int>(async (_, inputs) => { calls++; processing.SetResult(); await commit.Task; return inputs.Select(i => i * 2).ToArray(); }, () => collect.Task);
        var room = Guid.NewGuid(); var first = queue.Enqueue(room, 2); var second = queue.Enqueue(room, 4);
        collect.SetResult(); await processing.Task;
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); commit.SetResult();
        var values = await Task.WhenAll(first, second); Assert.Equal(new[] { 4, 8 }, values); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task AFailedTransactionRejectsEveryAcknowledgementAndFutureWorkCanProceed()
    {
        var collect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var fail = true;
        var queue = new CommandBatcher<int, int>((_, inputs) => fail ? throw new IOException("failed commit") : Task.FromResult<IReadOnlyList<int>>(inputs), () => collect.Task);
        var room = Guid.NewGuid(); var first = queue.Enqueue(room, 1); var second = queue.Enqueue(room, 2); collect.SetResult();
        await Assert.ThrowsAsync<IOException>(() => first); await Assert.ThrowsAsync<IOException>(() => second);
        fail = false; Assert.Equal(3, await queue.Enqueue(room, 3));
    }
    [Fact]
    public async Task QueueBoundsApplyPerRoomWithoutBlockingAnotherRoom()
    {
        var collect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new CommandBatcher<int, int>((_, inputs) => Task.FromResult<IReadOnlyList<int>>(inputs), () => collect.Task, 1);
        var room = Guid.NewGuid(); _ = queue.Enqueue(room, 1);
        var error = await Assert.ThrowsAsync<RoomException>(() => queue.Enqueue(room, 2)); Assert.Equal(429, error.Status);
        _ = queue.Enqueue(Guid.NewGuid(), 3); collect.SetResult();
    }
}
