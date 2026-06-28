using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Trackarr.MockServer;

public sealed class CleanConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool ShowTimestamp { get; set; } = true;
    public bool ShowLevel { get; set; } = true;
}

public sealed class CleanConsoleFormatter(IOptionsMonitor<CleanConsoleFormatterOptions> options)
    : ConsoleFormatter("clean")
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[90m";

    private static readonly Dictionary<LogLevel, (string Label, string Ansi)> Levels = new()
    {
        [LogLevel.Trace] = ("trce", "\x1b[90m"),
        [LogLevel.Debug] = ("dbug", "\x1b[37m"),
        [LogLevel.Information] = ("info", "\x1b[32m"),
        [LogLevel.Warning] = ("warn", "\x1b[33m"),
        [LogLevel.Error] = ("fail", "\x1b[31m"),
        [LogLevel.Critical] = ("crit", "\x1b[91m")
    };

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopeProvider,
        TextWriter writer)
    {
        var message = entry.Formatter(entry.State, entry.Exception);
        if (message is null) return;

        var opts = options.CurrentValue;
        var (label, ansi) = Levels.GetValueOrDefault(entry.LogLevel, ("????", "\x1b[37m"));

        if (opts.ShowTimestamp)
            writer.Write($"{Dim}{DateTime.Now:HH:mm:ss}{Reset} ");

        if (opts.ShowLevel)
            writer.Write($"{ansi}{label}{Reset}: ");

        writer.WriteLine(message);

        if (entry.Exception is not null)
            writer.WriteLine($"{Dim}{entry.Exception}{Reset}");
    }
}
