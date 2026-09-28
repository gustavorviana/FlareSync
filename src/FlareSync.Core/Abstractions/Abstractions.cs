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

    /// <summary>
    /// Updates the records of one target. Called only when <see cref="DnsUpdate.Changed"/> is not empty.
    /// Returns a result per family; families of <see cref="DnsUpdate.Changed"/> without a result count as failed.
    /// </summary>
    Task<IReadOnlyDictionary<IpFamily, SyncResult>> UpdateAsync(DnsUpdate update, CancellationToken cancellationToken);
}

/// <summary>
/// Update request for one target. <see cref="Addresses"/> holds every enabled and detected family (protocols such as
/// DynDNS2 send them together); <see cref="Changed"/> the families whose address differs from the last applied one.
/// </summary>
public sealed record DnsUpdate(
    DnsTarget Target,
    IReadOnlyDictionary<IpFamily, IPAddress> Addresses,
    IReadOnlySet<IpFamily> Changed);

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
