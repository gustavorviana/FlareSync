using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Config;
using FlareSync.Core.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlareSync.Providers.DynDns2;

/// <summary>
/// DynDNS2 provider module. One instance per <see cref="DynDns2Preset"/>; per-instance services are keyed by the preset name.
/// </summary>
public sealed class DynDns2Module(DynDns2Preset preset) : IProviderModule
{
    public string Name => preset.Name;

    public void ConfigureServices(IServiceCollection services)
    {
        // No resilience handler: retrying an update could be treated as abuse by the service.
        services.AddHttpClient(DynDns2Client.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddSingleton<DynDns2Client>();

        services.AddKeyedSingleton(preset.Name, (sp, _) => new DynDns2ConfigStore(
            preset,
            sp.GetRequiredService<ConfigPaths>(),
            sp.GetRequiredService<JsonConfigStore>(),
            sp.GetRequiredService<ISecretProtector>()));

        services.AddKeyedSingleton(preset.Name, (sp, _) => new DynDns2Provider(
            preset,
            sp.GetRequiredKeyedService<DynDns2ConfigStore>(preset.Name),
            sp.GetRequiredService<DynDns2Client>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredKeyedService<DynDns2Provider>(preset.Name));
    }

    public void RegisterCommands(CommandCatalog catalog) => catalog.Add(new DynDns2Commands(preset).Create());
}
