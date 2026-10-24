using TeamGames.Core.Storage;

namespace TeamGames.Core.Tests;

public sealed class StoragePathsTests
{
    [Fact]
    public void RelativeStorageIsResolvedAgainstTheApplicationRoot()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "teamgames"));
        var paths = StoragePaths.Resolve("data", root);
        Assert.Equal(Path.Combine(root, "data", "teamgames.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(root, "data", "keys"), paths.KeyDirectory);
    }

    [Fact]
    public void AbsoluteStorageIsIndependentOfTheWorkingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "teamgames-volume");
        Assert.Equal(directory, StoragePaths.Resolve(directory, Path.GetTempPath()).DirectoryPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\0path")]
    public void InvalidConfigurationIsRejected(string? value) =>
        Assert.Throws<ArgumentException>(() => StoragePaths.Resolve(value, Path.GetTempPath()));
}
