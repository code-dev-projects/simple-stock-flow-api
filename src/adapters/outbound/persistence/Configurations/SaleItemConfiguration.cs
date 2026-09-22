using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.Persistence.Configurations;

internal sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_item", table =>
            table.HasCheckConstraint("ck_sale_item_quantity_positive", "quantity > 0"));
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id).HasColumnName("id");

        // Declared here, and not left to the relationship in SaleConfiguration, so the composite
        // index below can name it whichever of the two configurations EF applies first.
        builder.Property<Guid>("sale_id");
        builder.Property(item => item.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(item => item.ProductName).HasColumnName("product_name").HasMaxLength(200).IsRequired();

        // 120 is the width of category.name, and the two must move together: widening one without
        // the other would truncate the label AT THE SALE, and the history cannot be repaired after.
        builder.Property(item => item.CategoryName).HasColumnName("category_name").HasMaxLength(120).IsRequired();

        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .HasConversion(quantity => quantity.Value, value => new Quantity(value))
            .IsRequired();

        builder.Property(item => item.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(18,2)")
            .HasConversion(price => price.Amount, amount => new Money(amount, Money.DefaultCurrency))
            .IsRequired();

        builder.Ignore(item => item.Subtotal);

        // The last-resort barrier ADR-003 already argues from: with logical deletion this never
        // fires, and it exists so that a manual DELETE or a future change of code fails loudly
        // instead of orphaning a sale line and corrupting the report.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => item.ProductId);

        // Uniqueness and read path in one object: a product cannot repeat inside a sale, and with
        // the two amounts carried along the report's aggregation is served by the index without
        // touching the table. It also makes the plain index on sale_id redundant, so EF stops
        // creating it and the migration drops it.
        builder.HasIndex("sale_id", nameof(SaleItem.ProductId))
            .IsUnique()
            .IncludeProperties(nameof(SaleItem.Quantity), nameof(SaleItem.UnitPrice));
    }
}
