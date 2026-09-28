using System.Net;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlareSync.Core.Sync;

public sealed record SyncEntry(string Provider, string Hostname, IpFamily Family, IPAddress? Address, SyncResult Result);

public sealed record SyncReport(IReadOnlyList<IpDetection> Detections, IReadOnlyList<SyncEntry> Entries)
{
    public bool HasFailures => Entries.Any(e => e.Result.Outcome == SyncOutcome.Failed);
}

/// <summary>Runs one sync cycle: detect addresses once, then upsert every record whose address changed.</summary>
public sealed class SyncOrchestrator(
    IEnumerable<IDnsProvider> providers,
    IIpResolver ipResolver,
    SyncStateStore state,
    ILogger<SyncOrchestrator> logger)
{
    private readonly Dictionary<IpFamily, IPAddress?> _lastDetected = [];

    public async Task<SyncReport> RunOnceAsync(bool force, CancellationToken cancellationToken)
    {
        var targets = new List<(IDnsProvider Provider, DnsTarget Target)>();
        var entries = new List<SyncEntry>();

        foreach (var provider in providers)
        {
            try
            {
                foreach (var target in await provider.GetTargetsAsync(cancellationToken))
                {
                    targets.Add((provider, target));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Could not load records of provider {Provider}: {Error}", provider.Name, ex.Message);
                entries.Add(new SyncEntry(provider.Name, "*", IpFamily.IPv4, null, SyncResult.Failed(ex.Message)));
            }
        }

        if (targets.Count == 0)
        {
            logger.LogInformation("No records configured.");
            return new SyncReport([], entries);
        }

        var detections = new Dictionary<IpFamily, IpDetection>();
        foreach (var family in Enum.GetValues<IpFamily>())
        {
            if (!targets.Any(t => t.Target.Uses(family)))
            {
                continue;
            }

            var detection = await ipResolver.DetectAsync(family, cancellationToken);
            detections[family] = detection;
            LogDetection(detection);
        }

        foreach (var (provider, target) in targets)
        {
            foreach (var family in Enum.GetValues<IpFamily>())
            {
                if (!target.Uses(family))
                {
                    continue;
                }

                var detection = detections[family];
                var result = detection.Address is { } address
                    ? await SyncRecordAsync(provider, target, family, address, force, cancellationToken)
                    : SyncResult.Skipped($"no public {family} address detected");
                entries.Add(new SyncEntry(provider.Name, target.Hostname, family, detection.Address, result));
            }
        }

        return new SyncReport(detections.Values.ToList(), entries);
    }

    private async Task<SyncResult> SyncRecordAsync(
        IDnsProvider provider, DnsTarget target, IpFamily family, IPAddress address, bool force, CancellationToken cancellationToken)
    {
        var text = address.ToString();
        if (!force)
        {
            var last = await state.GetAsync(provider.Name, target.Hostname, family, cancellationToken);
            if (last?.Address == text)
            {
                return SyncResult.Unchanged("address unchanged since last sync");
            }
        }

        SyncResult result;
        try
        {
            result = await provider.UpsertAsync(target, family, address, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = SyncResult.Failed(ex.Message);
        }

        if (result.Succeeded)
        {
            await state.SetAsync(provider.Name, target.Hostname, family, text, cancellationToken);
        }

        LogResult(provider.Name, target.Hostname, family, text, result);
        return result;
    }

    private void LogResult(string provider, string hostname, IpFamily family, string address, SyncResult result)
    {
        switch (result.Outcome)
        {
            case SyncOutcome.Created:
                logger.LogInformation("[{Provider}] {Hostname} ({Family}): record created pointing to {Address}.",
                    provider, hostname, family, address);
                break;
            case SyncOutcome.Updated:
                logger.LogInformation("[{Provider}] {Hostname} ({Family}): updated to {Address} ({Details}).",
                    provider, hostname, family, address, result.Message);
                break;
            case SyncOutcome.Unchanged:
                logger.LogInformation("[{Provider}] {Hostname} ({Family}): already points to {Address}, nothing to change.",
                    provider, hostname, family, address);
                break;
            default:
                logger.LogError("[{Provider}] {Hostname} ({Family}): failed to update to {Address}: {Error}",
                    provider, hostname, family, address, result.Message);
                break;
        }
    }

    /// <summary>
    /// Logs the public address when first seen or changed, and detection failures only when they start or end,
    /// so the service log is not flooded every cycle.
    /// </summary>
    private void LogDetection(IpDetection detection)
    {
        lock (_lastDetected)
        {
            var hadPrevious = _lastDetected.TryGetValue(detection.Family, out var previous);
            _lastDetected[detection.Family] = detection.Address;

            if (detection.Address is { } address)
            {
                if (!hadPrevious)
                {
                    logger.LogInformation("Public {Family} address: {Address} (source: {Source}).", detection.Family, address, detection.Source);
                }
                else if (previous is null)
                {
                    logger.LogInformation("Public {Family} address detected again: {Address} (source: {Source}).", detection.Family, address, detection.Source);
                }
                else if (!previous.Equals(address))
                {
                    logger.LogInformation("Public {Family} address changed from {Previous} to {Address} (source: {Source}).",
                        detection.Family, previous, address, detection.Source);
                }
                else
                {
                    logger.LogDebug("Public {Family} address unchanged: {Address}.", detection.Family, address);
                }
            }
            else
            {
                var errors = string.Join("; ", detection.Errors);
                if (!hadPrevious || previous is not null)
                {
                    logger.LogWarning("Could not detect the public {Family} address; its records are skipped until it is available again. {Errors}",
                        detection.Family, errors);
                }
                else
                {
                    logger.LogDebug("Public {Family} address still not detected: {Errors}", detection.Family, errors);
                }
            }
        }
    }
}
