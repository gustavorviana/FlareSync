using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Commands.Builtin;
using FlareSync.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;

namespace FlareSync.Cli;

/// <summary><c>run</c>: service mode on top of the .NET Generic Host (systemd / Windows Service aware).</summary>
internal static class RunCommand
{
    public static CommandDefinition Create(IReadOnlyList<IProviderModule> modules) => new()
    {
        Name = "run",
        Description = "Run as a service: sync now and then at the configured interval.",
        Handler = async ctx =>
        {
            var paths = ctx.Service<ConfigPaths>();
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = "FlareSync",
            });

            builder.Services.AddSystemd();
            builder.Services.AddWindowsService(o => o.ServiceName = "FlareSync");
            // journald adds its own timestamps and reads the priority prefix; in a terminal show time and level.
            var systemd = SystemdHelpers.IsSystemdService();
            ServiceSetup.ConfigureLogging(
                builder.Logging, ctx.Get(CoreOptions.Verbose), LogLevel.Information,
                systemd, timestamps: !systemd, toStandardError: false);
            ServiceSetup.AddFlareSync(builder.Services, paths, modules);
            builder.Services.AddHostedService<SyncWorker>();

            using var host = builder.Build();
            await host.RunAsync(ctx.CancellationToken);
            return 0;
        },
    };
}
