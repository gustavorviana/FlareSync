using FlareSync.Core.Config;
using FlareSync.Core.Models;
using FlareSync.Core.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlareSync.Cli;

/// <summary>
/// Runs a sync cycle at start-up and then at the configured interval. Configuration files are re-read every
/// cycle, so changes made through the CLI apply without a restart.
/// </summary>
internal sealed class SyncWorker(
    SyncOrchestrator orchestrator,
    GlobalConfigStore globalConfig,
    ConfigPaths paths,
    TimeProvider timeProvider,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FlareSync started. Configuration directory: {Directory}. Interval: {Interval}.",
            paths.Root, await GetIntervalAsync(stoppingToken));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = await orchestrator.RunOnceAsync(force: false, stoppingToken);
                var changed = report.Entries.Count(e => e.Result.Outcome is SyncOutcome.Created or SyncOutcome.Updated);
                var failed = report.Entries.Count(e => e.Result.Outcome == SyncOutcome.Failed);
                var skipped = report.Entries.Count(e => e.Result.Outcome == SyncOutcome.Skipped);
                logger.Log(
                    changed + failed > 0 ? LogLevel.Information : LogLevel.Debug,
                    "Sync cycle finished: {Changed} changed, {Unchanged} up to date, {Skipped} skipped, {Failed} failed.",
                    changed, report.Entries.Count - changed - failed - skipped, skipped, failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync cycle failed.");
            }

            var interval = await GetIntervalAsync(stoppingToken);
            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("FlareSync stopped.");
    }

    private async Task<TimeSpan> GetIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var interval = (await globalConfig.LoadAsync(cancellationToken)).Interval;
            return interval < GlobalConfig.MinimumInterval ? GlobalConfig.MinimumInterval : interval;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Could not read the interval ({Error}); using the default.", ex.Message);
            return new GlobalConfig().Interval;
        }
    }
}
