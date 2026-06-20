using Trackarr.Core.Event;

namespace Trackarr.Services.Diagnostics;

public class LogFileWriter
{
    private static readonly Lock _lock = new();
    private static readonly long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static readonly string LogDir =
        Path.Combine(
            Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.FullName ??
            AppContext.BaseDirectory, "logs");

    private string CurrentLogPath => Path.Combine(LogDir, $"trackarr_{DateTime.UtcNow:yyyyMMdd}.log");

    public string? LastError { get; private set; }

    public void Write(LogEvent evt)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var path = ResolveLogPath();
                var line = $"[{evt.TimeStamp:yyyy-MM-dd HH:mm:ss}] [{evt.LogLevel}] [{evt.Source}] {evt.Message}";
                if (evt.Exception is not null)
                    line += $"\n  Exception: {evt.Exception}";
                File.AppendAllText(path, line + "\n");
            }
            catch (Exception ex)
            {
                LastError =
                    $"{ex.GetType().Name}: {ex.Message}\nLogDir: {LogDir}\nBaseDirectory: {AppContext.BaseDirectory}";
            }
        }
    }

    private string ResolveLogPath()
    {
        var path = CurrentLogPath;

        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileSizeBytes)
            return path;

        var i = 1;
        while (true)
        {
            var rolled = Path.Combine(LogDir, $"trackarr_{DateTime.UtcNow:yyyyMMdd}_{i}.log");
            if (!File.Exists(rolled) || new FileInfo(rolled).Length < MaxFileSizeBytes)
                return rolled;
            i++;
        }
    }
}