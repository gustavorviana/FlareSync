using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Models;
using FlareSync.Core.Sync;

namespace FlareSync.Providers.Cloudflare;

/// <summary><c>cloudflare</c> command group, described with the neutral command model.</summary>
internal static class CloudflareCommands
{
    private static readonly OptionDefinition<string?> TokenOption = new()
    {
        Name = "--token",
        Description = "API token to store (skips the interactive link flow). Prefer the interactive flow to keep the token out of shell history.",
    };

    private static readonly OptionDefinition<bool> NoBrowserOption = new()
    {
        Name = "--no-browser",
        Description = "Only print the link; do not try to open a browser.",
    };

    private static readonly ArgumentDefinition<string> HostnameArgument = new()
    {
        Name = "hostname",
        Description = "Fully qualified host name, e.g. home.example.com.",
        Validator = v => Uri.CheckHostName(ZoneMatcher.Normalize(v)) == UriHostNameType.Dns && v.Contains('.')
            ? null
            : $"'{v}' is not a valid host name.",
    };

    private static readonly OptionDefinition<bool> NoIPv4Option = new() { Name = "--no-ipv4", Description = "Do not manage the A (IPv4) record." };

    private static readonly OptionDefinition<bool> NoIPv6Option = new() { Name = "--no-ipv6", Description = "Do not manage the AAAA (IPv6) record." };

    private static readonly OptionDefinition<bool> ProxiedFlag = new() { Name = "--proxied", Description = "Proxy traffic through Cloudflare (orange cloud)." };

    private static readonly OptionDefinition<int> TtlOption = new()
    {
        Name = "--ttl",
        Description = "Record TTL in seconds; 1 = automatic.",
        DefaultValue = CloudflareRecord.AutomaticTtl,
        Validator = CloudflareRecord.ValidateTtl,
    };

    private static readonly OptionDefinition<bool> CreateOption = new()
    {
        Name = "--create",
        Description = "Create or update the DNS record right away with the detected address.",
    };

    private static readonly OptionDefinition<bool?> SetIPv4Option = new() { Name = "--ipv4", Description = "Manage the A (IPv4) record (true/false)." };

    private static readonly OptionDefinition<bool?> SetIPv6Option = new() { Name = "--ipv6", Description = "Manage the AAAA (IPv6) record (true/false)." };

    private static readonly OptionDefinition<bool?> SetProxiedOption = new() { Name = "--proxied", Description = "Proxy traffic through Cloudflare (true/false)." };

    private static readonly OptionDefinition<int?> SetTtlOption = new()
    {
        Name = "--ttl",
        Description = "Record TTL in seconds; 1 = automatic.",
        Validator = v => v is { } ttl ? CloudflareRecord.ValidateTtl(ttl) : null,
    };

    public static CommandDefinition Create() => new()
    {
        Name = CloudflareModule.ProviderName,
        Description = "Manage the Cloudflare provider (credential and records).",
        Subcommands = { Login(), Logout(), Zones(), Add(), List(), Set(), Remove() },
    };

    private static CommandDefinition Login() => new()
    {
        Name = "login",
        Description = "Store a Cloudflare API token. Prints a link that pre-fills the token form in the dashboard.",
        Options = { TokenOption, NoBrowserOption },
        Handler = async ctx =>
        {
            var token = ctx.Get(TokenOption);
            if (string.IsNullOrWhiteSpace(token))
            {
                if (!ctx.Console.IsInteractive)
                {
                    throw new FlareSyncException("No terminal available to paste the token. Pass it with --token.");
                }

                var url = CloudflareTokenTemplate.BuildUrl();
                ctx.Console.WriteLine("Create an API token for FlareSync in the Cloudflare dashboard:");
                ctx.Console.WriteLine();
                ctx.Console.WriteLine("  " + url);
                ctx.Console.WriteLine();
                if (!ctx.Get(NoBrowserOption) && ctx.Service<IBrowserLauncher>().TryOpen(url))
                {
                    ctx.Console.WriteLine("The link was opened in your browser.");
                }

                ctx.Console.WriteLine("The form is pre-filled with 'Zone:Read' and 'DNS:Edit' for all zones.");
                ctx.Console.WriteLine("Optionally restrict it to specific zones, then click 'Continue to summary' and 'Create Token'.");
                ctx.Console.WriteLine();
                token = ctx.Console.ReadSecret("Paste the API token: ");
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new FlareSyncException("No token entered.");
                }
            }

            token = token.Trim();
            var api = ctx.Service<CloudflareApiClient>();
            string status;
            try
            {
                status = await api.VerifyTokenAsync(token, ctx.CancellationToken);
            }
            catch (CloudflareApiException ex)
            {
                throw new FlareSyncException($"Cloudflare rejected the token. {ex.Message}");
            }

            if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            {
                throw new FlareSyncException($"The token is not active (status: {status}).");
            }

            var zones = await api.ListZonesAsync(token, ctx.CancellationToken);
            if (zones.Count == 0)
            {
                throw new FlareSyncException("The token cannot see any zone. Make sure it has the 'Zone:Read' permission.");
            }

            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            store.SetToken(config, token);
            await store.SaveAsync(config, ctx.CancellationToken);

            ctx.Console.WriteLine($"Logged in. The token can access {zones.Count} zone(s): {string.Join(", ", zones.Select(z => z.Name))}.");
            ctx.Console.WriteLine($"Saved (encrypted) to {store.FilePath}.");
            return 0;
        },
    };

    private static CommandDefinition Logout() => new()
    {
        Name = "logout",
        Description = "Remove the stored API token (records are kept).",
        Handler = async ctx =>
        {
            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            config.ApiToken = null;
            await store.SaveAsync(config, ctx.CancellationToken);
            ctx.Console.WriteLine("Cloudflare API token removed.");
            return 0;
        },
    };

    private static CommandDefinition Zones() => new()
    {
        Name = "zones",
        Description = "List the zones accessible with the stored token.",
        Handler = async ctx =>
        {
            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            var zones = await ctx.Service<CloudflareApiClient>().ListZonesAsync(store.GetToken(config), ctx.CancellationToken);

            ctx.Console.WriteTable(
                ["Zone", "Id", "Status", "Records"],
                zones.OrderBy(z => z.Name).Select(z => (IReadOnlyList<string>)
                    [z.Name, z.Id, z.Status, config.Records.Count(r => r.ZoneId == z.Id).ToString()]).ToList());
            return 0;
        },
    };

    private static CommandDefinition Add() => new()
    {
        Name = "add",
        Description = "Add a host name to keep updated. The zone is detected from the host name.",
        Arguments = { HostnameArgument },
        Options = { NoIPv4Option, NoIPv6Option, ProxiedFlag, TtlOption, CreateOption },
        Handler = async ctx =>
        {
            var hostname = ZoneMatcher.Normalize(ctx.Get(HostnameArgument)!);
            var record = new CloudflareRecord
            {
                Hostname = hostname,
                IPv4 = !ctx.Get(NoIPv4Option),
                IPv6 = !ctx.Get(NoIPv6Option),
                Proxied = ctx.Get(ProxiedFlag),
                Ttl = ctx.Get(TtlOption),
            };

            if (!record.IPv4 && !record.IPv6)
            {
                throw new FlareSyncException("At least one of IPv4 or IPv6 must be enabled.");
            }

            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            if (config.Find(hostname) is not null)
            {
                throw new FlareSyncException($"'{hostname}' is already configured. Use 'flaresync cloudflare set' to change it.");
            }

            var token = store.GetToken(config);
            var zones = await ctx.Service<CloudflareApiClient>().ListZonesAsync(token, ctx.CancellationToken);
            var zone = ZoneMatcher.FindZone(hostname, zones) ?? throw new FlareSyncException(
                $"No zone accessible with the token matches '{hostname}'. Available zones: {string.Join(", ", zones.Select(z => z.Name))}.");

            record.ZoneId = zone.Id;
            record.ZoneName = zone.Name;
            config.Records.Add(record);
            await store.SaveAsync(config, ctx.CancellationToken);
            await ctx.Service<SyncStateStore>().RemoveAsync(CloudflareModule.ProviderName, hostname, ctx.CancellationToken);

            ctx.Console.WriteLine($"Added {hostname} (zone {zone.Name}, {Families(record)}, proxied: {record.Proxied}, TTL: {Ttl(record)}).");

            return ctx.Get(CreateOption) ? await SyncNowAsync(ctx, token, record) : 0;
        },
    };

    private static CommandDefinition List() => new()
    {
        Name = "list",
        Description = "List the configured host names.",
        Handler = async ctx =>
        {
            var config = await ctx.Service<CloudflareConfigStore>().LoadAsync(ctx.CancellationToken);
            ctx.Console.WriteLine($"Logged in: {(string.IsNullOrWhiteSpace(config.ApiToken) ? "no" : "yes")}");
            if (config.Records.Count == 0)
            {
                ctx.Console.WriteLine("No records configured. Add one with 'flaresync cloudflare add <hostname>'.");
                return 0;
            }

            ctx.Console.WriteTable(
                ["Hostname", "Zone", "IPv4", "IPv6", "Proxied", "TTL"],
                config.Records.Select(r => (IReadOnlyList<string>)
                    [r.Hostname, r.ZoneName ?? r.ZoneId, YesNo(r.IPv4), YesNo(r.IPv6), YesNo(r.Proxied), Ttl(r)]).ToList());
            return 0;
        },
    };

    private static CommandDefinition Set() => new()
    {
        Name = "set",
        Description = "Change the settings of a configured host name.",
        Arguments = { HostnameArgument },
        Options = { SetIPv4Option, SetIPv6Option, SetProxiedOption, SetTtlOption },
        Handler = async ctx =>
        {
            var hostname = ZoneMatcher.Normalize(ctx.Get(HostnameArgument)!);
            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            var record = config.Find(hostname) ?? throw new FlareSyncException($"'{hostname}' is not configured.");

            var ipv4 = ctx.Get(SetIPv4Option);
            var ipv6 = ctx.Get(SetIPv6Option);
            var proxied = ctx.Get(SetProxiedOption);
            var ttl = ctx.Get(SetTtlOption);
            if (ipv4 is null && ipv6 is null && proxied is null && ttl is null)
            {
                throw new FlareSyncException("Nothing to change. Use --ipv4, --ipv6, --proxied or --ttl.");
            }

            record.IPv4 = ipv4 ?? record.IPv4;
            record.IPv6 = ipv6 ?? record.IPv6;
            record.Proxied = proxied ?? record.Proxied;
            record.Ttl = ttl ?? record.Ttl;
            if (!record.IPv4 && !record.IPv6)
            {
                throw new FlareSyncException("At least one of IPv4 or IPv6 must be enabled.");
            }

            await store.SaveAsync(config, ctx.CancellationToken);
            // Forget the last applied address so the next sync pushes the new settings.
            await ctx.Service<SyncStateStore>().RemoveAsync(CloudflareModule.ProviderName, hostname, ctx.CancellationToken);

            ctx.Console.WriteLine($"Updated {hostname} ({Families(record)}, proxied: {record.Proxied}, TTL: {Ttl(record)}). Changes are applied on the next sync.");
            return 0;
        },
    };

    private static CommandDefinition Remove() => new()
    {
        Name = "remove",
        Description = "Stop managing a host name (the DNS record itself is not deleted).",
        Arguments = { HostnameArgument },
        Handler = async ctx =>
        {
            var hostname = ZoneMatcher.Normalize(ctx.Get(HostnameArgument)!);
            var store = ctx.Service<CloudflareConfigStore>();
            var config = await store.LoadAsync(ctx.CancellationToken);
            if (config.Records.RemoveAll(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                throw new FlareSyncException($"'{hostname}' is not configured.");
            }

            await store.SaveAsync(config, ctx.CancellationToken);
            await ctx.Service<SyncStateStore>().RemoveAsync(CloudflareModule.ProviderName, hostname, ctx.CancellationToken);
            ctx.Console.WriteLine($"Removed {hostname}. The DNS record at Cloudflare was left untouched.");
            return 0;
        },
    };

    private static async Task<int> SyncNowAsync(CommandContext ctx, string token, CloudflareRecord record)
    {
        var resolver = ctx.Service<IIpResolver>();
        var provider = ctx.Service<CloudflareDnsProvider>();
        var state = ctx.Service<SyncStateStore>();
        var exitCode = 0;

        foreach (var family in Enum.GetValues<IpFamily>())
        {
            if (family == IpFamily.IPv4 ? !record.IPv4 : !record.IPv6)
            {
                continue;
            }

            var detection = await resolver.DetectAsync(family, ctx.CancellationToken);
            if (detection.Address is null)
            {
                ctx.Console.WriteError($"{family.Label()}: address not detected ({string.Join("; ", detection.Errors)}).");
                exitCode = 1;
                continue;
            }

            var result = await provider.UpsertAsync(token, record, family, detection.Address, ctx.CancellationToken);
            await state.SetAsync(CloudflareModule.ProviderName, record.Hostname, family, detection.Address.ToString(), ctx.CancellationToken);
            ctx.Console.WriteLine($"{family.Label()}: {detection.Address} - {result.Message}.");
        }

        return exitCode;
    }

    private static string Families(CloudflareRecord r) => (r.IPv4, r.IPv6) switch
    {
        (true, true) => "IPv4 + IPv6",
        (true, false) => "IPv4 only",
        _ => "IPv6 only",
    };

    private static string Ttl(CloudflareRecord r) => r.Proxied || r.Ttl == CloudflareRecord.AutomaticTtl ? "auto" : r.Ttl + "s";

    private static string YesNo(bool value) => value ? "yes" : "no";
}
