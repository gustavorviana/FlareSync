using FlareSync.Core.Config;
using FlareSync.Core.IpResolution;
using FlareSync.Core.Models;

namespace FlareSync.Core.Commands.Builtin;

/// <summary><c>dns</c>: optional custom DNS servers used to resolve IP sources.</summary>
public static class DnsCommands
{
    private static readonly ArgumentDefinition<string> ServerArgument = new()
    {
        Name = "server",
        Description = "DNS server address with optional port: 1.1.1.1, 1.1.1.1:53, 2606:4700:4700::1111, [2606:4700:4700::1111]:53.",
        Validator = v => DnsServerEndpoint.TryParse(v, out _) ? null : $"'{v}' is not a valid DNS server address.",
    };

    private static readonly ArgumentDefinition<string?> HostArgument = new()
    {
        Name = "host",
        Description = "Host name to resolve (default: the hosts of every configured IP source).",
        Required = false,
    };

    public static CommandDefinition Create() => new()
    {
        Name = "dns",
        Description = "Manage the optional DNS servers used to resolve IP sources (default: system resolver).",
        Subcommands = { List(), Add(), Remove(), Clear(), Test() },
    };

    private static CommandDefinition List() => new()
    {
        Name = "list",
        Description = "List the configured DNS servers.",
        Handler = async ctx =>
        {
            var servers = (await ctx.Service<GlobalConfigStore>().LoadAsync(ctx.CancellationToken)).Dns.Servers;
            if (servers.Count == 0)
            {
                ctx.Console.WriteLine("No custom DNS servers configured; the system resolver is used.");
            }
            else
            {
                foreach (var server in servers)
                {
                    ctx.Console.WriteLine(server);
                }
            }

            return 0;
        },
    };

    private static CommandDefinition Add() => new()
    {
        Name = "add",
        Description = "Add a DNS server (servers are tried in order).",
        Arguments = { ServerArgument },
        Handler = async ctx =>
        {
            DnsServerEndpoint.TryParse(ctx.Get(ServerArgument), out var endpoint);
            var text = DnsServerEndpoint.Format(endpoint!);
            var added = false;
            await ctx.Service<GlobalConfigStore>().UpdateAsync(config =>
            {
                if (!config.Dns.Servers.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    config.Dns.Servers.Add(text);
                    added = true;
                }
            }, ctx.CancellationToken);

            ctx.Console.WriteLine(added ? $"Added DNS server {text}." : $"DNS server {text} already configured.");
            return 0;
        },
    };

    private static CommandDefinition Remove() => new()
    {
        Name = "remove",
        Description = "Remove a DNS server.",
        Arguments = { ServerArgument },
        Handler = async ctx =>
        {
            DnsServerEndpoint.TryParse(ctx.Get(ServerArgument), out var endpoint);
            var removed = 0;
            await ctx.Service<GlobalConfigStore>().UpdateAsync(config =>
                removed = config.Dns.Servers.RemoveAll(s => DnsServerEndpoint.TryParse(s, out var e) && e.Equals(endpoint)),
                ctx.CancellationToken);

            if (removed == 0)
            {
                ctx.Console.WriteError($"DNS server {endpoint} is not configured.");
                return 1;
            }

            ctx.Console.WriteLine($"Removed DNS server {endpoint}.");
            return 0;
        },
    };

    private static CommandDefinition Clear() => new()
    {
        Name = "clear",
        Description = "Remove every custom DNS server and use the system resolver.",
        Handler = async ctx =>
        {
            await ctx.Service<GlobalConfigStore>().UpdateAsync(config => config.Dns.Servers.Clear(), ctx.CancellationToken);
            ctx.Console.WriteLine("Custom DNS servers removed; the system resolver will be used.");
            return 0;
        },
    };

    private static CommandDefinition Test() => new()
    {
        Name = "test",
        Description = "Resolve host names with the configured DNS servers.",
        Arguments = { HostArgument },
        Handler = async ctx =>
        {
            var config = await ctx.Service<GlobalConfigStore>().LoadAsync(ctx.CancellationToken);
            var resolver = ctx.Service<HostResolver>();

            var hosts = ctx.Get(HostArgument) is { } host
                ? [(host, (IpFamily?)null)]
                : Enum.GetValues<IpFamily>()
                    .SelectMany(f => config.IpSources.For(f).Select(s => (Host: new Uri(s.Url).Host, Family: (IpFamily?)f)))
                    .Distinct()
                    .ToList();

            ctx.Console.WriteLine(config.Dns.Servers.Count == 0
                ? "Resolver: system"
                : $"Resolver: {string.Join(", ", config.Dns.Servers)}");

            var rows = new List<IReadOnlyList<string>>();
            var failures = 0;
            foreach (var (name, onlyFamily) in hosts)
            {
                foreach (var family in Enum.GetValues<IpFamily>().Where(f => onlyFamily is null || f == onlyFamily))
                {
                    string result;
                    try
                    {
                        var addresses = await resolver.ResolveAsync(name, family, config.Dns.Servers, ctx.CancellationToken);
                        result = addresses.Length == 0 ? "no records" : string.Join(", ", addresses.Select(a => a.ToString()));
                        failures += addresses.Length == 0 ? 1 : 0;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        result = "error: " + ex.Message;
                        failures++;
                    }

                    rows.Add([name, family == IpFamily.IPv4 ? "A" : "AAAA", result]);
                }
            }

            ctx.Console.WriteTable(["Host", "Type", "Result"], rows);
            return failures == 0 ? 0 : 1;
        },
    };
}
