using System.Net;
using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlareSync.Providers.Cloudflare;

public sealed class CloudflareDnsProvider(
    CloudflareConfigStore configStore,
    CloudflareApiClient api,
    ILogger<CloudflareDnsProvider> logger) : IDnsProvider
{
    public string Name => CloudflareModule.ProviderName;

    public async Task<IReadOnlyList<DnsTarget>> GetTargetsAsync(CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        return config.Records
            .Select(r => new DnsTarget(Name, r.Hostname, r.IPv4, r.IPv6) { ProviderData = r })
            .ToList();
    }

    public async Task<IReadOnlyDictionary<IpFamily, SyncResult>> UpdateAsync(DnsUpdate update, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var record = config.Find(update.Target.Hostname) ?? update.Target.ProviderData as CloudflareRecord;
        if (record is null)
        {
            return update.Changed.ToDictionary(f => f, _ => SyncResult.Failed($"record '{update.Target.Hostname}' is not configured"));
        }

        var token = configStore.GetToken(config);
        var results = new Dictionary<IpFamily, SyncResult>();

        // A and AAAA are independent records at Cloudflare: only touch the families that changed.
        foreach (var family in update.Changed)
        {
            try
            {
                results[family] = await UpsertAsync(token, record, family, update.Addresses[family], cancellationToken);
            }
            catch (FlareSyncException ex)
            {
                results[family] = SyncResult.Failed(ex.Message);
            }
        }

        return results;
    }

    internal async Task<SyncResult> UpsertAsync(
        string token, CloudflareRecord record, IpFamily family, IPAddress address, CancellationToken cancellationToken)
    {
        var type = family == IpFamily.IPv4 ? "A" : "AAAA";
        var desired = new CloudflareDnsRecord
        {
            Type = type,
            Name = record.Hostname,
            Content = address.ToString(),
            // Cloudflare always reports TTL 1 (automatic) for proxied records.
            Ttl = record.Proxied ? CloudflareRecord.AutomaticTtl : record.Ttl,
            Proxied = record.Proxied,
        };

        var existing = await api.FindRecordsAsync(token, record.ZoneId, type, record.Hostname, cancellationToken);
        if (existing.Count == 0)
        {
            await api.CreateRecordAsync(token, record.ZoneId, desired, cancellationToken);
            return SyncResult.Created($"{type} record created");
        }

        if (existing.Count > 1)
        {
            logger.LogWarning("{Hostname} has {Count} {Type} records; only the first one is managed.", record.Hostname, existing.Count, type);
        }

        var current = existing[0];
        var sameAddress = IPAddress.TryParse(current.Content, out var currentAddress) && currentAddress.Equals(address);
        if (sameAddress && current.Proxied == desired.Proxied && current.Ttl == desired.Ttl)
        {
            return SyncResult.Unchanged($"{type} record already up to date");
        }

        await api.UpdateRecordAsync(token, record.ZoneId, current.Id, desired, cancellationToken);
        return SyncResult.Updated(sameAddress ? $"{type} record settings updated" : $"{type} record changed from {current.Content}");
    }
}
