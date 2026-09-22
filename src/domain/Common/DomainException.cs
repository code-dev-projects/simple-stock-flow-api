namespace SimpleStockFlow.Domain.Common;

/// <summary>A broken domain invariant. The REST adapter translates it into a 422.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    /// <summary>
    /// Used when an adapter recognises an engine failure as a broken invariant. The cause
    /// stays in the chain for the logs; only the message reaches the caller.
    /// </summary>
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
