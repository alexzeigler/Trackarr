using Trackarr.Core.Exception;
using System.Globalization;

namespace Trackarr.Services.Diagnostics;

public static class CrashReporter
{
    #region Properties

    private static string CrashDir => AppPaths.CrashDirectory;

    public static string? LastError { get; private set; }

    #endregion

    #region Public API

    public static void Write(Exception ex, string source = "Unhandled")
    {
        try
        {
            Directory.CreateDirectory(CrashDir);

            // Invariant: "HH:mm:ss" uses the culture's time separator, which ReadAll then couldn't parse back.
            var path = Path.Combine(CrashDir,
                string.Create(CultureInfo.InvariantCulture, $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.log"));
            var content = string.Create(CultureInfo.InvariantCulture,
                $"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\nSource: {source}\nType: {ex.GetType().FullName}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}");

            File.WriteAllText(path, content);
        }
        catch (Exception e)
        {
            LastError =
                $"{e.GetType().Name}: {e.Message}\nCrashDir: {CrashDir}\nBaseDirectory: {AppContext.BaseDirectory}";
        }
    }

    /// <summary>
    ///     "Timestamp: 2026-11-08 14:03:22", UTC. Files from older versions used the server's time separator, which
    ///     the server's culture still reads.
    /// </summary>
    internal static DateTime ParseTimestamp(string line)
    {
        var value = line.Replace("Timestamp: ", "", StringComparison.Ordinal);
        return DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var at)
            ? at
            : DateTime.Parse(value, CultureInfo.CurrentCulture);
    }

    public static IEnumerable<(string Path, CrashReport Report)> ReadAll()
    {
        if (!Directory.Exists(CrashDir))
            yield break;

        foreach (var file in Directory.GetFiles(CrashDir, "crash_*.log"))
        {
            CrashReport? report = null;
            try
            {
                var lines = File.ReadAllLines(file);
                report = new CrashReport
                {
                    TimeStamp = ParseTimestamp(lines[0]),
                    Source = lines[1].Replace("Source: ", ""),
                    ExceptionType = lines[2].Replace("Type: ", ""),
                    Message = lines[3].Replace("Message: ", ""),
                    StackTrace = string.Join("\n", lines.Skip(5))
                };
            }
            catch (Exception e)
            {
                LastError = $"Failed to parse crash file '{file}': {e.GetType().Name}: {e.Message}";
            }

            if (report is not null)
                yield return (file, report);
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            LastError = $"Failed to delete crash file '{path}': {e.GetType().Name}: {e.Message}";
        }
    }

    #endregion
}
