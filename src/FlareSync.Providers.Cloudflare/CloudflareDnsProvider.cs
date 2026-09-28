using System.Net;
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

    public async Task<SyncResult> UpsertAsync(DnsTarget target, IpFamily family, IPAddress address, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var record = config.Find(target.Hostname) ?? target.ProviderData as CloudflareRecord;
        if (record is null)
        {
            return SyncResult.Failed($"record '{target.Hostname}' is not configured");
        }

        var token = configStore.GetToken(config);
        return await UpsertAsync(token, record, family, address, cancellationToken);
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
