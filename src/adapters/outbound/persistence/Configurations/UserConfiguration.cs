using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // The normalization check mirrors User.NormalizeUsername, Trim() then ToLowerInvariant(),
        // and both halves are needed: testing the lower case alone would admit ' ana ', which the
        // aggregate stores as 'ana', and the engine and the code would disagree about the name of
        // the same account.
        builder.ToTable("user", table =>
        {
            table.HasCheckConstraint("ck_user_role_allowed", "role IN ('admin', 'seller')");
            table.HasCheckConstraint("ck_user_username_normalized", "username = lower(btrim(username))");
        });
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.Username).HasColumnName("username").HasMaxLength(120).IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
        builder.Property(user => user.Role).HasColumnName("role").HasMaxLength(40).IsRequired();

        builder.HasIndex(user => user.Username).IsUnique();
    }
}
