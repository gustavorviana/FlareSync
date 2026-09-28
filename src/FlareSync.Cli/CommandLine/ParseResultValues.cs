using System.CommandLine;
using FlareSync.Core.Commands;

namespace FlareSync.Cli.CommandLine;

/// <summary><see cref="IParsedValues"/> over a System.CommandLine <see cref="ParseResult"/>.</summary>
internal sealed class ParseResultValues(ParseResult parseResult, IReadOnlyDictionary<object, Symbol> symbols) : IParsedValues
{
    public T? Get<T>(ArgumentDefinition<T> argument)
        => parseResult.GetValue((Argument<T>)Lookup(argument, argument.Name));

    public T? Get<T>(OptionDefinition<T> option)
        => parseResult.GetValue((Option<T>)Lookup(option, option.Name));

    public bool IsSpecified(OptionDefinition option)
        => symbols.TryGetValue(option, out var symbol)
            && parseResult.GetResult((Option)symbol) is { Implicit: false };

    private Symbol Lookup(object definition, string name)
        => symbols.TryGetValue(definition, out var symbol)
            ? symbol
            : throw new InvalidOperationException($"'{name}' is not defined on the invoked command.");
}
