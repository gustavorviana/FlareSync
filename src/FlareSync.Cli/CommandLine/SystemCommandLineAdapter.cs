using System.CommandLine;
using FlareSync.Core;
using FlareSync.Core.Commands;

namespace FlareSync.Cli.CommandLine;

/// <summary>
/// Translates the neutral <see cref="CommandCatalog"/> into System.CommandLine symbols and runs the handlers.
/// This is the only place that knows about System.CommandLine.
/// </summary>
public sealed class SystemCommandLineAdapter(
    CommandCatalog catalog,
    Func<IParsedValues, IServiceProvider> servicesFactory,
    ICommandConsole console,
    Func<IParsedValues, bool>? isVerbose = null)
{
    public const int CancelledExitCode = 130;

    public RootCommand Build()
    {
        var root = new RootCommand(catalog.Description);
        var inherited = new Dictionary<object, Symbol>(ReferenceEqualityComparer.Instance);

        foreach (var definition in catalog.GlobalOptions)
        {
            var option = (Option)definition.Accept(SymbolFactory.Instance);
            root.Options.Add(option);
            inherited[definition] = option;
        }

        foreach (var definition in catalog.Commands)
        {
            root.Subcommands.Add(BuildCommand(definition, inherited));
        }

        return root;
    }

    public Task<int> InvokeAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        => Build().Parse(args).InvokeAsync(
            new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(10) },
            cancellationToken);

    private Command BuildCommand(CommandDefinition definition, IReadOnlyDictionary<object, Symbol> inherited)
    {
        var command = new Command(definition.Name, definition.Description);
        foreach (var alias in definition.Aliases)
        {
            command.Aliases.Add(alias);
        }

        var symbols = new Dictionary<object, Symbol>(inherited, ReferenceEqualityComparer.Instance);
        var forChildren = new Dictionary<object, Symbol>(inherited, ReferenceEqualityComparer.Instance);

        foreach (var argumentDefinition in definition.Arguments)
        {
            var argument = (Argument)argumentDefinition.Accept(SymbolFactory.Instance);
            command.Arguments.Add(argument);
            symbols[argumentDefinition] = argument;
        }

        foreach (var optionDefinition in definition.Options)
        {
            var option = (Option)optionDefinition.Accept(SymbolFactory.Instance);
            command.Options.Add(option);
            symbols[optionDefinition] = option;
            if (optionDefinition.Recursive)
            {
                forChildren[optionDefinition] = option;
            }
        }

        foreach (var sub in definition.Subcommands)
        {
            command.Subcommands.Add(BuildCommand(sub, forChildren));
        }

        if (definition.Handler is { } handler)
        {
            command.SetAction((parseResult, cancellationToken) =>
                RunHandlerAsync(handler, new ParseResultValues(parseResult, symbols), cancellationToken));
        }

        return command;
    }

    private async Task<int> RunHandlerAsync(Func<CommandContext, Task<int>> handler, IParsedValues values, CancellationToken cancellationToken)
    {
        IServiceProvider? services = null;
        try
        {
            services = servicesFactory(values);
            return await handler(new CommandContext
            {
                Values = values,
                Console = console,
                Services = services,
                CancellationToken = cancellationToken,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            console.WriteError("Cancelled.");
            return CancelledExitCode;
        }
        catch (FlareSyncException ex)
        {
            console.WriteError("Error: " + ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            console.WriteError("Network error: " + (ex.InnerException?.Message ?? ex.Message));
            return 1;
        }
        catch (Exception ex)
        {
            console.WriteError(isVerbose?.Invoke(values) == true ? ex.ToString() : $"Unexpected error: {ex.Message}");
            return 1;
        }
        finally
        {
            switch (services)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    /// <summary>Creates typed System.CommandLine symbols from neutral definitions.</summary>
    private sealed class SymbolFactory : ICommandDefinitionVisitor<Symbol>
    {
        public static readonly SymbolFactory Instance = new();

        public Symbol VisitArgument<T>(ArgumentDefinition<T> definition)
        {
            var argument = new Argument<T>(definition.Name) { Description = definition.Description };

            if (!definition.Required || definition.HasDefaultValue)
            {
                argument.Arity = typeof(T).IsArray ? ArgumentArity.ZeroOrMore : ArgumentArity.ZeroOrOne;
            }

            if (definition.HasDefaultValue)
            {
                var defaultValue = definition.DefaultValue;
                argument.DefaultValueFactory = _ => defaultValue!;
            }

            if (definition.AllowedValues is { Count: > 0 } allowed)
            {
                argument.Validators.Add(result =>
                {
                    foreach (var token in result.Tokens.Where(t => !allowed.Contains(t.Value, StringComparer.OrdinalIgnoreCase)))
                    {
                        result.AddError($"Invalid value '{token.Value}' for {definition.Name}. Allowed: {string.Join(", ", allowed)}.");
                    }
                });
            }

            if (definition.Validator is { } validator)
            {
                argument.Validators.Add(result =>
                {
                    // Only user-supplied values are validated; defaults are trusted.
                    if (result.Tokens.Count > 0 && TryGet(() => result.GetValueOrDefault<T>(), out var value) && validator(value!) is { } error)
                    {
                        result.AddError(error);
                    }
                });
            }

            return argument;
        }

        public Symbol VisitOption<T>(OptionDefinition<T> definition)
        {
            var option = new Option<T>(definition.Name, [.. definition.Aliases])
            {
                Description = definition.Description,
                Required = definition.Required,
                Recursive = definition.Recursive,
            };

            if (definition.HasDefaultValue)
            {
                var defaultValue = definition.DefaultValue;
                option.DefaultValueFactory = _ => defaultValue!;
            }

            if (definition.AllowedValues is { Count: > 0 } allowed)
            {
                option.AcceptOnlyFromAmong([.. allowed]);
            }

            if (definition.Validator is { } validator)
            {
                option.Validators.Add(result =>
                {
                    if (!result.Implicit && result.Tokens.Count > 0 && TryGet(() => result.GetValueOrDefault<T>(), out var value) && validator(value!) is { } error)
                    {
                        result.AddError(error);
                    }
                });
            }

            return option;
        }

        private static bool TryGet<T>(Func<T> getter, out T? value)
        {
            try
            {
                value = getter();
                return true;
            }
            catch (InvalidOperationException)
            {
                // Conversion errors are already reported by the parser.
                value = default;
                return false;
            }
        }
    }
}
