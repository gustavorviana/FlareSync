using System.Net;
using FlareSync.Core.Commands;
using FlareSync.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FlareSync.Core.Abstractions;

/// <summary>
/// A pluggable DNS provider module. Modules depend only on FlareSync.Core.
/// </summary>
public interface IProviderModule
{
    /// <summary>Unique lowercase name: command group and configuration file name (<c>providers/&lt;name&gt;.json</c>).</summary>
    string Name { get; }

    /// <summary>Registers the module services, including its <see cref="IDnsProvider"/>.</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Registers the module commands in the central catalog.</summary>
    void RegisterCommands(CommandCatalog catalog);
}

/// <summary>Updates DNS records at a provider.</summary>
public interface IDnsProvider
{
    /// <summary>Same value as the owning <see cref="IProviderModule.Name"/>.</summary>
    string Name { get; }

    /// <summary>Loads the records currently configured for this provider.</summary>
    Task<IReadOnlyList<DnsTarget>> GetTargetsAsync(CancellationToken cancellationToken);

    /// <summary>Makes the record of <paramref name="family"/> for <paramref name="target"/> point to <paramref name="address"/>.</summary>
    Task<SyncResult> UpsertAsync(DnsTarget target, IpFamily family, IPAddress address, CancellationToken cancellationToken);
}

/// <summary>Detects the public address of the host.</summary>
public interface IIpResolver
{
    Task<IpDetection> DetectAsync(IpFamily family, CancellationToken cancellationToken);
}

/// <summary>Resolves host names of IP sources, optionally through custom DNS servers.</summary>
public interface IHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, IpFamily family, CancellationToken cancellationToken);
}

/// <summary>Opens URLs in the user's browser when a graphical environment is available.</summary>
public interface IBrowserLauncher
{
    bool CanOpen { get; }

    bool TryOpen(string url);
}
