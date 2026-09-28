using FlareSync.Cli.Logging;
using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Commands.Builtin;
using FlareSync.Core.Config;
using FlareSync.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlareSync.Cli;

internal static class ServiceSetup
{
    public static void AddFlareSync(IServiceCollection services, ConfigPaths paths, IEnumerable<IProviderModule> modules)
    {
        services.AddFlareSyncCore(paths);
        foreach (var module in modules)
        {
            module.ConfigureServices(services);
        }
    }

    /// <summary>
    /// Console logging with <see cref="FlareSyncConsoleFormatter"/> (no category names or event ids).
    /// </summary>
    /// <param name="systemd">Running under systemd: journald priority prefixes instead of timestamp and level.</param>
    /// <param name="timestamps">Prefix terminal lines with the local time.</param>
    /// <param name="toStandardError">Write every level to stderr (CLI commands keep stdout for their output).</param>
    public static void ConfigureLogging(
        ILoggingBuilder logging, bool verbose, LogLevel defaultLevel, bool systemd, bool timestamps, bool toStandardError)
    {
        logging.AddConsole(o =>
        {
            o.FormatterName = FlareSyncConsoleFormatter.FormatterName;
            o.LogToStandardErrorThreshold = toStandardError ? LogLevel.Trace : LogLevel.None;
        });
        logging.AddConsoleFormatter<FlareSyncConsoleFormatter, FlareSyncConsoleFormatterOptions>(o =>
        {
            o.Systemd = systemd;
            o.TimestampFormat = timestamps ? "yyyy-MM-dd HH:mm:ss " : null;
            o.IncludeStackTrace = verbose;
        });

        logging.SetMinimumLevel(verbose ? LogLevel.Debug : defaultLevel);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        // Host start-up chatter ("Application started", "Content root path"...); FlareSync logs its own start/stop.
        logging.AddFilter("Microsoft.Hosting", verbose ? LogLevel.Debug : LogLevel.Warning);
        // Retry attempts are noise; final failures are reported by FlareSync itself.
        logging.AddFilter("Polly", verbose ? LogLevel.Debug : LogLevel.None);
    }

    /// <summary>Service provider for a single CLI command invocation.</summary>
    public static IServiceProvider BuildForCommand(IParsedValues values, IEnumerable<IProviderModule> modules)
    {
        var paths = ConfigPaths.Resolve(values.Get(CoreOptions.ConfigDir));
        var verbose = values.Get(CoreOptions.Verbose);

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            ConfigureLogging(logging, verbose, LogLevel.Warning, systemd: false, timestamps: false, toStandardError: true);
            if (!verbose)
            {
                // The sync command prints its own report.
                logging.AddFilter(typeof(SyncOrchestrator).FullName, LogLevel.None);
            }
        });

        AddFlareSync(services, paths, modules);
        return services.BuildServiceProvider();
    }
}
