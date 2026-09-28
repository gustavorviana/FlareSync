using FlareSync.Core.Config;
using FlareSync.Core.Models;

namespace FlareSync.Core.Commands.Builtin;

/// <summary><c>ip-source</c>: manage the URLs used to detect the public address.</summary>
public static class IpSourceCommands
{
    private static readonly ArgumentDefinition<string> UrlArgument = new()
    {
        Name = "url",
        Description = "HTTP(S) URL returning the public address.",
        Validator = v => Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? null
            : $"'{v}' is not a valid http(s) URL.",
    };

    private static readonly OptionDefinition<IpFamily> FamilyOption = new()
    {
        Name = "--family",
        Description = "Address family returned by the source.",
        Required = true,
    };

    private static readonly OptionDefinition<IpFamily?> OptionalFamilyOption = new()
    {
        Name = "--family",
        Description = "Only this address family (default: both).",
    };

    private static readonly OptionDefinition<int?> PositionOption = new()
    {
        Name = "--position",
        Description = "1-based position in the fallback order (default: last).",
        Validator = v => v is null or >= 1 ? null : "Position must be 1 or greater.",
    };

    private static readonly OptionDefinition<string?> JsonFieldOption = new()
    {
        Name = "--json-field",
        Description = "The response is JSON; read the address from this dot-separated field.",
    };

    public static CommandDefinition Create() => new()
    {
        Name = "ip-source",
        Description = "Manage the sources used to detect the public address.",
        Subcommands = { List(), Add(), Remove() },
    };

    private static CommandDefinition List() => new()
    {
        Name = "list",
        Description = "List the IP sources in fallback order.",
        Handler = async ctx =>
        {
            var config = await ctx.Service<GlobalConfigStore>().LoadAsync(ctx.CancellationToken);
            var rows = Enum.GetValues<IpFamily>()
                .SelectMany(f => config.IpSources.For(f).Select((s, i) => (IReadOnlyList<string>)
                    [f.Label(), (i + 1).ToString(), s.Url, s.JsonField ?? "(plain text)"]));
            ctx.Console.WriteTable(["Family", "Order", "URL", "Format"], rows.ToList());
            return 0;
        },
    };

    private static CommandDefinition Add() => new()
    {
        Name = "add",
        Description = "Add an IP source.",
        Arguments = { UrlArgument },
        Options = { FamilyOption, PositionOption, JsonFieldOption },
        Handler = async ctx =>
        {
            var url = ctx.Get(UrlArgument)!;
            var family = ctx.Get(FamilyOption);
            var added = false;

            await ctx.Service<GlobalConfigStore>().UpdateAsync(config =>
            {
                var sources = config.IpSources.For(family);
                if (sources.Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                var source = new IpSourceSettings { Url = url, JsonField = ctx.Get(JsonFieldOption) };
                var index = Math.Min((ctx.Get(PositionOption) ?? int.MaxValue) - 1, sources.Count);
                sources.Insert(index, source);
                added = true;
            }, ctx.CancellationToken);

            ctx.Console.WriteLine(added ? $"Added {family.Label()} source {url}." : $"{family.Label()} source {url} already exists.");
            return 0;
        },
    };

    private static CommandDefinition Remove() => new()
    {
        Name = "remove",
        Description = "Remove an IP source.",
        Arguments = { UrlArgument },
        Options = { OptionalFamilyOption },
        Handler = async ctx =>
        {
            var url = ctx.Get(UrlArgument)!;
            var family = ctx.Get(OptionalFamilyOption);
            var removed = 0;

            await ctx.Service<GlobalConfigStore>().UpdateAsync(config =>
            {
                foreach (var f in Enum.GetValues<IpFamily>().Where(f => family is null || f == family))
                {
                    removed += config.IpSources.For(f).RemoveAll(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase));
                }
            }, ctx.CancellationToken);

            if (removed == 0)
            {
                ctx.Console.WriteError($"Source {url} not found.");
                return 1;
            }

            ctx.Console.WriteLine($"Removed source {url}.");
            return 0;
        },
    };
}
