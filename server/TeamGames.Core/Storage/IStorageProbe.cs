namespace TeamGames.Core.Storage;

public interface IStorageProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
