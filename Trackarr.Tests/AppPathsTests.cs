using Trackarr.Services.Diagnostics;

namespace Trackarr.Tests;

[CollectionDefinition(nameof(AppPathsCollection), DisableParallelization = true)]
public class AppPathsCollection;

/// <summary>
///     Regression guards for H8: logs and crash reports must go to the data directory (/config in Docker),
///     not four folders up from the binaries, and old files must be pruned.
/// </summary>
[Collection(nameof(AppPathsCollection))]
public class AppPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"trackarr-paths-{Guid.NewGuid():N}");

    public AppPathsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void LogAndCrashDirectories_AreUnderTheDataDirectory()
    {
        var previous = AppPaths.DataDirectory;
        try
        {
            AppPaths.DataDirectory = _dir;

            Assert.Equal(Path.Combine(_dir, "logs"), AppPaths.LogDirectory);
            Assert.Equal(Path.Combine(_dir, "crashes"), AppPaths.CrashDirectory);

            CrashReporter.Write(new InvalidOperationException("boom"), "Test");
            var crash = Assert.Single(Directory.GetFiles(AppPaths.CrashDirectory, "crash_*.log"));
            Assert.Contains("boom", File.ReadAllText(crash));
        }
        finally
        {
            AppPaths.DataDirectory = previous;
        }
    }

    [Fact]
    public void DeleteFilesOlderThan_RemovesOnlyOldMatchingFiles()
    {
        var oldLog = Touch("trackarr_20260101.log", DateTime.UtcNow.AddDays(-20));
        var newLog = Touch("trackarr_20261104.log", DateTime.UtcNow.AddDays(-1));
        var otherFile = Touch("notes.txt", DateTime.UtcNow.AddDays(-20));

        var deleted = AppPaths.DeleteFilesOlderThan(_dir, "trackarr_*.log", TimeSpan.FromDays(14));

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldLog));
        Assert.True(File.Exists(newLog));
        Assert.True(File.Exists(otherFile));
    }

    [Fact]
    public void DeleteFilesOlderThan_MissingDirectoryOrZeroAge_DoesNothing()
    {
        Assert.Equal(0, AppPaths.DeleteFilesOlderThan(Path.Combine(_dir, "nope"), "*.log", TimeSpan.FromDays(1)));

        Touch("trackarr_1.log", DateTime.UtcNow.AddYears(-1));
        Assert.Equal(0, AppPaths.DeleteFilesOlderThan(_dir, "*.log", TimeSpan.Zero));
    }

    private string Touch(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // best effort
        }
    }
}
