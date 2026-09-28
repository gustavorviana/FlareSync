namespace FlareSync.Core.Config;

/// <summary>Locations of every file inside the configuration directory.</summary>
public sealed class ConfigPaths
{
    public const string EnvironmentVariable = "FLARESYNC_CONFIG_DIR";

    public ConfigPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string GlobalFile => Path.Combine(Root, "flaresync.json");

    public string StateFile => Path.Combine(Root, "state.json");

    public string SecretKeyFile => Path.Combine(Root, "secret.key");

    public string ProvidersDirectory => Path.Combine(Root, "providers");

    public string ProviderFile(string providerName) => Path.Combine(ProvidersDirectory, providerName + ".json");

    /// <summary>
    /// Resolves the configuration directory: explicit value, then <c>FLARESYNC_CONFIG_DIR</c>,
    /// then <c>config/</c> next to the executable.
    /// </summary>
    public static ConfigPaths Resolve(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return new ConfigPaths(explicitDirectory);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new ConfigPaths(fromEnvironment);
        }

        return new ConfigPaths(Path.Combine(AppContext.BaseDirectory, "config"));
    }
}
