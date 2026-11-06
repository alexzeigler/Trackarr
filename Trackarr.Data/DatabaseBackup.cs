using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Trackarr.Data;

/// <summary>
///     Copies the database before an upgrade changes it (AUDIT F13), so a failed or unwanted migration can be undone
///     by putting the copy back.
/// </summary>
public static class DatabaseBackup
{
    public const string FilePrefix = "trackarr_pre_";
    public const int DefaultKeep = 5;

    /// <summary>
    ///     When <paramref name="db" /> has migrations waiting and isn't brand new, writes a copy to
    ///     <paramref name="backupDirectory" /> as <c>trackarr_pre_&lt;version&gt;_&lt;utc time&gt;.db</c> and returns its
    ///     path. Returns null when there's nothing to protect. Only the newest <paramref name="keep" /> backups are
    ///     kept.
    /// </summary>
    public static async Task<string?> BackupBeforeMigrateAsync(AppDbContext db, string backupDirectory,
        string version, int keep = DefaultKeep, CancellationToken cancellationToken = default)
    {
        if (!(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()) return null;

        // A new database has nothing worth keeping yet.
        if (!(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any()) return null;

        Directory.CreateDirectory(backupDirectory);

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupDirectory, $"{FilePrefix}{SafeFileName(version)}_{stamp}.db");

        // VACUUM INTO writes a consistent, compacted copy, even while WAL is in use. SQLite accepts the file name as
        // a bound parameter.
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}", cancellationToken);

        Prune(backupDirectory, keep);
        return path;
    }

    /// <summary>Deletes all but the newest <paramref name="keep" /> backups in <paramref name="backupDirectory" />.</summary>
    public static void Prune(string backupDirectory, int keep)
    {
        if (!Directory.Exists(backupDirectory)) return;

        var old = new DirectoryInfo(backupDirectory).GetFiles($"{FilePrefix}*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(Math.Max(keep, 1));

        foreach (var file in old)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // In use or already gone. The next upgrade tries again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string SafeFileName(string version)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(version.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
        return safe.Length == 0 ? "unknown" : safe;
    }
}
