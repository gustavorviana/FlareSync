using FlareSync.Core;
using FlareSync.Core.Config;
using FlareSync.Core.Secrets;

namespace FlareSync.Providers.DynDns2;

/// <summary>Content of <c>providers/&lt;preset&gt;.json</c>: one credential and every host it updates.</summary>
public sealed class DynDns2Config
{
    /// <summary>Update URL; overrides the preset default.</summary>
    public string? Server { get; set; }

    public string? Username { get; set; }

    /// <summary>Encrypted password (<c>enc:v1:...</c>).</summary>
    public string? Password { get; set; }

    /// <summary>Maintainer e-mail sent in the User-Agent (required by No-IP).</summary>
    public string? Contact { get; set; }

    /// <summary>"Company Program/Version" part of the User-Agent; <c>null</c> = FlareSync default.</summary>
    public string? UserAgentProduct { get; set; }

    /// <summary>Set after a fatal answer (badauth, abuse...): no update is sent until the user intervenes.</summary>
    public DynDns2Block? Blocked { get; set; }

    /// <summary>Set after a server error (911): no update is sent before this time.</summary>
    public DateTimeOffset? RetryAfter { get; set; }

    public List<DynDns2Record> Records { get; set; } = [];

    public DynDns2Record? Find(string hostname)
        => Records.Find(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
}

public sealed class DynDns2Record
{
    public string Hostname { get; set; } = "";

    public bool IPv4 { get; set; } = true;

    public bool IPv6 { get; set; } = true;

    /// <summary>Set after a host-specific fatal answer (nohost, notfqdn...).</summary>
    public DynDns2Block? Blocked { get; set; }
}

public sealed class DynDns2Block
{
    /// <summary>Response code returned by the server, e.g. <c>badauth</c>.</summary>
    public string Reason { get; set; } = "";

    public string Message { get; set; } = "";

    public DateTimeOffset Since { get; set; }
}

/// <summary>Resolved values needed to send an update.</summary>
public sealed record DynDns2Credentials(string Server, string Username, string Password, string UserAgent);

/// <summary>Loads and saves <see cref="DynDns2Config"/> for one preset, encrypting the password.</summary>
public sealed class DynDns2ConfigStore(DynDns2Preset preset, ConfigPaths paths, JsonConfigStore store, ISecretProtector protector)
{
    public DynDns2Preset Preset => preset;

    public string FilePath => paths.ProviderFile(preset.Name);

    public async Task<DynDns2Config> LoadAsync(CancellationToken cancellationToken)
        => await store.ReadAsync<DynDns2Config>(FilePath, cancellationToken) ?? new DynDns2Config();

    public Task SaveAsync(DynDns2Config config, CancellationToken cancellationToken)
        => store.WriteAsync(FilePath, config, cancellationToken);

    public void SetPassword(DynDns2Config config, string password) => config.Password = protector.Protect(password);

    public string? Server(DynDns2Config config) => string.IsNullOrWhiteSpace(config.Server) ? preset.DefaultServer : config.Server;

    /// <summary>Returns everything needed to send an update, throwing when the login is incomplete.</summary>
    public DynDns2Credentials GetCredentials(DynDns2Config config)
    {
        var server = Server(config);
        if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password)
            || string.IsNullOrWhiteSpace(config.Contact) || server is null)
        {
            throw new FlareSyncException($"Not logged in to {preset.DisplayName}. Run 'flaresync {preset.Name} login'.");
        }

        var password = protector.IsProtected(config.Password) ? protector.Unprotect(config.Password) : config.Password;
        return new DynDns2Credentials(server, config.Username, password, UserAgentBuilder.Build(config.UserAgentProduct, config.Contact));
    }
}
