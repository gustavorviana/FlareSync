using FlareSync.Core.Models;

namespace FlareSync.Core.Config;

/// <summary>Content of <c>flaresync.json</c>.</summary>
public sealed class GlobalConfig
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    public DnsSettings Dns { get; set; } = new();

    public IpSourcesSettings IpSources { get; set; } = new();

    public static GlobalConfig CreateDefault() => new()
    {
        IpSources = new IpSourcesSettings
        {
            IPv4 =
            [
                new IpSourceSettings { Url = "https://ipv4.am.i.mullvad.net/ip" },
                new IpSourceSettings { Url = "https://api.ipify.org" },
            ],
            IPv6 =
            [
                new IpSourceSettings { Url = "https://ipv6.am.i.mullvad.net/ip" },
                new IpSourceSettings { Url = "https://api6.ipify.org" },
            ],
        },
    };
}

public sealed class DnsSettings
{
    /// <summary>Custom DNS servers (<c>host[:port]</c>) used to resolve IP sources. Empty = system resolver.</summary>
    public List<string> Servers { get; set; } = [];
}

public sealed class IpSourcesSettings
{
    public List<IpSourceSettings> IPv4 { get; set; } = [];

    public List<IpSourceSettings> IPv6 { get; set; } = [];

    public List<IpSourceSettings> For(IpFamily family) => family == IpFamily.IPv4 ? IPv4 : IPv6;
}

public sealed class IpSourceSettings
{
    public string Url { get; set; } = "";

    /// <summary>When set, the response is JSON and this (dot-separated) field holds the address.</summary>
    public string? JsonField { get; set; }
}

/// <summary>Loads and saves <see cref="GlobalConfig"/>.</summary>
public sealed class GlobalConfigStore(ConfigPaths paths, JsonConfigStore store)
{
    public bool Exists => File.Exists(paths.GlobalFile);

    /// <summary>Returns the stored configuration, or the defaults when the file does not exist.</summary>
    public async Task<GlobalConfig> LoadAsync(CancellationToken cancellationToken)
        => await store.ReadAsync<GlobalConfig>(paths.GlobalFile, cancellationToken) ?? GlobalConfig.CreateDefault();

    public Task SaveAsync(GlobalConfig config, CancellationToken cancellationToken)
        => store.WriteAsync(paths.GlobalFile, config, cancellationToken);

    public async Task UpdateAsync(Action<GlobalConfig> update, CancellationToken cancellationToken)
    {
        var config = await LoadAsync(cancellationToken);
        update(config);
        await SaveAsync(config, cancellationToken);
    }
}
