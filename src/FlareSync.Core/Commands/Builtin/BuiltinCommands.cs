using FlareSync.Core.Abstractions;
using FlareSync.Core.Config;
using FlareSync.Core.Models;
using FlareSync.Core.Secrets;
using FlareSync.Core.Sync;

namespace FlareSync.Core.Commands.Builtin;

public static class BuiltinCommands
{
    /// <summary>Registers the global options and every built-in command.</summary>
    public static CommandCatalog AddBuiltinCommands(this CommandCatalog catalog)
    {
        catalog.AddGlobalOption(CoreOptions.ConfigDir);
        catalog.AddGlobalOption(CoreOptions.Verbose);

        catalog.Add(Init());
        catalog.Add(Sync());
        catalog.Add(Ip());
        catalog.Add(Status());
        catalog.Add(Interval());
        catalog.Add(IpSourceCommands.Create());
        catalog.Add(DnsCommands.Create());
        return catalog;
    }

    private static CommandDefinition Init() => new()
    {
        Name = "init",
        Description = "Create the configuration directory, the default settings and the master key.",
        Handler = async ctx =>
        {
            var paths = ctx.Service<ConfigPaths>();
            var globals = ctx.Service<GlobalConfigStore>();
            var keys = ctx.Service<MasterKeyProvider>();

            JsonConfigStore.EnsureDirectory(paths.Root);
            JsonConfigStore.EnsureDirectory(paths.ProvidersDirectory);

            if (globals.Exists)
            {
                ctx.Console.WriteLine($"Settings already exist: {paths.GlobalFile}");
            }
            else
            {
                await globals.SaveAsync(GlobalConfig.CreateDefault(), ctx.CancellationToken);
                ctx.Console.WriteLine($"Created settings: {paths.GlobalFile}");
            }

            if (keys.EnsureKeyFile())
            {
                ctx.Console.WriteLine($"Created master key: {paths.SecretKeyFile}");
                ctx.Console.WriteLine("Keep this file private. For stronger protection use a systemd encrypted credential (see README).");
            }
            else
            {
                ctx.Console.WriteLine($"Master key source: {keys.DescribeSource()}");
            }

            return 0;
        },
    };

    private static readonly OptionDefinition<bool> ForceOption = new()
    {
        Name = "--force",
        Aliases = { "-f" },
        Description = "Update every record even if the address did not change since the last sync.",
    };

    private static CommandDefinition Sync() => new()
    {
        Name = "sync",
        Description = "Run one sync cycle and exit.",
        Options = { ForceOption },
        Handler = async ctx =>
        {
            var report = await ctx.Service<SyncOrchestrator>().RunOnceAsync(ctx.Get(ForceOption), ctx.CancellationToken);

            foreach (var detection in report.Detections.Where(d => !d.Succeeded))
            {
                ctx.Console.WriteError($"Could not detect the public {detection.Family.Label()} address:");
                foreach (var error in detection.Errors)
                {
                    ctx.Console.WriteError($"  {error}");
                }
            }

            if (report.Entries.Count == 0)
            {
                ctx.Console.WriteLine("No records configured. Add one with a provider command, e.g. 'flaresync cloudflare add <hostname>'.");
                return 0;
            }

            ctx.Console.WriteTable(
                ["Provider", "Hostname", "Family", "Address", "Result", "Details"],
                report.Entries.Select(e => (IReadOnlyList<string>)
                [
                    e.Provider, e.Hostname, e.Family.Label(), e.Address?.ToString() ?? "-", e.Result.Outcome.ToString(), e.Result.Message ?? "",
                ]));

            return report.HasFailures ? 1 : 0;
        },
    };

    private static CommandDefinition Ip() => new()
    {
        Name = "ip",
        Description = "Show the detected public addresses.",
        Handler = async ctx =>
        {
            var resolver = ctx.Service<IIpResolver>();
            var rows = new List<IReadOnlyList<string>>();
            var anySucceeded = false;

            foreach (var family in Enum.GetValues<IpFamily>())
            {
                var detection = await resolver.DetectAsync(family, ctx.CancellationToken);
                anySucceeded |= detection.Succeeded;
                rows.Add(detection.Succeeded
                    ? [family.Label(), detection.Address!.ToString(), detection.Source!]
                    : [family.Label(), "not detected", string.Join("; ", detection.Errors)]);
            }

            ctx.Console.WriteTable(["Family", "Address", "Source / errors"], rows);
            return anySucceeded ? 0 : 1;
        },
    };

    private static CommandDefinition Status() => new()
    {
        Name = "status",
        Description = "Show every configured record with the last applied address.",
        Handler = async ctx =>
        {
            var state = await ctx.Service<SyncStateStore>().GetAllAsync(ctx.CancellationToken);
            var globals = await ctx.Service<GlobalConfigStore>().LoadAsync(ctx.CancellationToken);
            var rows = new List<IReadOnlyList<string>>();

            foreach (var provider in ctx.Services.GetServicesOf<IDnsProvider>())
            {
                IReadOnlyList<DnsTarget> targets;
                try
                {
                    targets = await provider.GetTargetsAsync(ctx.CancellationToken);
                }
                catch (FlareSyncException ex)
                {
                    ctx.Console.WriteError($"{provider.Name}: {ex.Message}");
                    continue;
                }

                foreach (var target in targets)
                {
                    foreach (var family in Enum.GetValues<IpFamily>().Where(target.Uses))
                    {
                        var entry = state.GetValueOrDefault(SyncStateStore.Key(provider.Name, target.Hostname, family));
                        rows.Add([provider.Name, target.Hostname, family.Label(), entry?.Address ?? "-", ((DateTimeOffset?)entry?.UpdatedAt).Format()]);
                    }
                }
            }

            ctx.Console.WriteLine($"Configuration: {ctx.Service<ConfigPaths>().Root}");
            ctx.Console.WriteLine($"Interval: {globals.Interval}");
            ctx.Console.WriteLine();
            if (rows.Count == 0)
            {
                ctx.Console.WriteLine("No records configured.");
            }
            else
            {
                ctx.Console.WriteTable(["Provider", "Hostname", "Family", "Last address", "Updated"], rows);
            }

            return 0;
        },
    };

    private static readonly ArgumentDefinition<string?> IntervalArgument = new()
    {
        Name = "value",
        Description = "New interval, e.g. 30s, 5m, 1h or 00:05:00. Omit to show the current value.",
        Required = false,
        Validator = v => v is null || (CommandHelpers.TryParseDuration(v, out var d) && d >= GlobalConfig.MinimumInterval)
            ? null
            : $"Invalid interval '{v}'. Use e.g. 30s, 5m, 1h (minimum {GlobalConfig.MinimumInterval.TotalSeconds}s).",
    };

    private static CommandDefinition Interval() => new()
    {
        Name = "interval",
        Description = "Show or set the interval between sync cycles in service mode.",
        Arguments = { IntervalArgument },
        Handler = async ctx =>
        {
            var store = ctx.Service<GlobalConfigStore>();
            var value = ctx.Get(IntervalArgument);
            if (value is null)
            {
                ctx.Console.WriteLine((await store.LoadAsync(ctx.CancellationToken)).Interval.ToString());
                return 0;
            }

            CommandHelpers.TryParseDuration(value, out var interval);
            await store.UpdateAsync(c => c.Interval = interval, ctx.CancellationToken);
            ctx.Console.WriteLine($"Interval set to {interval}.");
            return 0;
        },
    };

    private static IEnumerable<T> GetServicesOf<T>(this IServiceProvider services)
        => (IEnumerable<T>?)services.GetService(typeof(IEnumerable<T>)) ?? [];
}
