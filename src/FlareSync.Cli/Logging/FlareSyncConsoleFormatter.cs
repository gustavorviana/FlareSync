using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace FlareSync.Cli.Logging;

internal sealed class FlareSyncConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>Write journald priority prefixes (<c>&lt;3&gt;</c>, <c>&lt;6&gt;</c>...) instead of timestamp and level.</summary>
    public bool Systemd { get; set; }

    /// <summary>Append exception stack traces (otherwise only the exception message).</summary>
    public bool IncludeStackTrace { get; set; }
}

/// <summary>
/// Plain log lines without category names or event ids:
/// terminal <c>2026-09-28 12:56:04 INFO  message</c>, systemd <c>&lt;6&gt;message</c>.
/// </summary>
internal sealed class FlareSyncConsoleFormatter(IOptionsMonitor<FlareSyncConsoleFormatterOptions> options)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "flaresync";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        var settings = options.CurrentValue;
        var line = new StringBuilder();

        if (settings.Systemd)
        {
            line.Append('<').Append(SyslogPriority(logEntry.LogLevel)).Append('>');
        }
        else
        {
            if (!string.IsNullOrEmpty(settings.TimestampFormat))
            {
                var now = settings.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
                line.Append(now.ToString(settings.TimestampFormat));
            }

            line.Append(Label(logEntry.LogLevel)).Append(' ');
        }

        line.Append(message);

        if (logEntry.Exception is { } exception)
        {
            line.Append(string.IsNullOrEmpty(message) ? "" : " - ")
                .Append(settings.IncludeStackTrace ? exception.ToString() : exception.Message);
        }

        // journald treats every line as a separate entry.
        textWriter.WriteLine(settings.Systemd ? line.Replace(Environment.NewLine, " ").Replace('\n', ' ').ToString() : line.ToString());
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "FATAL",
        _ => "     ",
    };

    private static int SyslogPriority(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => 7,
        LogLevel.Information => 6,
        LogLevel.Warning => 4,
        LogLevel.Error => 3,
        LogLevel.Critical => 2,
        _ => 6,
    };
}
