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
            entries.AddRange(await SyncTargetAsync(provider, target, detections, force, cancellationToken));
        }

        return new SyncReport(detections.Values.ToList(), entries);
    }

    private async Task<List<SyncEntry>> SyncTargetAsync(
        IDnsProvider provider, DnsTarget target, IReadOnlyDictionary<IpFamily, IpDetection> detections, bool force, CancellationToken cancellationToken)
    {
        var families = Enum.GetValues<IpFamily>().Where(target.Uses).ToList();
        var addresses = new Dictionary<IpFamily, IPAddress>();
        var changed = new HashSet<IpFamily>();
        var results = new Dictionary<IpFamily, SyncResult>();

        foreach (var family in families)
        {
            if (detections[family].Address is not { } address)
            {
                results[family] = SyncResult.Skipped($"no public {family} address detected");
                continue;
            }

            addresses[family] = address;
            var last = force ? null : await state.GetAsync(provider.Name, target.Hostname, family, cancellationToken);
            if (last?.Address == address.ToString())
            {
                results[family] = SyncResult.Unchanged("address unchanged since last sync");
            }
            else
            {
                changed.Add(family);
            }
        }

        if (changed.Count > 0)
        {
            IReadOnlyDictionary<IpFamily, SyncResult> providerResults;
            try
            {
                providerResults = await provider.UpdateAsync(new DnsUpdate(target, addresses, changed), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                providerResults = changed.ToDictionary(f => f, _ => SyncResult.Failed(ex.Message));
            }

            foreach (var family in changed)
            {
                var result = providerResults.GetValueOrDefault(family) ?? SyncResult.Failed("the provider returned no result");
                var text = addresses[family].ToString();
                if (result.Succeeded)
                {
                    await state.SetAsync(provider.Name, target.Hostname, family, text, cancellationToken);
                }

                LogResult(provider.Name, target.Hostname, family, text, result);
                results[family] = result;
            }
        }

        return families
            .Select(f => new SyncEntry(provider.Name, target.Hostname, f, detections[f].Address, results[f]))
            .ToList();
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
