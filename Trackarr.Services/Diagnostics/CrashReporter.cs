using Trackarr.Core.Exception;

namespace Trackarr.Services.Diagnostics;

public static class CrashReporter
{
    private static string CrashDir =>
        Path.Combine(
            Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.FullName ??
            AppContext.BaseDirectory, "crashes");

    public static string? LastError { get; private set; }

    public static void Write(Exception ex, string source = "Unhandled")
    {
        try
        {
            Directory.CreateDirectory(CrashDir);

            var path = Path.Combine(CrashDir, $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.log");
            var content =
                $"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\nSource: {source}\nType: {ex.GetType().FullName}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}";

            File.WriteAllText(path, content);
        }
        catch (Exception e)
        {
            LastError =
                $"{e.GetType().Name}: {e.Message}\nCrashDir: {CrashDir}\nBaseDirectory: {AppContext.BaseDirectory}";
        }
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
                    TimeStamp = DateTime.Parse(lines[0].Replace("Timestamp: ", "")),
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
}