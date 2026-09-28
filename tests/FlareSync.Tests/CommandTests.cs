using FlareSync.Cli.CommandLine;
using FlareSync.Core.Commands;
using FlareSync.Core.Commands.Builtin;
using FlareSync.Core.Models;
using FlareSync.Providers.Cloudflare;
using Microsoft.Extensions.DependencyInjection;

namespace FlareSync.Tests;

public class CommandCatalogTests
{
    [Fact]
    [Trait("Req", "FR-303")]
    public void Validate_rejects_duplicate_root_commands_from_modules()
    {
        var catalog = new CommandCatalog()
            .Add(new CommandDefinition { Name = "cloudflare", Handler = _ => Task.FromResult(0) })
            .Add(new CommandDefinition { Name = "Cloudflare", Handler = _ => Task.FromResult(0) });

        var ex = Assert.Throws<InvalidOperationException>(catalog.Validate);
        Assert.Contains("duplicate command name", ex.Message);
    }

    [Fact]
    [Trait("Req", "FR-303")]
    public void Validate_rejects_leaf_without_handler()
    {
        var catalog = new CommandCatalog().Add(new CommandDefinition { Name = "empty" });

        var ex = Assert.Throws<InvalidOperationException>(catalog.Validate);
        Assert.Contains("must have a handler", ex.Message);
    }

    [Fact]
    [Trait("Req", "FR-303")]
    public void Validate_rejects_option_clashing_with_global_option()
    {
        var catalog = new CommandCatalog().AddBuiltinCommands();
        catalog.Add(new CommandDefinition
        {
            Name = "x",
            Options = { new OptionDefinition<bool> { Name = "--verbose" } },
            Handler = _ => Task.FromResult(0),
        });

        Assert.Throws<InvalidOperationException>(catalog.Validate);
    }

    [Fact]
    [Trait("Req", "FR-304")]
    public void Builtin_and_cloudflare_commands_form_a_valid_catalog()
    {
        var catalog = new CommandCatalog().AddBuiltinCommands();
        new CloudflareModule().RegisterCommands(catalog);

        catalog.Validate();
        Assert.Contains(catalog.Commands, c => c.Name == "cloudflare");
    }
}

public class SystemCommandLineAdapterTests
{
    private static readonly ArgumentDefinition<string> Host = new() { Name = "hostname" };
    private static readonly OptionDefinition<int> Ttl = new()
    {
        Name = "--ttl",
        DefaultValue = 1,
        Validator = v => v is 1 or >= 60 ? null : "bad ttl",
    };
    private static readonly OptionDefinition<bool> NoIPv6 = new() { Name = "--no-ipv6" };
    private static readonly OptionDefinition<IpFamily?> Family = new() { Name = "--family" };
    private static readonly OptionDefinition<string?> Mode = new() { Name = "--mode", AllowedValues = ["a", "b"] };

    private static (SystemCommandLineAdapter Adapter, List<IParsedValues> Seen, FakeConsole Console) Create(Func<CommandContext, Task<int>>? handler = null)
    {
        var seen = new List<IParsedValues>();
        var catalog = new CommandCatalog().AddBuiltinCommands();
        catalog.Add(new CommandDefinition
        {
            Name = "provider",
            Subcommands =
            {
                new CommandDefinition
                {
                    Name = "add",
                    Arguments = { Host },
                    Options = { Ttl, NoIPv6, Family, Mode },
                    Handler = ctx =>
                    {
                        seen.Add(ctx.Values);
                        return handler?.Invoke(ctx) ?? Task.FromResult(0);
                    },
                },
            },
        });
        catalog.Validate();
        var console = new FakeConsole();
        var adapter = new SystemCommandLineAdapter(catalog, _ => new ServiceCollection().BuildServiceProvider(), console);
        return (adapter, seen, console);
    }

    [Fact]
    [Trait("Req", "FR-302")]
    public async Task Typed_values_reach_the_handler()
    {
        var (adapter, seen, _) = Create();

        var exit = await adapter.InvokeAsync(["provider", "add", "x.example.com", "--ttl", "300", "--no-ipv6", "--family", "ipv6", "--config-dir", "/tmp/x"]);

        Assert.Equal(0, exit);
        var values = Assert.Single(seen);
        Assert.Equal("x.example.com", values.Get(Host));
        Assert.Equal(300, values.Get(Ttl));
        Assert.True(values.Get(NoIPv6));
        Assert.Equal(IpFamily.IPv6, values.Get(Family));
        Assert.Equal("/tmp/x", values.Get(CoreOptions.ConfigDir));
        Assert.True(values.IsSpecified(Ttl));
    }

    [Fact]
    [Trait("Req", "FR-302")]
    public async Task Defaults_apply_when_options_are_omitted()
    {
        var (adapter, seen, _) = Create();

        await adapter.InvokeAsync(["provider", "add", "x.example.com"]);

        var values = Assert.Single(seen);
        Assert.Equal(1, values.Get(Ttl));
        Assert.False(values.Get(NoIPv6));
        Assert.Null(values.Get(Family));
        Assert.False(values.IsSpecified(Ttl));
    }

    [Theory]
    [Trait("Req", "FR-302")]
    [InlineData("--ttl", "5")]
    [InlineData("--mode", "c")]
    public async Task Validators_and_allowed_values_reject_input(string option, string value)
    {
        var (adapter, seen, _) = Create();

        var exit = await adapter.InvokeAsync(["provider", "add", "x.example.com", option, value]);

        Assert.NotEqual(0, exit);
        Assert.Empty(seen);
    }

    [Fact]
    [Trait("Req", "FR-203")]
    public async Task User_errors_are_printed_and_return_non_zero()
    {
        var (adapter, _, console) = Create(_ => throw new FlareSync.Core.FlareSyncException("boom"));

        var exit = await adapter.InvokeAsync(["provider", "add", "x.example.com"]);

        Assert.Equal(1, exit);
        Assert.Contains("Error: boom", console.Errors.ToString());
    }

    [Fact]
    [Trait("Req", "FR-304")]
    public void Module_commands_appear_in_the_command_tree()
    {
        var catalog = new CommandCatalog().AddBuiltinCommands();
        new CloudflareModule().RegisterCommands(catalog);
        var root = new SystemCommandLineAdapter(catalog, _ => null!, new FakeConsole()).Build();

        var cloudflare = Assert.Single(root.Subcommands, c => c.Name == "cloudflare");
        Assert.Contains(cloudflare.Subcommands, c => c.Name == "login");
        Assert.Contains(cloudflare.Subcommands, c => c.Name == "add");
    }
}

public class CommandHelpersTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("00:05:00", 300)]
    public void Parses_durations(string text, int seconds)
    {
        Assert.True(CommandHelpers.TryParseDuration(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData("5x")]
    [InlineData("-5m")]
    [InlineData("")]
    public void Rejects_invalid_durations(string text) => Assert.False(CommandHelpers.TryParseDuration(text, out _));
}
