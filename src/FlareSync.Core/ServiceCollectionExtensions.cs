using FlareSync.Core.Abstractions;
using FlareSync.Core.Config;
using FlareSync.Core.IpResolution;
using FlareSync.Core.Models;
using FlareSync.Core.Platform;
using FlareSync.Core.Secrets;
using FlareSync.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlareSync.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the core services for the given configuration directory.</summary>
    public static IServiceCollection AddFlareSyncCore(this IServiceCollection services, ConfigPaths paths)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(paths);
        services.AddSingleton<JsonConfigStore>();
        services.AddSingleton<GlobalConfigStore>();
        services.AddSingleton<MasterKeyProvider>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<HostResolver>();
        services.AddSingleton<IHostResolver>(sp => sp.GetRequiredService<HostResolver>());
        services.AddSingleton<IIpResolver, IpResolver>();
        services.AddSingleton<SyncStateStore>();
        services.AddSingleton<SyncOrchestrator>();
        services.TryAddSingleton<IBrowserLauncher, BrowserLauncher>();

        foreach (var family in Enum.GetValues<IpFamily>())
        {
            services.AddHttpClient(IpResolver.ClientName(family))
                .ConfigurePrimaryHttpMessageHandler(sp => FamilyBoundHandler.Create(sp.GetRequiredService<IHostResolver>(), family))
                .AddStandardResilienceHandler(options =>
                {
                    // Sources have their own fallback, so fail fast per source.
                    options.Retry.MaxRetryAttempts = 1;
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
                });
        }

        return services;
    }
}
