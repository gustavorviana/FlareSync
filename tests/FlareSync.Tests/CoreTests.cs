using System.Net;
using System.Security.Cryptography;
using FlareSync.Core;
using FlareSync.Core.Config;
using FlareSync.Core.IpResolution;
using FlareSync.Core.Models;
using FlareSync.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlareSync.Tests;

public class SecretProtectorTests
{
    [Fact]
    [Trait("Req", "FR-101")]
    public void Round_trips_and_hides_plaintext()
    {
        var protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));

        var encrypted = protector.Protect("my-token");

        Assert.StartsWith(AesGcmSecretProtector.Prefix, encrypted);
        Assert.DoesNotContain("my-token", encrypted);
        Assert.Equal("my-token", protector.Unprotect(encrypted));
    }

    [Fact]
    public void Wrong_key_fails()
    {
        var encrypted = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)).Protect("my-token");
        var other = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));

        Assert.Throws<FlareSyncException>(() => other.Unprotect(encrypted));
    }

    [Fact]
    public void Tampered_value_fails()
    {
        var protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
        var payload = Convert.FromBase64String(protector.Protect("my-token")[AesGcmSecretProtector.Prefix.Length..]);
        payload[^1] ^= 0xFF;

        Assert.Throws<FlareSyncException>(() => protector.Unprotect(AesGcmSecretProtector.Prefix + Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Master_key_file_is_created_once_and_reused()
    {
        using var temp = new TempConfig();
        var keys = new MasterKeyProvider(temp.Paths);
        if (keys.DescribeSource() is not null && !File.Exists(temp.Paths.SecretKeyFile))
        {
            return; // A key is supplied by the environment of the test run.
        }

        var encrypted = new AesGcmSecretProtector(keys).Protect("secret");

        Assert.True(File.Exists(temp.Paths.SecretKeyFile));
        Assert.Equal("secret", new AesGcmSecretProtector(new MasterKeyProvider(temp.Paths)).Unprotect(encrypted));
        Assert.False(keys.EnsureKeyFile());
    }
}

public class JsonConfigStoreTests
{
    [Fact]
    [Trait("Req", "FR-204")]
    public async Task Writes_atomically_and_round_trips()
    {
        using var temp = new TempConfig();
        var config = GlobalConfig.CreateDefault();
        config.Interval = TimeSpan.FromMinutes(7);
        config.Dns.Servers.Add("1.1.1.1:53");

        await temp.Store.WriteAsync(temp.Paths.GlobalFile, config, CancellationToken.None);
        var loaded = await temp.Store.ReadAsync<GlobalConfig>(temp.Paths.GlobalFile, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(TimeSpan.FromMinutes(7), loaded.Interval);
        Assert.Equal(["1.1.1.1:53"], loaded.Dns.Servers);
        Assert.Equal(2, loaded.IpSources.IPv4.Count);
        Assert.False(File.Exists(temp.Paths.GlobalFile + ".tmp"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(temp.Paths.GlobalFile));
        }
    }

    [Fact]
    public async Task Missing_global_file_yields_defaults()
    {
        using var temp = new TempConfig();
        var config = await new GlobalConfigStore(temp.Paths, temp.Store).LoadAsync(CancellationToken.None);

        Assert.Equal("https://ipv4.am.i.mullvad.net/ip", config.IpSources.IPv4[0].Url);
        Assert.Empty(config.Dns.Servers);
    }
}

public class DnsServerEndpointTests
{
    [Theory]
    [Trait("Req", "FR-006")]
    [InlineData("1.1.1.1", "1.1.1.1:53")]
    [InlineData("1.1.1.1:5353", "1.1.1.1:5353")]
    [InlineData("2606:4700:4700::1111", "[2606:4700:4700::1111]:53")]
    [InlineData("[2606:4700:4700::1111]:853", "[2606:4700:4700::1111]:853")]
    [InlineData("[::1]", "[::1]:53")]
    public void Parses_server_notations(string input, string expected)
    {
        Assert.True(DnsServerEndpoint.TryParse(input, out var endpoint));
        Assert.Equal(expected, DnsServerEndpoint.Format(endpoint));
    }

    [Theory]
    [InlineData("dns.google")]
    [InlineData("1.1.1.1:0")]
    [InlineData("1.1.1.1:abc")]
    [InlineData("[::1")]
    [InlineData("")]
    public void Rejects_invalid_servers(string input) => Assert.False(DnsServerEndpoint.TryParse(input, out _));
}

public class IpAddressRulesTests
{
    [Theory]
    [Trait("Req", "FR-004")]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.5.4", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("2a03:1b20:bef1::1", true)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::1", false)]
    [InlineData("2001:db8::1", false)]
    public void Detects_public_addresses(string address, bool expected)
        => Assert.Equal(expected, IpAddressRules.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public void Rejects_wrong_family()
        => Assert.NotNull(IpAddressRules.Validate(IPAddress.Parse("8.8.8.8"), IpFamily.IPv6));
}

public class HostResolverTests
{
    [Fact]
    public async Task Ip_literals_are_returned_only_for_their_family()
    {
        using var temp = new TempConfig();
        var resolver = new HostResolver(new GlobalConfigStore(temp.Paths, temp.Store));

        Assert.Equal([IPAddress.Parse("1.2.3.4")], await resolver.ResolveAsync("1.2.3.4", IpFamily.IPv4, [], CancellationToken.None));
        Assert.Empty(await resolver.ResolveAsync("1.2.3.4", IpFamily.IPv6, [], CancellationToken.None));
    }

    [Fact]
    [Trait("Req", "FR-006")]
    public async Task Invalid_configured_server_is_reported()
    {
        using var temp = new TempConfig();
        var resolver = new HostResolver(new GlobalConfigStore(temp.Paths, temp.Store));

        await Assert.ThrowsAsync<FlareSyncException>(() =>
            resolver.ResolveAsync("example.com", IpFamily.IPv4, ["not-a-server"], CancellationToken.None));
    }
}

public class IpResolverTests
{
    private static async Task<(IpDetection Detection, FakeHttpHandler Handler)> DetectAsync(
        IpFamily family, Action<GlobalConfig> configure, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        using var temp = new TempConfig();
        var store = new GlobalConfigStore(temp.Paths, temp.Store);
        var config = new GlobalConfig();
        configure(config);
        await store.SaveAsync(config, CancellationToken.None);

        var handler = new FakeHttpHandler(respond);
        var resolver = new IpResolver(new FakeHttpClientFactory(handler), store, NullLogger<IpResolver>.Instance);
        return (await resolver.DetectAsync(family, CancellationToken.None), handler);
    }

    [Fact]
    [Trait("Req", "FR-002")]
    public async Task Falls_back_to_next_source()
    {
        var (detection, handler) = await DetectAsync(
            IpFamily.IPv4,
            c => c.IpSources.IPv4 = [new() { Url = "https://one.test/ip" }, new() { Url = "https://two.test/ip" }],
            r => Task.FromResult(r.RequestUri!.Host == "one.test"
                ? FakeHttpHandler.Text("oops", HttpStatusCode.InternalServerError)
                : FakeHttpHandler.Text("203.0.114.7\n")));

        Assert.Equal(IPAddress.Parse("203.0.114.7"), detection.Address);
        Assert.Equal("https://two.test/ip", detection.Source);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(detection.Errors);
    }

    [Theory]
    [Trait("Req", "FR-004")]
    [InlineData("not an ip")]
    [InlineData("192.168.0.10")]
    [InlineData("2a03:1b20:bef1::1")]
    public async Task Rejects_invalid_private_or_wrong_family_answers(string body)
    {
        var (detection, _) = await DetectAsync(
            IpFamily.IPv4,
            c => c.IpSources.IPv4 = [new() { Url = "https://one.test/ip" }],
            _ => Task.FromResult(FakeHttpHandler.Text(body)));

        Assert.False(detection.Succeeded);
        Assert.Single(detection.Errors);
    }

    [Fact]
    [Trait("Req", "FR-005")]
    public async Task Reads_address_from_json_field()
    {
        var (detection, _) = await DetectAsync(
            IpFamily.IPv6,
            c => c.IpSources.IPv6 = [new() { Url = "https://json.test/", JsonField = "data.ip" }],
            _ => Task.FromResult(FakeHttpHandler.Json("""{"data":{"ip":"2a03:1b20:bef1::1"}}""")));

        Assert.Equal(IPAddress.Parse("2a03:1b20:bef1::1"), detection.Address);
    }

    [Fact]
    public async Task No_sources_is_reported()
    {
        var (detection, _) = await DetectAsync(IpFamily.IPv6, c => c.IpSources.IPv6 = [], _ => throw new InvalidOperationException());

        Assert.False(detection.Succeeded);
        Assert.Contains("no IPv6 sources", detection.Errors[0]);
    }
}
