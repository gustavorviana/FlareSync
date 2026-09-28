using FlareSync.Core;
using FlareSync.Core.Config;
using FlareSync.Core.Secrets;

namespace FlareSync.Providers.Cloudflare;

/// <summary>Content of <c>providers/cloudflare.json</c>: one credential and every record it serves.</summary>
public sealed class CloudflareConfig
{
    /// <summary>Encrypted API token (<c>enc:v1:...</c>).</summary>
    public string? ApiToken { get; set; }

    public List<CloudflareRecord> Records { get; set; } = [];

    public CloudflareRecord? Find(string hostname)
        => Records.Find(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
}

public sealed class CloudflareRecord
{
    public const int AutomaticTtl = 1;

    public string Hostname { get; set; } = "";

    public string ZoneId { get; set; } = "";

    public string? ZoneName { get; set; }

    public bool IPv4 { get; set; } = true;

    public bool IPv6 { get; set; } = true;

    public bool Proxied { get; set; }

    /// <summary>Record TTL in seconds; 1 means automatic.</summary>
    public int Ttl { get; set; } = AutomaticTtl;

    public static string? ValidateTtl(int ttl)
        => ttl == AutomaticTtl || ttl is >= 60 and <= 86400 ? null : "TTL must be 1 (automatic) or between 60 and 86400 seconds.";
}

/// <summary>Loads and saves <see cref="CloudflareConfig"/>, encrypting the API token.</summary>
public sealed class CloudflareConfigStore(ConfigPaths paths, JsonConfigStore store, ISecretProtector protector)
{
    public string FilePath => paths.ProviderFile(CloudflareModule.ProviderName);

    public async Task<CloudflareConfig> LoadAsync(CancellationToken cancellationToken)
        => await store.ReadAsync<CloudflareConfig>(FilePath, cancellationToken) ?? new CloudflareConfig();

    public Task SaveAsync(CloudflareConfig config, CancellationToken cancellationToken)
        => store.WriteAsync(FilePath, config, cancellationToken);

    public void SetToken(CloudflareConfig config, string plainToken) => config.ApiToken = protector.Protect(plainToken);

    /// <summary>Returns the decrypted token, throwing when not logged in.</summary>
    public string GetToken(CloudflareConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiToken))
        {
            throw new FlareSyncException("Not logged in to Cloudflare. Run 'flaresync cloudflare login'.");
        }

        return protector.IsProtected(config.ApiToken) ? protector.Unprotect(config.ApiToken) : config.ApiToken;
    }
}
