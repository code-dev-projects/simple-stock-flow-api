using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Adapters.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sale");
        builder.HasKey(sale => sale.Id);

        builder.Property(sale => sale.Id).HasColumnName("id");
        builder.Property(sale => sale.SoldAt).HasColumnName("sold_at").IsRequired();
        // Renamed with the identifier arriving beside it: with both fields present "sold_by"
        // no longer says which of the two it is (T-12). Type and nullability do not move.
        builder.Property(sale => sale.SoldByUsername)
            .HasColumnName("sold_by_username").HasMaxLength(120).IsRequired();

        // No index on purpose: plan.md §3.3 rules it out, no access pattern reads by author, and
        // DP-02 closed the report to any breakdown by seller.
        builder.Property(sale => sale.SoldByUserId).HasColumnName("sold_by_user_id").IsRequired();

        // FK-4, RESTRICT: the authorship of an accounting record must never be left orphaned, and
        // deleting a user must never delete the sales they registered.
        builder.HasOne<SimpleStockFlow.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(sale => sale.SoldByUserId)
            .HasConstraintName("FK_sale_user_sold_by_user_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(sale => sale.Total);

        // IsRequired is what makes sale_id NOT NULL. Without it EF leaves the shadow foreign key
        // nullable, which contradicts the cascade configured right here -- a line has no life of
        // its own outside its sale -- and leaves the composite unique index on sale_item unable
        // to protect anything, because two NULLs never collide.
        builder.HasMany(sale => sale.Items)
            .WithOne()
            .HasForeignKey("sale_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // The aggregate exposes IReadOnlyCollection; EF writes through the _items field.
        builder.Navigation(sale => sale.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        // The date-range report always filters by this column.
        builder.HasIndex(sale => sale.SoldAt);
    }
}
