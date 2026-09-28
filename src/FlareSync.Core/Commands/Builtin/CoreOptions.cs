namespace FlareSync.Core.Commands.Builtin;

/// <summary>Global options available on every command.</summary>
public static class CoreOptions
{
    public static readonly OptionDefinition<string?> ConfigDir = new()
    {
        Name = "--config-dir",
        Description = "Configuration directory (default: 'config' next to the executable, or $FLARESYNC_CONFIG_DIR).",
        Recursive = true,
    };

    public static readonly OptionDefinition<bool> Verbose = new()
    {
        Name = "--verbose",
        Aliases = { "-v" },
        Description = "Show detailed log output.",
        Recursive = true,
    };
}
