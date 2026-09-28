using FlareSync.Cli;
using FlareSync.Cli.CommandLine;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Commands;
using FlareSync.Core.Commands.Builtin;
using FlareSync.Providers.Cloudflare;
using FlareSync.Providers.DynDns2;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Provider modules. Adding a provider = adding it here.
IProviderModule[] modules =
[
    new CloudflareModule(),
    new DynDns2Module(DynDns2Preset.NoIp),
    new DynDns2Module(DynDns2Preset.Generic),
];

var catalog = new CommandCatalog().AddBuiltinCommands();
catalog.Add(RunCommand.Create(modules));
foreach (var module in modules)
{
    module.RegisterCommands(catalog);
}

catalog.Validate();

var adapter = new SystemCommandLineAdapter(
    catalog,
    values => ServiceSetup.BuildForCommand(values, modules),
    new ConsoleCommandConsole(),
    values => values.Get(CoreOptions.Verbose));

return await adapter.InvokeAsync(args);
