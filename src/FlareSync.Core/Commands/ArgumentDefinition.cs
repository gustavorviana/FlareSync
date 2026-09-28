namespace FlareSync.Core.Commands;

/// <summary>Positional argument of a command.</summary>
public abstract class ArgumentDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>When <c>false</c> the argument may be omitted.</summary>
    public bool Required { get; init; } = true;

    public abstract Type ValueType { get; }

    public abstract bool HasDefaultValue { get; }

    public abstract TResult Accept<TResult>(ICommandDefinitionVisitor<TResult> visitor);

    public override string ToString() => $"<{Name}>";
}

public sealed class ArgumentDefinition<T> : ArgumentDefinition
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

    public override TResult Accept<TResult>(ICommandDefinitionVisitor<TResult> visitor) => visitor.VisitArgument(this);
}
