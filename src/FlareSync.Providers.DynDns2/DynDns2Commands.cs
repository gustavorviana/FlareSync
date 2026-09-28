using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Models;
using FlareSync.Core.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace FlareSync.Providers.DynDns2;

/// <summary>Command group of one DynDNS2 preset (<c>flaresync noip ...</c>), described with the neutral command model.</summary>
internal sealed class DynDns2Commands(DynDns2Preset preset)
{
    public const string DefaultUserAgentKeyword = "default";

    private readonly OptionDefinition<string?> _usernameOption = new() { Name = "--username", Description = "Account username (or DDNS key username)." };

    private readonly OptionDefinition<string?> _passwordOption = new()
    {
        Name = "--password",
        Description = "Account password. Prefer the interactive prompt to keep it out of shell history.",
    };

    private readonly OptionDefinition<string?> _contactOption = new()
    {
        Name = "--contact",
        Description = "Maintainer e-mail sent in the User-Agent, required by the service.",
        Validator = UserAgentBuilder.ValidateContact,
    };

    private readonly OptionDefinition<string?> _userAgentOption = new()
    {
        Name = "--user-agent",
        Description = $"'Company Program/Version' part of the User-Agent (default: '{UserAgentBuilder.DefaultProduct}'). " +
                      $"Use '{DefaultUserAgentKeyword}' to go back to the default.",
        Validator = v => v is null || v == DefaultUserAgentKeyword ? null : UserAgentBuilder.ValidateProduct(v),
    };

    private readonly OptionDefinition<string?> _serverOption = new()
    {
        Name = "--server",
        Description = "Update URL, e.g. https://example.com/nic/update.",
        Validator = ValidateServer,
    };

    private readonly ArgumentDefinition<string> _hostnameArgument = CommandHelpers.HostnameArgument("Host name, e.g. home.ddns.net.");

    private readonly ArgumentDefinition<string?> _optionalHostnameArgument = new()
    {
        Name = "hostname",
        Description = "Host to unblock. Omit to unblock the account (after badauth, abuse...) and pending retries.",
        Required = false,
    };

    private readonly OptionDefinition<IpFamilies> _familyOption = CommandHelpers.FamiliesOption("Addresses to send: ipv4, ipv6 or both");

    private readonly OptionDefinition<bool> _createOption = new() { Name = "--create", Description = "Send the update right away with the detected address." };

    private readonly OptionDefinition<IpFamilies?> _setFamilyOption = CommandHelpers.OptionalFamiliesOption("Addresses to send: ipv4, ipv6 or both");

    private string Name => preset.Name;

    public CommandDefinition Create() => new()
    {
        Name = Name,
        Description = $"Manage the {preset.DisplayName} provider (credential and hosts).",
        Subcommands = { Login(), Logout(), Add(), List(), Set(), Remove(), Unblock() },
    };

    private CommandDefinition Login() => new()
    {
        Name = "login",
        Description = $"Store the {preset.DisplayName} credentials and the User-Agent contact.",
        Options = { _usernameOption, _passwordOption, _contactOption, _userAgentOption, _serverOption },
        Handler = async ctx =>
        {
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            var console = ctx.Console;

            var missingInput = ctx.Get(_usernameOption) is null || ctx.Get(_passwordOption) is null;
            if (missingInput && console.IsInteractive)
            {
                console.WriteLine(preset.CredentialHint);
            }

            config.Server = ctx.Get(_serverOption) ?? config.Server;
            if (preset.DefaultServer is null && string.IsNullOrWhiteSpace(config.Server))
            {
                config.Server = Ask(ctx, "Update URL (e.g. https://example.com/nic/update): ", "--server", ValidateServer);
            }

            var username = ctx.Get(_usernameOption) ?? Ask(ctx, "Username: ", "--username");
            var password = ctx.Get(_passwordOption) ?? AskSecret(ctx, "Password: ", "--password");
            var contact = ctx.Get(_contactOption) ?? config.Contact
                ?? Ask(ctx, "Contact e-mail for the User-Agent (required by the service): ", "--contact", UserAgentBuilder.ValidateContact);

            config.UserAgentProduct = ctx.Get(_userAgentOption) switch
            {
                null => config.UserAgentProduct,
                DefaultUserAgentKeyword => null,
                var product => product.Trim(),
            };

            config.Username = username.Trim();
            store.SetPassword(config, password);
            config.Contact = contact.Trim();
            config.Blocked = null;
            config.RetryAfter = null;
            await store.SaveAsync(config, ctx.CancellationToken);

            console.WriteLine($"Credentials saved (password encrypted) to {store.FilePath}.");
            console.WriteLine($"Update URL: {store.Server(config)}");
            console.WriteLine($"User-Agent: {UserAgentBuilder.Build(config.UserAgentProduct, config.Contact)}");
            console.WriteLine("DynDNS2 services cannot check credentials without an update: they are checked on the next sync, " +
                              $"or right away with 'flaresync {Name} add <hostname> --create'.");
            return 0;
        },
    };

    private CommandDefinition Logout() => new()
    {
        Name = "logout",
        Description = "Remove the stored username and password (hosts are kept).",
        Handler = async ctx =>
        {
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            config.Username = null;
            config.Password = null;
            await store.SaveAsync(config, ctx.CancellationToken);
            ctx.Console.WriteLine($"{preset.DisplayName} credentials removed.");
            return 0;
        },
    };

    private CommandDefinition Add() => new()
    {
        Name = "add",
        Description = "Add a host name to keep updated.",
        Arguments = { _hostnameArgument },
        Options = { _familyOption, _createOption },
        Handler = async ctx =>
        {
            var hostname = CommandHelpers.NormalizeHostname(ctx.Get(_hostnameArgument)!);
            var families = ctx.Get(_familyOption);
            var record = new DynDns2Record { Hostname = hostname, IPv4 = families.UsesIPv4(), IPv6 = families.UsesIPv6() };

            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            if (config.Find(hostname) is not null)
            {
                throw new FlareSyncException($"'{hostname}' is already configured. Use 'flaresync {Name} set' to change it.");
            }

            config.Records.Add(record);
            await store.SaveAsync(config, ctx.CancellationToken);
            await ctx.Service<SyncStateStore>().RemoveAsync(Name, hostname, ctx.CancellationToken);
            ctx.Console.WriteLine($"Added {hostname} ({CommandHelpers.FormatFamilies(record.IPv4, record.IPv6)}).");

            return ctx.Get(_createOption) ? await SyncNowAsync(ctx, record) : 0;
        },
    };

    private CommandDefinition List() => new()
    {
        Name = "list",
        Description = "Show the account settings and the configured host names.",
        Handler = async ctx =>
        {
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            var console = ctx.Console;

            console.WriteLine($"Update URL: {store.Server(config) ?? "(not set)"}");
            console.WriteLine($"Username:   {config.Username ?? "(not logged in)"}");
            console.WriteLine($"User-Agent: {(config.Contact is null ? "(no contact set)" : UserAgentBuilder.Build(config.UserAgentProduct, config.Contact))}");
            if (config.Blocked is { } blocked)
            {
                console.WriteLine($"Status:     BLOCKED since {((DateTimeOffset?)blocked.Since).Format()}: {blocked.Message}");
            }
            else if (config.RetryAfter is { } retryAfter)
            {
                console.WriteLine($"Status:     waiting after a server error until {((DateTimeOffset?)retryAfter).Format()}");
            }

            console.WriteLine();
            if (config.Records.Count == 0)
            {
                console.WriteLine($"No hosts configured. Add one with 'flaresync {Name} add <hostname>'.");
                return 0;
            }

            console.WriteTable(
                ["Hostname", "IPv4", "IPv6", "Status"],
                config.Records.Select(r => (IReadOnlyList<string>)
                [
                    r.Hostname, CommandHelpers.YesNo(r.IPv4), CommandHelpers.YesNo(r.IPv6),
                    r.Blocked is null ? "ok" : $"blocked: {r.Blocked.Message}",
                ]).ToList());
            return 0;
        },
    };

    private CommandDefinition Set() => new()
    {
        Name = "set",
        Description = "Change which addresses are sent for a host (also clears a host block).",
        Arguments = { _hostnameArgument },
        Options = { _setFamilyOption },
        Handler = async ctx =>
        {
            var hostname = CommandHelpers.NormalizeHostname(ctx.Get(_hostnameArgument)!);
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            var record = config.Find(hostname) ?? throw new FlareSyncException($"'{hostname}' is not configured.");

            var families = ctx.Get(_setFamilyOption) ?? throw new FlareSyncException(
                $"Nothing to change. Use --family (or 'flaresync {Name} unblock {hostname}').");

            record.IPv4 = families.UsesIPv4();
            record.IPv6 = families.UsesIPv6();
            record.Blocked = null;
            await store.SaveAsync(config, ctx.CancellationToken);
            await ctx.Service<SyncStateStore>().RemoveAsync(Name, hostname, ctx.CancellationToken);
            ctx.Console.WriteLine($"Updated {hostname} ({CommandHelpers.FormatFamilies(record.IPv4, record.IPv6)}). Changes are sent on the next sync.");
            return 0;
        },
    };

    private CommandDefinition Remove() => new()
    {
        Name = "remove",
        Description = "Stop updating a host name.",
        Arguments = { _hostnameArgument },
        Handler = async ctx =>
        {
            var hostname = CommandHelpers.NormalizeHostname(ctx.Get(_hostnameArgument)!);
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);
            if (config.Records.RemoveAll(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                throw new FlareSyncException($"'{hostname}' is not configured.");
            }

            await store.SaveAsync(config, ctx.CancellationToken);
            await ctx.Service<SyncStateStore>().RemoveAsync(Name, hostname, ctx.CancellationToken);
            ctx.Console.WriteLine($"Removed {hostname}. The host itself was left untouched at {preset.DisplayName}.");
            return 0;
        },
    };

    private CommandDefinition Unblock() => new()
    {
        Name = "unblock",
        Description = "Allow updates again after a fatal answer from the service (fix the cause first).",
        Arguments = { _optionalHostnameArgument },
        Handler = async ctx =>
        {
            var store = Store(ctx);
            var config = await store.LoadAsync(ctx.CancellationToken);

            if (ctx.Get(_optionalHostnameArgument) is { } value)
            {
                var hostname = CommandHelpers.NormalizeHostname(value);
                var record = config.Find(hostname) ?? throw new FlareSyncException($"'{hostname}' is not configured.");
                record.Blocked = null;
                await store.SaveAsync(config, ctx.CancellationToken);
                ctx.Console.WriteLine($"{hostname} unblocked; it is updated on the next sync.");
                return 0;
            }

            config.Blocked = null;
            config.RetryAfter = null;
            await store.SaveAsync(config, ctx.CancellationToken);
            ctx.Console.WriteLine($"{preset.DisplayName} account unblocked; updates resume on the next sync.");
            return 0;
        },
    };

    private async Task<int> SyncNowAsync(CommandContext ctx, DynDns2Record record)
    {
        var resolver = ctx.Service<IIpResolver>();
        var addresses = new Dictionary<IpFamily, System.Net.IPAddress>();
        var exitCode = 0;

        foreach (var family in Enum.GetValues<IpFamily>().Where(f => f == IpFamily.IPv4 ? record.IPv4 : record.IPv6))
        {
            var detection = await resolver.DetectAsync(family, ctx.CancellationToken);
            if (detection.Address is null)
            {
                ctx.Console.WriteError($"{family.Label()}: address not detected ({string.Join("; ", detection.Errors)}).");
                exitCode = 1;
                continue;
            }

            addresses[family] = detection.Address;
        }

        if (addresses.Count == 0)
        {
            return 1;
        }

        var target = new DnsTarget(Name, record.Hostname, record.IPv4, record.IPv6) { ProviderData = record };
        var results = await ctx.Services.GetRequiredKeyedService<DynDns2Provider>(Name)
            .UpdateAsync(new DnsUpdate(target, addresses, addresses.Keys.ToHashSet()), ctx.CancellationToken);

        var state = ctx.Service<SyncStateStore>();
        foreach (var (family, result) in results)
        {
            if (result.Succeeded)
            {
                await state.SetAsync(Name, record.Hostname, family, addresses[family].ToString(), ctx.CancellationToken);
                ctx.Console.WriteLine($"{family.Label()}: {addresses[family]} - {result.Message}.");
            }
            else
            {
                ctx.Console.WriteError($"{family.Label()}: {addresses[family]} - {result.Message}.");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private DynDns2ConfigStore Store(CommandContext ctx) => ctx.Services.GetRequiredKeyedService<DynDns2ConfigStore>(Name);

    private static string Ask(CommandContext ctx, string prompt, string option, Func<string, string?>? validate = null)
    {
        if (!ctx.Console.IsInteractive)
        {
            throw new FlareSyncException($"No terminal available to ask for it. Pass {option}.");
        }

        var value = ctx.Console.ReadLine(prompt)?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new FlareSyncException($"No value entered for {option}.");
        }

        return validate?.Invoke(value) is { } error ? throw new FlareSyncException(error) : value;
    }

    private static string AskSecret(CommandContext ctx, string prompt, string option)
    {
        if (!ctx.Console.IsInteractive)
        {
            throw new FlareSyncException($"No terminal available to ask for it. Pass {option}.");
        }

        var value = ctx.Console.ReadSecret(prompt);
        return string.IsNullOrEmpty(value) ? throw new FlareSyncException($"No value entered for {option}.") : value;
    }

    private static string? ValidateServer(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? null
            : $"'{value}' is not a valid http(s) URL.";
}
