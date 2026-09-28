using System.Net;
using System.Security.Cryptography;
using FlareSync.Core;
using FlareSync.Core.Commands;
using FlareSync.Core.Config;
using FlareSync.Core.Models;
using FlareSync.Core.Secrets;
using FlareSync.Core.Sync;
using FlareSync.Providers.Cloudflare;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlareSync.Tests;

public class CloudflareDnsProviderTests
{
    private static readonly IPAddress Address = IPAddress.Parse("203.0.114.9");

    private static readonly CloudflareRecord Record = new() { Hostname = "home.example.com", ZoneId = "zone1", Ttl = 1 };

    private static (CloudflareDnsProvider Provider, FakeHttpHandler Handler) Create(Func<HttpRequestMessage, string> respond)
    {
        var handler = new FakeHttpHandler(r => Task.FromResult(FakeHttpHandler.Json(respond(r))));
        var api = new CloudflareApiClient(new FakeHttpClientFactory(handler));
        var provider = new CloudflareDnsProvider(null!, api, NullLogger<CloudflareDnsProvider>.Instance);
        return (provider, handler);
    }

    private const string Empty = """{"success":true,"errors":[],"result":[]}""";
    private const string Written = """{"success":true,"errors":[],"result":{"id":"r1","type":"A","name":"home.example.com","content":"203.0.114.9","ttl":1,"proxied":false}}""";

    private static string Existing(string content, int ttl = 1, bool proxied = false)
        => $$"""{"success":true,"errors":[],"result":[{"id":"r1","type":"A","name":"home.example.com","content":"{{content}}","ttl":{{ttl}},"proxied":{{(proxied ? "true" : "false")}}}]}""";

    [Fact]
    [Trait("Req", "FR-130")]
    public async Task Creates_missing_record()
    {
        var (provider, handler) = Create(r => r.Method == HttpMethod.Get ? Empty : Written);

        var result = await provider.UpsertAsync("token", Record, IpFamily.IPv4, Address, CancellationToken.None);

        Assert.Equal(SyncOutcome.Created, result.Outcome);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("zones/zone1/dns_records", handler.Requests[1].Url);
        Assert.Contains("\"content\":\"203.0.114.9\"", handler.Requests[1].Body);
        Assert.Contains("\"type\":\"A\"", handler.Requests[1].Body);
        Assert.Contains("type=A&name=home.example.com", handler.Requests[0].Url);
    }

    [Fact]
    [Trait("Req", "FR-130")]
    public async Task Patches_record_with_different_address()
    {
        var (provider, handler) = Create(r => r.Method == HttpMethod.Get ? Existing("198.51.101.1") : Written);

        var result = await provider.UpsertAsync("token", Record, IpFamily.IPv4, Address, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, result.Outcome);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        Assert.EndsWith("zones/zone1/dns_records/r1", handler.Requests[1].Url);
    }

    [Fact]
    [Trait("Req", "FR-130")]
    public async Task Leaves_up_to_date_record_alone()
    {
        var (provider, handler) = Create(_ => Existing("203.0.114.9"));

        var result = await provider.UpsertAsync("token", Record, IpFamily.IPv4, Address, CancellationToken.None);

        Assert.Equal(SyncOutcome.Unchanged, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Uses_aaaa_for_ipv6()
    {
        var (provider, handler) = Create(r => r.Method == HttpMethod.Get ? Empty : Written);

        await provider.UpsertAsync("token", Record, IpFamily.IPv6, IPAddress.Parse("2a03:1b20:bef1::1"), CancellationToken.None);

        Assert.Contains("type=AAAA", handler.Requests[0].Url);
        Assert.Contains("\"type\":\"AAAA\"", handler.Requests[1].Body);
    }

    [Fact]
    [Trait("Req", "FR-131")]
    public async Task Api_errors_are_surfaced()
    {
        var (provider, _) = Create(_ => """{"success":false,"errors":[{"code":10000,"message":"Authentication error"}],"result":null}""");

        var ex = await Assert.ThrowsAsync<CloudflareApiException>(() =>
            provider.UpsertAsync("token", Record, IpFamily.IPv4, Address, CancellationToken.None));
        Assert.Contains("Authentication error", ex.Message);
    }
}

public class ZoneMatcherTests
{
    private static readonly CloudflareZone[] Zones =
    [
        new() { Id = "1", Name = "example.com" },
        new() { Id = "2", Name = "sub.example.com" },
        new() { Id = "3", Name = "exemplo.com.br" },
    ];

    [Theory]
    [Trait("Req", "FR-121")]
    [InlineData("home.example.com", "1")]
    [InlineData("a.sub.example.com", "2")]
    [InlineData("sub.example.com", "2")]
    [InlineData("A.B.Exemplo.com.br.", "3")]
    [InlineData("example.com", "1")]
    public void Picks_longest_matching_zone(string hostname, string zoneId)
        => Assert.Equal(zoneId, ZoneMatcher.FindZone(hostname, Zones)?.Id);

    [Theory]
    [InlineData("notexample.com")]
    [InlineData("example.org")]
    public void Returns_null_when_no_zone_matches(string hostname)
        => Assert.Null(ZoneMatcher.FindZone(hostname, Zones));
}

public class CloudflareTokenTemplateTests
{
    [Fact]
    [Trait("Req", "FR-110")]
    public void Link_prefills_name_and_permissions()
    {
        var url = new Uri(CloudflareTokenTemplate.BuildUrl());
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("dash.cloudflare.com", url.Host);
        Assert.Equal("/profile/api-tokens", url.AbsolutePath);
        Assert.Equal("""[{"key":"zone","type":"read"},{"key":"dns","type":"edit"}]""", query["permissionGroupKeys"]);
        Assert.Equal("*", query["accountId"]);
        Assert.Equal("all", query["zoneId"]);
        Assert.Equal("FlareSync", query["name"]);
    }
}

public class CloudflareCommandTests
{
    private sealed class Harness : IDisposable
    {
        public Harness(Func<HttpRequestMessage, string> respond)
        {
            Handler = new FakeHttpHandler(r => Task.FromResult(FakeHttpHandler.Json(respond(r))));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFlareSyncCore(Temp.Paths);
            services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));
            new CloudflareModule().ConfigureServices(services);
            services.AddSingleton(new CloudflareApiClient(new FakeHttpClientFactory(Handler)));
            Services = services.BuildServiceProvider();
        }

        public TempConfig Temp { get; } = new();

        public FakeHttpHandler Handler { get; }

        public ServiceProvider Services { get; }

        public FakeConsole Console { get; } = new();

        public CloudflareConfigStore Store => Services.GetRequiredService<CloudflareConfigStore>();

        public Task<int> RunAsync(string path, FakeValues values)
        {
            var command = CloudflareCommands.Create();
            foreach (var name in path.Split(' '))
            {
                command = command.Subcommands.Single(c => c.Name == name);
            }

            return command.Handler!(new CommandContext { Values = values, Console = Console, Services = Services });
        }

        public static TOption Option<TOption>(string path, string name)
            where TOption : OptionDefinition
            => (TOption)CloudflareCommands.Create().Subcommands.Single(c => c.Name == path).Options.Single(o => o.Name == name);

        public void Dispose()
        {
            Services.Dispose();
            Temp.Dispose();
        }
    }

    private const string Zones = """{"success":true,"errors":[],"result":[{"id":"z1","name":"example.com","status":"active"}],"result_info":{"page":1,"total_pages":1}}""";
    private const string Verify = """{"success":true,"errors":[],"result":{"id":"t","status":"active"}}""";

    private static string Respond(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("verify") ? Verify : Zones;

    [Fact]
    [Trait("Req", "FR-112")]
    [Trait("Req", "FR-101")]
    public async Task Login_reads_pasted_token_and_stores_it_encrypted()
    {
        using var h = new Harness(Respond);
        h.Console.Inputs.Enqueue("  secret-token  ");

        var exit = await h.RunAsync("login", new FakeValues().Set(Harness.Option<OptionDefinition<bool>>("login", "--no-browser"), true));

        Assert.Equal(0, exit);
        Assert.Contains("dash.cloudflare.com/profile/api-tokens", h.Console.Output.ToString());
        var raw = await File.ReadAllTextAsync(h.Store.FilePath);
        Assert.DoesNotContain("secret-token", raw);
        Assert.Equal("secret-token", h.Store.GetToken(await h.Store.LoadAsync(CancellationToken.None)));
        Assert.Contains(h.Handler.Requests, r => r.Url.EndsWith("user/tokens/verify"));
    }

    [Fact]
    [Trait("Req", "FR-113")]
    public async Task Login_without_terminal_requires_token_option()
    {
        using var h = new Harness(Respond);
        h.Console.IsInteractive = false;

        await Assert.ThrowsAsync<FlareSyncException>(() => h.RunAsync("login", new FakeValues()));
    }

    [Fact]
    [Trait("Req", "FR-121")]
    public async Task Add_detects_zone_and_saves_record()
    {
        using var h = new Harness(Respond);
        var config = new CloudflareConfig();
        h.Store.SetToken(config, "token");
        await h.Store.SaveAsync(config, CancellationToken.None);

        var add = CloudflareCommands.Create().Subcommands.Single(c => c.Name == "add");
        var hostname = (ArgumentDefinition<string>)add.Arguments[0];
        var family = (OptionDefinition<IpFamilies>)add.Options.Single(o => o.Name == "--family");

        Assert.Equal(0, await h.RunAsync("add", new FakeValues().Set(hostname, "Home.Example.com")));
        Assert.Equal(0, await h.RunAsync("add", new FakeValues().Set(hostname, "www.example.com").Set(family, IpFamilies.Both)));

        var records = (await h.Store.LoadAsync(CancellationToken.None)).Records;
        Assert.Equal(2, records.Count);
        Assert.Equal("home.example.com", records[0].Hostname);
        Assert.Equal("z1", records[0].ZoneId);
        Assert.True(records[0].IPv4);
        Assert.False(records[0].IPv6);
        Assert.True(records[1].IPv4);
        Assert.True(records[1].IPv6);
    }

    [Fact]
    [Trait("Req", "FR-123")]
    public async Task Set_and_remove_update_the_provider_file()
    {
        using var h = new Harness(Respond);
        await h.Store.SaveAsync(new CloudflareConfig { Records = [new() { Hostname = "home.example.com", ZoneId = "z1" }] }, CancellationToken.None);
        var state = h.Services.GetRequiredService<SyncStateStore>();
        await state.SetAsync("cloudflare", "home.example.com", IpFamily.IPv4, "203.0.114.1", CancellationToken.None);

        var set = CloudflareCommands.Create().Subcommands.Single(c => c.Name == "set");
        var hostname = (ArgumentDefinition<string>)set.Arguments[0];
        var proxied = (OptionDefinition<bool?>)set.Options.Single(o => o.Name == "--proxied");
        var family = (OptionDefinition<IpFamilies?>)set.Options.Single(o => o.Name == "--family");

        Assert.Equal(0, await h.RunAsync("set", new FakeValues().Set(hostname, "home.example.com").Set(proxied, true).Set(family, IpFamilies.IPv6)));
        var record = (await h.Store.LoadAsync(CancellationToken.None)).Records[0];
        Assert.True(record.Proxied);
        Assert.False(record.IPv4);
        Assert.True(record.IPv6);
        Assert.Null(await state.GetAsync("cloudflare", "home.example.com", IpFamily.IPv4, CancellationToken.None));

        Assert.Equal(0, await h.RunAsync("remove", new FakeValues().Set(hostname, "home.example.com")));
        Assert.Empty((await h.Store.LoadAsync(CancellationToken.None)).Records);
    }
}
