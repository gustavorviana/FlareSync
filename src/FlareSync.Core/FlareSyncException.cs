namespace FlareSync.Core;

/// <summary>
/// An expected error whose message is meant for the user (printed without a stack trace).
/// </summary>
public class FlareSyncException : Exception
{
    public FlareSyncException(string message)
        : base(message)
    {
    }

    public FlareSyncException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
