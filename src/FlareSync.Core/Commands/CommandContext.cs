namespace FlareSync.Core.Commands;

/// <summary>Everything a command handler needs at invocation time.</summary>
public sealed class CommandContext
{
    public required IParsedValues Values { get; init; }

    public required ICommandConsole Console { get; init; }

    public required IServiceProvider Services { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public T? Get<T>(ArgumentDefinition<T> argument) => Values.Get(argument);

    public T? Get<T>(OptionDefinition<T> option) => Values.Get(option);
}

/// <summary>Typed access to parsed argument and option values.</summary>
public interface IParsedValues
{
    T? Get<T>(ArgumentDefinition<T> argument);

    T? Get<T>(OptionDefinition<T> option);

    /// <summary>Whether the option was explicitly given on the command line.</summary>
    bool IsSpecified(OptionDefinition option);
}

/// <summary>Terminal input/output used by command handlers.</summary>
public interface ICommandConsole
{
    /// <summary>Whether a user can answer prompts (stdin is not redirected).</summary>
    bool IsInteractive { get; }

    void WriteLine(string text = "");

    void WriteError(string text);

    void WriteTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows);

    string? ReadLine(string prompt);

    /// <summary>Reads a line without echoing it.</summary>
    string? ReadSecret(string prompt);

    bool Confirm(string prompt, bool defaultValue);
}
