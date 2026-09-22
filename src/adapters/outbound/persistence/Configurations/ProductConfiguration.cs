using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <summary>
    /// Named once and read from here by the repository too. The mark is a shadow property, so
    /// no compiler check stands between the two places that spell it, and a typo would go on
    /// compiling while the takedown silently stopped filtering.
    /// </summary>
    internal const string DeletedAt = "deleted_at";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // The check constraints are the barrier under the optimistic token, not a duplicate of
        // it: the token guards writes that go through the adapter, these guard everything else.
        // ADR-002 wants them to fail loudly, so nothing catches them.
        //
        // Money itself admits an amount of zero, so the only guard on the price is
        // Product.ChangePrice, which creation also goes through. A price written by any other
        // route -- psql, a migration, a future service -- meets no resistance without this.
        builder.ToTable("product", table =>
        {
            table.HasCheckConstraint("ck_product_stock_non_negative", "stock >= 0");
            table.HasCheckConstraint("ck_product_price_positive", "price > 0");
        });
        builder.HasKey(product => product.Id);

        // xmin is the engine's own row version, exposed as a shadow property. Postgres bumps it
        // on every update, so no write path can forget to: a column kept by the application
        // would go silently dead the day one migration or one new adapter skipped it (ADR-002).
        // It is a system column, so it costs no schema surface either.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(product => product.Id).HasColumnName("id");
        builder.Property(product => product.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(product => product.Stock).HasColumnName("stock").IsRequired();
        builder.Property(product => product.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(product => product.ImageKey).HasColumnName("image_key").HasMaxLength(512);

        // Money persists as the amount alone; the currency is single system-wide
        // (Money.DefaultCurrency). Multi-currency would add a column, not change the domain.
        builder.Property(product => product.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(18,2)")
            .HasConversion(price => price.Amount, amount => new Money(amount, Money.DefaultCurrency))
            .IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(product => product.Name);
        builder.HasIndex(product => product.CategoryId);

        // ADR-003. The mark of the takedown is persistence, not domain: Product gains nothing,
        // and a date rather than a boolean both answers *when* and serves as the predicate of
        // the partial indexes T-13 adds. Nullable is what makes that predicate cheap; a
        // sentinel value in a NOT NULL column would index two effective values instead of one.
        builder.Property<DateTimeOffset?>(DeletedAt).HasColumnName(DeletedAt);

        // The filter is global so that the safe thing is what happens by default and the
        // exception has to be written by hand. The inverse -- filtering in every query and
        // trusting nobody forgets -- is exactly how a withdrawn product gets sold: the load
        // that matters is not the catalogue's, it is the one that precedes a sale.
        builder.HasQueryFilter(product => EF.Property<DateTimeOffset?>(product, DeletedAt) == null);
    }
}
