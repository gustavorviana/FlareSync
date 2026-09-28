namespace FlareSync.Core.Commands;

/// <summary>
/// Double dispatch over typed arguments/options, letting adapters create generic symbols without reflection.
/// </summary>
public interface ICommandDefinitionVisitor<out TResult>
{
    TResult VisitArgument<T>(ArgumentDefinition<T> argument);

    TResult VisitOption<T>(OptionDefinition<T> option);
}
