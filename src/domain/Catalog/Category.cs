using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Domain.Catalog;

public sealed class Category : Entity<Guid>
{
    public string Name { get; private set; } = null!;

    private Category() { }

    public Category(Guid id, string name) : base(id) => Rename(name);

    public static Category Create(string name) => new(Guid.NewGuid(), name);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la categoría es obligatorio.");
        Name = name.Trim();
    }
}
