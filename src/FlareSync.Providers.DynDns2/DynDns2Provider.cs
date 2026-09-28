using System.Globalization;
using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Models;

namespace FlareSync.Providers.DynDns2;

/// <summary>
/// Sends one DynDNS2 update per host with every detected address, and enforces the protocol rules:
/// fatal answers block further updates until the user intervenes, server errors wait 30 minutes.
/// </summary>
public sealed class DynDns2Provider(
    DynDns2Preset preset,
    DynDns2ConfigStore configStore,
    DynDns2Client client,
    TimeProvider timeProvider) : IDnsProvider
{
    public string Name => preset.Name;

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
        var hostname = update.Target.Hostname;
        var record = config.Find(hostname);
        if (record is null)
        {
            return Fail(update, $"host '{hostname}' is not configured");
        }

        if (config.Blocked is { } blocked)
        {
            return Fail(update,
                $"updates are blocked since {Format(blocked.Since)}: {blocked.Message}. " +
                $"Fix the problem, then run 'flaresync {Name} login' or 'flaresync {Name} unblock'");
        }

        if (config.RetryAfter is { } retryAfter && retryAfter > timeProvider.GetUtcNow())
        {
            return Fail(update, $"the service reported a server error; next attempt after {Format(retryAfter)}");
        }

        if (record.Blocked is { } hostBlocked)
        {
            return Fail(update,
                $"host blocked since {Format(hostBlocked.Since)}: {hostBlocked.Message}. " +
                $"Fix it, then run 'flaresync {Name} unblock {hostname}'");
        }

        DynDns2Credentials credentials;
        try
        {
            credentials = configStore.GetCredentials(config);
        }
        catch (FlareSyncException ex)
        {
            return Fail(update, ex.Message);
        }

        var response = await client.UpdateAsync(credentials, hostname, update.Addresses, cancellationToken);
        return await ApplyAsync(config, record, response, update, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<IpFamily, SyncResult>> ApplyAsync(
        DynDns2Config config, DynDns2Record record, DynDns2Response response, DnsUpdate update, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        switch (response.Action)
        {
            case DynDns2Action.None:
                if (config.RetryAfter is not null)
                {
                    config.RetryAfter = null;
                    await configStore.SaveAsync(config, cancellationToken);
                }

                return update.Changed.ToDictionary(
                    f => f,
                    _ => response.Status == DynDns2Status.Good ? SyncResult.Updated(response.Message) : SyncResult.Unchanged(response.Message));

            case DynDns2Action.BlockProvider:
                config.Blocked = new DynDns2Block { Reason = response.Code, Message = response.Message, Since = now };
                await configStore.SaveAsync(config, cancellationToken);
                return Fail(update,
                    $"{response.Message}; updates for every {preset.DisplayName} host are stopped until " +
                    $"'flaresync {Name} login' or 'flaresync {Name} unblock'");

            case DynDns2Action.BlockHost:
                record.Blocked = new DynDns2Block { Reason = response.Code, Message = response.Message, Since = now };
                await configStore.SaveAsync(config, cancellationToken);
                return Fail(update, $"{response.Message}; updates for this host are stopped until 'flaresync {Name} unblock {record.Hostname}'");

            case DynDns2Action.RetryLater:
                config.RetryAfter = now + DynDns2Response.RetryDelay;
                await configStore.SaveAsync(config, cancellationToken);
                return Fail(update, $"{response.Message}; next attempt after {Format(config.RetryAfter.Value)}");

            default:
                return Fail(update, response.Message);
        }
    }

    private static IReadOnlyDictionary<IpFamily, SyncResult> Fail(DnsUpdate update, string message)
        => update.Changed.ToDictionary(f => f, _ => SyncResult.Failed(message));

    private static string Format(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
