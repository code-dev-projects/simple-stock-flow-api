using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimpleStockFlow.Domain.Catalog;

namespace SimpleStockFlow.Adapters.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        // "Not blank" rather than "not empty": the aggregate trims before storing, so a name of
        // spaces alone is precisely the value NOT NULL and a length test would let through.
        builder.ToTable("category", table =>
            table.HasCheckConstraint("ck_category_name_not_blank", "btrim(name) <> ''"));
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.Name).HasColumnName("name").HasMaxLength(120).IsRequired();

        builder.HasIndex(category => category.Name).IsUnique();

        Seed(builder);
    }

    /// <summary>
    /// Categories are reference data, not sample data: the category of a product is mandatory
    /// and no port can create one, so an empty table makes the product CRUD impossible to use.
    /// The identifiers are literal so fixtures can point at a known category.
    /// </summary>
    private static void Seed(EntityTypeBuilder<Category> builder) =>
        builder.HasData(
            new Category(Guid.Parse("11111111-1111-4111-8111-111111111111"), "General"),
            new Category(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Herramientas"),
            new Category(Guid.Parse("33333333-3333-4333-8333-333333333333"), "Electricidad"),
            new Category(Guid.Parse("44444444-4444-4444-8444-444444444444"), "Fontanería"),
            new Category(Guid.Parse("55555555-5555-4555-8555-555555555555"), "Pinturas"));
}
