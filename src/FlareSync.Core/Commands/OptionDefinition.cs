namespace FlareSync.Core.Commands;

/// <summary>Named option of a command, e.g. <c>--ttl</c>.</summary>
public abstract class OptionDefinition
{
    /// <summary>Option name including its prefix, e.g. <c>--ttl</c>.</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    public IList<string> Aliases { get; } = [];

    public bool Required { get; init; }

    /// <summary>When <c>true</c> the option is also available on every subcommand.</summary>
    public bool Recursive { get; init; }

    public abstract Type ValueType { get; }

    public abstract bool HasDefaultValue { get; }

    public abstract TResult Accept<TResult>(ICommandDefinitionVisitor<TResult> visitor);

    public override string ToString() => Name;
}

public sealed class OptionDefinition<T> : OptionDefinition
{
    private readonly T? _defaultValue;
    private readonly bool _hasDefaultValue;

    public T? DefaultValue
    {
        get => _defaultValue;
        init
        {
            _defaultValue = value;
            _hasDefaultValue = true;
        }
    }

    public override bool HasDefaultValue => _hasDefaultValue;

    /// <summary>Restricts the accepted raw values (case-insensitive).</summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>Returns an error message for an invalid value, or <c>null</c> when valid.</summary>
    public Func<T, string?>? Validator { get; init; }

    public override Type ValueType => typeof(T);

    public override TResult Accept<TResult>(ICommandDefinitionVisitor<TResult> visitor) => visitor.VisitOption(this);
}
