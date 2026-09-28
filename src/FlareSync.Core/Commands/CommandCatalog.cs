namespace FlareSync.Core.Commands;

/// <summary>
/// Central registry receiving the root commands of the core and of every provider module.
/// </summary>
public sealed class CommandCatalog
{
    private readonly List<CommandDefinition> _commands = [];
    private readonly List<OptionDefinition> _globalOptions = [];

    public string Description { get; init; } = "FlareSync - dynamic DNS client.";

    public IReadOnlyList<CommandDefinition> Commands => _commands;

    /// <summary>Options available on every command (e.g. <c>--config-dir</c>).</summary>
    public IReadOnlyList<OptionDefinition> GlobalOptions => _globalOptions;

    public CommandCatalog Add(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        return this;
    }

    public CommandCatalog AddGlobalOption(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _globalOptions.Add(option);
        return this;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> listing every problem found in the catalog.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        CheckNames(_commands, "root", errors);
        CheckOptionNames(_globalOptions, "global options", errors);

        foreach (var command in _commands)
        {
            ValidateCommand(command, command.Name, errors);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid command catalog:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
        }
    }

    private void ValidateCommand(CommandDefinition command, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.StartsWith('-') || command.Name.Contains(' '))
        {
            errors.Add($"'{path}': invalid command name '{command.Name}'.");
        }

        if (command.Handler is null && command.Subcommands.Count == 0)
        {
            errors.Add($"'{path}': a command without subcommands must have a handler.");
        }

        var optionalSeen = false;
        foreach (var argument in command.Arguments)
        {
            var optional = !argument.Required || argument.HasDefaultValue;
            if (!optional && optionalSeen)
            {
                errors.Add($"'{path}': required argument '{argument.Name}' follows an optional argument.");
            }

            optionalSeen |= optional;
        }

        CheckOptionNames(_globalOptions.Concat(command.Options), $"'{path}'", errors);
        CheckNames(command.Subcommands, path, errors);

        foreach (var sub in command.Subcommands)
        {
            ValidateCommand(sub, $"{path} {sub.Name}", errors);
        }
    }

    private static void CheckNames(IEnumerable<CommandDefinition> commands, string scope, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            foreach (var name in command.Aliases.Prepend(command.Name))
            {
                if (!seen.Add(name))
                {
                    errors.Add($"'{scope}': duplicate command name or alias '{name}'.");
                }
            }
        }
    }

    private static void CheckOptionNames(IEnumerable<OptionDefinition> options, string scope, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            foreach (var name in option.Aliases.Prepend(option.Name))
            {
                if (!name.StartsWith('-'))
                {
                    errors.Add($"{scope}: option '{name}' must start with '-'.");
                }

                if (!seen.Add(name))
                {
                    errors.Add($"{scope}: duplicate option name or alias '{name}'.");
                }
            }
        }
    }
}
