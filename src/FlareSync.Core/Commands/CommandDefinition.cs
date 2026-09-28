namespace FlareSync.Core.Commands;

/// <summary>
/// Library-agnostic description of a command. Translated into a concrete command-line library by the CLI host.
/// </summary>
public sealed class CommandDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public IList<string> Aliases { get; } = [];

    public IList<ArgumentDefinition> Arguments { get; } = [];

    public IList<OptionDefinition> Options { get; } = [];

    public IList<CommandDefinition> Subcommands { get; } = [];

    /// <summary>Command handler returning the process exit code. <c>null</c> for pure command groups.</summary>
    public Func<CommandContext, Task<int>>? Handler { get; init; }

    public override string ToString() => Name;
}
