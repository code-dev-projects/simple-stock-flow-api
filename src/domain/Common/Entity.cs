namespace SimpleStockFlow.Domain.Common;

public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity() { }
    protected Entity(TId id) => Id = id;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && Id.Equals(other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
