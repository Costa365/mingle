namespace TeamGames.Core.Rooms;

// Scheduling only: the transaction processor still owns authorization and rules.
public sealed class CommandBatcher<T, TResult>(Func<Guid, IReadOnlyList<T>, Task<IReadOnlyList<TResult>>> apply,
    Func<Task> wait, int queueLimit = 512)
{
    private sealed record Work(T Input, TaskCompletionSource<TResult> Completion);
    private readonly object sync = new();
    private readonly Dictionary<Guid, List<Work>> rooms = [];
    public Task<TResult> Enqueue(Guid roomId, T input)
    {
        lock (sync)
        {
            var exists = rooms.TryGetValue(roomId, out var pending);
            if (!exists) { pending = []; rooms.Add(roomId, pending); }
            if (pending!.Count >= queueLimit) throw new RoomException("room_busy", "This room is busy; retry the same action", 429);
            var work = new Work(input, new(TaskCreationOptions.RunContinuationsAsynchronously));
            pending.Add(work);
            if (!exists) _ = Run(roomId, pending);
            return work.Completion.Task;
        }
    }
    private async Task Run(Guid roomId, List<Work> pending)
    {
        while (true)
        {
            await wait();
            Work[] batch;
            lock (sync) { batch = pending.ToArray(); pending.Clear(); }
            try
            {
                var results = await apply(roomId, batch.Select(w => w.Input).ToArray());
                if (results.Count != batch.Length) throw new InvalidOperationException("Incomplete command batch result");
                for (var i = 0; i < batch.Length; i++) batch[i].Completion.TrySetResult(results[i]);
            }
            catch (Exception error) { foreach (var work in batch) work.Completion.TrySetException(error); }
            lock (sync) { if (pending.Count > 0) continue; rooms.Remove(roomId); return; }
        }
    }
}
