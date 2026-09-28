using System.Net;
using System.Security.Cryptography;
using System.Text;
using FlareSync.Core;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Commands.Builtin;
using FlareSync.Core.Models;
using FlareSync.Core.Secrets;
using FlareSync.Providers.DynDns2;
using Microsoft.Extensions.DependencyInjection;

namespace FlareSync.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public class DynDns2ClientTests
{
    private static readonly IPAddress V4 = IPAddress.Parse("203.0.114.1");
    private static readonly IPAddress V6 = IPAddress.Parse("2a03:1b20:bef1::1");

    [Fact]
    [Trait("Req", "FR-410")]
    public void Url_carries_host_and_both_addresses_ipv4_first()
    {
        var url = DynDns2Client.BuildUrl("https://dyn.test/nic/update", "home.ddns.net",
            new Dictionary<IpFamily, IPAddress> { [IpFamily.IPv6] = V6, [IpFamily.IPv4] = V4 });

        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("home.ddns.net", query["hostname"]);
        Assert.Equal("203.0.114.1,2a03:1b20:bef1::1", query["myip"]);
    }

    [Fact]
    public void Url_appends_to_existing_query()
    {
        var url = DynDns2Client.BuildUrl("https://dyn.test/update?system=dyndns", "h.example.com",
            new Dictionary<IpFamily, IPAddress> { [IpFamily.IPv4] = V4 });

        Assert.Equal("?system=dyndns&hostname=h.example.com&myip=203.0.114.1", url.Query);
    }

    [Theory]
    [Trait("Req", "FR-420")]
    [InlineData("good 203.0.114.1", DynDns2Status.Good, DynDns2Action.None)]
    [InlineData("nochg 203.0.114.1\n", DynDns2Status.NoChange, DynDns2Action.None)]
    [InlineData("badauth", DynDns2Status.BadAuth, DynDns2Action.BlockProvider)]
    [InlineData("badagent", DynDns2Status.BadAgent, DynDns2Action.BlockProvider)]
    [InlineData("!donator", DynDns2Status.NotDonator, DynDns2Action.BlockProvider)]
    [InlineData("abuse", DynDns2Status.Abuse, DynDns2Action.BlockProvider)]
    [InlineData("nohost", DynDns2Status.NoHost, DynDns2Action.BlockHost)]
    [InlineData("notfqdn", DynDns2Status.NotFqdn, DynDns2Action.BlockHost)]
    [InlineData("numhost", DynDns2Status.NumHost, DynDns2Action.BlockHost)]
    [InlineData("911", DynDns2Status.ServerError, DynDns2Action.RetryLater)]
    [InlineData("dnserr", DynDns2Status.DnsError, DynDns2Action.RetryLater)]
    [InlineData("<html>oops</html>", DynDns2Status.Unknown, DynDns2Action.Fail)]
    [InlineData("", DynDns2Status.Unknown, DynDns2Action.Fail)]
    public void Parses_response_codes(string body, DynDns2Status status, DynDns2Action action)
    {
        var response = DynDns2Client.Parse(body, HttpStatusCode.OK);

        Assert.Equal(status, response.Status);
        Assert.Equal(action, response.Action);
    }

    [Fact]
    [Trait("Req", "FR-420")]
    public void Parses_badauth_sent_with_http_401()
        => Assert.Equal(DynDns2Status.BadAuth, DynDns2Client.Parse("badauth", HttpStatusCode.Unauthorized).Status);

    [Fact]
    [Trait("Req", "FR-411")]
    public async Task Sends_basic_auth_and_user_agent()
    {
        var handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Text("good 203.0.114.1")));
        var client = new DynDns2Client(new FakeHttpClientFactory(handler));

        await client.UpdateAsync(
            new DynDns2Credentials("https://dyn.test/nic/update", "user@example.com", "p:ss", "Acme Tool/1.0 ops@example.com"),
            "home.ddns.net",
            new Dictionary<IpFamily, IPAddress> { [IpFamily.IPv4] = V4 },
            CancellationToken.None);

        var headers = Assert.Single(handler.Headers);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user@example.com:p:ss")), headers["Authorization"]);
        Assert.Equal("Acme Tool/1.0 ops@example.com", headers["User-Agent"]);
    }
}

public class UserAgentBuilderTests
{
    [Fact]
    [Trait("Req", "FR-411")]
    public void Default_product_is_used_when_not_customized()
    {
        Assert.Matches(@"^FlareSync FlareSync/(linux|windows|macos|other)-\d+\.\d+\.\d+ ops@example\.com$", UserAgentBuilder.Build(null, "ops@example.com"));
        Assert.Equal("Acme Box-1/2.0 ops@example.com", UserAgentBuilder.Build(" Acme Box-1/2.0 ", "ops@example.com"));
    }

    [Theory]
    [InlineData("ops@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("Ops <ops@example.com>", false)]
    [InlineData("", false)]
    public void Validates_contact(string contact, bool valid) => Assert.Equal(valid, UserAgentBuilder.ValidateContact(contact) is null);

    [Theory]
    [InlineData("Acme Tool/1.0", true)]
    [InlineData("Acme Tool-Linux/2.1-beta", true)]
    [InlineData("Tool/1.0", false)]
    [InlineData("Acme Tool", false)]
    public void Validates_product(string product, bool valid) => Assert.Equal(valid, UserAgentBuilder.ValidateProduct(product) is null);
}

public class DynDns2ProviderTests
{
    private static readonly IPAddress V4 = IPAddress.Parse("203.0.114.1");
    private static readonly IPAddress V6 = IPAddress.Parse("2a03:1b20:bef1::1");

    internal sealed class Harness : IDisposable
    {
        private readonly Queue<string> _answers = new();

        public Harness(DynDns2Preset? preset = null)
        {
            Preset = preset ?? DynDns2Preset.NoIp;
            Handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Text(_answers.Count > 1 ? _answers.Dequeue() : _answers.Peek())));

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Time);
            services.AddLogging();
            services.AddFlareSyncCore(Temp.Paths);
            services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));
            new DynDns2Module(Preset).ConfigureServices(services);
            services.AddSingleton(new DynDns2Client(new FakeHttpClientFactory(Handler)));
            Services = services.BuildServiceProvider();
        }

        public DynDns2Preset Preset { get; }

        public TempConfig Temp { get; } = new();

        public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

        public FakeHttpHandler Handler { get; }

        public ServiceProvider Services { get; }

        public DynDns2ConfigStore Store => Services.GetRequiredKeyedService<DynDns2ConfigStore>(Preset.Name);

        public DynDns2Provider Provider => Services.GetRequiredKeyedService<DynDns2Provider>(Preset.Name);

        public Harness Answer(params string[] answers)
        {
            foreach (var answer in answers)
            {
                _answers.Enqueue(answer);
            }

            return this;
        }

        public async Task<Harness> LoggedInAsync(params string[] hosts)
        {
            var config = new DynDns2Config { Username = "user@example.com", Contact = "ops@example.com" };
            Store.SetPassword(config, "secret");
            config.Records.AddRange(hosts.Select(h => new DynDns2Record { Hostname = h }));
            await Store.SaveAsync(config, CancellationToken.None);
            return this;
        }

        public Task<IReadOnlyDictionary<IpFamily, SyncResult>> UpdateAsync(string host, bool withIPv6 = false)
        {
            var addresses = new Dictionary<IpFamily, IPAddress> { [IpFamily.IPv4] = V4 };
            if (withIPv6)
            {
                addresses[IpFamily.IPv6] = V6;
            }

            return Provider.UpdateAsync(
                new DnsUpdate(new DnsTarget(Preset.Name, host, true, withIPv6), addresses, addresses.Keys.ToHashSet()),
                CancellationToken.None);
        }

        public void Dispose()
        {
            Services.Dispose();
            Temp.Dispose();
        }
    }

    [Fact]
    [Trait("Req", "FR-410")]
    public async Task Good_answer_updates_every_changed_family_in_one_request()
    {
        using var h = await new Harness().Answer("good 203.0.114.1,2a03:1b20:bef1::1").LoggedInAsync("home.ddns.net");

        var results = await h.UpdateAsync("home.ddns.net", withIPv6: true);

        Assert.All(results.Values, r => Assert.Equal(SyncOutcome.Updated, r.Outcome));
        var request = Assert.Single(h.Handler.Requests);
        Assert.StartsWith(DynDns2Preset.NoIp.DefaultServer!, request.Url);
    }

    [Fact]
    [Trait("Req", "FR-420")]
    public async Task Nochg_is_reported_as_unchanged()
    {
        using var h = await new Harness().Answer("nochg 203.0.114.1").LoggedInAsync("home.ddns.net");

        var results = await h.UpdateAsync("home.ddns.net");

        Assert.Equal(SyncOutcome.Unchanged, results[IpFamily.IPv4].Outcome);
    }

    [Fact]
    [Trait("Req", "FR-421")]
    public async Task Badauth_blocks_every_host_until_login_or_unblock()
    {
        using var h = await new Harness().Answer("badauth").LoggedInAsync("a.ddns.net", "b.ddns.net");

        Assert.Equal(SyncOutcome.Failed, (await h.UpdateAsync("a.ddns.net"))[IpFamily.IPv4].Outcome);
        var second = await h.UpdateAsync("b.ddns.net");

        Assert.Single(h.Handler.Requests);
        Assert.Contains("blocked", second[IpFamily.IPv4].Message);
        Assert.Equal("badauth", (await h.Store.LoadAsync(CancellationToken.None)).Blocked?.Reason);
    }

    [Fact]
    [Trait("Req", "FR-422")]
    public async Task Nohost_blocks_only_that_host()
    {
        using var h = await new Harness().Answer("nohost", "good 203.0.114.1").LoggedInAsync("missing.ddns.net", "ok.ddns.net");

        await h.UpdateAsync("missing.ddns.net");
        var again = await h.UpdateAsync("missing.ddns.net");
        var other = await h.UpdateAsync("ok.ddns.net");

        Assert.Equal(2, h.Handler.Requests.Count);
        Assert.Contains("unblock missing.ddns.net", again[IpFamily.IPv4].Message);
        Assert.Equal(SyncOutcome.Updated, other[IpFamily.IPv4].Outcome);
    }

    [Fact]
    [Trait("Req", "FR-423")]
    public async Task Server_error_waits_thirty_minutes()
    {
        using var h = await new Harness().Answer("911", "good 203.0.114.1").LoggedInAsync("home.ddns.net");

        await h.UpdateAsync("home.ddns.net");
        h.Time.Now += TimeSpan.FromMinutes(29);
        var early = await h.UpdateAsync("home.ddns.net");
        h.Time.Now += TimeSpan.FromMinutes(2);
        var later = await h.UpdateAsync("home.ddns.net");

        Assert.Equal(2, h.Handler.Requests.Count);
        Assert.Equal(SyncOutcome.Failed, early[IpFamily.IPv4].Outcome);
        Assert.Equal(SyncOutcome.Updated, later[IpFamily.IPv4].Outcome);
        Assert.Null((await h.Store.LoadAsync(CancellationToken.None)).RetryAfter);
    }

    [Fact]
    public async Task Missing_login_fails_without_request()
    {
        using var h = new Harness().Answer("good 203.0.114.1");
        await h.Store.SaveAsync(new DynDns2Config { Records = [new() { Hostname = "home.ddns.net" }] }, CancellationToken.None);

        var results = await h.UpdateAsync("home.ddns.net");

        Assert.Contains("flaresync noip login", results[IpFamily.IPv4].Message);
        Assert.Empty(h.Handler.Requests);
    }
}

public class DynDns2CommandTests
{
    private static CommandDefinition Find(CommandDefinition root, string name) => root.Subcommands.Single(c => c.Name == name);

    private static T Option<T>(CommandDefinition command, string name)
        where T : OptionDefinition
        => (T)command.Options.Single(o => o.Name == name);

    private static Task<int> RunAsync(DynDns2ProviderTests.Harness h, CommandDefinition command, FakeValues values, FakeConsole console)
        => command.Handler!(new CommandContext { Values = values, Console = console, Services = h.Services });

    [Fact]
    [Trait("Req", "FR-430")]
    public async Task Login_prompts_for_missing_values_and_encrypts_password()
    {
        using var h = new DynDns2ProviderTests.Harness();
        var root = new DynDns2Commands(h.Preset).Create();
        var console = new FakeConsole();
        console.Inputs.Enqueue("user@example.com");
        console.Inputs.Enqueue("s3cret");
        console.Inputs.Enqueue("ops@example.com");

        var exit = await RunAsync(h, Find(root, "login"), new FakeValues(), console);

        Assert.Equal(0, exit);
        var config = await h.Store.LoadAsync(CancellationToken.None);
        Assert.Equal("user@example.com", config.Username);
        Assert.Equal("ops@example.com", config.Contact);
        Assert.DoesNotContain("s3cret", await File.ReadAllTextAsync(h.Store.FilePath));
        Assert.Equal("s3cret", h.Store.GetCredentials(config).Password);
        Assert.Contains("User-Agent: FlareSync FlareSync/", console.Output.ToString());
    }

    [Fact]
    [Trait("Req", "FR-430")]
    public async Task Login_clears_blocks_and_keeps_custom_user_agent_until_reset()
    {
        using var h = new DynDns2ProviderTests.Harness();
        await h.Store.SaveAsync(new DynDns2Config
        {
            Contact = "ops@example.com",
            Blocked = new DynDns2Block { Reason = "badauth" },
            RetryAfter = DateTimeOffset.UtcNow.AddHours(1),
        }, CancellationToken.None);
        var root = new DynDns2Commands(h.Preset).Create();
        var login = Find(root, "login");
        var values = new FakeValues()
            .Set(Option<OptionDefinition<string?>>(login, "--username"), "u")
            .Set(Option<OptionDefinition<string?>>(login, "--password"), "p")
            .Set(Option<OptionDefinition<string?>>(login, "--user-agent"), "Acme Tool/1.0");

        await RunAsync(h, login, values, new FakeConsole { IsInteractive = false });

        var config = await h.Store.LoadAsync(CancellationToken.None);
        Assert.Null(config.Blocked);
        Assert.Null(config.RetryAfter);
        Assert.Equal("Acme Tool/1.0", config.UserAgentProduct);

        values.Set(Option<OptionDefinition<string?>>(login, "--user-agent"), DynDns2Commands.DefaultUserAgentKeyword);
        await RunAsync(h, login, values, new FakeConsole { IsInteractive = false });
        Assert.Null((await h.Store.LoadAsync(CancellationToken.None)).UserAgentProduct);
    }

    [Fact]
    [Trait("Req", "FR-431")]
    public async Task Generic_preset_requires_a_server()
    {
        using var h = new DynDns2ProviderTests.Harness(DynDns2Preset.Generic);
        var login = Find(new DynDns2Commands(h.Preset).Create(), "login");
        var values = new FakeValues()
            .Set(Option<OptionDefinition<string?>>(login, "--username"), "u")
            .Set(Option<OptionDefinition<string?>>(login, "--password"), "p")
            .Set(Option<OptionDefinition<string?>>(login, "--contact"), "ops@example.com");

        var ex = await Assert.ThrowsAsync<FlareSyncException>(() => RunAsync(h, login, values, new FakeConsole { IsInteractive = false }));
        Assert.Contains("--server", ex.Message);
    }

    [Fact]
    [Trait("Req", "FR-432")]
    public async Task Unblock_clears_account_and_host_blocks()
    {
        using var h = new DynDns2ProviderTests.Harness();
        await h.Store.SaveAsync(new DynDns2Config
        {
            Blocked = new DynDns2Block { Reason = "abuse" },
            Records = [new() { Hostname = "home.ddns.net", Blocked = new DynDns2Block { Reason = "nohost" } }],
        }, CancellationToken.None);
        var unblock = Find(new DynDns2Commands(h.Preset).Create(), "unblock");
        var host = (ArgumentDefinition<string?>)unblock.Arguments[0];

        await RunAsync(h, unblock, new FakeValues(), new FakeConsole());
        await RunAsync(h, unblock, new FakeValues().Set(host, "home.ddns.net"), new FakeConsole());

        var config = await h.Store.LoadAsync(CancellationToken.None);
        Assert.Null(config.Blocked);
        Assert.Null(config.Records[0].Blocked);
    }

    [Fact]
    [Trait("Req", "FR-304")]
    public void All_modules_form_a_valid_catalog()
    {
        var catalog = new CommandCatalog().AddBuiltinCommands();
        foreach (IProviderModule module in new IProviderModule[]
                 {
                     new FlareSync.Providers.Cloudflare.CloudflareModule(),
                     new DynDns2Module(DynDns2Preset.NoIp),
                     new DynDns2Module(DynDns2Preset.Generic),
                 })
        {
            module.RegisterCommands(catalog);
        }

        catalog.Validate();
        Assert.Contains(catalog.Commands, c => c.Name == "noip");
        Assert.Contains(catalog.Commands, c => c.Name == "dyndns2");
    }
}

