using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace FlareSync.Providers.Cloudflare;

public sealed class CloudflareModule : IProviderModule
{
    public const string ProviderName = "cloudflare";

    public string Name => ProviderName;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient(CloudflareApiClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        services.AddSingleton<CloudflareApiClient>();
        services.AddSingleton<CloudflareConfigStore>();
        services.AddSingleton<CloudflareDnsProvider>();
        services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<CloudflareDnsProvider>());
    }

    public void RegisterCommands(CommandCatalog catalog) => catalog.Add(CloudflareCommands.Create());
}
