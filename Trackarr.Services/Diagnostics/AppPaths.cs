namespace Trackarr.Services.Diagnostics;

/// <summary>
///     Where Trackarr keeps files that must survive restarts and image updates. Program.cs sets
///     <see cref="DataDirectory" /> at startup to the database's folder (/config in Docker). Before that, or in
///     tests, it falls back to a "data" folder next to the app.
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectory;

    public static string DataDirectory
    {
        get => _dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data");
        set => _dataDirectory = string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string CrashDirectory => Path.Combine(DataDirectory, "crashes");

    /// <summary>Deletes files matching <paramref name="pattern" /> last written before the cutoff. Returns how many.</summary>
    public static int DeleteFilesOlderThan(string directory, string pattern, TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero || !Directory.Exists(directory)) return 0;

        var cutoff = DateTime.UtcNow - maxAge;
        var deleted = 0;

        foreach (var file in Directory.GetFiles(directory, pattern))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                File.Delete(file);
                deleted++;
            }
            catch (IOException)
            {
                // In use (today's log) or already gone: skip it, the next cleanup tries again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
