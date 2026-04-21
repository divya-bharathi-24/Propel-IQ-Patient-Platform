using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="CredentialSetupToken"/> entity.
/// Table: <c>credential_setup_tokens</c>
/// Key design decisions:
///   - Only the SHA-256 hash of the raw token is stored (NFR-008).
///   - Unique index on <c>token_hash</c> guarantees O(1) lookup and prevents collision.
///   - Index on <c>user_id</c> supports efficient invalidation of pending tokens.
///   - FK to <c>users</c> uses ON DELETE CASCADE to clean up orphaned tokens.
/// </summary>
public sealed class CredentialSetupTokenConfiguration
    : IEntityTypeConfiguration<CredentialSetupToken>
{
    public void Configure(EntityTypeBuilder<CredentialSetupToken> builder)
    {
        builder.ToTable("credential_setup_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.UserId)
               .IsRequired();

        // SHA-256 hex = 64 characters; raw token never stored (NFR-008)
        builder.Property(t => t.TokenHash)
               .HasMaxLength(64)
               .IsRequired();

        builder.Property(t => t.ExpiresAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        builder.Property(t => t.UsedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(t => t.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()")
               .IsRequired();

        // Unique index: prevents duplicate token hashes and enables O(1) lookup
        builder.HasIndex(t => t.TokenHash)
               .IsUnique()
               .HasDatabaseName("ix_credential_setup_tokens_token_hash");

        // Index for efficient user-scoped invalidation on resend
        builder.HasIndex(t => t.UserId)
               .HasDatabaseName("ix_credential_setup_tokens_user_id");

        // FK: cascade delete orphaned tokens when a user is deleted
        builder.HasOne(t => t.User)
               .WithMany(u => u.CredentialSetupTokens)
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
