namespace SimpleStockFlow.Domain.Common;

/// <summary>The only transactional entry point into the object graph.</summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    protected AggregateRoot() { }
    protected AggregateRoot(TId id) : base(id) { }
}
