using System.Net;
using System.Net.Sockets;
using DnsClient;
using DnsClient.Protocol;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Config;
using FlareSync.Core.Models;

namespace FlareSync.Core.IpResolution;

/// <summary>
/// Resolves IP source host names with the system resolver, or with the custom DNS servers configured in
/// <c>flaresync.json</c> (<c>A</c> queries for IPv4, <c>AAAA</c> for IPv6). The configuration is read on every call.
/// </summary>
public sealed class HostResolver(GlobalConfigStore configStore) : IHostResolver
{
    private readonly Lock _lock = new();
    private string? _clientKey;
    private LookupClient? _client;

    public async Task<IPAddress[]> ResolveAsync(string host, IpFamily family, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        return await ResolveAsync(host, family, config.Dns.Servers, cancellationToken);
    }

    public async Task<IPAddress[]> ResolveAsync(
        string host, IpFamily family, IReadOnlyList<string> servers, CancellationToken cancellationToken)
    {
        var addressFamily = family == IpFamily.IPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;

        if (IPAddress.TryParse(host, out var literal))
        {
            return literal.AddressFamily == addressFamily ? [literal] : [];
        }

        if (servers.Count == 0)
        {
            try
            {
                return await Dns.GetHostAddressesAsync(host, addressFamily, cancellationToken);
            }
            catch (SocketException)
            {
                return [];
            }
        }

        var client = GetClient(servers);
        var response = await client.QueryAsync(host, family == IpFamily.IPv4 ? QueryType.A : QueryType.AAAA, QueryClass.IN, cancellationToken);
        if (response.HasError)
        {
            throw new FlareSyncException($"DNS query for '{host}' failed: {response.ErrorMessage}");
        }

        IEnumerable<IPAddress> addresses = family == IpFamily.IPv4
            ? response.Answers.OfType<ARecord>().Select(r => r.Address)
            : response.Answers.OfType<AaaaRecord>().Select(r => r.Address);
        return addresses.ToArray();
    }

    private LookupClient GetClient(IReadOnlyList<string> servers)
    {
        var key = string.Join(',', servers);
        lock (_lock)
        {
            if (_client is not null && _clientKey == key)
            {
                return _client;
            }

            var endpoints = servers
                .Select(s => DnsServerEndpoint.TryParse(s, out var endpoint)
                    ? endpoint
                    : throw new FlareSyncException($"Invalid DNS server '{s}' in configuration."))
                .ToArray();

            _client = new LookupClient(new LookupClientOptions(endpoints)
            {
                UseCache = true,
                Timeout = TimeSpan.FromSeconds(3),
                Retries = 1,
                UseRandomNameServer = false,
                ThrowDnsErrors = false,
            });
            _clientKey = key;
            return _client;
        }
    }
}
