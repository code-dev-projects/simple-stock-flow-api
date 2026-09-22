namespace SimpleStockFlow.Application.Common;

/// <summary>
/// Another writer changed the row between the read and the commit. This is not a broken
/// invariant: the operation was valid and simply lost the race, which is why it does not
/// derive from DomainException and never becomes a 422.
///
/// It is also the application-side face of the ORM's own concurrency exception, which
/// ADR-002 forbids from leaving the persistence adapter.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message) { }

    public ConcurrencyConflictException(string message, Exception inner) : base(message, inner) { }
}
