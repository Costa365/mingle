namespace TeamGames.Core.Storage;

public sealed record StoragePaths(string DirectoryPath, string DatabasePath, string KeyDirectory)
{
    public static StoragePaths Resolve(string? directory, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Contains('\0'))
        {
            throw new ArgumentException("Storage:DataDirectory must be a non-empty valid directory path.", nameof(directory));
        }

        var absolutePath = Path.GetFullPath(directory, contentRoot);
        return new(absolutePath, Path.Combine(absolutePath, "teamgames.db"), Path.Combine(absolutePath, "keys"));
    }
}
